using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

public static class TaskbarHider {
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr FindWindow(string lpClassName, IntPtr lpWindowName);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string lpszClass, IntPtr lpszWindow);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref RECT pvParam, uint fWinIni);

    private const int SW_HIDE = 0;
    private const uint SPI_GETWORKAREA = 0x0030;
    private const uint SPI_SETWORKAREA = 0x002F;
    private const uint SPIF_SENDCHANGE = 0x0002;
    private static readonly string FlagPath = @"C:\Users\Laziko\.config\yasb\show_taskbar.flag";

    private static void HideWindowIfVisible(IntPtr hwnd) {
        if (hwnd != IntPtr.Zero && IsWindowVisible(hwnd)) {
            ShowWindow(hwnd, SW_HIDE);
        }
    }

    private static void EnsureWorkArea() {
        RECT r = new RECT();
        if (SystemParametersInfo(SPI_GETWORKAREA, 0, ref r, 0)) {
            int screenBottom = Screen.PrimaryScreen.Bounds.Bottom;
            if (r.Bottom < screenBottom) {
                r.Bottom = screenBottom;
                SystemParametersInfo(SPI_SETWORKAREA, 0, ref r, SPIF_SENDCHANGE);
            }
        }
    }

    [STAThread]
    public static void Main() {
        bool createdNew;
        using (var mutex = new Mutex(true, "Local\\YASB_TaskbarHider_Mutex", out createdNew)) {
            if (!createdNew) return;

            while (true) {
                try {
                    if (!File.Exists(FlagPath)) {
                        HideWindowIfVisible(FindWindow("Shell_TrayWnd", IntPtr.Zero));
                        IntPtr sec = IntPtr.Zero;
                        while ((sec = FindWindowEx(IntPtr.Zero, sec, "Shell_SecondaryTrayWnd", IntPtr.Zero)) != IntPtr.Zero) {
                            HideWindowIfVisible(sec);
                        }
                        EnsureWorkArea();
                    }
                } catch {
                }
                Thread.Sleep(500);
            }
        }
    }
}
