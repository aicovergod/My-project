using Player;
using Player.Chat;
using UnityEngine;
using UnityEngine.SceneManagement;
using World;
using Object = UnityEngine.Object;

namespace UI.Chat
{
    /// <summary>
    /// Bootstrapper responsible for spawning the chat HUD and wiring it to the active player.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ChatHudBootstrap : MonoBehaviour
    {
        private static ChatHudBootstrap instance;

        private ChatHudController hudInstance;
        private PlayerChatController cachedPlayerController;
        private float nextProbeTime;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateBootstrapper()
        {
            if (instance != null)
                return;

            var go = new GameObject(nameof(ChatHudBootstrap));
            DontDestroyOnLoad(go);
            instance = go.AddComponent<ChatHudBootstrap>();
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            EnsureHud();
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += HandleSceneLoaded;
            PersistentSceneGate.SceneEvaluationChanged += HandleSceneEvaluation;

            if (PersistentSceneGate.IsActiveSceneAllowed)
            {
                EnsureHud();
                TryAttachHudToPlayer();
            }
            else
            {
                DestroyHudInstance();
            }
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            PersistentSceneGate.SceneEvaluationChanged -= HandleSceneEvaluation;
        }

        private void Update()
        {
            if (!PersistentSceneGate.IsActiveSceneAllowed)
                return;

            if (Time.unscaledTime < nextProbeTime)
                return;

            nextProbeTime = Time.unscaledTime + 1f;
            TryAttachHudToPlayer();
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!PersistentSceneGate.IsActiveSceneAllowed)
            {
                DestroyHudInstance();
                return;
            }

            EnsureHud();
            TryAttachHudToPlayer();
        }

        private void HandleSceneEvaluation(Scene scene, bool allowed)
        {
            if (scene != SceneManager.GetActiveScene())
                return;

            if (allowed)
            {
                EnsureHud();
                TryAttachHudToPlayer();
            }
            else
            {
                DestroyHudInstance();
            }
        }

        private void EnsureHud()
        {
            if (!PersistentSceneGate.IsActiveSceneAllowed)
            {
                DestroyHudInstance();
                return;
            }

            if (hudInstance != null)
                return;

            Transform parent = ResolveUiRootParent();
            hudInstance = ChatHudController.Create(parent);
        }

        private Transform ResolveUiRootParent()
        {
            string[] candidates = { "HUDRoot", "HudRoot", "UIRoot", "GlobalUIRoot" };
            for (int i = 0; i < candidates.Length; i++)
            {
                var found = GameObject.Find(candidates[i]);
                if (found != null)
                    return found.transform;
            }

            return null;
        }

        private void TryAttachHudToPlayer()
        {
            if (!PersistentSceneGate.IsActiveSceneAllowed)
            {
                DestroyHudInstance();
                return;
            }

            EnsureHud();
            if (hudInstance == null)
                return;

            var controller = FindPlayerChatController();
            if (controller == null)
            {
                cachedPlayerController = null;
                return;
            }

            if (controller.HasHud(hudInstance))
            {
                cachedPlayerController = controller;
                return;
            }

            controller.SetHud(hudInstance);
            cachedPlayerController = controller;
        }

        private PlayerChatController FindPlayerChatController()
        {
            if (cachedPlayerController != null && cachedPlayerController.isActiveAndEnabled)
                return cachedPlayerController;

            if (PlayerLocator.TryFindPlayer(out var playerObj))
            {
                var controller = playerObj.GetComponent<PlayerChatController>();
                if (controller != null)
                    return controller;
            }

            return Object.FindFirstObjectByType<PlayerChatController>(FindObjectsInactive.Include);
        }

        private void DestroyHudInstance()
        {
            if (hudInstance != null)
            {
                if (cachedPlayerController != null && cachedPlayerController.HasHud(hudInstance))
                    cachedPlayerController.SetHud(null);

                var hudObject = hudInstance.gameObject;
                Object.Destroy(hudObject);
                hudInstance = null;
            }

            cachedPlayerController = null;
        }
    }
}
