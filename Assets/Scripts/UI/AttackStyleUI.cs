using UnityEngine;
using UnityEngine.UI;
using Combat;
using Player;
using World;

namespace UI
{
    /// <summary>
    /// Simple interface for selecting the player's combat style.
    /// </summary>
    public class AttackStyleUI : MonoBehaviour, IUIWindow
    {
        private GameObject uiRoot;
        private PlayerCombatLoadout loadout;
        private Button accurateButton;
        private Button aggressiveButton;
        private Button defensiveButton;
        private Button controlledButton;

        /// <summary>Whether the interface is currently visible.</summary>
        public bool IsOpen => uiRoot != null && uiRoot.activeSelf;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Init()
        {
            if (UnityEngine.Object.FindObjectOfType<AttackStyleUI>() != null)
                return;

            var go = new GameObject("AttackStyleUI");
            // Ensure the UI survives scene loads and is tracked by the transition system so it
            // can be re-parented into the active scene after a door transition.
            go.AddComponent<ScenePersistentObject>();
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<AttackStyleUI>();
        }

        private void Awake()
        {
            loadout = FindObjectOfType<PlayerCombatLoadout>();
            CreateUI();
            if (uiRoot != null)
                uiRoot.SetActive(false);
            UIManager.Instance.RegisterWindow(this);
        }

        private void CreateUI()
        {
            uiRoot = new GameObject("AttackStyleUIRoot");
            uiRoot.transform.SetParent(transform, false);

            var canvas = uiRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            uiRoot.AddComponent<CanvasScaler>();
            uiRoot.AddComponent<GraphicRaycaster>();

            var panel = new GameObject("Panel", typeof(Image), typeof(VerticalLayoutGroup));
            panel.transform.SetParent(uiRoot.transform, false);
            var panelImage = panel.GetComponent<Image>();
            panelImage.color = new Color(0f, 0f, 0f, 0.5f);
            var panelRect = panel.GetComponent<RectTransform>();
            panelRect.sizeDelta = new Vector2(170f, 220f);
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.anchoredPosition = new Vector2(295f, -75f);

            var layout = panel.GetComponent<VerticalLayoutGroup>();
            layout.spacing = -25f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = false;

            accurateButton = CreateStyleButton(panel.transform, "Accurate", CombatStyle.Accurate);
            aggressiveButton = CreateStyleButton(panel.transform, "Aggressive", CombatStyle.Aggressive);
            defensiveButton = CreateStyleButton(panel.transform, "Defensive", CombatStyle.Defensive);
            controlledButton = CreateStyleButton(panel.transform, "Controlled", CombatStyle.Controlled);

            UpdateSelection();
        }

        private Button CreateStyleButton(Transform parent, string spriteName, CombatStyle style)
        {
            var sprite = Resources.Load<Sprite>("Interfaces/AttackStyle/" + spriteName);
            var go = new GameObject(spriteName, typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.preserveAspect = true;
            var rect = go.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(30f, 30f);
            rect.localScale = new Vector3(2f, 1f, 1f);
            var btn = go.GetComponent<Button>();
            btn.onClick.AddListener(() => SetStyle(style));
            return btn;
        }

        /// <summary>Toggle the visibility of the UI.</summary>
        public void Toggle()
        {
            if (IsOpen)
                Close();
            else
                Open();
        }

        /// <summary>Open the attack style interface.</summary>
        public void Open()
        {
            UIManager.Instance.OpenWindow(this);
            if (uiRoot != null)
            {
                uiRoot.SetActive(true);
                UpdateSelection();
            }
        }

        /// <summary>Close the attack style interface.</summary>
        public void Close()
        {
            if (uiRoot != null)
                uiRoot.SetActive(false);
        }

        private void SetStyle(CombatStyle style)
        {
            if (loadout == null)
                loadout = FindObjectOfType<PlayerCombatLoadout>();
            if (loadout != null)
                loadout.Style = style;
            UpdateSelection();
        }

        private void UpdateSelection()
        {
            if (loadout == null)
                loadout = FindObjectOfType<PlayerCombatLoadout>();
            if (loadout == null)
                return;
            Highlight(accurateButton, loadout.Style == CombatStyle.Accurate);
            Highlight(aggressiveButton, loadout.Style == CombatStyle.Aggressive);
            Highlight(defensiveButton, loadout.Style == CombatStyle.Defensive);
            Highlight(controlledButton, loadout.Style == CombatStyle.Controlled);
        }

        private void Highlight(Button btn, bool selected)
        {
            if (btn == null)
                return;
            var colors = btn.colors;
            var color = selected ? Color.green : Color.white;
            colors.normalColor = color;
            colors.highlightedColor = color;
            colors.selectedColor = color;
            colors.pressedColor = color;
            btn.colors = colors;
        }
    }
}

