using System.Runtime.InteropServices;
using ExactFrame.Core.ViewModels;
using ExactFrame.Interop;

namespace ExactFrame.Native;

/// <summary>
/// ExactFrame's icon in the notification area. Selecting it opens the main window; its menu shows or hides the
/// outline, applies a profile or quits. The icon comes back by itself when Explorer restarts.
/// </summary>
internal sealed class TrayIcon : NativeWindow
{
    private const uint CallbackMessage = NativeMethods.WmApp + 1;
    private const uint IconId = 1;
    private const int CommandOpen = 1;
    private const int CommandToggleOutline = 2;
    private const int CommandQuit = 3;
    private const int CommandProfileBase = 100;

    private readonly MainViewModel _viewModel;
    private readonly uint _taskbarCreated;
    private readonly nint _icon;
    private readonly bool _ownsIcon;

    public TrayIcon(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        CreateHandle(NativeMethods.WsExToolWindow, NativeMethods.WsPopup);
        _taskbarCreated = NativeMethods.RegisterWindowMessage("TaskbarCreated");
        // Explorer's restart broadcast is blocked for an elevated app unless it's allowed.
        if (_taskbarCreated != 0)
            NativeMethods.ChangeWindowMessageFilterEx(Handle, _taskbarCreated, NativeMethods.MsgfltAllow, 0);
        (_icon, _ownsIcon) = LoadAppIcon();
        Add();
    }

    public event EventHandler? OpenRequested;

    public event EventHandler? QuitRequested;

    /// <summary>Shows a notification from the icon. Windows holds it back during quiet hours.</summary>
    public void ShowNotice(string title, string text)
    {
        var data = Data(NativeMethods.NifInfo);
        data.InfoTitle = title;
        data.Info = text;
        data.InfoFlags = NativeMethods.NiifInfo | NativeMethods.NiifRespectQuietTime;
        NativeMethods.ShellNotifyIcon(NativeMethods.NimModify, ref data);
    }

    protected override nint WndProc(nint hwnd, uint message, nint wParam, nint lParam)
    {
        if (message == CallbackMessage)
        {
            switch ((uint)NativeMethods.LowWord(lParam))
            {
                case NativeMethods.NinSelect:
                case NativeMethods.NinKeySelect:
                    OpenRequested?.Invoke(this, EventArgs.Empty);
                    break;
                case NativeMethods.WmContextMenu:
                    // Version 4 passes where to open the menu in wParam, also when it's opened from the keyboard.
                    ShowMenu((short)((long)wParam & 0xFFFF), (short)(((long)wParam >> 16) & 0xFFFF));
                    break;
            }
            return 0;
        }

        if (message == _taskbarCreated && _taskbarCreated != 0)
        {
            Add();
            return 0;
        }
        return base.WndProc(hwnd, message, wParam, lParam);
    }

    protected override void OnDisposing()
    {
        var data = Data(0);
        NativeMethods.ShellNotifyIcon(NativeMethods.NimDelete, ref data);
        if (_ownsIcon) NativeMethods.DestroyIcon(_icon);
    }

    private void Add()
    {
        var data = Data(NativeMethods.NifMessage | NativeMethods.NifIcon | NativeMethods.NifTip | NativeMethods.NifShowTip);
        data.CallbackMessage = CallbackMessage;
        data.Icon = _icon;
        data.Tip = "ExactFrame";
        // Fails while Explorer isn't running; TaskbarCreated adds the icon once it starts.
        if (!NativeMethods.ShellNotifyIcon(NativeMethods.NimAdd, ref data)) return;
        data.TimeoutOrVersion = NativeMethods.NotifyIconVersion4;
        NativeMethods.ShellNotifyIcon(NativeMethods.NimSetVersion, ref data);
    }

    private NativeMethods.NotifyIconData Data(uint flags) => new()
    {
        Size = (uint)Marshal.SizeOf<NativeMethods.NotifyIconData>(),
        Window = Handle,
        Id = IconId,
        Flags = flags,
        Tip = string.Empty,
        Info = string.Empty,
        InfoTitle = string.Empty
    };

    private void ShowMenu(int x, int y)
    {
        var profiles = _viewModel.Profiles.ToList();
        uint busy = _viewModel.IsBusy ? NativeMethods.MfGrayed : 0;
        nint menu = NativeMethods.CreatePopupMenu();
        if (menu == 0) return;
        try
        {
            NativeMethods.AppendMenu(menu, NativeMethods.MfString, CommandOpen, "Open ExactFrame");
            NativeMethods.SetMenuDefaultItem(menu, CommandOpen, 0);
            NativeMethods.AppendMenu(menu, NativeMethods.MfString | busy, CommandToggleOutline,
                _viewModel.IsOutlineVisible ? "Hide outline" : "Show outline");
            if (profiles.Count > 0)
            {
                nint submenu = NativeMethods.CreatePopupMenu();
                for (int i = 0; i < profiles.Count; i++)
                {
                    uint check = profiles[i].IsSelected ? NativeMethods.MfChecked : 0;
                    NativeMethods.AppendMenu(submenu, NativeMethods.MfString | check | busy, CommandProfileBase + i,
                        profiles[i].Name.Replace("&", "&&")); // A single & would underline the next letter.
                }
                // Destroying the menu destroys this submenu too.
                NativeMethods.AppendMenu(menu, NativeMethods.MfPopup, submenu, "Apply profile");
            }
            NativeMethods.AppendMenu(menu, NativeMethods.MfSeparator, 0, null);
            NativeMethods.AppendMenu(menu, NativeMethods.MfString, CommandQuit, "Quit ExactFrame");

            // The menu only closes when you click elsewhere if its window is in front.
            NativeMethods.SetForegroundWindow(Handle);
            int command = NativeMethods.TrackPopupMenuEx(menu,
                NativeMethods.TpmReturnCmd | NativeMethods.TpmNoNotify | NativeMethods.TpmRightButton, x, y, Handle, 0);
            NativeMethods.PostMessage(Handle, NativeMethods.WmNull, 0, 0);
            Run(command, profiles);
        }
        finally
        {
            NativeMethods.DestroyMenu(menu);
        }
    }

    private void Run(int command, IReadOnlyList<ProfileItem> profiles)
    {
        switch (command)
        {
            case CommandOpen:
                OpenRequested?.Invoke(this, EventArgs.Empty);
                break;
            case CommandToggleOutline:
                _viewModel.ToggleOutlineCommand.Execute(null);
                break;
            case CommandQuit:
                QuitRequested?.Invoke(this, EventArgs.Empty);
                break;
            case >= CommandProfileBase when command - CommandProfileBase < profiles.Count:
                profiles[command - CommandProfileBase].SelectCommand.Execute(null);
                break;
        }
    }

    /// <summary>The small icon from ExactFrame.ico, then the exe's own icon, then the Windows default.</summary>
    private static (nint Icon, bool Owned) LoadAppIcon()
    {
        int size = NativeMethods.GetSystemMetrics(NativeMethods.SmCxSmIcon);
        string file = Path.Combine(AppContext.BaseDirectory, "ExactFrame.ico");
        if (File.Exists(file))
        {
            nint icon = NativeMethods.LoadImage(0, file, NativeMethods.ImageIcon, size, size, NativeMethods.LrLoadFromFile);
            if (icon != 0) return (icon, true);
        }
        if (Environment.ProcessPath is { } exe &&
            NativeMethods.ExtractIconEx(exe, 0, 0, out nint small, 1) > 0 && small != 0) return (small, true);
        return (NativeMethods.LoadIcon(0, NativeMethods.IdiApplication), false);
    }
}
