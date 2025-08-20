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
