using UnityEngine;

namespace Skills.Woodcutting
{
    [CreateAssetMenu(menuName = "Skills/Woodcutting/Tree Definition")]
    public class TreeDefinition : ScriptableObject
    {
        [Header("Identification")]
        [SerializeField] private string id;
        [SerializeField] private string displayName;

        [Header("Requirements")]
        [SerializeField] private int requiredWoodcuttingLevel = 1;

        [Header("Rewards")]
        [SerializeField] private int xpPerLog = 25;
        [SerializeField] private string logItemId;

        [Header("Depletion")]
        [SerializeField] private bool depletesAfterOneLog = false;
        [SerializeField] private int depleteRollInverse = 8;

        [Header("Respawn")]
        [SerializeField] private int respawnSeconds = 5;

        [Header("Chop Timing")]
        [SerializeField] private int chopIntervalTicks = 4;

        [Header("Pet Drop Chance")]
        [SerializeField] private int petDropChance = 0;

        [Header("Ranges")]
        [SerializeField] private float interactRange = 1.5f;
        [SerializeField] private float cancelDistance = 3f;

        [Header("Visuals")]
        [SerializeField] private Sprite aliveSprite;
        [SerializeField] private Sprite depletedSprite;

        public string Id => id;
        public string DisplayName => displayName;
        public int RequiredWoodcuttingLevel => requiredWoodcuttingLevel;
        public int XpPerLog => xpPerLog;
        public string LogItemId => logItemId;
        public bool DepletesAfterOneLog => depletesAfterOneLog;
        public int DepleteRollInverse => depleteRollInverse;
        public int RespawnSeconds => respawnSeconds;
        public int ChopIntervalTicks => chopIntervalTicks;
        public int PetDropChance => petDropChance;
        public float InteractRange => interactRange;
        public float CancelDistance => cancelDistance;
        public Sprite AliveSprite => aliveSprite;
        public Sprite DepletedSprite => depletedSprite;
    }
}
