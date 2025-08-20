using UnityEngine;
using Skills;

namespace Beastmaster
{
    /// <summary>
    /// Simple adapter that exposes the Beastmaster level from <see cref="SkillManager"/>.
    /// </summary>
    public class BeastmasterServiceAdapter : MonoBehaviour, IBeastmasterService
    {
        [SerializeField] private SkillManager skills;

        private void Awake()
        {
            if (skills == null)
                skills = GetComponent<SkillManager>();
        }

        public int CurrentLevel => skills != null ? skills.GetLevel(SkillType.Beastmaster) : 1;

        public void SetLevel(int level)
        {
            if (skills != null)
                skills.DebugSetLevel(SkillType.Beastmaster, level);
        }
    }
}
