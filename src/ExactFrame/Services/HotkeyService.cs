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

    /// <summary>
    /// Asks every installed keyboard layout what the keys type. Only Ctrl + Alt without Win can type a character,
    /// because that is how Windows reports AltGr. Control characters, such as Ctrl + 2 on a US layout, don't count.
    /// </summary>
    public string? TypedCharacter(HotkeyGesture gesture)
    {
        const HotkeyModifiers altGr = HotkeyModifiers.Control | HotkeyModifiers.Alt;
        if ((gesture.Modifiers & altGr) != altGr || gesture.Modifiers.HasFlag(HotkeyModifiers.Windows) ||
            gesture.VirtualKey is <= 0 or > 0xFE) return null;

        const byte down = 0x80;
        var state = new byte[256];
        state[NativeMethods.VkControl] = state[NativeMethods.VkLControl] = down;
        state[NativeMethods.VkMenu] = state[NativeMethods.VkRMenu] = down;
        if (gesture.Modifiers.HasFlag(HotkeyModifiers.Shift)) state[NativeMethods.VkShift] = state[NativeMethods.VkLShift] = down;

        int count = NativeMethods.GetKeyboardLayoutList(0, null);
        if (count <= 0) return null;
        var layouts = new nint[count];
        count = NativeMethods.GetKeyboardLayoutList(count, layouts);

        var buffer = new char[8];
        uint key = (uint)gesture.VirtualKey;
        foreach (nint layout in layouts.Take(count))
        {
            uint scanCode = NativeMethods.MapVirtualKeyEx(key, NativeMethods.MapVkToVsc, layout);
            // Without changing the keyboard state, so a dead key (-1) doesn't affect the next key typed.
            int length = NativeMethods.ToUnicodeEx(key, scanCode, state, buffer, buffer.Length,
                NativeMethods.ToUnicodeKeepKeyboardState, layout);
            if (length == 0) continue;
            string typed = new(buffer, 0, Math.Min(Math.Abs(length), buffer.Length));
            if (typed.Any(c => !char.IsControl(c))) return typed;
        }
        return null;
    }

    public void Dispose()
    {
        _messages.HotkeyPressed -= OnHotkeyPressed;
        foreach (int id in _registered.ToArray()) Unregister(id);
    }

    private void OnHotkeyPressed(object? sender, int id) => Pressed?.Invoke(this, id);
}
