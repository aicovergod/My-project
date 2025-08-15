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
        public TMP_FontAsset font;

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

            EnsureLabel();

            if (_labelTf == null || _tmp == null)
            {
                Debug.LogError("[NPCLabel] Failed to create label transform", this);
                return;
            }

            // Configure TMP once (so you can see Text change in Play)
            _tmp.text = labelText;
            _tmp.color = textColor;
            _tmp.fontSize = fontSize;
            if (font != null) _tmp.font = font;
            _tmp.alignment = TextAlignmentOptions.Center;
            _tmp.enableWordWrapping = false;
            _tmp.isOverlay = false;

            // IMPORTANT for diagnosis: do NOT SetActive(false) at startup.
            // Keep renderer enabled; we’ll only drive alpha so we can see state changes clearly.
            ApplyAlpha(0f, toggleRenderer:false);
        }

        void EnsureLabel()
        {
            // If we already have a label transform and its GameObject still exists,
            // there's nothing to do. Unity "missing" objects compare equal to null,
            // but a stale reference might still have a GameObject that has been
            // destroyed, so check that explicitly.
            if (_labelTf != null && _labelTf.gameObject != null)
                return;

            // Clear potentially stale references if the previous label was removed.
            _labelTf = null;
            _tmp = null;

            // Try to find an existing child first.
            var t = transform.Find(ChildName);
            if (t == null || t.gameObject == null)
            {
                // No valid child found – create one now.
                var go = new GameObject(ChildName);
                t = go.transform;
                t.SetParent(transform, false);
            }

            _labelTf = t;
            if (_labelTf == null || _labelTf.gameObject == null)
                return;

            // Cache or create the TextMeshPro component used for rendering.
            _tmp = _labelTf.GetComponent<TextMeshPro>();
            if (_tmp == null)
                _tmp = _labelTf.gameObject.AddComponent<TextMeshPro>();

            // Guard against the label transform being destroyed during component
            // creation. If it survived, update its position information.
            if (_labelTf == null || _labelTf.gameObject == null)
                return;

            _labelTf.localPosition = new Vector3(0, yOffset, 0);
            _baseLocalPos = _labelTf.localPosition;
        }

        void OnEnable()
        {
            Debug.Log($"[NPCLabel] OnEnable on '{name}'");
            EnsureLabel();
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
