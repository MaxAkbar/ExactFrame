using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Rectangle = System.Drawing.Rectangle;

namespace ExactFrame.Interop;

/// <summary>The narrow Win32 boundary. Everything here uses physical pixels (the app is per-monitor DPI aware).</summary>
internal static class NativeMethods
{
    // Window styles
    internal const int WsPopup = unchecked((int)0x80000000);
    internal const int WsExTopmost = 0x8;
    internal const int WsExTransparent = 0x20;
    internal const int WsExToolWindow = 0x80;
    internal const int WsExLayered = 0x80000;
    internal const int WsExNoActivate = 0x8000000;
    internal const int GwlExStyle = -20;

    // SetWindowPos / ShowWindow
    internal const uint SwpNoSize = 0x0001;
    internal const uint SwpNoMove = 0x0002;
    internal const uint SwpNoZOrder = 0x0004;
    internal const uint SwpNoActivate = 0x0010;
    internal const uint SwpShowWindow = 0x0040;
    internal const uint SwpNoOwnerZOrder = 0x0200;
    internal const uint SwpAsyncWindowPos = 0x4000;
    internal const uint PlacementRestoreToMaximized = 0x0002;
    internal const uint PlacementAsync = 0x0004;
    internal const int SwHide = 0;
    internal const int SwShowNormal = 1;
    internal const int SwShowNoActivate = 4;
    internal const int SwRestore = 9;
    internal static readonly nint HwndTop = 0;
    internal static readonly nint HwndTopmost = new(-1);

    // Messages
    internal const uint WmNull = 0x0000;
    internal const uint WmSettingChange = 0x001A;
    internal const uint WmSetCursor = 0x0020;
    internal const uint WmMouseActivate = 0x0021;
    internal const uint WmCopyData = 0x004A;
    internal const uint WmContextMenu = 0x007B;
    internal const uint WmDisplayChange = 0x007E;
    internal const uint WmNcHitTest = 0x0084;
    internal const uint WmKeyDown = 0x0100;
    internal const uint WmSysKeyDown = 0x0104;
    internal const uint WmMouseMove = 0x0200;
    internal const uint WmLButtonDown = 0x0201;
    internal const uint WmLButtonUp = 0x0202;
    internal const uint WmRButtonDown = 0x0204;
    internal const uint WmRButtonUp = 0x0205;
    internal const uint WmCaptureChanged = 0x0215;
    internal const uint WmDpiChanged = 0x02E0;
    internal const uint WmHotkey = 0x0312;
    internal const uint WmApp = 0x8000;
    internal const int MaNoActivate = 3;
    internal const uint MsgfltAllow = 1;

    // WM_NCHITTEST results: which part of a window is at a point
    internal const int HtClient = 1;
    internal const int HtCaption = 2;
    internal const int HtSysMenu = 3;
    internal const int HtMinButton = 8;
    internal const int HtMaxButton = 9;
    internal const int HtTop = 12;
    internal const int HtTopLeft = 13;
    internal const int HtTopRight = 14;
    internal const int HtClose = 20;
    internal const int HtHelp = 21;

    // SendMessageTimeout
    internal const uint SmtoAbortIfHung = 0x0002;
    internal const uint SmtoErrorOnExit = 0x0020;

    // GetAwarenessFromDpiAwarenessContext
    private const int DpiAwarenessPerMonitorAware = 2;
    internal const int SpiSetWorkArea = 0x002F;

    // Layered windows and GDI
    internal const uint UlwAlpha = 0x2;
    internal const uint LwaAlpha = 0x2;
    internal const byte AcSrcOver = 0x0;
    internal const byte AcSrcAlpha = 0x1;
    internal const int BlackBrush = 4;
    internal const int IdcArrow = 32512;
    internal const int IdcSizeAll = 32646;

    // Hooks, monitors, DWM, capture exclusion
    internal const int WhKeyboardLl = 13;
    internal const int WhMouseLl = 14;
    internal const uint GaRoot = 2;
    internal const int MdtEffectiveDpi = 0;
    internal const uint MonitorInfoPrimary = 1;
    internal const int DwmwaExtendedFrameBounds = 9;
    internal const int DwmwaCloaked = 14;
    internal const int DwmwaWindowCornerPreference = 33;
    internal const int DwmwcpRound = 2;
    internal const uint WdaNone = 0x0;
    internal const uint WdaExcludeFromCapture = 0x11;

    // Notification area icon (Shell_NotifyIcon, version 4), its menu and icon
    internal const uint NimAdd = 0x0;
    internal const uint NimModify = 0x1;
    internal const uint NimDelete = 0x2;
    internal const uint NimSetVersion = 0x4;
    internal const uint NifMessage = 0x01;
    internal const uint NifIcon = 0x02;
    internal const uint NifTip = 0x04;
    internal const uint NifInfo = 0x10;
    internal const uint NifShowTip = 0x80;
    internal const uint NiifInfo = 0x01;
    internal const uint NiifRespectQuietTime = 0x80;
    internal const uint NotifyIconVersion4 = 4;
    internal const uint NinSelect = 0x400;
    internal const uint NinKeySelect = 0x401;
    internal const uint MfString = 0x0;
    internal const uint MfGrayed = 0x1;
    internal const uint MfChecked = 0x8;
    internal const uint MfPopup = 0x10;
    internal const uint MfSeparator = 0x800;
    internal const uint TpmRightButton = 0x2;
    internal const uint TpmNoNotify = 0x80;
    internal const uint TpmReturnCmd = 0x100;
    internal const uint ImageIcon = 1;
    internal const uint LrLoadFromFile = 0x10;
    internal const int SmCxSmIcon = 49;
    internal const int IdiApplication = 32512;

    // Keyboard layouts
    internal const uint MapVkToVsc = 0;
    internal const uint ToUnicodeKeepKeyboardState = 0x4;
    internal const int VkShift = 0x10;
    internal const int VkControl = 0x11;
    internal const int VkMenu = 0x12;
    internal const int VkLShift = 0xA0;
    internal const int VkLControl = 0xA2;
    internal const int VkRMenu = 0xA5;

    internal delegate nint WndProc(nint hwnd, uint message, nint wParam, nint lParam);
    internal delegate nint HookProc(int code, nint wParam, nint lParam);
    internal delegate bool EnumWindowsProc(nint hwnd, nint parameter);
    internal delegate bool MonitorEnumProc(nint monitor, nint hdc, ref Rect rect, nint data);

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect
    {
        public int Left, Top, Right, Bottom;

        public readonly Rectangle ToRectangle() => Rectangle.FromLTRB(Left, Top, Right, Bottom);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Point
    {
        public int X, Y;

        public Point(int x, int y)
        {
            X = x;
            Y = y;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Size
    {
        public int Width, Height;

        public Size(int width, int height)
        {
            Width = width;
            Height = height;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct WindowPlacement
    {
        public uint Length, Flags, ShowCmd;
        public Point MinPosition, MaxPosition;
        public Rect NormalPosition;

        public static WindowPlacement Create() => new() { Length = (uint)Marshal.SizeOf<WindowPlacement>() };
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct WndClassEx
    {
        public uint Size;
        public uint Style;
        public nint WndProc;
        public int ClassExtra;
        public int WindowExtra;
        public nint Instance;
        public nint Icon;
        public nint Cursor;
        public nint Background;
        public string? MenuName;
        public string ClassName;
        public nint SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct BlendFunction
    {
        public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct BitmapInfoHeader
    {
        public uint Size;
        public int Width, Height;
        public ushort Planes, BitCount;
        public uint Compression, SizeImage;
        public int XPelsPerMeter, YPelsPerMeter;
        public uint ClrUsed, ClrImportant;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct MonitorInfoEx
    {
        public uint Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string Device;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct DisplayDevice
    {
        public uint Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        public uint StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Key;
    }

    [DllImport("user32.dll", EntryPoint = "EnumDisplayDevicesW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumDisplayDevices(string device, uint index, ref DisplayDevice display, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    internal struct MouseLowLevel
    {
        public Point Point;
        public uint MouseData, Flags, Time;
        public nint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct NotifyIconData
    {
        public uint Size;
        public nint Window;
        public uint Id;
        public uint Flags;
        public uint CallbackMessage;
        public nint Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Tip;
        public uint State;
        public uint StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string Info;
        public uint TimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string InfoTitle;
        public uint InfoFlags;
        public Guid Item;
        public nint BalloonIcon;
    }

    /// <summary>COPYDATASTRUCT: the payload of WM_COPYDATA.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct CopyData
    {
        public nint Id;
        public uint Size;
        public nint Data;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct KeyboardLowLevel
    {
        public uint VirtualKey, ScanCode, Flags, Time;
        public nint ExtraInfo;
    }

    // ---- Windows ----------------------------------------------------------------------------

    [DllImport("user32.dll", EntryPoint = "RegisterClassExW", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern ushort RegisterClassEx(ref WndClassEx windowClass);

    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern nint CreateWindowEx(int exStyle, string className, string windowName, int style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyWindow(nint hwnd);

    [DllImport("user32.dll", EntryPoint = "DefWindowProcW")]
    internal static extern nint DefWindowProc(nint hwnd, uint message, nint wParam, nint lParam);

    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode)]
    internal static extern nint GetModuleHandle(string? moduleName);

    [DllImport("user32.dll", EntryPoint = "LoadCursorW")]
    internal static extern nint LoadCursor(nint instance, nint cursorName);

    [DllImport("user32.dll")]
    internal static extern nint SetCursor(nint cursor);

    [DllImport("gdi32.dll")]
    internal static extern nint GetStockObject(int stockObject);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ShowWindow(nint hwnd, int command);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumWindows(EnumWindowsProc callback, nint parameter);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumChildWindows(nint parent, EnumWindowsProc callback, nint parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindow(nint hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowVisible(nint hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsIconic(nint hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsZoomed(nint hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsHungAppWindow(nint hwnd);

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    [DllImport("user32.dll", EntryPoint = "GetWindowTextW", CharSet = CharSet.Unicode)]
    internal static extern int GetWindowText(nint hwnd, StringBuilder text, int count);

    [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern int GetClassName(nint hwnd, StringBuilder className, int count);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowRect(nint hwnd, out Rect rect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetClientRect(nint hwnd, out Rect rect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ClientToScreen(nint hwnd, ref Point point);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowPlacement(nint hwnd, ref WindowPlacement placement);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowPlacement(nint hwnd, in WindowPlacement placement);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ShowWindowAsync(nint hwnd, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetForegroundWindow(nint hwnd);

    [DllImport("user32.dll")]
    internal static extern nint WindowFromPoint(Point point);

    [DllImport("user32.dll")]
    internal static extern nint GetAncestor(nint hwnd, uint flags);

    [DllImport("user32.dll")]
    internal static extern uint GetDpiForWindow(nint hwnd);

    [DllImport("user32.dll")]
    private static extern nint GetWindowDpiAwarenessContext(nint hwnd);

    [DllImport("user32.dll")]
    private static extern int GetAwarenessFromDpiAwarenessContext(nint context);

    /// <summary>Whether the window's app is per-monitor DPI aware, so it reads screen points as physical pixels, like ExactFrame.</summary>
    internal static bool IsPerMonitorDpiAware(nint hwnd)
    {
        nint context = GetWindowDpiAwarenessContext(hwnd);
        return context != 0 && GetAwarenessFromDpiAwarenessContext(context) == DpiAwarenessPerMonitorAware;
    }

    [DllImport("user32.dll", EntryPoint = "FindWindowW", CharSet = CharSet.Unicode)]
    internal static extern nint FindWindow(string? className, string? windowName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool AllowSetForegroundWindow(uint processId);

    [DllImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PostMessage(nint hwnd, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll", EntryPoint = "RegisterWindowMessageW", CharSet = CharSet.Unicode)]
    internal static extern uint RegisterWindowMessage(string name);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ChangeWindowMessageFilterEx(nint hwnd, uint message, uint action, nint changeFilter);

    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW", SetLastError = true)]
    internal static extern nint SendMessageTimeout(nint hwnd, uint message, nint wParam, nint lParam,
        uint flags, uint timeoutMs, out nint result);

    /// <summary>Packs a screen point into an lParam like MAKELPARAM, keeping negative coordinates on left or upper displays.</summary>
    internal static nint PointParam(int x, int y) => (nint)(int)(((uint)(y & 0xFFFF) << 16) | (uint)(x & 0xFFFF));

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr64(nint hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtr64(nint hwnd, int index, nint value);

    internal static nint GetWindowLongPtr(nint hwnd, int index) => GetWindowLongPtr64(hwnd, index);

    internal static void SetWindowLongPtr(nint hwnd, int index, nint value)
    {
        Marshal.SetLastPInvokeError(0);
        nint previous = SetWindowLongPtr64(hwnd, index, value);
        if (previous == 0 && Marshal.GetLastPInvokeError() != 0)
            throw new Win32Exception(Marshal.GetLastPInvokeError());
    }

    // ---- Notification area, menus and icons -------------------------------------------------

    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ShellNotifyIcon(uint message, ref NotifyIconData data);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern nint CreatePopupMenu();

    [DllImport("user32.dll", EntryPoint = "AppendMenuW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool AppendMenu(nint menu, uint flags, nint idOrSubmenu, string? text);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetMenuDefaultItem(nint menu, uint item, uint byPosition);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern int TrackPopupMenuEx(nint menu, uint flags, int x, int y, nint hwnd, nint parameters);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyMenu(nint menu);

    [DllImport("user32.dll", EntryPoint = "LoadImageW", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern nint LoadImage(nint instance, string name, uint type, int width, int height, uint flags);

    [DllImport("user32.dll", EntryPoint = "LoadIconW")]
    internal static extern nint LoadIcon(nint instance, nint name);

    [DllImport("shell32.dll", EntryPoint = "ExtractIconExW", CharSet = CharSet.Unicode)]
    internal static extern uint ExtractIconEx(string file, int index, nint largeIcons, out nint smallIcon, uint count);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyIcon(nint icon);

    [DllImport("user32.dll")]
    internal static extern int GetSystemMetrics(int index);

    // ---- Input ------------------------------------------------------------------------------

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    internal static extern nint SetCapture(nint hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ReleaseCapture();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint key);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnregisterHotKey(nint hwnd, int id);

    [DllImport("user32.dll")]
    internal static extern int GetKeyboardLayoutList(int count, [Out] nint[]? layouts);

    [DllImport("user32.dll", EntryPoint = "MapVirtualKeyExW")]
    internal static extern uint MapVirtualKeyEx(uint code, uint mapType, nint layout);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int ToUnicodeEx(uint virtualKey, uint scanCode, byte[] keyState,
        [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 4)] char[] buffer, int bufferSize, uint flags, nint layout);

    [DllImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
    internal static extern nint SetWindowsHookEx(int hookId, HookProc callback, nint module, uint threadId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnhookWindowsHookEx(nint hook);

    [DllImport("user32.dll")]
    internal static extern nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);

    // ---- Layered drawing --------------------------------------------------------------------

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UpdateLayeredWindow(nint hwnd, nint screenDc, ref Point destination, ref Size size,
        nint sourceDc, ref Point source, uint colorKey, ref BlendFunction blend, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetLayeredWindowAttributes(nint hwnd, uint colorKey, byte alpha, uint flags);

    [DllImport("user32.dll")]
    internal static extern nint GetDC(nint hwnd);

    [DllImport("user32.dll")]
    internal static extern int ReleaseDC(nint hwnd, nint hdc);

    [DllImport("gdi32.dll")]
    internal static extern nint CreateCompatibleDC(nint hdc);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeleteDC(nint hdc);

    [DllImport("gdi32.dll")]
    internal static extern nint SelectObject(nint hdc, nint gdiObject);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeleteObject(nint gdiObject);

    [DllImport("gdi32.dll", SetLastError = true)]
    internal static extern nint CreateDIBSection(nint hdc, ref BitmapInfoHeader header, uint usage, out nint bits,
        nint section, uint offset);

    // ---- Monitors, DWM and capture ----------------------------------------------------------

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumDisplayMonitors(nint hdc, nint clip, MonitorEnumProc callback, nint data);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetMonitorInfo(nint monitor, ref MonitorInfoEx info);

    [DllImport("shcore.dll")]
    internal static extern int GetDpiForMonitor(nint monitor, int dpiType, out uint dpiX, out uint dpiY);

    [DllImport("dwmapi.dll")]
    internal static extern int DwmGetWindowAttribute(nint hwnd, int attribute, out Rect value, int size);

    [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
    internal static extern int DwmGetWindowAttributeInt(nint hwnd, int attribute, out int value, int size);

    [DllImport("dwmapi.dll")]
    internal static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowDisplayAffinity(nint hwnd, uint affinity);

    internal static void Check(bool succeeded, string operation)
    {
        if (!succeeded)
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Windows could not {operation}.");
    }

    internal static int LowWord(nint value) => (int)((long)value & 0xFFFF);
}
