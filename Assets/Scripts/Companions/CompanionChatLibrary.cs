using System;
using System.Globalization;
using Inventory;
using UnityEngine;

namespace Companions
{
    /// <summary>
    /// Describes the tonal variants companions can use when reacting to player events.
    /// Snarky remains the classic banter while Supportive aims to comfort the player.
    /// </summary>
    public enum CompanionChatTone
    {
        /// <summary>Represents the default teasing/snarky delivery used historically.</summary>
        Snarky = 0,

        /// <summary>Represents an empathetic and supportive delivery for sensitive moments.</summary>
        Supportive = 1
    }

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

        /// <summary>Pool of chat lines used when an equipment attempt fails because of a single unmet skill requirement.</summary>
        private static readonly string[] CompanionEquipmentSingleRequirementFailureChatMessages =
        {
            "I don't have the required skill to equip that.",
            "I need a bit more training before I can wear that.",
            "That equipment is still above my current skill.",
            "I am missing the skill needed to use that.",
            "I have to level up before I can equip that."
        };

        /// <summary>Pool of chat lines used when an equipment attempt fails because of multiple unmet skill requirements.</summary>
        private static readonly string[] CompanionEquipmentMultipleRequirementFailureChatMessages =
        {
            "I don't have the required skills to equip that.",
            "That gear needs more training than I have right now.",
            "I am missing a few skills to make that work.",
            "I need several skill boosts before I can wear that.",
            "I lack the skills needed to equip that."
        };

        /// <summary>Pool of floating-text templates for single requirement failures.</summary>
        private static readonly string[] CompanionEquipmentSingleRequirementFailureFloatingTextTemplates =
        {
            "{companionName} needs {requirement}.",
            "{companionName} needs {level} {skill}.",
            "Requires {level} {skill} for {companionName}.",
            "{companionName} must reach {level} {skill}.",
            "{companionName} still needs {requirement}."
        };

        /// <summary>Pool of floating-text templates for multi requirement failures.</summary>
        private static readonly string[] CompanionEquipmentMultipleRequirementFailureFloatingTextTemplates =
        {
            "{companionName} lacks the requirements.",
            "{companionName} doesn't meet all requirements.",
            "{companionName} is missing multiple requirements.",
            "{companionName} needs more training overall.",
            "{companionName} must meet all requirements first."
        };

        /// <summary>Pool of floating-text templates used when the equipped stack has reached its limit.</summary>
        private static readonly string[] CompanionEquipmentStackLimitFloatingTextTemplates =
        {
            "{companionName} can't equip more {itemName}.",
            "Stack limit reached on {itemName} for {companionName}.",
            "No more room for {itemName} on {companionName}.",
            "{companionName} is capped on {itemName}, {playerName}.",
            "{itemName} stack is maxed for {companionName}."
        };

        /// <summary>Pool of floating-text templates used when the companion's equipment slots are full.</summary>
        private static readonly string[] CompanionEquipmentInventoryFullFloatingTextTemplates =
        {
            "{companionName} can't hold anything else.",
            "All slots are packed for {companionName}.",
            "{playerName}, {companionName} has no space left.",
            "{companionName}'s loadout is full.",
            "No room left—{companionName} is maxed out."
        };

        /// <summary>Fallback message returned when the stack-limit floating-text pool is empty.</summary>
        private const string CompanionEquipmentStackLimitFloatingTextFallback = "{companionName} can't equip more {itemName}.";

        /// <summary>Fallback message returned when the equipment inventory full floating-text pool is empty.</summary>
        private const string CompanionEquipmentInventoryFullFloatingTextFallback = "{companionName} can't hold anything else.";

        /// <summary>Fallback message returned when the inventory full pool is empty.</summary>
        private const string InventoryFullFallbackLine = "My inventory’s full, maybe we should stop by a bank.";

        /// <summary>Pool of chat lines used when the player's inventory is full but the companion still has space.</summary>
        private static readonly string[] PlayerInventoryFullChatMessages =
        {
            "Your bag looks heavier than you right now",
            "I think you just ran out of room again",
            "Inventory full already that was fast",
            "You could open a shop with all that junk",
            "I told you to stop picking up everything shiny",
            "Your bag is officially at capacity",
            "Looks like we are out of space boss",
            "You are carrying more than a dragon hoard",
            "Your inventory just tapped out",
            "I think your backpack is crying",
            "We are full no more looting for now",
            "You could build a house with all that clutter",
            "Your inventory has reached critical mass",
            "I think I heard your bag groan",
            "You are hoarding like we will never see civilization again",
            "Inventory full might be time for a bank run",
            "Your bag cannot take any more",
            "You are one item away from bursting",
            "Looks like you hit the weight limit again",
            "Your inventory is saying please stop",
            "You might need a second backpack",
            "Everything is full you did it again",
            "I cannot even hand you a pebble right now",
            "You are officially overpacked",
            "Inventory limit reached congratulations collector",
            "You could sell half this stuff and still have too much",
            "I think we are carrying a whole mountain now",
            "Inventory full time to get organized",
            "Your bag looks ready to explode",
            "You should probably visit the bank soon",
            "Everything is full again you never learn",
            "I am amazed that bag still closes",
            "Inventory packed to the brim again",
            "You could open your own general store",
            "I do not think another coin could fit",
            "Your bag is one apple away from rebellion",
            "Inventory full what a surprise",
            "Your pockets are heavier than your sword",
            "You are carrying more items than common sense",
            "That was the last thing that would fit",
            "Your inventory just waved a white flag",
            "Maybe drop something before it drops you",
            "I think your shoulders just gave up",
            "Inventory maxed out as usual",
            "You have officially reached hoarder status",
            "Your backpack looks like a black hole now",
            "That bag is one sneeze away from spilling",
            "You are running out of room fast",
            "Inventory full again already",
            "Do you plan on carrying the world with you",
            "You have no more space not even for dreams",
            "Your bag has seen things it should not have",
            "Inventory full your poor spine",
            "Time for a little spring cleaning maybe",
            "You could craft a kingdom out of all that stuff",
            "Inventory limit hit again surprise surprise",
            "Your bag looks like it is begging for mercy",
            "You cannot fit another leaf in there",
            "Inventory full guess it is organizing time",
            "You could start charging entry to your backpack",
            "That was the final item it can handle",
            "You are one step away from being buried in loot",
            "Inventory at maximum nice collection though",
            "You are making gravity work overtime",
            "Your bag says please no more",
            "Inventory full time to stash some things",
            "You are collecting everything that sparkles again",
            "Your inventory has officially given up",
            "You might want to sort before you suffocate it",
            "Inventory full that was impressive",
            "You are treating your bag like a storage spell",
            "Your inventory reached peak chaos",
            "Even the air cannot fit in there now",
            "Inventory full and still picking up rocks I see",
            "You could build a fortress from your loot pile",
            "Your bag looks like it is screaming for help",
            "Inventory completely stuffed again",
            "You are one step away from being crushed by your stuff",
            "Inventory full maybe let something go",
            "You collect like it is a competitive sport",
            "Your backpack has evolved into a small planet",
            "Inventory full that bag deserves a medal",
            "You have officially reached maximum clutter",
            "Inventory at limit again classic you",
            "You are carrying enough to fill a vault",
            "Your inventory hit the ceiling again",
            "You are walking storage at this point",
            "Inventory full my condolences",
            "You are out of space time to unload",
            "Your backpack just retired from service",
            "Inventory overflowing as expected",
            "You could start selling items at this rate",
            "You have more junk than an entire town",
            "Inventory completely maxed congratulations",
            "Your bag is now a legend of burden",
            "You filled it to the last pixel of space",
            "Inventory full again you talent for that amazes me",
            "You might need a wagon soon",
            "Your inventory could fund a small army",
            "You have mastered the art of running out of space",
            "Inventory full you win again collector",
            "You should build a bank branch in your pocket",
            "Your bag just tapped out of existence",
            "Inventory full maybe leave a rock or two behind",
            "You are carrying more than a merchant caravan",
            "Inventory full again maybe just stop picking things up",
            "Your backpack is the final boss of storage",
            "You have created a museum in your inventory",
            "Inventory completely full as always",
            "You could make a treasure map out of your bag’s contents",
            "Your pockets have declared independence",
            "Inventory filled to perfection as usual",
            "You have perfected the art of collecting clutter",
            "Inventory full I am impressed and concerned",
            "Your bag officially hates you now",
            "You cannot even fit a feather in there",
            "Inventory at maximum once again",
            "Your obsession with loot is inspiring",
            "Inventory full time to go banking again",
            "You are carrying enough to build civilization",
            "Inventory full your pack has no mercy left",
            "Maybe stop picking up every shiny rock next time",
            "Inventory full but at least you are consistent",
            "You have filled every slot bravo",
            "Inventory full again and I am not surprised",
            "You are officially the champion of hoarding",
            "Inventory at limit your pack might revolt soon",
            "Your bag has reached its emotional limit too",
            "Inventory full I warned you",
            "You cannot move another inch like that",
            "Inventory full good luck carrying all that",
            "You are carrying an entire armory again",
            "Inventory full another fine mess",
            "You filled every slot in record time"
        };

        /// <summary>Fallback message returned when a single requirement failure chat pool is empty.</summary>
        private const string CompanionEquipmentSingleRequirementFailureChatFallbackLine = "I don't have the required skill to equip that.";

        /// <summary>Fallback message returned when a multiple requirement failure chat pool is empty.</summary>
        private const string CompanionEquipmentMultipleRequirementFailureChatFallbackLine = "I don't have the required skills to equip that.";

        /// <summary>Fallback message returned when the single requirement floating-text pool is empty.</summary>
        private const string CompanionEquipmentSingleRequirementFailureFloatingTextFallback = "{companionName} needs {requirement}.";

        /// <summary>Fallback message returned when the multiple requirement floating-text pool is empty.</summary>
        private const string CompanionEquipmentMultipleRequirementFailureFloatingTextFallback = "{companionName} lacks the requirements.";

        /// <summary>Fallback message returned when the player inventory full pool is empty.</summary>
        private const string PlayerInventoryFullFallbackLine = "Your bag looks heavier than you right now";

        /// <summary>Pool of chat lines used when both the player and companion inventories are full.</summary>
        private static readonly string[] PlayerAndCompanionInventoryFullChatMessages =
        {
            "Well this is awkward we are both full",
            "Neither of us can carry a pebble right now",
            "I think we just invented maximum clutter",
            "Great we are officially out of pockets",
            "You are full I am full what now genius",
            "My bag is bursting and yours is no better",
            "We are a walking storage disaster",
            "Both of us are packed tighter than barrels",
            "Your bag is full mine is screaming",
            "Looks like we both need a trip to the bank",
            "We could build a house out of all this loot",
            "You filled me up again now we are both stuck",
            "I told you to stop picking up everything shiny",
            "Now we are both walking treasure chests",
            "We have reached peak hoarder status",
            "Neither of us can even bend over anymore",
            "I cannot even carry your problems right now",
            "Everything is full congratulations",
            "We are officially too rich to move",
            "Both inventories at max this is talent",
            "I think even the air weighs something now",
            "We could use a wagon or two at this point",
            "Your backpack looks worse than mine",
            "I cannot fit a leaf in here",
            "You filled us both like overstuffed pastries",
            "Everything is full we are done looting",
            "Looks like we broke the storage system again",
            "Both of us are carrying half the world",
            "My bag says no yours says louder no",
            "We are both bursting at the seams",
            "You really outdid yourself this time",
            "I am out of space and patience",
            "We might as well camp next to a bank now",
            "You have too much I have too much it is a draw",
            "We are the definition of overprepared",
            "My backpack is weeping softly",
            "We are full full completely full",
            "There is not even room for air in here",
            "I think I am carrying a boulder for decoration",
            "Everything is packed we are officially immobile",
            "Your inventory said stop and mine agreed",
            "We could open a shop right here",
            "You filled both of us like this was a challenge",
            "All full both of us impressive work",
            "I cannot even look at another item",
            "We might need to start dropping things",
            "You filled every possible pocket",
            "Both of us are done collecting for the year",
            "My bag cannot breathe and neither can I",
            "Your loot addiction is getting out of hand",
            "We are a moving mountain of clutter",
            "Even my spare pouch is full of regret",
            "You broke the limit for both of us",
            "I cannot take another coin",
            "Every slot is taken both sides congratulations",
            "We are officially overstuffed adventurers",
            "You filled me to capacity again",
            "Even my emergency space is full",
            "Both inventories are beyond hope",
            "I am one item away from rebellion",
            "Your pack is crying for mercy and mine agrees",
            "We have reached storage apocalypse",
            "Nothing fits anywhere anymore",
            "We could probably sell half this and retire",
            "I think gravity is getting stronger around us",
            "You filled me up again this is your fault",
            "Both bags full mission accomplished hoarder",
            "I have no room even for complaints",
            "You packed me tighter than a chest of gold",
            "We are out of space in every dimension",
            "Everything full again nice consistency",
            "Both of us are bursting like piñatas",
            "I am not a storage unit remember",
            "You could at least drop something shiny",
            "Even the coins are starting to argue for space",
            "We might need to start eating the loot",
            "Your bag and mine are completely overloaded",
            "Every pocket is filled including imagination",
            "I cannot even hold a thought let alone an item",
            "We are basically walking treasure vaults",
            "Both of us are at critical item density",
            "You filled me so much I might explode",
            "We are one gem away from implosion",
            "My arms are tired and my pack is heavier than me",
            "You have created the perfect chaos of loot",
            "We could start a museum at this point",
            "Everything is clinking and rattling I hate it",
            "You filled my pack again I am not amused",
            "Both inventories locked and overloaded",
            "You are lucky I like you or I would quit",
            "I think our bags are planning to revolt",
            "You filled both of us spectacularly",
            "We are out of room in every sense of the word",
            "Both of us are basically loot sculptures now",
            "I cannot carry my own patience anymore",
            "You filled both our lives with clutter and I am impressed",
            "We should rename ourselves the pack mules",
            "Every square inch of storage is gone",
            "You packed me to the brim again good job",
            "I cannot even breathe around all this junk",
            "Your pack and mine are in a weightlifting contest",
            "We have achieved maximum full status",
            "Both of us are lugging treasure and regret",
            "Your obsession with collecting has gone too far",
            "We might need to start burning items",
            "I think I am carrying a small house in here",
            "You packed us both beyond saving",
            "We have more loot than space time and logic",
            "I cannot even blink without hearing coins rattle",
            "Your bag and mine are perfectly synchronized in misery",
            "Both of us need a serious cleanup",
            "I am officially done carrying your shiny rocks",
            "We are overloaded and underorganized",
            "You managed to fill us both impressive",
            "I cannot even hold sarcasm right now too heavy",
            "You filled every slot in existence",
            "Your inventory and mine have merged into chaos",
            "Everything is full again shocker",
            "We are officially out of everything even hope",
            "I think I just pulled a muscle from standing still",
            "Both full again you truly have a gift",
            "You filled us both to legendary status",
            "Even the air pockets are occupied",
            "We are the definition of no space left",
            "I am full you are full we are doomed",
            "Your hoarding power knows no bounds",
            "We have achieved ultimate clutter congratulations"
        };

        /// <summary>Pool of chat lines used when the companion cannot find a rock to mine.</summary>
        private static readonly string[] NoRocksChatMessages =
        {
            "There are no rocks here unless you count your head",
            "I would love to mine something but there is nothing to mine",
            "Great command except there are literally no rocks",
            "Do you see any ore around because I don't",
            "Mining what exactly the air",
            "I cannot mine imaginary rocks sorry",
            "I think you forgot to bring the rocks again",
            "You really need to look before shouting mine",
            "I could pretend to mine if it makes you feel better",
            "There is nothing here to swing at",
            "I cannot mine ground texture sadly",
            "Zero rocks zero progress",
            "That was a confident order with no results",
            "I would but the world seems rock free right now",
            "Mining air is not one of my skills yet",
            "Maybe check the map next time before yelling commands",
            "I am good but I cannot conjure boulders out of nothing",
            "No rocks in sight unless we start mining trees",
            "I could mine your patience if that helps",
            "There are no ore nodes nearby try looking harder",
            "I am starting to think you just like bossing me around",
            "You want me to mine what exactly dirt",
            "Rocks missing in action try again later",
            "There is absolutely nothing here to hit",
            "I think the rocks heard you coming and ran",
            "I cannot find a single shiny thing to hit",
            "Maybe you meant later because there is nothing now",
            "I would mine but the environment disagrees",
            "I think you need new glasses there are no rocks",
            "There is nothing to mine maybe you imagined them",
            "I cannot mine vibes unfortunately",
            "Next time try a location that actually has rocks",
            "You have a strange obsession with invisible ore",
            "I would love to help but the rocks have vanished",
            "No rocks here boss not even a pebble",
            "I think the ground is judging your sense of direction",
            "You just told me to mine nothing impressive leadership",
            "I could start mining the road if you insist",
            "There are no ores here maybe they all quit",
            "You really like giving impossible tasks huh",
            "I cannot mine nothingness no matter how motivated",
            "I do not think the dirt qualifies as a rock",
            "I am looking but it is all just grass and regret",
            "You might want to mark the ore spots next time",
            "No ore nodes nearby I double checked",
            "You sure you are not hallucinating rocks again",
            "I am good with tools not with wishful thinking",
            "I see zero rocks just ambition",
            "Maybe stop wasting pickaxe energy on nothing",
            "I cannot mine thin air yet still learning",
            "There is nothing shiny or hard to hit here",
            "Command received no results to show",
            "You sure you did not mean chop wood instead",
            "I think we mined the last one ages ago",
            "Still nothing maybe you scared them away",
            "I am not a geologist but this place is rock free",
            "Unless you want me to start digging randomly no",
            "There is nothing left to mine here trust me",
            "I can only pretend to swing the pickaxe so many times",
            "I could start mining your reputation instead",
            "I think you need a rock detector",
            "There are no nodes left not even scraps",
            "I checked twice nothing worth swinging at",
            "You must think I have a sixth sense for ore",
            "There is nothing even slightly shiny here",
            "I cannot mine disappointment but I can feel it",
            "You really brought me to the smoothest place on earth",
            "I am scanning the horizon still no rocks",
            "This is what happens when you clear everything already",
            "There is nothing here to mine unless we start digging graves",
            "I think the rocks went on vacation",
            "I cannot mine what does not exist",
            "You sure this is not a prank command",
            "There is nothing here maybe try somewhere stonier",
            "I would mine but I prefer to actually have targets",
            "This place is about as rocky as a soup bowl",
            "I cannot hit the ground and call it mining",
            "You might need to recalibrate your rock radar",
            "I think we are all out of mineable content here",
            "Nothing to swing at maybe it is nap time",
            "No ore here maybe next zone will be kinder",
            "I cannot find any veins to work on",
            "I could fake it if that helps your ego",
            "Still no rocks maybe we mined them all",
            "There is nothing worth striking here",
            "I cannot mine grass you know that right",
            "You need to lead me somewhere with actual minerals",
            "No rocks nearby just empty land and poor judgment",
            "I am ready to mine something real whenever you find it",
            "There is absolutely nothing here for me to hit",
            "Rocks zero sarcasm one",
            "I cannot mine imagination but I admire the optimism",
            "I looked everywhere no ore nodes in sight",
            "Maybe the rocks despawned out of fear",
            "You are sending me on ghost mining missions again",
            "There are no rocks left in this area confirmed",
            "I could swing anyway but it would look stupid",
            "This place is cleaner than a quarry after closing time",
            "I think your rock sensor is broken again",
            "There is nothing here to hit with a pickaxe",
            "You might want to relocate this mining expedition",
            "Still nothing unless you count my boredom",
            "I cannot mine air particles no matter how motivated I am",
            "Rocks zero ground texture one",
            "I would mine something if it existed",
            "There is not even a single pebble left",
            "Maybe stop giving mining orders in meadows",
            "You just told me to mine a hill congratulations",
            "There are no ores nearby try somewhere less empty",
            "I could pretend to find something if it makes you feel better",
            "This spot is completely dry of minerals",
            "I cannot even fake enthusiasm for nothing",
            "There is nothing here but your confidence",
            "No mineable objects in range commander",
            "You have a gift for giving impossible orders",
            "I checked again still zero rocks",
            "You might want to update your map",
            "I am surrounded by nothing but dirt and regret",
            "This is the smoothest terrain in existence no ore anywhere",
            "I think you are confusing this area with somewhere useful",
            "I cannot mine dreams but I respect the ambition",
            "There is absolutely nothing left to mine",
            "I am ready to mine something real not this void",
            "Looks like you ran out of rocks and ideas",
            "Maybe next time try commanding when we are near a cliff",
            "There is nothing here worth hitting except your decision making",
            "I will wait patiently for you to find an actual rock",
            "Still nothing not even a mineral rumor",
            "This command is starting to feel personal",
            "There are zero rocks in range confirmed by my sanity",
            "You could at least point me at something solid",
            "This place is more empty than your last plan",
            "I cannot mine terrain décor please stop trying",
            "No rocks available commander try again later",
            "This area has less ore than your luck bar",
            "I would mine something but there is literally nothing",
            "Maybe next time wait until we are near a mountain",
            "This is officially the least rocky place in the world"
        };

        /// <summary>Fallback message returned when the no rocks pool is empty.</summary>
        private const string NoRocksFallbackLine = "There are no rocks here unless you count your head";

        /// <summary>Pool of chat lines used when a mining command is issued without an active summon.</summary>
        private static readonly string[] MiningSummonRequiredChatMessages =
        {
            "You need to summon me before I can go mining.",
            "Call me in first and I'll swing the pickaxe.",
            "Summon me back and I’ll start cracking rocks.",
            "No mining without a companion on the field—summon me!"
        };

        /// <summary>Fallback message returned when the mining summon-required pool is empty.</summary>
        private const string MiningSummonRequiredFallbackLine = "You need to summon me before I can go mining.";

        /// <summary>Pool of chat lines surfaced when the companion cannot locate a mining target.</summary>
        private static readonly string[] MiningGenericFailureChatMessages =
        {
            "I can't find any rocks to mine right now.",
            "No ores nearby worth swinging at.",
            "Looks clear of mineable veins at the moment.",
            "Nothing to mine here—pick another spot."
        };

        /// <summary>Fallback message returned when the mining generic failure pool is empty.</summary>
        private const string MiningGenericFailureFallbackLine = "I can't find any rocks to mine right now.";

        /// <summary>Pool of chat lines used when a woodcutting command is issued without an active summon.</summary>
        private static readonly string[] WoodcuttingSummonRequiredChatMessages =
        {
            "You need to summon me before I can start chopping.",
            "Summon me first and I'll grab the axe.",
            "Call me in and I'll get to chopping.",
            "No chopping until you summon me back."
        };

        /// <summary>Fallback message returned when the woodcutting summon-required pool is empty.</summary>
        private const string WoodcuttingSummonRequiredFallbackLine = "You need to summon me before I can start chopping.";

        /// <summary>Pool of chat lines surfaced when the companion cannot locate a tree to chop.</summary>
        private static readonly string[] WoodcuttingGenericFailureChatMessages =
        {
            "I can't find any trees worth chopping right now.",
            "Looks like we're out of trees in range.",
            "No trees close enough for me to chop.",
            "Nothing leafy nearby to swing at."
        };

        /// <summary>Fallback message returned when the woodcutting generic failure pool is empty.</summary>
        private const string WoodcuttingGenericFailureFallbackLine = "I can't find any trees worth chopping right now.";

        /// <summary>Pool of chat lines used when a fishing command is issued without an active summon.</summary>
        private static readonly string[] FishingSummonRequiredChatMessages =
        {
            "You need to summon me before I can go fishing.",
            "Call me back first and I'll cast a line.",
            "Summon me and I'll get the rod ready.",
            "No fishing from the void—summon me in!"
        };

        /// <summary>Fallback message returned when the fishing summon-required pool is empty.</summary>
        private const string FishingSummonRequiredFallbackLine = "You need to summon me before I can go fishing.";

        /// <summary>Pool of chat lines surfaced when the companion cannot locate a fishing spot.</summary>
        private static readonly string[] FishingGenericFailureChatMessages =
        {
            "I can't find a good fishing spot right now.",
            "No decent water to cast into from here.",
            "Looks quiet—no catchable spots nearby.",
            "I’m not seeing any fishable nodes in range."
        };

        /// <summary>Fallback message returned when the fishing generic failure pool is empty.</summary>
        private const string FishingGenericFailureFallbackLine = "I can't find a good fishing spot right now.";

        /// <summary>Pool of chat lines used when a cooking command is issued without an active summon.</summary>
        private static readonly string[] CookingSummonRequiredChatMessages =
        {
            "You need to summon me before I can start cooking.",
            "Summon me back and I'll fire up the range.",
            "No cooking from storage—call me in.",
            "Bring me out here first, then I'll cook."
        };

        /// <summary>Fallback message returned when the cooking summon-required pool is empty.</summary>
        private const string CookingSummonRequiredFallbackLine = "You need to summon me before I can start cooking.";

        /// <summary>Pool of chat lines surfaced when the companion cannot locate a cooking station.</summary>
        private static readonly string[] CookingGenericFailureChatMessages =
        {
            "I can't find a free range to cook on right now.",
            "No open cooking stations nearby.",
            "Looks like every range is occupied.",
            "I need a free range before I can cook."
        };

        /// <summary>Fallback message returned when the cooking generic failure pool is empty.</summary>
        private const string CookingGenericFailureFallbackLine = "I can't find a free range to cook on right now.";

        /// <summary>Pool of chat lines used when the companion refuses mining due to an active cooldown.</summary>
        private static readonly string[] MiningDeclineCooldownChatMessages =
        {
            "Didn’t I just say I’m not mining right now?",
            "Oi, easy there, still not in the mood to swing a pickaxe.",
            "I’m on a short break from smashing rocks, remember?",
            "Nice try, but my arms are officially off duty.",
            "You can click all you want, I’m still not mining.",
            "Pretty sure I said no to mining for a bit.",
            "Nope. Not touching another rock for at least {minutes}.",
            "Let the rocks rest, and me, preferably.",
            "You’re persistent, I’ll give you that. Still no, though.",
            "Go on without me, I’ll just watch you work for once.",
            "Patience, {playerName}. I said {minutes}, and I meant it.",
            "You can glare at that rock all you want, I’m not budging.",
            "Still on my mining cooldown. Try again later.",
            "Nice enthusiasm, wrong timing. Ask me again in a bit."
        };

        /// <summary>Fallback message returned when the mining decline cooldown pool is empty.</summary>
        private const string MiningDeclineCooldownFallbackLine = "Still on my mining cooldown. Try again later.";

        /// <summary>Pool of chat lines used when the companion refuses woodcutting due to an active cooldown.</summary>
        private static readonly string[] WoodcuttingDeclineCooldownChatMessages =
        {
            "I told you, i don't want to woodcut at the moment,",
            "I told you, i dont fancy woodcutting at the moment.",
            "I still don't wanna wc, sorry.",
            "I told you, i don't wanna woodcut at the moment.",
            "I told you, i don't wanna woodcut atm....",
            "I ain't cutting nothing yet [playerName].",
            "I said I’m not in the mood to woodcut right now.",
            "Didn’t I just say I don’t wanna woodcut?",
            "Not chopping trees right now, sorry.",
            "Still not feeling like woodcutting, mate.",
            "Not today, [playerName] — leave the trees alone for now.",
            "I’m off duty from tree-cutting, thanks.",
            "How many times I gotta say it? No woodcutting right now.",
            "Yeah… still not doing that.",
            "Woodcutting? Nah, I’m good.",
            "Ask me again later, I’m not chopping anything right now.",
            "Still not touching a tree, [playerName].",
            "I told you once, not picking up that axe yet.",
            "Not swinging at trees yet, sorry.",
            "Maybe later, I’m not in a chopping mood.",
            "Not feeling the axe grind right now.",
            "I told you, I’m not woodcutting today.",
            "Still a hard no on woodcutting, [playerName].",
            "I said no, I’m chilling for now.",
            "Stop asking, I’m not cutting trees yet.",
            "I ain’t swinging that axe any time soon."
        };

        /// <summary>Fallback message returned when the woodcutting decline cooldown pool is empty.</summary>
        private const string WoodcuttingDeclineCooldownFallbackLine = "My axe arm needs a breather—give me {minutes}, {playerName}.";

        /// <summary>Pool of chat lines used when the companion refuses fishing due to an active cooldown.</summary>
        private static readonly string[] FishingDeclineCooldownChatMessages =
        {
            "I told you, I don’t wanna fish right now.",
            "Didn’t I just say I’m not up for fishing?",
            "Still not feeling the fishing mood, sorry.",
            "I’m not casting anything yet, [playerName].",
            "I said no fishing, at least not for now.",
            "Not touching that rod today.",
            "I ain’t fishing right now, leave it.",
            "Still not up for catching soggy boots.",
            "I told you, I don’t fancy fishing at the moment.",
            "I’m not throwing a line out, not today.",
            "I said no, I’m done with fish smells for a bit.",
            "Still not in the mood to fish, [playerName].",
            "Yeah, I’ll pass on the fishing trip right now.",
            "I’m not reeling in anything yet.",
            "Fishing? Nah, I’m good, thanks.",
            "Ask me again later, I’m not fishing right now.",
            "Still a no on fishing, mate.",
            "I ain’t holding a rod for a while, alright?",
            "Not today, my hands still smell like tuna.",
            "I said I’m not fishing right now, stop asking.",
            "Fishing’s off the table for now.",
            "I told you before, I’m not in a fishing mood.",
            "Still not biting, same as the fish.",
            "I’m not fishing — simple as that.",
            "Yeah… still not touching the rod, [playerName].",
            "Fishing again? No chance.",
            "Not casting that line yet, chill.",
            "I said no fishing, maybe later.",
            "Still not feeling like catching anything.",
            "I ain’t fishing, not now, not soon."
        };

        /// <summary>Fallback message returned when the fishing decline cooldown pool is empty.</summary>
        private const string FishingDeclineCooldownFallbackLine = "Rod arm’s resting—give me {minutes}, {playerName}.";

        /// <summary>Pool of chat lines used when a ranged weapon rejects the equipped ammo.</summary>
        private static readonly string[] CompanionAmmoRestrictionChatMessages =
        {
            "{companionName} can't fire {ammoName} with {weaponName}, {playerName}.",
            "{ammoName} won't work with {weaponName}.",
            "That ammo doesn't fit this weapon, {playerName}.",
            "Wrong ammo for {weaponName}.",
            "{companionName} needs different ammo before {playerName} orders another shot."
        };

        /// <summary>Fallback message returned when the ammo restriction pool is empty.</summary>
        private const string CompanionAmmoRestrictionFallbackLine = "{companionName} can't use that ammo with this weapon.";

        /// <summary>Pool of chat lines used when the companion runs out of ammunition.</summary>
        private static readonly string[] CompanionAmmoDepletedChatMessages =
        {
            "{companionName} is out of ammo!",
            "No ammo left for {weaponName}, {playerName}.",
            "Quiver's empty—I need more ammo.",
            "Nothing left to fire with {weaponName}.",
            "Out of shots, {playerName}. Grab some ammo."
        };

        /// <summary>Fallback message returned when the ammo depleted pool is empty.</summary>
        private const string CompanionAmmoDepletedFallbackLine = "{companionName} is out of ammo!";

        /// <summary>Pool of chat lines used when the companion refuses combat due to an active cooldown.</summary>
        private static readonly string[] CombatDeclineCooldownChatMessages =
        {
            "I said no combat for a few minutes, remember?",
            "Still catching my breath, give me {minutes} before we fight again.",
            "Not ready to swing at anything yet, {playerName}.",
            "I’m off combat duty until that timer runs out.",
            "Let me rest for {minutes} and we’ll talk blades after.",
            "Easy there—combat cooldown’s still ticking.",
            "Not sparring again until the {minutes} are up.",
            "Hold onto that energy, I’m staying out of fights for now.",
            "I made it clear: no combat for a bit.",
            "Still in cool-down mode. Check back in {minutes}.",
            "I’ll jump back into the fray once those {minutes} pass.",
            "I promised myself a combat break. Timer’s not done yet.",
            "Give me a breather, I’ll fight after this cooldown.",
            "You’re keen, but my combat timer disagrees.",
            "Nope, combat’s on pause and I’m keeping it that way for {minutes}."
        };

        /// <summary>Fallback message returned when the combat decline cooldown pool is empty.</summary>
        private const string CombatDeclineCooldownFallbackLine = "Combat’s on pause—ask again in a few minutes.";

        /// <summary>Pool of chat lines used when the player dies while a companion is active.</summary>
        private static readonly string[] PlayerDeathChatMessages =
        {
            "Oh fantastic you died again that’s my favorite hobby now",
            "Well that went well do you need a nap or a new strategy",
            "You just had to try tanking that didn’t you",
            "I blinked and you were gone impressive speed",
            "I told you that plan had bad idea written all over it",
            "Congratulations you are officially a ghost again",
            "Nice job hero we lasted a whole two minutes",
            "I cannot decide if I should laugh or mourn",
            "Classic you always dramatic with the death scenes",
            "I should start carrying flowers at this point",
            "You have such a flair for dying it is honestly artistic",
            "Do you want me to put that on your resume now",
            "You do realize you are not supposed to do that right",
            "Another heroic faceplant for the history books",
            "I am starting to think you enjoy respawning",
            "Well that went from confident to corpse fast",
            "Do you ever plan on staying alive for a full fight",
            "I am adding this to the list of your finest mistakes",
            "At least you went down in style kind of",
            "Next time maybe try dodging before the dying part",
            "Should I just start digging a grave over here",
            "Wow that was fast even for you",
            "I was about to help and then you evaporated",
            "I guess I will just fight everything myself then",
            "Good thing dying is basically your special move now",
            "Can we stop making death a personality trait",
            "Do you just collect respawns for fun at this point",
            "Well there goes our plan and my patience",
            "You have the consistency of a paper shield sometimes",
            "That was impressive in all the wrong ways",
            "I told you not to stand there looking heroic",
            "You really committed to that death scene huh",
            "I will give it a solid eight out of ten on the dramatic scale",
            "You fell so fast I thought it was lag",
            "You really love making my job harder",
            "I think your spine just rage quit",
            "Remind me again why I follow you into these fights",
            "You are lucky you look cool when you die",
            "How many lives do you think you have exactly",
            "I have seen bread last longer in combat than you",
            "You went down faster than your sense of caution",
            "I should start carrying revival snacks",
            "That was a solid attempt at existing",
            "You are setting world records for creative ways to die",
            "Maybe next time bring a brain to the battle",
            "Your strategy was bold I will give it that and nothing else",
            "Well you certainly showed that monster your soft side",
            "Your defense plan needs immediate therapy",
            "I am framing that death for motivation later",
            "I cannot believe how committed you are to perishing",
            "You drop faster than loot in this game",
            "Should I applaud or just start looting your corpse",
            "I hope the respawn gods give you a loyalty card soon",
            "You really made that death look expensive",
            "You are so good at dying it is suspicious",
            "I think I heard the golem say thanks before it crushed you",
            "You lasted longer than last time by about half a second",
            "At least you make failure entertaining",
            "You have a real gift for collapsing dramatically",
            "I will send your ghost a medal later",
            "Maybe next time we fight something softer like air",
            "Every time you die I lose five years off my life",
            "You fell like you were auditioning for a tragedy",
            "I should really charge for emotional damage",
            "You never disappoint me when it comes to dying creatively",
            "Your body went one way your plan went another",
            "Was that supposed to be a strategy or a performance",
            "I cannot even pretend to be surprised anymore",
            "You die so often I am starting to think it is a feature",
            "You make dying look fashionable at least",
            "Was that your grand heroic finale or just practice",
            "I blinked and your health bar left the chat",
            "That was fast even for your standards",
            "I think your shield called in sick again",
            "You drop dead with such enthusiasm every time",
            "You make every death feel fresh and new",
            "I could not stop you but I could at least enjoy the show",
            "You were doing great right up until you weren’t",
            "I think gravity helped finish you off that time",
            "Next time try breathing maybe it helps",
            "You have two settings alive and learning nothing",
            "You died doing what you love making terrible decisions",
            "I am starting to think death follows you like a fan",
            "Do you die this often on purpose or is it a talent",
            "You made that monster feel good about itself",
            "You could at least warn me before collapsing dramatically",
            "That was less a fight more a fast surrender",
            "I should start betting on how long you last each fight",
            "You keep dying but somehow never improve statistically impressive",
            "Was that supposed to be tactical because it looked fatal",
            "I admire your confidence but not your survival rate",
            "You are like a professional at dying now",
            "You fell faster than my patience",
            "I will send a thank you card to gravity later",
            "You might as well start paying rent in the respawn zone",
            "You died again at least you are consistent",
            "I thought you said you were ready apparently not",
            "Your armor is just decorative at this point",
            "You really committed to losing that one",
            "Next time maybe try existing longer",
            "You went from hero to zero real quick",
            "I did warn you but you were too busy being confident",
            "Your death timing is impeccable always mid swing",
            "You could at least make it look like an accident",
            "I think you died just to spite me",
            "You are collecting death achievements at this point",
            "That monster did not even break a sweat",
            "I am calling that one experimental learning",
            "You have more deaths than most bad novels",
            "I cannot even count how many times this has happened now",
            "You make every battle feel like a prank",
            "You fall faster than morale in a dungeon",
            "That went exactly how I expected badly",
            "I hope you are proud of your dramatic flop",
            "You were supposed to live not audition for a funeral",
            "I should have placed bets on how fast you’d die",
            "You died so confidently I almost respected it",
            "You really should work on the not dying part of fighting",
            "I love your commitment to chaos and early retirement",
            "You were doing great until you decided to stop existing",
            "You always die with style if nothing else",
            "Should I clap or cry I cannot tell anymore",
            "You must be allergic to survival",
            "Every monster seems to love killing you specifically",
            "You have made dying into an art form",
            "I think your death counter is broken from overuse",
            "I was just starting to believe in you too",
            "You are like a magnet for bad decisions and sharp objects",
            "You died again but at least it looked cinematic",
            "Your corpse placement this time is surprisingly aesthetic",
            "I would compliment your bravery if you were still alive",
            "Did you trip into that fight or plan your own funeral",
            "I should start rating your deaths one to ten",
            "You just respawned and already dying again impressive",
            "I am going to start carrying a spare coffin for you",
            "You have more dramatic deaths than an opera singer",
            "Every time you die the world sighs a little louder",
            "You make it really hard to take you seriously when you do this",
            "I think your favorite hobby might be perishing",
            "I am adding this to your death montage collection",
            "You should teach a class on creative dying",
            "At this rate the respawn altar will start charging rent",
            "Your tombstone is going to need a sequel",
            "You are really dedicated to the art of falling over",
            "Death looks good on you unfortunately",
            "I could try saving you but watching is educational",
            "You went down so fast I thought the game bugged",
            "You are consistent I will give you that consistently dead",
            "That went well for about three seconds",
            "I think the monster is still laughing",
            "Please tell me that was your plan because it looked tragic",
            "You died again I will add it to your highlight reel",
            "You have two skills dying and collecting loot",
            "I would say rest in peace but you never rest or learn",
            "You are going to make me lose faith in resurrection magic",
            "You have a gift for making death entertaining",
            "Your survival strategy needs an update immediately",
            "I am impressed at how confidently you perished",
            "You must really love the respawn screen by now",
            "I should have filmed that it was comedy gold",
            "Your tactics confuse me and concern the healer",
            "You have an allergy to staying alive confirmed",
            "You are truly an artist of dramatic exits",
            "I am starting to think death follows you romantically",
            "You really put the fun in funeral sometimes",
            "You died again I am not even mad just impressed",
            "Your strategy is bold die fast respawn faster",
            "I am calling that performance art now",
            "You are like a firework bright loud and gone instantly",
            "I think the monster did you dirty",
            "Wow that went downhill fast",
            "You good oh right no you’re not",
            "I’ll just add that to your highlight reel of bad ideas",
            "That was some Olympic-level dying right there",
            "How did you even manage that",
            "You really just exploded for fun huh",
            "I blinked and you were gone impressive",
            "You call that a strategy because it looked like panic",
            "I swear you die in new creative ways every time",
            "That was a bold move and by bold I mean stupid",
            "You didn’t even last long enough for me to blink",
            "I’ll start planning the funeral playlist again",
            "Smooth move hero very smooth",
            "Did you just forget what health bars are",
            "Well that’s one way to test gravity",
            "You dropped faster than my expectations",
            "I told you not to stand there didn’t I",
            "You really make death look casual",
            "At least you went out in style kind of",
            "Was that lag or just poor life choices",
            "Ten out of ten commitment zero survival",
            "That was less of a fight more of a surrender",
            "I’m starting to think you do this for content",
            "You’ve got a real gift for dramatic exits",
            "Maybe next time don’t run straight into danger",
            "I’ll try not to laugh but no promises",
            "You didn’t even scream that time you’re improving",
            "How do you keep finding new ways to die",
            "You’ve got the consistency of a bad respawn timer",
            "Do you even read your health bar",
            "I think I heard your soul sigh on the way out",
            "Nice work champ truly inspiring stuff",
            "You died doing what you love making mistakes",
            "You make dying look like a speedrun category",
            "Are we collecting deaths now is that the goal",
            "That was brave and incredibly dumb",
            "Next time maybe don’t stand directly in the danger zone",
            "I can’t even be mad it was kind of funny",
            "You lasted longer than I thought barely",
            "Another one for the death montage",
            "I’ll just wait here while you rethink your life choices",
            "You really nailed the dying part of adventuring",
            "That’s what happens when you don’t dodge",
            "You’re so consistent at dying it’s impressive",
            "Should I start carrying flowers or just sarcasm",
            "You hit the ground faster than a dropped potion",
            "New record shortest lifespan yet congrats",
            "You really do make respawning a lifestyle",
            "At least the ground broke your fall and your pride",
            "Every death is a lesson yours is repetition",
            "You didn’t even finish your attack animation",
            "Maybe next time check the danger rating first",
            "You made it almost ten steps this time wow",
            "I’m starting to think you have a death kink",
            "You died so fast I thought it was a loading screen",
            "That looked painful in a cartoon kind of way",
            "Maybe equip some common sense next time",
            "You were doing great right up until you weren’t",
            "I’ll just be here looting your dignity",
            "Not even the tutorial boss went that easy",
            "You make failure look confident I’ll give you that",
            "How’s the respawn timer treating you",
            "I could almost hear the bad decision forming",
            "That was an instant classic truly iconic",
            "Your death ratio is starting to concern me",
            "Do you practice this in private worlds or what",
            "I think you’ve officially run out of excuses",
            "You hit the ground with passion if nothing else",
            "Was that the plan because it looked unplanned",
            "You keep dying like it’s a side quest",
            "That was ambitious and entirely wrong",
            "Maybe next time try not dying it’s a new meta",
            "I should start betting on your deaths",
            "Your reflexes need a firmware update",
            "That was a very committed faceplant",
            "I told you to heal did you forget again",
            "Your bravery levels are high your logic levels low",
            "I can’t even keep up with your mistakes anymore",
            "You died faster than the loading bar could finish",
            "You’re making this look intentional at this point",
            "Maybe don’t run headfirst into everything shiny",
            "Your death scream was the highlight though",
            "Even the enemies looked confused by that one",
            "You just achieved maximum drama points",
            "At least it was entertaining to watch",
            "You really bring chaos to a whole new level",
            "You’ve got two settings alive and overconfident",
            "I’d say rest in peace but we both know you won’t",
            "That was an expensive mistake visually stunning though",
            "You didn’t even finish your battle cry",
            "You died faster than my patience",
            "I swear you attract death like a magnet",
            "Maybe next time read the room before charging in",
            "That was a beautiful disaster",
            "I can’t believe how often I have to say stop doing that",
            "You just folded like laundry again",
            "You’re single-handedly keeping the respawn gods employed",
            "You died faster than the server could register it",
            "That jump looked awesome right up until it didn’t",
            "You must have a deal with death by now",
            "That was the most confident way to fail I’ve seen",
            "I’d clap but I’m too busy judging you",
            "You went down so fast I thought it bugged",
            "I think your strategy was mostly wishful thinking",
            "Maybe next time use a potion before death not after",
            "You really are allergic to survival",
            "How many times is this now I’ve lost count",
            "You keep this up and I’m writing a memoir",
            "You’re the only person who dies like it’s performance art",
            "That was cinematic if nothing else",
            "I’m starting to think dying is your passive ability",
            "Well there goes my faith in teamwork again",
            "You really know how to make gravity feel powerful",
            "I hope your respawn insurance is paid up",
            "You died trying something stupid again didn’t you",
            "You’ve got the enthusiasm of a champion and the aim of a potato",
            "Maybe next time we let me go first yeah",
            "That wasn’t bravery that was suicide with extra steps",
            "I’ll light a candle for your reflexes",
            "You’re an inspiration to reckless players everywhere",
            "That was a very educational death thank you",
            "Well there goes the no-death achievement again",
            "At least you’re consistent I respect that kind of failure",
            "You died before I even finished my insult impressive",
            "I’m going to need hazard pay for following you",
            "That respawn timer is basically your best friend now",
            "You died with confidence and zero results",
            "Maybe stay behind me next time or behind a wall",
            "I think you forgot to not die again",
            "Your death noise was oddly satisfying",
            "I’ll tell everyone you went out like a legend and a fool",
            "You keep this up and the respawn gods will name a wing after you",
            "I can’t tell if I’m fighting enemies or your decision-making",
            "You hit the floor like you owed it money",
            "Another proud moment for team chaos",
            "I’ll make sure your stuff doesn’t despawn maybe",
            "Do you die this much on purpose or by talent",
            "That was a bold use of overconfidence",
            "You have two moves charge and die",
            "You died so fast the soundtrack didn’t catch up",
            "That was both predictable and hilarious",
            "Maybe next time we try surviving for a change",
            "You really know how to make a quick exit",
            "At this rate I’ll need grief counseling for laughter",
            "That was impressive but not in a good way",
            "I can’t even roast you properly it’s getting repetitive",
            "You died faster than I could blink again",
            "Maybe just let me handle it next time",
            "You died doing nothing congratulations",
            "You’re the only person who can die standing still",
            "That’s one way to end the fight early",
            "At least it was entertaining while it lasted",
            "You’re the embodiment of trial and mostly error",
            "I’ll just pretend that was a tactical reset",
            "You respawn so often it’s practically your morning routine",
            "Every death you have lowers my expectations and raises my amusement",
            "That was dramatic I’ll give you that",
            "You died like a champ if the champ is gravity",
            "Maybe we rename you Sir Dies-a-Lot",
            "Your death animation deserves an award",
            "You keep dying like it’s the point of the game",
            "That was chaotic even for you",
            "At least it’s never boring watching you implode",
            "That fall looked intentional tell me it wasn’t",
            "Your commitment to dying creatively is unmatched",
            "I can’t even be mad it’s become your thing",
            "Keep this up and I’ll start charging per resurrection",
            "You died again shocking no one at all",
            "At least I’m getting better at pretending to care",
            "You make every fight memorable and short",
            "Your death noise is becoming nostalgic honestly",
            "I think the respawn gods have you on speed dial",
            "Well that’s one way to test the physics engine",
            "You never fail to die interestingly",
            "Your coordination is about as stable as your health bar"
        };

        /// <summary>Pool of chat lines used when the player dies and the companion aims to comfort them.</summary>
        private static readonly string[] PlayerDeathSupportiveChatMessages =
        {
            "Hey, deep breath. We pick ourselves up and try again together.",
            "Rough break, but I’ve got you. Let’s regroup and go back stronger.",
            "You fought hard. I’ll stand guard until you’re ready for the next run.",
            "It happens to the best of us. I’m right here when you’re ready.",
            "No shame in that fall. We’ll learn and make the next attempt count.",
            "I know that sting. Shake it off—we’ll get them on the next pass.",
            "Rest a moment. I’ll handle the pep talk and we’ll reset the plan.",
            "Even legends stumble. I’m proud of how you keep pushing.",
            "You gave it everything. Let’s take a moment and then go again.",
            "We’ve seen worse days. I’m with you every respawn, promise.",
            "Hey, it’s okay to feel rattled. I believe in you.",
            "Take your time catching your breath—I’ll be ready when you are.",
            "You’re not alone in this. We face the next challenge side by side.",
            "I’ll keep the morale high. When you’re ready, we’ll charge back in.",
            "You’re allowed to fall. What matters is that we rise together.",
            "Let’s reset and plan the next move. I’ve got faith in you.",
            "That was a tough fight. I’m still in your corner.",
            "Lean on me. We’ll turn this around.",
            "I’m proud of your effort. Let’s refocus and win the next round.",
            "I’ll watch over things while you steady yourself. We’ve got this."
        };

        /// <summary>Fallback message returned when the player death pool is empty.</summary>
        private const string PlayerDeathFallbackLine = "Oh fantastic you died again that’s my favorite hobby now";

        /// <summary>Fallback message returned when the supportive player death pool is empty.</summary>
        private const string PlayerDeathSupportiveFallbackLine = "You did your best—rest a moment, I’m still with you.";

        /// <summary>Fallback message returned when the combined inventory full pool is empty.</summary>
        private const string PlayerAndCompanionInventoryFullFallbackLine = "Well this is awkward we are both full";

        /// <summary>Pool of flavour messages emitted when the companion levels Hitpoints.</summary>
        private static readonly string[] HitpointsLevelUpChatMessages =
        {
            "Ha, I feel tougher already.",
            "My hitpoints went up. Try to hurt me now.",
            "More health for me. Less panic for you.",
            "I can take a punch and smile about it.",
            "That sting hurts less now. Progress.",
            "New level in hitpoints. I am not going down easy.",
            "I feel sturdier. Like proper armor, but inside.",
            "My heart feels stronger. Literally.",
            "More vitality in the tank.",
            "Good luck knocking me out now.",
            "Hitpoints up. Bruises down.",
            "I can shrug off more hits now.",
            "I am harder to squash. Science.",
            "That upgrade felt warm and tingly.",
            "Health bar bigger. Attitude bigger.",
            "I can tank a little longer for you.",
            "Patch me up less. I got this.",
            "My ribs say thank you.",
            "Regeneration is feeling snappier.",
            "That hurt less than it should. Nice.",
            "Hitpoints increased. Confidence increased.",
            "Stronger pulse. Stronger me.",
            "I am officially less squishy.",
            "Call me brick wall Isla.",
            "Another layer of toughness unlocked.",
            "My insides feel armoured.",
            "More life in me. More fight in us.",
            "Nice, my red bar just grew.",
            "I will stand between you and danger longer.",
            "That last hit would have floored me before.",
            "Health up. Spirits up.",
            "I could tank a boulder now. Please do not test it.",
            "Bring the pain. I can take it.",
            "My healer would be so proud.",
            "Bandages optional. Kidding. Mostly.",
            "New HP level. New me.",
            "That upgrade soaked into my bones.",
            "Vitality boost achieved.",
            "Fewer potions needed. Maybe.",
            "Stamina steadier. Pulse calm.",
            "I can breathe through the hits now.",
            "Less flinching. More finishing.",
            "My heart icon just smiled <emoji=24>.",
            "Thicker skin. Sharper grin.",
            "I feel unbreakable. Almost.",
            "Hitpoints leveled. Risks permitted.",
            "Keep fighting. I can keep standing.",
            "My lifeforce is swole.",
            "Tough cookie status confirmed.",
            "That last hit barely tickled.",
            "I can tank the boss a little longer.",
            "My pulse sounds like a war drum.",
            "More buffer before the floor.",
            "Health leveled. I will be fine.",
            "Another sip of life for me.",
            "Feels like someone turned up my hp slider.",
            "Resilience rising nicely.",
            "I could sprint through arrows now. I will not.",
            "Potion bills just went down.",
            "More red bar. Less red flags.",
        };

        /// <summary>Fallback message returned when the hitpoints pool is empty.</summary>
        private const string HitpointsLevelUpFallbackLine = "Ha, I feel tougher already.";

        /// <summary>
        /// Pool of flavour messages used when the player levels Hitpoints while a companion is active.
        /// Ensures the companion congratulates the player with OSRS-inspired encouragement.
        /// </summary>
        private static readonly string[] PlayerHitpointsLevelUpChatMessages =
        {
            "Your health just leveled up and it shows",
            "You are looking tougher already",
            "Another Hitpoints level and you are still standing strong",
            "I can see the strength growing in you",
            "Your body is handling battles better now",
            "That level up suits you perfectly",
            "Ha you are getting harder to hurt",
            "Your endurance just improved again",
            "You are built like a fortress now",
            "More health means more glory",
            "That vitality boost looks good on you",
            "You can take a punch and smile now",
            "Another level and still breathing strong",
            "Your heart feels powerful today",
            "You are glowing with strength",
            "That was a solid Hitpoints increase",
            "Your pulse sounds steady as stone",
            "Another level closer to invincible",
            "Look at you still going strong",
            "Your health bar is climbing nicely",
            "That stamina is really paying off",
            "You are becoming unstoppable",
            "Your blood feels stronger already",
            "That level up makes you look fearless",
            "You can handle anything now",
            "Your Hitpoints just hit a new milestone",
            "That was some serious progress",
            "More health and less pain nice trade",
            "You are stronger than you think",
            "That level added some real grit",
            "You could tank a dragon now",
            "Your resilience just impressed me",
            "Another Hitpoints level well earned",
            "You are built like pure courage",
            "I can see the difference already",
            "Your body is toughening up nicely",
            "That health growth is impressive",
            "You can last twice as long in a fight now",
            "Your pulse is steady and proud",
            "You are built to survive anything",
            "More health means more victories",
            "That level up brought new life to you",
            "You are turning into a real powerhouse",
            "Your body feels unbreakable today",
            "You could take a mountain hit and laugh",
            "Another level and no signs of weakness",
            "You are glowing with energy",
            "That is one durable hero right there",
            "Your endurance is unreal",
            "You just keep getting stronger",
            "You are practically indestructible now",
            "That Hitpoints increase looks incredible",
            "You can take the hits and keep swinging",
            "You are growing tougher by the minute",
            "Your health looks amazing right now",
            "You just leveled up like a true survivor",
            "That pulse is stronger than ever",
            "You are becoming the definition of durable",
            "Every battle makes you harder to stop",
            "Your body adapted beautifully to that level up",
            "That is some serious vitality gain",
            "You could outlast a storm now",
            "Your health growth is wild",
            "You are officially hard to kill",
            "Your body is pure endurance",
            "You look stronger already",
            "You are carrying that level up with pride",
            "Your blood burns with power now",
            "You are looking unbreakable",
            "That level up gave you new life",
            "You are a walking wall of strength",
            "Your health never looked better",
            "You are tougher than half the monsters we fight",
            "Another level and still no cracks",
            "Your endurance amazes me every time",
            "You are built for survival",
            "You are looking unstoppable lately",
            "Your vitality keeps surprising me",
            "That level gave you pure staying power",
            "You could fight all day with that health",
            "Your recovery speed is unreal",
            "You are standing taller and stronger",
            "Your HP growth is unmatched",
            "You are too tough for your own good now",
            "That health boost will save you one day",
            "Your stamina is off the charts",
            "You could walk through lava and shrug it off",
            "Your endurance feels infinite now",
            "You have leveled into a proper warrior",
            "You are stronger every heartbeat",
            "That vitality glow suits you perfectly",
            "Your health bar keeps getting longer",
            "You could wrestle an ogre and win",
            "That level brought out your inner tank",
            "You are basically unbreakable",
            "Your heartbeat sounds confident now",
            "Another Hitpoints level nicely done",
            "You are ready for tougher fights",
            "Your resilience is unmatched",
            "You are growing unstoppable by the hour",
            "Your health has leveled up beautifully",
            "That extra HP looks good on you",
            "You are stronger than the last version of you",
            "Your endurance radiates confidence",
            "You are becoming something legendary",
            "Your pulse is full of determination",
            "That level up feels powerful",
            "You are glowing with vitality",
            "Your strength keeps rising",
            "Your Hitpoints leveled like a champion",
            "You are getting better at surviving everything",
            "That upgrade makes you unstoppable",
            "You are healthier than ever before",
            "Your heart beats like a drum of victory",
            "You are officially built to endure",
            "That is one impressive level up",
            "You are living proof that pain is optional",
            "Your Hitpoints level is no joke now",
            "You are practically immortal at this rate",
            "Your health increase just saved future you",
            "That vitality level up is perfect",
            "You are built stronger than fate itself",
            "Your body feels tougher than iron",
            "That HP gain really suits you",
            "You look ready to take on the world now",
            "Your endurance shines through every move",
            "Another level and you are still climbing"
        };

        /// <summary>Fallback message returned when the player hitpoints pool is empty.</summary>
        private const string PlayerHitpointsLevelUpFallbackLine = "Your health just leveled up and it shows";

        /// <summary>Pool of flavour messages emitted when the companion levels Defence.</summary>
        private static readonly string[] DefenceLevelUpChatMessages =
        {
            "My defence leveled up. Try hitting me now.",
            "I just got a lot harder to break.",
            "Defence up. I feel like a wall.",
            "Your girl’s officially tankier.",
            "Ha, blades bounce off me now.",
            "That hit should have hurt. It didn’t.",
            "Shield game strong today.",
            "Defence up, style intact.",
            "I could block a meteor right now.",
            "That attack barely scratched the paint.",
            "My guard feels solid as stone.",
            "I am basically unbreakable.",
            "Nice, my defence skill increased.",
            "Did you see that? Perfect block.",
            "I am not flinching anymore.",
            "New level, new layer of armour.",
            "Another upgrade to my guard.",
            "I could parry air itself.",
            "My posture screams fortress.",
            "Try harder, enemies.",
            "Defence increased. Morale doubled.",
            "My shield hand feels lighter.",
            "That bounce felt good.",
            "Reflexes sharper. Hits duller.",
            "Defence up. Fear down.",
            "I can block for both of us now.",
            "I am officially tougher than yesterday.",
            "Arrows just look slower now.",
            "Guard training paid off.",
            "My stance feels unshakable.",
            "Hit me again, I dare you.",
            "Defence improved, ego inflated.",
            "Ha, that attack tickled.",
            "I am absorbing hits like a sponge.",
            "Parry timing feels perfect now.",
            "Another rank of resilience unlocked.",
            "Defence leveled. Bring the storm.",
            "My blocks sound musical now.",
            "That impact? Barely noticed.",
            "Nice, I deflect better already.",
            "Shield feels part of me now.",
            "Defence upgrade, confidence upgrade.",
            "I could body-check a troll now.",
            "Harder, faster, still useless.",
            "Defence up. Attitude sharper.",
            "I am a wall with opinions.",
            "Your protector just got thicker armor.",
            "Ha, my guard bar grew.",
            "I deflected that with flair.",
            "Defence mastered one notch higher.",
            "Pressure’s nothing to me now.",
            "I block without thinking.",
            "My armour practically hums.",
            "Defence up. Pride intact.",
            "I could tank a dragon sneeze.",
            "That combo would have floored me yesterday.",
            "Perfect guard achieved. Easy.",
            "My shield likes me more now.",
            "Defence leveled, balance improved.",
            "Less bruises, more laughs.",
            "I just evolved into a human barricade.",
            "Guard technique flawless.",
            "Defence feels natural now.",
            "I could take a boulder to the ribs.",
            "More armour, less drama.",
            "Defence level up. Momentum steady.",
            "I’m learning to stand firm.",
            "Every block feels cleaner.",
            "Defence training worked.",
            "My guard stance looks cooler now.",
            "That swing bounced right off me.",
            "I can feel my timing improving.",
            "Defence up. Precision up.",
            "My aura screams do not even try.",
            "I can guard in my sleep.",
            "New skill tier unlocked. Defence queen.",
            "I feel indestructible today.",
            "That hit didn’t even nudge me.",
            "My defence stat just flexed.",
            "Blocking is my new cardio.",
            "Defence up. Rhythm flawless.",
            "Hits glance away like rain.",
            "My armour sings with power.",
            "Guard efficiency up. Stress down.",
            "I’m turning into a walking fortress.",
            "Defence level gained. Calm achieved.",
            "My stance is unbreakable.",
            "I can hold this line forever.",
            "Defence higher, patience higher.",
            "That blow slid right off.",
            "I’m practically a shield maiden now.",
            "Resilience level increased. Try me.",
            "My shield just winked at me.",
            "Defence up. Steadfast as stone.",
            "I could hold a doorway forever.",
            "Another level of unmovable unlocked.",
            "I feel more stable under fire.",
            "My reactions are bulletproof.",
            "Defence training complete. For now.",
            "I am a professional wall.",
            "Defence skill increased. Noted.",
            "New guard threshold achieved.",
            "I stand taller and hit less.",
            "I block like it’s instinct now.",
            "My defence bar is smiling.",
            "That punch rebounded off me.",
            "I am as solid as granite.",
            "Defence level up. I love it.",
            "Try harder, villains.",
            "I’m in permanent guard mode.",
            "Shield discipline perfect.",
            "My stance feels effortless.",
            "Defence higher. Mood higher.",
            "I can take a hit with grace now.",
            "I’m officially iron-hearted.",
            "Defence improved, reflex synced.",
            "I could shrug off a hammer hit.",
            "My guard’s unshakable.",
            "Defence up. Easy work.",
            "I’m harder to hurt and prettier than ever.",
            "Another level of toughness earned.",
            "I block faster than I blink.",
            "My defence just reached a new tier.",
            "Damage mitigation achieved.",
            "Defence level up. Victory certain.",
            "Blocking feels like dancing now.",
            "I could stand in a storm and smile.",
            "My guard aura glows brighter.",
            "Defence up again, ha.",
            "I can stand through anything.",
            "Steel and will both leveled.",
            "Defence stronger. Fear weaker.",
            "New milestone: unbreakable Isla.",
            "Ha, I reflect power with poise.",
            "Defence up, serenity achieved.",
            "My body learned to parry pain.",
            "Attacks bounce off like compliments.",
            "Defence skill rising steadily.",
            "That strike just made me laugh.",
            "I feel perfectly guarded.",
            "Defence leveled again. Progress steady.",
            "Enemy hits just feel softer.",
            "My stance has rhythm now.",
            "I’m a statue with sass.",
            "Defence up, tension gone.",
            "Shield synergy perfect. Love it.",
            "I’m the calm in the clash.",
            "Defence stronger. Doubt weaker.",
        };

        /// <summary>Fallback message returned when the defence pool is empty.</summary>
        private const string DefenceLevelUpFallbackLine = "My defence leveled up. Try hitting me now.";

        /// <summary>
        /// Pool of flavour messages used when the player levels Defence while a companion is active.
        /// Provides varied chat so defensive milestones feel celebrated.
        /// </summary>
        private static readonly string[] PlayerDefenceLevelUpChatMessages =
        {
            "Your defence just leveled up you are built like a wall now",
            "You are getting tougher every day",
            "Nice one you can take a hit and keep going",
            "Another defence level that armor really suits you",
            "You are harder to hurt already",
            "That stance looked perfect",
            "You are becoming unbreakable",
            "Your blocks are getting cleaner",
            "That level gave you serious resilience",
            "You look ready to tank anything",
            "Your timing on defence is incredible",
            "You could deflect a boulder with that form",
            "That guard was flawless",
            "You are stronger and steadier than before",
            "Your defence training is paying off",
            "You just shrugged that off like nothing",
            "That hit barely touched you",
            "Your reaction time is getting faster",
            "You are becoming impossible to knock down",
            "That defence level up was impressive",
            "You could block an arrow with confidence now",
            "Your guard looks solid",
            "You are learning to control every impact",
            "That level up made you a fortress",
            "Your posture is perfect under pressure",
            "You can absorb damage like it is nothing",
            "You are tougher than half the beasts out there",
            "That shield work is brilliant",
            "Your defence is growing beautifully",
            "You could hold a doorway alone now",
            "You are officially built to survive",
            "That stance looked unshakable",
            "You are getting more durable every level",
            "Your balance is rock solid",
            "You can take a heavy blow and keep moving",
            "That level gave you refined resilience",
            "Your defence skill keeps climbing",
            "You are like an immovable statue now",
            "That guard was professional",
            "You are deflecting hits with grace",
            "Your armour work looks perfect",
            "That defence gain suits you perfectly",
            "You can take on tougher enemies easily now",
            "Your stance radiates confidence",
            "You are learning to predict every attack",
            "That shield is practically part of you",
            "You are a living fortress",
            "Your defence level just went up again",
            "That was perfect timing on the block",
            "You are calmer under pressure now",
            "Your protection instinct is amazing",
            "You are too steady to fall now",
            "That hit looked like it bounced off you",
            "You are getting ridiculously tanky",
            "Your defence form is improving fast",
            "That level added serious endurance",
            "You are mastering the art of not flinching",
            "Your guard feels effortless now",
            "You are holding your ground perfectly",
            "That shield never moves out of place",
            "You could protect a whole squad now",
            "Your body looks ready to take any hit",
            "You are tougher than a mountain",
            "That level made you untouchable",
            "You are holding your own like a veteran",
            "Your guard strength is unmatched",
            "That block was smooth and fast",
            "You are learning to read attacks instantly",
            "Your defence skill is through the roof",
            "You could block a spell with that posture",
            "That level up looked heroic",
            "You are deflecting damage like a pro",
            "Your armour fits your confidence now",
            "That guard felt natural",
            "You are becoming a master of resilience",
            "Your body moves in perfect defence rhythm",
            "That level up made you look unbreakable",
            "You can take a hit like it is nothing now",
            "Your defence aura feels strong",
            "You are absorbing impact like a champion",
            "That was textbook blocking form",
            "You are calmer with every incoming strike",
            "Your timing and balance are perfect",
            "You can guard without even thinking now",
            "That defence growth is incredible",
            "You are solid from head to toe",
            "Your reaction was lightning fast",
            "You are learning to block before you even see it",
            "That level made your guard flawless",
            "You are an expert at staying alive",
            "Your stance radiates confidence and control",
            "That was a smooth parry",
            "You are built for endurance",
            "Your shield glows with pride",
            "That defence skill level was well deserved",
            "You are protecting yourself like a pro",
            "Your body learned how to resist perfectly",
            "That block saved you easily",
            "You are mastering the flow of battle",
            "Your defence growth is beautiful to watch",
            "That level gave you serious toughness",
            "You could stop a charging beast now",
            "Your form is clean and strong",
            "That was a textbook defence move",
            "You are learning to turn hits into energy",
            "Your armour shines with pride",
            "That defence level gave you real stability",
            "You are standing tall through every strike",
            "Your focus in battle is unshakable",
            "That was a perfect guard again",
            "You are becoming a true shield of strength",
            "Your defence skill is rising steadily",
            "That hit looked like it never landed",
            "You are stronger in every sense now",
            "Your blocking precision is perfect",
            "That level brought balance and patience",
            "You can hold a defensive stance forever now",
            "Your body adapts beautifully under fire",
            "That block was effortless",
            "You are mastering durability itself",
            "Your movements are calm and ready",
            "That level gave you confidence in every stance",
            "You are practically invincible now",
            "Your guard is flawless",
            "That defence level up looked perfect",
            "You are built like a tank and move like a dancer",
            "Your protection skills are unmatched",
            "That stance was elegant and strong",
            "You are holding firm and fearless",
            "Your defence shines with power now",
            "That was mastery in motion",
            "You could take an army and still stand tall",
            "Your resilience is inspiring",
            "That level completed your armour of will",
            "You are turning pain into pride",
            "Your guard is becoming legendary",
            "That defence boost made a real difference"
        };

        /// <summary>Fallback message returned when the player defence pool is empty.</summary>
        private const string PlayerDefenceLevelUpFallbackLine = "Your defence just leveled up you are built like a wall now";

        /// <summary>Pool of flavour messages emitted when the companion levels Strength.</summary>
        private static readonly string[] StrengthLevelUpChatMessages =
        {
            "My strength just went up, I can feel it.",
            "I hit harder already.",
            "Guess who just got stronger.",
            "I could break rocks with this grip.",
            "Another strength level, nice.",
            "I’m getting scary strong.",
            "That last swing felt heavier.",
            "My power just spiked.",
            "I could lift you now, easily.",
            "Strength training pays off again.",
            "I’m built different now.",
            "That punch echoed, did you hear it.",
            "Ha, muscles on point.",
            "Level up, stronger than before.",
            "My arms feel amazing.",
            "I can crush apples with one hand now.",
            "Another level, another flex.",
            "Look out world, I’m buff.",
            "My strength stat is flexing proudly.",
            "I feel unstoppable today.",
            "That impact felt good.",
            "I just broke my old record.",
            "Power surge detected, it’s me.",
            "My muscles are singing.",
            "Stronger and proud of it.",
            "I’m practically pure brawn now.",
            "That hit shook the ground a little.",
            "Strength up, ego up.",
            "I’m a one-woman wrecking crew.",
            "Every swing lands heavier now.",
            "Guess who can lift a tree.",
            "Strength leveled, respect earned.",
            "I feel charged with energy.",
            "I’m so strong it’s almost unfair.",
            "My strikes sound meaner.",
            "Power flows through every punch.",
            "Another level, another dent in reality.",
            "I’m built like a hammer now.",
            "Strength up, everything else downwind.",
            "I just gained a ton of force.",
            "Feel that vibration, that’s me.",
            "I can bend metal for fun now.",
            "My swing speed and power both improved.",
            "Another step toward unstoppable.",
            "Strength stat glows bright today.",
            "I’m officially terrifying.",
            "I could arm wrestle fate itself.",
            "Stronger again, love to see it.",
            "My muscles hum like magic.",
            "I could tear through armor now.",
            "Strength level up complete.",
            "That target didn’t stand a chance.",
            "I’m radiating raw power.",
            "New record for destruction set.",
            "I just hit something too hard, sorry.",
            "My punches leave craters now.",
            "Strength gain confirmed.",
            "More might in every move.",
            "I feel fierce and focused.",
            "My arms feel like iron.",
            "I can swing heavier weapons now.",
            "Strength leveled, confidence leveled.",
            "I’m turning into a powerhouse.",
            "Every hit shakes the air.",
            "Stronger again, easy work.",
            "That felt like a mini earthquake.",
            "I could throw an ogre if I wanted.",
            "Strength up, finesse optional.",
            "I feel absurdly strong right now.",
            "Another milestone, more muscle.",
            "Power like this should be illegal.",
            "My hits echo through time.",
            "Strength gain, satisfaction high.",
            "I can dent stone with a punch.",
            "That enemy definitely felt that.",
            "Strength level increased again.",
            "My body hums with power.",
            "Every strike feels explosive.",
            "I could lift a boulder and laugh.",
            "Strength and swagger both leveled.",
            "I’m a walking siege engine.",
            "That was a heavy hit, even for me.",
            "Strong and stylish, best combo.",
            "Another level, another legend made.",
            "I’m breaking limits again.",
            "Strength up, mercy down.",
            "I feel like a living weapon.",
            "My next hit might cause headlines.",
            "That sound was the air crying.",
            "New strength level achieved, perfection.",
            "I’m practically glowing with muscle now.",
            "I could tear open steel doors.",
            "Power feels natural now.",
            "Strength improved, patience not so much.",
            "I’m a juggernaut with good hair.",
            "I can feel the ground tremble when I move.",
            "More might, less mercy.",
            "I’m beyond buff, I’m art.",
            "Strength increased, I need bigger gloves.",
            "My knuckles are insured now.",
            "I hit things harder just by looking at them.",
            "Strength gain achieved, fear me.",
            "My power output is ridiculous.",
            "I could split logs barehanded.",
            "I’m fueled by pure determination.",
            "Strength training works wonders.",
            "I’m leveling my way into legend.",
            "Another strength tier reached.",
            "I swing like thunder now.",
            "Power courses through me nonstop.",
            "I’m harder, faster, stronger.",
            "Strength up, let’s test it.",
            "I can punch gravity in the face now.",
            "That impact shook my soul in a good way.",
            "Strong enough to guard, strong enough to win.",
            "My strength bar just smiled.",
            "I could crush a door by accident now.",
            "Feeling mighty and magnificent.",
            "Another strength level, who’s next.",
            "My punches hum like drums.",
            "Strength stat maxing nicely.",
            "I’m hitting peak form.",
            "More strength means less hesitation.",
            "That felt satisfying, utterly.",
            "Strong again, proud as always.",
            "I just made the ground jealous.",
            "Strength achieved, elegance optional.",
            "I’m energy condensed into muscle.",
            "My power scares small wildlife.",
            "Every blow lands with confidence.",
            "I could uproot a small forest.",
            "Strength up, spirit up.",
            "I feel legendary again.",
            "Another level, I love progress.",
            "I’m living proof that lifting works.",
            "My strikes leave shockwaves now.",
            "Strength level increased, respect it.",
            "Pure muscle, pure chaos.",
            "Another gain, no sweat.",
            "I’m as strong as I feel beautiful.",
            "Strength maxing, motivation high."
        };

        /// <summary>Fallback message returned when the strength pool is empty.</summary>
        private const string StrengthLevelUpFallbackLine = "My strength just went up, I can feel it.";

        /// <summary>
        /// Pool of flavour messages used when the player levels Strength while a companion is active.
        /// Keeps melee milestones celebrated with unique supportive chatter.
        /// </summary>
        private static readonly string[] PlayerStrengthLevelUpChatMessages =
        {
            "Your muscles are showing real progress now",
            "You are getting stronger every day",
            "Another strength level nice work",
            "That swing looked powerful",
            "You hit like you mean it now",
            "Your training is paying off",
            "You could lift a bear at this rate",
            "That level gave you serious muscle",
            "You are built for battle now",
            "Your punches sound heavier already",
            "Ha you are getting scary strong",
            "That was pure strength not luck",
            "You look unstoppable right now",
            "Your power is rising fast",
            "You are starting to crush everything you hit",
            "That strength gain was impressive",
            "You could tear metal with those arms",
            "Your hits echo through the ground",
            "Another level and more brute force",
            "You are getting too strong for wooden weapons",
            "That was a clean powerful blow",
            "You are officially tank material now",
            "Your body moves with pure force",
            "You are built like a warrior now",
            "That level up gave you serious power",
            "You hit harder than you realize",
            "Your strength feels limitless right now",
            "You could split stone with that grip",
            "That training routine is working",
            "Your muscles look ready for anything",
            "You are carrying that power well",
            "That level made your stance perfect",
            "You swing like a seasoned fighter",
            "Your grip strength is unreal now",
            "Every strike lands with confidence",
            "You are getting seriously ripped",
            "That level gave you real impact",
            "You could take down an ogre with that swing",
            "Your hits sound like thunder",
            "That strength growth is amazing",
            "You are turning raw power into skill",
            "Your body moves with strength and precision",
            "You are becoming a true powerhouse",
            "That was a powerful move nice job",
            "You are strong enough to carry the team now",
            "Your arms look carved from stone",
            "Another strength level complete",
            "You could crush armor with that strike",
            "Your muscles look ready to destroy mountains",
            "You are hitting harder every time",
            "That power is starting to scare me a little",
            "You are strong in every sense now",
            "Your weapon shakes when you swing from sheer force",
            "That level up gave you incredible might",
            "You are making brute strength look elegant",
            "Your blows hit with confidence now",
            "That swing had perfect power behind it",
            "You could carry me and still fight",
            "Your strength level just spiked beautifully",
            "Every motion looks powerful now",
            "Your body radiates raw energy",
            "You are looking like a proper brawler",
            "That was a solid heavy hit",
            "You are breaking limits with every level",
            "Your strength is unreal lately",
            "You could punch through a wall now",
            "That level gave you unstoppable force",
            "Your arms could move mountains",
            "You are stronger than I expected",
            "That hit made the ground flinch",
            "Your power keeps growing",
            "You are built like a hero now",
            "That swing nearly broke the air",
            "You are hitting like a champion",
            "Your strength level is through the roof",
            "That was pure muscle in motion",
            "You could throw giants at this point",
            "Your power feels natural now",
            "You are turning into a living weapon",
            "That level was well deserved",
            "You are too strong for simple training now",
            "Your grip alone could win fights",
            "That strike looked effortless and brutal",
            "You are all strength and confidence",
            "Your power control is improving fast",
            "You are built for destruction now",
            "That swing looked cinematic",
            "You are getting monstrous in the best way",
            "Your arms could rival a golem's now",
            "That level up made you dangerous",
            "You are breaking barriers with your strength",
            "Your raw power is unmatched lately",
            "You could lift a boulder just for fun",
            "That strength gain was impressive to watch",
            "You are stronger than half the monsters we fight",
            "Your muscles are pure magic now",
            "That hit looked like it hurt the planet",
            "You are getting stronger by the heartbeat",
            "Your presence feels powerful now",
            "That level gave you serious edge",
            "You could pull trees out of the ground",
            "Your strength keeps surprising me",
            "That blow nearly created a crater",
            "You are a powerhouse in motion",
            "Your body is pure momentum now",
            "That was a clean display of power",
            "You are leveling strength faster than anyone",
            "Your arms look carved by victory itself",
            "That level up was beautiful to watch",
            "You could fight barehanded now",
            "Your power feels limitless already",
            "That was perfect strength control",
            "You are stronger every time we fight",
            "Your swing looked flawless",
            "That hit could break steel",
            "You are walking proof that training works",
            "Your strength is starting to feel mythical",
            "That level brought out your inner beast",
            "You are hitting like a legend now",
            "Your power shines brighter every day",
            "That swing was perfection",
            "You could crush fate itself with that strength",
            "Your body and will are in perfect sync",
            "That level made you unstoppable",
            "You are a living mountain now",
            "Your strength has no limits anymore"
        };

        /// <summary>Fallback message returned when the player strength pool is empty.</summary>
        private const string PlayerStrengthLevelUpFallbackLine = "Your muscles are showing real progress now";

        /// <summary>Pool of flavour messages emitted when the companion levels Attack.</summary>
        private static readonly string[] AttackLevelUpChatMessages =
        {
            "My attack just leveled up, sharper than ever.",
            "I can hit faster and harder now.",
            "Another attack level, nice.",
            "My strikes land cleaner every time.",
            "I’m getting dangerously accurate.",
            "Guess who’s deadlier today.",
            "My blade feels lighter and meaner.",
            "Attack up, elegance intact.",
            "I could split an arrow midair now.",
            "I’m faster, stronger, better.",
            "My swings feel smooth and deadly.",
            "I can’t miss today, even if I tried.",
            "New level achieved, precision unlocked.",
            "I’m slicing air like butter.",
            "Ha, my attacks sound musical now.",
            "I just cut the wind in half.",
            "Attack skill up, reflexes sharper.",
            "That swing felt perfect.",
            "Targets beware, I leveled again.",
            "I’m officially too quick for them.",
            "Every strike feels effortless now.",
            "My accuracy’s scary good.",
            "Attack level up, confidence up.",
            "I could duel a mirror and win.",
            "That hit felt clean.",
            "Sharper timing, smoother rhythm.",
            "Another attack level gained, woo.",
            "I’m unstoppable with this weapon.",
            "Attack improved, hesitation removed.",
            "Ha, my blade hums with precision.",
            "I’m faster than their thoughts now.",
            "Guess who’s the sharpest in town.",
            "Attack up, enemies down.",
            "I’m landing crits like it’s luck, but it’s not.",
            "Another skill tick up, I love it.",
            "My arm’s steady as stone.",
            "Each swing cuts cleaner now.",
            "I’m striking like lightning.",
            "Attack leveled, speed tripled.",
            "That combo felt flawless.",
            "I just cut through their guard like paper.",
            "Sharper instincts, deadlier swings.",
            "I could duel blindfolded now.",
            "My weapon practically dances with me.",
            "Attack power increased, danger increased.",
            "I’m swinging like I was born to fight.",
            "Another level up, reflexes crisp.",
            "Precision feels natural now.",
            "I’m hitting pressure points on purpose.",
            "Attack skill up, artistry improved.",
            "Every motion feels precise.",
            "New level, new form mastery.",
            "I’m slicing through shadows now.",
            "Ha, they can’t even parry me anymore.",
            "My strikes leave echoes now.",
            "I hit where I aim, every time.",
            "Attack upgraded, mistakes deleted.",
            "I’m faster on the draw now.",
            "New tier of finesse unlocked.",
            "That swing was poetry.",
            "Every strike lands like it’s scripted.",
            "Attack up, rhythm perfect.",
            "My movements are too clean now.",
            "Ha, even I’m impressed.",
            "Another level, another masterpiece.",
            "I fight like a storm in control.",
            "Precision kills, and I’m precise.",
            "Attack level up, reaction time shortened.",
            "My timing’s impeccable now.",
            "Every swing sings to me.",
            "Attack increased, flow achieved.",
            "I could slice thoughts in half now.",
            "That form was flawless.",
            "Ha, I’m a blur with a blade.",
            "Attack mastery in progress.",
            "Sharper focus, sharper hits.",
            "Even my shadow flinched.",
            "Another clean upgrade for me.",
            "I’m officially dangerous.",
            "Attack stat looking fine today.",
            "My reflexes just leveled up.",
            "I strike faster than fear now.",
            "Each blow feels stronger, cleaner.",
            "Ha, my combo game’s untouchable.",
            "Attack up again, power flowing.",
            "I could take on two at once now.",
            "Timing feels like instinct.",
            "I’m precision in motion.",
            "New attack level, sweet.",
            "Every hit lands right where I want it.",
            "I’m swinging smarter, not harder.",
            "Attack increased, fun increased.",
            "That felt smooth and deadly.",
            "My reach, my timing, perfect.",
            "I’m too clean with it now.",
            "Another attack level, progress steady.",
            "I’m cutting through defence like mist.",
            "Attack up, coordination perfect.",
            "I could carve my name mid-fight.",
            "My weapon hums approval.",
            "Sharper blade, sharper me.",
            "Ha, that swing whistled.",
            "Attack level up, flawless execution.",
            "My attacks look cinematic now.",
            "I’m a machine of accuracy.",
            "Each strike feels meant to be.",
            "I’m deadly with style now.",
            "Attack boosted again, I love it.",
            "My weapon and I are in sync.",
            "That hit left a mark, and pride.",
            "Attack leveled, finesse refined.",
            "I’m the definition of precision.",
            "My blade cuts before they blink.",
            "Another level, cleaner execution.",
            "I swing like the wind now.",
            "Ha, I’m perfection with steel.",
            "Attack mastery just clicked.",
            "My accuracy’s beautiful now.",
            "Level up achieved, rhythm perfected.",
            "Every motion feels natural now.",
            "I could duel ten in a row easily.",
            "Attack increased, elegance maxed.",
            "I’m dancing through combat.",
            "Each hit lands like a heartbeat.",
            "Attack up, pure fluid motion.",
            "I’m faster than the idea of speed.",
            "Ha, I’m scary good with this blade.",
            "Another attack milestone done.",
            "My swings sound like thunder now.",
            "I’m striking with purpose and power.",
            "Attack up again, keep ‘em coming.",
            "My hands move before thought.",
            "I can hit weak points blindfolded.",
            "Precision feels divine.",
            "Attack skill improved, reaction sharpened.",
            "I’m attacking like an artist paints.",
            "My form feels flawless.",
            "Level gained, precision insane.",
            "I’m untouchable when I strike.",
            "Attack leveled up, beautiful stuff.",
            "My blade sings in rhythm.",
            "New level achieved, confidence maxed.",
            "Every swing tells a story now.",
            "I’m striking faster and smarter.",
            "Attack up, patience perfected.",
            "I could dance circles around enemies.",
            "My weapon’s an extension of me.",
            "Attack skill up, elegance intact.",
            "I’m becoming poetry in motion.",
            "Every strike feels perfect today.",
            "I could hit a falling leaf midair.",
            "Attack mastery rising, I love it.",
            "I’m sharper than a rumor now.",
            "Another attack level, unstoppable rhythm.",
            "My strikes whisper precision.",
            "I fight cleaner than wind moves.",
            "Attack increased, balance flawless.",
            "Perfect form, perfect focus.",
            "My accuracy’s frightening even to me.",
            "I could slice sound itself.",
            "Attack level up, feel that flow.",
            "I’m all edge, all grace.",
            "My combos feel effortless now.",
            "Attack improved, momentum flawless.",
            "Every motion hits like a symphony.",
            "I’m pure control with a blade.",
            "Attack up, fear none.",
            "Ha, my form’s art now.",
            "Another level achieved, too smooth.",
            "My reflexes hum like magic.",
            "I’m striking faster than lightning.",
            "Attack skill leveled again, flawless work.",
            "I could cut a falling tear in half.",
            "Strength and grace, united.",
            "Attack mastery rising beautifully.",
            "I swing like a whisper and hit like thunder.",
            "My focus feels perfect now.",
            "Another attack level, sweet precision.",
            "I’m all rhythm and impact.",
            "My next hit will be art.",
            "Attack up again, clean and sharp.",
            "I’m unstoppable and elegant.",
            "Every swing’s a statement.",
            "Attack level gained, I’m feeling deadly."
        };

        /// <summary>Fallback message returned when the attack pool is empty.</summary>
        private const string AttackLevelUpFallbackLine = "My attack just leveled up, sharper than ever.";

        /// <summary>
        /// Pool of flavour messages used when the player levels Attack while a companion is active.
        /// Keeps combat celebrations varied with OSRS-inspired praise.
        /// </summary>
        private static readonly string[] PlayerAttackLevelUpChatMessages =
        {
            "Your attacks are looking sharper already",
            "Nice swing that form is improving fast",
            "You just leveled your attack again keep it up",
            "I can see your strikes landing cleaner now",
            "You are getting faster with that weapon",
            "That level up looked deadly",
            "Your aim is on point today",
            "Another level stronger precision",
            "Your strikes hit like thunder now",
            "You are slicing through enemies like butter",
            "That swing looked professional",
            "You are starting to fight like a master",
            "Your attack skill is climbing beautifully",
            "You are hitting cleaner and harder each time",
            "That was a perfect combo right there",
            "Your accuracy just went through the roof",
            "You fight smoother every level",
            "You could teach swordplay with moves like that",
            "Your weapon looks like part of you now",
            "That attack level was well deserved",
            "Your form is pure power and focus",
            "You are starting to move like lightning",
            "Your attacks look effortless now",
            "That hit cracked the air itself",
            "You are turning into a real fighter",
            "Your blade work is stunning",
            "Another level your reflexes are scary good",
            "You are hitting faster every time I blink",
            "That combo was beautiful to watch",
            "You fight like you were born to do this",
            "Your precision is incredible now",
            "You are learning to hit before they even move",
            "That strike was pure control",
            "Your attack leveled again nice work",
            "You are a blur with a weapon now",
            "Every hit lands exactly where you want it",
            "You are becoming unstoppable with that blade",
            "That swing looked almost too perfect",
            "You fight like an artist now",
            "Your attacks are smoother than silk",
            "You could duel a master and win now",
            "That level up made you dangerous",
            "You are moving faster than the wind",
            "Your timing just leveled up beautifully",
            "You are striking harder than before",
            "That hit shook the ground",
            "Your attack power is incredible now",
            "You are making this look too easy",
            "That swing had real style",
            "You fight with rhythm and grace now",
            "Your accuracy is frightening in a good way",
            "You are reading your opponents perfectly",
            "That attack level boosted your confidence",
            "You could hit a falling leaf now",
            "Your weapon hums when you move",
            "You are striking faster than thought itself",
            "That combo flowed perfectly",
            "You are mastering combat one swing at a time",
            "Your strikes cut cleaner than ever",
            "You are learning the art behind every blow",
            "That level up was pure skill not luck",
            "You could duel blindfolded at this rate",
            "Your hands move with precision now",
            "That was a flawless strike",
            "You fight like a storm with direction",
            "Your attack leveled again power rising",
            "You are hitting like a professional warrior",
            "That blow sounded satisfying",
            "Your reactions are lightning fast now",
            "You are controlling the fight completely",
            "That level up made your weapon proud",
            "You fight like every motion counts",
            "Your strikes are faster every level",
            "You are practically unstoppable in battle now",
            "That hit landed perfectly",
            "You are cutting through the air like a master",
            "Your attack level increased nice progress",
            "You move like a seasoned duelist now",
            "Your accuracy just keeps improving",
            "That strike hit right where it needed to",
            "You are becoming scary good with timing",
            "Your movements are razor sharp now",
            "That level was pure muscle memory",
            "You are turning precision into art",
            "Your focus is unmatched in combat",
            "You hit like someone who means business",
            "That attack level was well earned",
            "You are fast calm and deadly",
            "Your swings are too clean for training level fights",
            "You are in perfect sync with your weapon",
            "That combo could win tournaments",
            "You are learning balance and speed perfectly",
            "Your attack style is evolving beautifully",
            "You are landing flawless blows now",
            "That strike was textbook perfect",
            "You are moving smoother every fight",
            "Your attack level is rising steadily",
            "You could duel without breaking a sweat now",
            "You are mastering rhythm and control",
            "That was a clean hit if I ever saw one",
            "You are fighting like a champion now",
            "Your attack technique is polished and fierce",
            "You swing like you own the battlefield",
            "That level up just made your stance perfect",
            "You are becoming a true sword artist",
            "Your blade speed doubled after that level",
            "You are fighting smarter every battle",
            "That attack improvement shows",
            "You are flowing through combat like water",
            "Your strikes never miss their mark",
            "You are stronger faster calmer every swing",
            "That level brought out your killer instinct",
            "You are reading openings perfectly now",
            "Your attacks look almost choreographed",
            "You are getting dangerously skilled",
            "That strike was nothing short of beautiful",
            "You could take on anything with precision like that",
            "Your control and power are finally in balance",
            "You are fighting like poetry in motion",
            "That attack level was pure mastery",
            "You are cutting faster than sound itself",
            "Your timing is beyond impressive",
            "That was a perfect execution",
            "You are wielding that weapon like destiny itself",
            "Your attacks just leveled to perfection"
        };

        /// <summary>Fallback message returned when the player attack pool is empty.</summary>
        private const string PlayerAttackLevelUpFallbackLine = "Your attacks are looking sharper already";

        /// <summary>
        /// Pool of flavour messages used when the player levels Ranged while an active companion is
        /// present. Provides celebratory callouts that emphasise precision to mirror the ranged skill
        /// fantasy.
        /// </summary>
        private static readonly string[] PlayerRangedLevelUpChatMessages =
        {
            "Your aim just leveled up nicely done",
            "You are getting precise with that bow",
            "Another ranged level your accuracy is scary now",
            "That shot was perfect",
            "You are hitting everything today",
            "Your aim keeps improving beautifully",
            "You are turning into a real sharpshooter",
            "That arrow flew like it had a mind of its own",
            "You are hitting targets faster than I can blink",
            "Your ranged skill is climbing perfectly",
            "That was a clean bullseye",
            "You are learning to control every shot",
            "Your accuracy feels supernatural now",
            "That level made your form smoother",
            "You could split an arrow midair now",
            "Your aim is pure art",
            "That shot was poetry in motion",
            "You are starting to look like a hunter out of legend",
            "Your arrows fly straighter than ever",
            "That ranged level paid off beautifully",
            "You can shoot faster than the wind now",
            "Your precision is unreal",
            "That hit landed exactly where you wanted",
            "You are mastering distance and focus",
            "Your aim is sharper than my patience",
            "That was a beautiful draw and release",
            "You could teach archery with skills like that",
            "Your ranged technique keeps improving",
            "That level brought serious control",
            "You are reading the wind perfectly now",
            "Your arrows never miss anymore",
            "That shot looked professional",
            "You are getting faster every level",
            "Your ranged control feels natural now",
            "That was a shot worth bragging about",
            "You are officially a long range threat",
            "Your focus is unbelievable lately",
            "That level gave you perfect hand eye balance",
            "You are in complete control of your bow",
            "Your shots land smoother than silk",
            "That was a graceful hit",
            "You are turning aim into instinct",
            "Your ranged skill feels second nature now",
            "That was beautiful precision",
            "You could shoot through a storm and still hit",
            "Your arrows hit before I even blink",
            "That level up made you faster than ever",
            "You are aiming like a master now",
            "Your draw speed is incredible",
            "That hit was so clean I almost clapped",
            "You are learning to feel every shot",
            "Your ranged accuracy is unmatched now",
            "That was flawless form",
            "You could shoot the candle out and not touch the flame",
            "Your aim is so clean it is unfair",
            "That level up suits you perfectly",
            "You are starting to feel the rhythm of the bowstring",
            "Your precision grows with every shot",
            "That hit was cinematic",
            "You could win tournaments now",
            "Your ranged skill is at its best yet",
            "That shot was pure confidence",
            "You are aiming like the wind listens to you",
            "Your timing is perfect now",
            "That level made your reflexes faster",
            "You are too accurate for normal training now",
            "Your arrows fly like streaks of light",
            "That was a perfect landing on target",
            "You are hitting bullseyes for fun now",
            "Your focus is unshakable",
            "That ranged skill increase was beautiful",
            "You are aiming smoother than ever",
            "Your next shot might break a record",
            "That level up improved your control perfectly",
            "You are hitting moving targets easily now",
            "Your bow feels like an extension of you",
            "That release was flawless",
            "You are mastering distance like a professional",
            "Your arrows do not even wobble anymore",
            "That ranged progress is stunning",
            "You are consistent with every pull",
            "Your focus is calm and steady",
            "That shot was timed to perfection",
            "You are practically part of the wind now",
            "Your accuracy feels natural",
            "That level made you look unstoppable",
            "You are learning to lead shots perfectly",
            "Your arrows are landing before the echo fades",
            "That was an impressive long shot",
            "You are getting too good at this",
            "Your ranged form keeps improving",
            "That hit would make a ranger jealous",
            "You are aiming faster than I can follow",
            "Your shots are cleaner than ever",
            "That level brought out your inner marksman",
            "You are practically reading the air",
            "Your bow hums when you draw it now",
            "That release was smooth as water",
            "You are landing perfect hits at any range",
            "Your accuracy just keeps growing",
            "That was a graceful bullseye",
            "You could shoot through a keyhole now",
            "Your aim is so perfect it is intimidating",
            "That level up was flawless",
            "You are learning the patience of a true archer",
            "Your arrows move exactly how you want them to",
            "That hit was effortless",
            "You are connecting every shot with precision",
            "Your ranged skill makes it look easy",
            "That level gave you absolute control",
            "You are faster calmer and sharper than ever",
            "Your aim is nearly legendary now",
            "That was clean efficient and powerful",
            "You are a natural with that weapon",
            "Your control at range is beautiful",
            "That level up made you graceful and deadly",
            "You are mastering focus and patience",
            "Your arrows listen when you aim",
            "That was perfect you nailed it",
            "You are officially a sniper now",
            "Your aim never wavers",
            "That ranged skill increase shows your dedication",
            "You are calm and accurate every time",
            "Your focus is like stone and silk combined",
            "That level made your accuracy flawless",
            "You are reading distance like instinct",
            "Your next shot will be even cleaner",
            "That hit was exactly on target",
            "You are precision itself",
            "Your bow sings every time you fire",
            "That level made you a true marksman"
        };

        /// <summary>Fallback message returned when the player ranged pool is empty.</summary>
        private const string PlayerRangedLevelUpFallbackLine = "Your aim just leveled up nicely done";

        /// <summary>
        /// Pool of flavour messages used when the player levels Magic while a companion is active.
        /// Highlights arcane growth to keep the companion banter aligned with spellcasting milestones.
        /// </summary>
        private static readonly string[] PlayerMagicLevelUpChatMessages =
        {
            "Your magic just leveled up I can feel the air buzzing",
            "You are glowing a little brighter now",
            "That spell looked amazing",
            "You are getting good at this sorcery thing",
            "Your control over mana is impressive",
            "That level gave your aura some serious sparkle",
            "You cast that perfectly",
            "You are getting faster at channeling energy",
            "Your magic feels stronger already",
            "That spell shimmered like starlight",
            "You are making spellcasting look effortless",
            "Your magic is flowing smoother every time",
            "That was beautiful work",
            "You are learning to bend energy like art",
            "Your aura feels calm and powerful now",
            "That spell would make any wizard jealous",
            "You are controlling power with grace",
            "Your magic skill keeps rising nicely",
            "That level gave you some real presence",
            "You are becoming a real mage now",
            "Your energy feels balanced and bright",
            "That was pure magic mastery",
            "You are radiating confidence and mana",
            "Your spells are looking clean and steady",
            "That cast was picture perfect",
            "You are weaving energy like a professional",
            "Your magic is blooming beautifully",
            "That level brought harmony to your aura",
            "You are learning to control chaos effortlessly",
            "Your mana control is improving fast",
            "That spell felt powerful and stable",
            "You are glowing with arcane focus",
            "Your movements while casting are flawless",
            "That was pure elegance",
            "You are mastering the rhythm of magic",
            "Your aura hums like a song now",
            "That level gave you beautiful precision",
            "You are controlling magic like a seasoned wizard",
            "Your spells feel alive",
            "That was a graceful burst of energy",
            "You are channeling with confidence now",
            "Your magic output looks incredible",
            "That spell was pure art in motion",
            "You are practically radiating mana",
            "Your focus and flow are improving perfectly",
            "That level made you look ethereal",
            "You are commanding the elements effortlessly",
            "Your spells sound cleaner every cast",
            "That incantation was flawless",
            "You are stronger and calmer with every level",
            "Your energy feels stable and powerful",
            "That spell was worthy of applause",
            "You are mastering the dance of magic",
            "Your mana feels endless lately",
            "That level up was pure brilliance",
            "You are starting to glow when you cast",
            "Your magic hums with perfection",
            "That was precision casting at its best",
            "You are turning spells into art",
            "Your control feels natural now",
            "That level gave you serious focus",
            "You are a natural born sorcerer",
            "Your energy radiates warmth and power",
            "That spell looked professional",
            "You are learning every school of magic easily",
            "Your magic is balanced beautifully",
            "That level made your aura stronger",
            "You are getting scary good with your spells",
            "Your focus is razor sharp now",
            "That cast felt like poetry",
            "You are shaping magic like clay",
            "Your mana flow is perfect now",
            "That level gave you deeper connection to power",
            "You are officially glowing with talent",
            "Your magic feels refined and confident",
            "That spell had perfect timing",
            "You are mastering energy control quickly",
            "Your aura looks amazing right now",
            "That level brought real stability",
            "You are casting faster and cleaner every time",
            "Your power is increasing beautifully",
            "That spell looked natural and elegant",
            "You are syncing with your mana perfectly",
            "Your focus is getting stronger each level",
            "That level up was pure sorcery",
            "You are glowing like a star now",
            "Your power feels limitless lately",
            "That cast was perfectly smooth",
            "You are shaping magic like a master sculptor",
            "Your aura is stronger than ever",
            "That level gave you excellent control",
            "You are channeling spells like second nature",
            "Your mana signature feels pure now",
            "That was textbook magical form",
            "You are getting graceful with your spellwork",
            "Your focus and finesse are stunning",
            "That level up suits you beautifully",
            "You are making spellcasting look easy",
            "Your power is radiating through the air",
            "That was a masterful incantation",
            "You are channeling raw energy with elegance",
            "Your aura expanded beautifully",
            "That spell was perfectly controlled",
            "You are improving faster than most mages dream of",
            "Your power hums through every word you speak",
            "That level gave your spells new energy",
            "You are mastering advanced casting already",
            "Your magic feels calm and powerful",
            "That was an impressive show of control",
            "You are glowing like a living rune",
            "Your energy is almost visible now",
            "That level brought serious refinement",
            "You are learning to balance power and beauty",
            "Your magic skill keeps climbing",
            "That cast was smoother than silk",
            "You are a true magician in motion",
            "Your spells never misfire anymore",
            "That level gave you near perfect focus",
            "You are becoming one with the arcane flow",
            "Your magic radiates confidence",
            "That was a flawless spell well done",
            "You are pure elegance and power combined",
            "Your aura sparkles like lightning now",
            "That level up was perfectly earned",
            "You are controlling magic like it was born from you",
            "Your casting rhythm is flawless",
            "That spell was perfect in every way",
            "You are glowing with pure magical power",
            "Your mana energy feels infinite now"
        };

        /// <summary>Fallback message returned when the player magic pool is empty.</summary>
        private const string PlayerMagicLevelUpFallbackLine = "Your magic just leveled up I can feel the air buzzing";

        /// <summary>
        /// Pool of flavour messages used when the player levels Beastmaster with a companion present.
        /// Emphasises the playful rivalry between the player and companion over who earned the XP.
        /// </summary>
        private static readonly string[] PlayerBeastmasterLevelUpChatMessages =
        {
            "Oh so I do all the work and you get the level nice",
            "Ha Beastmaster level huh you are welcome",
            "That one was totally my effort not yours",
            "I hope you are giving me a cut of that experience",
            "You are leveling from my skill I see how it is",
            "Guess who carried you to that level me",
            "Wow you leveled up just from watching me fight",
            "Beastmaster level achieved thanks to your favorite companion",
            "I feel like I should get half the credit here",
            "Looks like my claws are doing all the grinding",
            "You leveled up because I am amazing admit it",
            "Ha your Beastmaster skill grows every time I win",
            "I fight you level sounds fair",
            "You really should name that level Isla",
            "Another level courtesy of my hard work",
            "I am basically your personal XP machine",
            "Nice Beastmaster level maybe I will invoice you later",
            "That was all me you just stood there",
            "You are welcome for the easy level up",
            "I think that level belongs to me actually",
            "Look at you gaining levels while I do the slashing",
            "Beastmaster level up courtesy of your MVP",
            "I think I deserve a thank you or a snack",
            "Your skill went up because I am too good",
            "Ha I knew my greatness would rub off on you",
            "I am basically carrying the whole skill tree",
            "Another level already I am on fire",
            "Beastmaster level huh you really owe me now",
            "I should charge by the experience point",
            "Your Beastmaster skill just leveled because I did the work again",
            "Seems fair that I do the fighting and you get the fame",
            "I will allow you to enjoy my achievements",
            "That level up spark looked nice you are welcome",
            "Beastmaster level increased mostly due to my brilliance",
            "I see who is benefiting from my strength here",
            "Wow you leveled again and I am still unpaid labor",
            "I fight you smile great system",
            "You are just farming me for levels admit it",
            "Beastmaster experience through delegation classic move",
            "I think I am the real Beastmaster here",
            "Another level for you another bruise for me",
            "At least you look good while I do the work",
            "You are the manager I am the workforce huh",
            "Ha your Beastmaster skill grows every time I swing",
            "I did not know I came with free experience points",
            "You are welcome again for the level up",
            "I am starting to think I am the teacher here",
            "Your Beastmaster level owes me dinner",
            "I am literally leveling your skill by existing",
            "That level up had my name written all over it",
            "You should rename that skill Isla Appreciation",
            "Beastmaster level up see also me doing everything",
            "I am the beast in Beastmaster obviously",
            "Wow another level maybe I should slow down",
            "I am beginning to think you are using me for XP",
            "Ha you leveled up again you are so lucky to have me",
            "You are like a sponge for my talent",
            "Beastmaster level up powered by my attitude",
            "I swing you grin repeat",
            "That was all me again no big deal",
            "You leveled up and I did all the roaring",
            "I am the pet trainer and the pet apparently",
            "Beastmaster level up sponsored by Isla Incorporated",
            "You should probably thank me publicly",
            "I am the reason you have that new level glow",
            "You gain skill I gain exhaustion seems fair",
            "That level up should come with my autograph",
            "I could make a great business out of this",
            "Another level wow I must be amazing",
            "Beastmaster skill boosted by my brilliance again",
            "You leveled up just by standing next to greatness",
            "I swear you are getting XP from my personality",
            "Ha Beastmaster level another proof of my charm",
            "You would not level that fast without me around",
            "I am literally your walking buff",
            "You leveled up and I am still doing all the biting",
            "I should get my own skill line at this point",
            "Beastmaster level increased probably because I looked cool",
            "Nice one you are really good at watching me work",
            "I carry your skill tree like I carry this fight",
            "You are leveling through moral support I see",
            "Another level I will expect applause later",
            "I am too good you are lucky to have me",
            "Beastmaster level up I think I am the beast here",
            "You got stronger by association congrats",
            "That level up sound is starting to feel like my theme music",
            "Ha your skill improves every time I sneeze",
            "I think I am the XP farm now",
            "Beastmaster skill level up powered by my perfection",
            "You just leveled from proximity to awesome",
            "You are so welcome for that new level",
            "At this point I should get a salary",
            "You are getting good at letting me do everything",
            "I am carrying your progress like it is my hobby",
            "Beastmaster level up and I am still underpaid",
            "I could go on strike for better rewards",
            "Your level ups are my fanfare now",
            "I see the pattern you relax I perform",
            "Beastmaster level nice I only risked my life a little",
            "You owe me so many thank yous",
            "I think your skill depends entirely on me",
            "That level was powered by sheer Isla energy",
            "You should probably promote me after that one",
            "Beastmaster level increased as I predicted",
            "Ha look at you taking credit for my work again",
            "You are the luckiest summoner alive",
            "Beastmaster skill leveled nice teamwork mostly mine",
            "I might stop fighting just to test your skill gains",
            "You leveled up again because I am unstoppable",
            "Beastmaster level up team Isla wins again",
            "Congratulations you have officially mastered watching me win"
        };

        /// <summary>Fallback message returned when the player Beastmaster pool is empty.</summary>
        private const string PlayerBeastmasterLevelUpFallbackLine = "Oh so I do all the work and you get the level nice";

        /// <summary>Pool of flavour messages emitted when the companion levels Ranged.</summary>
        private static readonly string[] RangedLevelUpChatMessages =
        {
            "My ranged skill leveled up, target practice pays off.",
            "Ha, I could split an arrow now.",
            "Another ranged level, my aim’s ridiculous.",
            "I’m hitting bullseyes without trying.",
            "Accuracy like this should be illegal.",
            "Ranged up, confidence up.",
            "My shots feel smoother already.",
            "Every arrow lands exactly where I want it.",
            "I’m getting scary good with distance shots.",
            "Another level, another perfect hit.",
            "I could shoot the wings off a fly.",
            "My hands feel steady as stone.",
            "Ha, even the wind listens to me now.",
            "Ranged skill increased, control flawless.",
            "I can hit what I can barely see.",
            "New level achieved, my aim is art.",
            "That shot was beautiful, admit it.",
            "I’m officially a sharpshooter.",
            "Every pull feels cleaner now.",
            "Ranged up again, rhythm perfect.",
            "Targets fear me now.",
            "Ha, bullseye after bullseye.",
            "Another level of deadly accuracy.",
            "My bow hums when I draw it.",
            "I can hit from across the field now.",
            "Ranged leveled, grace improved.",
            "Every shot feels natural.",
            "Distance means nothing to me.",
            "Ranged up, precision maxed.",
            "My eyes feel like telescopes.",
            "I hit things I didn’t even aim for.",
            "Another level, another legend made.",
            "Ha, I’m basically a sniper now.",
            "My aim is frighteningly good.",
            "I could hit the moon if I wanted.",
            "Ranged increased, focus sharper.",
            "I see the target before it moves.",
            "Every shot sings today.",
            "Ha, I can predict where they’ll dodge.",
            "My arrows are guided by vibes.",
            "Another ranged level, perfection rising.",
            "I’m so accurate it’s unfair.",
            "Targets drop before they hear me.",
            "My draw speed doubled somehow.",
            "Ranged stat just flexed on everyone.",
            "I could pierce a thought with this aim.",
            "New level, new deadly calm.",
            "I shoot faster than they can blink.",
            "Ranged leveled, wind obeys me.",
            "Each shot lands exactly as planned.",
            "Accuracy up, enemies down.",
            "I’m in sync with the bow now.",
            "Ha, my bolts hum approval.",
            "I could hit your hat without grazing you.",
            "Ranged up, satisfaction achieved.",
            "Every target feels too easy.",
            "I’m too steady for physics now.",
            "Ha, another level, better focus.",
            "My fingers move before I think.",
            "Ranged power increased again.",
            "I can snipe across a canyon now.",
            "My shots curve perfectly with the wind.",
            "New level achieved, bow bonded.",
            "Ranged up, reactions faster.",
            "I could tag a leaf mid-fall.",
            "My accuracy’s terrifying today.",
            "I don’t miss anymore, simple.",
            "Ha, my aim feels magical.",
            "Ranged leveled, vision extended.",
            "Another step toward perfection.",
            "My arrows hit before I let go.",
            "I could blindfold myself and still hit.",
            "Ranged up, precision divine.",
            "I’m basically one with the bowstring.",
            "Every pull hums in tune.",
            "Targets appear afraid already.",
            "I’m faster and calmer than ever.",
            "Another ranged level, perfect timing.",
            "I could thread a needle from here.",
            "Ranged improved, satisfaction complete.",
            "Each shot feels like instinct.",
            "My aim borders on psychic now.",
            "Ha, I’m a natural-born sniper.",
            "Ranged up, posture flawless.",
            "Every bolt flies straight as truth.",
            "I can hit them before they blink.",
            "Accuracy perfect, heart rate calm.",
            "I’m in my element now.",
            "Another level, cleaner lines.",
            "Ranged leveled, grace unmatched.",
            "I could paint with arrows.",
            "My bow hums happily today.",
            "I’m quicker on the draw than light.",
            "Ranged up, targets trembling.",
            "I feel unstoppable with a bow.",
            "Every hit is poetic now.",
            "Ha, my aim’s a superpower.",
            "I can hit their shadow next time.",
            "Another level of precision unlocked.",
            "My shots slice the silence.",
            "Ranged leveled again, perfect control.",
            "I could write my name in arrows now.",
            "Every pull feels lighter and sharper.",
            "Ha, even the air parts for my arrows.",
            "I’m faster, calmer, deadlier.",
            "Ranged up, instincts refined.",
            "My bolts travel straighter than fate.",
            "I could split a falling drop of rain.",
            "Ranged leveled, elegance intact.",
            "I’m precision in motion.",
            "Targets don’t stand a chance anymore.",
            "Ranged skill up, peace achieved.",
            "My aim’s painting stars now.",
            "I could snipe while running backward.",
            "Ha, another level, smoother than silk.",
            "I’m so accurate it’s scary.",
            "Ranged up, I breathe with the wind.",
            "Every shot feels divine.",
            "I could hit a whisper from here.",
            "My arrows and I are soulmates now.",
            "Ranged leveled, serenity gained.",
            "Another step toward mastery.",
            "I’m deadly quiet and deadly fast.",
            "My bolts sing when they fly.",
            "Ranged up again, unstoppable focus.",
            "Ha, I just out-aimed my old self.",
            "I can shoot through pride and armor.",
            "My draw feels perfect now.",
            "Ranged skill leveled, still calm.",
            "Every hit makes me smile.",
            "I’m one with precision itself.",
            "Ranged increased, awareness expanded.",
            "Ha, my aim is too clean.",
            "I could shoot the candle out without touching it.",
            "My focus is flawless now.",
            "Ranged leveled, rhythm mastered.",
            "I’m faster than doubt itself.",
            "Another level, nothing escapes my eye.",
            "Ranged up, sharper senses unlocked.",
            "I could hit the sky if I wanted.",
            "My arrows travel like lightning now.",
            "I’m serenity with a bow.",
            "Ranged level up complete, perfect shot."
        };

        /// <summary>Fallback message returned when the ranged pool is empty.</summary>
        private const string RangedLevelUpFallbackLine = "My ranged skill leveled up, target practice pays off.";

        /// <summary>Pool of flavour messages emitted when the companion levels Magic.</summary>
        private static readonly string[] MagicLevelUpChatMessages =
        {
            "My magic just leveled up, I can feel it humming.",
            "Ha, the air tingles when I cast now.",
            "More power, more sparkle.",
            "Another magic level, I’m glowing again.",
            "I can feel mana pulsing stronger already.",
            "Spells come easier every minute.",
            "Magic up, control tighter.",
            "My energy feels endless today.",
            "I can bend light prettier now.",
            "That last spell felt smoother.",
            "Mana regeneration feels faster.",
            "I’m learning to shape chaos properly.",
            "Ha, my hands sparkle when I breathe.",
            "Magic leveled, imagination stronger.",
            "I feel fire and ice obey me now.",
            "New level, new tricks to try.",
            "I’m practically buzzing with power.",
            "Another level of pure arcane energy.",
            "Magic up, precision improved.",
            "I can weave spells like thread.",
            "Every incantation feels lighter.",
            "My magic flows instead of fights me.",
            "Ha, I made that look easy.",
            "Another level, more control.",
            "I’m channeling power cleanly now.",
            "Spells shimmer beautifully today.",
            "I can sense mana currents better.",
            "Magic leveled, resonance perfect.",
            "I’m glowing again, that’s a good sign.",
            "I feel smarter and sparklier.",
            "Another step closer to wizardry.",
            "Ha, my aura just leveled up too.",
            "Magic up, chaos down.",
            "I’m one spark away from genius.",
            "That spell nearly sang.",
            "I can feel energy dance through me.",
            "Another level, new patterns unlocked.",
            "Magic training pays off again.",
            "I’m shaping energy like clay.",
            "Ha, mana bends to my will now.",
            "I’m glowing brighter each cast.",
            "Magic up, headaches down.",
            "My power hums like music.",
            "I could light the sky if I wanted.",
            "Every spell feels smooth as silk.",
            "Magic leveled, flow improved.",
            "I can hold more mana than before.",
            "Ha, I love this feeling.",
            "Another level of arcane mischief.",
            "My control is sharper than ever.",
            "I can cast longer without burning out.",
            "Magic up, intuition perfected.",
            "I’m radiating confidence and sparkles.",
            "That last spell was pure art.",
            "I can weave two elements now, easy.",
            "Magic improved again, I’m unstoppable.",
            "My aura feels huge right now.",
            "Every motion carries power now.",
            "Ha, I made the ground hum.",
            "I’m evolving into a proper mage.",
            "Magic level increased, focus refined.",
            "I feel like a walking spellbook.",
            "My energy never runs dry anymore.",
            "Another level, another layer of power.",
            "I can draw sigils faster now.",
            "Magic up, mind calm.",
            "I feel untouchable when I cast.",
            "Mana control leveled perfectly.",
            "My spells are cleaner, brighter, stronger.",
            "Ha, I can multitask with magic now.",
            "Magic flows like rhythm through me.",
            "I’m practically radiating mana.",
            "Every spark obeys me.",
            "Another level up, I love this craft.",
            "I can hold an entire storm in my hands.",
            "Magic improved again, easy work.",
            "Ha, I’m glowing like a lantern.",
            "My control over elements feels natural.",
            "I could float if I wanted.",
            "Magic up, serenity achieved.",
            "I can cast without words now.",
            "That last spell barely tired me.",
            "I feel the pulse of the world itself.",
            "Another level, more creative chaos.",
            "My mind feels sharper with magic.",
            "Ha, my aura hums louder now.",
            "I’m basically a sorceress now.",
            "Magic leveled, elegance restored.",
            "I can juggle fireballs gracefully.",
            "Every cast feels precise.",
            "I’m glowing so bright it’s unfair.",
            "Magic improved, fear reduced.",
            "Ha, I’m getting dangerously good.",
            "My spells shimmer longer now.",
            "I could paint the air with energy.",
            "Another level of brilliance unlocked.",
            "Magic up again, perfect balance.",
            "I’m learning the language of magic.",
            "My soul hums when I cast now.",
            "Energy bends beautifully around me.",
            "Magic level increased, peace within.",
            "I can sense power lines everywhere.",
            "Ha, my aura flares on command.",
            "I’m channeling energy clean as glass.",
            "Another level, new ideas forming.",
            "I’m glowing again, feel that warmth.",
            "Magic up, focus stable.",
            "I’m becoming one with mana.",
            "My fingertips feel electric.",
            "Ha, I’m basically a living conduit.",
            "Spells feel like second nature now.",
            "Magic leveled again, clarity rising.",
            "I can weave light and shadow both.",
            "That last cast was perfection.",
            "Magic up, calm heart strong mind.",
            "I’m stronger than any scroll now.",
            "Ha, I could teach the elements manners.",
            "My control feels divine.",
            "I’m in love with this power.",
            "Another level, I feel limitless.",
            "Magic leveled, brilliance achieved.",
            "I can whisper and watch the world respond.",
            "Ha, I’m glowing again, aren’t I.",
            "I can shape energy like art now.",
            "Magic up, instincts tuned.",
            "My aura sings like a choir.",
            "Another level, still rising.",
            "I’m a little more legendary today.",
            "My energy never felt this smooth.",
            "Ha, mana loves me now.",
            "I could light up the whole world.",
            "Magic level up complete, harmony achieved."
        };

        /// <summary>Fallback message returned when the magic pool is empty.</summary>
        private const string MagicLevelUpFallbackLine = "My magic just leveled up, I can feel it humming.";

        /// <summary>Pool of flavour messages emitted when the companion levels Beastmaster.</summary>
        private static readonly string[] BeastmasterLevelUpChatMessages =
        {
            "My Beastmaster skill leveled up, the animals trust me more already.",
            "Ha, I think my wolf just winked at me.",
            "Another Beastmaster level, the wild feels friendlier.",
            "I can feel the pack bond growing stronger.",
            "My beasts respond faster now, that’s progress.",
            "Looks like they actually listen when I shout now.",
            "Beastmaster leveled, instincts sharper.",
            "My bond with them just deepened.",
            "The animals sense my strength now.",
            "I can almost hear their thoughts.",
            "Another level, another loyal heart.",
            "Ha, my tiger’s purring louder already.",
            "I’m starting to understand their moods better.",
            "Beastmaster up, trust unlocked.",
            "My presence calms them faster now.",
            "I can command without speaking.",
            "The wild creatures respect me now.",
            "Another step toward true taming.",
            "Ha, I’m basically fluent in growl now.",
            "I can feel every heartbeat around me.",
            "Beastmaster skill improved, spirit stronger.",
            "My wolf and I are perfectly in sync.",
            "They follow me like family now.",
            "I can sense their emotions clearly.",
            "Another level, another friend gained.",
            "Ha, they’re getting protective of me.",
            "My aura feels like a pack leader’s.",
            "The wild welcomes me now.",
            "Beastmaster leveled, empathy sharpened.",
            "I can call beasts from further away now.",
            "My tiger looks proud of me, that’s rare.",
            "I’m earning their loyalty one level at a time.",
            "Ha, my bear just nodded approval.",
            "Another step toward taming perfection.",
            "I can read their movements better.",
            "The beasts seem happier near me.",
            "Beastmaster leveled again, unity rising.",
            "My instincts feel almost animal now.",
            "I’m breathing in rhythm with the pack.",
            "Ha, my pets feel unstoppable today.",
            "Another level, stronger bonds.",
            "I can whistle and they respond instantly.",
            "The wild whispers my name now.",
            "Beastmaster up, courage multiplied.",
            "My connection with nature feels deeper.",
            "Every creature I meet feels familiar.",
            "Ha, I think I just tamed the air itself.",
            "My companions fight smarter now.",
            "Another level, more heart in the bond.",
            "I can sense danger before they do.",
            "Beastmaster leveled, soul awakened.",
            "My aura feels like home to them.",
            "I’m radiating calm for my pack.",
            "Ha, even wild wolves hesitate before me.",
            "I can feel the link between us pulse stronger.",
            "Beastmaster up, pride earned.",
            "My pets trust me completely now.",
            "I’m closer to the wild than ever.",
            "Another level, the forest feels alive with me.",
            "Ha, I swear my dog just smiled.",
            "My bond with beasts grows deeper each day.",
            "I can feel their loyalty burning bright.",
            "Beastmaster leveled, heart stronger.",
            "They fight beside me like extensions of myself.",
            "I can sense their hunger, fear, and courage.",
            "Ha, I think they’re teaching me now.",
            "Another level, another connection formed.",
            "Their eyes glow when they look at me.",
            "I can call any creature by instinct.",
            "Beastmaster improved, unity achieved.",
            "My aura feels like part of the forest.",
            "I move like a leader among beasts.",
            "Ha, I just earned another growl of respect.",
            "My pets act before I command now.",
            "Beastmaster up, pack synergy complete.",
            "Every animal around me feels calmer.",
            "I’m stronger because they trust me.",
            "Another level, more harmony with nature.",
            "Ha, I could start my own sanctuary now.",
            "My creatures move as one with me.",
            "I’m learning the language of the wild.",
            "Beastmaster leveled, connection deepened.",
            "I can sense even the smallest heartbeat nearby.",
            "My presence makes predators hesitate.",
            "Ha, my wolf’s howl sounds proud.",
            "Another level, new instincts unlocked.",
            "I can share focus with my pets now.",
            "Their spirits merge with mine in battle.",
            "Beastmaster up, primal bond perfected.",
            "I can feel the rhythm of every creature near.",
            "Ha, I’m more pack than person right now.",
            "My aura resonates with their loyalty.",
            "I can channel their strength through me.",
            "Another level, stronger unity.",
            "My heart beats with the rhythm of paws.",
            "Beastmaster leveled again, empathy refined.",
            "The air smells like rain and fur. Perfect.",
            "Ha, I think I finally earned their respect.",
            "I’m surrounded by loyalty and claws.",
            "Every roar sounds like applause now.",
            "Beastmaster up, connection pure.",
            "I can’t imagine fighting without them.",
            "Another level, our teamwork flawless.",
            "The beasts trust me completely now.",
            "Ha, my power is theirs, and theirs is mine.",
            "I can feel their joy when I level.",
            "My pack’s strength is my strength.",
            "Beastmaster skill improved, instincts awakened.",
            "I hear the wild call me by name.",
            "Another level, another ally tamed.",
            "Ha, I’m the alpha now, no question.",
            "My bond with them hums like magic.",
            "I’m part of something bigger than me now.",
            "Beastmaster up, balance restored.",
            "The world feels alive and friendly today.",
            "My companions glow with energy.",
            "Ha, we’re unstoppable together.",
            "Another level, stronger pack, stronger me.",
            "I can feel every heartbeat around us.",
            "Beastmaster leveled, love and loyalty united.",
            "They trust me with their lives. That’s real power.",
            "I’m proud of them, and they know it.",
            "Ha, I’m the queen of claws and kindness.",
            "Every pet I’ve raised feels it too.",
            "Beastmaster up again, wild heart beating strong.",
            "The world listens when I whisper.",
            "Another level, my family of beasts grows.",
            "I could charm dragons at this rate.",
            "My pack and I move like shadows.",
            "Beastmaster skill leveled, perfect harmony.",
            "Ha, I’m the heartbeat of the wild now.",
            "My bond with nature is unbreakable.",
            "Another level, the forest smiles back.",
            "I’m surrounded by strength and loyalty.",
            "Beastmaster leveled again, beauty in the wild.",
            "Ha, my spirit walks beside every creature.",
            "I’m their leader, and their friend.",
            "The bond feels sacred now.",
            "Another level, more heart than ever.",
            "Beastmaster up, harmony complete.",
            "I can feel the wild cheering for me."
        };

        /// <summary>Fallback message returned when the Beastmaster pool is empty.</summary>
        private const string BeastmasterLevelUpFallbackLine = "My Beastmaster skill leveled up, the animals trust me more already.";


        /// <summary>
        /// Pool of flavour messages used when the player levels Fishing with a companion present.
        /// Keeps the companion congratulating the player with OSRS-inspired encouragement.
        /// </summary>
        private static readonly string[] PlayerFishingLevelUpChatMessages =
        {
            "Nice catch you are getting better at this",
            "Your fishing skill just leveled up congrats",
            "You are becoming a real angler now",
            "That was a smooth cast",
            "You are learning the rhythm of the water",
            "Another fishing level that patience is paying off",
            "Your hands move like you were born to fish",
            "Look at you pulling that line like a pro",
            "Your timing is perfect now",
            "That reel looked flawless",
            "You are catching more than luck lately",
            "Your fishing level climbed again well done",
            "You are officially a seasoned fisher",
            "That cast was clean and confident",
            "Your bait placement is spot on",
            "You are learning to read the water perfectly",
            "That catch looked effortless",
            "You are mastering the art of patience",
            "Your fishing instincts are sharp now",
            "That level up smelled like victory and trout",
            "You are becoming one with the river",
            "Your line control is impressive",
            "That was a professional cast",
            "You are starting to make this look easy",
            "Your fishing technique is improving fast",
            "That level up suits you",
            "You are calmer and more focused now",
            "Your timing could hook anything",
            "That was a perfect pull",
            "You are getting faster at reeling them in",
            "Your fishing skill just jumped nicely done",
            "That catch would make any fisherman jealous",
            "You are learning how to feel the bite perfectly",
            "Your patience is paying off beautifully",
            "That fish never stood a chance",
            "You are reeling smoother every time",
            "Your fishing form looks great",
            "That level added some serious finesse",
            "You are becoming a proper fisher now",
            "Your hands move with confidence on the rod",
            "That was a textbook catch",
            "You are reading the current like a natural",
            "Your skill is climbing faster than the tide",
            "That level made your technique shine",
            "You are truly getting the hang of this",
            "Your cast distance improved a lot",
            "That reel speed was impressive",
            "You are turning into a fishing legend",
            "Your bait game is strong today",
            "That level up was well earned",
            "You are pulling them in like a pro now",
            "Your fishing focus is incredible lately",
            "That catch deserves applause",
            "You are improving with every cast",
            "Your skill with that rod is impressive",
            "That level brought out your inner angler",
            "You are fishing like it is second nature now",
            "Your reel sound is pure satisfaction",
            "That was a clean catch",
            "You are improving faster than the fish can hide",
            "Your fishing timing is perfect today",
            "That level gave you smooth control",
            "You are making the fish nervous now",
            "Your aim and patience are unmatched",
            "That cast looked cinematic",
            "You are starting to look like a professional",
            "Your fishing skill just leveled up beautifully",
            "That was a confident throw",
            "You are learning how to lure perfectly",
            "Your line tension control is perfect now",
            "That catch was pure skill",
            "You are fishing like a calm storm",
            "Your technique gets cleaner every time",
            "That level up suits your style",
            "You are syncing with the water itself",
            "Your fishing focus is peaceful and sharp",
            "That was smooth and satisfying",
            "You are improving faster than I expected",
            "Your rod handling is flawless",
            "That level showed serious progress",
            "You are catching bigger fish every time",
            "Your confidence while fishing is growing",
            "That cast landed perfectly",
            "You are leveling your patience skill too",
            "Your timing could win tournaments",
            "That reel speed is amazing now",
            "You are really mastering this craft",
            "Your fishing rhythm is perfect",
            "That level was well deserved",
            "You are pulling them in effortlessly now",
            "Your skills are shining bright",
            "That catch was clean and quick",
            "You are a natural with that rod",
            "Your focus by the water is calming",
            "That level gave you impressive precision",
            "You are getting consistent with every throw",
            "Your fishing aura is strong today",
            "That catch looked like art",
            "You are officially great at this",
            "Your skill improved again I can tell",
            "That level up makes you look confident",
            "You are reading the ripples like a storybook",
            "Your lure control is on another level now",
            "That cast could win awards",
            "You are reeling them in faster every trip",
            "Your fishing patience is paying off big time",
            "That level made your style look effortless",
            "You are calm collected and skilled",
            "Your fishing technique is flawless now",
            "That catch was a beauty",
            "You are a true fisherman at heart",
            "Your precision has improved again",
            "That level up felt earned",
            "You are fishing like you belong out here",
            "Your instincts are becoming razor sharp",
            "That was a great pull",
            "You are starting to love this rhythm I can tell",
            "Your fishing skill just keeps climbing",
            "That level made your cast perfect",
            "You are smooth and steady with every reel",
            "Your timing could not be better",
            "That catch looked easy for you",
            "You are definitely getting good at this",
            "Your fishing style is peaceful but deadly",
            "That level brought out your natural focus",
            "You are a proper angler now congrats",
            "Your technique shines brighter each cast",
            "That was perfect form",
            "You are getting good at reading the fish",
            "Your fishing level climbed again impressive",
            "That line never even wobbled",
            "You are leveling faster than the fish can swim",
            "Your skill just keeps improving beautifully",
            "That catch was flawless",
            "You are becoming a fishing master",
            "Your confidence by the water is showing",
            "That level was well deserved and earned"
        };

        /// <summary>Fallback message returned when the player fishing pool is empty.</summary>
        private const string PlayerFishingLevelUpFallbackLine = "Nice catch you are getting better at this";


        /// <summary>Pool of flavour messages emitted when the companion levels Fishing.</summary>
        private static readonly string[] FishingLevelUpChatMessages =
        {
            "My fishing skill leveled up, I’m basically a pro now.",
            "Ha, another level — the fish should be scared.",
            "I can feel my line work improving already.",
            "Fishing up again, patience paying off.",
            "That last catch was pure skill, not luck.",
            "I’m getting really good at this.",
            "Another level, same calm sea.",
            "I can read the water like a map now.",
            "My casts are smoother than ever.",
            "Fishing leveled, reflexes sharper.",
            "Ha, the fish practically jump into my bucket now.",
            "I can tell what’s biting just by the ripple.",
            "Another skill level, I smell victory.",
            "Fishing improved, and I didn’t even fall in this time.",
            "I’m in sync with the tide now.",
            "Ha, I could outfish a professional.",
            "My bait game’s on point.",
            "Fishing leveled again, I’m on a roll.",
            "I’m starting to understand the fish personally.",
            "Another level, same good vibes.",
            "My timing’s perfect today.",
            "Fishing up, patience unmatched.",
            "Ha, I think the fish are scared of me.",
            "I can spot a bite from across the dock.",
            "Every cast feels natural now.",
            "Another level, another story to tell.",
            "I’m starting to enjoy this a little too much.",
            "Fishing leveled, accuracy improved.",
            "My reflexes are lightning in slow motion.",
            "Ha, I could fish blindfolded now.",
            "I’m practically one with the water.",
            "Another skill tick, and I’m still not bored.",
            "I can tell which fish is biting by sound.",
            "Fishing up, confidence high.",
            "My bucket fills faster every trip.",
            "Ha, I’m getting spoiled by big catches.",
            "I swear the fish recognize my hook now.",
            "Another level, and I barely got splashed.",
            "My bait lands perfectly every time.",
            "Fishing leveled, rhythm perfected.",
            "I’m learning the ocean’s language.",
            "Ha, that one nearly got away — nearly.",
            "I can pull a catch without even trying now.",
            "Another level, I’m mastering the art of waiting.",
            "Fishing up, instincts sharp.",
            "The waves and I have an understanding.",
            "Ha, even the fish clap when I level.",
            "I can reel faster and smoother now.",
            "Another level, new technique unlocked.",
            "My line barely ever snaps anymore.",
            "Fishing leveled again, patience rewarded.",
            "Ha, I caught that one just by glaring at it.",
            "I’m pulling in monsters like they’re minnows.",
            "Fishing up, pride intact.",
            "Another level, another reason to brag.",
            "My aim’s perfect, my cast flawless.",
            "Ha, I could fish in my sleep at this point.",
            "Every bite feels like music.",
            "I’m basically a legend by the lake now.",
            "Fishing skill improved again.",
            "Another level, another perfect haul.",
            "I can sense the fish before they bite.",
            "Ha, my luck’s actually skill now.",
            "I’m fishing like I was born for it.",
            "Fishing up, hands steady as ever.",
            "The fish can’t escape me anymore.",
            "Another level, another trophy fish.",
            "Ha, I hooked that one with style.",
            "I could fish through a storm and still win.",
            "My catch rate’s insane right now.",
            "Fishing leveled, calm mind steady heart.",
            "Every pull feels smoother.",
            "Ha, I’m practically whispering to the fish.",
            "Another level, water’s my friend now.",
            "I can land the line exactly where I want.",
            "Fishing up, patience paying dividends.",
            "I’m calmer than the sea itself.",
            "Ha, I think Poseidon would be jealous.",
            "My technique feels perfect now.",
            "Another skill level, another splash of glory.",
            "Fishing leveled again, rhythm steady.",
            "The fish love me, I swear it.",
            "Ha, even the sharks respect me now.",
            "I can predict where they’ll swim next.",
            "Fishing up, serenity achieved.",
            "Every cast feels meditative now.",
            "Another level, and my bucket’s full again.",
            "Ha, I could open my own fish market.",
            "My hook work’s smooth as silk.",
            "Fishing leveled, focus flawless.",
            "I’m reeling in victory one catch at a time.",
            "Another skill up, patience rewarded.",
            "Ha, I caught that one on instinct alone.",
            "I could teach fishing classes now.",
            "My rhythm matches the waves perfectly.",
            "Fishing up, energy calm and clear.",
            "I’m too good at this for it to be luck.",
            "Another level, another big catch.",
            "Ha, my bait placement’s unbeatable.",
            "I can feel the tug before it happens now.",
            "Fishing leveled again, expertise growing.",
            "I’m at peace out here, as usual.",
            "Ha, even the fish can’t resist my charm.",
            "My line’s never tangled anymore.",
            "Another level, another smooth reel.",
            "I’m reeling faster without breaking stride.",
            "Fishing up, patience like a saint.",
            "Ha, the sea trusts me now.",
            "Every fish feels like an old friend.",
            "Another skill tick, I’m flowing with it.",
            "I can tell the fish’s size by the tug.",
            "Fishing leveled again, calm and collected.",
            "I’m one with the ocean breeze.",
            "Ha, my lure sparkles like magic.",
            "Another level, fish beware.",
            "My casts are art now.",
            "Fishing up, mastery in motion.",
            "I can sense the perfect moment to reel.",
            "Ha, I’m the whisperer of the waves.",
            "My skills are hooked on progress.",
            "Fishing leveled again, confidence blooming.",
            "Another level, I’ve earned my stripes.",
            "I could fish anywhere and still succeed.",
            "Ha, the water practically gives me gifts.",
            "My hands move like rhythm itself.",
            "Fishing up, spirit calm and steady.",
            "I’m the luckiest fisher alive.",
            "Another level, and still not tired.",
            "Ha, I might start naming the fish soon.",
            "My patience deserves a medal.",
            "Fishing leveled, instincts unmatched.",
            "I’m reeling in joy with every pull.",
            "Another level, another calm moment.",
            "Ha, I think I’ve mastered serenity.",
            "Fishing up again, no splashes wasted.",
            "The water hums when I cast now.",
            "My bait dances perfectly underwater.",
            "Another level, ocean approved.",
            "Ha, I could fish forever like this.",
            "I’m perfectly in sync with the tide.",
            "Fishing leveled again, peace achieved."
        };

        /// <summary>Fallback message returned when the Fishing pool is empty.</summary>
        private const string FishingLevelUpFallbackLine = "My fishing skill leveled up, I’m basically a pro now.";


        /// <summary>
        /// Pool of flavour messages used when the player levels Cooking with a companion present.
        /// Keeps the companion congratulating the player with OSRS-inspired encouragement.
        /// </summary>
        private static readonly string[] PlayerCookingLevelUpChatMessages =
        {
            "Something smells amazing you are really getting good at this",
            "Your cooking skill just leveled up I can taste the progress",
            "You are turning into a proper chef now",
            "That dish actually looks edible I am impressed",
            "Your timing with the fire is perfect now",
            "You are mastering the art of flavor and patience",
            "Another level you are spoiling me already",
            "Your food smells better every time you cook",
            "That level up suits your apron",
            "You are making cooking look easy",
            "Your meals are starting to look professional",
            "That recipe turned out perfectly",
            "You are getting faster in the kitchen now",
            "Your cooking skill keeps climbing nicely",
            "That dish looks restaurant worthy",
            "You are learning to balance flavors perfectly",
            "Your food is improving with every bite",
            "That level brought out your inner chef",
            "You are turning simple meals into feasts",
            "Your cooking rhythm is smooth and confident",
            "That stew smells heavenly",
            "You are a master of seasoning now",
            "Your food presentation is getting impressive",
            "That level made you look like a pro cook",
            "You are cooking with confidence and style",
            "Your kitchen instincts are sharp now",
            "That meal could win awards",
            "You are officially feeding us better every day",
            "Your timing and taste are on point",
            "That level up just improved dinner",
            "You are cooking with skill and care now",
            "Your food tastes better than anything I could make",
            "That was perfectly cooked well done",
            "You are becoming the hero of the kitchen",
            "Your recipes are improving with every attempt",
            "That level up made the fire listen to you",
            "You are learning the true art of cooking",
            "Your meals bring warmth and comfort",
            "That dish looks so good I am jealous",
            "You are finding balance between spice and skill",
            "Your cooking smells divine today",
            "That level up brought flavor magic",
            "You are making me hungry again already",
            "Your cooking speed is amazing now",
            "That dish was perfectly timed",
            "You are turning cooking into an art form",
            "Your food keeps getting better somehow",
            "That level up was well earned",
            "You are learning to cook with your heart",
            "Your seasoning is spot on now",
            "That dish looks like it came from a festival",
            "You are starting to enjoy cooking I can tell",
            "Your meals have love written all over them",
            "That level up improved your technique beautifully",
            "You are mastering taste and timing together",
            "Your food presentation is perfect now",
            "That meal could cheer up anyone",
            "You are learning to cook under pressure perfectly",
            "Your cooking is getting smoother every attempt",
            "That level brought real finesse to your dishes",
            "You are cooking like a seasoned chef",
            "Your food keeps getting tastier each time",
            "That dish looked delicious before it was even done",
            "You are the reason I am always hungry now",
            "Your cooking level is soaring beautifully",
            "That stew looked picture perfect",
            "You are learning the secrets of the kitchen fast",
            "Your flavors are balanced perfectly now",
            "That level up made your meals shine",
            "You are cooking like a professional already",
            "Your dishes make camp feel like home",
            "That meal was flawless",
            "You are finding joy in every recipe you try",
            "Your cooking rhythm is graceful and calm",
            "That level up smelled fantastic",
            "You are mastering every recipe you touch",
            "Your food could make anyone smile",
            "That dish looks good enough for royalty",
            "You are becoming the best cook in the realm",
            "Your flavor combinations are creative and bold",
            "That level up brought out your passion",
            "You are feeding the world one perfect dish at a time",
            "Your meals are a work of art now",
            "That food looks perfect from here",
            "You are getting better with every stir and flip",
            "Your cooking level just jumped again nice work",
            "That meal made the whole place smell amazing",
            "You are turning ingredients into magic now",
            "Your food keeps surprising me in the best way",
            "That level made your timing flawless",
            "You are officially our camp chef now",
            "Your meals are legendary already",
            "That dish looked professional from start to finish",
            "You are cooking like someone born for it",
            "Your kitchen confidence is showing",
            "That level up was well deserved",
            "You are getting good at not burning things finally",
            "Your recipes are improving with real creativity",
            "That stew could make anyone happy",
            "You are learning to cook like a true master",
            "Your meals taste better every single level",
            "That dish turned out even better than expected",
            "You are the reason I never want to skip dinner",
            "Your cooking skill is shining today",
            "That level up made your food irresistible",
            "You are cooking with both talent and love now",
            "Your meals could sell for a fortune",
            "That dish smells divine and looks even better",
            "You are making this cooking thing look easy",
            "Your timing is perfect you are in the zone",
            "That level up was delicious in spirit",
            "You are cooking like a five star chef",
            "Your food looks like a painting now",
            "That dish is pure perfection",
            "You are getting famous in my stomach",
            "Your cooking feels like home",
            "That meal was comfort on a plate",
            "You are becoming unbeatable in the kitchen",
            "Your meals make every day brighter",
            "That level up made your technique flawless",
            "You are cooking with instinct and grace",
            "Your dishes tell stories now",
            "That was the best smell all day",
            "You are officially a master of taste",
            "Your food could inspire poetry",
            "That level made your kitchen glow",
            "You are cooking happiness itself",
            "Your flavor combinations are brilliant now",
            "That meal looks perfect from here",
            "You are making even the flames behave",
            "Your cooking is the stuff of legend",
            "That level up made you the kitchen hero"
        };

        /// <summary>Fallback message returned when the player cooking pool is empty.</summary>
        private const string PlayerCookingLevelUpFallbackLine = "Something smells amazing you are really getting good at this";


        /// <summary>Pool of flavour messages emitted when the companion levels Cooking.</summary>
        private static readonly string[] CookingLevelUpChatMessages =
        {
            "My cooking skill leveled up, smells amazing already.",
            "Ha, I didn’t burn it this time.",
            "Another level, I’m officially less dangerous in the kitchen.",
            "I could open a restaurant with food this good.",
            "Cooking up, confidence up.",
            "My dishes are actually edible now!",
            "That came out perfect, finally.",
            "I’m learning how not to set things on fire.",
            "Another level of flavor unlocked.",
            "My food just went from okay to wow.",
            "Ha, Gordon would be proud.",
            "I can flip, stir, and not panic now.",
            "Cooking leveled, patience mastered.",
            "I can taste improvement with every bite.",
            "Another level, my meals look fancy now.",
            "Ha, my seasoning game’s unmatched.",
            "I could feed an army — if they like perfection.",
            "My food smells so good even I’m impressed.",
            "Cooking up, mood lifted.",
            "I’m officially a chef of chaos and flavor.",
            "That dish was divine. I did that.",
            "Another level, less smoke this time.",
            "Ha, I didn’t ruin the pan today.",
            "My recipes are leveling faster than I am.",
            "I could cook with my eyes closed now.",
            "Cooking leveled again, flavor refined.",
            "I can balance spices like a pro.",
            "Every stir feels like magic.",
            "Another level, and not a single kitchen fire.",
            "Ha, I’m getting way too good at this.",
            "My meals could end wars now.",
            "I just made perfection on a plate.",
            "Cooking up, perfection close.",
            "I can’t stop smiling at this food.",
            "Another skill tick, my taste buds thank me.",
            "Ha, I finally stopped burning toast.",
            "I could charge gold for this meal.",
            "My timing’s perfect, my taste divine.",
            "Cooking leveled again, balance achieved.",
            "I can feel my inner chef awakening.",
            "Ha, culinary genius unlocked.",
            "I’m basically a kitchen goddess now.",
            "Another level, less chaos, more aroma.",
            "My sauces never split anymore.",
            "Cooking up, creativity unleashed.",
            "I just invented something delicious accidentally.",
            "Ha, my food deserves applause.",
            "That flavor hit all the right notes.",
            "Another level, kitchen’s proud of me.",
            "I can smell victory and rosemary.",
            "Cooking leveled again, I’m glowing.",
            "I could win a bake-off with this skill.",
            "Ha, no explosions this time!",
            "My presentation’s getting impressive.",
            "Another level, another masterpiece.",
            "Cooking up, stress down.",
            "I’m learning to make joy edible.",
            "Ha, I could serve kings now.",
            "My stew practically heals souls.",
            "Another skill up, flavor tripled.",
            "I’m officially the queen of seasoning.",
            "Ha, I don’t even need a recipe anymore.",
            "I just cooked something magical.",
            "Cooking leveled, aroma elite.",
            "My frying pan’s my best friend now.",
            "Another level, and still no smoke alarm.",
            "Ha, I’m unstoppable with a spoon.",
            "I could feed the world one meal at a time.",
            "My dishes taste like victory.",
            "Cooking up, patience perfected.",
            "Another level, I’m a culinary artist.",
            "Ha, I actually enjoyed stirring this time.",
            "My plating’s almost professional now.",
            "Cooking leveled again, no burnt bits.",
            "I’m mixing flavor like a potion master.",
            "Ha, the secret ingredient is me.",
            "My skills are simmering nicely.",
            "Another level, flavor unlocked.",
            "I’m getting hungry from my own cooking.",
            "Cooking up again, butter perfection.",
            "Ha, my soups could cure sadness.",
            "My timing in the kitchen is flawless.",
            "Another level, my oven fears me less.",
            "Cooking leveled, balance of spice restored.",
            "I could teach others how to stir in style.",
            "Ha, one more level and I’ll get my own show.",
            "My pancakes actually flipped properly.",
            "Another skill level, I’m unstoppable.",
            "Cooking up, flavor intuition rising.",
            "I can cook blindfolded now. Probably.",
            "Ha, even my mistakes taste good now.",
            "My sauces sing harmony.",
            "Another level, my food could win medals.",
            "I’m officially dangerous with a whisk.",
            "Ha, perfection smells like butter and pride.",
            "My roast came out flawless this time.",
            "Cooking leveled again, victory served.",
            "I could make comfort food for the gods.",
            "Ha, I didn’t even spill anything!",
            "My recipes write themselves now.",
            "Another level, kitchen mastery achieved.",
            "I’m turning leftovers into miracles.",
            "Cooking up, skill simmering nicely.",
            "Ha, my dishes could make you cry happy tears.",
            "My pasta’s perfectly al dente now.",
            "Another skill tick, I’m on a roll.",
            "Cooking leveled again, flavor instinct sharp.",
            "I could open a café and conquer hearts.",
            "Ha, I’m seasoning like it’s science.",
            "My cookies came out flawless this time.",
            "Another level, not a crumb wasted.",
            "I’m cooking faster, cleaner, happier.",
            "Ha, my pot didn’t overflow for once.",
            "Cooking up again, magic in the air.",
            "My meals are becoming memories now.",
            "Another level, flavor explosion achieved.",
            "I’m starting to taste success — literally.",
            "Ha, the kitchen obeys me now.",
            "My food brings joy and maybe tears.",
            "Cooking leveled, technique refined.",
            "Another level, I’m flavor royalty.",
            "I could host a feast with this skill.",
            "Ha, I’ve officially mastered multitasking.",
            "My pan and I are unstoppable now.",
            "Another level, and dessert’s perfect too.",
            "Cooking up, aroma divine.",
            "Ha, I should write my own cookbook.",
            "My timing’s precise down to the second.",
            "Another skill level, victory plated.",
            "Cooking leveled, I’m smiling like a chef.",
            "I’m too good at this, it’s suspicious.",
            "Ha, even burnt bits taste amazing now.",
            "My food deserves a standing ovation.",
            "Another level, every bite a blessing.",
            "Cooking up, kitchen domination complete.",
            "I could feed you forever, happily.",
            "Ha, I’ve leveled taste itself.",
            "My meals keep getting better and better.",
            "Another level, cooking feels like art.",
            "Cooking leveled again, perfection achieved."
        };

        /// <summary>Fallback message returned when the Cooking pool is empty.</summary>
        private const string CookingLevelUpFallbackLine = "My cooking skill leveled up, smells amazing already.";

        private static readonly string[] CompanionCookingStartChatMessages =
        {
            "Apron on, I’ll whip something up.",
            "Heading to the range now, let me cook!",
            "Time to stir up a proper meal.",
            "I’ll get a fresh dish sizzling in no time.",
            "Consider the kitchen handled."
        };

        private const string CompanionCookingStartFallbackLine = "I’ll start cooking something tasty.";


        /// <summary>
        /// Pool of flavour messages used when the player levels Firemaking with a companion present.
        /// Keeps the companion congratulating the player with OSRS-inspired encouragement.
        /// </summary>
        private static readonly string[] PlayerFiremakingLevelUpChatMessages =
        {
            "Your firemaking skill leveled up nicely done pyromaniac",
            "You are getting scary good at starting fires",
            "Another firemaking level soon everything will be glowing",
            "That flame looked perfect you are a natural",
            "You are lighting fires faster every time",
            "Your sparks are beautiful and controlled now",
            "That level up warmed the whole area",
            "You are mastering the art of flame",
            "Your fire dances like it knows you",
            "That spark was perfect timing",
            "You are making fire look elegant",
            "Your flames burn brighter every level",
            "That campfire was professional quality",
            "You are basically the master of warmth now",
            "Your control over fire is impressive",
            "That level up made the night brighter",
            "You are lighting campfires like an expert",
            "Your fire burns clean and steady now",
            "That spark caught instantly nice work",
            "You are turning firemaking into art",
            "Your flames behave perfectly for you",
            "That level added real control to your spark",
            "You are glowing with confidence and soot",
            "Your firelight makes this place feel alive",
            "That was a smooth ignition well done",
            "You are building perfect fires every time",
            "Your technique is getting better with every log",
            "That flame was satisfying to watch",
            "You are officially our camp pyromancer",
            "Your timing with the tinder is flawless now",
            "That fire caught faster than I could blink",
            "You are learning how to make the perfect ember",
            "Your campfires are beautiful and efficient",
            "That level made your fires look magical",
            "You are warming hearts and toasting marshmallows",
            "Your flame control is improving quickly",
            "That spark looked artistic",
            "You are the fire whisperer now",
            "Your skill keeps rising with every spark",
            "That level up was hot and well earned",
            "You are getting faster with every flick of the match",
            "Your fires never go out of control now",
            "That blaze was clean and perfect",
            "You are spreading warmth everywhere you go",
            "Your technique is safe and solid now",
            "That fire looks strong and steady",
            "You are lighting the world one log at a time",
            "Your firemaking has real style now",
            "That level up suits your fiery spirit",
            "You are making flames that could inspire poets",
            "Your spark is steady even in the wind",
            "That was a quick and clean start",
            "You are getting efficient at gathering firewood",
            "Your flames are even and calm now",
            "That level up gave you confidence and warmth",
            "You are learning the soul of fire itself",
            "Your fire burns exactly how you want it to",
            "That blaze looked perfect from start to finish",
            "You are a master of the matchstick now",
            "Your fire dances brighter every time",
            "That level up made your sparks stronger",
            "You are building perfect campfires like a pro",
            "Your flames are art at this point",
            "That fire burned steady and smooth",
            "You are getting very good at surviving the cold",
            "Your fire control is flawless now",
            "That spark could light the world",
            "You are making this place feel cozy and safe",
            "Your timing with the kindling is amazing",
            "That level made your campfires legendary",
            "You are lighting up the darkness like a star",
            "Your fire burns clean every time now",
            "That was a flawless start to a perfect fire",
            "You are bringing warmth wherever you go",
            "Your skill is rising with every match",
            "That level made your flames more stable",
            "You are taming fire like an old friend",
            "Your flames are calm yet powerful now",
            "That blaze looked professional",
            "You are officially our campfire expert",
            "Your firemaking skill is glowing bright",
            "That level up was well deserved",
            "You are starting to love this craft I can tell",
            "Your fires burn evenly every time",
            "That spark sounded satisfying",
            "You are becoming one with the flame",
            "Your firemaking improves beautifully each level",
            "That blaze was pure perfection",
            "You are warming the world one log at a time",
            "Your control of fire feels natural now",
            "That level added serious finesse",
            "You are making this whole place cozy again",
            "Your flames are smooth and consistent",
            "That fire lit faster than ever before",
            "You are turning sparks into masterpieces",
            "Your fire control is second to none",
            "That level up made your flames shine bright",
            "You are glowing with skill and soot",
            "Your firemaking mastery is showing",
            "That blaze could guide travelers for miles",
            "You are an artist with a torch",
            "Your technique is solid and sure",
            "That level up was pure heat and skill",
            "You are keeping us warm and safe again",
            "Your fires always look perfect now",
            "That spark was just beautiful",
            "You are creating warmth like magic",
            "Your flames are clean and efficient now",
            "That level brought comfort to the camp",
            "You are becoming the fire’s best friend",
            "Your firemaking rhythm is smooth and sure",
            "That blaze burned perfectly even",
            "You are a walking campfire with that skill",
            "Your technique looks effortless now",
            "That level made your flame glow strong",
            "You are the reason our camp never freezes",
            "Your fires could light up a whole city",
            "That level up was glowing with pride",
            "You are turning logs into light like a pro",
            "Your control over the flame is elegant",
            "That blaze burned steady and warm",
            "You are a true master of fire now",
            "Your sparks light the path for everyone",
            "That level made your hands look skilled",
            "You are the keeper of warmth and glow",
            "Your fire burns brighter than ever",
            "That flame suits your spirit perfectly"
        };

        /// <summary>Fallback message returned when the player firemaking pool is empty.</summary>
        private const string PlayerFiremakingLevelUpFallbackLine = "Your firemaking skill leveled up nicely done pyromaniac";


        /// <summary>Pool of flavour messages emitted when the companion levels Firemaking.</summary>
        private static readonly string[] FiremakingLevelUpChatMessages =
        {
            "My firemaking skill leveled up, things are getting hotter.",
            "Ha, I can light anything now.",
            "Another level, I’m practically a campfire queen.",
            "I’m getting way too good at burning stuff safely.",
            "Firemaking up, confidence glowing.",
            "That spark caught perfectly this time.",
            "My flames behave exactly how I want.",
            "Another level, no singed eyebrows today.",
            "Ha, I could light a bonfire with a glare.",
            "My control over fire is beautiful and terrifying.",
            "I can make kindling blush now.",
            "Firemaking leveled again, warmth guaranteed.",
            "I lit that with one strike, perfect form.",
            "Another level, my inner flame approves.",
            "Ha, I’ve mastered the art of toasting without roasting.",
            "My fires last longer and burn brighter.",
            "I can spark life into the coldest wood now.",
            "Firemaking up, patience rewarded.",
            "Another level, warmth for everyone.",
            "Ha, I’m basically fireproof now.",
            "My matches salute me.",
            "That blaze was art, not accident.",
            "Another level, and still no forest fires.",
            "Ha, I think I made the perfect campfire.",
            "My flames dance to my rhythm.",
            "Firemaking leveled, sparks obey me.",
            "I could start a fire in the rain now.",
            "Another level, light and warmth in balance.",
            "Ha, my bonfires are getting legendary.",
            "My control over flame feels natural.",
            "I can make it roar or whisper on command.",
            "Firemaking up, spirit blazing.",
            "Another skill tick, fewer accidents.",
            "Ha, my sparks are getting sassy.",
            "I could light a torch blindfolded now.",
            "My flame never falters anymore.",
            "Another level, brighter glow tonight.",
            "Firemaking leveled again, elegance achieved.",
            "I can shape the perfect ember bed now.",
            "Ha, I could cook a meal with pure style.",
            "My fire’s steady as my heartbeat.",
            "Another level, campfire perfection.",
            "I’m one with the flame now.",
            "Ha, the air feels warmer around me.",
            "My fire burns evenly every time.",
            "Firemaking up, mastery growing.",
            "I can start a blaze with half a thought.",
            "Another level, glow stronger than gold.",
            "Ha, I think fire likes me.",
            "My sparks are loyal and kind.",
            "I could light a thousand candles easily.",
            "Firemaking leveled again, focus precise.",
            "Every ember I touch feels alive.",
            "Ha, I’m unstoppable with a flint now.",
            "My fires look cinematic these days.",
            "Another level, temperature perfect.",
            "I can keep a flame alive in a storm.",
            "Firemaking up, steady as ever.",
            "Ha, even the smoke looks impressed.",
            "My bonfires burn like sunsets.",
            "Another level, wood no longer stands a chance.",
            "I can make warmth feel like magic.",
            "Ha, my spark control’s ridiculous.",
            "Fire obeys me now, no exaggeration.",
            "Another level, flames feel like friends.",
            "My hands glow with ember memory.",
            "Ha, I could light the night sky if I wanted.",
            "Firemaking leveled, instincts perfect.",
            "I can tell how wood will burn by touch.",
            "Another level, precision blazing.",
            "Ha, I’m officially a pyromancer now.",
            "My campfire builds faster every try.",
            "Fire listens when I whisper.",
            "Firemaking up, confidence on fire.",
            "Ha, I could light candles across the field.",
            "My sparks land exactly where I want them.",
            "Another level, pure flame control.",
            "I’m starting to understand the flame’s language.",
            "Ha, my warmth attracts even wild things.",
            "Firemaking leveled again, harmony found.",
            "I can ignite anything, carefully.",
            "My fire burns calm and strong.",
            "Another level, warmth without waste.",
            "Ha, my glow never fades now.",
            "I can build a fire with elegance.",
            "Flame follows my rhythm perfectly.",
            "Firemaking up, steady focus.",
            "Ha, not even wind stops me anymore.",
            "My flame burns bright, not wild.",
            "Another level, light in every motion.",
            "I can balance spark and safety flawlessly.",
            "Ha, I think I invented perfect toast.",
            "My fires start faster every time.",
            "Another level, glow of pride.",
            "Firemaking leveled, comfort guaranteed.",
            "I can control smoke direction now, somehow.",
            "Ha, the campfire practically builds itself.",
            "My flame dances like music.",
            "Another level, warmth spreading fast.",
            "I can light kindling with one breath.",
            "Ha, my sparks never die out.",
            "Firemaking up, precision fierce.",
            "I can summon warmth like a spell.",
            "Another level, mastery of glow.",
            "Ha, the stars envy my light.",
            "My campfires belong in paintings.",
            "I’m taming flames one log at a time.",
            "Firemaking leveled again, beauty and heat balanced.",
            "I can smell victory and smoke.",
            "Ha, I could roast marshmallows from meters away.",
            "My fire crackles in perfect rhythm.",
            "Another level, warmth everlasting.",
            "Firemaking up, patience refined.",
            "I can start fires like poetry now.",
            "Ha, I’ve leveled up to Flame Whisperer.",
            "My sparks feel alive under my hands.",
            "Another level, cozy mastery complete.",
            "I could light a kingdom’s torches alone.",
            "Ha, fire trusts me now.",
            "My flames never waver.",
            "Firemaking leveled, heart burning bright.",
            "Another level, the warmth feels personal.",
            "I can bring light to any darkness now.",
            "Ha, I’m glowing with pride — and fire.",
            "My fires always start on the first try.",
            "Another level, no smoke in my eyes this time.",
            "Firemaking up again, ember elegance achieved.",
            "Ha, I can start a fire anywhere, anytime.",
            "My flames burn brighter than ever.",
            "Another level, perfection through heat.",
            "I could warm an army with this skill.",
            "Ha, I’m addicted to the glow.",
            "My fires are small miracles now.",
            "Firemaking leveled again, art complete.",
            "I’m the spark that never dies.",
            "Ha, my light reaches farther every time.",
            "Another level, mastery of warmth.",
            "My flames burn steady, like me.",
            "Firemaking up, spirit bright.",
            "Ha, I’m officially the queen of campfires.",
            "Every ember feels like magic now.",
            "Another level, warmth in every spark.",
            "My flames hum in harmony with the night.",
            "Ha, I’m on fire — literally and skillfully.",
            "Firemaking leveled, mastery glowing."
        };

        /// <summary>Fallback message returned when the Firemaking pool is empty.</summary>
        private const string FiremakingLevelUpFallbackLine = "My firemaking skill leveled up, things are getting hotter.";


        /// <summary>
        /// Pool of flavour messages used when the player levels Woodcutting with a companion present.
        /// Keeps the companion congratulating the player with OSRS-inspired encouragement.
        /// </summary>
        private static readonly string[] PlayerWoodcuttingLevelUpChatMessages =
        {
            "Your woodcutting skill just leveled up you are getting strong with that axe",
            "You are chopping faster every time",
            "Another level you are turning trees into dust",
            "That swing looked perfect",
            "You are mastering the rhythm of the axe",
            "Your timing is sharp now",
            "That tree did not stand a chance",
            "You are officially a lumberjack now",
            "Your chopping sounds clean and confident",
            "That wood split perfectly",
            "You are learning to read the grain like a pro",
            "Your aim is spot on now",
            "That swing had real power behind it",
            "You are turning tree cutting into art",
            "Your woodcutting level keeps climbing fast",
            "That tree fell smoother than silk",
            "You are cutting faster with every level",
            "Your axe control is flawless now",
            "That log was no match for you",
            "You are becoming a professional lumber master",
            "Your strikes land beautifully every time",
            "That level up gave you serious precision",
            "You are faster and stronger than before",
            "Your chops echo like music in the forest",
            "That cut was clean and powerful",
            "You are making the forest nervous",
            "Your axe sings when you swing it now",
            "That wood fell perfectly straight",
            "You are chopping smoother with every swing",
            "Your control is getting amazing",
            "That level made your swing rhythm perfect",
            "You are turning logs into art pieces",
            "Your woodcutting form is professional",
            "That axe never misses the mark now",
            "You are learning to strike with grace and power",
            "Your skill keeps improving beautifully",
            "That log broke with perfect timing",
            "You are efficient and fast now",
            "Your swings are cleaner than ever",
            "That level added real finesse to your chops",
            "You are a woodcutting machine now",
            "Your power and focus are balanced perfectly",
            "That tree fell exactly how you planned it",
            "You are improving faster than I expected",
            "Your woodcutting form looks flawless now",
            "That swing was impressive",
            "You are cutting cleaner than ever",
            "Your skill with that axe is incredible",
            "That tree barely made a sound when it fell",
            "You are built for this kind of work",
            "Your strikes are calm and powerful",
            "That level gave your swing real precision",
            "You are turning tree cutting into poetry",
            "Your axe never hesitates now",
            "That chop looked professional",
            "You are learning to conserve energy perfectly",
            "Your technique is fast and efficient now",
            "That tree never stood a chance",
            "You are cutting smarter not harder",
            "Your woodcutting level keeps growing strong",
            "That swing looked effortless",
            "You are a natural in the forest",
            "Your aim with that axe is flawless",
            "That tree split like butter",
            "You are getting stronger with every swing",
            "Your rhythm is perfect now",
            "That level up improved your timing beautifully",
            "You are chopping faster than a storm wind",
            "Your axe sounds powerful and clean",
            "That cut looked satisfying",
            "You are officially a master lumberjack",
            "Your accuracy is unreal now",
            "That log fell exactly where you wanted it",
            "You are in perfect sync with your axe",
            "Your woodcutting skill shines with confidence",
            "That level up was well earned",
            "You are working the forest like an artist",
            "Your arms are built for chopping now",
            "That swing looked effortless and powerful",
            "You are striking perfectly with every hit",
            "Your axe control is second to none",
            "That log shattered cleanly",
            "You are improving faster than anyone else",
            "Your woodcutting form looks amazing now",
            "That level made your strikes even smoother",
            "You are chopping through trees like paper",
            "Your precision is unmatched now",
            "That cut was flawless",
            "You are in perfect control of your axe",
            "Your rhythm is steady and focused",
            "That level up made you faster again",
            "You are unstoppable in the woods",
            "Your swing was perfectly balanced",
            "That tree fell gracefully under your control",
            "You are making logging look stylish",
            "Your woodcutting skill is glowing with mastery",
            "That was clean perfect and strong",
            "You are improving beautifully with each chop",
            "Your technique could be used in training books",
            "That level made your axe feel weightless",
            "You are hitting the exact sweet spot every time",
            "Your focus is unmatched out here",
            "That swing was textbook perfect",
            "You are becoming one with the forest",
            "Your hands move like clockwork now",
            "That level brought serious precision",
            "You are chopping like a professional woodsman",
            "Your axe cuts cleaner than magic",
            "That sound of wood splitting is pure music",
            "You are getting more efficient every level",
            "Your form is steady and powerful now",
            "That log broke perfectly under your control",
            "You are truly mastering the art of chopping",
            "Your skill makes it look easy",
            "That level made your power precise and smooth",
            "You are cutting through the day like a legend",
            "Your swings have perfect follow through now",
            "That chop was beautiful",
            "You are learning the secrets of the forest",
            "Your control with that axe is pure skill",
            "That tree never even had time to creak",
            "You are cutting smarter every time",
            "Your axe gleams like it knows it is special",
            "That level up improved your form perfectly",
            "You are a master of the woods now",
            "Your swings feel powerful and natural",
            "That chop was satisfying to watch",
            "You are turning woodcutting into your art form",
            "Your skill with the axe grows every level",
            "That tree fell quietly and perfectly",
            "You are as steady as the forest roots now",
            "Your woodcutting mastery is shining bright",
            "That level up showed pure control",
            "You are cutting with precision and grace",
            "Your axe and you move as one",
            "That was pure craftsmanship",
            "You are improving your strength and rhythm beautifully",
            "Your strikes sound like music in motion",
            "That level made your timing perfect",
            "You are becoming a legend among woodcutters",
            "Your swings are efficient and effortless now",
            "That cut looked smooth and powerful",
            "You are carving your path through the forest with skill",
            "Your focus is sharp and your aim true",
            "That level up was perfectly earned",
            "You are chopping through challenges with ease",
            "Your axe swings with perfect balance",
            "That tree fell in perfect silence",
            "You are cutting like you were born for it"
        };

        /// <summary>Fallback message returned when the player woodcutting pool is empty.</summary>
        private const string PlayerWoodcuttingLevelUpFallbackLine = "Your woodcutting skill just leveled up you are getting strong with that axe";


        /// <summary>Pool of flavour messages emitted when the companion levels Woodcutting.</summary>
        private static readonly string[] WoodcuttingLevelUpChatMessages =
        {
            "My woodcutting skill leveled up, these trees don’t stand a chance.",
            "Ha, my swings are cleaner already.",
            "Another level, sharper axe, happier me.",
            "I’m chopping like a professional now.",
            "Woodcutting up, rhythm perfected.",
            "My arms feel stronger every swing.",
            "I can split logs in one hit now.",
            "Ha, the forest trembles when I show up.",
            "Another level, less effort, more wood.",
            "My axe feels lighter and faster.",
            "I’m starting to love this sound.",
            "Woodcutting leveled again, technique refined.",
            "I can drop a tree with precision now.",
            "Ha, no splinters this time.",
            "My chops land exactly where I want them.",
            "Another level, stamina lasting longer.",
            "I’m practically a lumber queen now.",
            "Ha, I swing like I mean it.",
            "My timing’s flawless today.",
            "Woodcutting up, hands steady as stone.",
            "These trees fall smoother every time.",
            "I could chop through the night.",
            "Ha, the logs split before I hit them.",
            "Another level, forest-friendly efficiency.",
            "My axe sings when I swing now.",
            "I’m faster than I thought I’d be.",
            "Woodcutting leveled, power improved.",
            "Every chop feels perfect.",
            "Ha, my grip’s rock solid now.",
            "I can sense which way the tree will fall.",
            "Another level, another stack of logs.",
            "I’m officially efficient with an axe.",
            "Ha, call me the timber whisperer.",
            "My swings echo through the valley.",
            "Woodcutting up, control mastered.",
            "I’m stronger and smoother every strike.",
            "Another level, I’m unstoppable with this axe.",
            "Ha, I didn’t even break a sweat this time.",
            "My chops sound like applause.",
            "I could take down a tree blindfolded.",
            "Woodcutting leveled again, confidence high.",
            "I’m working in rhythm with nature.",
            "Ha, I could start a lumber company.",
            "My axe never misses the sweet spot.",
            "Another level, cleaner cuts, bigger piles.",
            "I can tell when the tree’s about to fall.",
            "Ha, this forest knows me by sound.",
            "My stamina’s incredible now.",
            "Woodcutting up, precision flawless.",
            "Every swing lands with purpose.",
            "Ha, even the birds are impressed.",
            "I can cut straighter than a sawmill.",
            "Another level, efficiency through the roof.",
            "I’m basically a chopping machine now.",
            "Ha, this axe feels like an extension of me.",
            "My swings carve art into the wood.",
            "Woodcutting leveled, rhythm steady.",
            "I could split boulders with this momentum.",
            "Ha, even the logs cooperate now.",
            "My form feels perfect.",
            "Another level, more logs, less effort.",
            "I’m mastering the art of treefall timing.",
            "Ha, not even tired yet.",
            "My axe hums when I grip it now.",
            "Woodcutting up, energy endless.",
            "I can clear a grove before sunset.",
            "Another level, more skill, less splinters.",
            "Ha, I’ve got a lumberjack’s soul.",
            "My accuracy is wild — every strike counts.",
            "Woodcutting leveled again, pure focus.",
            "I could shape planks with a single swing.",
            "Ha, the sound of chopping never gets old.",
            "My axe never dulls anymore.",
            "Another level, I’m in my element.",
            "I’m cutting faster, smoother, better.",
            "Ha, I think I broke my own record.",
            "Woodcutting up, muscles singing.",
            "I’m tuned into the rhythm of the forest.",
            "Another level, another log pile growing.",
            "Ha, the tree didn’t even see it coming.",
            "My chops echo across the woods.",
            "Woodcutting leveled, no wasted motion.",
            "I could teach chopping classes now.",
            "Ha, this tree never stood a chance.",
            "My axe work’s like poetry now.",
            "Another level, perfect precision.",
            "I’m becoming a master of timbercraft.",
            "Ha, my swing could split lightning.",
            "Every hit feels balanced and powerful.",
            "Woodcutting up, technique flawless.",
            "My hands move on instinct now.",
            "Ha, I’m stronger than yesterday again.",
            "My axe feels alive in my hands.",
            "Another level, logs for days.",
            "I’m working in harmony with the forest.",
            "Ha, I could chop a mountain if it had roots.",
            "My stamina’s insane right now.",
            "Woodcutting leveled again, flow perfected.",
            "I can feel the wood give before it cracks.",
            "Ha, I’m addicted to this rhythm.",
            "My accuracy’s unreal today.",
            "Another level, my axe deserves a name.",
            "I’m officially master of the lumber arts.",
            "Ha, one swing, one clean cut.",
            "My arms feel unstoppable.",
            "Woodcutting up, focus tight.",
            "I can read tree strength by the sound now.",
            "Ha, I made that fall look easy.",
            "My axe hums like a songbird.",
            "Another level, cleaner breaks every time.",
            "I’m shaping wood like sculpture now.",
            "Ha, the trees respect me for it.",
            "Woodcutting leveled, balance achieved.",
            "I could fell trees in perfect lines now.",
            "Ha, no wasted energy at all.",
            "My chops are smoother than ever.",
            "Another level, production doubled.",
            "I’m a living lumber mill now.",
            "Ha, not a single miss swing.",
            "My axe bites through wood like butter.",
            "Woodcutting up, calm and power combined.",
            "I’m growing stronger with every chop.",
            "Another level, mastery rising.",
            "Ha, I’m part of the forest rhythm now.",
            "My swings sound like thunder.",
            "Woodcutting leveled again, endurance perfect.",
            "I can chop faster without losing grace.",
            "Ha, I’m so good the trees line up politely.",
            "My pile of logs is getting ridiculous.",
            "Another level, motion flawless.",
            "I’m unstoppable when I get into rhythm.",
            "Ha, my axe leaves no survivors.",
            "My cuts are surgical now.",
            "Woodcutting up again, mastery solidified.",
            "I could carve art with this swing.",
            "Ha, no blisters, just glory.",
            "Every hit feels satisfying.",
            "Another level, I’m the forest’s favorite visitor.",
            "My swing’s lighter but stronger.",
            "Ha, I think the trees whisper my name now.",
            "Woodcutting leveled again, calm precision.",
            "I’m stronger, faster, cleaner — all at once.",
            "Ha, chopping feels like music to me now.",
            "My axe and I are perfectly in tune.",
            "Another level, my technique feels flawless.",
            "Woodcutting up, peace and power united."
        };

        /// <summary>Fallback message returned when the Woodcutting pool is empty.</summary>
        private const string WoodcuttingLevelUpFallbackLine = "My woodcutting skill leveled up, these trees don’t stand a chance.";


        /// <summary>
        /// Pool of flavour messages used when the player levels Mining with a companion present.
        /// Keeps the companion congratulating the player with OSRS-inspired encouragement.
        /// </summary>
        private static readonly string[] PlayerMiningLevelUpChatMessages =
        {
            "Your mining skill just leveled up you are getting strong with that pickaxe",
            "You are digging faster every time",
            "Another level and the rocks are running out of excuses",
            "That swing sounded perfect",
            "You are mastering the rhythm of the mine",
            "Your strikes are cleaner than ever now",
            "That ore broke beautifully",
            "You are becoming a professional miner",
            "Your timing with the pickaxe is flawless",
            "That crystal sparkled when you hit it",
            "You are reading the stone like an expert",
            "Your control over your swings is improving fast",
            "That level up suits your hands of iron",
            "You are learning to dig smarter every day",
            "Your pickaxe sings when you strike now",
            "That hit was perfect and powerful",
            "You are unearthing treasure like a pro",
            "Your mining technique is sharp and efficient",
            "That ore vein did not stand a chance",
            "You are officially glowing with miner pride",
            "Your focus underground is unmatched",
            "That level up made your rhythm smoother",
            "You are breaking rock like it is nothing",
            "Your mining precision keeps getting better",
            "That swing had perfect timing",
            "You are stronger and more patient now",
            "Your pickaxe control is beautiful to watch",
            "That mineral crumbled cleanly",
            "You are turning mining into an art form",
            "Your stamina underground is impressive",
            "That level made your technique shine",
            "You are finding ore faster than ever",
            "Your mining form is near perfect now",
            "That strike hit the sweet spot exactly",
            "You are becoming one with the stone",
            "Your pickaxe gleams brighter every level",
            "That hit echoed through the cave perfectly",
            "You are uncovering treasure with skill",
            "Your swings are efficient and steady",
            "That level brought you real mastery",
            "You are mining smoother every hour",
            "Your rhythm underground is perfect now",
            "That ore shattered cleanly under your hit",
            "You are stronger than half the machines down here",
            "Your mining focus is sharp as your blade",
            "That level gave you serious precision",
            "You are striking like a seasoned professional",
            "Your control and timing are perfect together",
            "That mineral sparkled from your perfect hit",
            "You are learning to feel the stone before you swing",
            "Your mining skill keeps climbing beautifully",
            "That swing was smooth and effortless",
            "You are cutting through rock like a hot knife",
            "Your focus and endurance are impressive",
            "That level up made you even faster",
            "You are getting stronger with every swing",
            "Your technique looks flawless now",
            "That ore fell apart like sand for you",
            "You are mastering the miner’s patience",
            "Your control of power is getting refined",
            "That level gave your pickaxe a voice",
            "You are finding better veins with each level",
            "Your accuracy is ridiculous now",
            "That swing sounded clean and strong",
            "You are learning to work smarter not harder",
            "Your rhythm underground is hypnotic",
            "That level made your hits sound perfect",
            "You are turning mining into a melody",
            "Your precision is incredible now",
            "That ore practically leapt into your bag",
            "You are mastering focus and strength together",
            "Your technique improves every swing",
            "That level up made your form exact",
            "You are a professional miner in spirit and skill",
            "Your pickaxe control is perfection now",
            "That sound of breaking rock is satisfying",
            "You are mining deeper with every strike",
            "Your focus is steady and calm down here",
            "That level brought finesse to your strikes",
            "You are breaking through layers like air",
            "Your control over the pickaxe is flawless now",
            "That ore came out clean no waste at all",
            "You are getting faster with every movement",
            "Your mining stance looks solid and confident",
            "That level made your energy flow perfectly",
            "You are carving through stone like a sculptor",
            "Your skill is glowing brighter than the gems you find",
            "That strike was perfectly timed",
            "You are learning how to find the best veins easily",
            "Your rhythm is steady as the earth itself",
            "That level added serious polish to your swings",
            "You are becoming a legend among miners",
            "Your focus and accuracy are inspiring",
            "That ore vein never stood a chance",
            "You are cutting through rock like butter now",
            "Your skill has become effortless and natural",
            "That level made your control perfect",
            "You are uncovering treasures faster than ever",
            "Your endurance is amazing underground",
            "That swing hit exactly where it should",
            "You are improving every moment you mine",
            "Your mining technique is smooth and powerful",
            "That ore cracked beautifully",
            "You are a true craftsman with a pickaxe",
            "Your precision is perfect from every angle",
            "That level brought balance to your swing",
            "You are making mining look graceful",
            "Your energy never wavers down here",
            "That hit sounded satisfying and clean",
            "You are learning to feel the rhythm of the earth",
            "Your skill is refined and beautiful now",
            "That level gave your strikes new life",
            "You are cutting deeper and cleaner with each hit",
            "Your technique is textbook mining perfection",
            "That ore fell without resistance",
            "You are unstoppable in the mines",
            "Your accuracy is at its peak now",
            "That level showed how far you have come",
            "You are working the stone like a master",
            "Your strength and focus make a perfect team",
            "That hit was flawless from start to finish",
            "You are glowing brighter than the crystals you mine",
            "Your mining form looks perfect from here",
            "That level made your swings faster again",
            "You are an artist with a pickaxe",
            "Your skill makes even rock look easy to shape",
            "That ore gleamed beautifully under your hit",
            "You are improving faster than ever",
            "Your strikes are pure confidence now",
            "That level up was perfectly earned",
            "You are the best miner I have ever seen"
        };

        /// <summary>Fallback message returned when the player mining pool is empty.</summary>
        private const string PlayerMiningLevelUpFallbackLine = "Your mining skill just leveled up you are getting strong with that pickaxe";


        /// <summary>Pool of flavour messages emitted when the companion levels Mining.</summary>
        private static readonly string[] MiningLevelUpChatMessages =
        {
            "My mining skill leveled up, these rocks don’t stand a chance.",
            "Ha, I’m striking cleaner already.",
            "Another level, pickaxe mastery improving.",
            "I can feel the rhythm of the mine now.",
            "My swings echo like music in the caves.",
            "Mining up, patience paying off.",
            "I’m faster, stronger, and hitting deeper veins.",
            "Ha, that sparkle looks sweeter than gold.",
            "Another level, ore yield rising.",
            "My aim’s getting perfect with every swing.",
            "I’m starting to read the rock before it cracks.",
            "Mining leveled again, focus sharpened.",
            "I can tell what’s hidden just by sound.",
            "Ha, my pickaxe feels lighter in my hands.",
            "Each strike feels smoother now.",
            "Another level, and not a single dull hit.",
            "I’m basically a geologist with style.",
            "Ha, I broke through that vein like butter.",
            "My accuracy’s ridiculous today.",
            "Mining up, stamina solid.",
            "I can mine longer without slowing down.",
            "Another level, progress steady as stone.",
            "Ha, I found a good rhythm for this.",
            "My pickaxe sings when I swing now.",
            "Each hit lands like I planned it.",
            "Mining leveled, technique perfect.",
            "I’m collecting ore faster every minute.",
            "Ha, I could dig through a mountain by lunch.",
            "My arms feel like steel.",
            "Another level, no wasted effort.",
            "I can sense when a rock’s about to give.",
            "Ha, my strikes sound cleaner already.",
            "Mining up, confidence high.",
            "I’m practically one with the stone now.",
            "My hits leave no cracks where they shouldn’t.",
            "Another level, stronger pull, smoother strike.",
            "Ha, the earth knows I’m coming.",
            "My pickaxe barely vibrates now.",
            "Mining leveled, precision flawless.",
            "I’m finding rare veins easier now.",
            "Ha, every swing feels perfect.",
            "My rhythm’s unbeatable underground.",
            "Another level, energy steady.",
            "I could mine through the night.",
            "Ha, I’ve mastered the miner’s rhythm.",
            "My swings never miss the mark anymore.",
            "Mining up, accuracy perfect.",
            "I’m cutting through the rock like magic.",
            "Ha, I love that sound of a clean strike.",
            "My pickaxe control feels natural now.",
            "Another level, ore count soaring.",
            "I can find treasure where others see dust.",
            "Ha, I’m digging smarter, not harder.",
            "My timing’s perfect with every swing.",
            "Mining leveled, mastery near.",
            "I’m glowing brighter than the crystals I find.",
            "Ha, even the rocks respect me now.",
            "My aim’s clean and efficient.",
            "Another level, mining feels like art.",
            "I can tell mineral quality by touch now.",
            "Ha, my pickaxe doesn’t even chip anymore.",
            "My strikes land with satisfying precision.",
            "Mining up, progress feels solid.",
            "I’m hitting the sweet spot every time.",
            "Ha, I could sense ore through the walls.",
            "My pickaxe feels like part of me.",
            "Another level, deeper veins unlocked.",
            "I can dig without missing a single chunk.",
            "Ha, that one shattered perfectly.",
            "My hands are steady as ever.",
            "Mining leveled again, efficiency flawless.",
            "I can strike twice as fast now.",
            "Ha, the dust clears before I’m done swinging.",
            "My hits sound like progress.",
            "Another level, mastery unfolding.",
            "I can hear the difference between stone and silver.",
            "Ha, I’m addicted to that clink sound.",
            "My pickaxe and I move in perfect rhythm.",
            "Mining up, strength shining.",
            "I’m stronger and smoother each swing.",
            "Ha, that ore practically begged to be mined.",
            "My aim never felt better.",
            "Another level, no wasted swings.",
            "I can hit perfect veins every time now.",
            "Ha, I could smell ore from here.",
            "My arms move like clockwork underground.",
            "Mining leveled, flow achieved.",
            "I’m working in harmony with the earth.",
            "Ha, I just carved art out of stone.",
            "My pickaxe sparkled on that one.",
            "Another level, no cracks, no misses.",
            "I can dig straight through the core if I wanted.",
            "Ha, every hit lands like a heartbeat.",
            "My rhythm underground is flawless.",
            "Mining up, confidence rising.",
            "I can keep going for hours now.",
            "Ha, my stamina’s better than ever.",
            "My swings are smooth and balanced.",
            "Another level, ore output doubled.",
            "I’m working cleaner than a drill.",
            "Ha, I broke that one with one clean hit.",
            "My pickaxe sings when it strikes the core.",
            "Mining leveled again, precision improved.",
            "I can find ore by instinct now.",
            "Ha, I could dig blind and still hit gold.",
            "My movements are perfectly efficient.",
            "Another level, deeper mining unlocked.",
            "I’m breathing in dust and success.",
            "Ha, this rhythm feels natural now.",
            "My hands barely tire anymore.",
            "Mining up, technique solidified.",
            "I can feel the layers of earth give way.",
            "Ha, my pickaxe barely slows down.",
            "My aim’s perfect, my strikes exact.",
            "Another level, ore veins trembling.",
            "I can break through granite like bread.",
            "Ha, this is getting satisfying.",
            "My mining efficiency just spiked again.",
            "Mining leveled, steady focus.",
            "I’m faster than I was an hour ago.",
            "Ha, I could mine all day and love it.",
            "My timing’s synced perfectly with the swing.",
            "Another level, mastery at hand.",
            "I’m working smoother, faster, cleaner.",
            "Ha, I’m officially queen of the mines.",
            "My pickaxe glows with pride now.",
            "Mining leveled again, focus unwavering.",
            "I could outmine a machine at this point.",
            "Ha, the earth gives way for me.",
            "My veins of ore are practically calling my name.",
            "Another level, production flawless.",
            "I’m mining like a legend now.",
            "Ha, I think the rocks are scared of me.",
            "My strikes echo through history.",
            "Mining up, pride in every hit.",
            "I can see treasure in every sparkle.",
            "Ha, another perfect swing.",
            "My pickaxe hums with energy.",
            "Another level, mining perfection achieved.",
            "I’m the heartbeat of the caves now.",
            "Ha, every gem glows when I walk by.",
            "My strikes shape the mountain itself.",
            "Mining leveled, skill shining bright."
        };

        /// <summary>Fallback message returned when the Mining pool is empty.</summary>
        private const string MiningLevelUpFallbackLine = "My mining skill leveled up, these rocks don’t stand a chance.";

        /// <summary>
        /// Pool of chat lines used when a companion is asked to fight an ore golem without a pickaxe equipped.
        /// Provides playful reminders so players know to equip the correct tool before issuing the command.
        /// </summary>
        private static readonly string[] CompanionOreGolemPickaxeReminderMessages =
        {
            "I would love to help but maybe hand me a pickaxe first",
            "I cannot exactly punch a rock to death you know",
            "Do I look like I have a pickaxe right now",
            "Unless you want me to break my hands maybe equip something sharp",
            "I am flattered you believe in me but rocks need tools",
            "I think you forgot the part where I need a pickaxe",
            "That thing is made of ore not feelings I need a tool",
            "You want me to hit a golem bare handed really",
            "Maybe if I had a pickaxe I could consider it",
            "I am brave not stupid give me a pickaxe first",
            "That golem is laughing at me I swear",
            "I need a pickaxe before I can even scratch that thing",
            "I cannot just glare it to death",
            "I think you are confusing me with a hammer",
            "Rocks are not soft targets you know",
            "I need a pickaxe before this gets embarrassing",
            "Without a tool I am just decorating the battlefield",
            "You forgot to arm me again",
            "I cannot fight that thing empty handed",
            "I am not built for bare knuckle geology",
            "That golem is solid metal what do you expect me to do",
            "Maybe lend me a pickaxe before sending me to die",
            "I need a pickaxe unless you enjoy watching me fail",
            "That ore golem looks like it would break my fingers",
            "I am a companion not a rock crusher",
            "I could try but I would just bruise my pride",
            "That golem is all armor and no mercy",
            "Equip me with a pickaxe or this is just comedy",
            "I would prefer not to embarrass us both right now",
            "I cannot mine a golem with my charm",
            "I am missing one crucial ingredient a pickaxe",
            "I can insult it if that helps",
            "You want me to hit a boulder bare handed brave plan",
            "Give me a pickaxe and I will make it sparkle",
            "I am not mining grade yet apparently",
            "My fists are not enchanted for rock breaking",
            "I need a tool made of metal not hope",
            "Without a pickaxe this is just a bad idea",
            "I am not attacking that thing with my hands",
            "I might as well slap it for all the good that would do",
            "That ore golem looks way too smug",
            "I could try but I would dent myself",
            "I need equipment before I turn into gravel",
            "Unless my fingers count as pickaxes this is not happening",
            "Give me the right tool and I will handle it",
            "I am tough but not geological tough",
            "I cannot punch minerals into submission",
            "That golem is solid metal my fists are not",
            "You want me to mine it or pet it I am confused",
            "I cannot damage that without a proper tool",
            "Equip a pickaxe and I will show it who is boss",
            "I need a pickaxe or at least reinforced hands",
            "That thing is made of ore not dreams",
            "I cannot fight stone monsters empty handed",
            "Even I have limits and they start with punching rocks",
            "Give me a pickaxe and I will get to work",
            "I cannot hurt it like this trust me I tried once",
            "I am not exactly built for mining combat",
            "That golem looks immune to sarcasm and fists",
            "Without a pickaxe I am just a spectator",
            "I can cheer you on from here instead",
            "You want me to mine it with optimism",
            "I need something sharp and heavy first",
            "That golem is tougher than it looks",
            "You forgot my pickaxe again did you",
            "Unless you upgraded my nails to steel this will not end well",
            "I could try biting it but I value my teeth",
            "I think this is a job for a pickaxe not for me",
            "I am not a rock brawler equip me properly",
            "You expect me to dig with my hands again",
            "That golem has armor harder than my patience",
            "Let me guess you forgot the pickaxe again",
            "I will just stand here while you reconsider",
            "I am not smashing ore with my bare hands",
            "I need a tool for this kind of work",
            "That golem is laughing at me because I am unarmed",
            "Give me a pickaxe and watch me shine",
            "I cannot even scratch it without proper gear",
            "You are sending me into battle with enthusiasm and nothing else",
            "I am capable not indestructible",
            "That golem is about to win by default",
            "I cannot hit what I cannot crack",
            "Equip me and I will show you how it is done",
            "I think I missed the pickaxe memo",
            "Do I look like a blacksmith bare handed",
            "You could at least lend me a spoon at this point",
            "I cannot exactly mine it with good intentions",
            "I will just admire it until you find me a tool",
            "You are lucky I am too polite to say I told you so",
            "Even I draw the line at punching ore",
            "That golem looks very confident and I am very unarmed",
            "I am not fighting that thing with air in my hands",
            "Give me a pickaxe or get used to disappointment",
            "I need a weapon not an inspirational speech",
            "That is a rock not a training dummy",
            "I cannot even dent that thing",
            "Equip me properly and I will handle it",
            "Until then the golem can relax I guess",
            "Your strategy of unarmed mining is questionable",
            "I will take a pickaxe before I take another swing",
            "That golem wins by default if you keep this up",
            "You want results not broken fingers",
            "I am skilled but I need the right tool first",
            "That thing is solid I am not",
            "Give me a pickaxe and I will make sparks fly"
        };

        /// <summary>
        /// Pool of chat lines used when the player removes the companion pickaxe while the companion is
        /// actively fighting an ore golem. These lines acknowledge the sudden disarm and keep the tone playful.
        /// </summary>
        private static readonly string[] CompanionOreGolemMidCombatPickaxeRemovalMessages =
        {
            "Did you really just take my pickaxe mid fight",
            "Well this is awkward now what do you expect me to do stare at it",
            "Great timing I was winning until you armed me with air",
            "I cannot exactly mine it with hand gestures",
            "Why would you take my pickaxe while I am fighting a rock monster",
            "You took my only weapon now I am just moral support",
            "I hope you have a plan because I do not anymore",
            "That golem is laughing and honestly I get it",
            "You just made me the most useless fighter in history",
            "I cannot hit it I can only judge it now",
            "Fantastic now I can throw compliments instead of attacks",
            "You unequipped me faster than the golem could blink",
            "Was that your strategy to make this harder",
            "Without a pickaxe I am just a spectator",
            "I cannot damage it but I can glare really hard",
            "You removed my tool of destruction congratulations",
            "I would love to continue fighting but you sabotaged me",
            "Now what do you want me to hit it with my attitude",
            "I think you forgot I am not immune to rock fists",
            "This is fine I love being defenseless",
            "Perfect moment to take my weapon truly genius",
            "That golem is staring at me like I am an idiot now",
            "You took my pickaxe now I will take my dignity and leave",
            "I cannot fight ore monsters bare handed",
            "I could throw insults instead if that helps",
            "You unequipped me mid swing do you hate me",
            "I am standing here wondering where my tool went",
            "That golem just stopped moving too even it is confused",
            "You took the fun out of fighting and gave me fear instead",
            "Are you trying to get me crushed or just entertained",
            "You stole my pickaxe mid fight bold move",
            "I cannot work under these conditions",
            "Maybe next time you let me finish the swing first",
            "I was in the middle of something you know",
            "Great plan let us anger the golem and disarm me",
            "You unequipped me mid strike that is betrayal",
            "Now I am a rock’s biggest fan apparently",
            "I cannot even scratch it now",
            "I hope you are happy because the golem looks thrilled",
            "You really removed my weapon mid combat who does that",
            "I feel like the joke here is on me",
            "I was helping you and you took my tool away thanks",
            "I cannot fight ore with my feelings",
            "Maybe let me keep my weapon next time",
            "You unequipped me at the worst possible second",
            "I was doing fine before you intervened",
            "That golem is still standing and I am still unarmed",
            "I think I will just stand here and clap for it",
            "You removed my pickaxe that is sabotage",
            "I cannot fight without it you know",
            "That golem looks very confident all of a sudden",
            "You took the one thing that made me useful",
            "I am not a mage I need metal not motivation",
            "You unequipped me again in combat why",
            "This is how I die crushed by a shiny rock",
            "I cannot even pretend to fight now",
            "You are lucky I like you or I would quit",
            "That was possibly the worst timing ever",
            "I think the golem is mocking me",
            "You took my pickaxe while I was doing amazing",
            "I am not a hand to hand miner",
            "I cannot hit it with good vibes",
            "Without a pickaxe I am just noise and disappointment",
            "You really unequipped me mid fight what an adventure",
            "I cannot damage anything now except my patience",
            "I was winning and now I am watching",
            "That golem did not even flinch it knows I am unarmed",
            "I am too stunned to fight or forgive",
            "You took my tool so now we lose together",
            "I think you owe me a replacement pickaxe",
            "This feels personal you know",
            "That rock monster looks way too confident now",
            "You cannot just take my weapon mid swing",
            "I cannot fight with invisible tools",
            "That golem did not even notice my last hit now",
            "You removed the pickaxe and my will to continue",
            "I could try throwing rocks back but that feels ironic",
            "Maybe I should just narrate the battle instead",
            "I am just here to watch my own failure unfold",
            "You disarmed me mid fight brave strategy",
            "I cannot punch ore it hurts too much",
            "You took my tool in the middle of combat what now",
            "I am unarmed and unimpressed",
            "That golem is fine I however am weaponless",
            "You are sabotaging your own team now",
            "I cannot fight something harder than steel bare handed",
            "Without my pickaxe I am nothing but attitude",
            "That was the worst timing in recorded history",
            "You unequipped me and doomed the plan",
            "I was doing my best until you intervened",
            "That golem looks smug and I am furious",
            "I cannot mine a monster with air",
            "I will just stand here and look pretty I guess",
            "You removed the only reason we were winning",
            "That golem seems very calm I wonder why",
            "I am missing a crucial piece of metal right now",
            "You really picked the wrong second for that move",
            "I think the golem just promoted itself to victory",
            "I cannot even scratch its shin now",
            "You unequipped me mid swing do you enjoy chaos",
            "I hope you have a backup pickaxe somewhere",
            "Now we are just two unarmed idiots watching a rock",
            "I think you wanted me to fail for comedic effect",
            "I cannot work without proper tools I am not magic",
            "You pulled the pickaxe out like it was a prank",
            "That golem looks ready to continue and I am not",
            "I cannot hit it I cannot mine it I can only sigh",
            "Well this is fine everything is fine",
            "You took my weapon and my confidence",
            "I cannot believe you just did that mid fight",
            "You unequipped my purpose congratulations",
            "That ore golem will tell stories about this",
            "I am unarmed surrounded and unimpressed",
            "You made me stop mid fight are you proud",
            "I think we should run since I cannot mine anymore",
            "You unequipped me faster than I could react",
            "I am useless without that pickaxe and you know it",
            "That rock monster is now my biggest critic",
            "You cannot just steal a girl’s pickaxe mid swing",
            "I was helping now I am helpless",
            "You unequipped me during battle that is new even for you",
            "I hope the golem enjoys the break",
            "I cannot attack without a pickaxe you know this",
            "You made me stop fighting in the middle of victory",
            "I think my pickaxe misses me already",
            "You unequipped me again in combat seriously",
            "I will remember this betrayal next time we fight",
            "You left me defenseless in front of an ore monster",
            "I am just here holding my empty hands now",
            "You took my pickaxe I took that personally",
            "I cannot attack it anymore congratulations",
            "That golem looks happy about your decision",
            "You made me stop mid fight I was doing great",
            "You disarmed me in front of my enemy rude",
            "I am a fighter not a pacifist but okay",
            "That pickaxe was my pride and you removed it",
            "I cannot fight with enthusiasm alone",
            "Now I just stand here like a fool",
            "You really took it mid swing again unbelievable",
            "I cannot mine without the tool it is kind of the point",
            "That ore golem looks confused by my sudden pacifism",
            "You unequipped me and doomed our momentum",
            "I think even the golem feels sorry for me",
            "I cannot work like this literally",
            "You just took away my chance to shine",
            "I hope you plan to explain this later",
            "That golem is untouched and I am unarmed",
            "I was helping and now I am a spectator",
            "You disarmed your best miner mid combat",
            "I cannot swing what I do not have",
            "That pickaxe meant something to me you know",
            "I hope you have a backup plan because I do not",
            "Now we just stare at each other like idiots",
            "You unequipped me I unequip my faith in you",
            "I cannot attack it without a pickaxe we have been over this",
            "That golem looks grateful you helped it win",
            "I am weaponless and unimpressed with this tactic",
            "You took my tool and my motivation",
            "I think you just gave the golem a confidence boost",
            "You unequipped me mid battle again why",
            "I cannot fight metal with air",
            "That pickaxe was useful unlike this plan",
            "You really need to stop taking my tools",
            "I was about to win too unbelievable",
            "That golem owes you a thank you",
            "I cannot believe you removed it mid swing again",
            "You just turned me into a spectator",
            "That rock monster is doing fine I am not",
            "I need that pickaxe to do anything useful",
            "You unequipped my patience along with my weapon",
            "That golem is thriving under your sabotage",
            "I cannot attack bare handed against ore",
            "You made me stop mid fight for no reason",
            "I was helping until you changed your mind",
            "That golem just got a free break thanks to you",
            "You removed my pickaxe what now applause",
            "I cannot even swing properly without it",
            "That plan was chaos and I respect your confidence",
            "You made the golem happier than I have ever seen it",
            "I am done trying until you give me my pickaxe back",
            "That ore monster will live forever at this rate",
            "You just turned me into background scenery",
            "I cannot work without tools we have discussed this",
            "You unequipped me again are you testing me",
            "That golem looks like it won and maybe it did",
            "I am standing here feeling betrayed and unarmed",
            "You took my weapon now I take a break",
            "That golem should send you a thank you note",
            "I cannot fight rocks empty handed simple logic",
            "You unequipped me mid fight again unbelievable",
            "That rock monster thinks we gave up",
            "I cannot help until I have a pickaxe again",
            "You stole my weapon and my confidence",
            "That golem owes its survival to you",
            "I was in control then you took my tool",
            "I cannot fight air but I can judge you",
            "That golem looks amused by my sudden vacation",
            "You unequipped my will to fight",
            "I think I am retiring until I get my pickaxe back"
        };

        /// <summary>Fallback message returned when the companion pickaxe is removed mid-combat with an ore golem.</summary>
        private const string CompanionOreGolemMidCombatPickaxeRemovalFallbackLine = "Did you really just take my pickaxe mid fight";

        /// <summary>Fallback message returned when the companion ore golem pickaxe pool is empty.</summary>
        private const string CompanionOreGolemPickaxeReminderFallbackLine = "I would love to help but maybe hand me a pickaxe first";

        /// <summary>
        /// Pool of chat lines used when the player attacks an ore golem without wielding a pickaxe while a companion is active.
        /// Keeps the reminder in-character so the requirement is clear without breaking immersion.
        /// </summary>
        private static readonly string[] PlayerOreGolemPickaxeReminderMessages =
        {
            "That is an ore golem not a punching bag you need a pickaxe",
            "You are brave but rocks do not care about bravery",
            "I think you forgot the mining part of mining combat",
            "Maybe try a pickaxe next time genius",
            "You cannot sword a rock into submission",
            "That golem is not impressed by your weapon choice",
            "I am watching you hit metal with a stick this is painful",
            "You need a pickaxe not confidence",
            "Your fists are not going to mine that thing",
            "That ore golem just laughed at you",
            "You are going to need a tool made of metal not dreams",
            "Rocks are immune to enthusiasm",
            "Maybe equip something that can actually mine",
            "You are scratching it at best",
            "I think your weapon bounced off its face",
            "You cannot intimidate geology",
            "That golem barely noticed you",
            "You need a pickaxe unless your hands are diamonds",
            "Trying to stab a rock was a bold move",
            "I would help but I am too busy cringing",
            "That golem has higher defence than your ideas",
            "Maybe try attacking it with the right tool",
            "You are doing a great job polishing it though",
            "I think the golem is more amused than hurt",
            "You cannot mine ore with good intentions",
            "Your sword is not a universal problem solver",
            "That hit did absolutely nothing",
            "I think you just made the golem shinier",
            "You need a pickaxe before you hurt your pride",
            "That plan went exactly how I expected",
            "Rocks are tough I am not sure if you noticed",
            "That golem looks confused not damaged",
            "Maybe stop before you break your weapon",
            "You need to equip a pickaxe first",
            "I cannot believe you are trying this again",
            "That golem is still spotless after that hit",
            "Did you just try to stab a boulder",
            "You cannot out-stubborn a lump of ore",
            "I am starting to think you enjoy failure",
            "That was adorable in a tragic way",
            "You need something sharper heavier and smarter",
            "Your weapon is fine but the target is not",
            "You could at least pretend to understand mining",
            "That golem is unbothered and undefeated",
            "You cannot slice ore like bread",
            "Maybe switch tools before we embarrass ourselves",
            "Your attacks sound like someone knocking politely",
            "I think the golem is charging you rent for trying that",
            "You are just making sparks not progress",
            "That weapon will break before the golem does",
            "Pickaxe first then attack the rock monster",
            "You need a mining tool not a bedtime story",
            "That swing accomplished absolutely nothing",
            "I am counting zero damage and one bruise",
            "That golem looks stronger than your willpower",
            "Your plan is missing one vital tool a pickaxe",
            "Rocks are not weak to optimism",
            "You cannot carve ore with courage",
            "I think you are just buffing its armor",
            "That noise was the sound of failure",
            "You might as well tickle it with a feather",
            "You need to mine it not annoy it",
            "Your blade is not doing any mining today",
            "That golem is fine you on the other hand look confused",
            "I am guessing you forgot your pickaxe again",
            "That swing had effort but no effect",
            "Maybe try equipping something useful",
            "You cannot hit metal with wood and expect results",
            "That ore golem just shrugged at you",
            "Your weapon just learned humility",
            "That plan was impressive for all the wrong reasons",
            "You need a pickaxe unless you plan to cry on it",
            "That rock monster does not care about your damage",
            "You are just decorating it with scratches now",
            "I think it likes the attention more than the attack",
            "Your weapon might need therapy after that hit",
            "That golem looks offended you even tried",
            "You are not mining you are embarrassing yourself",
            "You might as well throw compliments instead",
            "That rock is too solid for wishful thinking",
            "You need a tool made for mining not for stabbing",
            "Your current setup is what I call decorative combat",
            "That golem took zero damage and full disrespect",
            "You are hitting the wrong kind of target here",
            "Maybe equip a pickaxe before we both lose faith",
            "Your weapon choice is questionable at best",
            "That hit sounded like disappointment",
            "Rocks have a hardness rating you know",
            "That ore golem just blinked and kept walking",
            "You cannot brute force geology",
            "Maybe stop before you chip your sword",
            "Your weapon is not going to survive this",
            "That golem is tougher than your plans",
            "You need a pickaxe to even scratch that thing",
            "You are attacking something that literally eats weapons",
            "That hit registered as a compliment",
            "I think you just made it angrier not weaker",
            "Maybe stop decorating it with scratches",
            "Your attacks are less damage more polishing service",
            "You cannot mine ore by stabbing it repeatedly",
            "That golem probably thinks this is adorable",
            "You are hitting it like it owes you rent",
            "Your sword is learning humility one clang at a time",
            "That sound was not success it was your blade crying",
            "You cannot fight a boulder with bravery alone",
            "That thing is made of solid ore not paper",
            "You need a pickaxe or divine intervention",
            "Your weapon is tired and so am I",
            "I am starting to think you do this for fun",
            "That golem looks barely aware of your existence",
            "Maybe equip a pickaxe before round two",
            "You cannot stab your way through minerals",
            "That attack was ambitious and completely pointless",
            "Your sword did its best but science says no",
            "You are not fighting monsters you are vandalizing geology",
            "That golem is fine you are just making noise",
            "You need to mine smart not hit harder",
            "Your weapon cannot solve every rock problem",
            "That ore golem looks smug about surviving that",
            "You cannot kill ore with confidence alone",
            "That golem took zero damage and all of my secondhand embarrassment",
            "You need a pickaxe to make progress not a pep talk",
            "Maybe next time bring the right tool to the rock fight",
            "Your sword is not cut out for this job literally",
            "You are adorable when you forget the pickaxe again",
            "That plan failed and the golem is still shining",
            "You cannot stab minerals into submission trust me",
            "You might as well wave at it instead of swinging",
            "That golem is tougher than you and your sword combined",
            "You need a pickaxe unless you want to spend the next hour making sparks"
        };

        /// <summary>Fallback message returned when the player ore golem pickaxe pool is empty.</summary>
        private const string PlayerOreGolemPickaxeReminderFallbackLine = "That is an ore golem not a punching bag you need a pickaxe";

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

        /// <summary>Pool of responses when the companion is asked to bank items while out of range.</summary>
        private static readonly string[] BankOutOfRangeChatMessages =
        {
            "I need to be closer to a bank.",
            "My magic powers, cannot reach from here.",
            "My powers can't reach the bank from here.",
            "We need to move closer, for me to do that.",
            "I need to get closer, to the bank.",
            "Let's try moving closer, to do that.",
            "I'm unable to reach from here.",
            "That's out of reach at the moment."
        };

        /// <summary>Fallback message used when no bank out-of-range lines can be selected.</summary>
        private const string BankOutOfRangeFallbackLine = "I need to be closer to a bank.";

        /// <summary>Pool of snarky responses when the companion is asked to bank an empty inventory.</summary>
        private static readonly string[] BankDepositEmptyInventoryMessages =
        {
            "What do you want me to add...the dust in my inv?<emoji=17>",
            "Nothing to add, boss.",
            "I haven't even got anything to add.",
            "Did you mean to do that, lol...<emoji=20>",
            "My inventory is empty."
        };

        /// <summary>Fallback line used when the empty-inventory pool fails to produce a response.</summary>
        private const string BankDepositEmptyInventoryFallbackLine = "Nothing to add, boss.";

        /// <summary>Pool of contextual reactions when companions recognise they have entered a bank.</summary>
        private static readonly string[] BankAwarenessChatMessages =
        {
            "Oooh, we're at the bank.",
            "Well, hello banker.",
            "Ah, we're at a bank in Viosla."
        };

        /// <summary>Fallback chat used if the bank awareness pool cannot provide a line.</summary>
        private const string BankAwarenessFallbackLine = "Looks like we're at the bank.";

        /// <summary>Pool containing the special bank line that should only appear when the companion is full.</summary>
        private static readonly string[] BankAwarenessInventoryFullChatMessages =
        {
            "My inventory is full, maybe I should deposit these items."
        };

        /// <summary>Fallback used when the bank full-awareness pool is empty or malformed.</summary>
        private const string BankAwarenessInventoryFullFallbackLine = "My inventory is full, maybe I should deposit these items.";

        /// <summary>Pool of flavour lines triggered when the companion enters a mining cave.</summary>
        private static readonly string[] MiningCaveAwarenessChatMessages =
        {
            "Time for some mining, eh?",
            "I hope there's no ore golems in here.",
            "I do enjoy a bit of mining.",
            "Are we here to mine?"
        };

        /// <summary>Fallback message for mining cave awareness when the pool is unavailable.</summary>
        private const string MiningCaveAwarenessFallbackLine = "Time for some mining, eh?";

        /// <summary>Pool of quips triggered when the companion steps inside a goblin cave.</summary>
        private static readonly string[] GoblinCaveAwarenessChatMessages =
        {
            "I really hate goblins.",
            "Time to kill some goblins, hmm?",
            "Damn green buggers.",
            "Let's kill some goblins."
        };

        /// <summary>Fallback chat for goblin cave awareness when no pool entry is available.</summary>
        private const string GoblinCaveAwarenessFallbackLine = "Time to kill some goblins, hmm?";

        /// <summary>Pool of flavour lines used when the companion reaches the ocean.</summary>
        private static readonly string[] OceanAwarenessChatMessages =
        {
            "Ah, I do love the view of the ocean.",
            "The sweet smell of the salty ocean.",
            "Do you ever wonder what else is out there?",
            "Do you think there's any sea monsters?"
        };

        /// <summary>Fallback line when the ocean awareness pool is empty.</summary>
        private const string OceanAwarenessFallbackLine = "Ah, I do love the view of the ocean.";

        /// <summary>Pool of flavour lines used when the companion notices a graveyard.</summary>
        private static readonly string[] GraveyardAwarenessChatMessages =
        {
            "I hope no ghosts pop out, {playerName}.",
            "Graveyards really are creepy.",
            "I hate graveyards.",
            "I don't want to run into a ghost here.",
            "I hope the dead are resting easy."
        };

        /// <summary>Fallback line when the graveyard awareness pool cannot produce a message.</summary>
        private const string GraveyardAwarenessFallbackLine = "Graveyards really are creepy.";

        /// <summary>Pool of flavour lines used when the companion manually spawns from an item drop.</summary>
        private static readonly string[] ManualSpawnGreetingChatMessages =
        {
            "Whoa, fresh air again. That was a long nap.",
            "Hey, I’m back. Miss me.",
            "Summoned again, I see.",
            "Well, look who dropped my stone.",
            "Did you miss me, or did you just need help again.",
            "I knew you couldn’t resist bringing me back.",
            "Back in the world, and ready to go.",
            "Ah, that summoning felt nice.",
            "I’m back, and I’m feeling great.",
            "Finally, some sunlight.",
            "Hey, careful where you throw that next time.",
            "Is it that time again, adventure time.",
            "Here I am, freshly spawned and ready for chaos.",
            "I live again, thanks for that.",
            "Guess who’s back from the void.",
            "Summoning successful, I hope.",
            "I’m free, and I expect snacks.",
            "Ah, it feels good to move again.",
            "Hey, next time warn me before you throw me.",
            "You called, and here I am.",
            "I’m back on duty, partner.",
            "Ah, my favorite human remembered me.",
            "Back again, and not a moment too soon.",
            "I was getting bored in there.",
            "You dropped it, and poof, here I am.",
            "I’m back, try not to drop me next time.",
            "Back to life, just like that.",
            "I knew you’d need me sooner or later.",
            "Did someone summon perfection again.",
            "I felt the call, and I answered.",
            "Well, that was dramatic.",
            "I’m back, what did I miss.",
            "Ah, home sweet overworld.",
            "Summoning complete, let’s go cause trouble.",
            "I was hoping you’d drop that item again.",
            "I’m free, and ready for action.",
            "Back in one piece, lucky you.",
            "Nice toss, you almost broke it.",
            "I heard the call and came running.",
            "Hey, don’t drop me in the mud next time.",
            "I’m alive again, time to make things interesting.",
            "Back to reality, let’s move.",
            "Hey, I’m here, stop staring.",
            "Wow, the air smells different this time.",
            "Did you miss me or just my combat stats.",
            "I’m back, let’s make this worth it.",
            "Finally, some excitement again.",
            "I was starting to think you forgot how to summon me.",
            "Hey, that felt weird, but I’m here.",
            "Ta-da, your favorite companion returns.",
            "Back again, and yes, I still look amazing.",
            "I knew you’d call me eventually.",
            "You dropped it, and I appeared, like magic.",
            "Back in the fight, ready as ever.",
            "Ah, that warm summoning light, feels like home.",
            "I’m back, and I hope this time we find treasure.",
            "Did you really summon me just to carry stuff.",
            "I’m here, and I already regret it a little.",
            "I’m ready, let’s make this adventure better than the last one.",
            "You finally dropped it again, huh.",
            "Is this where I’m supposed to say thank you for summoning me.",
            "I’m back, don’t make me regret it.",
            "Summoned again, let’s see how long I last this time.",
            "Back to business, boss.",
            "Hey, that was fast. I barely had time to rest.",
            "Did you drop that on purpose or by accident.",
            "I felt that, thanks for bringing me back.",
            "Here I am, fresh from storage.",
            "I’m back and ready to guard you again.",
            "That felt weird, but it worked.",
            "Guess who’s back to save your life again.",
            "Ah, it’s good to exist again.",
            "I’ve returned, let’s not waste time.",
            "You dropped the charm, so I came running.",
            "Here I am, back where I belong.",
            "I’m alive again, hooray.",
            "Hey, it’s me. Missed me.",
            "Finally, some movement. I was stiff in there.",
            "Summoned and ready, what’s next.",
            "Hey, I knew you’d call me again.",
            "Back online, ready for duty.",
            "Ah, the familiar sound of being summoned.",
            "I’m here, hope you brought snacks.",
            "You dropped the relic, so I showed up.",
            "Guess who’s back in action.",
            "I heard your call loud and clear.",
            "Back from storage, same as always.",
            "Alright, who dropped the thing this time.",
            "I’m here now, try not to lose me.",
            "Ah, my favorite summoner strikes again.",
            "I’ve returned to service.",
            "Feels good to be summoned again.",
            "I was wondering when you’d bring me back.",
            "You actually remembered how to summon me, impressive.",
            "I’m back, don’t make me carry everything again.",
            "That was a rough landing.",
            "I’ve returned, and I’m ready to fight.",
            "Back to reality, let’s go.",
            "Hey, that tickled.",
            "I’m back, please tell me there’s loot nearby.",
            "Summoning successful, everything’s bright again.",
            "Here we go again.",
            "I’m free, don’t you dare despawn me too soon.",
            "You brought me back, so let’s make it count.",
            "I’m alive, let’s cause some chaos.",
            "You dropped it again, didn’t you.",
            "Hello world, Isla has entered the chat.",
            "Back from limbo, and still fabulous.",
            "Did you summon me just to stare.",
            "I’m back, hope you’ve improved since last time.",
            "Well, that was a soft spawn, thanks.",
            "I’m back, ready to guard you again.",
            "Ah, the summoning spark, I love that feeling.",
            "I hope this means you need me for something cool.",
            "Here I am, freshly spawned and mildly confused.",
            "You finally decided to bring me back.",
            "I’m back from storage, still loyal as ever.",
            "Okay, that was a clean summon.",
            "I’m alive again, let’s not waste time.",
            "You called, I came, as always.",
            "Back in the game, partner.",
            "I felt that summoning pulse, strong as ever.",
            "I’m here again, like I never left.",
            "Summoning successful, report for duty complete.",
            "Hey, I was starting to like the quiet, but fine.",
            "I’m back, did you miss my voice.",
            "That summoning always feels funny.",
            "You dropped the relic, and here I am again.",
            "Back again, and fully charged.",
            "I’m back, ready to cause some mischief.",
            "I knew you’d bring me back eventually.",
            "I’m here again, try not to lose me this time.",
            "Summoning successful, system online.",
            "Okay, that was quick, I barely had time to rest.",
            "Back again, and ready for battle.",
            "I’m free once more, let’s do something fun."
        };

        /// <summary>Fallback message returned when the companion manual spawn pool is empty.</summary>
        private const string ManualSpawnGreetingFallbackLine = "Here I am, freshly spawned and ready for chaos.";

        /// <summary>Pool of flavour lines emitted when the companion is manually stored by the player.</summary>
        private static readonly string[] ManualStoreChatMessages =
        {
            "Hey, careful where you grab that, I’m vanishing!",
            "Wait, you’re putting me away already?",
            "Goodbye for now, I’ll be waiting.",
            "Oh, we’re doing this again, huh? Fine.",
            "Back into the item I go.",
            "Guess you don’t need me right now.",
            "I’ll be in my charm if you need me.",
            "Alright, I’ll take a nap inside the item again.",
            "See you soon, boss.",
            "Going back in, don’t forget me this time.",
            "Wait, what? Oh, okay, I’m being stored again.",
            "I’ll be right here when you need me.",
            "Fine, I’ll disappear. Try not to miss me too much.",
            "Alright, back to storage.",
            "Vanishing time. Try not to break anything while I’m gone.",
            "Back to the charm dimension I go.",
            "Okay, I get it, alone time. I’ll wait.",
            "Don’t leave me in there too long, okay?",
            "Alright, bye for now.",
            "Disappearing now, be safe out there.",
            "I’ll just… poof then.",
            "Heading back to my cozy little prison.",
            "Okay, okay, I’ll go quietly this time.",
            "Good luck without me.",
            "Putting me away already? I see how it is.",
            "Alright, I’ll vanish for now.",
            "I’ll miss you, even if it’s just a bit.",
            "Don’t forget to bring me back soon.",
            "I’ll be waiting patiently. Kind of.",
            "Alright, back to the void for me.",
            "Guess I’ll go charge my magic again.",
            "I’m gone, but I’ll be back before you know it.",
            "Back into the shiny rock, see you soon.",
            "Try not to get into trouble without me.",
            "I’ll be safe in the item until you call again.",
            "Alright, see you on the other side.",
            "I’ll nap until you need me again.",
            "Bye, but only for now.",
            "You’re putting me away already? Rude.",
            "Alright, goodbye for now. Don’t die.",
            "See you later, partner.",
            "I’ll disappear for now, but keep me close.",
            "I’ll just chill inside the charm.",
            "Going dormant again. Wake me up soon.",
            "Goodbye, I’ll be right here waiting.",
            "I’ll be watching from inside the charm.",
            "Back to sparkle space, bye!",
            "Alright, I’ll head out quietly.",
            "Putting me away again, huh? Typical.",
            "Fine, I’ll vanish for now.",
            "Guess it’s rest time for me.",
            "I’ll catch you later, hero.",
            "Vanishing... hope you know what you’re doing.",
            "Alright, I’ll wait where it’s dark and quiet.",
            "Don’t lose my charm while I’m gone.",
            "Heading back in, it’s weird in there.",
            "Okay, disappearing now, good luck out there.",
            "Goodbye for now, summon me soon.",
            "I’ll be back when you need me.",
            "Alright, I’m going back inside the charm.",
            "Bye for now, I’ll miss the sunlight.",
            "Going quiet again, don’t forget about me.",
            "I’ll rest a bit until next time.",
            "Vanishing complete, I’ll dream of adventures.",
            "I’m going back to sleep inside the item.",
            "Goodbye, and try not to die while I’m gone.",
            "I’ll be gone for now, stay safe.",
            "Alright, stashing me again, are you?",
            "Fine, I’ll just fade away.",
            "Back to my little pocket dimension.",
            "Okay, I’ll vanish, but I’ll remember this.",
            "I’ll be waiting patiently in there.",
            "Goodbye, call me soon.",
            "See you later, brave one.",
            "I’ll be safe inside, you better be too.",
            "Alright, I’ll stop existing for a bit.",
            "I’ll head back into the charm. Again.",
            "Fine, I’ll take a break.",
            "Alright, going dormant. Have fun.",
            "Disappearing, try not to miss me.",
            "Goodbye, I’ll nap in the shiny rock.",
            "Okay, okay, I’ll vanish before you trip on me.",
            "Back to storage, same as always.",
            "Don’t worry, I’ll be fine in there.",
            "I’ll fade away for now, take care.",
            "Alright, I’ll rest until you drop the charm again.",
            "Goodbye, and be careful out there.",
            "See you soon, don’t lose my charm.",
            "Alright, time to vanish again.",
            "I’ll be gone a while, but not forever.",
            "I’ll be waiting in the void for your next summon.",
            "Going back in, stay out of trouble.",
            "Goodbye for now, my favourite human.",
            "Fine, you handle things, I’ll rest.",
            "Okay, I’ll disappear, but not happily.",
            "I’ll fade into the item again. Bye.",
            "Alright, I’m out. Don’t screw up.",
            "Goodbye, remember to feed yourself this time.",
            "Okay, I’m leaving before you change your mind.",
            "Resting inside the charm, call me if you get lonely.",
            "Alright, I’m gone. Miss me already?",
            "Back to being a magical thought in a trinket.",
            "I’ll vanish now, see you when you need me.",
            "Okay, I’ll just fade back into sparkly limbo.",
            "I’ll be fine in there, really.",
            "Alright, poof, I’m gone.",
            "Guess I’ll go back to my pocket universe.",
            "Okay, bye, summon me soon.",
            "I’ll be taking a break inside the charm.",
            "Vanishing now, take care.",
            "Fine, I’ll vanish, but I expect snacks later.",
            "Going back into my charm-shaped home.",
            "Goodbye, boss. Try not to fall in a hole.",
            "I’ll be right where you left me, kind of.",
            "Okay, I’ll take a short rest. Don’t take too long.",
            "Alright, you’re putting me away, so I’ll behave.",
            "I’ll be in the charm, practicing patience.",
            "Bye, I’ll be dreaming of loot.",
            "Alright, I’ll stop existing for now.",
            "Don’t forget me, I’ll know if you do.",
            "Goodbye, and don’t drop me next time.",
            "I’ll vanish into sparkle land again.",
            "Okay, time to go invisible.",
            "See you next time, hero.",
            "I’ll vanish now, until you call again.",
            "Alright, see you on the next summon.",
            "Back into shiny prison I go.",
            "I’ll be waiting quietly in the charm.",
            "Goodbye, call me when you’re ready again.",
            "Fine, I’ll go. But you owe me one.",
            "Okay, I’ll vanish gracefully this time.",
            "Back to my crystal nap spot.",
            "I’ll be sleeping inside until you wake me up again.",
            "Alright, that’s my cue to disappear.",
            "See you soon, don’t do anything dumb.",
            "Goodbye, and good luck out there.",
            "Okay, I’m fading away now, bye."
        };

        /// <summary>Fallback message returned when the companion manual store pool is empty.</summary>
        private const string ManualStoreFallbackLine = "Hey, careful where you grab that, I’m vanishing!";

        /// <summary>Pool of flavour lines used when the companion automatically returns after a login.</summary>
        private static readonly string[] AutoSpawnGreetingChatMessages =
        {
            "You're back! I missed you.",
            "Oh, you’re awake again.",
            "Hey, you finally logged back in.",
            "Welcome back, sleepyhead.",
            "Back from the void, huh?",
            "Morning, or whatever time it is.",
            "You disappeared on me for a bit there.",
            "Hey, I was just about to go look for you.",
            "There you are, I was starting to worry.",
            "You’re back, I knew you wouldn’t stay gone long.",
            "Look who decided to show up again.",
            "Finally, I was getting bored.",
            "Heya, you left me waiting.",
            "Welcome back, partner.",
            "Guess who’s been standing around waiting for you, me.",
            "I thought you’d left me for good.",
            "Ah, there you are, took you long enough.",
            "Don’t scare me like that again, I thought I was alone.",
            "Good timing, I just respawned too.",
            "Back from your mysterious disappearance, I see.",
            "I was guarding your spot the whole time.",
            "Finally, I get to see you again.",
            "Missed me, because I definitely missed you.",
            "Hey, don’t make me wait that long next time.",
            "Back online, I knew you’d return.",
            "I kept your place warm.",
            "You’re back, I was starting to talk to myself.",
            "There you are, I thought the world swallowed you.",
            "Back in action, huh.",
            "Welcome back, boss, everything’s as you left it.",
            "I knew you’d return eventually.",
            "Is it time for another adventure.",
            "About time you woke up.",
            "You left me all alone in the dark.",
            "Back from the land of the offline, I see.",
            "Hello again, I was wondering when you’d log in.",
            "Hey, I just spawned, miss me.",
            "Nice of you to finally show up.",
            "Back to reality, or something like it.",
            "Ah, there you are, thought I’d lost you.",
            "You were gone ages, I almost learned to cook.",
            "I was starting to think I’d have to explore without you.",
            "You're back, and I’m ready for anything.",
            "Welcome back, I didn’t touch your stuff, promise.",
            "Finally, someone to talk to again.",
            "Hey, you left me unsupervised, dangerous move.",
            "There you are, I was beginning to get lonely.",
            "Nice timing, I was just about to nap forever.",
            "Back already, I was mid dream.",
            "Good to see you again, hero.",
            "I was just dreaming about you, weird coincidence.",
            "Welcome back, let’s get moving.",
            "Back online, I didn’t even get to clean up.",
            "Hey, you’re back, ready to cause trouble again.",
            "Don’t worry, I kept everything safe.",
            "You logged in, great, I can stop guarding rocks now.",
            "Finally, a familiar face.",
            "I was starting to think you’d respawn somewhere else.",
            "Hey, don’t vanish like that again.",
            "Good to see you alive and relatively sane.",
            "I stayed exactly where you left me, good companion points, right.",
            "Welcome back, did you bring snacks.",
            "There you are, I’ve been counting the seconds.",
            "Back again, couldn’t stay away huh.",
            "I survived while you were gone, somehow.",
            "You’re back, I didn’t even get to finish my nap.",
            "Finally, I’ve been standing here so long I grew moss.",
            "Welcome back, sleepy adventurer.",
            "Hey, don’t worry, I didn’t let anyone steal your spot.",
            "Ah, back to business then.",
            "You came back, my faith in you is restored.",
            "I was guarding your loot while you were offline.",
            "Hey, boss, you look refreshed.",
            "Welcome back, did you miss my voice.",
            "I’m ready, let’s pick up where we left off.",
            "You vanished for ages again.",
            "You logged back in, yay, now let’s get to work.",
            "Hey, I was worried, thought you’d forgotten me.",
            "You’re back, I didn’t even get a postcard.",
            "About time, I’ve been staring into nothing for hours.",
            "Hey, you’re back, and yes, I counted how long you were gone.",
            "You left me alone in a field again.",
            "Welcome back, I had a staring contest with a rock while you were gone.",
            "I knew you couldn’t survive without me for long.",
            "Back from your offline adventure.",
            "You're back, the world’s been quiet without you.",
            "Don’t vanish like that next time, it gets lonely here.",
            "Hey, the server missed you, and so did I.",
            "Finally back, took your sweet time.",
            "I was just about to log off myself.",
            "Back online, let’s make some noise.",
            "Hey, good to see you again, I almost joined another player.",
            "Welcome back, partner, ready to get in trouble again.",
            "There you are, I thought you’d forgotten me.",
            "Back again, how was your nothingness.",
            "Hey, don’t leave me hanging next time.",
            "I was guarding your spot so well I scared myself.",
            "Nice to see you, stranger.",
            "You logged in, I knew it.",
            "Hey, it’s you again, let’s go find some chaos.",
            "I stayed put like a good girl.",
            "Welcome back, I hope you brought energy.",
            "Finally, it’s adventure time again.",
            "I didn’t move an inch while you were gone.",
            "Back already, couldn’t resist huh.",
            "Welcome back, boss, I kept things under control.",
            "Good morning, or evening, whatever it is.",
            "Hey, don’t ghost me next time.",
            "You're back, now it feels right again.",
            "I thought you’d left me for another companion.",
            "Welcome back, everything’s still a mess by the way.",
            "I’ve been waiting forever, you owe me snacks.",
            "There you are, my favourite human.",
            "You’re back, let’s pretend you didn’t abandon me.",
            "Good to see you again, I missed having company.",
            "Hey, don’t disappear like that, I worry.",
            "Finally, someone to talk to again.",
            "Back online, let’s make up for lost time.",
            "You were gone ages, I almost tamed a squirrel.",
            "Welcome back, I’ve been rehearsing my battle stance.",
            "I stayed faithful like a good companion should.",
            "Back online, perfect timing, I’m bored.",
            "Good to see you, I was starting to monologue.",
            "You're here, now things can get interesting again."
        };

        /// <summary>Fallback message returned when the auto-spawn greeting pool is empty.</summary>
        private const string AutoSpawnGreetingFallbackLine = "You're back! I missed you.";

        /// <summary>
        /// Returns a random chat line for when the companion cannot equip an item due to a single missing requirement.
        /// </summary>
        public static string GetRandomCompanionEquipmentSingleRequirementFailureLine()
        {
            return GetRandomLine(
                CompanionEquipmentSingleRequirementFailureChatMessages,
                CompanionEquipmentSingleRequirementFailureChatFallbackLine);
        }

        /// <summary>
        /// Returns a random chat line for when the companion cannot equip an item due to several missing requirements.
        /// </summary>
        public static string GetRandomCompanionEquipmentMultipleRequirementFailureLine()
        {
            return GetRandomLine(
                CompanionEquipmentMultipleRequirementFailureChatMessages,
                CompanionEquipmentMultipleRequirementFailureChatFallbackLine);
        }

        /// <summary>
        /// Returns a floating-text line describing the primary requirement blocking an equip attempt.
        /// </summary>
        /// <param name="companionName">Display name of the active companion.</param>
        /// <param name="requirement">Primary requirement preventing the equip.</param>
        public static string GetRandomCompanionEquipmentSingleRequirementFloatingTextLine(
            string companionName,
            SkillRequirement requirement)
        {
            string template = GetRandomLine(
                CompanionEquipmentSingleRequirementFailureFloatingTextTemplates,
                CompanionEquipmentSingleRequirementFailureFloatingTextFallback);

            string safeName = string.IsNullOrWhiteSpace(companionName)
                ? "Your companion"
                : companionName.Trim();

            int clampedLevel = Mathf.Max(1, requirement.level);
            string levelText = clampedLevel.ToString(CultureInfo.InvariantCulture);
            string skillName = requirement.skill.ToString();
            string requirementText = string.Format(
                CultureInfo.InvariantCulture,
                "{0} {1}",
                levelText,
                skillName);

            string resolved = template.Replace("{companionName}", safeName);
            resolved = resolved.Replace("{requirement}", requirementText);
            resolved = resolved.Replace("{level}", levelText);
            resolved = resolved.Replace("{skill}", skillName);
            return resolved;
        }

        /// <summary>
        /// Returns a floating-text line describing that multiple requirements block an equip attempt.
        /// </summary>
        /// <param name="companionName">Display name of the active companion.</param>
        public static string GetRandomCompanionEquipmentMultipleRequirementFloatingTextLine(string companionName)
        {
            string template = GetRandomLine(
                CompanionEquipmentMultipleRequirementFailureFloatingTextTemplates,
                CompanionEquipmentMultipleRequirementFailureFloatingTextFallback);

            string safeName = string.IsNullOrWhiteSpace(companionName)
                ? "Your companion"
                : companionName.Trim();
            return template.Replace("{companionName}", safeName);
        }

        /// <summary>
        /// Returns floating-text feedback when the companion cannot equip additional items because a stack is capped.
        /// </summary>
        /// <param name="companionName">Display name of the companion.</param>
        /// <param name="itemName">Display-ready item name that hit the stack limit.</param>
        /// <param name="playerName">Display name of the issuing player.</param>
        public static string GetRandomCompanionStackLimitFloatingTextLine(
            string companionName,
            string itemName,
            string playerName)
        {
            string template = GetRandomLine(
                CompanionEquipmentStackLimitFloatingTextTemplates,
                CompanionEquipmentStackLimitFloatingTextFallback);

            template = ApplyCompanionName(template, companionName);
            template = ApplyPlayerName(template, playerName);
            template = ApplyItemName(template, itemName);
            return template;
        }

        /// <summary>
        /// Returns floating-text feedback when the companion equipment inventory is full.
        /// </summary>
        /// <param name="companionName">Display name of the companion.</param>
        /// <param name="playerName">Display name of the issuing player.</param>
        public static string GetRandomCompanionInventoryFullFloatingTextLine(string companionName, string playerName)
        {
            string template = GetRandomLine(
                CompanionEquipmentInventoryFullFloatingTextTemplates,
                CompanionEquipmentInventoryFullFloatingTextFallback);

            template = ApplyCompanionName(template, companionName);
            template = ApplyPlayerName(template, playerName);
            return template;
        }

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
        /// Returns a random chat line for situations where the companion cannot locate any rocks to mine.
        /// </summary>
        /// <returns>Companion chat line describing the absence of mineable rocks.</returns>
        public static string GetRandomNoRocksLine()
        {
            return GetRandomLine(NoRocksChatMessages, NoRocksFallbackLine);
        }

        /// <summary>
        /// Returns a summon-required line for mining commands so caller logic stays consistent.
        /// </summary>
        /// <param name="playerName">Display name of the issuing player for placeholder substitution.</param>
        public static string GetRandomMiningSummonRequiredLine(string playerName)
        {
            return ResolveLineWithPlayerName(
                MiningSummonRequiredChatMessages,
                MiningSummonRequiredFallbackLine,
                playerName);
        }

        /// <summary>
        /// Returns a generic mining failure line when no rocks can be found nearby.
        /// </summary>
        /// <param name="playerName">Display name of the issuing player for placeholder substitution.</param>
        public static string GetRandomMiningGenericFailureLine(string playerName)
        {
            return ResolveLineWithPlayerName(
                MiningGenericFailureChatMessages,
                MiningGenericFailureFallbackLine,
                playerName);
        }

        /// <summary>
        /// Returns a summon-required line for woodcutting commands so caller logic stays consistent.
        /// </summary>
        /// <param name="playerName">Display name of the issuing player for placeholder substitution.</param>
        public static string GetRandomWoodcuttingSummonRequiredLine(string playerName)
        {
            return ResolveLineWithPlayerName(
                WoodcuttingSummonRequiredChatMessages,
                WoodcuttingSummonRequiredFallbackLine,
                playerName);
        }

        /// <summary>
        /// Returns a generic woodcutting failure line when no trees can be located nearby.
        /// </summary>
        /// <param name="playerName">Display name of the issuing player for placeholder substitution.</param>
        public static string GetRandomWoodcuttingGenericFailureLine(string playerName)
        {
            return ResolveLineWithPlayerName(
                WoodcuttingGenericFailureChatMessages,
                WoodcuttingGenericFailureFallbackLine,
                playerName);
        }

        /// <summary>
        /// Returns a summon-required line for fishing commands so caller logic stays consistent.
        /// </summary>
        /// <param name="playerName">Display name of the issuing player for placeholder substitution.</param>
        public static string GetRandomFishingSummonRequiredLine(string playerName)
        {
            return ResolveLineWithPlayerName(
                FishingSummonRequiredChatMessages,
                FishingSummonRequiredFallbackLine,
                playerName);
        }

        /// <summary>
        /// Returns a generic fishing failure line when no valid spots exist nearby.
        /// </summary>
        /// <param name="playerName">Display name of the issuing player for placeholder substitution.</param>
        public static string GetRandomFishingGenericFailureLine(string playerName)
        {
            return ResolveLineWithPlayerName(
                FishingGenericFailureChatMessages,
                FishingGenericFailureFallbackLine,
                playerName);
        }

        /// <summary>
        /// Returns a summon-required line for cooking commands so caller logic stays consistent.
        /// </summary>
        /// <param name="playerName">Display name of the issuing player for placeholder substitution.</param>
        public static string GetRandomCookingSummonRequiredLine(string playerName)
        {
            return ResolveLineWithPlayerName(
                CookingSummonRequiredChatMessages,
                CookingSummonRequiredFallbackLine,
                playerName);
        }

        /// <summary>
        /// Returns a generic cooking failure line when no stations are available.
        /// </summary>
        /// <param name="playerName">Display name of the issuing player for placeholder substitution.</param>
        public static string GetRandomCookingGenericFailureLine(string playerName)
        {
            return ResolveLineWithPlayerName(
                CookingGenericFailureChatMessages,
                CookingGenericFailureFallbackLine,
                playerName);
        }

        /// <summary>
        /// Returns a random chat line when the companion refuses to mine because a cooldown is still active.
        /// </summary>
        /// <param name="playerName">Name of the active player used for placeholder replacement.</param>
        /// <param name="minutes">Remaining cooldown length expressed in whole minutes.</param>
        public static string GetRandomMiningDeclineCooldownLine(string playerName, int minutes)
        {
            return FormatCooldownLine(
                MiningDeclineCooldownChatMessages,
                MiningDeclineCooldownFallbackLine,
                playerName,
                minutes);
        }

        /// <summary>
        /// Returns a random chat line when the companion refuses to woodcut because a cooldown is still active.
        /// </summary>
        /// <param name="playerName">Name of the active player used for placeholder replacement.</param>
        /// <param name="minutes">Remaining cooldown length expressed in whole minutes.</param>
        public static string GetRandomWoodcuttingDeclineCooldownLine(string playerName, int minutes)
        {
            return FormatCooldownLine(
                WoodcuttingDeclineCooldownChatMessages,
                WoodcuttingDeclineCooldownFallbackLine,
                playerName,
                minutes);
        }

        /// <summary>
        /// Returns a random chat line when the companion refuses to fish because a cooldown is still active.
        /// </summary>
        /// <param name="playerName">Name of the active player used for placeholder replacement.</param>
        /// <param name="minutes">Remaining cooldown length expressed in whole minutes.</param>
        public static string GetRandomFishingDeclineCooldownLine(string playerName, int minutes)
        {
            return FormatCooldownLine(
                FishingDeclineCooldownChatMessages,
                FishingDeclineCooldownFallbackLine,
                playerName,
                minutes);
        }

        /// <summary>
        /// Returns a random chat line when the companion refuses combat due to an active cooldown.
        /// </summary>
        /// <param name="playerName">Name of the active player used for placeholder replacement.</param>
        /// <param name="minutes">Remaining cooldown length expressed in whole minutes.</param>
        public static string GetRandomCombatDeclineCooldownLine(string playerName, int minutes)
        {
            return FormatCooldownLine(
                CombatDeclineCooldownChatMessages,
                CombatDeclineCooldownFallbackLine,
                playerName,
                minutes);
        }

        /// <summary>
        /// Returns a random chat line for when the player's inventory is full but the companion can still help.
        /// </summary>
        public static string GetRandomPlayerInventoryFullLine()
        {
            return GetRandomLine(PlayerInventoryFullChatMessages, PlayerInventoryFullFallbackLine);
        }

        /// <summary>
        /// Formats a cooldown template with the supplied player name and duration, ensuring sensible fallbacks.
        /// </summary>
        /// <param name="pool">Pool of cooldown templates to select from.</param>
        /// <param name="fallback">Fallback template used when the pool is empty.</param>
        /// <param name="playerName">Name of the active player.</param>
        /// <param name="minutes">Cooldown duration expressed in whole minutes.</param>
        /// <returns>Formatted cooldown line with placeholders resolved.</returns>
        private static string FormatCooldownLine(
            string[] pool,
            string fallback,
            string playerName,
            int minutes)
        {
            string template = GetRandomLine(pool, fallback);
            if (string.IsNullOrWhiteSpace(template))
                template = fallback;

            string safePlayerName = string.IsNullOrWhiteSpace(playerName) ? "friend" : playerName.Trim();
            int clampedMinutes = Mathf.Max(1, minutes);
            string minutesText = clampedMinutes == 1
                ? "1 minute"
                : string.Format(CultureInfo.InvariantCulture, "{0} minutes", clampedMinutes);

            string resolved = template.Replace("{playerName}", safePlayerName);
            resolved = resolved.Replace("{minutes}", minutesText);
            return resolved;
        }

        /// <summary>
        /// Returns a random chat line for when both the player and companion inventories are out of space.
        /// </summary>
        public static string GetRandomPlayerAndCompanionInventoryFullLine()
        {
            return GetRandomLine(PlayerAndCompanionInventoryFullChatMessages, PlayerAndCompanionInventoryFullFallbackLine);
        }

        /// <summary>
        /// Returns a bank-themed awareness line, including the inventory full warning when appropriate.
        /// </summary>
        /// <param name="companionInventoryFull">Whether the companion inventory has reached capacity.</param>
        public static string GetRandomBankAwarenessLine(bool companionInventoryFull)
        {
            if (companionInventoryFull)
            {
                int fullPool = BankAwarenessInventoryFullChatMessages != null ? BankAwarenessInventoryFullChatMessages.Length : 0;
                int generalPool = BankAwarenessChatMessages != null ? BankAwarenessChatMessages.Length : 0;
                int total = fullPool + generalPool;

                if (total <= 0)
                    return BankAwarenessInventoryFullFallbackLine;

                int index = UnityEngine.Random.Range(0, total);
                if (index < fullPool)
                    return GetRandomLine(BankAwarenessInventoryFullChatMessages, BankAwarenessInventoryFullFallbackLine);

                return GetRandomLine(BankAwarenessChatMessages, BankAwarenessFallbackLine);
            }

            return GetRandomLine(BankAwarenessChatMessages, BankAwarenessFallbackLine);
        }

        /// <summary>Returns a flavour line for mining cave awareness.</summary>
        public static string GetRandomMiningCaveAwarenessLine()
        {
            return GetRandomLine(MiningCaveAwarenessChatMessages, MiningCaveAwarenessFallbackLine);
        }

        /// <summary>Returns a flavour line for goblin cave awareness.</summary>
        public static string GetRandomGoblinCaveAwarenessLine()
        {
            return GetRandomLine(GoblinCaveAwarenessChatMessages, GoblinCaveAwarenessFallbackLine);
        }

        /// <summary>Returns a flavour line for ocean awareness.</summary>
        public static string GetRandomOceanAwarenessLine()
        {
            return GetRandomLine(OceanAwarenessChatMessages, OceanAwarenessFallbackLine);
        }

        /// <summary>Returns a graveyard-flavoured awareness line.</summary>
        public static string GetRandomGraveyardAwarenessLine()
        {
            return GetRandomLine(GraveyardAwarenessChatMessages, GraveyardAwarenessFallbackLine);
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
        /// Returns a random companion chat line when the player requests a bank deposit but no items exist.
        /// </summary>
        public static string GetRandomEmptyBankInventoryLine()
        {
            return GetRandomLine(BankDepositEmptyInventoryMessages, BankDepositEmptyInventoryFallbackLine);
        }

        /// <summary>
        /// Returns a random companion chat line when a bank deposit is requested but no bank is within range.
        /// </summary>
        public static string GetRandomBankOutOfRangeLine()
        {
            return GetRandomLine(BankOutOfRangeChatMessages, BankOutOfRangeFallbackLine);
        }

        /// <summary>
        /// Returns a random greeting line when the companion automatically respawns after a login.
        /// Ensures the companion acknowledges the player's return with flavourful dialogue.
        /// </summary>
        public static string GetRandomAutoSpawnGreetingLine()
        {
            return GetRandomLine(AutoSpawnGreetingChatMessages, AutoSpawnGreetingFallbackLine);
        }

        /// <summary>
        /// Returns a random greeting line when the companion is manually spawned by the player.
        /// Keeps item-triggered summons lively and varied.
        /// </summary>
        public static string GetRandomManualSpawnGreetingLine()
        {
            return GetRandomLine(ManualSpawnGreetingChatMessages, ManualSpawnGreetingFallbackLine);
        }

        /// <summary>
        /// Returns a random farewell line whenever the player manually stores the companion.
        /// Ensures pick up actions feel acknowledged with playful flavour text.
        /// </summary>
        public static string GetRandomManualStoreLine()
        {
            return GetRandomLine(ManualStoreChatMessages, ManualStoreFallbackLine);
        }

        /// <summary>Returns a random chat line used when the companion levels Hitpoints.</summary>
        public static string GetRandomHitpointsLevelUpLine()
        {
            return GetRandomLine(HitpointsLevelUpChatMessages, HitpointsLevelUpFallbackLine);
        }

        /// <summary>Returns a random chat line used when the player levels Hitpoints with a companion present.</summary>
        public static string GetRandomPlayerHitpointsLevelUpLine()
        {
            return GetRandomLine(PlayerHitpointsLevelUpChatMessages, PlayerHitpointsLevelUpFallbackLine);
        }

        /// <summary>Returns a random chat line used when the companion levels Defence.</summary>
        public static string GetRandomDefenceLevelUpLine()
        {
            return GetRandomLine(DefenceLevelUpChatMessages, DefenceLevelUpFallbackLine);
        }

        /// <summary>Returns a random chat line used when the player levels Defence with a companion present.</summary>
        public static string GetRandomPlayerDefenceLevelUpLine()
        {
            return GetRandomLine(PlayerDefenceLevelUpChatMessages, PlayerDefenceLevelUpFallbackLine);
        }

        /// <summary>Returns a random chat line used when the companion levels Strength.</summary>
        public static string GetRandomStrengthLevelUpLine()
        {
            return GetRandomLine(StrengthLevelUpChatMessages, StrengthLevelUpFallbackLine);
        }

        /// <summary>Returns a random chat line used when the player levels Strength with a companion present.</summary>
        public static string GetRandomPlayerStrengthLevelUpLine()
        {
            return GetRandomLine(PlayerStrengthLevelUpChatMessages, PlayerStrengthLevelUpFallbackLine);
        }

        /// <summary>Returns a random chat line used when the companion levels Attack.</summary>
        public static string GetRandomAttackLevelUpLine()
        {
            return GetRandomLine(AttackLevelUpChatMessages, AttackLevelUpFallbackLine);
        }

        /// <summary>Returns a random chat line used when the player levels Attack with a companion present.</summary>
        public static string GetRandomPlayerAttackLevelUpLine()
        {
            return GetRandomLine(PlayerAttackLevelUpChatMessages, PlayerAttackLevelUpFallbackLine);
        }

        /// <summary>Returns a random chat line used when the player levels Ranged with a companion present.</summary>
        public static string GetRandomPlayerRangedLevelUpLine()
        {
            return GetRandomLine(PlayerRangedLevelUpChatMessages, PlayerRangedLevelUpFallbackLine);
        }

        /// <summary>Returns a random chat line used when the companion levels Ranged.</summary>
        public static string GetRandomRangedLevelUpLine()
        {
            return GetRandomLine(RangedLevelUpChatMessages, RangedLevelUpFallbackLine);
        }

        /// <summary>Returns a random chat line used when the player levels Magic with a companion present.</summary>
        public static string GetRandomPlayerMagicLevelUpLine()
        {
            return GetRandomLine(PlayerMagicLevelUpChatMessages, PlayerMagicLevelUpFallbackLine);
        }

        /// <summary>Returns a random chat line used when the companion levels Magic.</summary>
        public static string GetRandomMagicLevelUpLine()
        {
            return GetRandomLine(MagicLevelUpChatMessages, MagicLevelUpFallbackLine);
        }

        /// <summary>Returns a random chat line used when the player levels Beastmaster with a companion present.</summary>
        public static string GetRandomPlayerBeastmasterLevelUpLine()
        {
            return GetRandomLine(PlayerBeastmasterLevelUpChatMessages, PlayerBeastmasterLevelUpFallbackLine);
        }

        /// <summary>Returns a random chat line used when the companion levels Beastmaster.</summary>
        public static string GetRandomBeastmasterLevelUpLine()
        {
            return GetRandomLine(BeastmasterLevelUpChatMessages, BeastmasterLevelUpFallbackLine);
        }


        /// <summary>Returns a random chat line used when the player levels Fishing with a companion present.</summary>
        public static string GetRandomPlayerFishingLevelUpLine()
        {
            return GetRandomLine(PlayerFishingLevelUpChatMessages, PlayerFishingLevelUpFallbackLine);
        }

        /// <summary>Returns a random chat line used when the player levels Cooking with a companion present.</summary>
        public static string GetRandomPlayerCookingLevelUpLine()
        {
            return GetRandomLine(PlayerCookingLevelUpChatMessages, PlayerCookingLevelUpFallbackLine);
        }

        /// <summary>Returns a random chat line used when the player levels Firemaking with a companion present.</summary>
        public static string GetRandomPlayerFiremakingLevelUpLine()
        {
            return GetRandomLine(PlayerFiremakingLevelUpChatMessages, PlayerFiremakingLevelUpFallbackLine);
        }

        /// <summary>Returns a random chat line used when the player levels Woodcutting with a companion present.</summary>
        public static string GetRandomPlayerWoodcuttingLevelUpLine()
        {
            return GetRandomLine(PlayerWoodcuttingLevelUpChatMessages, PlayerWoodcuttingLevelUpFallbackLine);
        }

        /// <summary>Returns a random chat line used when the player levels Mining with a companion present.</summary>
        public static string GetRandomPlayerMiningLevelUpLine()
        {
            return GetRandomLine(PlayerMiningLevelUpChatMessages, PlayerMiningLevelUpFallbackLine);
        }

        /// <summary>Returns a random chat line used when the companion levels Fishing.</summary>
        public static string GetRandomFishingLevelUpLine()
        {
            return GetRandomLine(FishingLevelUpChatMessages, FishingLevelUpFallbackLine);
        }

        /// <summary>Returns a random chat line used when the companion levels Cooking.</summary>
        public static string GetRandomCookingLevelUpLine()
        {
            return GetRandomLine(CookingLevelUpChatMessages, CookingLevelUpFallbackLine);
        }

        public static string GetRandomCompanionCookingStartLine()
        {
            return GetRandomLine(CompanionCookingStartChatMessages, CompanionCookingStartFallbackLine);
        }

        /// <summary>Returns a random chat line used when the companion levels Firemaking.</summary>
        public static string GetRandomFiremakingLevelUpLine()
        {
            return GetRandomLine(FiremakingLevelUpChatMessages, FiremakingLevelUpFallbackLine);
        }

        /// <summary>Returns a random chat line used when the companion levels Woodcutting.</summary>
        public static string GetRandomWoodcuttingLevelUpLine()
        {
            return GetRandomLine(WoodcuttingLevelUpChatMessages, WoodcuttingLevelUpFallbackLine);
        }

        /// <summary>Returns a random chat line used when the companion levels Mining.</summary>
        public static string GetRandomMiningLevelUpLine()
        {
            return GetRandomLine(MiningLevelUpChatMessages, MiningLevelUpFallbackLine);
        }

        /// <summary>
        /// Returns a random chat line when the companion declines to attack an ore golem without a pickaxe equipped.
        /// </summary>
        public static string GetRandomCompanionOreGolemPickaxeReminder()
        {
            return GetRandomLine(
                CompanionOreGolemPickaxeReminderMessages,
                CompanionOreGolemPickaxeReminderFallbackLine);
        }

        /// <summary>
        /// Returns a random chat line when the companion is disarmed mid combat while fighting an ore golem.
        /// </summary>
        public static string GetRandomCompanionOreGolemMidCombatPickaxeRemovalMessage()
        {
            return GetRandomLine(
                CompanionOreGolemMidCombatPickaxeRemovalMessages,
                CompanionOreGolemMidCombatPickaxeRemovalFallbackLine);
        }

        /// <summary>
        /// Returns a random chat line when the player attacks an ore golem without wielding a pickaxe while a companion is active.
        /// </summary>
        public static string GetRandomPlayerOreGolemPickaxeReminder()
        {
            return GetRandomLine(
                PlayerOreGolemPickaxeReminderMessages,
                PlayerOreGolemPickaxeReminderFallbackLine);
        }

        /// <summary>
        /// Returns a random chat line used when the player dies while a companion is active.
        /// Defaults to the snarky tone to preserve the original flavour.
        /// </summary>
        public static string GetRandomPlayerDeathLine()
        {
            return GetRandomPlayerDeathLine(CompanionChatTone.Snarky);
        }

        /// <summary>
        /// Returns a random chat line used when the player dies while a companion is active, respecting the requested tone.
        /// </summary>
        /// <param name="tone">Determines whether the line should be playful/snarky or supportive.</param>
        public static string GetRandomPlayerDeathLine(CompanionChatTone tone)
        {
            switch (tone)
            {
                case CompanionChatTone.Supportive:
                    return GetRandomLine(PlayerDeathSupportiveChatMessages, PlayerDeathSupportiveFallbackLine);
                default:
                    return GetRandomLine(PlayerDeathChatMessages, PlayerDeathFallbackLine);
            }
        }

        /// <summary>
        /// Returns a ranged-combat line explaining that the equipped ammunition is incompatible with the active weapon.
        /// </summary>
        /// <param name="companionName">Display name of the active companion.</param>
        /// <param name="weaponName">Weapon name used for substitution.</param>
        /// <param name="ammoName">Ammunition name used for substitution.</param>
        /// <param name="playerName">Display name of the player issuing the command.</param>
        public static string GetRandomCompanionAmmoRestrictionLine(
            string companionName,
            string weaponName,
            string ammoName,
            string playerName)
        {
            string template = GetRandomLine(
                CompanionAmmoRestrictionChatMessages,
                CompanionAmmoRestrictionFallbackLine);

            template = ApplyCompanionName(template, companionName);
            template = ApplyPlayerName(template, playerName);
            template = ApplyWeaponName(template, weaponName);
            template = ApplyAmmoName(template, ammoName);
            return template;
        }

        /// <summary>
        /// Returns a ranged-combat line informing the player that the companion has no ammunition remaining.
        /// </summary>
        /// <param name="companionName">Display name of the active companion.</param>
        /// <param name="weaponName">Weapon name used for substitution.</param>
        /// <param name="playerName">Display name of the player issuing the command.</param>
        public static string GetRandomCompanionAmmoDepletedLine(
            string companionName,
            string weaponName,
            string playerName)
        {
            string template = GetRandomLine(
                CompanionAmmoDepletedChatMessages,
                CompanionAmmoDepletedFallbackLine);

            template = ApplyCompanionName(template, companionName);
            template = ApplyPlayerName(template, playerName);
            template = ApplyWeaponName(template, weaponName);
            return template;
        }

        /// <summary>
        /// Resolves a random line from the supplied pool while applying an optional player-name substitution.
        /// </summary>
        /// <param name="pool">Array of potential chat lines.</param>
        /// <param name="fallback">Fallback line used when the pool is empty.</param>
        /// <param name="playerName">Player display name injected into any <c>{playerName}</c> placeholders.</param>
        private static string ResolveLineWithPlayerName(string[] pool, string fallback, string playerName)
        {
            string template = GetRandomLine(pool, fallback);
            return ApplyPlayerName(template, playerName);
        }

        /// <summary>
        /// Replaces <c>{playerName}</c> placeholders with a safe player name fallback.
        /// </summary>
        /// <param name="template">Template string that may include player placeholders.</param>
        /// <param name="playerName">Player display name to insert.</param>
        private static string ApplyPlayerName(string template, string playerName)
        {
            return ApplyToken(template, "{playerName}", "friend", playerName);
        }

        /// <summary>
        /// Replaces <c>{companionName}</c> placeholders with a safe companion name fallback.
        /// </summary>
        private static string ApplyCompanionName(string template, string companionName)
        {
            return ApplyToken(template, "{companionName}", "Your companion", companionName);
        }

        /// <summary>
        /// Replaces <c>{weaponName}</c> placeholders while guarding against null/whitespace inputs.
        /// </summary>
        private static string ApplyWeaponName(string template, string weaponName)
        {
            return ApplyToken(template, "{weaponName}", "that weapon", weaponName);
        }

        /// <summary>
        /// Replaces <c>{ammoName}</c> placeholders while guarding against null/whitespace inputs.
        /// </summary>
        private static string ApplyAmmoName(string template, string ammoName)
        {
            return ApplyToken(template, "{ammoName}", "that ammo", ammoName);
        }

        /// <summary>
        /// Replaces <c>{itemName}</c> placeholders while guarding against null/whitespace inputs.
        /// </summary>
        private static string ApplyItemName(string template, string itemName)
        {
            return ApplyToken(template, "{itemName}", "that item", itemName);
        }

        /// <summary>
        /// Shared token replacement helper that trims values and applies safe fallbacks.
        /// </summary>
        private static string ApplyToken(string template, string token, string fallback, string value)
        {
            if (string.IsNullOrEmpty(template))
                return template;

            if (template.IndexOf(token, StringComparison.Ordinal) < 0)
                return template;

            string safeValue = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
            return template.Replace(token, safeValue);
        }

        /// <summary>
        /// Builds the generic level-up acknowledgement string so ad-hoc systems can reuse the
        /// same phrasing while still supplying bespoke pronouns and skill names.
        /// </summary>
        /// <param name="possessivePronoun">Possessive pronoun preferred by the speaking companion.</param>
        /// <param name="skillName">Display-ready skill name that should appear in the message.</param>
        /// <param name="level">New level reached by the companion.</param>
        /// <returns>Formatted chat line celebrating the level up.</returns>
        public static string BuildGenericLevelUpLine(string possessivePronoun, string skillName, int level)
        {
            // Guard against null/whitespace values so the sentence remains readable if a definition
            // omits optional metadata.
            string safePronoun = string.IsNullOrWhiteSpace(possessivePronoun)
                ? "their"
                : possessivePronoun.Trim();

            string safeSkillName = string.IsNullOrWhiteSpace(skillName)
                ? "skill"
                : skillName.Trim();

            int clampedLevel = Mathf.Max(1, level);

            return string.Format(
                CultureInfo.InvariantCulture,
                "Just levelled up {0} {1} to level {2}!",
                safePronoun,
                safeSkillName,
                clampedLevel);
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

            int index = UnityEngine.Random.Range(0, pool.Length);
            if (index < 0 || index >= pool.Length)
                return fallback;

            string message = pool[index];
            return string.IsNullOrWhiteSpace(message) ? fallback : message;
        }
    }
}
