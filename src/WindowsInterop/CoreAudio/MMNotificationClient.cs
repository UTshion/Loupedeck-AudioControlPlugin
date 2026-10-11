namespace WindowsInterop.CoreAudio;

using WindowsInterop.PropertySystem;

internal class MMNotificationClient : IMMNotificationClient
{
    public event EventHandler<DeviceStateEventArgs> DeviceStateChanged;
    public event EventHandler<DeviceIdEventArgs> DeviceAdded;
    public event EventHandler<DeviceIdEventArgs> DeviceRemoved;
    public event EventHandler<DefaultDeviceEventArgs> DefaultDeviceChanged;
    public event EventHandler<PropertyValueEventArgs> PropertyValueChanged;

    public MMNotificationClient()
    {
    }

    void IMMNotificationClient.OnDeviceStateChanged(string deviceId, DeviceState newState)
    {
        try
        {
            this.DeviceStateChanged?.Invoke(null, new DeviceStateEventArgs(deviceId, newState));
        }
        catch (Exception ex)
        {
            ComCallback.Report(ex);
        }
    }

    void IMMNotificationClient.OnDeviceAdded(string deviceId)
    {
        try
        {
            this.DeviceAdded?.Invoke(null, new DeviceIdEventArgs(deviceId));
        }
        catch (Exception ex)
        {
            ComCallback.Report(ex);
        }
    }

    void IMMNotificationClient.OnDeviceRemoved(string deviceId)
    {
        try
        {
            this.DeviceRemoved?.Invoke(null, new DeviceIdEventArgs(deviceId));
        }
        catch (Exception ex)
        {
            ComCallback.Report(ex);
        }
    }

    void IMMNotificationClient.OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
    {
        try
        {
            this.DefaultDeviceChanged?.Invoke(null, new DefaultDeviceEventArgs(flow, role, defaultDeviceId));
        }
        catch (Exception ex)
        {
            ComCallback.Report(ex);
        }
    }

    void IMMNotificationClient.OnPropertyValueChanged(string deviceId, PropertyKey key)
    {
        try
        {
            this.PropertyValueChanged?.Invoke(null, new PropertyValueEventArgs(deviceId, key));
        }
        catch (Exception ex)
        {
            ComCallback.Report(ex);
        }
    }
}
