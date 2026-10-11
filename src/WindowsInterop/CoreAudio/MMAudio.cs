namespace WindowsInterop.CoreAudio;

using System.Collections.Generic;
using System.Linq;

using WindowsInterop.PropertySystem;

public class MMAudio : IDisposable
{
    private readonly MMDeviceEnumerator _deviceEnumerator;

    public event EventHandler<DefaultDeviceEventArgs> DefaultDeviceChanged;
    public event EventHandler<DeviceStateEventArgs> DeviceStateChanged;
    public event EventHandler<string> DevicePropertyChanged;
    public event EventHandler<AudioSessionState> SessionStateChanged;
    public event EventHandler<Exception> SessionLoadError;
    public event EventHandler DevicesChanged;

    // Devices are added and removed from COM notification threads while the plugin reads them from
    // timer and UI threads, so every access goes through this lock and readers get a snapshot.
    private readonly object _devicesLock = new object();
    private readonly List<MMDevice> _devices = new List<MMDevice>();
    private readonly HashSet<string> _sessionWatchedDeviceIds = new HashSet<string>();

    public AudioPolicyConfig AudioPolicyConfig { get; }

    public MMDevice DefaultMultimediaCapture { get; private set; } = null;
    public MMDevice DefaultCommunicationsCapture { get; private set; } = null;
    public MMDevice DefaultMultimediaRender { get; private set; } = null;
    public MMDevice DefaultCommunicationsRender { get; private set; } = null;

    public IReadOnlyList<MMDevice> Devices
    {
        get
        {
            lock (this._devicesLock)
            {
                return this._devices.ToArray();
            }
        }
    }

    public IEnumerable<MMDevice> CaptureDevices
    {
        get
        {
            return this.Devices.Where(x => x.DataFlow == DataFlow.Capture);
        }
    }

    public IEnumerable<MMDevice> RenderDevices
    {
        get
        {
            return this.Devices.Where(x => x.DataFlow == DataFlow.Render);
        }
    }

    public IEnumerable<AudioSessionControl> RenderSessions
    {
        get
        {
            return this.RenderDevices
                .Where(x => x.State == DeviceState.Active)
                .SelectMany(x => (IEnumerable<AudioSessionControl>)x.AudioSessionManager?.Sessions ?? Array.Empty<AudioSessionControl>())
                .ToArray();
        }
    }

    public MMAudio()
    {
        this._deviceEnumerator = new MMDeviceEnumerator();
        this._deviceEnumerator.DefaultDeviceChanged += this.OnDefaultDeviceChanged;
        this._deviceEnumerator.DeviceAdded += this.OnDeviceAdded;
        this._deviceEnumerator.DeviceRemoved += this.OnDeviceRemoved;
        this._deviceEnumerator.DeviceStateChanged += this.OnDeviceStateChanged;
        this._deviceEnumerator.DevicePropertyChanged += this.OnDevicePropertyChanged;

        this.AudioPolicyConfig = new AudioPolicyConfig();

        foreach (MMDevice device in this._deviceEnumerator.EnumAudioEndpoints(DataFlow.All, DeviceState.All))
        {
            this.AddDevice(device, raiseEvent: false);
        }

        this.DefaultMultimediaCapture = this.FindDefaultDevice(DataFlow.Capture, Role.Multimedia);
        this.DefaultCommunicationsCapture = this.FindDefaultDevice(DataFlow.Capture, Role.Communications);
        this.DefaultMultimediaRender = this.FindDefaultDevice(DataFlow.Render, Role.Multimedia);
        this.DefaultCommunicationsRender = this.FindDefaultDevice(DataFlow.Render, Role.Communications);
    }

    // Use the instance from the device list so that a default device is not a second, separately
    // registered copy of the same endpoint.
    private MMDevice FindDefaultDevice(DataFlow dataFlow, Role role)
    {
        if (this._deviceEnumerator.GetDefaultAudioEndpointId(dataFlow, role, out string deviceId))
        {
            return this.FindDevice(deviceId);
        }
        return null;
    }

    private MMDevice FindDevice(string deviceId)
    {
        lock (this._devicesLock)
        {
            return this._devices.FirstOrDefault(x => x.Id == deviceId);
        }
    }

    private void AddDevice(MMDevice device, bool raiseEvent)
    {
        lock (this._devicesLock)
        {
            if (this._devices.Any(x => x.Id == device.Id))
            {
                device.Dispose();
                return;
            }
            this._devices.Add(device);
        }
        device.PropertyChanged += (sender, e) => this.DevicePropertyChanged?.Invoke(this, e);
        device.SessionLoadError += (s, ex) => this.SessionLoadError?.Invoke(s, ex);
        this.WatchSessions(device);
        if (raiseEvent)
        {
            this.DevicesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    // Subscribes to session notifications of an active render device. Called again whenever a device
    // becomes active, so that a device enabled or plugged in after start-up also reports its sessions.
    private void WatchSessions(MMDevice device)
    {
        if (device.DataFlow != DataFlow.Render || device.State != DeviceState.Active)
        {
            return;
        }
        lock (this._devicesLock)
        {
            if (!this._sessionWatchedDeviceIds.Add(device.Id))
            {
                return;
            }
        }
        if (device.AudioSessionManager is AudioSessionManager manager)
        {
            manager.SessionCreated += this.OnDeviceSessionCreated;
            manager.StateChanged += this.OnSessionStateChanged;
        }
        else
        {
            lock (this._devicesLock)
            {
                this._sessionWatchedDeviceIds.Remove(device.Id);
            }
        }
    }

    public void SetDefaultAudioEndpoint(string deviceId, Role role)
    {
        if (!string.IsNullOrEmpty(deviceId))
        {
            this._deviceEnumerator.SetDefaultAudioEndpoint(deviceId, role);
        }
    }

    private void OnDefaultDeviceChanged(object sender, DefaultDeviceEventArgs e)
    {
        if (this.FindDevice(e.DefaultDeviceId) is MMDevice device)
        {
            if (e.Flow == DataFlow.Capture)
            {
                if (e.Role == Role.Multimedia)
                {
                    this.DefaultMultimediaCapture = device;
                }
                else if (e.Role == Role.Communications)
                {
                    this.DefaultCommunicationsCapture = device;
                }
            }
            else if (e.Flow == DataFlow.Render)
            {
                if (e.Role == Role.Multimedia)
                {
                    this.DefaultMultimediaRender = device;
                }
                else if (e.Role == Role.Communications)
                {
                    this.DefaultCommunicationsRender = device;
                }
            }
        }
        ComCallback.Defer(() => this.DefaultDeviceChanged?.Invoke(this, e));
    }

    private void OnDeviceAdded(object sender, DeviceIdEventArgs e)
    {
        // Activating the new device and registering its notifications must not happen inside the callback.
        ComCallback.Defer(() =>
        {
            if (this.FindDevice(e.DeviceId) is null)
            {
                this.AddDevice(this._deviceEnumerator.GetDevice(e.DeviceId), raiseEvent: true);
            }
        });
    }

    private void OnDeviceRemoved(object sender, DeviceIdEventArgs e)
    {
        ComCallback.Defer(() =>
        {
            MMDevice device;
            lock (this._devicesLock)
            {
                device = this._devices.FirstOrDefault(x => x.Id == e.DeviceId);
                if (device is null)
                {
                    return;
                }
                this._devices.Remove(device);
                this._sessionWatchedDeviceIds.Remove(device.Id);
            }
            this.DevicesChanged?.Invoke(this, EventArgs.Empty);
            device.Dispose();
        });
    }

    private void OnDeviceStateChanged(object sender, DeviceStateEventArgs e)
    {
        ComCallback.Defer(() =>
        {
            if (this.FindDevice(e.DeviceId) is MMDevice device)
            {
                device.GetState();
                this.WatchSessions(device);
            }
            this.DeviceStateChanged?.Invoke(this, e);
        });
    }

    private void OnDevicePropertyChanged(object sender, PropertyValueEventArgs e)
    {
        ComCallback.Defer(() =>
        {
            if (this.FindDevice(e.DeviceId) is MMDevice device)
            {
                device.LoadProperties();
            }
        });
    }

    private void OnDeviceSessionCreated(object sender, AudioSessionControl e)
    {
        this.SessionStateChanged?.Invoke(e, e.State);
    }

    private void OnSessionStateChanged(object sender, AudioSessionState e)
    {
        this.SessionStateChanged?.Invoke(sender, e);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);

        MMDevice[] devices;
        lock (this._devicesLock)
        {
            devices = this._devices.ToArray();
            this._devices.Clear();
            this._sessionWatchedDeviceIds.Clear();
        }
        foreach (MMDevice device in devices)
        {
            device.Dispose();
        }

        try
        {
            this.AudioPolicyConfig.Dispose();
        }
        catch
        {
        }

        this._deviceEnumerator.Dispose();
    }

    ~MMAudio()
    {
        this.Dispose();
    }
}
