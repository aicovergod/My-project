using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace UI.Chat
{
    /// <summary>
    /// Utility component that renders a sequence of <see cref="EmojiMarkupToken"/> instances using pooled Text/Image children.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class EmojiTokenLayout : MonoBehaviour
    {
        [SerializeField] private float spacing = 2f;

        private readonly List<Component> activeComponents = new List<Component>();
        private readonly Queue<Text> textPool = new Queue<Text>();
        private readonly Queue<Image> imagePool = new Queue<Image>();

        private HorizontalLayoutGroup layoutGroup;

        private void Awake()
        {
            layoutGroup = GetComponent<HorizontalLayoutGroup>();
            if (layoutGroup == null)
            {
                layoutGroup = gameObject.AddComponent<HorizontalLayoutGroup>();
                layoutGroup.childControlWidth = true;
                layoutGroup.childControlHeight = true;
                layoutGroup.childForceExpandWidth = false;
                layoutGroup.childForceExpandHeight = false;
                layoutGroup.childAlignment = TextAnchor.UpperLeft;
            }

            layoutGroup.spacing = spacing;

            var fitter = GetComponent<ContentSizeFitter>();
            if (fitter == null)
            {
                fitter = gameObject.AddComponent<ContentSizeFitter>();
                fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
                fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            }
        }

        /// <summary>
        /// Renders the supplied token list.
        /// </summary>
        /// <param name="tokens">Token sequence to display.</param>
        /// <param name="textColor">Color applied to literal text segments.</param>
        /// <param name="fontSize">Font size used for literal text segments.</param>
        /// <param name="alignment">Alignment applied to generated text components.</param>
        public void RenderTokens(IReadOnlyList<EmojiMarkupToken> tokens, Color textColor, int fontSize, TextAnchor alignment)
        {
            if (tokens == null)
            {
                ClearActiveComponents();
                return;
            }

            EnsureLayout();

            int activeIndex = 0;
            for (int i = 0; i < tokens.Count; i++)
            {
                var token = tokens[i];
                var existing = activeIndex < activeComponents.Count ? activeComponents[activeIndex] : null;
                if (token.IsEmoji)
                {
                    if (existing != null && !(existing is Image))
                    {
                        ReturnToPool(existing);
                        activeComponents[activeIndex] = null;
                        existing = null;
                    }

                    var image = existing as Image ?? AcquireImage();
                    PrepareImage(image);
                    token.Emoji.ApplyTo(image);
                    Activate(image, ref activeIndex);
                }
                else
                {
                    if (existing != null && !(existing is Text))
                    {
                        ReturnToPool(existing);
                        activeComponents[activeIndex] = null;
                        existing = null;
                    }

                    var text = existing as Text ?? AcquireText();
                    PrepareText(text);
                    text.text = token.Text ?? string.Empty;
                    text.color = textColor;
                    text.fontSize = fontSize;
                    text.alignment = alignment;
                    text.horizontalOverflow = HorizontalWrapMode.Wrap;
                    text.verticalOverflow = VerticalWrapMode.Overflow;
                    text.raycastTarget = false;
                    Activate(text, ref activeIndex);
                }
            }

            TrimExcess(activeIndex);
            LayoutRebuilder.MarkLayoutForRebuild(transform as RectTransform);
        }

        private void EnsureLayout()
        {
            if (layoutGroup != null)
                return;

            Awake();
        }

        private void Activate(Component component, ref int index)
        {
            if (component == null)
                return;

            if (index < activeComponents.Count)
            {
                var existing = activeComponents[index];
                if (existing != null && existing != component)
                {
                    ReturnToPool(existing);
                }
            }

            var transformComponent = component.transform;
            transformComponent.SetParent(transform, false);
            transformComponent.SetSiblingIndex(index);
            if (!transformComponent.gameObject.activeSelf)
                transformComponent.gameObject.SetActive(true);

            if (index < activeComponents.Count)
                activeComponents[index] = component;
            else
                activeComponents.Add(component);

            index++;
        }

        private Text AcquireText()
        {
            if (textPool.Count > 0)
            {
                var pooledText = textPool.Dequeue();
                ConfigureTextComponent(pooledText);
                return PrepareText(pooledText);
            }

            var go = new GameObject("TextToken", typeof(RectTransform), typeof(Text), typeof(LayoutElement));
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var layout = go.GetComponent<LayoutElement>();
            ConfigureLayoutElement(layout);

            var text = go.GetComponent<Text>();
            ConfigureTextComponent(text);

            return PrepareText(text);
        }

        private static Text PrepareText(Text text)
        {
            if (text == null)
                return null;

            text.gameObject.SetActive(true);
            return text;
        }

        private static void ConfigureTextComponent(Text text)
        {
            if (text == null)
                return;

            text.text = string.Empty;
            text.supportRichText = false;
            LegacyFontProvider.ApplyTo(text);
            text.raycastTarget = false;

            ConfigureLayoutElement(text.GetComponent<LayoutElement>());
        }

        private static void ConfigureLayoutElement(LayoutElement layout)
        {
            if (layout == null)
                return;

            layout.flexibleWidth = 0f;
            layout.minWidth = 0f;
            layout.preferredWidth = -1f;
        }

        private Image AcquireImage()
        {
            if (imagePool.Count > 0)
                return PrepareImage(imagePool.Dequeue());

            var go = new GameObject("EmojiToken", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var layout = go.GetComponent<LayoutElement>();
            layout.preferredWidth = 16f;
            layout.preferredHeight = 16f;
            layout.minWidth = 16f;
            layout.minHeight = 16f;

            var image = go.GetComponent<Image>();
            image.raycastTarget = false;

            return PrepareImage(image);
        }

        private static Image PrepareImage(Image image)
        {
            if (image == null)
                return null;

            image.gameObject.SetActive(true);
            return image;
        }

        private void TrimExcess(int activeCount)
        {
            for (int i = activeComponents.Count - 1; i >= activeCount; i--)
            {
                var component = activeComponents[i];
                ReturnToPool(component);
                activeComponents.RemoveAt(i);
            }
        }

        private void ReturnToPool(Component component)
        {
            if (component == null)
                return;

            var go = component.gameObject;
            go.SetActive(false);
            go.transform.SetParent(transform, false);

            if (component is Text text)
                textPool.Enqueue(text);
            else if (component is Image image)
                imagePool.Enqueue(image);
        }

        private void ClearActiveComponents()
        {
            TrimExcess(0);
        }
    }
}
