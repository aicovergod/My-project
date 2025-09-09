using UnityEngine;
using UnityEngine.UI;
using Combat;
using Player;

namespace UI
{
    /// <summary>
    /// Simple spellbook interface allowing the player to select a spell.
    /// </summary>
    public class MagicUI : MonoBehaviour, IUIWindow
    {
        private GameObject uiRoot;
        private PlayerCombatLoadout loadout;
        private Button windStrikeButton;

        public enum Spell { WindStrike }

        /// <summary>Currently selected spell.</summary>
        public static Spell ActiveSpell { get; private set; } = Spell.WindStrike;

        /// <summary>Maximum hit for the active spell.</summary>
        public static int ActiveSpellMaxHit => ActiveSpell switch { Spell.WindStrike => 2, _ => 0 };

        public bool IsOpen => uiRoot != null && uiRoot.activeSelf;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Init()
        {
            if (FindObjectOfType<MagicUI>() != null)
                return;

            var go = new GameObject("MagicUI");
            DontDestroyOnLoad(go);
            go.AddComponent<MagicUI>();
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
            uiRoot = new GameObject("MagicUIRoot");
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
            panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.anchoredPosition = new Vector2(295f, -75f);

            var layout = panel.GetComponent<VerticalLayoutGroup>();
            layout.spacing = -25f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = false;

            windStrikeButton = CreateSpellButton(panel.transform, "WindStrike", Spell.WindStrike);
            UpdateSelection();
        }

        private Button CreateSpellButton(Transform parent, string spriteName, Spell spell)
        {
            var sprite = Resources.Load<Sprite>("Interfaces/Magic/" + spriteName);
            var go = new GameObject(spriteName, typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.preserveAspect = true;
            var rect = go.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(30f, 30f);
            rect.localScale = new Vector3(2f, 1f, 1f);
            var btn = go.GetComponent<Button>();
            btn.onClick.AddListener(() => SelectSpell(spell));
            return btn;
        }

        public void Toggle()
        {
            if (IsOpen)
                Close();
            else
                Open();
        }

        public void Open()
        {
            UIManager.Instance.OpenWindow(this);
            if (uiRoot != null)
                uiRoot.SetActive(true);
        }

        public void Close()
        {
            if (uiRoot != null)
                uiRoot.SetActive(false);
        }

        private void SelectSpell(Spell spell)
        {
            ActiveSpell = spell;
            if (loadout == null)
                loadout = FindObjectOfType<PlayerCombatLoadout>();
            loadout?.SetDamageType(DamageType.Magic);
            UpdateSelection();
        }

        private void UpdateSelection()
        {
            Highlight(windStrikeButton, ActiveSpell == Spell.WindStrike);
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

