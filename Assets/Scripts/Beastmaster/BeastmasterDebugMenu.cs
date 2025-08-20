using UnityEngine;
using System;

namespace Beastmaster
{
    /// <summary>
    /// Debug menu for adjusting the Beastmaster level and viewing merge parameters.
    /// Toggle with F2.
    /// </summary>
    public class BeastmasterDebugMenu : MonoBehaviour
    {
        [SerializeField] private MergeConfig config;
        [SerializeField] private MonoBehaviour beastmasterServiceComponent;

        private IBeastmasterService service;
        private bool visible;
        private string levelField = "1";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            var go = new GameObject("BeastmasterDebugMenu");
            DontDestroyOnLoad(go);
            go.AddComponent<BeastmasterDebugMenu>();
        }

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F2))
            {
                visible = !visible;
                Refresh();
            }
        }

        private void Refresh()
        {
            service = beastmasterServiceComponent as IBeastmasterService;
            if (service == null)
            {
                foreach (var mb in FindObjectsOfType<MonoBehaviour>())
                {
                    if (mb is IBeastmasterService b)
                    {
                        service = b;
                        break;
                    }
                }
            }
            levelField = service != null ? service.CurrentLevel.ToString() : levelField;
        }

        private void OnGUI()
        {
            if (!visible)
                return;

            Rect area = new Rect(10f, 240f, 220f, 120f);
            GUILayout.BeginArea(area, GUI.skin.box);
            GUILayout.Label("Beastmaster Level");
            levelField = GUILayout.TextField(levelField);
            if (int.TryParse(levelField, out int lvl))
            {
                if (service != null)
                    service.SetLevel(Mathf.Clamp(lvl, 1, 99));
            }

            if (config != null && int.TryParse(levelField, out int current))
            {
                if (config.TryGetMergeParams(current, out var dur, out var cd, out var locked))
                {
                    GUILayout.Label($"Duration: {dur.TotalMinutes:0}m");
                    GUILayout.Label($"Cooldown: {cd.TotalMinutes:0}m");
                    if (locked)
                        GUILayout.Label("Locked (<50)");
                }
            }
            GUILayout.EndArea();
        }
    }
}
