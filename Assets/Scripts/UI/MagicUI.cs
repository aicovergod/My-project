using UnityEngine;
using UnityEngine.UI;
using Combat;
using Player;
using System;
using System.Collections.Generic;
using Magic;
using Skills;

namespace UI
{
    /// <summary>
    /// Simple spellbook interface allowing the player to select a spell.
    /// </summary>
    public class MagicUI : MonoBehaviour, IUIWindow
    {
        private GameObject uiRoot;
        private PlayerCombatLoadout loadout;
        private readonly Dictionary<SpellDefinition, Button> spellButtons = new();
        private readonly List<SpellDefinition> spells = new();

        /// <summary>Currently selected spell.</summary>
        public static SpellDefinition ActiveSpell { get; private set; }
            = null;

        /// <summary>Most recently selected spell.</summary>
        public static SpellDefinition LastSelectedSpell { get; private set; } = null;

        /// <summary>Maximum hit for the active spell.</summary>
        public static int ActiveSpellMaxHit => ActiveSpell != null ? ActiveSpell.maxHit : 0;

        public static void ClearActiveSpell()
        {
            ActiveSpell = null;
            var instance = FindObjectOfType<MagicUI>();
            instance?.UpdateSelection();
        }

        /// <summary>Range for the active spell or melee range if none.</summary>
        public static float GetActiveSpellRange() =>
            ActiveSpell != null ? ActiveSpell.range : CombatMath.MELEE_RANGE;

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
            LoadSpells();
            CreateUI();
            if (uiRoot != null)
                uiRoot.SetActive(false);
            UIManager.Instance.RegisterWindow(this);
        }

        private void LoadSpells()
        {
            spells.Clear();
            var loaded = Resources.LoadAll<SpellDefinition>("Spells");
            if (loaded != null)
                spells.AddRange(loaded);
            // Don't automatically select a spell on load. This allows melee
            // range to be used at spawn when no magic weapon is equipped.
            if (spells.Count > 0 && LastSelectedSpell == null)
                LastSelectedSpell = spells[0];
        }

        private void CreateUI()
        {
            uiRoot = new GameObject("MagicUIRoot");
            uiRoot.transform.SetParent(transform, false);

            var canvas = uiRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            uiRoot.AddComponent<CanvasScaler>();
            uiRoot.AddComponent<GraphicRaycaster>();

            var panel = new GameObject("Panel", typeof(Image), typeof(GridLayoutGroup));
            panel.transform.SetParent(uiRoot.transform, false);
            var panelImage = panel.GetComponent<Image>();
            panelImage.color = new Color(0f, 0f, 0f, 0.5f);
            var panelRect = panel.GetComponent<RectTransform>();
            var columns = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(spells.Count)));
            var rows = Mathf.Max(1, Mathf.CeilToInt(spells.Count / (float)columns));
            panelRect.sizeDelta = new Vector2(columns * 64f, rows * 64f);
            panelRect.anchorMin = panelRect.anchorMax = new Vector2(1f, 1f);
            panelRect.pivot = new Vector2(1f, 1f);
            panelRect.anchoredPosition = new Vector2(-10f, -10f);

            var layout = panel.GetComponent<GridLayoutGroup>();
            layout.cellSize = new Vector2(64f, 64f);
            layout.spacing = Vector2.zero;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            layout.constraintCount = columns;

            foreach (var spell in spells)
            {
                var btn = CreateSpellButton(panel.transform, spell);
                spellButtons[spell] = btn;
            }

            UpdateSelection();
        }

        private Button CreateSpellButton(Transform parent, SpellDefinition spell)
        {
            var sprite = spell.icon != null
                ? spell.icon
                : Resources.Load<Sprite>("Interfaces/StandardSpellBook/" + spell.name);
            var go = new GameObject(spell.name, typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.preserveAspect = true;
            var rect = go.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(64f, 64f);
            rect.localScale = Vector3.one;
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

        private void SelectSpell(SpellDefinition spell)
        {
            if (loadout == null)
                loadout = FindObjectOfType<PlayerCombatLoadout>();

            // Check for magic level requirement before selecting the spell
            var skills = loadout != null ? loadout.GetComponent<SkillManager>() : null;
            if (skills != null && skills.GetLevel(SkillType.Magic) < spell.requiredMagicLevel)
            {
                var anchor = loadout.transform.Find("FloatingTextAnchor") ?? loadout.transform;
                FloatingText.Show($"You need a Magic level of {spell.requiredMagicLevel} to use this spell", anchor.position);
                return;
            }

            if (ActiveSpell == spell)
            {
                ClearActiveSpell();
                loadout?.SetDamageType(DamageType.Melee);
            }
            else
            {
                ActiveSpell = spell;
                LastSelectedSpell = spell;
                loadout?.SetDamageType(DamageType.Magic);
            }

            UpdateSelection();
        }

        private void UpdateSelection()
        {
            foreach (var pair in spellButtons)
                Highlight(pair.Value, ActiveSpell == pair.Key);
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

        /// <summary>Restore the last selected spell and update UI highlighting.</summary>
        public static void RestoreLastSpell()
        {
            if (LastSelectedSpell == null)
                return;
            ActiveSpell = LastSelectedSpell;
            var instance = FindObjectOfType<MagicUI>();
            instance?.UpdateSelection();
        }
    }
}

