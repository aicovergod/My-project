using UnityEngine;

namespace Magic
{
    /// <summary>
    /// Fades a sprite renderer over time and destroys the game object when done.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class HitEffect : MonoBehaviour
    {
        private SpriteRenderer sr;
        private float fadeTime = 0.5f;
        private float timer;

        private void Awake()
        {
            sr = GetComponent<SpriteRenderer>();
        }

        public void Initialize(float time)
        {
            fadeTime = time;
            timer = time;
        }

        private void Update()
        {
            if (fadeTime <= 0f)
            {
                Destroy(gameObject);
                return;
            }

            timer -= Time.deltaTime;
            var color = sr.color;
            color.a = Mathf.Clamp01(timer / fadeTime);
            sr.color = color;

            if (timer <= 0f)
                Destroy(gameObject);
        }
    }
}
