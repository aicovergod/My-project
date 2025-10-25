using System.Collections.Generic;
using UnityEngine;

namespace Companions.Conversation
{
    /// <summary>
    /// Provides flavour text for handling player apologies around skill reminder chatter.
    /// Keeps the response pools centralised so the conversation service stays focused on orchestration.
    /// </summary>
    public static class CompanionApologyDialogueLibrary
    {
        /// <summary>
        /// Responses used when the player apologises after the companion had to remind them about a skill plan.
        /// </summary>
        private static readonly string[] reminderAcknowledgementLines =
        {
            "All good, I forget stuff too sometimes.",
            "Don’t worry about it, happens to the best of us.",
            "No problem, easy mistake.",
            "It’s fine, no harm done.",
            "All good, {playerName}.",
            "Don’t sweat it, I got you.",
            "Hey, no big deal.",
            "All good, happens.",
            "Forgiven already.",
            "You're fine, I knew you'd remember eventually.",
            "No worries, I’m patient.",
            "It’s cool, just glad you remembered.",
            "About time you remembered!",
            "You sure you’re not running low on memory, {playerName}?",
            "Finally caught up, did ya?",
            "You’d forget your own tools if I didn’t remind you.",
            "I swear, I need to start writing things down for you.",
            "Memory of a goldfish, I swear.",
            "I should start charging for reminders.",
            "That’s alright, keeps me feeling useful.",
            "Ha, at least you said sorry this time.",
            "I’ll let it slide… again.",
            "Apology accepted, but I’m keeping score.",
            "You better remember next time!",
            "At this rate I’ll start calling you Forgetful {playerName}."
        };

        /// <summary>
        /// Responses used when the player apologises even though no reminder was needed.
        /// </summary>
        private static readonly string[] unpromptedApologyLines =
        {
            "Huh? What are you sorry for?",
            "Wait… what did you do?",
            "Why’re you saying sorry?",
            "Did I miss something?",
            "You good? You didn’t do anything wrong.",
            "Sorry for what exactly?",
            "You don’t need to apologise, nothing happened.",
            "I’m confused, what are you apologising for?",
            "That came out of nowhere.",
            "You’re fine, {playerName}. No reason to apologise.",
            "Already saying sorry? What did you break this time?",
            "Don’t tell me you stepped on my toes again.",
            "You practising your manners or something?",
            "That’s suspiciously polite of you.",
            "You didn’t even mess up yet!",
            "You planning to do something bad? ’Cause that sounded pre-emptive.",
            "If this is a trick to make me go easy on you, it’s working.",
            "You feeling guilty about something I don’t know?",
            "What are you plotting, saying sorry for no reason?",
            "That sorry sounded guilty as hell."
        };

        /// <summary>
        /// Picks a reminder acknowledgement line at random.
        /// </summary>
        public static string GetReminderAcknowledgementLine()
        {
            return GetRandomLine(reminderAcknowledgementLines);
        }

        /// <summary>
        /// Picks a response for unexpected apologies at random.
        /// </summary>
        public static string GetUnpromptedApologyLine()
        {
            return GetRandomLine(unpromptedApologyLines);
        }

        private static string GetRandomLine(IReadOnlyList<string> source)
        {
            if (source == null || source.Count == 0)
                return string.Empty;

            if (source.Count == 1)
                return source[0];

            int index = Random.Range(0, source.Count);
            return source[index];
        }
    }
}
