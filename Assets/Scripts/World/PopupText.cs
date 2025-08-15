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

            var go = new GameObject("PopupText");
            go.transform.SetParent(target, false);

            var popup = go.AddComponent<PopupText>();
            popup._life = duration;
            popup._offset = new Vector3(0f, 1f, 0f);

            var tmp = go.AddComponent<TextMeshPro>();
            tmp.text = message;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontSize = 2f;
        }

        private void Update()
        {
            if (Camera.main) transform.rotation = Camera.main.transform.rotation;
            transform.localPosition = _offset;

            _life -= Time.deltaTime;
            if (_life <= 0f)
                Destroy(gameObject);
        }
    }
}
