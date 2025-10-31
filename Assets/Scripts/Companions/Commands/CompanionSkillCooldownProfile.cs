using System;
using Skills;

namespace Companions.Commands
{
    /// <summary>
    /// Describes the metadata required to manage a companion skill cooldown, including
    /// the targeted <see cref="SkillType"/>, the default cooldown duration, and the
    /// companion chat line factory that should be invoked when a cooldown rejection occurs.
    /// </summary>
    public readonly struct CompanionSkillCooldownProfile
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="CompanionSkillCooldownProfile"/> struct.
        /// </summary>
        /// <param name="skill">Skill governed by the cooldown.</param>
        /// <param name="defaultDuration">Default cooldown duration applied when starting the timer.</param>
        /// <param name="chatLineFactory">Factory that produces the cooldown chat line.</param>
        public CompanionSkillCooldownProfile(
            SkillType skill,
            TimeSpan defaultDuration,
            Func<string, int, string> chatLineFactory)
        {
            Skill = skill;
            DefaultDuration = defaultDuration;
            ChatLineFactory = chatLineFactory ?? throw new ArgumentNullException(nameof(chatLineFactory));
        }

        /// <summary>Skill governed by the cooldown.</summary>
        public SkillType Skill { get; }

        /// <summary>Default cooldown duration applied when starting the timer.</summary>
        public TimeSpan DefaultDuration { get; }

        /// <summary>Factory that produces the cooldown chat line.</summary>
        public Func<string, int, string> ChatLineFactory { get; }
    }
}
