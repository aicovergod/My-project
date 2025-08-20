namespace Beastmaster
{
    /// <summary>
    /// Abstraction over the Beastmaster skill so other systems can query or set the level.
    /// </summary>
    public interface IBeastmasterService
    {
        int CurrentLevel { get; }
        void SetLevel(int level);
    }
}
