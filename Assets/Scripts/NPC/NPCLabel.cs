using TMPro;
using UnityEngine;

namespace NPC
{
    public class NpcLabel : MonoBehaviour
    {
        [Header("Text")]
        public string labelText = "WEEEEEEE";
        public Color textColor = Color.white;
        public float fontSize = 2f;
        public float yOffset = 1f;
        [Range(0f,1f)] public float visibleAlpha = 1f;

        [Header("Float")]
        public float floatAmplitude = 0.05f;
        public float floatFrequency = 2f;

        [Header("Show Within Radius")]
        public Transform player;              // drag Player here or tag "Player"
        public float showRadiusTiles = 2f;
        public float unitsPerTile = 1f;
        public float fadeInDuration = 0.2f;
        public float fadeOutDuration = 0.25f;

        [Header("Debug")]
        public bool alwaysShowRadiusGizmo = true;   // draw even when not selected
        public bool logState = true;
        public float logEverySeconds = 0.5f;

        const string ChildName = "NPC_Label";
        TextMeshPro _tmp;
        Transform _labelTf;
        Vector3 _baseLocalPos;
        float _alpha = 0f;
        float _nextLog;
        bool _shouldShow;

        void Awake()
        {
            Debug.Log($"[NPCLabel] Awake on '{name}' (enabled={enabled}, activeInHierarchy={gameObject.activeInHierarchy})");

            if (player == null)
            {
                var p = GameObject.FindGameObjectWithTag("Player");
                if (p) player = p.transform;
            }

            // Find or create the label child under THIS NPC only
            var t = transform.Find(ChildName);
            if (t == null)
            {
                var go = new GameObject(ChildName);
                go.transform.SetParent(transform, false);
                t = go.transform;
                _tmp = go.AddComponent<TextMeshPro>();
            }
            else
            {
                _tmp = t.GetComponent<TextMeshPro>();
                if (_tmp == null) _tmp = t.gameObject.AddComponent<TextMeshPro>();
            }

            _labelTf = t;
           _labelTf.localPosition = new Vector3(0, yOffset, 0);
            _baseLocalPos = _labelTf.localPosition;

            // Configure TMP once (so you can see Text change in Play)
            _tmp.text = labelText;
            _tmp.color = textColor;
            _tmp.fontSize = fontSize;
            _tmp.alignment = TextAlignmentOptions.Center;
            _tmp.enableWordWrapping = false;
            _tmp.isOverlay = false;

            // IMPORTANT for diagnosis: do NOT SetActive(false) at startup.
            // Keep renderer enabled; we’ll only drive alpha so we can see state changes clearly.
            ApplyAlpha(0f, toggleRenderer:false);
        }

        void OnEnable()
        {
            Debug.Log($"[NPCLabel] OnEnable on '{name}'");
        }

        void Start()
        {
            Debug.Log($"[NPCLabel] Start on '{name}' (player={(player?player.name:"<null>")})");
        }

        void Update()
        {
            if (_labelTf == null || _tmp == null) return;

            // Bob + billboard
            float bob = Mathf.Sin(Time.time * floatFrequency) * floatAmplitude;
            _labelTf.localPosition = _baseLocalPos + new Vector3(0, bob, 0);
            if (Camera.main) _labelTf.rotation = Camera.main.transform.rotation;

            // Distance (tiles -> units)
            float maxDist = Mathf.Max(0.0001f, showRadiusTiles * Mathf.Max(0.0001f, unitsPerTile));
            if (player != null)
            {
                float dist = Vector2.Distance(player.position, transform.position);
                _shouldShow = dist <= maxDist;
                if (logState && Time.unscaledTime >= _nextLog)
                {
                    _nextLog = Time.unscaledTime + Mathf.Max(0.1f, logEverySeconds);
                    Debug.Log($"[NPCLabel] '{name}' dist={dist:F2} max={maxDist:F2} shouldShow={_shouldShow} alpha={_alpha:F2} compEnabled={enabled} goActive={gameObject.activeInHierarchy}");
                }
            }
            else
            {
                _shouldShow = false;
            }

            // Fade (alpha only; do NOT set active in this diagnostic build)
            float target = _shouldShow ? Mathf.Clamp01(visibleAlpha) : 0f;
            float dur = _shouldShow ? Mathf.Max(0.001f, fadeInDuration) : Mathf.Max(0.001f, fadeOutDuration);
            _alpha = Mathf.MoveTowards(_alpha, target, Time.deltaTime * (1f / dur));
            ApplyAlpha(_alpha, toggleRenderer:false);
        }

        void ApplyAlpha(float a01, bool toggleRenderer)
        {
            if (_tmp == null) return;
            a01 = Mathf.Clamp01(a01);
            var c = _tmp.color; c.a = a01; _tmp.color = c;
            if (toggleRenderer)
            {
                var r = _tmp.renderer;
                if (r) r.enabled = a01 > 0.01f;
            }
        }

#if UNITY_EDITOR
        void OnDrawGizmos()
        {
            if (!alwaysShowRadiusGizmo) return;
            Gizmos.color = new Color(0f, 1f, 1f, 0.6f);
            float r = Mathf.Max(0.0001f, showRadiusTiles * Mathf.Max(0.0001f, unitsPerTile));
            Gizmos.DrawWireSphere(transform.position, r);
        }
#endif
    }
}
