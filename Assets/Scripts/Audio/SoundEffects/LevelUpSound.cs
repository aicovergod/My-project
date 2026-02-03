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
        /// Delegate used to resolve the sound effect that should play for a given skill level.
        /// Some skills (Hitpoints/Strength) change clip depending on the milestone reached.
        /// </summary>
        /// <param name="level">Player level that triggered the event.</param>
        /// <returns>Sound effect identifier that should be played.</returns>
        private delegate SoundEffect LevelUpEffectResolver(int level);

        /// <summary>
        /// Cached lookup linking each supported skill to a resolver capable of selecting the
        /// appropriate sound effect for the new level. This keeps the logic centralised and
        /// avoids scattering conditional statements across the listener.
        /// </summary>
        private static readonly Dictionary<SkillType, LevelUpEffectResolver> SkillSoundResolvers = new()
        {
            { SkillType.Magic, _ => SoundEffect.MagicLevelUp },
            { SkillType.Attack, _ => SoundEffect.AttackLevelUp },
            { SkillType.Defence, _ => SoundEffect.DefenceLevelUp },
            { SkillType.Mining, _ => SoundEffect.MiningLevelUp },
            { SkillType.Woodcutting, _ => SoundEffect.WoodcuttingLevelUp },
            { SkillType.Fishing, _ => SoundEffect.FishingLevelUp },
            { SkillType.Cooking, _ => SoundEffect.CookingLevelUp },
            { SkillType.Thieving, _ => SoundEffect.ThievingLevelUp },
            { SkillType.Beastmaster, _ => SoundEffect.BeastmasterLevelUp },
            { SkillType.Firemaking, _ => SoundEffect.FiremakingLevelUp },
            { SkillType.Ranged, _ => SoundEffect.RangedLevelUp },
            { SkillType.Strength, level => level >= 50 ? SoundEffect.StrengthLevelUpHigh : SoundEffect.StrengthLevelUpLow },
            { SkillType.Hitpoints, level => level >= 50 ? SoundEffect.HitpointsLevelUpHigh : SoundEffect.HitpointsLevelUpLow }
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
            if (!SkillSoundResolvers.TryGetValue(type, out var resolver))
                return;

            var effect = resolver(level);
            SoundManager.Instance.PlaySfx(effect);
        }
    }
}
