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

        [Header("Frame-based Sprites")]
        [Tooltip("Frames for idle animation when facing up.")]
        public Sprite[] idleUp;

        [Tooltip("Frames for walking animation when facing up.")]
        public Sprite[] walkUp;

        [Tooltip("Frames for idle animation when facing down.")]
        public Sprite[] idleDown;

        [Tooltip("Frames for walking animation when facing down.")]
        public Sprite[] walkDown;

        [Tooltip("Frames for idle animation when facing left.")]
        public Sprite[] idleLeft;

        [Tooltip("Frames for walking animation when facing left.")]
        public Sprite[] walkLeft;

        [Tooltip("Frames for idle animation when facing right.")]
        public Sprite[] idleRight;

        [Tooltip("Frames for walking animation when facing right.")]
        public Sprite[] walkRight;

        [Tooltip("If true, flip right-facing sprites to use for left-facing animations.")]
        public bool useRightSpritesForLeft = true;

        [Tooltip("If true, flip left-facing sprites to use for right-facing animations.")]
        public bool useLeftSpritesForRight = false;

        [Header("Combat")]
        [Tooltip("If true, this pet can participate in combat.")]
        public bool canFight;

        [Tooltip("Attack level used for combat calculations.")]
        public int petAttackLevel = 1;

        [Tooltip("Strength level used for combat calculations.")]
        public int petStrengthLevel = 1;

        [Tooltip("Attack speed in OSRS ticks.")]
        public int attackSpeedTicks = 4;

        [Tooltip("Additional attack accuracy bonus.")]
        public int accuracyBonus;

        [Tooltip("Additional strength/damage bonus.")]
        public int damageBonus;

        [Header("UI")]
        [Tooltip("Optional color for drop announcement messages.")]
        public Color messageColor = Color.white;
    }
}