using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UI;
using Util;

namespace NPC
{
    /// <summary>
    /// Simple world-space health bar displayed above an NPC while in combat.
    /// </summary>
    [RequireComponent(typeof(NpcCombatant))]
    public class NpcHealthHUD : MonoBehaviour
    {
        private NpcCombatant combatant;
        private BaseNpcCombat combat;
        private Canvas canvas;
        private CanvasGroup canvasGroup;
        private Image fill;
        private Text text;

        [SerializeField] private float heightOffset = 1.5f;
        [SerializeField] private int sortingOrder = 5000;
        [SerializeField] private float fadeDuration = 0.25f;
        private Coroutine fadeRoutine;
        private bool isVisible;

        private void Awake()
        {
            combatant = GetComponent<NpcCombatant>();
            combat = GetComponent<BaseNpcCombat>();
            combatant.OnHealthChanged += HandleHealthChanged;
            combatant.OnDeath += HandleDeath;
            if (combat != null)
                combat.OnCombatStateChanged += HandleCombatStateChanged;
            CreateHud();
            canvas.gameObject.SetActive(false);
            bool inCombat = combat != null && combat.InCombat;
            HandleCombatStateChanged(inCombat);
            isVisible = inCombat;
        }

        private void CreateHud()
        {
            var go = new GameObject("NpcHealthHUD", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.overrideSorting = true; // Force the HUD to obey the explicit sorting order so it can render above world geometry.
            canvas.sortingLayerID = SortingLayer.NameToID("Default");
            canvas.sortingOrder = sortingOrder;
            canvas.transform.SetParent(transform, false);
            canvas.transform.localPosition = new Vector3(0f, heightOffset, 0f);
            canvas.transform.localRotation = Quaternion.identity;
            canvas.GetComponent<RectTransform>().sizeDelta = new Vector2(1f, 0.15f);
            canvasGroup = go.AddComponent<CanvasGroup>();
            canvasGroup.alpha = 0f;

            var sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0f,0f,1f,1f), new Vector2(0.5f,0.5f));

            var bg = new GameObject("BG", typeof(Image));
            bg.transform.SetParent(canvas.transform, false);
            var bgImg = bg.GetComponent<Image>();
            bgImg.color = Color.red;
            bgImg.sprite = sprite;
            bgImg.raycastTarget = false; // Prevent the HUD background from intercepting UI raycasts.
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
            fill.raycastTarget = false; // Allow player UI input to pass through the fill graphic.
            var fillRect = fill.rectTransform;
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;

            var textGO = new GameObject("Text", typeof(Text));
            textGO.transform.SetParent(bg.transform, false);
            text = textGO.GetComponent<Text>();
            LegacyFontProvider.ApplyTo(text);
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.fontSize = 11;
            text.raycastTarget = false; // Ensure the text does not block other UI.
            var textRect = text.rectTransform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            LayerUtility.SetLayerRecursively(go.transform, LayerMask.NameToLayer("UI"));
        }

        private void HandleHealthChanged(int current, int max)
        {
            if (fill != null)
                fill.fillAmount = max > 0 ? (float)current / max : 0f;
            if (text != null)
                text.text = $"{current}/{max}";
            if (current <= 0)
            {
                HandleDeath();
            }
        }

        private void HandleCombatStateChanged(bool inCombat)
        {
            if (canvas == null || canvasGroup == null)
                return;
            if (fadeRoutine != null)
                StopCoroutine(fadeRoutine);
            if (inCombat)
            {
                canvas.gameObject.SetActive(true);
                fadeRoutine = StartCoroutine(FadeCanvas(1f));
            }
            else
            {
                fadeRoutine = StartCoroutine(FadeCanvas(0f));
            }
        }

        private IEnumerator FadeCanvas(float target)
        {
            float start = canvasGroup.alpha;
            float t = 0f;
            while (t < fadeDuration)
            {
                t += Time.unscaledDeltaTime;
                canvasGroup.alpha = Mathf.Lerp(start, target, t / fadeDuration);
                yield return null;
            }
            canvasGroup.alpha = target;
            if (Mathf.Approximately(target, 0f))
                canvas.gameObject.SetActive(false);
            fadeRoutine = null;
        }

        private void HandleDeath()
        {
            if (fadeRoutine != null)
                StopCoroutine(fadeRoutine);
            if (canvas != null)
            {
                canvas.gameObject.SetActive(false);
                if (canvasGroup != null)
                    canvasGroup.alpha = 0f;
            }
            isVisible = false;
        }

        private void OnDestroy()
        {
            if (combatant != null)
            {
                combatant.OnHealthChanged -= HandleHealthChanged;
                combatant.OnDeath -= HandleDeath;
            }
            if (combat != null)
                combat.OnCombatStateChanged -= HandleCombatStateChanged;
        }

        private void Update()
        {
            bool inCombat = combat != null && combat.InCombat;
            if (inCombat != isVisible)
            {
                HandleCombatStateChanged(inCombat);
                isVisible = inCombat;
            }
        }
    }
}
