using UnityEngine;

namespace Skills.Fishing
{
    [CreateAssetMenu(menuName = "Skills/Fishing/Fish Definition")]
    public class FishDefinition : ScriptableObject
    {
        [Header("Identification")]
        [SerializeField] private string id;
        [SerializeField] private string displayName;

        [Header("Requirements")]
        [SerializeField] private int requiredLevel = 1;

        [Header("Rewards")]
        [SerializeField] private int xp = 10;
        [SerializeField] private string itemId;

        public string Id => id;
        public string DisplayName => displayName;
        public int RequiredLevel => requiredLevel;
        public int Xp => xp;
        public string ItemId => itemId;
    }
}
