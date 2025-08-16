using UnityEngine;

namespace Skills.Mining
{
    [CreateAssetMenu(menuName = "Skills/Mining/XP Table")]
    public class XpTable : ScriptableObject
    {
        [SerializeField] private int[] levelXp = new int[99];

        public int GetLevel(int xp)
        {
            for (int i = levelXp.Length - 1; i >= 0; i--)
            {
                if (xp >= levelXp[i])
                    return i + 1;
            }
            return 1;
        }

        public int GetXpForLevel(int level)
        {
            level = Mathf.Clamp(level, 1, levelXp.Length);
            return levelXp[level - 1];
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (levelXp == null || levelXp.Length != 99)
                levelXp = GenerateXpTable();
        }

        public static int[] GenerateXpTable()
        {
            int[] xp = new int[99];
            int points = 0;
            for (int level = 1; level <= 99; level++)
            {
                if (level == 1)
                {
                    xp[0] = 0;
                    continue;
                }

                points += Mathf.FloorToInt(level + 300f * Mathf.Pow(2f, level / 7f));
                xp[level - 1] = points / 4;
            }
            return xp;
        }
#endif
    }
}
