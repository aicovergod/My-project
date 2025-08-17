using Core.Save;

namespace Pets
{
    /// <summary>
    /// Bridges pet persistence with the project's SaveManager.
    /// </summary>
    public static class PetSaveBridge
    {
        private const string Key = "activePetId";

        public static void Save(string id)
        {
            SaveManager.Save(Key, id);
        }

        public static string Load()
        {
            return SaveManager.Load<string>(Key);
        }

        public static void Clear()
        {
            SaveManager.Delete(Key);
        }
    }
}