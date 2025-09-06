using UnityEngine;
using Player;
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
            miningLevel = skillManager != null ? skillManager.GetLevel(SkillType.Mining).ToString() : "";
            woodcuttingLevel = skillManager != null ? skillManager.GetLevel(SkillType.Woodcutting).ToString() : "";
            fishingLevel = skillManager != null ? skillManager.GetLevel(SkillType.Fishing).ToString() : "";
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
                if (skillManager != null && int.TryParse(miningLevel, out var mine))
                    skillManager.DebugSetLevel(SkillType.Mining, mine);
                if (skillManager != null && int.TryParse(woodcuttingLevel, out var wood))
                    skillManager.DebugSetLevel(SkillType.Woodcutting, wood);
                if (skillManager != null && int.TryParse(fishingLevel, out var fish))
                    skillManager.DebugSetLevel(SkillType.Fishing, fish);
                if (skillManager != null && int.TryParse(beastmasterLevel, out var bm))
                {
                    skillManager.DebugSetLevel(SkillType.Beastmaster, bm);
                    beastmasterService?.SetLevel(Mathf.Clamp(bm, 1, 99));
                }

                RefreshFields();
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
        }
    }
}
