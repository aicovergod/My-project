using Skills;

namespace Companions.Chat
{
    /// <summary>
    /// Centralises companion chat reactions for key gameplay events so systems can
    /// surface flavour text without duplicating the publishing logic.
    /// </summary>
    public static class CompanionChatEventService
    {
        /// <summary>
        /// Publishes a greeting when the companion automatically respawns after a login.
        /// Helps returning players feel acknowledged without requiring manual interaction.
        /// </summary>
        public static void PublishAutoSpawnGreeting()
        {
            CompanionChatPublisher.TryPublish(
                CompanionChatLibrary.GetRandomAutoSpawnGreetingLine,
                requireActiveCompanion: true);
        }

        /// <summary>
        /// Publishes a random guard mode activation message to the companion chat channel.
        /// Ensures enabling guard mode feels responsive without spamming when toggled off.
        /// </summary>
        public static void PublishRandomGuardModeActivationMessage()
        {
            CompanionChatPublisher.TryPublish(CompanionChatLibrary.GetRandomGuardActivationLine);
        }

        /// <summary>
        /// Publishes a random guard mode deactivation message when the player disables guard mode.
        /// Keeps flavourful feedback flowing even as the companion relaxes from defence duty.
        /// </summary>
        public static void PublishRandomGuardModeDeactivationMessage()
        {
            CompanionChatPublisher.TryPublish(CompanionChatLibrary.GetRandomGuardDeactivationLine);
        }

        /// <summary>
        /// Publishes a random chat line whenever the companion is freshly spawned by the player.
        /// Adds flavour to manual summons triggered by dropping the companion’s charm item.
        /// </summary>
        public static void PublishRandomManualSpawnMessage()
        {
            CompanionChatPublisher.TryPublish(CompanionChatLibrary.GetRandomManualSpawnGreetingLine);
        }

        /// <summary>
        /// Publishes a random farewell line when the player manually stores their companion.
        /// Keeps the pickup action flavourful so the companion acknowledges being dismissed.
        /// </summary>
        public static void PublishRandomManualStoreMessage()
        {
            CompanionChatPublisher.TryPublish(CompanionChatLibrary.GetRandomManualStoreLine);
        }

        /// <summary>
        /// Broadcasts a companion-channel chat message whenever the active companion levels a skill.
        /// </summary>
        /// <param name="skill">Skill that gained a level.</param>
        /// <param name="level">Resulting companion level.</param>
        /// <param name="possessivePronoun">
        /// Possessive pronoun preferred by the companion. When null or whitespace the message
        /// falls back to a neutral pronoun so the sentence stays grammatically correct.
        /// </param>
        public static void PublishCompanionLevelUpMessage(SkillType skill, int level, string possessivePronoun)
        {
            CompanionChatPublisher.TryPublish(() => ResolveLevelUpLine(skill, level, possessivePronoun));
        }

        /// <summary>
        /// Resolves the appropriate level-up line for the supplied skill using the shared library helpers.
        /// </summary>
        private static string ResolveLevelUpLine(SkillType skill, int level, string possessivePronoun)
        {
            switch (skill)
            {
                case SkillType.Hitpoints:
                    return CompanionChatLibrary.GetRandomHitpointsLevelUpLine();
                case SkillType.Defence:
                    return CompanionChatLibrary.GetRandomDefenceLevelUpLine();
                case SkillType.Strength:
                    return CompanionChatLibrary.GetRandomStrengthLevelUpLine();
                case SkillType.Attack:
                    return CompanionChatLibrary.GetRandomAttackLevelUpLine();
                case SkillType.Ranged:
                    return CompanionChatLibrary.GetRandomRangedLevelUpLine();
                case SkillType.Magic:
                    return CompanionChatLibrary.GetRandomMagicLevelUpLine();
                case SkillType.Beastmaster:
                    return CompanionChatLibrary.GetRandomBeastmasterLevelUpLine();
                case SkillType.Fishing:
                    return CompanionChatLibrary.GetRandomFishingLevelUpLine();
                case SkillType.Cooking:
                    return CompanionChatLibrary.GetRandomCookingLevelUpLine();
                case SkillType.Firemaking:
                    return CompanionChatLibrary.GetRandomFiremakingLevelUpLine();
                case SkillType.Woodcutting:
                    return CompanionChatLibrary.GetRandomWoodcuttingLevelUpLine();
                case SkillType.Mining:
                    return CompanionChatLibrary.GetRandomMiningLevelUpLine();
                default:
                    string pronoun = SanitizePronoun(possessivePronoun);
                    string skillName = SkillNameUtility.GetSentenceName(skill);
                    return CompanionChatLibrary.BuildGenericLevelUpLine(pronoun, skillName, level);
            }
        }

        /// <summary>
        /// Normalises the supplied pronoun so generic level-up messaging stays polished.
        /// </summary>
        private static string SanitizePronoun(string possessivePronoun)
        {
            if (string.IsNullOrWhiteSpace(possessivePronoun))
                return "their";

            return possessivePronoun.Trim().ToLowerInvariant();
        }
    }
}
