using UnityEngine;
using UnityEngine.UI;
using Inventory;
using Player;
using UI;

namespace Skills
{
    /// <summary>
    /// Displays basic skill information such as mining and woodcutting levels and XP
    /// and can be toggled with the 'O' key.
    /// </summary>
    public class SkillsUI : MonoBehaviour, IUIWindow
    {
        private GameObject uiRoot;
        private Text skillText;
        private Mining.MiningSkill miningSkill;
        private Woodcutting.WoodcuttingSkill woodcuttingSkill;
        private Fishing.FishingSkill fishingSkill;
        private PlayerHitpoints hitpoints;
        private SkillManager skillManager;

        public static SkillsUI Instance { get; private set; }

        public bool IsOpen => uiRoot != null && uiRoot.activeSelf;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateInstance()
        {
            if (Instance != null || UnityEngine.Object.FindObjectOfType<SkillsUI>() != null)
                return;

            var go = new GameObject("SkillsUI");
            DontDestroyOnLoad(go);
            go.AddComponent<SkillsUI>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            miningSkill = FindObjectOfType<Mining.MiningSkill>();
            woodcuttingSkill = FindObjectOfType<Woodcutting.WoodcuttingSkill>();
            fishingSkill = FindObjectOfType<Fishing.FishingSkill>();
            hitpoints = FindObjectOfType<PlayerHitpoints>();
            skillManager = FindObjectOfType<SkillManager>();
            CreateUI();
            if (uiRoot != null)
                uiRoot.SetActive(false);
            UIManager.Instance.RegisterWindow(this);
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        private void CreateUI()
        {
            uiRoot = new GameObject("SkillsUIRoot");
            uiRoot.transform.SetParent(transform, false);

            var canvas = uiRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            uiRoot.AddComponent<CanvasScaler>();
            uiRoot.AddComponent<GraphicRaycaster>();

            var panel = new GameObject("Panel");
            panel.transform.SetParent(uiRoot.transform, false);
            var panelImage = panel.AddComponent<Image>();
            panelImage.color = new Color(0f, 0f, 0f, 0.5f);
            var panelRect = panel.GetComponent<RectTransform>();
            panelRect.sizeDelta = new Vector2(200f, 200f);
            panelRect.anchoredPosition = Vector2.zero;

            var textGo = new GameObject("SkillText");
            textGo.transform.SetParent(panel.transform, false);
            skillText = textGo.AddComponent<Text>();
            skillText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            skillText.alignment = TextAnchor.MiddleCenter;
            skillText.color = Color.white;
            var textRect = skillText.rectTransform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
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
            {
                var inv = Object.FindObjectOfType<Inventory.Inventory>();
                if (inv != null && inv.IsOpen)
                    inv.CloseUI();
                var eq = Object.FindObjectOfType<Inventory.Equipment>();
                if (eq != null && eq.IsOpen)
                    eq.CloseUI();
                uiRoot.SetActive(true);
            }
        }

        private void Update()
        {
            // Removed O key toggle

            if (uiRoot != null && uiRoot.activeSelf)
            {
                string text = "";
                if (hitpoints != null)
                    text += $"Hitpoints Level: {hitpoints.Level}  XP: {hitpoints.Xp:F2}";
                if (skillManager != null)
                {
                    if (text.Length > 0)
                        text += "\n";
                    text += $"Attack Level: {skillManager.GetLevel(SkillType.Attack)}  XP: {skillManager.GetXp(SkillType.Attack):F2}";
                    text += "\n";
                    text += $"Strength Level: {skillManager.GetLevel(SkillType.Strength)}  XP: {skillManager.GetXp(SkillType.Strength):F2}";
                    text += "\n";
                    text += $"Defence Level: {skillManager.GetLevel(SkillType.Defence)}  XP: {skillManager.GetXp(SkillType.Defence):F2}";
                    text += "\n";
                    text += $"Beastmaster Level: {skillManager.GetLevel(SkillType.Beastmaster)}  XP: {skillManager.GetXp(SkillType.Beastmaster):F2}";
                }
                if (miningSkill != null)
                {
                    if (text.Length > 0)
                        text += "\n";
                    text += $"Mining Level: {miningSkill.Level}  XP: {miningSkill.Xp}";
                }
                if (woodcuttingSkill != null)
                {
                    if (text.Length > 0)
                        text += "\n";
                    text += $"Woodcutting Level: {woodcuttingSkill.Level}  XP: {woodcuttingSkill.Xp}";
                }
                if (fishingSkill != null)
                {
                    if (text.Length > 0)
                        text += "\n";
                    text += $"Fishing Level: {fishingSkill.Level}  XP: {fishingSkill.Xp}";
                }
                skillText.text = text;
            }
        }

        public void Close()
        {
            if (uiRoot != null)
                uiRoot.SetActive(false);
        }
    }
}
