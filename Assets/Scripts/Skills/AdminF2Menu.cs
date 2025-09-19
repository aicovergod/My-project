using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using Player;
using Beastmaster;
using Pets;
using BankSystem;
using Skills.Fishing;
using Skills.Outfits;
using Status;
using Status.Antifire;
using Status.Poison;
using Status.Freeze;
using World;

namespace Skills
{
    /// <summary>
    /// Debug menu that allows setting player skill levels. Toggle with F2.
    /// </summary>
    [DisallowMultipleComponent]
    public class AdminF2Menu : MonoBehaviour
    {
        private static AdminF2Menu instance;

        private static bool waitingForAllowedScene;
        private static bool applicationIsQuitting;

        public static AdminF2Menu Instance => instance;

        private bool sceneGateSubscribed;

        private PlayerHitpoints hitpoints;
        private SkillManager skillManager;
        private IBeastmasterService beastmasterService;
        private MergeConfig mergeConfig;
        private PoisonController poisonController;
        private PoisonConfig poisonPConfig;

        private const string PoisonPResourcePath = "Status/Poison/Poison_p";

        private bool visible;
        private bool noclip;
        private bool showFreezePopup;
        private Rect freezePopupRect = new Rect(240f, 10f, 240f, 150f);
        private string freezeTickInput = "8";
        private string freezeError = string.Empty;
        private string hpLevel = "";
        private string attackLevel = "";
        private string strengthLevel = "";
        private string defenceLevel = "";
        private string magicLevel = "";
        private string miningLevel = "";
        private string woodcuttingLevel = "";
        private string fishingLevel = "";
        private string cookingLevel = "";
        private string beastmasterLevel = "";

        // Scroll position for the debug menu
        private Vector2 scrollPos;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
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
                instance = existing;
                if (existing.gameObject.scene.name != "DontDestroyOnLoad")
                    DontDestroyOnLoad(existing.gameObject);
                existing.EnsureSceneGateSubscription();
                return;
            }

            var go = new GameObject(nameof(AdminF2Menu));
            DontDestroyOnLoad(go);
            go.AddComponent<AdminF2Menu>();
        }

        private static AdminF2Menu FindExistingInstance()
        {
#if UNITY_2023_1_OR_NEWER
            return UnityEngine.Object.FindFirstObjectByType<AdminF2Menu>();
#else
            return UnityEngine.Object.FindObjectOfType<AdminF2Menu>();
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
            DontDestroyOnLoad(gameObject);

            StopWaitingForAllowedScene();
            EnsureSceneGateSubscription();
        }

        private void OnApplicationQuit()
        {
            applicationIsQuitting = true;
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

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F2))
            {
                visible = !visible;
                if (visible)
                    RefreshFields();
            }

            if (!visible)
                return;

            // Ensure references are valid in case scenes change
            if (hitpoints == null)
                hitpoints = FindObjectOfType<PlayerHitpoints>();
            if (skillManager == null)
                skillManager = FindObjectOfType<SkillManager>();
            if (poisonController == null && hitpoints != null)
                poisonController = hitpoints.GetComponent<PoisonController>();
            if (beastmasterService == null)
            {
                foreach (var mb in FindObjectsOfType<MonoBehaviour>())
                {
                    if (mb is IBeastmasterService b)
                    {
                        beastmasterService = b;
                        break;
                    }
                }
            }
        }

        private void RefreshFields()
        {
            hitpoints = FindObjectOfType<PlayerHitpoints>();
            skillManager = FindObjectOfType<SkillManager>();
            poisonController = hitpoints != null ? hitpoints.GetComponent<PoisonController>() : null;
            beastmasterService = null;
            foreach (var mb in FindObjectsOfType<MonoBehaviour>())
            {
                if (mb is IBeastmasterService b)
                {
                    beastmasterService = b;
                    break;
                }
            }
            if (mergeConfig == null)
                mergeConfig = Resources.Load<MergeConfig>("MergeConfig");

            hpLevel = skillManager != null ? skillManager.GetLevel(SkillType.Hitpoints).ToString() : "";
            attackLevel = skillManager != null ? skillManager.GetLevel(SkillType.Attack).ToString() : "";
            strengthLevel = skillManager != null ? skillManager.GetLevel(SkillType.Strength).ToString() : "";
            defenceLevel = skillManager != null ? skillManager.GetLevel(SkillType.Defence).ToString() : "";
            magicLevel = skillManager != null ? skillManager.GetLevel(SkillType.Magic).ToString() : "";
            miningLevel = skillManager != null ? skillManager.GetLevel(SkillType.Mining).ToString() : "";
            woodcuttingLevel = skillManager != null ? skillManager.GetLevel(SkillType.Woodcutting).ToString() : "";
            fishingLevel = skillManager != null ? skillManager.GetLevel(SkillType.Fishing).ToString() : "";
            cookingLevel = skillManager != null ? skillManager.GetLevel(SkillType.Cooking).ToString() : "";
            beastmasterLevel = skillManager != null ? skillManager.GetLevel(SkillType.Beastmaster).ToString() : "";
        }

        private void OnGUI()
        {
            if (!visible)
                return;

            const float width = 220f;
            const float height = 220f;
            Rect area = new Rect(10f, 10f, width, height);
            GUILayout.BeginArea(area, GUI.skin.box);

            // Begin scroll view so all fields are accessible even if the window is small
            scrollPos = GUILayout.BeginScrollView(scrollPos, false, true);

            GUILayout.Label("Hitpoints Level");
            hpLevel = GUILayout.TextField(hpLevel);

            GUILayout.Label("Attack Level");
            attackLevel = GUILayout.TextField(attackLevel);

            GUILayout.Label("Strength Level");
            strengthLevel = GUILayout.TextField(strengthLevel);

            GUILayout.Label("Defence Level");
            defenceLevel = GUILayout.TextField(defenceLevel);

            GUILayout.Label("Magic Level");
            magicLevel = GUILayout.TextField(magicLevel);

            GUILayout.Label("Mining Level");
            miningLevel = GUILayout.TextField(miningLevel);

            GUILayout.Label("Woodcutting Level");
            woodcuttingLevel = GUILayout.TextField(woodcuttingLevel);

            GUILayout.Label("Fishing Level");
            fishingLevel = GUILayout.TextField(fishingLevel);

            GUILayout.Label("Cooking Level");
            cookingLevel = GUILayout.TextField(cookingLevel);

            GUILayout.Label("Beastmaster Level");
            beastmasterLevel = GUILayout.TextField(beastmasterLevel);
            if (mergeConfig != null && int.TryParse(beastmasterLevel, out var bmLevel))
            {
                if (mergeConfig.TryGetMergeParams(bmLevel, out var dur, out var cd, out var locked))
                {
                    GUILayout.Label($"Duration: {dur.TotalMinutes:0}m");
                    GUILayout.Label($"Cooldown: {cd.TotalMinutes:0}m");
                    if (locked)
                        GUILayout.Label("Locked (<50)");
                }
            }

            if (GUILayout.Button("Apply"))
            {
                if (skillManager != null && int.TryParse(hpLevel, out var hp))
                {
                    skillManager.DebugSetLevel(SkillType.Hitpoints, hp);
                    if (hitpoints != null)
                        hitpoints.DebugSetCurrentHp(Mathf.Min(hitpoints.CurrentHp, hitpoints.MaxHp));
                }
                if (skillManager != null && int.TryParse(attackLevel, out var atk))
                    skillManager.DebugSetLevel(SkillType.Attack, atk);
                if (skillManager != null && int.TryParse(strengthLevel, out var str))
                    skillManager.DebugSetLevel(SkillType.Strength, str);
                if (skillManager != null && int.TryParse(defenceLevel, out var def))
                    skillManager.DebugSetLevel(SkillType.Defence, def);
                if (skillManager != null && int.TryParse(magicLevel, out var mag))
                    skillManager.DebugSetLevel(SkillType.Magic, mag);
                if (skillManager != null && int.TryParse(miningLevel, out var mine))
                    skillManager.DebugSetLevel(SkillType.Mining, mine);
                if (skillManager != null && int.TryParse(woodcuttingLevel, out var wood))
                    skillManager.DebugSetLevel(SkillType.Woodcutting, wood);
                if (skillManager != null && int.TryParse(fishingLevel, out var fish))
                    skillManager.DebugSetLevel(SkillType.Fishing, fish);
                if (skillManager != null && int.TryParse(cookingLevel, out var cook))
                    skillManager.DebugSetLevel(SkillType.Cooking, cook);
                if (skillManager != null && int.TryParse(beastmasterLevel, out var bm))
                {
                    skillManager.DebugSetLevel(SkillType.Beastmaster, bm);
                    beastmasterService?.SetLevel(Mathf.Clamp(bm, 1, 99));
                }

                RefreshFields();
            }

            if (GUILayout.Button("Max Stats"))
            {
                if (skillManager != null)
                {
                    foreach (SkillType type in Enum.GetValues(typeof(SkillType)))
                        skillManager.DebugSetLevel(type, 99);
                    hitpoints?.DebugSetCurrentHp(hitpoints.MaxHp);
                    beastmasterService?.SetLevel(99);
                    RefreshFields();
                }
            }

            if (GUILayout.Button("Restore Health"))
            {
                hitpoints?.DebugSetCurrentHp(hitpoints.MaxHp);
            }

            if (GUILayout.Button("Godmode"))
            {
                hitpoints?.DebugSetCurrentHp(99999, false);
            }

            if (GUILayout.Button("Godmode Off"))
            {
                hitpoints?.DebugSetCurrentHp(hitpoints.MaxHp);
            }

            if (GUILayout.Button("Apply Poison (p)"))
            {
                ApplyPoisonP();
            }

            if (GUILayout.Button("Apply Antifire Buff"))
            {
                ApplyAntifireBuff();
            }

            if (GUILayout.Button("Freeze for X time"))
            {
                ShowFreezePopup();
            }

            if (GUILayout.Button(noclip ? "Disable Noclip" : "Enable Noclip"))
            {
                var playerObj = GameObject.FindGameObjectWithTag("Player");
                if (playerObj != null)
                {
                    foreach (var col in playerObj.GetComponentsInChildren<Collider2D>())
                        col.enabled = !noclip;
                    foreach (var col in playerObj.GetComponentsInChildren<Collider>())
                        col.enabled = !noclip;
                }
                noclip = !noclip;
            }

            if (GUILayout.Button("Reset Merge Timer"))
            {
                PetMergeController.Instance?.ResetMergeTimer();
            }

            if (GUILayout.Button(PetDropSystem.DebugPetRolls ? "Disable Pet Roll Debug" : "Enable Pet Roll Debug"))
            {
                PetDropSystem.DebugPetRolls = !PetDropSystem.DebugPetRolls;
            }

            if (GUILayout.Button(BycatchManager.DebugBycatchRolls ? "Disable Bycatch Debug" : "Enable Bycatch Debug"))
            {
                BycatchManager.DebugBycatchRolls = !BycatchManager.DebugBycatchRolls;
            }

            if (GUILayout.Button(SkillingOutfitProgress.DebugChance ? "Disable Skilling Outfit Chance" : "Enable Skilling Outfit Chance"))
            {
                SkillingOutfitProgress.DebugChance = !SkillingOutfitProgress.DebugChance;
            }

            if (GUILayout.Button("Open Bank"))
            {
                BankUI.Instance?.Open();
            }

            if (GUILayout.Button("Clear Inventory"))
            {
                var playerInv = GameObject.FindGameObjectWithTag("Player")?.GetComponent<Inventory.Inventory>();
                if (playerInv != null)
                {
                    for (int i = 0; i < playerInv.size; i++)
                        playerInv.ClearSlot(i);
                    playerInv.Save();
                }
            }

            if (GUILayout.Button("Clear Bank"))
            {
                BankUI.Instance?.ClearBank();
            }

            GUILayout.EndScrollView();

            GUILayout.EndArea();

            if (showFreezePopup)
                freezePopupRect = GUI.ModalWindow(0xF20F2, freezePopupRect, DrawFreezePopup, "Freeze Player");
        }

        /// <summary>
        /// Applies the standard poison (p) status effect to the player for quick debugging.
        /// </summary>
        private void ApplyPoisonP()
        {
            var controller = ResolvePoisonController();
            if (controller == null)
            {
                Debug.LogWarning("AdminF2Menu could not find a PoisonController on the player to apply poison.");
                return;
            }

            if (poisonPConfig == null)
            {
                poisonPConfig = Resources.Load<PoisonConfig>(PoisonPResourcePath);
                if (poisonPConfig == null)
                {
                    Debug.LogWarning($"AdminF2Menu could not load poison config at Resources/{PoisonPResourcePath}.");
                    return;
                }
            }

            controller.ApplyPoison(poisonPConfig);
        }

        /// <summary>
        /// Applies the standard antifire buff to the player for debugging.
        /// </summary>
        private void ApplyAntifireBuff()
        {
            if (hitpoints == null)
                hitpoints = FindObjectOfType<PlayerHitpoints>();

            var target = hitpoints != null ? hitpoints.gameObject : GameObject.FindGameObjectWithTag("Player");
            if (target == null)
            {
                Debug.LogWarning("AdminF2Menu could not locate the player to apply the antifire buff.");
                return;
            }

            var definition = AntifireProtectionController.BuildStandardAntifireBuffDefinition();
            var context = new BuffEventContext
            {
                target = target,
                definition = definition,
                sourceType = BuffSourceType.Scripted,
                sourceId = nameof(AdminF2Menu)
            };

            BuffEvents.RaiseBuffApplied(context);
        }

        /// <summary>
        /// Ensures we are referencing the current player's <see cref="PoisonController"/>.
        /// </summary>
        private PoisonController ResolvePoisonController()
        {
            if (poisonController != null)
                return poisonController;

            if (hitpoints != null)
                poisonController = hitpoints.GetComponent<PoisonController>();

            if (poisonController == null)
            {
                var playerObj = GameObject.FindGameObjectWithTag("Player");
                poisonController = playerObj != null ? playerObj.GetComponent<PoisonController>() : null;
            }

            return poisonController;
        }

        /// <summary>
        /// Opens the freeze popup and clears any previous error message.
        /// </summary>
        private void ShowFreezePopup()
        {
            showFreezePopup = true;
            if (string.IsNullOrEmpty(freezeTickInput))
                freezeTickInput = "8";
            freezeError = string.Empty;
        }

        /// <summary>
        /// Renders the freeze duration popup window used to debug the frozen status effect.
        /// </summary>
        private void DrawFreezePopup(int windowId)
        {
            Event e = Event.current;
            if (e != null && e.type == EventType.KeyDown)
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                {
                    ApplyFreezePopupSelection();
                    e.Use();
                }
                else if (e.keyCode == KeyCode.Escape)
                {
                    CloseFreezePopup();
                    e.Use();
                }
            }

            GUILayout.Label("Duration (ticks, 0.6s each)");
            GUI.SetNextControlName("FreezeTickField");
            freezeTickInput = GUILayout.TextField(freezeTickInput);

            if (!string.IsNullOrEmpty(freezeError))
            {
                Color previous = GUI.color;
                GUI.color = Color.red;
                GUILayout.Label(freezeError);
                GUI.color = previous;
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Apply"))
                ApplyFreezePopupSelection();
            if (GUILayout.Button("Cancel"))
                CloseFreezePopup();
            GUILayout.EndHorizontal();

            GUI.DragWindow(new Rect(0f, 0f, 10000f, 20f));
        }

        /// <summary>
        /// Parses the freeze popup input and applies the frozen status if valid.
        /// </summary>
        private void ApplyFreezePopupSelection()
        {
            freezeError = string.Empty;

            if (!int.TryParse(freezeTickInput, out int ticks) || ticks <= 0)
            {
                freezeError = "Enter a positive number of ticks.";
                return;
            }

            if (!TryApplyFreezeToPlayer(ticks))
                return;

            CloseFreezePopup();
        }

        /// <summary>
        /// Attempts to apply a freeze buff to the current player.
        /// </summary>
        private bool TryApplyFreezeToPlayer(int durationTicks)
        {
            var mover = FindObjectOfType<PlayerMover>();
            if (mover == null)
            {
                freezeError = "Could not locate the player.";
                return false;
            }

            var controller = mover.GetComponent<FrozenStatusController>();
            if (controller == null)
            {
                freezeError = "Player is missing FrozenStatusController.";
                return false;
            }

            FreezeUtility.ApplyFreezeTicks(controller.gameObject, durationTicks, BuffSourceType.Scripted, nameof(AdminF2Menu));
            return true;
        }

        /// <summary>
        /// Hides the freeze popup and clears focus so keyboard shortcuts resume working immediately.
        /// </summary>
        private void CloseFreezePopup()
        {
            showFreezePopup = false;
            freezeError = string.Empty;
            GUI.FocusControl(null);
        }
    }
}
