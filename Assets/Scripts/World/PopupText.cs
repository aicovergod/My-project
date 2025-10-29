using TMPro;
using UnityEngine;

namespace World
{
    /// <summary>
    /// Displays a temporary text popup above a target transform.
    /// </summary>
    public class PopupText : MonoBehaviour
    {
        private float _life;
        private Vector3 _offset;

        /// <summary>
        /// Creates a popup text above the target.
        /// </summary>
        public static void Show(string message, Transform target, float duration = 2f)
        {
            if (target == null || string.IsNullOrEmpty(message)) return;

            PopupText popup;
            TextMeshPro tmp;

            if (PopupTextPool.Instance != null)
            {
                popup = PopupTextPool.Instance.Get();
                tmp = popup.GetComponent<TextMeshPro>();
                ApplySortingLayer(tmp, target);
            }
            else
            {
                var goNew = new GameObject("PopupText");
                popup = goNew.AddComponent<PopupText>();
                tmp = goNew.AddComponent<TextMeshPro>();
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.fontSize = 2f;
                ApplySortingLayer(tmp, target);
            }

            var go = popup.gameObject;
            go.transform.SetParent(target, false);
            go.SetActive(true);

            popup._life = duration;
            popup._offset = new Vector3(0f, 1f, 0f);

            tmp.text = message;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontSize = 2f;
        }

        /// <summary>
        /// Ensures the popup text renders above the associated world elements by aligning sorting settings.
        /// </summary>
        /// <param name="tmp">The TextMeshPro component that renders the popup.</param>
        /// <param name="anchor">The transform the popup is anchored to for positioning.</param>
        private static void ApplySortingLayer(TextMeshPro tmp, Transform anchor)
        {
            if (tmp == null) return;

            // Retrieve the renderer responsible for displaying the popup text so we can update its sorting data.
            var textRenderer = tmp.GetComponent<Renderer>();
            if (textRenderer == null) return;

            // Attempt to mirror the sorting layer/order from the target object so the popup remains visible above it.
            var anchorRenderer = anchor != null ? anchor.GetComponentInParent<Renderer>() : null;
            if (anchorRenderer != null)
            {
                textRenderer.sortingLayerID = anchorRenderer.sortingLayerID;
                textRenderer.sortingOrder = Mathf.Max(anchorRenderer.sortingOrder + 1, textRenderer.sortingOrder);
                return;
            }

            // Fall back to the "Physical Objects" sorting layer which sits above ground/character layers in the project.
            var fallbackLayerId = SortingLayer.NameToID("Physical Objects");
            if (fallbackLayerId != 0)
            {
                textRenderer.sortingLayerID = fallbackLayerId;
            }

            // Ensure the text draws above most world elements even when falling back to the default layer choice.
            textRenderer.sortingOrder = Mathf.Max(textRenderer.sortingOrder, 1);
        }

        private void Update()
        {
            if (Camera.main) transform.rotation = Camera.main.transform.rotation;
            transform.localPosition = _offset;

            _life -= Time.deltaTime;
            if (_life <= 0f)
            {
                if (PopupTextPool.Instance != null)
                    PopupTextPool.Instance.Return(this);
                else
                    Destroy(gameObject);
            }
        }
    }
}
