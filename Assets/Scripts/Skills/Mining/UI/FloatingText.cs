using UnityEngine;

namespace Skills.Mining
{
    /// <summary>
    /// Simple floating text utility for feedback messages.
    /// </summary>
    public class FloatingText : MonoBehaviour
    {
        [SerializeField] private float lifetime = 1.5f;
        [SerializeField] private Vector3 floatSpeed = new Vector3(0f, 1f, 0f);
        private TextMesh textMesh;

        public static void Show(string message, Vector3 position, Color? color = null)
        {
            GameObject go = new GameObject("FloatingText");
            go.transform.position = position;
            var ft = go.AddComponent<FloatingText>();
            ft.textMesh = go.AddComponent<TextMesh>();
            ft.textMesh.text = message;
            ft.textMesh.color = color ?? Color.white;
        }

        private void Update()
        {
            transform.position += floatSpeed * Time.deltaTime;
            lifetime -= Time.deltaTime;
            if (lifetime <= 0f)
                Destroy(gameObject);
        }
    }
}
