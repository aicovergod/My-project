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
        [SerializeField] private float textSize = 0.2f;

        private float remainingLifetime;

        private static FloatingText activeInstance;

        public static void Show(string message, Vector3 position, Color? color = null, float? size = null)
        {
            if (activeInstance == null)
            {
                GameObject go = new GameObject("FloatingText");
                go.transform.position = position;
                activeInstance = go.AddComponent<FloatingText>();
                activeInstance.textMesh = go.AddComponent<TextMesh>();
            }

            activeInstance.transform.position = position;
            activeInstance.textMesh.text = message;
            activeInstance.textMesh.color = color ?? Color.white;
            float finalSize = size ?? activeInstance.textSize;
            activeInstance.textMesh.characterSize = finalSize;
            activeInstance.textMesh.fontSize = Mathf.RoundToInt(64 * finalSize);
            activeInstance.remainingLifetime = activeInstance.lifetime;
        }

        private void Awake()
        {
            remainingLifetime = lifetime;
        }

        private void Update()
        {
            transform.position += floatSpeed * Time.deltaTime;
            remainingLifetime -= Time.deltaTime;
            if (remainingLifetime <= 0f)
                Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (activeInstance == this)
                activeInstance = null;
        }
    }
}
