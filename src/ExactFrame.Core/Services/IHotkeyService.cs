using ExactFrame.Core.Models;

namespace ExactFrame.Core.Services;

/// <summary>System-wide shortcuts that work while another app has focus.</summary>
public interface IHotkeyService : IDisposable
{
    /// <summary>Raised on the UI thread with the id passed to <see cref="Register"/>.</summary>
    event EventHandler<int>? Pressed;

    /// <summary>Returns <c>false</c> when Windows refuses, usually because another app owns the shortcut.</summary>
    bool Register(int id, HotkeyGesture gesture);

    void Unregister(int id);
}
