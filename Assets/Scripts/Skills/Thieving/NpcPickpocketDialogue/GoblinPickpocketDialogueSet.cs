using System.Collections.Generic;
using UnityEngine;

namespace Skills.Thieving.NpcPickpocketDialogue
{
    /// <summary>
    ///     Dialogue collection for goblin pickpocket attempts. Success lines have a 1-in-20
    ///     chance to play while failure lines trigger every time an attempt is caught.
    /// </summary>
    internal sealed class GoblinPickpocketDialogueSet : NpcPickpocketDialogueSet
    {
        private static readonly string[] SuccessLines =
        {
            "\"Eh? Thought I had more coins than this…\"",
            "\"Huh… must’ve dropped it in me soup again.\"",
            "\"Blimey, these pockets got holes or what?\"",
            "\"Can’t trust these new belts, always loosening…\"",
            "\"Oi, where’s me sarnie gone?!\"",
            "\"That’s odd… me purse feels lighter.\"",
            "\"I swear me mum cursed these pockets.\"",
            "\"Someone nicked me lunch again, I just know it.\"",
            "\"Maybe I spent it all… on what though?\"",
            "\"I really gotta stop keepin’ me coins next to me arse.\"",
        };

        private static readonly string[] FailureLines =
        {
            "\"Oi! You tryna nick me dosh, ya muppet?!\"",
            "\"’Ave a slap, ya sneaky sod!\"",
            "\"Oi oi! Me pockets ain’t your buffet!\"",
            "\"Hands off, ya grubby fingered git!\"",
            "\"I’ll cave yer skull in fer that, I will!\"",
            "\"You what?! That’s me coin purse!\"",
            "\"Cor blimey, thought you could rob a goblin, did ya?\"",
            "\"Keep them mitts to yerself, sunshine!\"",
            "\"Sneaky little toerag! I felt that!\"",
            "\"You best leg it, ‘fore I bash yer kneecaps!\"",
        };

        /// <inheritdoc />
        public override string NpcId => "Goblin";

        /// <inheritdoc />
        protected override IReadOnlyList<string> SuccessDialogueLines => SuccessLines;

        /// <inheritdoc />
        protected override IReadOnlyList<string> FailureDialogueLines => FailureLines;

        /// <summary>
        ///     Registers the goblin dialogue set during startup so it is ready before any
        ///     pickpocket attempts occur.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            RegisterSet(new GoblinPickpocketDialogueSet());
        }
    }
}
