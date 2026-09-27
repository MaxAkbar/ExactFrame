using System.Runtime.InteropServices;
using ExactFrame.Core.Models;
using ExactFrame.Core.Services;
using ExactFrame.Interop;

namespace ExactFrame.Services;

/// <summary>
/// "Pick on screen": low-level mouse and keyboard hooks catch the next click on another app's window.
/// The click is swallowed so it doesn't reach that app. Right-click, Esc or 15 seconds cancel.
/// Hooks run on the UI thread's message loop, which the WinUI app already pumps.
/// </summary>
internal sealed class WindowPicker : IWindowPicker
{
    private const int VkEscape = 0x1B;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private readonly NativeMethods.HookProc _mouseProc;
    private readonly NativeMethods.HookProc _keyboardProc;
    private nint _mouseHook;
    private nint _keyboardHook;
    private bool _swallowNextUp;
    private TaskCompletionSource<WindowInfo?>? _pending;

    public WindowPicker()
    {
        // Keep delegates in fields so the GC can't collect them while hooks are installed.
        _mouseProc = MouseProc;
        _keyboardProc = KeyboardProc;
    }

    public async Task<WindowInfo?> PickAsync(CancellationToken cancellation)
    {
        Complete(null);
        Unhook();
        _swallowNextUp = false;

        // Continuations must not run inside the hook callback, which Windows times out.
        var pending = new TaskCompletionSource<WindowInfo?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending = pending;

        nint module = NativeMethods.GetModuleHandle(null);
        _mouseHook = NativeMethods.SetWindowsHookEx(NativeMethods.WhMouseLl, _mouseProc, module, 0);
        _keyboardHook = NativeMethods.SetWindowsHookEx(NativeMethods.WhKeyboardLl, _keyboardProc, module, 0);
        if (_mouseHook == 0)
        {
            Unhook();
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Windows couldn’t start window picking.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(Timeout);
        using (timeout.Token.Register(() => Complete(null)))
        {
            return await pending.Task;
        }
    }

    private nint MouseProc(int code, nint wParam, nint lParam)
    {
        if (code >= 0 && _pending is not null)
        {
            uint message = (uint)wParam;
            if (message == NativeMethods.WmLButtonDown)
            {
                var data = Marshal.PtrToStructure<NativeMethods.MouseLowLevel>(lParam);
                nint hwnd = NativeMethods.GetAncestor(NativeMethods.WindowFromPoint(data.Point), NativeMethods.GaRoot);
                var window = hwnd == 0 ? null : WindowService.Describe(hwnd);
                if (window is not null)
                {
                    _swallowNextUp = true;
                    Complete(window);
                    return 1;
                }
            }
            else if (message == NativeMethods.WmRButtonDown)
            {
                _swallowNextUp = true;
                Complete(null);
                return 1;
            }
        }

        if (_swallowNextUp && (uint)wParam is NativeMethods.WmLButtonUp or NativeMethods.WmRButtonUp)
        {
            _swallowNextUp = false;
            return 1;
        }

        return NativeMethods.CallNextHookEx(_mouseHook, code, wParam, lParam);
    }

    private nint KeyboardProc(int code, nint wParam, nint lParam)
    {
        if (code >= 0 && _pending is not null && (uint)wParam is NativeMethods.WmKeyDown or NativeMethods.WmSysKeyDown)
        {
            var data = Marshal.PtrToStructure<NativeMethods.KeyboardLowLevel>(lParam);
            if (data.VirtualKey == VkEscape)
            {
                Complete(null);
                return 1;
            }
        }
        return NativeMethods.CallNextHookEx(_keyboardHook, code, wParam, lParam);
    }

    private void Complete(WindowInfo? result)
    {
        var pending = _pending;
        _pending = null;
        // Keep the mouse hook until the button-up has been swallowed.
        if (!_swallowNextUp) Unhook();
        else UnhookKeyboard();
        pending?.TrySetResult(result);
        if (_swallowNextUp) _ = ReleaseMouseHookSoonAsync();
    }

    private async Task ReleaseMouseHookSoonAsync()
    {
        await Task.Delay(1500);
        if (_pending is null)
        {
            _swallowNextUp = false;
            Unhook();
        }
    }

    private void UnhookKeyboard()
    {
        if (_keyboardHook == 0) return;
        NativeMethods.UnhookWindowsHookEx(_keyboardHook);
        _keyboardHook = 0;
    }

    private void Unhook()
    {
        UnhookKeyboard();
        if (_mouseHook == 0) return;
        NativeMethods.UnhookWindowsHookEx(_mouseHook);
        _mouseHook = 0;
    }
}
