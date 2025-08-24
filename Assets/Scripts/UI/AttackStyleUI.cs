using UnityEngine;
using UnityEngine.UI;
using Combat;
using Player;

namespace UI
{
    /// <summary>
    /// Simple interface for selecting the player's combat style.
    /// </summary>
    public class AttackStyleUI : MonoBehaviour
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
            var go = new GameObject("AttackStyleUI");
            DontDestroyOnLoad(go);
            go.AddComponent<AttackStyleUI>();
        }

        private void Awake()
        {
            loadout = FindObjectOfType<PlayerCombatLoadout>();
            CreateUI();
            if (uiRoot != null)
                uiRoot.SetActive(false);
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
            layout.spacing = 5f;
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
            rect.localScale = new Vector3(2.5f, 1.5f, 1f);
            var btn = go.GetComponent<Button>();
            btn.onClick.AddListener(() => SetStyle(style));
            return btn;
        }

        /// <summary>Toggle the visibility of the UI.</summary>
        public void Toggle()
        {
            if (uiRoot != null)
            {
                bool opening = !uiRoot.activeSelf;
                uiRoot.SetActive(opening);
                if (opening)
                    UpdateSelection();
            }
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
            colors.normalColor = selected ? Color.green : Color.white;
            colors.highlightedColor = selected ? Color.green : Color.white;
            btn.colors = colors;
        }
    }
}

