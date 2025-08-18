using UnityEngine;
using UnityEngine.UI;

namespace Player
{
    /// <summary>
    /// Runtime created health bar that sits under the minimap and listens for hitpoint changes.
    /// </summary>
    public class HealthHUD : MonoBehaviour
    {
        private PlayerHitpoints hitpoints;
        private Image fillImage;
        private Text text;

        public static HealthHUD CreateUnderMinimap(RectTransform minimapRoot, PlayerHitpoints hp)
        {
            if (minimapRoot == null || hp == null)
                return null;

            var parent = minimapRoot.parent as RectTransform;
            var go = new GameObject("HealthHUD", typeof(RectTransform), typeof(HealthHUD));
            var hud = go.GetComponent<HealthHUD>();
            hud.hitpoints = hp;
            go.transform.SetParent(parent, false);

            var sprite = Resources.GetBuiltinResource<Sprite>("UI/Skin/UISprite.psd");

            const float height = 12f;
            const float margin = 4f;

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.sizeDelta = new Vector2(minimapRoot.sizeDelta.x, height);
            rect.anchoredPosition = minimapRoot.anchoredPosition + new Vector2(0f, -(minimapRoot.sizeDelta.y + margin));

            var bgGO = new GameObject("Background", typeof(Image));
            bgGO.transform.SetParent(go.transform, false);
            var bgImg = bgGO.GetComponent<Image>();
            bgImg.color = Color.red;
            bgImg.sprite = sprite;
            var bgRect = bgImg.rectTransform;
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;

            var fillGO = new GameObject("Fill", typeof(Image));
            fillGO.transform.SetParent(bgGO.transform, false);
            hud.fillImage = fillGO.GetComponent<Image>();
            hud.fillImage.color = Color.green;
            hud.fillImage.type = Image.Type.Filled;
            hud.fillImage.sprite = sprite;
            hud.fillImage.fillMethod = Image.FillMethod.Horizontal;
            hud.fillImage.fillOrigin = 0;
            hud.fillImage.fillAmount = 1f;
            var fillRect = hud.fillImage.rectTransform;
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;

            var textGO = new GameObject("Text", typeof(Text));
            textGO.transform.SetParent(bgGO.transform, false);
            hud.text = textGO.GetComponent<Text>();
            hud.text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            hud.text.alignment = TextAnchor.MiddleCenter;
            hud.text.color = Color.white;
            hud.text.fontSize = 11;
            var textRect = hud.text.rectTransform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            hp.OnHealthChanged += hud.HandleHealthChanged;
            hp.OnHitpointsLevelChanged += hud.HandleLevelChanged;
            hud.HandleHealthChanged(hp.CurrentHp, hp.MaxHp);

            return hud;
        }

        private void HandleHealthChanged(int current, int max)
        {
            if (fillImage != null)
                fillImage.fillAmount = max > 0 ? (float)current / max : 0f;
            if (text != null)
                text.text = $"{current}/{max}";
        }

        private void HandleLevelChanged(int newLevel)
        {
            HandleHealthChanged(hitpoints.CurrentHp, hitpoints.MaxHp);
        }

        private void OnDestroy()
        {
            if (hitpoints != null)
            {
                hitpoints.OnHealthChanged -= HandleHealthChanged;
                hitpoints.OnHitpointsLevelChanged -= HandleLevelChanged;
            }
        }
    }
}
