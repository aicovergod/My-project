using UnityEngine;

namespace Companions
{
    /// <summary>
    /// Centralises the companion chat pools used by gameplay systems so flavour text stays consistent.
    /// Provides random selection helpers that gracefully fall back to default lines when pools are empty.
    /// </summary>
    public static class CompanionChatLibrary
    {
        /// <summary>Pool of flavour chat lines used when the companion cannot store additional resources.</summary>
        private static readonly string[] InventoryFullChatMessages =
        {
            "My inventory’s full, maybe we should stop by a bank.",
            "I can’t carry any more, you’re making me a pack mule!",
            "That’s it, no more room. We’ll need to unload soon.",
            "I’m out of space, want to stash some things away?",
            "My hands are full, let’s clear some room first.",
            "I can’t fit another thing in here!",
            "Everything’s full up, we need a bank trip.",
            "I’m full up, nothing else is going in this bag.",
            "Ugh, I’m packed tighter than a goblin’s purse!",
            "If I carry any more, I’ll explode <emoji=10>!",
            "That’s it, no more! I’m officially a walking chest now.",
            "I swear this bag gets smaller every day!",
            "Unless you want me dragging my feet, we’d better bank this.",
            "Inventory’s full! Maybe *you* should carry something for once.",
            "I can’t carry another pebble. Not one.",
            "Everything’s overflowing, and it’s not my fault this time!",
            "Do you secretly enjoy watching me struggle with all this loot?",
            "Full again... you and your shiny rock obsession.",
            "I’m out of space. Again. Why does this always happen?",
            "I can’t take another step like this, my shoulders hurt!",
            "That’s it, full. Maybe try throwing something out?",
            "Please tell me we’re heading to a bank soon.",
            "If I take one more thing, I’ll start dropping stuff at random.",
            "I’ve done my best, but I can’t hold any more for you.",
            "I’d carry the world for you… but it won’t fit.",
            "All full, sorry, maybe we can store it somewhere safe?",
            "I’d help if I could, but I’m at my limit.",
            "That’s everything I can manage — let’s bank the rest.",
            "Inventory full! Maybe I can wear some of it instead?",
            "My pockets are crying for mercy <emoji=06>",
            "If you hand me one more thing, it’s going straight on the floor.",
            "That’s it. My inventory has officially revolted.",
            "I think my bag just groaned.",
            "Full! I repeat, FULL! This is not a drill!"
        };

        /// <summary>Fallback message returned when the inventory full pool is empty.</summary>
        private const string InventoryFullFallbackLine = "My inventory’s full, maybe we should stop by a bank.";

        /// <summary>Pool of flavour messages emitted when the player enables companion guard mode.</summary>
        private static readonly string[] GuardModeActivationChatMessages =
        {
            "Guard mode active, I’ve got your back!",
            "Alright, time to keep you in one piece.",
            "Standing watch, nobody touches you on my watch.",
            "You swing, I’ll cover.",
            "Got it, I’ll handle the defence!",
            "I’ll protect you, promise.",
            "Guard mode? Finally, something to do!",
            "I’ll take the hits if you take the glory.",
            "Ready to shield you, boss.",
            "You focus on fighting, I’ll keep you safe.",
            "On guard! Let’s show them how it’s done.",
            "I’ve switched to protection mode, stay close.",
            "I’ll make sure nothing sneaks up on you.",
            "Don’t worry, I’ll guard you like a dragon guards gold.",
            "Activating guard stance, no one’s getting through me.",
            "Understood, defence priority set.",
            "Guard mode enabled. Let’s see them try!",
            "I’m your wall now.",
            "Time to play bodyguard, huh?",
            "You fight, I’ll block!",
            "I’ll keep the nasty ones off you.",
            "Alright, who’s first to try their luck?",
            "I’ll hold the line.",
            "You won’t fall while I’m around.",
            "Standing between you and danger.",
            "Switching to protector mode.",
            "I’ll guard you, even from yourself <emoji=03>.",
            "I’ll bite back if they touch you.",
            "I’ve got eyes everywhere, no ambushes today.",
            "Okay, fine, I’ll be the shield again!",
            "Stay behind me, I’ve got this.",
            "Guard mode ready, let’s get serious.",
            "Nobody hurts you while I’m here.",
            "Defence active. Let’s give them a bad day.",
            "I’ll make sure nothing gets through.",
            "I’m ready to intercept anything that moves.",
            "Protective instinct: engaged!",
            "Don’t make me regret this hero stuff.",
            "You swing wild, I’ll clean up behind you.",
            "Stay close, I’ll cover your blind spot.",
            "Go ahead, I’ll block for you.",
            "Guard duty? I was born for this.",
            "Brace yourself, I’m locking in.",
            "I’ll hold aggro, you do your thing!",
            "Protective mode on. Let’s roll.",
            "You do realise this means I take all the hits, right?",
            "Fine, but you owe me after this.",
            "I’ll be your sword and shield.",
            "Alright, standing guard.",
            "Frontline engaged, I’ve got your flank.",
            "Here we go, I’ll make sure you live through this.",
            "Activating guard stance. Try not to get hurt.",
            "Back-to-back, yeah?",
            "I’ll handle anything that gets too close.",
            "Let’s dance, I’ll keep the rhythm defensive.",
            "Okay, they’ll hit me first this time.",
            "I’ll guard your six!",
            "Enemies spotted, I’m on defence!",
            "Protecting you? My favourite pastime.",
            "I’ll block every hit I can.",
            "I’ll make sure no blade reaches you.",
            "Got your back, front, and sides!",
            "I’ll stand my ground, you finish them.",
            "Guard mode on, temper rising!",
            "Let’s see them get past me.",
            "I’ll shield you like a true knight.",
            "Stay near me, safer that way.",
            "Guard mode enabled, and no complaints!",
            "Shielding up,  let’s get messy.",
            "I’ll stand between you and anything stupid.",
            "Got it, defensive mode engaged!",
            "You’re under my protection now.",
            "I’ll take the front line, as usual.",
            "Guard mode… engaged and grumpy.",
            "Fine, I’ll be the wall again.",
            "I’ll protect you, even if you start the fight!",
            "I’ll hold the line until you say otherwise.",
            "I’ll intercept every hit, just don’t get cocky.",
            "You attack, I’ll defend.",
            "Ready to shield you, even from your own bad ideas.",
            "Let them come, I’m ready.",
            "I’ll stand my ground, promise.",
            "Protecting you is what I do best.",
            "I’ll block their blows so you can swing free.",
            "I’ll defend you ‘til my last hitpoint!",
            "Guard mode active. Try not to run off again.",
            "Defensive stance, let’s do this right.",
            "You’re covered. Literally.",
            "I’ll protect you, but I’m charging double <emoji=09>.",
            "Stay close, I’ll guard every step.",
            "I’ll absorb what I can, you handle the rest.",
            "Okay, bodyguard Isla reporting for duty!",
            "I’ll keep them busy while you attack.",
            "Frontline shield: online.",
            "Guess who’s your shield maiden again?",
            "I’m not just cute, I’m dangerous too!",
            "You hit hard, I guard harder.",
            "I’ll make sure no one touches a hair on your head.",
            "Guard mode? Finally, some action!",
            "Protective stance active, you’re safe with me.",
            "I’ll defend you till it’s over.",
            "I’ll keep danger at bay.",
            "Guard engaged, let’s finish this fight together.",
            "Alright, let’s guard like legends."
        };

        /// <summary>Fallback message used if the guard mode activation pool cannot supply a line.</summary>
        private const string GuardModeActivationFallbackLine = "Guard mode active, I’ve got your back!";

        /// <summary>Pool of flavour messages emitted when the player disables companion guard mode.</summary>
        private static readonly string[] GuardModeDeactivationChatMessages =
        {
            "Guard mode off, standing down.",
            "Alright, lowering my guard.",
            "Back to normal duty.",
            "Guard mode disabled, relaxing a bit now.",
            "Okay, no more defence drills.",
            "I’ll take a breather now.",
            "Standing down from guard duty.",
            "Guess I can stop shielding you now.",
            "Guard mode deactivated, finally.",
            "Alright, you’re on your own again.",
            "I’m done guarding, for now.",
            "Protection mode off, I needed the break.",
            "Standing easy, no more guarding.",
            "I’ll stop hovering now.",
            "Done being your bodyguard for the moment.",
            "Guard mode disengaged.",
            "Alright, back to chill mode.",
            "Deactivating protection, hope you’re ready.",
            "Relaxing guard stance.",
            "Guess the danger’s over.",
            "Lowering my weapon, I’m done guarding.",
            "Guard duty’s over for now.",
            "Alright, I’ll ease off the defence.",
            "Guard mode off, we survived.",
            "Okay, back to regular me.",
            "Disabling guard stance, I’m exhausted.",
            "That’s enough protecting for one day.",
            "Alright, no more shield duty.",
            "Standing down, looks like we’re safe.",
            "Protection off, finally some peace.",
            "Alright, you can fend for yourself now.",
            "Guard mode off, I’ll stop blocking everything.",
            "Deactivating defensive protocols.",
            "Okay, I’m done playing the shield.",
            "Back to neutral stance.",
            "Guard off, I need to stretch.",
            "I’m relaxing, no more guarding.",
            "Protection mode cancelled.",
            "Standing down, nice and easy.",
            "Guarding off, I’ll rest my arms.",
            "Alright, guard mode disabled.",
            "I’ll stop being your wall now.",
            "Okay, back to my usual self.",
            "Standing down from protector mode.",
            "I’m done watching your back for now.",
            "Alright, I’ll stop intercepting attacks.",
            "Guarding suspended, you’re safe.",
            "Disabling defence mode.",
            "I’ll let my guard down for a bit.",
            "Protection disengaged, finally.",
            "Guard mode off, taking a break.",
            "Alright, time to lower my defences.",
            "Switching off guard mode.",
            "Done guarding, until next time.",
            "Standing down, you’ve got this.",
            "Guard mode disabled, easy breathing now.",
            "Okay, no more bodyguarding.",
            "Disengaging defence, we’re in the clear.",
            "Guard stance relaxed, I’m done.",
            "I’ll take it easy now.",
            "Protection mode off, don’t get reckless.",
            "Standing down, I’ll watch from here.",
            "Alright, no need to guard anymore.",
            "Guard duty paused, for now.",
            "Okay, guard off, try not to die.",
            "Guarding done, we can move freely again.",
            "Disabling protection stance.",
            "I’ll lower my shield now.",
            "Guard mode off, finally.",
            "Relaxing, I’ll stop protecting you for now.",
            "Done with defence mode.",
            "Standing down, no more guarding needed.",
            "Guarding cancelled, peace at last.",
            "Okay, back to adventure mode.",
            "Protection down, I’ll rest for a second.",
            "Alright, I’ll stop blocking hits.",
            "Disabling guard state, done here.",
            "I’ll chill now, guard’s off.",
            "Guard mode turned off, nice and quiet.",
            "Standing down, you’re fine on your own.",
            "Alright, dropping my guard.",
            "Okay, no more defence stance.",
            "Back to being cute instead of useful.",
            "Guard mode deactivated, no more heroics.",
            "Alright, that’s enough guarding for today.",
            "I’ll stop hovering over you.",
            "Done shielding, time to relax.",
            "Guard duty off, don’t break anything.",
            "Alright, deactivating protection mode.",
            "Back to non-guard life.",
            "Okay, we’re safe, guard off.",
            "Guard mode off, finally I can breathe.",
            "Standing down, that’s my cue to rest.",
            "Alright, no more shield time.",
            "Defence off, good job out there.",
            "Deactivating guard role.",
            "I’ll stop blocking for a while.",
            "Protection off, feels nice to relax.",
            "Okay, we’re safe, standing down.",
            "Guard off, I’ll just watch now.",
            "Alright, we’re done with that phase.",
            "Standing down, no more hero mode.",
            "Guard mode off, ready for what’s next.",
            "Okay, I’m resting my sword arm now.",
            "Done guarding, time for a snack."
        };

        /// <summary>Fallback message used if the guard mode deactivation pool cannot supply a line.</summary>
        private const string GuardModeDeactivationFallbackLine = "Guard mode off, standing down.";

        /// <summary>Pool of flavour messages used when the companion deposits items into the bank.</summary>
        private static readonly string[] BankDepositChatMessages =
        {
            "I've just deposited it all in the bank.",
            "I've just deposited it all.",
            "All added <emoji=02>.",
            "I've added the items to the bank <emoji=41>.",
            "Items safely stored in the bank.",
            "Deposit complete, all tidy now.",
            "Everything’s been added to your bank.",
            "I’ve handled the deposit for you.",
            "All your items are safely banked.",
            "That’s everything stored away.",
            "Just dropped it all off at the bank.",
            "I didn’t steal anything… promise.",
            "All stashed away, not even one for me!",
            "Your loot’s safe. Probably.",
            "Deposited! You owe me a snack now.",
            "I threw it all in the shiny box again <emoji=43>.",
            "All banked. Try not to lose it this time.",
            "Cleaned up after you, as usual.",
            "Everything’s in, even that suspicious rock.",
            "Banked! Didn’t even break a sweat.",
            "Done and dusted!",
            "That’s all sorted, boss!",
            "Everything’s in the vault.",
            "Dropped it all off, easy job.",
            "The bank’s got it all now.",
            "Job done, bank’s full again!",
            "All packed away nice and neat!",
            "I’ll guard your riches with my life <emoji=49>.",
            "I took care of it. You can count on me.",
            "All secure, no one touches your stash but me.",
            "Stored, sealed, and safe.",
            "Your treasures are in good hands.",
            "Deposit successful. I’m a professional, you know.",
            "I’ve placed your items with utmost care <emoji=57>.",
            "The vault’s a bit heavier now!",
            "Bank balance just got shinier <emoji=15>.",
            "Your gold pile’s growing again!",
            "Deposit complete, the banker winked at me lol <emoji=02>.",
            "I love the sound of coins clinking <emoji=09>.",
            "All your treasures are tucked away nicely."
        };

        /// <summary>Fallback message returned when the bank deposit pool cannot supply a line.</summary>
        private const string BankDepositFallbackLine = "I've just deposited it all in the bank.";

        /// <summary>
        /// Returns a randomly selected inventory full chat line so repeated messages feel more natural.
        /// Falls back to a default string if the configured pool is empty or contains whitespace-only entries.
        /// </summary>
        /// <returns>Companion chat line describing the lack of free inventory space.</returns>
        public static string GetRandomInventoryFullLine()
        {
            return GetRandomLine(InventoryFullChatMessages, InventoryFullFallbackLine);
        }

        /// <summary>
        /// Returns a random guard mode activation line, falling back gracefully when the pool is empty.
        /// </summary>
        public static string GetRandomGuardActivationLine()
        {
            return GetRandomLine(GuardModeActivationChatMessages, GuardModeActivationFallbackLine);
        }

        /// <summary>
        /// Returns a random guard mode deactivation line, falling back gracefully when the pool is empty.
        /// </summary>
        public static string GetRandomGuardDeactivationLine()
        {
            return GetRandomLine(GuardModeDeactivationChatMessages, GuardModeDeactivationFallbackLine);
        }

        /// <summary>
        /// Returns a random bank deposit line so deposits feel acknowledged without repeating the same text.
        /// </summary>
        public static string GetRandomBankDepositLine()
        {
            return GetRandomLine(BankDepositChatMessages, BankDepositFallbackLine);
        }

        /// <summary>
        /// Shared helper that selects a random entry from the supplied pool while handling edge cases.
        /// </summary>
        /// <param name="pool">Array of potential chat lines.</param>
        /// <param name="fallback">Fallback line used when the pool is empty or the selected entry is invalid.</param>
        /// <returns>Randomly selected chat line with guaranteed non-empty text.</returns>
        private static string GetRandomLine(string[] pool, string fallback)
        {
            if (pool == null || pool.Length == 0)
                return fallback;

            int index = Random.Range(0, pool.Length);
            if (index < 0 || index >= pool.Length)
                return fallback;

            string message = pool[index];
            return string.IsNullOrWhiteSpace(message) ? fallback : message;
        }
    }
}
