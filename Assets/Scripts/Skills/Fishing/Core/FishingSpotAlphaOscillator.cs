using UnityEngine;

namespace Skills.Fishing
{
    public class FishingSpotAlphaOscillator : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer sr;
        [SerializeField] private float minAlpha = 0.5f;
        [SerializeField] private float maxAlpha = 1f;
        [SerializeField] private float minSpeed = 0.5f;
        [SerializeField] private float maxSpeed = 1f;

        private float targetAlpha;
        private float speed;

        private void Awake()
        {
            if (sr == null)
                sr = GetComponent<SpriteRenderer>();
            PickNewTarget();
        }

        private void OnEnable()
        {
            PickNewTarget();
        }

        private void Update()
        {
            if (sr == null)
                return;

            var color = sr.color;
            color.a = Mathf.MoveTowards(color.a, targetAlpha, speed * Time.deltaTime);
            sr.color = color;

            if (Mathf.Approximately(color.a, targetAlpha))
                PickNewTarget();
        }

        private void PickNewTarget()
        {
            targetAlpha = Random.Range(minAlpha, maxAlpha);
            speed = Random.Range(minSpeed, maxSpeed);
        }
    }
}
