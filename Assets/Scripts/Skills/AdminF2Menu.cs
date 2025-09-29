using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using Player;
using Beastmaster;
using Pets;
using NPC;
using BankSystem;
using Skills.Cooking;
using Skills.Fishing;
using Skills.Mining;
using Skills.Outfits;
using Skills.Woodcutting;
using Skills.Firemaking;
using Status;
using Status.Antifire;
using Status.Poison;
using Status.Freeze;
using World;
using UI;

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

        /// <summary>
        /// Indicates whether the Admin F2 debug menu is currently visible.
        /// External systems can query this to disable gameplay input while the
        /// menu overlays the screen.
        /// </summary>
        public static bool IsVisible => instance != null && instance.visible;

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
        private Rect freezePopupRect = new Rect(460f, 10f, 240f, 150f);
        private string freezeTickInput = "8";
        private string freezeError = string.Empty;
        private string hpLevel = "";
        private string attackLevel = "";
        private string strengthLevel = "";
        private string defenceLevel = "";
        private string magicLevel = "";
        private string miningLevel = "";
        private string woodcuttingLevel = "";
        private string firemakingLevel = "";
        private string fishingLevel = "";
        private string cookingLevel = "";
        private string beastmasterLevel = "";

        // Scroll position for the debug menu
        private Vector2 scrollPos;

        /// <summary>
        /// Indicates whether any text field inside the Admin F2 menu currently has keyboard focus.
        /// Movement systems query this so typing in the menu does not trigger gameplay input.
        /// </summary>
        public static bool HasTextInputFocus { get; private set; }

        private const string HpLevelControlName = "AdminF2Menu_HpLevel";
        private const string AttackLevelControlName = "AdminF2Menu_AttackLevel";
        private const string StrengthLevelControlName = "AdminF2Menu_StrengthLevel";
        private const string DefenceLevelControlName = "AdminF2Menu_DefenceLevel";
        private const string MagicLevelControlName = "AdminF2Menu_MagicLevel";
        private const string MiningLevelControlName = "AdminF2Menu_MiningLevel";
        private const string FishingLevelControlName = "AdminF2Menu_FishingLevel";
        private const string CookingLevelControlName = "AdminF2Menu_CookingLevel";
        private const string FiremakingLevelControlName = "AdminF2Menu_FiremakingLevel";
        private const string WoodcuttingLevelControlName = "AdminF2Menu_WoodcuttingLevel";
        private const string BeastmasterLevelControlName = "AdminF2Menu_BeastmasterLevel";
        private const string FreezeTickControlName = "AdminF2Menu_FreezeTicks";

        private MiningSkill miningSkillBehaviour;
        private WoodcuttingSkill woodcuttingSkillBehaviour;
        private FiremakingSkill firemakingSkillBehaviour;
        private FishingSkill fishingSkillBehaviour;
        private CookingSkill cookingSkillBehaviour;

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

            HasTextInputFocus = false;
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
                else
                    HasTextInputFocus = false;
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
            if (miningSkillBehaviour == null)
                miningSkillBehaviour = FindObjectOfType<MiningSkill>();
            if (woodcuttingSkillBehaviour == null)
                woodcuttingSkillBehaviour = FindObjectOfType<WoodcuttingSkill>();
            if (firemakingSkillBehaviour == null)
                firemakingSkillBehaviour = FindObjectOfType<FiremakingSkill>();
            if (fishingSkillBehaviour == null)
                fishingSkillBehaviour = FindObjectOfType<FishingSkill>();
            if (cookingSkillBehaviour == null)
                cookingSkillBehaviour = FindObjectOfType<CookingSkill>();
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

            miningSkillBehaviour = FindObjectOfType<MiningSkill>();
            woodcuttingSkillBehaviour = FindObjectOfType<WoodcuttingSkill>();
            firemakingSkillBehaviour = FindObjectOfType<FiremakingSkill>();
            fishingSkillBehaviour = FindObjectOfType<FishingSkill>();
            cookingSkillBehaviour = FindObjectOfType<CookingSkill>();

            hpLevel = skillManager != null ? skillManager.GetLevel(SkillType.Hitpoints).ToString() : "";
            attackLevel = skillManager != null ? skillManager.GetLevel(SkillType.Attack).ToString() : "";
            strengthLevel = skillManager != null ? skillManager.GetLevel(SkillType.Strength).ToString() : "";
            defenceLevel = skillManager != null ? skillManager.GetLevel(SkillType.Defence).ToString() : "";
            magicLevel = skillManager != null ? skillManager.GetLevel(SkillType.Magic).ToString() : "";
            miningLevel = skillManager != null ? skillManager.GetLevel(SkillType.Mining).ToString() : "";
            woodcuttingLevel = skillManager != null ? skillManager.GetLevel(SkillType.Woodcutting).ToString() : "";
            firemakingLevel = skillManager != null ? skillManager.GetLevel(SkillType.Firemaking).ToString() : "";
            fishingLevel = skillManager != null ? skillManager.GetLevel(SkillType.Fishing).ToString() : "";
            cookingLevel = skillManager != null ? skillManager.GetLevel(SkillType.Cooking).ToString() : "";
            beastmasterLevel = skillManager != null ? skillManager.GetLevel(SkillType.Beastmaster).ToString() : "";
        }

        private void OnGUI()
        {
            if (!visible)
            {
                HasTextInputFocus = false;
                return;
            }

            // Reset focus tracking; if any text field owns focus later in this repaint the flag will be restored.
            HasTextInputFocus = false;

            const float width = 440f;
            const float height = 480f;
            Rect area = new Rect(10f, 10f, width, height);
            GUILayout.BeginArea(area, GUI.skin.box);

            // Begin scroll view so all fields are accessible even if the window is small
            scrollPos = GUILayout.BeginScrollView(scrollPos, false, true);

            hpLevel = DrawLevelField("Hitpoints Level", HpLevelControlName, hpLevel);
            attackLevel = DrawLevelField("Attack Level", AttackLevelControlName, attackLevel);
            strengthLevel = DrawLevelField("Strength Level", StrengthLevelControlName, strengthLevel);
            defenceLevel = DrawLevelField("Defence Level", DefenceLevelControlName, defenceLevel);
            magicLevel = DrawLevelField("Magic Level", MagicLevelControlName, magicLevel);
            miningLevel = DrawLevelField("Mining Level", MiningLevelControlName, miningLevel);
            fishingLevel = DrawLevelField("Fishing Level", FishingLevelControlName, fishingLevel);
            cookingLevel = DrawLevelField("Cooking Level", CookingLevelControlName, cookingLevel);
            firemakingLevel = DrawLevelField("Firemaking Level", FiremakingLevelControlName, firemakingLevel);
            woodcuttingLevel = DrawLevelField("Woodcutting Level", WoodcuttingLevelControlName, woodcuttingLevel);
            beastmasterLevel = DrawLevelField("Beastmaster Level", BeastmasterLevelControlName, beastmasterLevel);
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

            GUILayout.Space(10f);
            GUILayout.Label("Skill Debug Logging");
            DrawSkillDebugToggle(
                "Mining Debug Logging",
                () => miningSkillBehaviour != null,
                () => miningSkillBehaviour.EnableDebugLogging,
                value => miningSkillBehaviour.EnableDebugLogging = value);
            DrawSkillDebugToggle(
                "Woodcutting Debug Logging",
                () => woodcuttingSkillBehaviour != null,
                () => woodcuttingSkillBehaviour.EnableDebugLogging,
                value => woodcuttingSkillBehaviour.EnableDebugLogging = value);
            DrawSkillDebugToggle(
                "Fishing Debug Logging",
                () => fishingSkillBehaviour != null,
                () => fishingSkillBehaviour.EnableDebugLogging,
                value => fishingSkillBehaviour.EnableDebugLogging = value);
            DrawSkillDebugToggle(
                "Cooking Debug Logging",
                () => cookingSkillBehaviour != null,
                () => cookingSkillBehaviour.EnableDebugLogging,
                value => cookingSkillBehaviour.EnableDebugLogging = value);
            DrawSkillDebugToggle(
                "Firemaking Debug Logging",
                () => firemakingSkillBehaviour != null,
                () => firemakingSkillBehaviour.EnableDebugLogging,
                value => firemakingSkillBehaviour.EnableDebugLogging = value);

            DrawNpcDamageLoggingControls();

            // Allow QA to mirror floating text popups in the console for firemaking/cooking debugging.
            bool echoFloatingText = FloatingText.DebugLogMessages;
            bool requestedEchoFloatingText = GUILayout.Toggle(echoFloatingText, "Echo Floating Text to Console");
            if (requestedEchoFloatingText != echoFloatingText)
                FloatingText.DebugLogMessages = requestedEchoFloatingText;

            bool teleportToggle = World.Minimap.DebugTeleportOnClickEnabled;
            bool requestedTeleportToggle = GUILayout.Toggle(teleportToggle, "Minimap Teleport On Click");
            if (requestedTeleportToggle != teleportToggle)
            {
                // Wire the Admin menu toggle directly into the minimap so QA can click-to-teleport while debugging.
                World.Minimap.DebugTeleportOnClickEnabled = requestedTeleportToggle;
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
                if (skillManager != null && int.TryParse(fishingLevel, out var fish))
                    skillManager.DebugSetLevel(SkillType.Fishing, fish);
                if (skillManager != null && int.TryParse(cookingLevel, out var cook))
                    skillManager.DebugSetLevel(SkillType.Cooking, cook);
                if (skillManager != null && int.TryParse(firemakingLevel, out var fire))
                    skillManager.DebugSetLevel(SkillType.Firemaking, fire);
                if (skillManager != null && int.TryParse(woodcuttingLevel, out var wood))
                    skillManager.DebugSetLevel(SkillType.Woodcutting, wood);
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

            if (GUILayout.Button("Kill Player"))
            {
                KillPlayer();
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
        ///     Renders a toggle used to control runtime debug logging for a specific skill.
        /// </summary>
        /// <param name="label">Label describing the toggle.</param>
        /// <param name="skillAvailable">Predicate that returns true when the skill component exists.</param>
        /// <param name="getValue">Delegate that retrieves the current toggle state.</param>
        /// <param name="setValue">Delegate that applies a new toggle state.</param>
        private void DrawSkillDebugToggle(string label, Func<bool> skillAvailable, Func<bool> getValue, Action<bool> setValue)
        {
            bool available = skillAvailable();
            bool previousEnabled = GUI.enabled;
            GUI.enabled = available && previousEnabled;

            bool current = available ? getValue() : false;
            bool updated = GUILayout.Toggle(current, label);

            if (available && updated != current)
                setValue(updated);

            GUI.enabled = previousEnabled;
        }

        /// <summary>
        /// Draws Admin menu controls that allow QA to toggle NPC combat damage logging at runtime.
        /// </summary>
        private void DrawNpcDamageLoggingControls()
        {
            GUILayout.Space(10f);
            GUILayout.Label("NPC Debug");

            var combatants = NpcCombatant.ActiveCombatants;
            if (combatants.Count == 0)
            {
                GUILayout.Label("No NPC combatants active.");
            }

            bool globalLoggingEnabled = NpcCombatant.GlobalDamageLoggingEnabled;
            bool requestedGlobalLogging = GUILayout.Toggle(globalLoggingEnabled, "Enable Damage Logs for All NPCs");
            if (requestedGlobalLogging != globalLoggingEnabled)
                NpcCombatant.GlobalDamageLoggingEnabled = requestedGlobalLogging;
        }

        /// <summary>
        /// Draws a labeled text field and tracks whether the control owns keyboard focus.
        /// </summary>
        /// <param name="label">Label describing the field.</param>
        /// <param name="controlName">Unique control name used to monitor focus.</param>
        /// <param name="currentValue">Current value displayed in the text field.</param>
        /// <returns>The potentially updated text entered by the player.</returns>
        private string DrawLevelField(string label, string controlName, string currentValue)
        {
            GUILayout.Label(label);
            GUI.SetNextControlName(controlName);
            string updatedValue = GUILayout.TextField(currentValue);
            if (GUI.GetNameOfFocusedControl() == controlName)
                HasTextInputFocus = true;
            return updatedValue;
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

            controller.ApplyPoison(poisonPConfig, null);
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
        /// Instantly reduces the player's hitpoints to zero, triggering the standard death flow.
        /// </summary>
        private void KillPlayer()
        {
            var target = hitpoints ?? FindObjectOfType<PlayerHitpoints>();
            if (target == null)
            {
                Debug.LogWarning("AdminF2Menu could not locate PlayerHitpoints to kill the player.");
                return;
            }

            // Clamp to zero so the respawn system receives an OnHealthChanged notification.
            target.DebugSetCurrentHp(0);
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
            GUI.SetNextControlName(FreezeTickControlName);
            freezeTickInput = GUILayout.TextField(freezeTickInput);
            if (GUI.GetNameOfFocusedControl() == FreezeTickControlName)
                HasTextInputFocus = true;

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
            HasTextInputFocus = false;
        }
    }
}
