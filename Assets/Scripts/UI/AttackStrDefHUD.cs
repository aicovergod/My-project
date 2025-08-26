using UnityEngine;
using UnityEngine.UI;
using Skills;

namespace UI
{
    /// <summary>
    /// Simple HUD displaying current Attack, Strength and Defence levels and XP values.
    /// Mirrors the style used for other skill displays in the project.
    /// </summary>
    public class AttackStrDefHUD : MonoBehaviour
    {
        [SerializeField] private SkillManager skills;
        private Text text;

        private void Awake()
        {
            if (skills == null)
                skills = FindObjectOfType<SkillManager>();
            CreateUI();
        }

        private void CreateUI()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            gameObject.AddComponent<CanvasScaler>();
            gameObject.AddComponent<GraphicRaycaster>();

            var panel = new GameObject("Panel");
            panel.transform.SetParent(transform, false);
            var image = panel.AddComponent<Image>();
            image.color = new Color(0f, 0f, 0f, 0.5f);
            var panelRect = panel.GetComponent<RectTransform>();
            panelRect.sizeDelta = new Vector2(160f, 60f);
            panelRect.anchorMin = new Vector2(0f, 1f);
            panelRect.anchorMax = new Vector2(0f, 1f);
            panelRect.pivot = new Vector2(0f, 1f);
            panelRect.anchoredPosition = new Vector2(10f, -10f);

            var textGO = new GameObject("Text");
            textGO.transform.SetParent(panel.transform, false);
            text = textGO.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.alignment = TextAnchor.UpperLeft;
            text.color = Color.white;
            var rect = text.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(4f, 4f);
            rect.offsetMax = new Vector2(-4f, -4f);
        }

        private void Update()
        {
            if (skills == null || text == null)
                return;

            text.text =
                $"Attack: {skills.GetLevel(SkillType.Attack)}  XP: {skills.GetXp(SkillType.Attack):F1}\n" +
                $"Strength: {skills.GetLevel(SkillType.Strength)}  XP: {skills.GetXp(SkillType.Strength):F1}\n" +
                $"Defence: {skills.GetLevel(SkillType.Defence)}  XP: {skills.GetXp(SkillType.Defence):F1}";
        }
    }
}
