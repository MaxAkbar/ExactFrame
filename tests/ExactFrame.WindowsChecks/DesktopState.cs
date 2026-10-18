using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace ExactFrame.WindowsChecks;

internal static class DesktopState
{
    public static void RequireInteractiveDesktop()
    {
        nint desktop = OpenInputDesktop(0, false, 1); // DESKTOP_READOBJECTS
        if (desktop == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Unlock Windows before running native recording checks; the input desktop is unavailable.");
        try
        {
            var name = new StringBuilder(256);
            if (!GetUserObjectInformation(desktop, 2, name, name.Capacity * 2, out _))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not check the input desktop.");
            if (!string.Equals(name.ToString(), "Default", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Unlock Windows before running native recording checks; the input desktop is " + name + ".");
        }
        finally { CloseDesktop(desktop); }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint OpenInputDesktop(uint flags, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint access);
    [DllImport("user32.dll", EntryPoint = "GetUserObjectInformationW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetUserObjectInformation(nint handle, int index, StringBuilder information, int length, out int needed);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseDesktop(nint desktop);
}
