using UnityEngine;
using Combat;

namespace Pets
{
    /// <summary>
    /// Handle to a pet instance used by the merging system.
    /// </summary>
    public sealed class PetHandle : MonoBehaviour { }

    /// <summary>
    /// Visual data required to mimic a pet's appearance.
    /// </summary>
    public sealed class PetVisualProfile
    {
        public RuntimeAnimatorController controller;
        public Sprite baseSprite;
        public Sprite idleDown;
        public Sprite idleDownRight;
        public Sprite idleLeft;
        public Sprite idleRight;
        public Sprite idleUp;
        public Sprite idleUpRight;
        public Sprite idleUpLeft;
        public Sprite idleDownLeft;
        public Sprite walkDown;
        public Sprite walkDownRight;
        public Sprite walkLeft;
        public Sprite walkRight;
        public Sprite walkUp;
        public Sprite walkUpRight;
        public Sprite walkUpLeft;
        public Sprite walkDownLeft;
        public Sprite[] hitDown;
        public Sprite[] hitDownRight;
        public Sprite[] hitLeft;
        public Sprite[] hitRight;
        public Sprite[] hitUp;
        public Sprite[] hitUpRight;
        public Sprite[] hitUpLeft;
        public Sprite[] hitDownLeft;
        public bool useFlipXForLeft;
        public bool useFlipXForRight;
        public bool useFlipXForDownLeft;
        public bool useFlipXForUpLeft;
        public bool useFlipXForUpRight;
        public bool useFlipXForDownRight;
        public Vector3 localScale = Vector3.one;
    }

    /// <summary>
    /// Abstraction over the pet system used by pet merging.
    /// </summary>
    public interface IPetService
    {
        bool TryGetActiveCombatPet(out PetHandle pet);
        void HideActivePet();
        void ShowActivePet(Vector3 worldPosition);
        PetVisualProfile GetVisuals(PetHandle pet);
        ICombatProfile GetCombatProfile(PetHandle pet);
    }
}
