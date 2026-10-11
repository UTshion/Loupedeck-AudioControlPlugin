namespace WindowsInterop.CoreAudio;

internal class AudioSessionNotification : IAudioSessionNotification
{
    public event EventHandler<IAudioSessionControl> SessionCreated;

    public AudioSessionNotification()
    {
    }

    void IAudioSessionNotification.OnSessionCreated(IAudioSessionControl newSession)
    {
        // Only hand the COM object over; the wrapper is built outside the callback.
        ComCallback.Run(() => this.SessionCreated?.Invoke(null, newSession));
    }
}
