using UnityEngine;
using UnityEngine.EventSystems;

namespace UI
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(EventSystem))]
    public sealed class PersistentEventSystem : MonoBehaviour
    {
        private static EventSystem _persistent;

        private void Awake()
        {
            var es = GetComponent<EventSystem>();

            if (_persistent != null && _persistent != es)
            {
                Destroy(gameObject);
                return;
            }

            _persistent = es;
            DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            if (_persistent == GetComponent<EventSystem>())
                _persistent = null;
        }
    }
}
