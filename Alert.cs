using System.Runtime.InteropServices;
using System;

namespace ducky;

public static class Alert
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(
        IntPtr hWnd,
        string text,
        string caption,
        uint type
    );

    public static void Info(string message)
    {
        MessageBox(IntPtr.Zero, message, "Ducky Debug", 0);
    }
}