namespace EyeCamXR
{
    /// <summary>An EEG acquisition backend feeding a SessionData store.</summary>
    public interface IEegSource
    {
        string[] ChannelNames { get; }
        string Info { get; }
        bool IsRunning { get; }

        /// <summary>Begin acquisition into the given store.</summary>
        void Start(SessionData store);

        /// <summary>Called every frame on the main thread (native drain etc.).</summary>
        void Tick();

        void Stop();
    }
}
