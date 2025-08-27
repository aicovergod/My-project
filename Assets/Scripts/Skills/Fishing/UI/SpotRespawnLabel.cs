using TMPro;
using UnityEngine;

namespace Skills.Fishing
{
    public class SpotRespawnLabel : MonoBehaviour
    {
        [SerializeField] private Color textColor = Color.white;
        [SerializeField] private int fontSize = 32;
        [SerializeField] private TMP_FontAsset font;
        [SerializeField] private Color outlineColor = Color.black;
        [SerializeField] private float outlineWidth = 0f;

        private FishableSpot spot;
        private TMP_Text tmp;
        private Transform labelTransform;
        private float remaining;
        private bool counting;

        private void Awake()
        {
            spot = GetComponent<FishableSpot>();
            if (spot == null)
                spot = GetComponentInParent<FishableSpot>();
            CreateLabel();
        }

        private void OnEnable()
        {
            if (spot != null)
            {
                spot.OnSpotDepleted += HandleDepleted;
                spot.OnSpotRespawned += HandleRespawned;
            }
        }

        private void OnDisable()
        {
            if (spot != null)
            {
                spot.OnSpotDepleted -= HandleDepleted;
                spot.OnSpotRespawned -= HandleRespawned;
            }
        }

        private void CreateLabel()
        {
            if (spot == null || labelTransform != null)
                return;

            var existing = spot.transform.Find("SpotRespawnLabel");
            if (existing != null)
            {
                labelTransform = existing;
                tmp = existing.GetComponent<TMP_Text>();
                if (tmp == null)
                    tmp = existing.gameObject.AddComponent<TextMeshPro>();
            }
            else
            {
                var go = new GameObject("SpotRespawnLabel");
                labelTransform = go.transform;
                labelTransform.SetParent(spot.transform, false);

                tmp = go.AddComponent<TextMeshPro>();
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.color = textColor;
                tmp.fontSize = fontSize;
                tmp.enableWordWrapping = false;
                if (font != null) tmp.font = font;
                if (outlineWidth > 0f)
                {
                    tmp.outlineWidth = outlineWidth;
                    tmp.outlineColor = outlineColor;
                }
            }

            labelTransform.gameObject.SetActive(false);
        }

        private void HandleDepleted(FishableSpot node, float respawnSeconds)
        {
            if (labelTransform == null)
                return;
            remaining = respawnSeconds;
            counting = true;
            labelTransform.gameObject.SetActive(true);
        }

        private void HandleRespawned(FishableSpot node)
        {
            counting = false;
            if (labelTransform != null)
                labelTransform.gameObject.SetActive(false);
        }

        private void Update()
        {
            if (!counting || tmp == null)
                return;
            remaining -= Time.deltaTime;
            if (remaining <= 0f)
            {
                counting = false;
                labelTransform.gameObject.SetActive(false);
            }
            else
            {
                tmp.text = $"Respawns in {Mathf.CeilToInt(remaining)}s";
            }
        }
    }
}
