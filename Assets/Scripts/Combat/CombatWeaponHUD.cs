using UnityEngine;
using UnityEngine.SceneManagement;
using Inventory;
using UI;
using World;

namespace Combat
{
    /// <summary>
    /// Displays the player's current weapon sprite above the active combat target.
    /// </summary>
    public class CombatWeaponHUD : MonoBehaviour
    {
        private static CombatWeaponHUD instance;
        private static bool waitingForAllowedScene;
        private static bool applicationIsQuitting;

        private CombatController controller;
        private Equipment equipment;
        private Transform target;
        private GameObject weaponRoot;
        private SpriteRenderer weaponRenderer;
        private readonly Vector3 offset = new Vector3(0f, 0.75f, 0f);
        private bool spellActiveLastFrame;
        private bool sceneGateSubscribed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateInstance()
        {
            if (instance != null)
                return;

#if UNITY_2023_1_OR_NEWER
            if (Object.FindFirstObjectByType<CombatWeaponHUD>() != null)
#else
            if (Object.FindObjectOfType<CombatWeaponHUD>() != null)
#endif
            {
                // An instance already exists in the scene. Adopt it so the gating logic can manage
                // persistence consistently.
                CreateOrAdoptInstance();
                return;
            }

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
                instance = existing;
                existing.EnsureSceneGateSubscription();
                return;
            }

            var go = new GameObject(nameof(CombatWeaponHUD));
            DontDestroyOnLoad(go);
            go.AddComponent<CombatWeaponHUD>();
        }

        private static CombatWeaponHUD FindExistingInstance()
        {
#if UNITY_2023_1_OR_NEWER
            return Object.FindFirstObjectByType<CombatWeaponHUD>();
#else
            return Object.FindObjectOfType<CombatWeaponHUD>();
#endif
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

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            StopWaitingForAllowedScene();
            EnsureSceneGateSubscription();

            controller = FindObjectOfType<CombatController>();
            if (controller != null)
                controller.OnCombatTargetChanged += HandleTargetChanged;
            if (controller != null)
                equipment = controller.GetComponent<Equipment>();
            if (equipment != null)
                equipment.OnEquipmentChanged += HandleEquipmentChanged;
            CreateWeaponSprite();
        }

        private void CreateWeaponSprite()
        {
            weaponRoot = new GameObject("CombatWeaponSprite");
            weaponRoot.transform.SetParent(transform);
            weaponRenderer = weaponRoot.AddComponent<SpriteRenderer>();
            weaponRenderer.sortingOrder = 100;
            weaponRoot.SetActive(false);
        }

        private void HandleTargetChanged(CombatTarget newTarget)
        {
            target = newTarget != null ? newTarget.transform : null;
            RefreshWeaponSprite();
        }

        private void Update()
        {
            bool spellActive = MagicUI.ActiveSpell != null;
            if (spellActive != spellActiveLastFrame)
            {
                RefreshWeaponSprite();
                spellActiveLastFrame = spellActive;
            }

            if (target != null && weaponRoot != null && weaponRoot.activeSelf)
                weaponRoot.transform.position = target.position + offset;
        }

        private void OnDestroy()
        {
            if (controller != null)
                controller.OnCombatTargetChanged -= HandleTargetChanged;
            if (equipment != null)
                equipment.OnEquipmentChanged -= HandleEquipmentChanged;

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

        private void HandleEquipmentChanged(EquipmentSlot slot)
        {
            if (slot != EquipmentSlot.Weapon)
                return;

            RefreshWeaponSprite();
        }

        private void RefreshWeaponSprite()
        {
            if (weaponRenderer == null || weaponRoot == null)
                return;

            if (target == null || MagicUI.ActiveSpell != null)
            {
                weaponRenderer.sprite = null;
                weaponRoot.SetActive(false);
                return;
            }

            var entry = equipment != null ? equipment.GetEquipped(EquipmentSlot.Weapon) : default;
            if (entry.item != null && entry.item.icon != null)
            {
                weaponRenderer.sprite = entry.item.icon;
                weaponRoot.SetActive(true);
            }
            else
            {
                weaponRenderer.sprite = null;
                weaponRoot.SetActive(false);
            }
        }
    }
}
