namespace Core.Save
{
    /// <summary>
    /// Interface for objects that participate in the save system.
    /// </summary>
    public interface ISaveable
    {
        /// <summary>
        /// Load state from persistent storage.
        /// </summary>
        void Load();

        /// <summary>
        /// Persist current state to storage.
        /// </summary>
        void Save();
    }
}
