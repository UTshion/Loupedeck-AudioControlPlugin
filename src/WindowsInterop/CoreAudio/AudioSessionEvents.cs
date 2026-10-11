namespace WindowsInterop.CoreAudio;

internal class AudioSessionEvents : IAudioSessionEvents
{
    public event EventHandler<(string displayName, Guid eventContext)> DisplayNameChanged;
    public event EventHandler<(string iconPath, Guid eventContext)> IconPathChanged;
    public event EventHandler<(float volume, bool isMuted, Guid eventContext)> SimpleVolumeChanged;
    public event EventHandler<(int channelCount, IntPtr newVolumes, int channelIndex, Guid eventContext)> ChannelVolumeChanged;
    public event EventHandler<(Guid groupingId, Guid eventContext)> GroupingParamChanged;
    public event EventHandler<AudioSessionState> StateChanged;
    public event EventHandler<AudioSessionDisconnectReason> SessionDisconnected;

    public AudioSessionEvents()
    {
    }

    void IAudioSessionEvents.OnDisplayNameChanged(string displayName, ref Guid eventContext)
    {
        try
        {
            this.DisplayNameChanged?.Invoke(null, (displayName, eventContext));
        }
        catch (Exception ex)
        {
            ComCallback.Report(ex);
        }
    }

    void IAudioSessionEvents.OnIconPathChanged(string iconPath, ref Guid eventContext)
    {
        try
        {
            this.IconPathChanged?.Invoke(null, (iconPath, eventContext));
        }
        catch (Exception ex)
        {
            ComCallback.Report(ex);
        }
    }

    void IAudioSessionEvents.OnSimpleVolumeChanged(float volume, bool isMuted, ref Guid eventContext)
    {
        try
        {
            this.SimpleVolumeChanged?.Invoke(null, (volume, isMuted, eventContext));
        }
        catch (Exception ex)
        {
            ComCallback.Report(ex);
        }
    }

    void IAudioSessionEvents.OnChannelVolumeChanged(int channelCount, IntPtr newVolumes, int channelIndex, ref Guid eventContext)
    {
        try
        {
            this.ChannelVolumeChanged?.Invoke(null, (channelCount, newVolumes, channelIndex, eventContext));
        }
        catch (Exception ex)
        {
            ComCallback.Report(ex);
        }
    }

    void IAudioSessionEvents.OnGroupingParamChanged(ref Guid groupingId, ref Guid eventContext)
    {
        try
        {
            this.GroupingParamChanged?.Invoke(null, (groupingId, eventContext));
        }
        catch (Exception ex)
        {
            ComCallback.Report(ex);
        }
    }

    void IAudioSessionEvents.OnStateChanged(AudioSessionState state)
    {
        try
        {
            this.StateChanged?.Invoke(null, state);
        }
        catch (Exception ex)
        {
            ComCallback.Report(ex);
        }
    }

    void IAudioSessionEvents.OnSessionDisconnected(AudioSessionDisconnectReason disconnectReason)
    {
        try
        {
            this.SessionDisconnected?.Invoke(null, disconnectReason);
        }
        catch (Exception ex)
        {
            ComCallback.Report(ex);
        }
    }
}
