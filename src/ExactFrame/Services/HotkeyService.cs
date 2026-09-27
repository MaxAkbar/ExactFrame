using ExactFrame.Core.Models;
using ExactFrame.Core.Services;
using ExactFrame.Interop;
using ExactFrame.Native;

namespace ExactFrame.Services;

/// <summary>Registers global shortcuts on a hidden window. MOD_NOREPEAT stops auto-repeat from toggling twice.</summary>
internal sealed class HotkeyService : IHotkeyService
{
    private const uint NoRepeat = 0x4000;

    private readonly MessageWindow _messages;
    private readonly HashSet<int> _registered = [];

    public HotkeyService(MessageWindow messages)
    {
        _messages = messages;
        _messages.HotkeyPressed += OnHotkeyPressed;
    }

    public event EventHandler<int>? Pressed;

    public bool Register(int id, HotkeyGesture gesture)
    {
        Unregister(id);
        if (!gesture.IsValid) return false;
        bool ok = NativeMethods.RegisterHotKey(_messages.Handle, id, (uint)gesture.Modifiers | NoRepeat, (uint)gesture.VirtualKey);
        if (ok) _registered.Add(id);
        return ok;
    }

    public void Unregister(int id)
    {
        if (_registered.Remove(id)) NativeMethods.UnregisterHotKey(_messages.Handle, id);
    }

    public void Dispose()
    {
        _messages.HotkeyPressed -= OnHotkeyPressed;
        foreach (int id in _registered.ToArray()) Unregister(id);
    }

    private void OnHotkeyPressed(object? sender, int id) => Pressed?.Invoke(this, id);
}
