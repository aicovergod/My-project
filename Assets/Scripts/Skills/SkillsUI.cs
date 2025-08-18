using UnityEngine;
using UnityEngine.UI;
using ShopSystem;
using Inventory;

namespace Skills
{
    /// <summary>
    /// Displays basic skill information such as mining and woodcutting levels and XP
    /// and can be toggled with the 'O' key.
    /// </summary>
    public class SkillsUI : MonoBehaviour
    {
        private GameObject uiRoot;
        private Text skillText;
        private Mining.MiningSkill miningSkill;
        private Woodcutting.WoodcuttingSkill woodcuttingSkill;

        public static SkillsUI Instance { get; private set; }

        public bool IsOpen => uiRoot != null && uiRoot.activeSelf;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateInstance()
        {
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
            CreateUI();
            if (uiRoot != null)
                uiRoot.SetActive(false);
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
            panelRect.sizeDelta = new Vector2(200f, 100f);
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

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.O))
            {
                var shop = ShopUI.Instance;
                if (shop != null && shop.IsOpen)
                    return;
                var bank = BankSystem.BankUI.Instance;
                if (bank != null && bank.IsOpen)
                    return;

                if (uiRoot != null)
                {
                    bool opening = !uiRoot.activeSelf;
                    if (opening)
                    {
                        var inv = Object.FindObjectOfType<Inventory.Inventory>();
                        if (inv != null && inv.IsOpen)
                            inv.CloseUI();
                        var eq = Object.FindObjectOfType<Inventory.Equipment>();
                        if (eq != null && eq.IsOpen)
                            eq.CloseUI();
                    }
                    uiRoot.SetActive(!uiRoot.activeSelf);
                }
            }

            if (uiRoot != null && uiRoot.activeSelf)
            {
                string text = "";
                if (miningSkill != null)
                    text += $"Mining Level: {miningSkill.Level}  XP: {miningSkill.Xp}";
                if (woodcuttingSkill != null)
                {
                    if (text.Length > 0)
                        text += "\n";
                    text += $"Woodcutting Level: {woodcuttingSkill.Level}  XP: {woodcuttingSkill.Xp}";
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
