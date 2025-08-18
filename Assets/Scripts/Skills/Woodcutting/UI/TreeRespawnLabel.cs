using TMPro;
using UnityEngine;
using Util;

namespace Skills.Woodcutting
{
    /// <summary>
    /// Displays a world-space countdown above a depleted tree until it respawns.
    /// </summary>
    public class TreeRespawnLabel : MonoBehaviour, ITickable
    {
        [SerializeField] private Vector3 worldOffset = new Vector3(0f, 0.6f, 0f);
        [SerializeField] private Color textColor = Color.white;
        [SerializeField] private int fontSize = 32;
        [SerializeField] private TMP_FontAsset font;
        [SerializeField] private Color outlineColor = Color.black;
        [SerializeField] private float outlineWidth = 0f;

        private TreeNode tree;
        private TextMeshPro tmp;
        private Transform labelTransform;
        private SpriteRenderer treeRenderer;
        private double endTime;
        private bool counting;
        private int lastSeconds = -1;

        private void Awake()
        {
            tree = GetComponent<TreeNode>();
            if (tree == null)
                tree = GetComponentInParent<TreeNode>();
            treeRenderer = tree != null ? tree.GetComponent<SpriteRenderer>() : null;
            CreateLabel();
        }

        private void OnEnable()
        {
            if (tree != null)
            {
                tree.OnTreeDepleted += HandleDepleted;
                tree.OnTreeRespawned += HandleRespawned;
            }
            if (Ticker.Instance != null)
                Ticker.Instance.Subscribe(this);
        }

        private void Start()
        {
            if (Ticker.Instance != null)
                Ticker.Instance.Subscribe(this);
        }

        private void OnDisable()
        {
            if (Ticker.Instance != null)
                Ticker.Instance.Unsubscribe(this);
            if (tree != null)
            {
                tree.OnTreeDepleted -= HandleDepleted;
                tree.OnTreeRespawned -= HandleRespawned;
            }
        }

        private void CreateLabel()
        {
            if (tree == null || labelTransform != null)
                return;

            // Reuse an existing label if one was already created for this tree.
            var existing = tree.transform.Find("TreeRespawnLabel");
            if (existing != null)
            {
                labelTransform = existing;
                tmp = existing.GetComponent<TextMeshPro>();
                if (tmp == null)
                    tmp = existing.gameObject.AddComponent<TextMeshPro>();
            }
            else
            {
                var go = new GameObject("TreeRespawnLabel");
                labelTransform = go.transform;
                labelTransform.SetParent(tree.transform, false);

                tmp = go.AddComponent<TextMeshPro>();
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.color = textColor;
                tmp.fontSize = fontSize;
                tmp.enableWordWrapping = false;
                if (font != null) tmp.font = font;
                if (outlineWidth > 0f)
                {
                    tmp.outlineWidth = outlineWidth;
                    tmp.outlineColor = outlineColor;
                }
            }

            labelTransform.gameObject.SetActive(false);
        }

        private void HandleDepleted(TreeNode node, float respawnSeconds)
        {
            endTime = Time.timeAsDouble + respawnSeconds;
            counting = true;
            lastSeconds = -1;

            // Ensure the label exists before trying to show it.
            if (labelTransform == null)
                CreateLabel();

            if (labelTransform != null)
                labelTransform.gameObject.SetActive(true);

            int secs = Mathf.CeilToInt(respawnSeconds);
            lastSeconds = secs;
            if (tmp != null)
                tmp.SetText("{0}", secs);
        }

        private void HandleRespawned(TreeNode node)
        {
            counting = false;
            if (labelTransform != null)
                labelTransform.gameObject.SetActive(false);
        }

        public void OnTick()
        {
            if (!counting)
                return;

            double remaining = endTime - Time.timeAsDouble;
            int secs = remaining > 0 ? Mathf.CeilToInt((float)remaining) : 0;
            if (secs <= 0)
            {
                counting = false;
                if (labelTransform != null)
                    labelTransform.gameObject.SetActive(false);
                return;
            }

            if (secs != lastSeconds)
            {
                lastSeconds = secs;
                tmp.SetText("{0}", secs);
            }

            bool visible = treeRenderer == null || treeRenderer.isVisible;
            if (labelTransform != null)
            {
                if (labelTransform.gameObject.activeSelf != visible)
                    labelTransform.gameObject.SetActive(visible);
                if (visible)
                {
                    labelTransform.position = tree.transform.position + worldOffset;
                    if (Camera.main != null)
                        labelTransform.rotation = Camera.main.transform.rotation;
                }
            }
        }
    }
}
