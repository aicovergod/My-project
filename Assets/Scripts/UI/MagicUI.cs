using UnityEngine;
using UnityEngine.UI;
using Combat;
using Player;
using System;
using System.Collections.Generic;
using Magic;
using Skills;
using World;

namespace UI
{
    /// <summary>
    /// Simple spellbook interface allowing the player to select a spell.
    /// </summary>
    public class MagicUI : MonoBehaviour, IUIWindow
    {
        private static MagicUI instance;
        public static MagicUI Instance => instance;

        private GameObject uiRoot;
        private PlayerCombatLoadout loadout;
        private readonly Dictionary<SpellDefinition, Button> spellButtons = new();
        private readonly List<SpellDefinition> spells = new();

        // Strike spell references cached for max hit adjustments
        private readonly List<SpellDefinition> strikeSpells = new();
        // Runtime map preserving each strike's original max hit so ScriptableObjects are never mutated.
        private readonly Dictionary<SpellDefinition, int> strikeOriginalMaxHits = new();
        // Runtime map storing the boosted max hit values currently applied to each strike spell.
        private readonly Dictionary<SpellDefinition, int> strikeRuntimeMaxHits = new();

        /// <summary>Currently selected spell.</summary>
        public static SpellDefinition ActiveSpell { get; private set; }
            = null;

        /// <summary>Most recently selected spell.</summary>
        public static SpellDefinition LastSelectedSpell { get; private set; } = null;

        /// <summary>Maximum hit for the active spell.</summary>
        public static int ActiveSpellMaxHit
        {
            get
            {
                if (ActiveSpell == null)
                    return 0;

                var ui = Instance ?? FindObjectOfType<MagicUI>();
                return ui != null
                    ? ui.GetRuntimeMaxHit(ActiveSpell)
                    : ActiveSpell.maxHit;
            }
        }

        public static void ClearActiveSpell()
        {
            ActiveSpell = null;
            var ui = Instance ?? FindObjectOfType<MagicUI>();
            ui?.UpdateSelection();
        }

        /// <summary>Range for the active spell or melee range if none.</summary>
        public static float GetActiveSpellRange() =>
            ActiveSpell != null ? ActiveSpell.range : CombatMath.MELEE_RANGE;

        public bool IsOpen => uiRoot != null && uiRoot.activeSelf;

        /// <summary>
        /// Updates strike spell max hits based on the given magic level.
        /// </summary>
        public static void UpdateStrikeMaxHits(int level)
        {
            var ui = Instance ?? FindObjectOfType<MagicUI>();
            ui?.ApplyStrikeMaxHits(level);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            PersistentSceneSingleton<MagicUI>.Bootstrap(CreateSingleton);
        }

        private static MagicUI CreateSingleton()
        {
            var go = new GameObject(nameof(MagicUI));
            return go.AddComponent<MagicUI>();
        }

        private void Awake()
        {
            if (!PersistentSceneSingleton<MagicUI>.HandleAwake(this))
                return;

            loadout = FindObjectOfType<PlayerCombatLoadout>();
            LoadSpells();
            CacheStrikeSpells();
            CreateUI();
            EnsureUiRootPersistence();
            if (uiRoot != null)
                uiRoot.SetActive(false);
            UIManager.Instance?.RegisterWindow(this);
        }

        private void OnEnable()
        {
            EnsureUiRootPersistence();
            RebindLoadout();
        }

        private void LoadSpells()
        {
            spells.Clear();
            var loaded = Resources.LoadAll<SpellDefinition>("Spells");
            if (loaded != null)
                spells.AddRange(loaded);
            spells.Sort((a, b) => a.loadOrder.CompareTo(b.loadOrder));
            // Don't automatically select a spell on load. This allows melee
            // range to be used at spawn when no magic weapon is equipped.
            if (spells.Count > 0 && LastSelectedSpell == null)
                LastSelectedSpell = spells[0];
        }

        private void CacheStrikeSpells()
        {
            strikeSpells.Clear();
            strikeOriginalMaxHits.Clear();
            strikeRuntimeMaxHits.Clear();
            string[] names = { "WindStrike", "WaterStrike", "EarthStrike", "ElectricStrike", "FireStrike" };
            foreach (var name in names)
            {
                var spell = spells.Find(s => s.name == name);
                if (spell != null)
                {
                    strikeSpells.Add(spell);
                    strikeOriginalMaxHits[spell] = spell.maxHit;
                    strikeRuntimeMaxHits[spell] = spell.maxHit;
                }
            }
        }

        private void CreateUI()
        {
            uiRoot = new GameObject("MagicUIRoot");
            uiRoot.transform.SetParent(transform, false);

            var canvas = uiRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = uiRoot.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1024f, 768f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
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

        private void ApplyStrikeMaxHits(int level)
        {
            if (strikeSpells.Count == 0)
                return;

            int highest = 0;
            foreach (var spell in strikeSpells)
            {
                var baseMaxHit = strikeOriginalMaxHits.TryGetValue(spell, out var original)
                    ? original
                    : spell.maxHit;
                if (spell.requiredMagicLevel <= level && baseMaxHit > highest)
                    highest = baseMaxHit;
            }

            foreach (var spell in strikeSpells)
            {
                var baseMaxHit = strikeOriginalMaxHits.TryGetValue(spell, out var original)
                    ? original
                    : spell.maxHit;
                if (spell.requiredMagicLevel <= level && highest > 0)
                    strikeRuntimeMaxHits[spell] = highest;
                else
                    strikeRuntimeMaxHits[spell] = baseMaxHit;
            }
        }

        private int GetRuntimeMaxHit(SpellDefinition spell)
        {
            if (spell == null)
                return 0;

            if (strikeRuntimeMaxHits.TryGetValue(spell, out var strikeMaxHit))
                return strikeMaxHit;

            return spell.maxHit;
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

        private void OnDestroy()
        {
            if (!PersistentSceneSingleton<MagicUI>.HandleOnDestroy(this))
                return;

            UIManager.Instance?.UnregisterWindow(this);
            TearDownUiRoot();
        }

        private void EnsureUiRootPersistence()
        {
            if (uiRoot == null)
                return;

            if (uiRoot.scene.name != "DontDestroyOnLoad")
                DontDestroyOnLoad(uiRoot);
        }

        private void TearDownUiRoot()
        {
            if (uiRoot != null)
            {
                uiRoot.SetActive(false);
                Destroy(uiRoot);
                uiRoot = null;
            }
            spellButtons.Clear();
        }

        private void RebindLoadout()
        {
            if (loadout == null)
                loadout = FindObjectOfType<PlayerCombatLoadout>();
        }

        private void SelectSpell(SpellDefinition spell)
        {
            if (loadout == null)
                loadout = FindObjectOfType<PlayerCombatLoadout>();

            // Check for magic level requirement before selecting the spell
            var skills = loadout != null ? loadout.GetComponent<SkillManager>() : null;
            if (skills != null && skills.GetLevel(SkillType.Magic) < spell.requiredMagicLevel)
            {
                // Determine a safe anchor for floating text feedback. Prefer the player's
                // dedicated floating text anchor if available, otherwise fall back to the
                // loadout transform, and ultimately use this UI transform when no loadout is present.
                Transform anchor = transform;
                if (loadout != null)
                {
                    var loadoutTransform = loadout.transform;
                    anchor = loadoutTransform.Find("FloatingTextAnchor") ?? loadoutTransform;
                }

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

