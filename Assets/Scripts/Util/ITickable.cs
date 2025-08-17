namespace Util
{
    /// <summary>
    /// Interface for objects that want to receive tick events from <see cref="Ticker"/>.
    /// </summary>
    public interface ITickable
    {
        /// <summary>
        /// Called every game tick by the <see cref="Ticker"/>.
        /// </summary>
        void OnTick();
    }
}

