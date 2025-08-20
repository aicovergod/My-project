using UnityEngine;

namespace Quests
{
    public class QuestRegistrar : MonoBehaviour {
        public QuestDefinition[] quests;
        void Start() {
            foreach (var q in quests)
                QuestManager.Instance.RegisterQuest(q);
        }
    }
}
