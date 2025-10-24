using System;
using System.Collections.Generic;
using UnityEngine;
using Player;
using Companions;
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
using UI.Chat;

namespace Skills
{
    /// <summary>
    /// Debug menu that allows setting player skill levels. Toggle with F2.
    /// </summary>
    [DisallowMultipleComponent]
    public class AdminF2Menu : SceneGatedSingletonBehaviour<AdminF2Menu>
    {
        public static AdminF2Menu Instance => SceneGatedSingletonBehaviour<AdminF2Menu>.Instance;

        /// <summary>
        /// Indicates whether the Admin F2 debug menu is currently visible.
        /// External systems can query this to disable gameplay input while the
        /// menu overlays the screen.
        /// </summary>
        public static bool IsVisible => Instance != null && Instance.visible;

        private PlayerHitpoints hitpoints;
        private SkillManager playerSkillManager;
        private SkillManager companionSkillManager;
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
        [SerializeField]
        [Tooltip("Serialized so QA can stage Ranged level overrides directly in the inspector when debugging scenes.")]
        private string rangedLevel = "";
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
        private const string RangedLevelControlName = "AdminF2Menu_RangedLevel";
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
            BootstrapSingleton(CreateInstance);
        }

        private static AdminF2Menu CreateInstance()
        {
            var go = new GameObject(nameof(AdminF2Menu));
            return go.AddComponent<AdminF2Menu>();
        }

        protected override void OnSingletonAwake()
        {
            visible = false;
        }

        protected override void OnSingletonDestroyed()
        {
            HasTextInputFocus = false;
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
            if (playerSkillManager == null)
                playerSkillManager = ResolvePlayerSkillManager();
            companionSkillManager = CompanionManager.CompanionSkills;
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
            playerSkillManager = ResolvePlayerSkillManager();
            companionSkillManager = CompanionManager.CompanionSkills;
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

            hpLevel = playerSkillManager != null ? playerSkillManager.GetLevel(SkillType.Hitpoints).ToString() : "";
            attackLevel = playerSkillManager != null ? playerSkillManager.GetLevel(SkillType.Attack).ToString() : "";
            strengthLevel = playerSkillManager != null ? playerSkillManager.GetLevel(SkillType.Strength).ToString() : "";
            defenceLevel = playerSkillManager != null ? playerSkillManager.GetLevel(SkillType.Defence).ToString() : "";
            rangedLevel = playerSkillManager != null ? playerSkillManager.GetLevel(SkillType.Ranged).ToString() : "";
            magicLevel = playerSkillManager != null ? playerSkillManager.GetLevel(SkillType.Magic).ToString() : "";
            miningLevel = playerSkillManager != null ? playerSkillManager.GetLevel(SkillType.Mining).ToString() : "";
            woodcuttingLevel = playerSkillManager != null ? playerSkillManager.GetLevel(SkillType.Woodcutting).ToString() : "";
            firemakingLevel = playerSkillManager != null ? playerSkillManager.GetLevel(SkillType.Firemaking).ToString() : "";
            fishingLevel = playerSkillManager != null ? playerSkillManager.GetLevel(SkillType.Fishing).ToString() : "";
            cookingLevel = playerSkillManager != null ? playerSkillManager.GetLevel(SkillType.Cooking).ToString() : "";
            beastmasterLevel = playerSkillManager != null ? playerSkillManager.GetLevel(SkillType.Beastmaster).ToString() : "";
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
            rangedLevel = DrawLevelField("Ranged Level", RangedLevelControlName, rangedLevel);
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

            bool companionDebugLogging = CompanionManager.EnableDebugLogging;
            bool requestedCompanionDebugLogging = GUILayout.Toggle(companionDebugLogging, "Companion Debug Logging");
            if (requestedCompanionDebugLogging != companionDebugLogging)
                CompanionManager.EnableDebugLogging = requestedCompanionDebugLogging;

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

            bool previousGuiEnabled = GUI.enabled;
            GUI.enabled = playerSkillManager != null;
            if (GUILayout.Button("Apply To Player"))
            {
                ApplySkillOverrides(playerSkillManager, hitpoints, beastmasterService);
                RefreshFields();
            }
            if (GUILayout.Button("Reset Stats For Player"))
            {
                ResetStatsToBaseline(playerSkillManager, hitpoints, beastmasterService);
                RefreshFields();
            }
            GUI.enabled = previousGuiEnabled;

            previousGuiEnabled = GUI.enabled;
            GUI.enabled = companionSkillManager != null;
            if (GUILayout.Button("Apply To Companion"))
            {
                ApplySkillOverrides(companionSkillManager, null, null);
                RefreshFields();
            }
            if (GUILayout.Button("Reset Stats For Companion"))
            {
                ResetStatsToBaseline(companionSkillManager, null, null);
                RefreshFields();
            }
            GUI.enabled = previousGuiEnabled;

            bool cooldownGuiEnabled = GUI.enabled;
            GUI.enabled = CompanionManager.CompanionSkillCooldowns != null;
            if (GUILayout.Button("Clear Companion Cooldown Timers"))
                ClearCompanionCooldownsFromMenu();
            GUI.enabled = cooldownGuiEnabled;

            if (GUILayout.Button("Max Stats"))
            {
                if (playerSkillManager != null)
                {
                    foreach (SkillType type in Enum.GetValues(typeof(SkillType)))
                        playerSkillManager.DebugSetLevel(type, 99);
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

            DrawActiveOutfitDefinitions();

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
        ///     Lists the skilling outfit definitions currently registered at runtime.
        ///     Helps QA verify which outfits are active after the move to ScriptableObjects.
        /// </summary>
        private void DrawActiveOutfitDefinitions()
        {
            GUILayout.Space(5f);
            GUILayout.Label("Skilling Outfits");

            var trackers = SkillingOutfitProgress.ActiveProgressTrackers;
            if (trackers == null || trackers.Count == 0)
            {
                GUILayout.Label("  No active outfit trackers registered.");
                return;
            }

            var seenDefinitions = new HashSet<SkillingOutfitDefinition>();
            bool missingDefinitionReported = false;

            foreach (var tracker in trackers)
            {
                if (tracker == null)
                    continue;

                var definition = tracker.Definition;
                if (definition == null)
                {
                    if (!missingDefinitionReported)
                    {
                        GUILayout.Label("  Outfit tracker missing definition reference.");
                        missingDefinitionReported = true;
                    }

                    continue;
                }

                if (!seenDefinitions.Add(definition))
                    continue;

                int ownedCount = tracker.owned != null ? tracker.owned.Count : 0;
                int totalCount = tracker.AllPieceIds != null ? tracker.AllPieceIds.Count : 0;
                if (totalCount <= 0 && definition.PieceItemIds != null)
                    totalCount = definition.PieceItemIds.Count;

                GUILayout.Label($"  {definition.DisplayName} ({ownedCount}/{Mathf.Max(0, totalCount)})");

                if (!string.IsNullOrEmpty(definition.SaveKey))
                    GUILayout.Label($"    Save Key: {definition.SaveKey}");

                var metadataParts = new List<string>(2);
                if (!string.IsNullOrEmpty(definition.AssociatedPetId))
                    metadataParts.Add($"Pet: {definition.AssociatedPetId}");
                if (!string.IsNullOrEmpty(definition.BonusDescription))
                    metadataParts.Add(definition.BonusDescription);

                if (metadataParts.Count > 0)
                    GUILayout.Label($"    {string.Join(" • ", metadataParts)}");
            }
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
        /// Clears all active companion cooldown timers when requested from the debug UI.
        /// </summary>
        private void ClearCompanionCooldownsFromMenu()
        {
            var tracker = CompanionManager.CompanionSkillCooldowns;
            if (tracker == null)
            {
                PublishAdminMessage("Companion cooldown tracker is not available.");
                return;
            }

            int cleared = tracker.ClearAllCooldowns();
            if (cleared <= 0)
            {
                PublishAdminMessage("Companion has no active skill cooldown timers.");
                return;
            }

            string message = cleared == 1
                ? "Cleared 1 companion skill cooldown timer."
                : $"Cleared {cleared} companion skill cooldown timers.";
            PublishAdminMessage(message);
        }

        /// <summary>
        /// Publishes admin feedback to the in-game chat (falling back to the console when chat is unavailable).
        /// </summary>
        private static void PublishAdminMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            var chat = ChatService.Instance;
            if (chat != null)
            {
                chat.PublishGameMessage(message);
            }
            else
            {
                Debug.Log(message);
            }
        }

        /// <summary>
        /// Resolves the player-controlled <see cref="SkillManager"/> so manual overrides do not impact companions.
        /// </summary>
        private SkillManager ResolvePlayerSkillManager()
        {
            if (hitpoints != null)
            {
                var playerManager = hitpoints.GetComponent<SkillManager>();
                if (playerManager != null)
                    return playerManager;
            }

            var playerHitpoints = FindObjectOfType<PlayerHitpoints>();
            if (playerHitpoints != null)
            {
                hitpoints = playerHitpoints;
                var playerManager = playerHitpoints.GetComponent<SkillManager>();
                if (playerManager != null)
                    return playerManager;
            }

            var playerObject = GameObject.FindGameObjectWithTag("Player");
            if (playerObject != null)
            {
                var playerManager = playerObject.GetComponent<SkillManager>();
                if (playerManager != null)
                    return playerManager;
            }

            return null;
        }

        /// <summary>
        /// Applies the currently entered skill overrides to the supplied skill manager instance.
        /// </summary>
        /// <param name="targetSkillManager">Target skill manager that should receive the overrides.</param>
        /// <param name="targetHitpoints">Optional hitpoints component used to clamp HP to the new maximum.</param>
        /// <param name="targetBeastmasterService">Optional beastmaster bridge that mirrors the Beastmaster level.</param>
        private void ApplySkillOverrides(
            SkillManager targetSkillManager,
            PlayerHitpoints targetHitpoints,
            IBeastmasterService targetBeastmasterService)
        {
            if (targetSkillManager == null)
                return;

            if (int.TryParse(hpLevel, out var hp))
            {
                targetSkillManager.DebugSetLevel(SkillType.Hitpoints, hp);
                if (targetHitpoints != null)
                    targetHitpoints.DebugSetCurrentHp(Mathf.Min(targetHitpoints.CurrentHp, targetHitpoints.MaxHp));
            }

            if (int.TryParse(attackLevel, out var atk))
                targetSkillManager.DebugSetLevel(SkillType.Attack, atk);
            if (int.TryParse(strengthLevel, out var str))
                targetSkillManager.DebugSetLevel(SkillType.Strength, str);
            if (int.TryParse(defenceLevel, out var def))
                targetSkillManager.DebugSetLevel(SkillType.Defence, def);
            if (int.TryParse(rangedLevel, out var rng))
                targetSkillManager.DebugSetLevel(SkillType.Ranged, rng);
            if (int.TryParse(magicLevel, out var mag))
                targetSkillManager.DebugSetLevel(SkillType.Magic, mag);
            if (int.TryParse(miningLevel, out var mine))
                targetSkillManager.DebugSetLevel(SkillType.Mining, mine);
            if (int.TryParse(fishingLevel, out var fish))
                targetSkillManager.DebugSetLevel(SkillType.Fishing, fish);
            if (int.TryParse(cookingLevel, out var cook))
                targetSkillManager.DebugSetLevel(SkillType.Cooking, cook);
            if (int.TryParse(firemakingLevel, out var fire))
                targetSkillManager.DebugSetLevel(SkillType.Firemaking, fire);
            if (int.TryParse(woodcuttingLevel, out var wood))
                targetSkillManager.DebugSetLevel(SkillType.Woodcutting, wood);
            if (int.TryParse(beastmasterLevel, out var bm))
            {
                targetSkillManager.DebugSetLevel(SkillType.Beastmaster, bm);
                targetBeastmasterService?.SetLevel(Mathf.Clamp(bm, 1, 99));
            }
        }

        /// <summary>
        /// Resets the supplied skill manager to the standard baseline levels used for new characters.
        /// Hitpoints returns to 10 while all other skills revert to level 1.
        /// </summary>
        /// <param name="targetSkillManager">Skill manager to reset.</param>
        /// <param name="targetHitpoints">Optional hitpoints component so current HP can be clamped to the new maximum.</param>
        /// <param name="targetBeastmasterService">Optional Beastmaster bridge to keep the Beastmaster service in sync.</param>
        private void ResetStatsToBaseline(
            SkillManager targetSkillManager,
            PlayerHitpoints targetHitpoints,
            IBeastmasterService targetBeastmasterService)
        {
            if (targetSkillManager == null)
                return;

            foreach (SkillType skill in Enum.GetValues(typeof(SkillType)))
            {
                int baselineLevel = GetBaselineLevel(skill);
                targetSkillManager.DebugSetLevel(skill, baselineLevel);

                if (skill == SkillType.Beastmaster)
                    targetBeastmasterService?.SetLevel(Mathf.Clamp(baselineLevel, 1, 99));
            }

            if (targetHitpoints != null)
                targetHitpoints.DebugSetCurrentHp(targetHitpoints.MaxHp);
        }

        /// <summary>
        /// Provides the baseline level that new characters should use for a given skill.
        /// </summary>
        /// <param name="skill">Skill type being queried.</param>
        /// <returns>Level that represents the baseline for the supplied skill.</returns>
        private static int GetBaselineLevel(SkillType skill)
        {
            switch (skill)
            {
                case SkillType.Hitpoints:
                    return 10;
                default:
                    return 1;
            }
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
