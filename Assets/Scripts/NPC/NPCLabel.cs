using UnityEngine;
using TMPro;
using Pets;

namespace NPC
{
    /// <summary>
    /// Displays a floating label above the NPC. If a <see cref="PetExperience"/>
    /// component is present on the same GameObject, the label automatically
    /// appends the current pet level.
    /// </summary>
    [DisallowMultipleComponent]
    public class NpcLabel : MonoBehaviour
    {
        [Header("Appearance")]
        [SerializeField] private string labelText = "Pet";
        [SerializeField] private Color textColor = Color.white;
        [SerializeField] private TMP_FontAsset font;
        [SerializeField] private float fontSize = 2f;
        [SerializeField] private float yOffset = 1.5f;

        [Header("Animation")]
        [SerializeField] private float bobAmplitude = 0.05f;
        [SerializeField] private float bobFrequency = 2f;

        [Header("Visibility")]
        [SerializeField] private Transform viewer;
        [SerializeField] private float showRadius = 3f;
        [SerializeField] private float fadeSpeed = 8f;

        private TextMeshPro _tmp;
        private Transform _labelTf;
        private float _alpha;
        private float _baseY;
        private PetExperience _experience;

        private void Awake()
        {
            if (viewer == null)
            {
                var go = GameObject.FindGameObjectWithTag("Player");
                if (go) viewer = go.transform;
            }

            _experience = GetComponent<PetExperience>();
            if (_experience) _experience.OnLevelChanged += OnLevelChanged;

            CreateLabel();
            UpdateLabelText();
        }

        private void OnDestroy()
        {
            if (_experience) _experience.OnLevelChanged -= OnLevelChanged;
        }

        private void CreateLabel()
        {
            var go = new GameObject("NpcLabel");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, yOffset, 0f);

            _labelTf = go.transform;
            _baseY = _labelTf.localPosition.y;

            _tmp = go.AddComponent<TextMeshPro>();
            _tmp.alignment = TextAlignmentOptions.Center;
            _tmp.fontSize = fontSize;
            _tmp.color = textColor;
            if (font) _tmp.font = font;
        }

        private void Update()
        {
            if (_labelTf == null) return;

            // bob
            float bob = Mathf.Sin(Time.time * bobFrequency) * bobAmplitude;
            _labelTf.localPosition = new Vector3(0f, _baseY + bob, 0f);

            // face camera
            if (Camera.main) _labelTf.rotation = Camera.main.transform.rotation;

            // fade based on distance
            float target = 1f;
            if (viewer)
            {
                float dist = Vector3.Distance(viewer.position, transform.position);
                target = dist <= showRadius ? 1f : 0f;
            }

            _alpha = Mathf.MoveTowards(_alpha, target, fadeSpeed * Time.deltaTime);
            var c = _tmp.color; c.a = _alpha; _tmp.color = c;
        }

        private void OnLevelChanged(int lvl)
        {
            UpdateLabelText();
        }

        private void UpdateLabelText()
        {
            if (_tmp == null) return;
            if (_experience)
                _tmp.text = $"{labelText} Lv {_experience.Level}";
            else
                _tmp.text = labelText;
        }
    }
}

