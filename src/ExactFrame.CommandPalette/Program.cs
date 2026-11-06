using Microsoft.CommandPalette.Extensions;
using Shmuelie.WinRTServer;
using Shmuelie.WinRTServer.CsWinRT;

namespace ExactFrame.CommandPalette;

internal static class Program
{
    [MTAThread]
    public static void Main(string[] args)
    {
        if (!IsServerLaunch(args)) return;
        using var stopped = new ManualResetEvent(false);
        var extension = new ExactFrameExtension(stopped);
        var server = new ComServer();
        server.RegisterClass<ExactFrameExtension, IExtension>(() => extension);
        server.Start();
        try { stopped.WaitOne(); }
        finally { server.Stop(); server.UnsafeDispose(); }
    }

    // COM can append -Embedding to the manifest's arguments when activating a local server.
    internal static bool IsServerLaunch(string[] args) => args.Length > 0 && args[0] == "-RegisterProcessAsComServer";
}
