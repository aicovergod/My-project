using System;
using Skills;
using UnityEngine;

namespace Combat
{
    /// <summary>
    /// Centralises combat XP distribution so both the player and companion flows can
    /// share identical logic while opting into bespoke melee handling strategies.
    /// </summary>
    public static class CombatXpDistributor
    {
        /// <summary>
        /// Configures how XP is routed for a damage event.
        /// </summary>
        public struct CombatXpDistributionConfig
        {
            /// <summary>Delegate used to award hitpoints XP. Optional.</summary>
            public Action<float> AwardHitpointsXp;

            /// <summary>Delegate used to award skill XP. Optional.</summary>
            public Action<SkillType, float> AwardSkillXp;

            /// <summary>
            /// Optional handler invoked for melee XP. Return true when the handler distributed
            /// the XP to prevent the default style-based flow from running.
            /// </summary>
            public Func<MeleeXpContext, bool> MeleeXpHandler;

            /// <summary>Optional logging delegate for debugging distribution decisions.</summary>
            public Action<string> Log;

            /// <summary>
            /// Prefix injected ahead of every log message when <see cref="Log"/> is supplied.
            /// </summary>
            public string LogPrefix;
        }

        /// <summary>
        /// Context payload passed into melee handlers, exposing helpers to award XP and log
        /// debug output without duplicating boilerplate.
        /// </summary>
        public readonly struct MeleeXpContext
        {
            public MeleeXpContext(int damage, float combatXp, CombatStyle style, Action<SkillType, float> awardSkillXp, Action<string> log)
            {
                Damage = damage;
                CombatXp = combatXp;
                Style = style;
                AwardSkillXp = awardSkillXp;
                Log = log;
            }

            /// <summary>Raw damage dealt by the hit.</summary>
            public int Damage { get; }

            /// <summary>Total combat XP generated for the damage event (4x damage).</summary>
            public float CombatXp { get; }

            /// <summary>Combat style used for the attack.</summary>
            public CombatStyle Style { get; }

            /// <summary>Delegate used to award skill XP.</summary>
            public Action<SkillType, float> AwardSkillXp { get; }

            /// <summary>Delegate used to emit prefixed log output.</summary>
            public Action<string> Log { get; }
        }

        /// <summary>
        /// Distributes combat XP using OSRS-style formulas with optional melee overrides.
        /// </summary>
        /// <param name="damage">Damage dealt by the swing or spell.</param>
        /// <param name="style">Combat style used for the attack.</param>
        /// <param name="damageType">Damage category (melee, ranged, magic).</param>
        /// <param name="config">Distribution configuration.</param>
        public static void AwardXp(int damage, CombatStyle style, DamageType damageType, CombatXpDistributionConfig config)
        {
            if (damage <= 0)
                return;

            float hitpointsXp = damage * 1.33f;
            config.AwardHitpointsXp?.Invoke(hitpointsXp);
            Log(config, $"Awarded {hitpointsXp:0.##} Hitpoints XP from {damage} damage ({damageType}).");

            if (damageType == DamageType.Magic)
            {
                float magicXp = 4f * damage;
                config.AwardSkillXp?.Invoke(SkillType.Magic, magicXp);
                Log(config, $"Awarded {magicXp:0.##} Magic XP from {damage} magic damage.");
                return;
            }

            if (damageType == DamageType.Ranged)
            {
                float total = 4f * damage;
                switch (style)
                {
                    case CombatStyle.Defensive:
                    case CombatStyle.Controlled:
                    case CombatStyle.Longrange:
                        float split = total * 0.5f;
                        config.AwardSkillXp?.Invoke(SkillType.Ranged, split);
                        config.AwardSkillXp?.Invoke(SkillType.Defence, split);
                        Log(config, $"Split ranged XP ({style}) -> {split:0.##} Ranged / {split:0.##} Defence from {damage} damage.");
                        break;
                    default:
                        config.AwardSkillXp?.Invoke(SkillType.Ranged, total);
                        Log(config, $"Awarded {total:0.##} Ranged XP from {damage} ranged damage using {style} style.");
                        break;
                }

                return;
            }

            float combatXp = 4f * damage;
            if (damageType == DamageType.Melee && config.MeleeXpHandler != null)
            {
                var meleeContext = new MeleeXpContext(damage, combatXp, style, config.AwardSkillXp, message => Log(config, message));
                if (config.MeleeXpHandler.Invoke(meleeContext))
                    return;
            }

            switch (style)
            {
                case CombatStyle.Accurate:
                    config.AwardSkillXp?.Invoke(SkillType.Attack, combatXp);
                    Log(config, $"Awarded {combatXp:0.##} Attack XP from {damage} damage via Accurate style.");
                    break;
                case CombatStyle.Aggressive:
                    config.AwardSkillXp?.Invoke(SkillType.Strength, combatXp);
                    Log(config, $"Awarded {combatXp:0.##} Strength XP from {damage} damage via Aggressive style.");
                    break;
                case CombatStyle.Defensive:
                    config.AwardSkillXp?.Invoke(SkillType.Defence, combatXp);
                    Log(config, $"Awarded {combatXp:0.##} Defence XP from {damage} damage via Defensive style.");
                    break;
                case CombatStyle.Controlled:
                    float total = combatXp;
                    int share = Mathf.FloorToInt(total / 3f);
                    int remainder = Mathf.RoundToInt(total - share * 3);
                    config.AwardSkillXp?.Invoke(SkillType.Attack, share);
                    config.AwardSkillXp?.Invoke(SkillType.Strength, share);
                    config.AwardSkillXp?.Invoke(SkillType.Defence, share + remainder);
                    Log(config, $"Controlled style awarded {share} Attack, {share} Strength, {share + remainder} Defence XP from {damage} damage.");
                    break;
            }
        }

        /// <summary>
        /// Emits a log message using the configured prefix when logging is enabled.
        /// </summary>
        private static void Log(CombatXpDistributionConfig config, string message)
        {
            if (config.Log == null)
                return;

            if (string.IsNullOrEmpty(config.LogPrefix))
            {
                config.Log(message);
                return;
            }

            config.Log($"{config.LogPrefix}{message}");
        }
    }
}
