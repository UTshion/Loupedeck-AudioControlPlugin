namespace WindowsInterop.CoreAudio;

internal class AudioEndpointVolumeCallback : IAudioEndpointVolumeCallback
{
    public event EventHandler<IntPtr> Notify;

    public AudioEndpointVolumeCallback()
    {
    }

    void IAudioEndpointVolumeCallback.OnNotify(IntPtr notifyData)
    {
        try
        {
            this.Notify?.Invoke(null, notifyData);
        }
        catch (Exception ex)
        {
            ComCallback.Report(ex);
        }
    }
}
