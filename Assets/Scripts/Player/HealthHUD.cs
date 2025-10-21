using System;
using UnityEngine;
using UnityEngine.UI;
using UI;

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

        /// <summary>Raised whenever a health HUD instance finishes its awake cycle.</summary>
        public static event Action<HealthHUD> HealthHudCreated;

        /// <summary>Raised when the active health HUD is destroyed so dependants can rebuild.</summary>
        public static event Action HealthHudDestroyed;

        /// <summary>Provides global access to the active health HUD instance.</summary>
        public static HealthHUD Instance { get; private set; }

        public static HealthHUD CreateUnderMinimap(RectTransform minimapRoot, PlayerHitpoints hp)
        {
            if (minimapRoot == null || hp == null)
                return null;

            var parent = minimapRoot.parent as RectTransform;
            var go = new GameObject("HealthHUD", typeof(RectTransform), typeof(HealthHUD));
            var hud = go.GetComponent<HealthHUD>();
            hud.hitpoints = hp;
            go.transform.SetParent(parent, false);

            var sprite = Sprite.Create(Texture2D.whiteTexture,
                new Rect(0f, 0f, 1f, 1f),
                new Vector2(0.5f, 0.5f));

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.sizeDelta = new Vector2(300f, 30f);
            rect.anchoredPosition = new Vector2(-10f, -314f);

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
            LegacyFontProvider.ApplyTo(hud.text);
            hud.text.alignment = TextAnchor.MiddleCenter;
            hud.text.color = Color.white;
            hud.text.fontSize = 28;
            // Add an outline to give the health text a crisp black border similar to OSRS UI treatment.
            var outline = textGO.AddComponent<Outline>();
            outline.effectColor = Color.black;
            outline.effectDistance = new Vector2(1f, -1f);
            outline.useGraphicAlpha = false;
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

        private void Awake()
        {
            // Publish the instance immediately so late subscribers (pet HUD, companion HUD, etc.)
            // can anchor to the health bar as soon as it is created.
            Instance = this;
            HealthHudCreated?.Invoke(this);
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

            // Clear the static instance and alert listeners so they can queue a rebuild once the
            // minimap recreates the HUD in the new scene.
            if (Instance == this)
            {
                Instance = null;
                HealthHudDestroyed?.Invoke();
            }
        }
    }
}
