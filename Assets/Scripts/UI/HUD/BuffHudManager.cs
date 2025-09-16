using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Status;
using World;

namespace UI.HUD
{
    /// <summary>
    /// Manages buff infobox UI anchored next to the minimap.
    /// </summary>
    [DisallowMultipleComponent]
    public class BuffHudManager : MonoBehaviour
    {
        [SerializeField] private BuffInfoBox infoBoxPrefab;
        [SerializeField] private Vector2 anchoredOffset = new Vector2(-8f, -140f);
        [SerializeField] private float verticalSpacing = 4f;
        [SerializeField] private BuffType[] ordering;
        [SerializeField] private bool playExpiryNotification = true;
        [SerializeField] private AudioClip expiryClip;

        private readonly Dictionary<BuffKey, BuffInfoBox> activeBoxes = new();

        private RectTransform container;
        private RectTransform anchor;
        private GameObject player;
        private AudioSource audioSource;

        private void Awake()
        {
            if (infoBoxPrefab == null)
            {
                var loaded = Resources.Load<BuffInfoBox>("UI/Status/BuffInfoBox");
                if (loaded == null)
                {
                    var prefabGO = Resources.Load<GameObject>("UI/Status/BuffInfoBox");
                    if (prefabGO != null)
                        loaded = prefabGO.GetComponent<BuffInfoBox>();
                }
                infoBoxPrefab = loaded;
            }

            if (expiryClip != null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
                audioSource.playOnAwake = false;
            }

        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += HandleSceneLoaded;
            SubscribeToService();
            TryInitialiseContainer();
            RefreshPlayerReference();
            RebuildExistingBuffs();
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            UnsubscribeFromService();
            ClearBoxes();
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            RefreshPlayerReference();
            TryInitialiseContainer();
            RebuildExistingBuffs();
        }

        private void LateUpdate()
        {
            if (container == null || anchor == null)
                TryInitialiseContainer();
            if (player == null)
                RefreshPlayerReference();
        }

        private void SubscribeToService()
        {
            if (BuffTimerService.Instance == null)
                return;

            BuffTimerService.Instance.BuffStarted += HandleBuffStarted;
            BuffTimerService.Instance.BuffUpdated += HandleBuffUpdated;
            BuffTimerService.Instance.BuffWarning += HandleBuffWarning;
            BuffTimerService.Instance.BuffEnded += HandleBuffEnded;
        }

        private void UnsubscribeFromService()
        {
            if (BuffTimerService.Instance == null)
                return;

            BuffTimerService.Instance.BuffStarted -= HandleBuffStarted;
            BuffTimerService.Instance.BuffUpdated -= HandleBuffUpdated;
            BuffTimerService.Instance.BuffWarning -= HandleBuffWarning;
            BuffTimerService.Instance.BuffEnded -= HandleBuffEnded;
        }

        private void HandleBuffStarted(BuffTimerInstance instance)
        {
            if (!IsPlayerBuff(instance))
                return;

            EnsureContainer();
            CreateOrUpdateBox(instance);
        }

        private void HandleBuffUpdated(BuffTimerInstance instance)
        {
            if (!IsPlayerBuff(instance))
                return;

            if (activeBoxes.TryGetValue(instance.Key, out var box))
            {
                box.UpdateTimer(instance);
                box.ResetVisuals();
            }
            else
            {
                CreateOrUpdateBox(instance);
            }
        }

        private void HandleBuffWarning(BuffTimerInstance instance)
        {
            if (!IsPlayerBuff(instance))
                return;

            if (activeBoxes.TryGetValue(instance.Key, out var box))
            {
                box.SetWarning(true);
                if (playExpiryNotification && expiryClip != null)
                    audioSource?.PlayOneShot(expiryClip);
            }
        }

        private void HandleBuffEnded(BuffTimerInstance instance, BuffEndReason reason)
        {
            if (!activeBoxes.TryGetValue(instance.Key, out var box))
                return;

            activeBoxes.Remove(instance.Key);
            if (box != null)
                Destroy(box.gameObject);
            LayoutBoxes();
        }

        private void CreateOrUpdateBox(BuffTimerInstance instance)
        {
            if (container == null || infoBoxPrefab == null)
                return;

            if (!activeBoxes.TryGetValue(instance.Key, out var box) || box == null)
            {
                box = Instantiate(infoBoxPrefab, container);
                activeBoxes[instance.Key] = box;
            }

            box.Bind(instance);
            LayoutBoxes();
        }

        private void LayoutBoxes()
        {
            if (container == null)
                return;

            var ordered = new List<BuffInfoBox>(activeBoxes.Values);
            ordered.Sort(CompareBoxes);

            float currentY = 0f;
            for (int i = 0; i < ordered.Count; i++)
            {
                var box = ordered[i];
                if (box == null)
                    continue;
                var rect = box.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(1f, 1f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(1f, 1f);
                rect.anchoredPosition = new Vector2(0f, -currentY);
                currentY += rect.rect.height + verticalSpacing;
            }
        }

        private int CompareBoxes(BuffInfoBox a, BuffInfoBox b)
        {
            if (a == null && b == null)
                return 0;
            if (a == null)
                return 1;
            if (b == null)
                return -1;

            int orderA = GetOrderIndex(a.BoundBuff);
            int orderB = GetOrderIndex(b.BoundBuff);
            if (orderA != orderB)
                return orderA.CompareTo(orderB);
            return a.BoundBuff.SequenceId.CompareTo(b.BoundBuff.SequenceId);
        }

        private int GetOrderIndex(BuffTimerInstance instance)
        {
            if (ordering == null || ordering.Length == 0)
                return (int)instance.Definition.type;
            for (int i = 0; i < ordering.Length; i++)
            {
                if (ordering[i] == instance.Definition.type)
                    return i;
            }
            return ordering.Length + (int)instance.Definition.type;
        }

        private bool IsPlayerBuff(BuffTimerInstance instance)
        {
            if (instance.Target == null)
                return false;
            if (player == null)
                RefreshPlayerReference();
            return instance.Target == player || instance.Target.CompareTag("Player");
        }

        private void RefreshPlayerReference()
        {
            if (player != null)
                return;
            player = GameObject.FindGameObjectWithTag("Player");
        }

        private void TryInitialiseContainer()
        {
            if (container != null && anchor != null)
                return;

            var minimap = Minimap.Instance;
            if (minimap == null)
                return;

            anchor = minimap.BorderRect != null ? minimap.BorderRect : minimap.SmallRootRect;
            if (anchor == null)
                return;

            EnsureContainer();
        }

        private void EnsureContainer()
        {
            if (anchor == null)
                return;

            if (container == null)
            {
                var go = new GameObject("BuffHud", typeof(RectTransform));
                container = go.GetComponent<RectTransform>();
                container.SetParent(anchor, false);
                container.anchorMin = new Vector2(1f, 1f);
                container.anchorMax = new Vector2(1f, 1f);
                container.pivot = new Vector2(1f, 1f);
            }

            container.anchoredPosition = anchoredOffset;
        }

        private void RebuildExistingBuffs()
        {
            ClearBoxes();
            if (BuffTimerService.Instance == null)
                return;

            foreach (var pair in BuffTimerService.Instance.ActiveBuffs)
            {
                var instance = pair.Value;
                if (!IsPlayerBuff(instance))
                    continue;
                CreateOrUpdateBox(instance);
            }
        }

        private void ClearBoxes()
        {
            foreach (var entry in activeBoxes.Values)
            {
                if (entry != null)
                    Destroy(entry.gameObject);
            }
            activeBoxes.Clear();
        }
    }
}
