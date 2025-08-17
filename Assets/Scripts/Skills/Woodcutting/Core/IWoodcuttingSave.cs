using UnityEngine;
using Core.Save;

namespace Skills.Woodcutting
{
    public interface IWoodcuttingSave
    {
        int LoadXp();
        void SaveXp(int xp);
    }

    public class SaveManagerWoodcuttingSave : IWoodcuttingSave
    {
        private const string Key = "woodcutting_xp";

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
