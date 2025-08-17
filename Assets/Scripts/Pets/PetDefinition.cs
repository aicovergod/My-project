using UnityEngine;
using Inventory;

namespace Pets
{
    /// <summary>
    /// Defines data for a cosmetic pet.
    /// </summary>
    [CreateAssetMenu(menuName = "Pets/Pet Definition")]
    public class PetDefinition : ScriptableObject
    {
        [Header("Identification")]
        [Tooltip("Unique identifier for this pet.")]
        public string id;

        [Header("Display")]
        [Tooltip("Display name for UI messages.")]
        public string displayName;

        [Header("Inventory Item")]
        [Tooltip("Item awarded when picking up the pet.")]
        public ItemData pickupItem;

        [Header("Visuals")]
        [Tooltip("Idle sprite if no animation clips are provided.")]
        public Sprite sprite;

        [Tooltip("Optional animation clips. If set, the pet will play these using an Animator.")]
        public AnimationClip[] animationClips;

        [Header("UI")]
        [Tooltip("Optional color for drop announcement messages.")]
        public Color messageColor = Color.white;
    }
}