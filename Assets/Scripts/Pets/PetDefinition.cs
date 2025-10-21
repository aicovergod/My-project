using UnityEngine;
using Inventory;
using Combat;

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

        [Tooltip("Possessive pronoun used for chat feedback (e.g. his, her, their). Defaults to neutral when empty.")]
        public string possessivePronoun = "their";

        [Header("Inventory Item")]
        [Tooltip("Item awarded when picking up the pet.")]
        public ItemData pickupItem;

        [Header("Companion Integration")]
        [Tooltip("If true, this pet definition should use the companion pipeline instead of the standard pet stack.")]
        public bool spawnAsCompanion;

        [Header("Storage")]
        [Tooltip("If true, this pet has its own inventory.")]
        public bool hasInventory;

        [Header("Visuals")]
        [Tooltip("Idle sprite if no animation clips are provided.")]
        public Sprite sprite;

        [Tooltip("Pixels per unit used to scale this pet's sprites.")]
        public float pixelsPerUnit = 64f;

        [System.Serializable]
        public struct EvolutionTier
        {
            public int level;
            public float pixelsPerUnit;
            public string tierName;
        }

        [Tooltip("Evolution tiers that adjust pixels per unit as the pet levels up.")]
        public EvolutionTier[] evolutionTiers;

        [Tooltip("Optional animation clips. If set, the pet will play these using an Animator.")]
        public AnimationClip[] animationClips;

        [Tooltip("Sprite used when attacking if no Animator is present.")]
        public Sprite attackSprite;

        [Tooltip("Frames for hit animation when facing down.")]
        public Sprite[] hitDown;

        [Tooltip("Frames for hit animation when facing down-right.")]
        public Sprite[] hitDownRight;

        [Tooltip("Frames for hit animation when facing right.")]
        public Sprite[] hitRight;

        [Tooltip("Frames for hit animation when facing up-right.")]
        public Sprite[] hitUpRight;

        [Tooltip("Frames for hit animation when facing up.")]
        public Sprite[] hitUp;

        [Tooltip("Frames for hit animation when facing up-left.")]
        public Sprite[] hitUpLeft;

        [Tooltip("Frames for hit animation when facing left.")]
        public Sprite[] hitLeft;

        [Tooltip("Frames for hit animation when facing down-left.")]
        public Sprite[] hitDownLeft;

        [Header("Frame-based Sprites")]
        [Tooltip("Frames for idle animation when facing down.")]
        public Sprite[] idleDown;

        [Tooltip("Frames for idle animation when facing down-right.")]
        public Sprite[] idleDownRight;

        [Tooltip("Frames for idle animation when facing right.")]
        public Sprite[] idleRight;

        [Tooltip("Frames for idle animation when facing up-right.")]
        public Sprite[] idleUpRight;

        [Tooltip("Frames for idle animation when facing up.")]
        public Sprite[] idleUp;

        [Tooltip("Frames for idle animation when facing up-left.")]
        public Sprite[] idleUpLeft;

        [Tooltip("Frames for idle animation when facing left.")]
        public Sprite[] idleLeft;

        [Tooltip("Frames for idle animation when facing down-left.")]
        public Sprite[] idleDownLeft;

        [Tooltip("Frames for walking animation when facing down.")]
        public Sprite[] walkDown;

        [Tooltip("Frames for walking animation when facing down-right.")]
        public Sprite[] walkDownRight;

        [Tooltip("Frames for walking animation when facing right.")]
        public Sprite[] walkRight;

        [Tooltip("Frames for walking animation when facing up-right.")]
        public Sprite[] walkUpRight;

        [Tooltip("Frames for walking animation when facing up.")]
        public Sprite[] walkUp;

        [Tooltip("Frames for walking animation when facing up-left.")]
        public Sprite[] walkUpLeft;

        [Tooltip("Frames for walking animation when facing left.")]
        public Sprite[] walkLeft;

        [Tooltip("Frames for walking animation when facing down-left.")]
        public Sprite[] walkDownLeft;

        [Header("Sprite Mirroring")]
        [Tooltip("If true, flip right-facing sprites to use for left-facing animations (idle/walk/hit).")]
        public bool useRightSpritesForLeft = true;

        [Tooltip("If true, flip left-facing sprites to use for right-facing animations (idle/walk/hit).")]
        public bool useLeftSpritesForRight = false;

        [Tooltip("If true, flip down-right sprites to use for down-left animations (idle/walk/hit).")]
        public bool useRightSpritesForDownLeft = true;

        [Tooltip("If true, flip down-left sprites to use for down-right animations (idle/walk/hit).")]
        public bool useLeftSpritesForDownRight = false;

        [Tooltip("If true, flip up-right sprites to use for up-left animations (idle/walk/hit).")]
        public bool useRightSpritesForUpLeft = true;

        [Tooltip("If true, flip up-left sprites to use for up-right animations (idle/walk/hit).")]
        public bool useLeftSpritesForUpRight = false;

        [Header("Frame Animation Settings")]
        [Tooltip("Base frames per second used for sprite animations when state overrides are not provided.")]
        public float baseAnimationFPS = 6f;

        [Tooltip("Frames per second to use for idle animations. Set to 0 or lower to fall back to Base Animation FPS.")]
        public float idleAnimationFPS = 0f;

        [Tooltip("Frames per second to use for walk animations. Set to 0 or lower to fall back to Base Animation FPS.")]
        public float walkAnimationFPS = 0f;

        [Tooltip("Frames per second to use for hit animations. Set to 0 or lower to fall back to Base Animation FPS.")]
        public float hitAnimationFPS = 0f;

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

        [Tooltip("Attack level multiplier applied per Beastmaster level (e.g. 0.05 for +5% per level).")]
        public float attackLevelPerBeastmasterLevel;

        [Tooltip("Strength level multiplier applied per Beastmaster level (e.g. 0.05 for +5% per level).")]
        public float strengthLevelPerBeastmasterLevel;

        [Tooltip("Max hit multiplier applied per Beastmaster level (e.g. 0.05 for +5% per level).")]
        public float maxHitPerBeastmasterLevel;

        [Header("UI")]
        [Tooltip("Optional color for drop announcement messages.")]
        public Color messageColor = Color.white;

#if UNITY_EDITOR
        [Header("Debugging")]
        [Tooltip("Beastmaster level used when testing max hit via context menu.")]
        public int debugBeastmasterLevel = 1;

        [ContextMenu("Test Max Hit")]
        private void TestMaxHit()
        {
            int max = GetMaxHit(debugBeastmasterLevel);
            Debug.Log($"{displayName} max hit at level {debugBeastmasterLevel}: {max}");
        }
#endif

        /// <summary>
        /// Calculate the maximum hit this pet can deal for a given Beastmaster level.
        /// </summary>
        /// <param name="beastmasterLevel">Owner's Beastmaster skill level.</param>
        public int GetMaxHit(int beastmasterLevel)
        {
            int strength = petStrengthLevel;
            if (strengthLevelPerBeastmasterLevel != 0f)
                strength = Mathf.RoundToInt(strength * (1f + strengthLevelPerBeastmasterLevel * beastmasterLevel));

            int effectiveStrength = CombatMath.GetEffectiveStrength(strength, CombatStyle.Accurate);
            int maxHit = CombatMath.GetMaxHit(effectiveStrength, damageBonus);

            if (maxHitPerBeastmasterLevel != 0f)
                maxHit = Mathf.RoundToInt(maxHit * (1f + maxHitPerBeastmasterLevel * beastmasterLevel));

            return maxHit;
        }
    }
}
