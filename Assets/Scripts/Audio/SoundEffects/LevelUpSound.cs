using System.Collections.Generic;
using Skills;
using UnityEngine;

namespace Audio.SoundEffects
{
    /// <summary>
    /// Listens for skill level up events and plays the matching sound effect. Separating this
    /// from the combat controller keeps audio handling modular so future systems can reuse the
    /// behaviour without depending on combat specific code.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LevelUpSound : MonoBehaviour
    {
        /// <summary>
        /// Cached lookup linking each supported skill to its corresponding sound effect.
        /// </summary>
        private static readonly Dictionary<SkillType, SoundEffect> SkillSoundMap = new()
        {
            { SkillType.Magic, SoundEffect.MagicLevelUp },
            { SkillType.Attack, SoundEffect.AttackLevelUp },
            { SkillType.Defence, SoundEffect.DefenceLevelUp },
            { SkillType.Mining, SoundEffect.MiningLevelUp },
            { SkillType.Woodcutting, SoundEffect.WoodcuttingLevelUp },
            { SkillType.Fishing, SoundEffect.FishingLevelUp },
            { SkillType.Cooking, SoundEffect.CookingLevelUp },
            { SkillType.Thieving, SoundEffect.ThievingLevelUp },
            { SkillType.Beastmaster, SoundEffect.BeastmasterLevelUp }
        };

        [SerializeField]
        [Tooltip("Skill manager that raises level change events. If unset the component searches the hierarchy.")]
        private SkillManager skillManager;

        private void Awake()
        {
            // Allow designers to either wire the dependency manually or rely on the automatic
            // hierarchy search so this component can sit alongside or above the SkillManager.
            if (skillManager == null)
            {
                skillManager = GetComponent<SkillManager>()
                    ?? GetComponentInParent<SkillManager>()
                    ?? GetComponentInChildren<SkillManager>();

                if (skillManager == null)
                {
                    // Fallback to a scene-wide lookup so the player manager can still be
                    // resolved when it lives outside this object's hierarchy (e.g. when the
                    // audio component is on a persistent object).
                    skillManager = FindFirstObjectByType<SkillManager>(FindObjectsInactive.Include);
                }
            }

            if (skillManager != null)
            {
                skillManager.LevelChanged += OnSkillLevelChanged;
            }
            else
            {
                Debug.LogWarning(
                    "LevelUpSound could not locate a SkillManager; level up sound effects will not play.",
                    this);
            }
        }

        private void OnDestroy()
        {
            if (skillManager != null)
            {
                skillManager.LevelChanged -= OnSkillLevelChanged;
            }
        }

        /// <summary>
        /// Plays the appropriate level up sound if the skill is mapped to an effect. Skills that
        /// do not have bespoke chimes simply fall through without triggering audio.
        /// </summary>
        private static void OnSkillLevelChanged(SkillType type, int level)
        {
            if (!SkillSoundMap.TryGetValue(type, out var effect))
                return;

            SoundManager.Instance.PlaySfx(effect);
        }
    }
}
