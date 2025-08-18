using TMPro;
using UnityEngine;

namespace Skills.Woodcutting
{
    /// <summary>
    /// (Deprecated) Previously displayed a world-space countdown above a depleted tree.
    /// Countdown functionality has been removed, leaving the label hidden.
    /// </summary>
    public class TreeRespawnLabel : MonoBehaviour
    {
        [SerializeField] private Color textColor = Color.white;
        [SerializeField] private int fontSize = 32;
        [SerializeField] private TMP_FontAsset font;
        [SerializeField] private Color outlineColor = Color.black;
        [SerializeField] private float outlineWidth = 0f;

        private TreeNode tree;
        // Using TMP_Text allows the label to work with either TextMeshPro or TextMeshProUGUI
        // components. This avoids cases where GetComponent<TextMeshPro>() fails to find the
        // existing text element, leaving the countdown blank.
        private TMP_Text tmp;
        private Transform labelTransform;

        private void Awake()
        {
            tree = GetComponent<TreeNode>();
            if (tree == null)
                tree = GetComponentInParent<TreeNode>();
            CreateLabel();
        }

        private void OnEnable()
        {
            if (tree != null)
            {
                tree.OnTreeDepleted += HandleDepleted;
                tree.OnTreeRespawned += HandleRespawned;
            }
        }

        private void OnDisable()
        {
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
                tmp = existing.GetComponent<TMP_Text>();
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
            if (labelTransform != null)
                labelTransform.gameObject.SetActive(false);
        }

        private void HandleRespawned(TreeNode node)
        {
            if (labelTransform != null)
                labelTransform.gameObject.SetActive(false);
        }
    }
}
