using System.ComponentModel;
using System.Runtime.InteropServices;
using ExactFrame.Interop;

namespace ExactFrame.Native;

/// <summary>
/// A plain Win32 window owned by the UI thread. Subclasses override <see cref="WndProc"/>.
/// Messages are dispatched by the WinUI message loop because the windows live on the same thread.
/// </summary>
internal abstract class NativeWindow : IDisposable
{
    private const string DefaultClass = "ExactFrame.NativeWindow";
    private const string BlackClass = "ExactFrame.ShadeWindow";

    // Keep the delegate alive for the lifetime of the process; Windows holds a raw pointer to it.
    private static readonly NativeMethods.WndProc SharedWndProc = StaticWndProc;
    private static readonly Dictionary<nint, NativeWindow> Windows = [];
    private static readonly HashSet<string> RegisteredClasses = [];
    private static NativeWindow? _creating;

    public nint Handle { get; private set; }

    protected bool IsDisposed { get; private set; }

    /// <summary>Creates the window. <paramref name="blackBackground"/> paints the client area black.</summary>
    protected void CreateHandle(int exStyle, int style, bool blackBackground = false)
    {
        string className = blackBackground ? BlackClass : DefaultClass;
        EnsureClass(className, blackBackground);

        _creating = this;
        try
        {
            nint handle = NativeMethods.CreateWindowEx(exStyle, className, string.Empty, style,
                0, 0, 1, 1, 0, 0, NativeMethods.GetModuleHandle(null), 0);
            if (handle == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not create an overlay window.");
            Handle = handle;
            Windows[handle] = this;
        }
        finally
        {
            _creating = null;
        }
    }

    protected virtual nint WndProc(nint hwnd, uint message, nint wParam, nint lParam) =>
        NativeMethods.DefWindowProc(hwnd, message, wParam, lParam);

    public void Dispose()
    {
        if (IsDisposed) return;
        IsDisposed = true;
        OnDisposing();
        if (Handle != 0)
        {
            Windows.Remove(Handle);
            NativeMethods.DestroyWindow(Handle);
            Handle = 0;
        }
        GC.SuppressFinalize(this);
    }

    protected virtual void OnDisposing()
    {
    }

    private static void EnsureClass(string className, bool blackBackground)
    {
        if (RegisteredClasses.Contains(className)) return;
        var windowClass = new NativeMethods.WndClassEx
        {
            Size = (uint)Marshal.SizeOf<NativeMethods.WndClassEx>(),
            WndProc = Marshal.GetFunctionPointerForDelegate(SharedWndProc),
            Instance = NativeMethods.GetModuleHandle(null),
            Cursor = NativeMethods.LoadCursor(0, NativeMethods.IdcArrow),
            Background = blackBackground ? NativeMethods.GetStockObject(NativeMethods.BlackBrush) : 0,
            ClassName = className
        };
        if (NativeMethods.RegisterClassEx(ref windowClass) == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not register the overlay window class.");
        RegisteredClasses.Add(className);
    }

    private static nint StaticWndProc(nint hwnd, uint message, nint wParam, nint lParam)
    {
        if (!Windows.TryGetValue(hwnd, out var window) && _creating is { } creating)
        {
            // Messages such as WM_NCCREATE arrive before CreateWindowEx returns.
            creating.Handle = hwnd;
            Windows[hwnd] = creating;
            window = creating;
        }

        if (window is null) return NativeMethods.DefWindowProc(hwnd, message, wParam, lParam);

        try
        {
            return window.WndProc(hwnd, message, wParam, lParam);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ExternalException or ArgumentException)
        {
            // Never let a managed exception unwind through native code.
            System.Diagnostics.Debug.WriteLine(ex);
            return NativeMethods.DefWindowProc(hwnd, message, wParam, lParam);
        }
    }
}
