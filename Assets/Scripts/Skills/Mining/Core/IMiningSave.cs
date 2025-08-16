using UnityEngine;

namespace Skills.Mining
{
    public interface IMiningSave
    {
        int LoadXp();
        void SaveXp(int xp);
    }

    public class PlayerPrefsMiningSave : IMiningSave
    {
        private const string Key = "mining_xp";

        public int LoadXp()
        {
            return PlayerPrefs.GetInt(Key, 0);
        }

        public void SaveXp(int xp)
        {
            PlayerPrefs.SetInt(Key, xp);
            PlayerPrefs.Save();
        }
    }
}
