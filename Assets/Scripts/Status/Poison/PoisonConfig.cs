using UnityEngine;

namespace Status.Poison
{
    /// <summary>
    /// Configuration data for a poison effect.
    /// </summary>
    [CreateAssetMenu(menuName = "Status/Poison Config")]
    public class PoisonConfig : ScriptableObject
    {
        [Tooltip("Unique identifier for saving and comparisons.")]
        public string Id;

        [Tooltip("Damage dealt every tick when poison is first applied.")]
        public int startDamagePerTick;

        [Tooltip("Seconds between poison damage ticks.")]
        public float tickIntervalSeconds = 15f;

        [Tooltip("Number of poison hits before severity decays.")]
        public int hitsPerDecayStep = 4;

        [Tooltip("How much damage is reduced at each decay step.")]
        public int decayAmountPerStep = 1;

        [Tooltip("Minimum damage per tick before poison ends.")]
        public int minDamagePerTick = 0;
    }
}
