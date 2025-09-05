using UnityEngine;
using UnityEngine.UI;

namespace NPC
{
    /// <summary>
    /// Simple world-space health bar displayed above an NPC while in combat.
    /// </summary>
    [RequireComponent(typeof(NpcCombatant))]
    public class NpcHealthHUD : MonoBehaviour
    {
        private NpcCombatant combatant;
        private Canvas canvas;
        private Image fill;
        private Text text;

        private void Awake()
        {
            combatant = GetComponent<NpcCombatant>();
            combatant.OnHealthChanged += HandleHealthChanged;
            combatant.OnDeath += HandleDeath;
            CreateHud();
            canvas.gameObject.SetActive(false);
        }

        private void CreateHud()
        {
            var go = new GameObject("NpcHealthHUD", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.transform.SetParent(transform, false);
            canvas.transform.localPosition = new Vector3(0f, 1.5f, 0f);
            canvas.transform.localRotation = Quaternion.identity;
            canvas.GetComponent<RectTransform>().sizeDelta = new Vector2(1f, 0.15f);

            var sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0f,0f,1f,1f), new Vector2(0.5f,0.5f));

            var bg = new GameObject("BG", typeof(Image));
            bg.transform.SetParent(canvas.transform, false);
            var bgImg = bg.GetComponent<Image>();
            bgImg.color = Color.red;
            bgImg.sprite = sprite;
            var bgRect = bgImg.rectTransform;
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;

            var fillGO = new GameObject("Fill", typeof(Image));
            fillGO.transform.SetParent(bg.transform, false);
            fill = fillGO.GetComponent<Image>();
            fill.color = Color.green;
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = 0;
            fill.sprite = sprite;
            var fillRect = fill.rectTransform;
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;

            var textGO = new GameObject("Text", typeof(Text));
            textGO.transform.SetParent(bg.transform, false);
            text = textGO.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.fontSize = 11;
            var textRect = text.rectTransform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
        }

        private void HandleHealthChanged(int current, int max)
        {
            if (canvas != null && !canvas.gameObject.activeSelf)
                canvas.gameObject.SetActive(true);
            if (fill != null)
                fill.fillAmount = max > 0 ? (float)current / max : 0f;
            if (text != null)
                text.text = $"{current}/{max}";
        }

        private void HandleDeath()
        {
            if (canvas != null)
                canvas.gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            if (combatant != null)
            {
                combatant.OnHealthChanged -= HandleHealthChanged;
                combatant.OnDeath -= HandleDeath;
            }
        }
    }
}
