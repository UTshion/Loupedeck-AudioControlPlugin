namespace WindowsInterop.CoreAudio;

using System.Collections.Generic;
using System.Runtime.InteropServices;

using WindowsInterop.PropertySystem;

public class MMDevice : IAudioControlDevice, IDisposable
{
    public event EventHandler<string> PropertyChanged;
    public event EventHandler<Exception> SessionLoadError;

    private readonly IMMDevice deviceComObj;

    private AudioClient _audioClient = null;
    private AudioEndpointVolume _audioEndpointVolume = null;
    private AudioMeterInformation _audioMeterInformation = null;
    private AudioSessionManager _audioSessionManager = null;
    private DeviceTopology _deviceTopology = null;
    private PropertyStore _propertyStore = null;
    private readonly object _lazyLock = new object();
    private int _disposed = 0;

    // The plugin reads these from timer, UI and COM notification threads at the same time; unsynchronized
    // lazy creation could build two instances and leak the registered notifications of one of them.
    private T GetOrCreate<T>(ref T field, Func<T> create) where T : class
    {
        T value = Volatile.Read(ref field);
        if (value != null || this._disposed != 0)
        {
            return value;
        }
        lock (this._lazyLock)
        {
            if (field == null && this._disposed == 0)
            {
                try
                {
                    field = create();
                }
                catch
                {
                    field = null;
                }
            }
            return field;
        }
    }

    public AudioClient AudioClient => this.GetOrCreate(ref this._audioClient, () =>
    {
        Marshal.ThrowExceptionForHR(this.deviceComObj.Activate(typeof(IAudioClient).GUID, ClsCtx.ALL, IntPtr.Zero, out object audioClient));
        return new AudioClient(audioClient as IAudioClient);
    });

    public AudioEndpointVolume AudioEndpointVolume => this.GetOrCreate(ref this._audioEndpointVolume, () =>
    {
        Marshal.ThrowExceptionForHR(this.deviceComObj.Activate(typeof(IAudioEndpointVolume).GUID, ClsCtx.ALL, IntPtr.Zero, out object audioEndpointVolume));
        return new AudioEndpointVolume(this, audioEndpointVolume as IAudioEndpointVolume);
    });

    public AudioMeterInformation AudioMeterInformation => this.GetOrCreate(ref this._audioMeterInformation, () =>
    {
        Marshal.ThrowExceptionForHR(this.deviceComObj.Activate(typeof(IAudioMeterInformation).GUID, ClsCtx.ALL, IntPtr.Zero, out object audioMeterInformation));
        return new AudioMeterInformation(audioMeterInformation as IAudioMeterInformation);
    });

    public AudioSessionManager AudioSessionManager => this.GetOrCreate(ref this._audioSessionManager, () =>
    {
        Marshal.ThrowExceptionForHR(this.deviceComObj.Activate(typeof(IAudioSessionManager).GUID, ClsCtx.ALL, IntPtr.Zero, out object audioSessionManager));
        AudioSessionManager manager = new AudioSessionManager(audioSessionManager as IAudioSessionManager);
        manager.SessionLoadError += (s, ex) => this.SessionLoadError?.Invoke(s, ex);
        return manager;
    });

    public DeviceTopology DeviceTopology => this.GetOrCreate(ref this._deviceTopology, () =>
    {
        Marshal.ThrowExceptionForHR(this.deviceComObj.Activate(typeof(IDeviceTopology).GUID, ClsCtx.ALL, IntPtr.Zero, out object deviceTopology));
        return new DeviceTopology(deviceTopology as IDeviceTopology);
    });

    public PropertyStore PropertyStore => this.GetOrCreate(ref this._propertyStore, () =>
    {
        Marshal.ThrowExceptionForHR(this.deviceComObj.OpenPropertyStore(StorageAccessMode.Read, out IPropertyStore propstore));
        return new PropertyStore(propstore);
    });

    public string Id { get; } = null;
    public DataFlow DataFlow { get; }
    public DeviceState State { get; private set; }
    public string DeviceName { get; private set; } = null;
    public string DeviceDescription { get; private set; } = null;
    public string InterfaceName { get; private set; } = null;
    public string InstanceId { get; private set; } = null;
    public string IconPath { get; private set; } = null;

    public MMDevice(IMMDevice device)
    {
        this.deviceComObj = device;

        Marshal.ThrowExceptionForHR(this.deviceComObj.GetId(out string id));
        this.Id = id;

        IMMEndpoint mmEndpoint = this.deviceComObj as IMMEndpoint;
        mmEndpoint.GetDataFlow(out DataFlow dataFlow);
        this.DataFlow = dataFlow;

        this.GetState();

        this.LoadProperties(raiseEvent: false);
    }

    public DeviceState GetState()
    {
        Marshal.ThrowExceptionForHR(this.deviceComObj.GetState(out DeviceState state));
        this.State = state;
        return state;
    }

    public void LoadProperties(bool raiseEvent = true)
    {
        if (this.PropertyStore != null)
        {
            try
            {
                string deviceName = this.PropertyStore.GetValue<string>(PropertyKeys.PKEY_Device_FriendlyName);
                if (this.DeviceName != deviceName)
                {
                    this.DeviceName = deviceName;
                    if (raiseEvent)
                    {
                        this.PropertyChanged?.Invoke(this, "DeviceName");
                    }
                }
            }
            catch
            {
            }

            try
            {
                string deviceDescription = this.PropertyStore.GetValue<string>(PropertyKeys.PKEY_Device_DeviceDesc);
                if (this.DeviceDescription != deviceDescription)
                {
                    this.DeviceDescription = deviceDescription;
                    if (raiseEvent)
                    {
                        this.PropertyChanged?.Invoke(this, "DeviceDescription");
                    }
                }
            }
            catch
            {
            }

            try
            {
                string interfaceName = this.PropertyStore.GetValue<string>(PropertyKeys.PKEY_DeviceInterface_FriendlyName);
                if (this.InterfaceName != interfaceName)
                {
                    this.InterfaceName = interfaceName;
                    if (raiseEvent)
                    {
                        this.PropertyChanged?.Invoke(this, "InterfaceName");
                    }
                }
            }
            catch
            {
            }

            try
            {
                string instanceId = this.PropertyStore.GetValue<string>(PropertyKeys.PKEY_Device_InstanceId);
                if (this.InstanceId != instanceId)
                {
                    this.InstanceId = instanceId;
                    if (raiseEvent)
                    {
                        this.PropertyChanged?.Invoke(this, "InstanceId");
                    }
                }
            }
            catch
            {
            }

            try
            {
                string iconPath = this.PropertyStore.GetValue<string>(PropertyKeys.PKEY_Device_IconPath);
                if (this.IconPath != iconPath)
                {
                    this.IconPath = iconPath;
                    if (raiseEvent)
                    {
                        this.PropertyChanged?.Invoke(this, "IconPath");
                    }
                }
            }
            catch
            {
            }
        }
    }

    // A device can disappear or refuse activation at any time (unplugged, disabled, exclusive mode);
    // report neutral values instead of throwing into the refresh loop.
    string IAudioControl.DisplayName { get => this.DeviceDescription; }
    bool IAudioControl.Muted { get => this.AudioEndpointVolume?.Mute ?? false; set => this.RequireEndpointVolume().Mute = value; }
    float IAudioControl.VolumeScalar { get => this.AudioEndpointVolume?.MasterVolumeLevelScalar ?? 0.0f; set => this.RequireEndpointVolume().MasterVolumeLevelScalar = value; }
    float[] IAudioControl.PeakValues { get => this.AudioMeterInformation?.PeakValues.ToArray() ?? Array.Empty<float>(); }
    float IAudioControlDevice.Volume { get => this.AudioEndpointVolume?.MasterVolumeLevel ?? 0.0f; set => this.RequireEndpointVolume().MasterVolumeLevel = value; }
    float IAudioControlDevice.MinDecibels { get => this.AudioEndpointVolume?.VolumeRange.MinDecibels ?? 0.0f; }
    float IAudioControlDevice.MaxDecibels { get => this.AudioEndpointVolume?.VolumeRange.MaxDecibels ?? 0.0f; }
    float IAudioControlDevice.IncrementDecibels { get => this.AudioEndpointVolume?.VolumeRange.IncrementDecibels ?? 0.0f; }
    IEnumerable<IAudioControlSession> IAudioControlDevice.Sessions { get => (IEnumerable<IAudioControlSession>)this.AudioSessionManager?.Sessions ?? Array.Empty<IAudioControlSession>(); }

    private AudioEndpointVolume RequireEndpointVolume()
    {
        return this.AudioEndpointVolume ?? throw new InvalidOperationException($"Endpoint volume is not available for device '{this.DeviceName}'.");
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        if (Interlocked.Exchange(ref this._disposed, 1) == 1)
        {
            return;
        }
        // Dispose only what was created: the lazy properties would activate new COM objects here.
        // This also runs on the finalizer thread, where an exception would terminate the host process.
        lock (this._lazyLock)
        {
            TryDispose(this._audioEndpointVolume);
            TryDispose(this._audioMeterInformation);
            TryDispose(this._audioSessionManager);
            TryDispose(this._deviceTopology);
            TryDispose(this._propertyStore);
        }
        try
        {
            Marshal.ReleaseComObject(this.deviceComObj);
        }
        catch
        {
        }
    }

    private static void TryDispose(IDisposable disposable)
    {
        try
        {
            disposable?.Dispose();
        }
        catch
        {
        }
    }

    ~MMDevice()
    {
        this.Dispose();
    }
}
