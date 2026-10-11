using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using static UsbGScope.Core.Interop.NativeMethods;

namespace UsbGScope.Core.Enumeration;

/// <summary>
/// Raises <see cref="Changed"/> (on a thread-pool thread, debounced) when USB devices, hubs,
/// disks, volumes or COM ports arrive or leave. Uses CM_Register_Notification: no window, no admin.
/// </summary>
public sealed unsafe class UsbChangeWatcher : IDisposable
{
    /// <summary>Interface classes whose arrival/removal can change what the tree shows.</summary>
    internal static readonly HashSet<Guid> RelevantInterfaces =
    [
        new("A5DCBF10-6530-11D2-901F-00C04FB951ED"), // GUID_DEVINTERFACE_USB_DEVICE
        new("F18A0E88-C30C-11D0-8815-00A0C906BED8"), // GUID_DEVINTERFACE_USB_HUB
        new("3ABF6F2D-71C4-462A-8A92-1E6861E6AF27"), // GUID_DEVINTERFACE_USB_HOST_CONTROLLER
        new("53F56307-B6BF-11D0-94F2-00A0C91EFB8B"), // GUID_DEVINTERFACE_DISK
        new("53F56308-B6BF-11D0-94F2-00A0C91EFB8B"), // GUID_DEVINTERFACE_CDROM
        new("53F5630D-B6BF-11D0-94F2-00A0C91EFB8B"), // GUID_DEVINTERFACE_VOLUME
        new("86E0D1E0-8089-11D0-9CE4-08003E301F73"), // GUID_DEVINTERFACE_COMPORT
    ];

    private readonly Debouncer _debouncer;
    private GCHandle _self;
    private nint _registration;

    public event EventHandler? Changed;

    public UsbChangeWatcher(TimeSpan? quietPeriod = null)
    {
        _debouncer = new Debouncer(quietPeriod ?? TimeSpan.FromMilliseconds(450), () => Changed?.Invoke(this, EventArgs.Empty));
        _self = GCHandle.Alloc(this);

        var filter = new CM_NOTIFY_FILTER
        {
            cbSize = (uint)sizeof(CM_NOTIFY_FILTER),
            Flags = CM_NOTIFY_FILTER_FLAG_ALL_INTERFACE_CLASSES,
            FilterType = CM_NOTIFY_FILTER_TYPE_DEVICEINTERFACE,
        };

        nint registration;
        uint cr = CM_Register_Notification(&filter, GCHandle.ToIntPtr(_self), &OnNotification, &registration);
        if (cr != CR_SUCCESS)
        {
            _self.Free();
            throw new InvalidOperationException($"CM_Register_Notification failed (CONFIGRET 0x{cr:X}).");
        }
        _registration = registration;
    }

    /// <summary>True when an interface event of this class should trigger a refresh.</summary>
    internal static bool IsRelevant(uint action, Guid interfaceClass) =>
        action is CM_NOTIFY_ACTION_DEVICEINTERFACEARRIVAL or CM_NOTIFY_ACTION_DEVICEINTERFACEREMOVAL
        && RelevantInterfaces.Contains(interfaceClass);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static uint OnNotification(nint notify, nint context, uint action, nint eventData, uint eventDataSize)
    {
        try
        {
            // CM_NOTIFY_EVENT_DATA: FilterType u32 @0, Reserved u32 @4, ClassGuid @8 (device-interface events).
            if (eventData != 0 && eventDataSize >= 24
                && IsRelevant(action, *(Guid*)(eventData + 8))
                && GCHandle.FromIntPtr(context).Target is UsbChangeWatcher watcher)
            {
                watcher._debouncer.Trigger();
            }
        }
        catch
        {
            // Never let an exception cross the native boundary.
        }
        return 0; // ERROR_SUCCESS
    }

    public void Dispose()
    {
        // Must not be called from inside the callback: CM_Unregister_Notification waits for callbacks to drain.
        if (_registration != 0)
        {
            CM_Unregister_Notification(_registration);
            _registration = 0;
        }
        _debouncer.Dispose();
        if (_self.IsAllocated)
            _self.Free();
    }
}

/// <summary>Coalesces bursts of triggers into one callback after a quiet period.</summary>
public sealed class Debouncer : IDisposable
{
    private readonly Timer _timer;
    private readonly TimeSpan _quietPeriod;
    private volatile bool _disposed;

    public Debouncer(TimeSpan quietPeriod, Action action)
    {
        _quietPeriod = quietPeriod;
        _timer = new Timer(_ => { if (!_disposed) action(); }, null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>Restarts the quiet period; the action runs once it elapses without another trigger.</summary>
    public void Trigger()
    {
        if (_disposed)
            return;
        try { _timer.Change(_quietPeriod, Timeout.InfiniteTimeSpan); }
        catch (ObjectDisposedException) { }
    }

    public void Dispose()
    {
        _disposed = true;
        _timer.Dispose();
    }
}
