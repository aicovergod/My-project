using UnityEngine;
using Core.Save;

namespace Skills.Mining
{
    public interface IMiningSave
    {
        int LoadXp();
        void SaveXp(int xp);
    }

    public class SaveManagerMiningSave : IMiningSave
    {
        private const string Key = "mining_xp";

        public int LoadXp()
        {
            return SaveManager.Load<int>(Key);
        }

        public void SaveXp(int xp)
        {
            SaveManager.Save(Key, xp);
        }
    }
}
