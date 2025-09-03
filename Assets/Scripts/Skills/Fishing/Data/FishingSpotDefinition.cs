using System.Collections.Generic;
using UnityEngine;
using Fishing;

namespace Skills.Fishing
{
    [CreateAssetMenu(menuName = "Skills/Fishing/Spot Definition")]
    public class FishingSpotDefinition : ScriptableObject
    {
        [Header("Identification")]
        [SerializeField] private string id;

        [Header("Fish")]
        [SerializeField] private List<FishDefinition> availableFish = new();

        [Header("Tools")]
        [SerializeField] private List<FishingToolDefinition> allowedTools = new();

        [Header("Bait")]
        [SerializeField] private string baitItemId;

        [Header("Water")]
        [SerializeField] private WaterType waterType;

        [Header("Depletion")]
        [SerializeField] private bool depletesAfterCatch = false;
        [SerializeField] private int depleteRollInverse = 8;

        [Header("Respawn")]
        [SerializeField] private int respawnSeconds = 5;

        [Header("Catch Timing")]
        [SerializeField] private int catchIntervalTicks = 4;

        [Header("Ranges")]
        [SerializeField] private float interactRange = 1.5f;
        [SerializeField] private float cancelDistance = 3f;

        public string Id => id;
        public List<FishDefinition> AvailableFish => availableFish;
        public List<FishingToolDefinition> AllowedTools => allowedTools;
        public string BaitItemId => baitItemId;
        public WaterType WaterType => waterType;
        public bool DepletesAfterCatch => depletesAfterCatch;
        public int DepleteRollInverse => depleteRollInverse;
        public int RespawnSeconds => respawnSeconds;
        public int CatchIntervalTicks => catchIntervalTicks;
        public float InteractRange => interactRange;
        public float CancelDistance => cancelDistance;
    }
}
