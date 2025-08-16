using UnityEngine;

namespace Skills.Mining
{
    [CreateAssetMenu(menuName = "Skills/Mining/Rock Definition")]
    public class RockDefinition : ScriptableObject
    {
        [Header("Identification")]
        [SerializeField] private string id;
        [SerializeField] private OreDefinition ore;

        [Header("Depletion")]
        [Range(0f,1f)]
        [SerializeField] private float depletionRoll = 0.33f;
        [SerializeField] private int depleteAfterNOres = 0;

        [Header("Respawn")]
        [SerializeField] private float respawnTimeSecondsMin = 3f;
        [SerializeField] private float respawnTimeSecondsMax = 6f;

        [Header("Requirements")]
        [SerializeField] private int requiresToolTier = 1;

        public string Id => id;
        public OreDefinition Ore => ore;
        public float DepletionRoll => depletionRoll;
        public int DepleteAfterNOres => depleteAfterNOres;
        public float RespawnTimeSecondsMin => respawnTimeSecondsMin;
        public float RespawnTimeSecondsMax => respawnTimeSecondsMax;
        public int RequiresToolTier => requiresToolTier;
    }
}
