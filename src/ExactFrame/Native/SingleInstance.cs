using System.Runtime.InteropServices;
using ExactFrame.Interop;

namespace ExactFrame.Native;

/// <summary>
/// Keeps one ExactFrame per Windows session, so two copies never compete for the same shortcuts or database.
/// A second launch sends its command line to the running copy's <see cref="MessageWindow"/>, then exits.
/// </summary>
internal static class SingleInstance
{
    private const string MutexName = @"Local\ExactFrame.SingleInstance";

    /// <summary>
    /// Claims the session for this process. Keep the returned mutex for the app's lifetime; it is released when the
    /// process ends. Returns <c>null</c> when another ExactFrame already runs.
    /// </summary>
    public static Mutex? TryClaim()
    {
        var mutex = new Mutex(initiallyOwned: true, MutexName, out bool created);
        if (created) return mutex;
        mutex.Dispose();
        return null;
    }

    /// <summary>
    /// Sends the arguments to the running copy and lets it come to the front. Returns <c>false</c> if it can't be
    /// reached, for example while it's still starting or when it runs as administrator and this copy doesn't.
    /// </summary>
    public static bool Forward(IReadOnlyList<string> arguments)
    {
        nint target = 0;
        // The first copy creates its window a moment after claiming the mutex.
        for (int attempt = 0; attempt < 20 && target == 0; attempt++)
        {
            target = NativeMethods.FindWindow(NativeWindow.DefaultClass, MessageWindow.Title);
            if (target == 0) Thread.Sleep(100);
        }
        if (target == 0) return false;

        // This process was just started by the user, so it may hand the foreground to the running copy.
        NativeMethods.GetWindowThreadProcessId(target, out uint processId);
        NativeMethods.AllowSetForegroundWindow(processId);

        string text = string.Join('\0', arguments);
        nint buffer = Marshal.StringToHGlobalUni(text);
        nint data = Marshal.AllocHGlobal(Marshal.SizeOf<NativeMethods.CopyData>());
        try
        {
            Marshal.StructureToPtr(new NativeMethods.CopyData
            {
                Id = MessageWindow.LaunchDataId,
                Size = (uint)((text.Length + 1) * sizeof(char)),
                Data = buffer
            }, data, fDeleteOld: false);
            return NativeMethods.SendMessageTimeout(target, NativeMethods.WmCopyData, 0, data,
                NativeMethods.SmtoAbortIfHung, 5000, out _) != 0;
        }
        finally
        {
            Marshal.FreeHGlobal(data);
            Marshal.FreeHGlobal(buffer);
        }
    }
}
