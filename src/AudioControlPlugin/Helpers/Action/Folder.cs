namespace Loupedeck.AudioControlPlugin;

using System.Collections.Generic;
using System.Linq;

internal abstract class Folder : PluginDynamicFolder, IPluginDynamicFolder
{
    private const int BACK_DEBOUNCE_MS = 500;

    private FolderPage? _homePage = null;

    // Guards the page stack; held only briefly and never while calling into the Loupedeck service.
    private readonly object _stackLock = new object();
    private readonly List<FolderPage> _pageStack = new List<FolderPage>();

    // Serializes whole navigation operations (leave, push/pop, enter), which run on service threads.
    private readonly object _navigationLock = new object();
    private FolderPage? _enteredPage = null;
    private long _lastBackTimestamp = 0;
    private volatile bool _closing = false;
    private long _closingTimestamp = 0;

    public List<string> ButtonActionNames { get; }

    public FolderPage? HomePage
    {
        get
        {
            return this._homePage;
        }
        set
        {
            lock (this._stackLock)
            {
                this._homePage = value;
                this._pageStack.Clear();
                if (this._homePage is not null)
                {
                    this._pageStack.Add(this._homePage);
                }
            }
        }
    }

    public FolderPage? CurrentPage
    {
        get
        {
            lock (this._stackLock)
            {
                return this._pageStack.Count > 0 ? this._pageStack[^1] : null;
            }
        }
    }

    public int PageDepth
    {
        get
        {
            lock (this._stackLock)
            {
                return this._pageStack.Count;
            }
        }
    }

    protected Folder(string displayName, string? description = null, string? groupName = null, DeviceType supportedDevices = DeviceType.All) : base()
    {
        base.DisplayName = displayName;
        base.Description = description;
        base.GroupName = groupName;
        base.SupportedDevices = supportedDevices;

        this.ButtonActionNames = new List<string>();
    }

    // Runs a navigation step serialized against the others. Calls back into the Loupedeck service
    // (Close, *ActionNamesChanged) are queued in 'after' and made only once the lock is released:
    // Close() waits for the service to run Deactivate on another thread, and Deactivate needs this lock.
    private void Navigate(string operation, Action<List<Action>> action)
    {
        List<Action> after = new List<Action>();
        if (!Monitor.TryEnter(this._navigationLock, TimeSpan.FromSeconds(5)))
        {
            PluginLog.Warning($"Folder navigation '{operation}' skipped: another navigation is still running");
            return;
        }
        try
        {
            action(after);
        }
        catch (Exception ex)
        {
            PluginLog.Error(ex, $"Folder navigation '{operation}' failed: {ex.Message}");
        }
        finally
        {
            Monitor.Exit(this._navigationLock);
        }
        foreach (Action notify in after)
        {
            try
            {
                notify();
            }
            catch (Exception ex)
            {
                PluginLog.ThrottledWarning($"Folder.{operation} (notify)", ex);
            }
        }
    }

    // Enter/Leave subscribe and unsubscribe timers and audio events, so they must stay strictly paired.
    private void EnterCurrentPage()
    {
        FolderPage? page = this.CurrentPage;
        if (page is not null && !ReferenceEquals(this._enteredPage, page))
        {
            this.LeaveEnteredPage();
            this._enteredPage = page;
            page.Enter();
        }
    }

    private void LeaveEnteredPage()
    {
        FolderPage? page = this._enteredPage;
        this._enteredPage = null;
        page?.Leave();
    }

    private void NotifyActionNamesChanged(List<Action> after)
    {
        after.Add(this.ButtonActionNamesChanged);
        after.Add(this.EncoderActionNamesChanged);
    }

    public void NavigateTo(FolderPage nextPage)
    {
        this.Navigate(nameof(NavigateTo), after =>
        {
            if (nextPage == null || this.CurrentPage == null)
            {
                return;
            }
            this.LeaveEnteredPage();
            lock (this._stackLock)
            {
                this._pageStack.Add(nextPage);
            }
            nextPage.Load();
            this.EnterCurrentPage();
            this.NotifyActionNamesChanged(after);
        });
    }

    public void LeavePage()
    {
        this.Navigate(nameof(LeavePage), after =>
        {
            FolderPage? page;
            lock (this._stackLock)
            {
                if (this._pageStack.Count <= 1)
                {
                    page = null;
                }
                else
                {
                    page = this._pageStack[^1];
                    this._pageStack.RemoveAt(this._pageStack.Count - 1);
                }
            }
            if (page is null)
            {
                // Exit from the home page. The home page always stays on the stack, so the folder can
                // never be reopened without a page (that state showed as an all-black screen).
                // Until the folder is activated again, any further Back/Exit is a late duplicate.
                this._closing = true;
                Interlocked.Exchange(ref this._closingTimestamp, Environment.TickCount64);
                after.Add(base.Close);
                return;
            }
            this.LeaveEnteredPage();
            page.Unload();
            this.EnterCurrentPage();
            this.NotifyActionNamesChanged(after);
        });
    }

    private void OnBackRequested()
    {
        // A tap on Back/Exit reaches the folder twice (touch event and command), and the second one can
        // arrive late; the time window catches Back, the closing flag catches a late Exit.
        long now = Environment.TickCount64;
        // The closing flag expires in case the service never deactivates the folder after Close().
        bool closing = this._closing && now - Interlocked.Read(ref this._closingTimestamp) < 3000;
        if (closing || now - Interlocked.Read(ref this._lastBackTimestamp) < BACK_DEBOUNCE_MS)
        {
            return;
        }
        Interlocked.Exchange(ref this._lastBackTimestamp, now);
        this.LeavePage();
    }

    public void ParseActionParameter(string actionParameter, out string pageName, out string pageActionParameter)
    {
        int index = actionParameter.IndexOf('+');
        if (index < 0)
        {
            pageName = string.Empty;
            pageActionParameter = actionParameter;
            return;
        }
        pageName = actionParameter.Substring(0, index);
        pageActionParameter = actionParameter.Substring(index + 1);
    }

    public FolderPage? GetPage(string pageName)
    {
        lock (this._stackLock)
        {
            for (int i = this._pageStack.Count - 1; i >= 0; i--)
            {
                if (this._pageStack[i].Name == pageName)
                {
                    return this._pageStack[i];
                }
            }
            return null;
        }
    }

    public override bool Activate()
    {
        this.Navigate(nameof(Activate), after =>
        {
            this._closing = false;
            lock (this._stackLock)
            {
                if (this._pageStack.Count == 0 && this._homePage is not null)
                {
                    this._pageStack.Add(this._homePage);
                }
            }
            this.CurrentPage?.Load();
            this.EnterCurrentPage();
            // Names may have changed while the page was not listening.
            this.NotifyActionNamesChanged(after);
        });
        return base.Activate();
    }

    public override bool Deactivate()
    {
        this.Navigate(nameof(Deactivate), after =>
        {
            this.LeaveEnteredPage();
            List<FolderPage> pages;
            lock (this._stackLock)
            {
                pages = this._pageStack.ToList();
                this._pageStack.Clear();
                if (this._homePage is not null)
                {
                    this._pageStack.Add(this._homePage);
                }
            }
            for (int i = pages.Count - 1; i >= 0; i--)
            {
                pages[i].Unload();
            }
        });
        return base.Deactivate();
    }

    public override BitmapImage GetButtonImage(PluginImageSize imageSize)
    {
        return PluginImage.DrawFolderTextImage(false, base.DisplayName, imageSize);
    }

    public override PluginDynamicFolderNavigation GetNavigationArea(DeviceType _)
    {
        return PluginDynamicFolderNavigation.None;
    }

    public override IEnumerable<string> GetButtonPressActionNames(DeviceType deviceType)
    {
        FolderPage? page = this.CurrentPage;
        lock (this.ButtonActionNames)
        {
            this.ButtonActionNames.Clear();
            if (page != null)
            {
                this.ButtonActionNames.Add($"{page.Name}+@back");
                this.ButtonActionNames.AddRange(page.GetButtonPressActionNames(deviceType).Select(s => $"{page.Name}+{s}"));
            }
            // Materialize while locked: a lazy query would be enumerated by the service after the lock
            // is released, while another thread may already be refilling the list.
            return this.ButtonActionNames.Select(s => base.CreateCommandName(s)).ToArray();
        }
    }

    public override IEnumerable<string> GetEncoderRotateActionNames(DeviceType deviceType)
    {
        FolderPage? page = this.CurrentPage;
        if (page == null)
        {
            return Array.Empty<string>();
        }
        return page.GetEncoderRotateActionNames(deviceType).Select(s => base.CreateAdjustmentName($"{page.Name}+{s}")).ToArray();
    }

    public override IEnumerable<string> GetEncoderPressActionNames(DeviceType deviceType)
    {
        FolderPage? page = this.CurrentPage;
        if (page == null)
        {
            return Array.Empty<string>();
        }
        return page.GetEncoderPressActionNames(deviceType).Select(s => base.CreateCommandName($"{page.Name}+{s}")).ToArray();
    }

    public override BitmapImage GetCommandImage(string actionParameter, PluginImageSize imageSize)
    {
        this.ParseActionParameter(actionParameter, out string pageName, out string pageActionParameter);
        if (this.GetPage(pageName) is FolderPage page && page == this.CurrentPage)
        {
            if (pageActionParameter == "@back")
            {
                if (this.PageDepth > 1)
                {
                    pageActionParameter = "Back";
                }
                else
                {
                    pageActionParameter = "Exit";
                }
                return PluginImage.DrawTextImage(pageActionParameter, false, imageSize);
            }
            else
            {
                try
                {
                    return page.GetCommandImage(pageActionParameter, imageSize);
                }
                catch (Exception ex)
                {
                    PluginLog.ThrottledWarning($"{page.Name}.GetCommandImage({pageActionParameter})", ex);
                    return PluginImage.DrawBlackImage(imageSize);
                }
            }
        }
        return PluginImage.DrawBlackImage(imageSize);
    }

    public override BitmapImage GetAdjustmentImage(string actionParameter, PluginImageSize imageSize)
    {
        this.ParseActionParameter(actionParameter, out string pageName, out string pageActionParameter);
        if (this.GetPage(pageName) is FolderPage page && page == this.CurrentPage)
        {
            return page.GetAdjustmentImage(pageActionParameter, imageSize);
        }
        return PluginImage.DrawBlackImage(imageSize);
    }

    // Input handlers talk to audio endpoints that can vanish at any moment (app closed, device unplugged);
    // a COM exception there must not escape into the Loupedeck service.
    private void Guard(string handler, Action action)
    {
        this.Guard(handler, () =>
        {
            action();
            return true;
        });
    }

    private bool Guard(string handler, Func<bool> action)
    {
        try
        {
            return action();
        }
        catch (Exception ex)
        {
            PluginLog.Warning(ex, $"{handler} failed: {ex.Message}");
            return false;
        }
    }

    public override void RunCommand(string actionParameter)
    {
        this.ParseActionParameter(actionParameter, out string pageName, out string pageActionParameter);
        if (this.GetPage(pageName) is FolderPage page && page == this.CurrentPage)
        {
            if (pageActionParameter == "@back")
            {
                this.OnBackRequested();
            }
            else
            {
                this.Guard(nameof(RunCommand), () => page.RunCommand(pageActionParameter));
            }
        }
        base.RunCommand(pageActionParameter);
    }

    public override void ApplyAdjustment(string actionParameter, int diff)
    {
        this.ParseActionParameter(actionParameter, out string pageName, out string pageActionParameter);
        if (this.GetPage(pageName) is FolderPage page && page == this.CurrentPage)
        {
            this.Guard(nameof(ApplyAdjustment), () => page.ApplyAdjustment(pageActionParameter, diff));
        }
        base.ApplyAdjustment(pageActionParameter, diff);
    }

    public override bool ProcessButtonEvent2(string actionParameter, DeviceButtonEvent2 buttonEvent)
    {
        this.ParseActionParameter(actionParameter, out string pageName, out string pageActionParameter);
        if (this.GetPage(pageName) is FolderPage page && page == this.CurrentPage)
        {
            if (buttonEvent.EventType == DeviceButtonEventType.Press)
            {
                return this.Guard(nameof(ProcessButtonEvent2), () => page.ProcessButtonEvent2(pageActionParameter, buttonEvent));
            }
        }
        return base.ProcessButtonEvent2(actionParameter, buttonEvent);
    }

    public override bool ProcessEncoderEvent(string actionParameter, DeviceEncoderEvent encoderEvent)
    {
        this.ParseActionParameter(actionParameter, out string pageName, out string pageActionParameter);
        if (this.GetPage(pageName) is FolderPage page && page == this.CurrentPage)
        {
            return this.Guard(nameof(ProcessEncoderEvent), () => page.ProcessEncoderEvent(pageActionParameter, encoderEvent));
        }
        return base.ProcessEncoderEvent(actionParameter, encoderEvent);
    }

    public override bool ProcessTouchEvent(string actionParameter, DeviceTouchEvent touchEvent)
    {
        this.ParseActionParameter(actionParameter, out string pageName, out string pageActionParameter);
        if (this.GetPage(pageName) is FolderPage page && page == this.CurrentPage)
        {
            if (pageActionParameter == "@back")
            {
                if (touchEvent.EventType == DeviceTouchEventType.Tap)
                {
                    this.OnBackRequested();
                }
            }
            else
            {
                return this.Guard(nameof(ProcessTouchEvent), () => page.ProcessTouchEvent(pageActionParameter, touchEvent));
            }
        }
        return base.ProcessTouchEvent(actionParameter, touchEvent);
    }

    public new void CommandImageChanged(string actionParameter)
    {
        if (this.CurrentPage != null)
        {
            base.CommandImageChanged($"{this.CurrentPage.Name}+{actionParameter}");
        }
    }

    public void CommandImageChanged(string pageName, string actionParameter)
    {
        base.CommandImageChanged($"{pageName}+{actionParameter}");
    }

    public new void AdjustmentImageChanged(string actionParameter)
    {
        if (this.CurrentPage != null)
        {
            base.AdjustmentImageChanged($"{this.CurrentPage.Name}+{actionParameter}");
        }
    }

    public void AdjustmentImageChanged(string pageName, string actionParameter)
    {
        base.AdjustmentImageChanged($"{pageName}+{actionParameter}");
    }

    public new void ButtonActionNamesChanged() => base.ButtonActionNamesChanged();

    public new void EncoderActionNamesChanged() => base.EncoderActionNamesChanged();

    public new void AdjustmentValueChanged(string actionParameter) => base.AdjustmentValueChanged(actionParameter);
}
