// NotepadHelper: open notepad and send text to it

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Sql2SqlCloner.Components
{
    public static class NotepadHelper
    {
        [DllImport("user32.dll", EntryPoint = "FindWindowEx")]
        private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string lpszClass, string lpszWindow);

        [DllImport("User32.dll", EntryPoint = "SendMessage")]
        private static extern int SendMessage(IntPtr hWnd, int uMsg, int wParam, string lParam);

        [DllImport("user32.dll", EntryPoint = "SetWindowText")]
        private static extern int SetWindowText(IntPtr hWnd, string text);

        public static void ShowMessage(string message = null, string title = null)
        {
            var notepad = Process.Start(new ProcessStartInfo("notepad.exe"));
            if (notepad != null)
            {
                notepad.WaitForInputIdle();
                if (!string.IsNullOrEmpty(title))
                {
                    SetWindowText(notepad.MainWindowHandle, title);
                }
                if (!string.IsNullOrEmpty(message))
                {
                    var child = FindWindowEx(notepad.MainWindowHandle, new IntPtr(0), "Edit", null);
                    SendMessage(child, 0x000C, 0, message);
                }
            }
        }
    }
}
