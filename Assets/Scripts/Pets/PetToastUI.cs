using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using World;

namespace Pets
{
    /// <summary>
    /// Simple top-center toast messages for pet notifications.
    /// </summary>
    public class PetToastUI : MonoBehaviour
    {
        private static PetToastUI instance;
        private static bool waitingForAllowedScene;
        private static bool applicationIsQuitting;

        private Text text;
        private CanvasGroup group;
        private float timer;
        private bool sceneGateSubscribed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (instance != null)
                return;

            var activeScene = SceneManager.GetActiveScene();
            if (!activeScene.IsValid() || !PersistentSceneGate.ShouldSpawnInScene(activeScene))
            {
                BeginWaitingForAllowedScene();
                return;
            }

            CreateOrAdoptInstance();
        }

        private static void CreateOrAdoptInstance()
        {
            if (instance != null)
                return;

            StopWaitingForAllowedScene();

            var existing = FindExistingInstance();
            if (existing != null)
            {
                if (existing.gameObject.scene.name != "DontDestroyOnLoad")
                    DontDestroyOnLoad(existing.gameObject);
                existing.FinaliseSetup();
                return;
            }

            SpawnNewInstance();
        }

        private static PetToastUI FindExistingInstance()
        {
#if UNITY_2023_1_OR_NEWER
            return Object.FindFirstObjectByType<PetToastUI>();
#else
            return Object.FindObjectOfType<PetToastUI>();
#endif
        }

        private static void SpawnNewInstance()
        {
            var go = new GameObject(nameof(PetToastUI), typeof(Canvas), typeof(CanvasScaler), typeof(PetToastUI), typeof(CanvasGroup));
            DontDestroyOnLoad(go);

            var toast = go.GetComponent<PetToastUI>();
            toast.FinaliseSetup();
        }

        private static void BeginWaitingForAllowedScene()
        {
            if (waitingForAllowedScene)
                return;

            waitingForAllowedScene = true;
            PersistentSceneGate.SceneEvaluationChanged += HandleSceneEvaluationForBootstrap;
        }

        private static void StopWaitingForAllowedScene()
        {
            if (!waitingForAllowedScene)
                return;

            PersistentSceneGate.SceneEvaluationChanged -= HandleSceneEvaluationForBootstrap;
            waitingForAllowedScene = false;
        }

        private static void HandleSceneEvaluationForBootstrap(Scene scene, bool allowed)
        {
            if (!allowed)
                return;

            if (scene != SceneManager.GetActiveScene())
                return;

            CreateOrAdoptInstance();
        }

        private void FinaliseSetup()
        {
            instance = this;

            ConfigureCanvas();
            EnsureTextElement();
            group = GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            timer = 0f;

            StopWaitingForAllowedScene();
            EnsureSceneGateSubscription();

            gameObject.SetActive(false);
        }

        private void ConfigureCanvas()
        {
            var canvas = GetComponent<Canvas>() ?? gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = GetComponent<CanvasScaler>() ?? gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        }

        private void EnsureTextElement()
        {
            if (text != null)
                return;

            var existing = GetComponentInChildren<Text>();
            if (existing == null)
            {
                var textGO = new GameObject("Text", typeof(Text));
                textGO.transform.SetParent(transform, false);
                existing = textGO.GetComponent<Text>();
                existing.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                existing.alignment = TextAnchor.UpperCenter;

                var rect = existing.rectTransform;
                rect.anchorMin = new Vector2(0.5f, 1f);
                rect.anchorMax = new Vector2(0.5f, 1f);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.anchoredPosition = new Vector2(0f, -20f);
            }

            text = existing;
        }

        /// <summary>
        /// Show a toast message for a short duration.
        /// </summary>
        public static void Show(string message, Color? color = null)
        {
            if (instance == null)
                return;

            instance.text.text = message;
            instance.text.color = color ?? Color.white;
            instance.timer = 3f;
            instance.group.alpha = 1f;
            instance.gameObject.SetActive(true);
        }

        private void Update()
        {
            if (timer > 0f)
            {
                timer -= Time.deltaTime;
                group.alpha = Mathf.Clamp01(timer / 3f);
                if (timer <= 0f)
                    gameObject.SetActive(false);
            }
        }

        private void OnDestroy()
        {
            if (instance == this)
            {
                if (sceneGateSubscribed)
                {
                    PersistentSceneGate.SceneEvaluationChanged -= HandleSceneGateEvaluation;
                    sceneGateSubscribed = false;
                }

                instance = null;

                if (!applicationIsQuitting)
                    BeginWaitingForAllowedScene();
            }
        }

        private void OnApplicationQuit()
        {
            applicationIsQuitting = true;
        }

        private void EnsureSceneGateSubscription()
        {
            if (sceneGateSubscribed)
                return;

            PersistentSceneGate.SceneEvaluationChanged += HandleSceneGateEvaluation;
            sceneGateSubscribed = true;
        }

        private void HandleSceneGateEvaluation(Scene scene, bool allowed)
        {
            if (instance != this)
                return;

            if (scene != SceneManager.GetActiveScene())
                return;

            if (allowed)
                return;

            PersistentSceneGate.SceneEvaluationChanged -= HandleSceneGateEvaluation;
            sceneGateSubscribed = false;
            Destroy(gameObject);
        }
    }
}
