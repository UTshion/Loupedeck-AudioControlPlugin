namespace WindowsInterop.CoreAudio;

using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

public class AudioSessionManager : IDisposable
{
    public event EventHandler<AudioSessionControl> SessionCreated;
    public event EventHandler<AudioSessionControl> SessionDisconnected;
    public event EventHandler<AudioSessionState> StateChanged;
    public event EventHandler<Exception> SessionLoadError;

    private readonly IAudioSessionManager2 audioSessionManagerComObject;
    private readonly AudioSessionNotification audioSessionNotification;

    // Sessions are added and removed from COM notification threads while the plugin enumerates them
    // from timer threads, so every access goes through this lock and readers get a snapshot.
    private readonly object sessionsLock = new object();
    private readonly List<AudioSessionControl> sessions = new List<AudioSessionControl>();
    private bool disposed = false;

    public IReadOnlyList<AudioSessionControl> Sessions
    {
        get
        {
            lock (this.sessionsLock)
            {
                return this.sessions.ToArray();
            }
        }
    }

    internal AudioSessionManager(IAudioSessionManager audioSessionManagerComObject)
    {
        this.audioSessionManagerComObject = audioSessionManagerComObject as IAudioSessionManager2;
        try
        {
            Marshal.ThrowExceptionForHR(this.audioSessionManagerComObject.GetSessionEnumerator(out IAudioSessionEnumerator sessionEnumeratorComObject));
            AudioSessionCollection sessionCollection = new AudioSessionCollection(sessionEnumeratorComObject);
            sessionCollection.SessionLoadError += this.OnSessionLoadError;
            foreach (AudioSessionControl session in sessionCollection)
            {
                this.Attach(session);
                this.sessions.Add(session);
            }
        }
        catch (Exception ex)
        {
            this.OnSessionLoadError(this, ex);
        }
        this.audioSessionNotification = new AudioSessionNotification();
        this.audioSessionNotification.SessionCreated += this.OnSessionCreated;
        Marshal.ThrowExceptionForHR(this.audioSessionManagerComObject.RegisterSessionNotification(this.audioSessionNotification));
    }

    private void Attach(AudioSessionControl session)
    {
        session.StateChanged += this.OnStateChanged;
        session.SessionDisconnected += this.OnSessionDisconnected;
    }

    private void Detach(AudioSessionControl session)
    {
        session.StateChanged -= this.OnStateChanged;
        session.SessionDisconnected -= this.OnSessionDisconnected;
    }

    private void OnSessionCreated(object sender, IAudioSessionControl newSessionComObject)
    {
        // Building the wrapper registers a session notification, which must not happen inside this callback.
        ComCallback.Defer(() =>
        {
            AudioSessionControl newSession;
            try
            {
                newSession = new AudioSessionControl(newSessionComObject);
            }
            catch (Exception ex)
            {
                this.OnSessionLoadError(this, ex);
                return;
            }

            bool added = false;
            lock (this.sessionsLock)
            {
                if (!this.disposed && !this.sessions.Any(s => s.SessionInstanceIdentifier == newSession.SessionInstanceIdentifier && s.State != AudioSessionState.Expired))
                {
                    this.Attach(newSession);
                    this.sessions.Add(newSession);
                    added = true;
                }
            }
            if (added)
            {
                this.SessionCreated?.Invoke(this, newSession);
            }
            else
            {
                newSession.Dispose();
            }
        });
    }

    private void OnStateChanged(object sender, AudioSessionState state)
    {
        this.StateChanged?.Invoke(sender, state);
        if (state == AudioSessionState.Expired && sender is AudioSessionControl expiredSession && !expiredSession.IsSystemSoundsSession)
        {
            // An expired session never comes back: the app gets a new session if it plays again.
            this.RemoveSession(expiredSession);
        }
    }

    private void OnSessionDisconnected(object sender, AudioSessionDisconnectReason disconnectReason)
    {
        if (sender is AudioSessionControl disconnectedSession)
        {
            this.SessionDisconnected?.Invoke(this, disconnectedSession);
            this.RemoveSession(disconnectedSession);
        }
    }

    private void RemoveSession(AudioSessionControl session)
    {
        bool removed;
        lock (this.sessionsLock)
        {
            removed = this.sessions.Remove(session);
        }
        if (removed)
        {
            this.Detach(session);
            // Unregistering a session notification inside its own callback can deadlock.
            ComCallback.Defer(session.Dispose);
        }
    }

    private void OnSessionLoadError(object sender, Exception ex)
    {
        this.SessionLoadError?.Invoke(sender, ex);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        AudioSessionControl[] toDispose;
        lock (this.sessionsLock)
        {
            if (this.disposed)
            {
                return;
            }
            this.disposed = true;
            toDispose = this.sessions.ToArray();
            this.sessions.Clear();
        }
        foreach (AudioSessionControl session in toDispose)
        {
            this.Detach(session);
            session.Dispose();
        }
        try
        {
            if (this.audioSessionNotification != null)
            {
                Marshal.ThrowExceptionForHR(this.audioSessionManagerComObject.UnregisterSessionNotification(this.audioSessionNotification));
            }
        }
        catch
        {
        }
    }

    ~AudioSessionManager()
    {
        this.Dispose();
    }
}
