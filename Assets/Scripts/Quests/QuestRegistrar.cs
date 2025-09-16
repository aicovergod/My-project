using UnityEngine;
using World;

namespace Quests
{
    /// <summary>
    /// Registers quests defined in the inspector so they are always available regardless
    /// of which scene loads first.
    /// </summary>
    public class QuestRegistrar : ScenePersistentObject
    {
        [Tooltip("Quest definitions that should be registered at startup.")]
        public QuestDefinition[] quests;

        protected override void Awake()
        {
            base.Awake();
        }

        private void Start()
        {
            if (quests == null)
                return;

            foreach (var q in quests)
                QuestManager.Instance.RegisterQuest(q);
        }
    }
}
