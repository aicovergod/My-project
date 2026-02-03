using System;
using UnityEngine;
using UnityEngine.UI;
using UI;

namespace Inventory
{
    /// <summary>
    /// Simple modal dialog that asks the player for an amount when splitting a stack.
    /// Built completely in code so no prefabs are required. The dialog is expected to
    /// be parented under an existing Canvas.
    /// </summary>
    public class StackSplitDialog : MonoBehaviour
    {
        private static StackSplitDialog instance;

        private InputField inputField;
        private RectTransform contentRoot;
        private Action<int> onConfirm;
        private int maxAmount;

        /// <summary>
        /// Creates and shows the dialog as a child of <paramref name="parent"/>.
        /// </summary>
        public static void Show(Transform parent, int max, Action<int> onConfirm)
        {
            // Ensure only one dialog exists at a time
            if (instance != null)
                Destroy(instance.gameObject);

            var go = new GameObject(
                "StackSplitDialog",
                typeof(Image),
                typeof(Canvas),
                typeof(GraphicRaycaster),
                typeof(StackSplitDialog));
            go.transform.SetParent(parent, false);
            go.transform.SetAsLastSibling();

            var canvas = go.GetComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = 1000;

            instance = go.GetComponent<StackSplitDialog>();
            instance.onConfirm = onConfirm;
            instance.maxAmount = Mathf.Max(1, max);
            instance.BuildUI();
        }

        private void BuildUI()
        {
            // Configure the full-screen overlay so it captures pointer input and darkens
            // the backdrop, preventing clicks from leaking through to the bank UI.
            var overlayImage = GetComponent<Image>();
            overlayImage.color = new Color(0f, 0f, 0f, 0.75f);
            overlayImage.raycastTarget = true;

            var overlayRect = GetComponent<RectTransform>();
            overlayRect.anchorMin = Vector2.zero;
            overlayRect.anchorMax = Vector2.one;
            overlayRect.offsetMin = Vector2.zero;
            overlayRect.offsetMax = Vector2.zero;
            overlayRect.pivot = new Vector2(0.5f, 0.5f);

            // The actual dialog content lives on a dedicated panel so button hitboxes
            // no longer span the entire overlay. This resolves the issue where clicking
            // the input field triggered a confirm.
            contentRoot = BuildPanel();

            // Input field
            BuildInputField(contentRoot);

            // Action buttons
            CreateButton("Confirm", contentRoot, new Vector2(0.1f, 0.1f), new Vector2(0.45f, 0.35f), Confirm);
            CreateButton("Cancel", contentRoot, new Vector2(0.55f, 0.1f), new Vector2(0.9f, 0.35f), () => Destroy(gameObject));
        }

        /// <summary>
        /// Creates the central dialog panel that contains all interactive controls.
        /// </summary>
        private RectTransform BuildPanel()
        {
            var panelGO = new GameObject("DialogPanel", typeof(Image));
            panelGO.transform.SetParent(transform, false);

            var panelImage = panelGO.GetComponent<Image>();
            panelImage.color = new Color(0.13f, 0.13f, 0.13f, 0.95f);

            var panelRect = panelGO.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(260f, 140f);

            return panelRect;
        }

        /// <summary>
        /// Builds and configures the numeric input field where the player enters an amount.
        /// </summary>
        private void BuildInputField(RectTransform parent)
        {
            var fieldGO = new GameObject("InputField", typeof(Image), typeof(InputField));
            fieldGO.transform.SetParent(parent, false);

            var fieldImage = fieldGO.GetComponent<Image>();
            fieldImage.color = Color.white;

            var fieldRect = fieldGO.GetComponent<RectTransform>();
            fieldRect.anchorMin = new Vector2(0.1f, 0.55f);
            fieldRect.anchorMax = new Vector2(0.9f, 0.85f);
            fieldRect.offsetMin = Vector2.zero;
            fieldRect.offsetMax = Vector2.zero;

            var textGO = new GameObject("Text", typeof(Text));
            textGO.transform.SetParent(fieldGO.transform, false);
            var text = textGO.GetComponent<Text>();
            LegacyFontProvider.ApplyTo(text);
            text.alignment = TextAnchor.MiddleLeft;
            text.text = "1";
            text.color = Color.black;

            var placeholderGO = new GameObject("Placeholder", typeof(Text));
            placeholderGO.transform.SetParent(fieldGO.transform, false);
            var placeholder = placeholderGO.GetComponent<Text>();
            placeholder.font = text.font;
            placeholder.alignment = TextAnchor.MiddleLeft;
            placeholder.text = "Amount";
            placeholder.color = new Color(0.5f, 0.5f, 0.5f, 0.75f);

            inputField = fieldGO.GetComponent<InputField>();
            inputField.textComponent = text;
            inputField.placeholder = placeholder;
            inputField.contentType = InputField.ContentType.IntegerNumber;
            inputField.text = "1";
            inputField.caretWidth = 1;

            // Disable automatic navigation so pointer clicks do not trigger implicit
            // submit events when using the new Input System module.
            var navigation = new Navigation { mode = Navigation.Mode.None };
            inputField.navigation = navigation;

            inputField.Select();
            inputField.ActivateInputField();
        }

        /// <summary>
        /// Creates a legacy-styled button and attaches the supplied click handler.
        /// </summary>
        private void CreateButton(string label, RectTransform parent, Vector2 anchorMin, Vector2 anchorMax, UnityEngine.Events.UnityAction onClick)
        {
            var btnGO = new GameObject(label, typeof(Image), typeof(Button));
            btnGO.transform.SetParent(parent, false);
            var rect = btnGO.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            var img = btnGO.GetComponent<Image>();
            img.color = Color.white;

            var txtGO = new GameObject("Text", typeof(Text));
            txtGO.transform.SetParent(btnGO.transform, false);
            var txt = txtGO.GetComponent<Text>();
            LegacyFontProvider.ApplyTo(txt);
            txt.alignment = TextAnchor.MiddleCenter;
            txt.color = Color.black;
            txt.text = label;

            var btn = btnGO.GetComponent<Button>();
            btn.onClick.AddListener(onClick);
            btn.navigation = new Navigation { mode = Navigation.Mode.None };
        }

        private void Confirm()
        {
            int value;
            if (!int.TryParse(inputField.text, out value))
                value = 1;
            value = Mathf.Clamp(value, 1, maxAmount);
            onConfirm?.Invoke(value);
            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (instance == this)
                instance = null;
        }
    }
}
