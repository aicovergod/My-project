using UnityEngine;
using Player;
using Skills.Mining;
using Skills.Woodcutting;
using Skills.Fishing;
using Beastmaster;
using Pets;
using BankSystem;

namespace Skills
{
    /// <summary>
    /// Debug menu that allows setting player skill levels. Toggle with F2.
    /// </summary>
    [DisallowMultipleComponent]
    public class SkillsDebugMenu : MonoBehaviour
    {
        private PlayerHitpoints hitpoints;
        private SkillManager skillManager;
        private MiningSkill miningSkill;
        private WoodcuttingSkill woodcuttingSkill;
        private FishingSkill fishingSkill;
        private IBeastmasterService beastmasterService;
        private MergeConfig mergeConfig;

        private bool visible;
        private string hpLevel = "";
        private string attackLevel = "";
        private string strengthLevel = "";
        private string defenceLevel = "";
        private string miningLevel = "";
        private string woodcuttingLevel = "";
        private string fishingLevel = "";
        private string beastmasterLevel = "";

        // Scroll position for the debug menu
        private Vector2 scrollPos;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            if (UnityEngine.Object.FindObjectOfType<SkillsDebugMenu>() != null)
                return;

            var go = new GameObject("SkillsDebugMenu");
            DontDestroyOnLoad(go);
            go.AddComponent<SkillsDebugMenu>();
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
            if (miningSkill == null)
                miningSkill = FindObjectOfType<MiningSkill>();
            if (woodcuttingSkill == null)
                woodcuttingSkill = FindObjectOfType<WoodcuttingSkill>();
            if (fishingSkill == null)
                fishingSkill = FindObjectOfType<FishingSkill>();
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
            miningSkill = FindObjectOfType<MiningSkill>();
            woodcuttingSkill = FindObjectOfType<WoodcuttingSkill>();
            fishingSkill = FindObjectOfType<FishingSkill>();
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

            hpLevel = hitpoints != null ? hitpoints.Level.ToString() : "";
            attackLevel = skillManager != null ? skillManager.GetLevel(SkillType.Attack).ToString() : "";
            strengthLevel = skillManager != null ? skillManager.GetLevel(SkillType.Strength).ToString() : "";
            defenceLevel = skillManager != null ? skillManager.GetLevel(SkillType.Defence).ToString() : "";
            miningLevel = miningSkill != null ? miningSkill.Level.ToString() : "";
            woodcuttingLevel = woodcuttingSkill != null ? woodcuttingSkill.Level.ToString() : "";
            fishingLevel = fishingSkill != null ? fishingSkill.Level.ToString() : "";
            beastmasterLevel = beastmasterService != null ? beastmasterService.CurrentLevel.ToString() : "";
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

            GUILayout.Label("Mining Level");
            miningLevel = GUILayout.TextField(miningLevel);

            GUILayout.Label("Woodcutting Level");
            woodcuttingLevel = GUILayout.TextField(woodcuttingLevel);

            GUILayout.Label("Fishing Level");
            fishingLevel = GUILayout.TextField(fishingLevel);

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
                if (hitpoints != null && int.TryParse(hpLevel, out var hp))
                    hitpoints.DebugSetLevel(hp);
                if (skillManager != null && int.TryParse(attackLevel, out var atk))
                    skillManager.DebugSetLevel(SkillType.Attack, atk);
                if (skillManager != null && int.TryParse(strengthLevel, out var str))
                    skillManager.DebugSetLevel(SkillType.Strength, str);
                if (skillManager != null && int.TryParse(defenceLevel, out var def))
                    skillManager.DebugSetLevel(SkillType.Defence, def);
                if (miningSkill != null && int.TryParse(miningLevel, out var mine))
                    miningSkill.DebugSetLevel(mine);
                if (woodcuttingSkill != null && int.TryParse(woodcuttingLevel, out var wood))
                    woodcuttingSkill.DebugSetLevel(wood);
                if (fishingSkill != null && int.TryParse(fishingLevel, out var fish))
                    fishingSkill.DebugSetLevel(fish);
                if (beastmasterService != null && int.TryParse(beastmasterLevel, out var bm))
                    beastmasterService.SetLevel(Mathf.Clamp(bm, 1, 99));

                RefreshFields();
            }

            if (GUILayout.Button("Reset Merge Timer"))
            {
                PetMergeController.Instance?.ResetMergeTimer();
            }

            if (GUILayout.Button(PetDropSystem.DebugPetRolls ? "Disable Pet Roll Debug" : "Enable Pet Roll Debug"))
            {
                PetDropSystem.DebugPetRolls = !PetDropSystem.DebugPetRolls;
            }

            if (GUILayout.Button("Open Bank"))
            {
                BankUI.Instance?.Open();
            }

            if (GUILayout.Button("Clear Inventory"))
            {
                var inv = FindObjectOfType<Inventory.Inventory>();
                if (inv != null)
                {
                    for (int i = 0; i < inv.size; i++)
                        inv.ClearSlot(i);
                }
            }

            GUILayout.EndScrollView();

            GUILayout.EndArea();
        }
    }
}
