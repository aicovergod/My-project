using System;
using UnityEngine;
using UnityEngine.UI;

namespace Inventory
{
    /// <summary>
    /// Simple modal dialog that asks the player for an amount when splitting a stack.
    /// Built completely in code so no prefabs are required. The dialog is expected to
    /// be parented under an existing Canvas.
    /// </summary>
    public class StackSplitDialog : MonoBehaviour
    {
        private InputField inputField;
        private Action<int> onConfirm;
        private int maxAmount;

        /// <summary>
        /// Creates and shows the dialog as a child of <paramref name="parent"/>.
        /// </summary>
        public static void Show(Transform parent, int max, Action<int> onConfirm)
        {
            var go = new GameObject("StackSplitDialog", typeof(Image), typeof(StackSplitDialog));
            go.transform.SetParent(parent, false);
            var dialog = go.GetComponent<StackSplitDialog>();
            dialog.onConfirm = onConfirm;
            dialog.maxAmount = Mathf.Max(1, max);
            dialog.BuildUI();
        }

        private void BuildUI()
        {
            var bg = GetComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.8f);

            var rect = GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(160f, 80f);

            // Input field
            var fieldGO = new GameObject("InputField", typeof(Image), typeof(InputField));
            fieldGO.transform.SetParent(transform, false);
            var fieldImage = fieldGO.GetComponent<Image>();
            fieldImage.color = Color.white;
            var fieldRect = fieldGO.GetComponent<RectTransform>();
            fieldRect.anchorMin = new Vector2(0.1f, 0.5f);
            fieldRect.anchorMax = new Vector2(0.9f, 0.8f);
            fieldRect.offsetMin = Vector2.zero;
            fieldRect.offsetMax = Vector2.zero;

            var textGO = new GameObject("Text", typeof(Text));
            textGO.transform.SetParent(fieldGO.transform, false);
            var text = textGO.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.alignment = TextAnchor.MiddleLeft;
            text.text = "1";
            text.color = Color.black;

            var placeholderGO = new GameObject("Placeholder", typeof(Text));
            placeholderGO.transform.SetParent(fieldGO.transform, false);
            var placeholder = placeholderGO.GetComponent<Text>();
            placeholder.font = text.font;
            placeholder.alignment = TextAnchor.MiddleLeft;
            placeholder.text = "1";
            placeholder.color = new Color(0.5f, 0.5f, 0.5f, 0.5f);

            inputField = fieldGO.GetComponent<InputField>();
            inputField.textComponent = text;
            inputField.placeholder = placeholder;
            inputField.contentType = InputField.ContentType.IntegerNumber;
            inputField.text = "1";

            CreateButton("OK", new Vector2(0.1f, 0.1f), new Vector2(0.45f, 0.4f), Confirm);
            CreateButton("Cancel", new Vector2(0.55f, 0.1f), new Vector2(0.9f, 0.4f), () => Destroy(gameObject));
        }

        private void CreateButton(string label, Vector2 anchorMin, Vector2 anchorMax, UnityEngine.Events.UnityAction onClick)
        {
            var btnGO = new GameObject(label, typeof(Image), typeof(Button));
            btnGO.transform.SetParent(transform, false);
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
            txt.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            txt.alignment = TextAnchor.MiddleCenter;
            txt.color = Color.black;
            txt.text = label;

            var btn = btnGO.GetComponent<Button>();
            btn.onClick.AddListener(onClick);
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
    }
}
