using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;

namespace YasbWorkspaceApp
{
    internal static class Native
    {
        public const int SW_RESTORE = 9;
        public const int DWMWA_CLOAKED = 14;
        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int WS_EX_APPWINDOW = 0x00040000;

        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("user32.dll")]
        public static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        public static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();

        [DllImport("dwmapi.dll")]
        public static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);
    }

    internal class WinEntry
    {
        public IntPtr Hwnd;
        public string Exe;
    }

    public static class Program
    {
        private static readonly string KomorebicPath = @"C:\Program Files\komorebi\bin\komorebic.exe";

        public static int Main(string[] args)
        {
            if (args.Length < 2) return 1;
            string mode = args[0].ToLowerInvariant();

            if (mode == "launch" && args.Length >= 3)
            {
                string exePatterns = args[1];
                string launchExe = args[2];
                var argList = new List<string>();
                for (int i = 3; i < args.Length; i++)
                {
                    string a = args[i];
                    argList.Add(a.Contains(" ") && !a.StartsWith("\"") ? "\"" + a + "\"" : a);
                }
                string launchArgs = string.Join(" ", argList);
                HandleLaunch(exePatterns, launchExe, launchArgs);
                return 0;
            }

            if (mode == "slot")
            {
                int slot;
                if (int.TryParse(args[1], out slot) && slot >= 1)
                {
                    var wins = GetCurrentWorkspaceWindows();
                    if (slot <= wins.Count)
                    {
                        FocusWindow(wins[slot - 1].Hwnd);
                    }
                }
                return 0;
            }

            if (mode == "cycle")
            {
                string dir = args[1].ToLowerInvariant();
                var wins = GetCurrentWorkspaceWindows();
                if (wins.Count == 0) return 0;

                IntPtr fg = Native.GetForegroundWindow();
                int idx = wins.FindIndex(w => w.Hwnd == fg);
                int nextIdx;
                if (dir == "prev" || dir == "previous")
                {
                    nextIdx = idx <= 0 ? wins.Count - 1 : idx - 1;
                }
                else
                {
                    nextIdx = (idx + 1) % wins.Count;
                }
                FocusWindow(wins[nextIdx].Hwnd);
                return 0;
            }

            return 1;
        }

        private static void HandleLaunch(string exePatterns, string launchExe, string launchArgs)
        {
            var patterns = new HashSet<string>(
                exePatterns.Split(new[] { '|', ',' }, StringSplitOptions.RemoveEmptyEntries),
                StringComparer.OrdinalIgnoreCase);

            var wins = GetCurrentWorkspaceWindows();
            var matches = new List<WinEntry>();
            foreach (var w in wins)
            {
                if (!string.IsNullOrEmpty(w.Exe) && patterns.Contains(w.Exe))
                {
                    matches.Add(w);
                }
            }

            if (matches.Count > 0)
            {
                IntPtr fg = Native.GetForegroundWindow();
                int fgIdx = matches.FindIndex(m => m.Hwnd == fg);
                int targetIdx = fgIdx >= 0 ? (fgIdx + 1) % matches.Count : 0;
                FocusWindow(matches[targetIdx].Hwnd);
                return;
            }

            try
            {
                var psi = new ProcessStartInfo(launchExe, launchArgs)
                {
                    UseShellExecute = true,
                    WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                };
                Process.Start(psi);
            }
            catch { }
        }

        private static List<WinEntry> GetCurrentWorkspaceWindows()
        {
            var result = new List<WinEntry>();
            var seen = new HashSet<long>();

            // 1. Query Komorebi focused workspace containers + floating windows
            var komorebiWins = QueryKomorebiFocusedWorkspace();
            foreach (var w in komorebiWins)
            {
                long k = w.Hwnd.ToInt64();
                if (k != 0 && Native.IsWindowVisible(w.Hwnd) && !IsCloaked(w.Hwnd) && seen.Add(k))
                {
                    result.Add(w);
                }
            }

            // 2. Append any unmanaged/minimized taskbar-eligible windows visible on current desktop
            var skippedExes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "yasb.exe", "folder_menu.exe", "workspace_app.exe", "taskbar_hider.exe",
                "Rainmeter.exe", "SearchHost.exe", "ShellExperienceHost.exe",
                "StartMenuExperienceHost.exe", "LockApp.exe", "TextInputHost.exe"
            };
            var skippedClasses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Progman", "WorkerW",
                "Windows.UI.Core.CoreWindow", "RainmeterMeterWindow"
            };

            Native.EnumWindows((hwnd, lp) =>
            {
                long k = hwnd.ToInt64();
                if (seen.Contains(k)) return true;
                if (!Native.IsWindowVisible(hwnd) || IsCloaked(hwnd)) return true;
                if (Native.GetWindowTextLength(hwnd) == 0) return true;

                int exStyle = Native.GetWindowLong(hwnd, Native.GWL_EXSTYLE);
                if ((exStyle & Native.WS_EX_TOOLWINDOW) != 0) return true;
                IntPtr owner = Native.GetWindow(hwnd, 4); // GW_OWNER
                if (owner != IntPtr.Zero && (exStyle & Native.WS_EX_APPWINDOW) == 0) return true;

                var cls = new StringBuilder(128);
                Native.GetClassName(hwnd, cls, cls.Capacity);
                string clsName = cls.ToString();
                if (skippedClasses.Contains(clsName)) return true;

                string exeName = GetProcessExeName(hwnd);
                if (string.IsNullOrEmpty(exeName) || skippedExes.Contains(exeName)) return true;
                if (exeName.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase) &&
                    !clsName.Equals("CabinetWClass", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                seen.Add(k);
                result.Add(new WinEntry { Hwnd = hwnd, Exe = exeName });
                return true;
            }, IntPtr.Zero);

            return result;
        }

        private static List<WinEntry> QueryKomorebiFocusedWorkspace()
        {
            var list = new List<WinEntry>();
            if (!File.Exists(KomorebicPath)) return list;

            try
            {
                var psi = new ProcessStartInfo(KomorebicPath, "state")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8
                };
                psi.EnvironmentVariables["KOMOREBI_CONFIG_HOME"] = @"C:\Users\Laziko\.config\komorebi";

                string json;
                using (var proc = Process.Start(psi))
                {
                    json = proc.StandardOutput.ReadToEnd();
                    proc.WaitForExit(1500);
                }

                if (string.IsNullOrWhiteSpace(json)) return list;
                var ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                var root = ser.DeserializeObject(json) as IDictionary<string, object>;
                if (root == null) return list;

                var monitors = root["monitors"] as IDictionary<string, object>;
                if (monitors == null) return list;
                int monFocused = Convert.ToInt32(monitors["focused"]);
                var monElements = monitors["elements"] as object[];
                if (monElements == null || monFocused < 0 || monFocused >= monElements.Length) return list;

                var mon = monElements[monFocused] as IDictionary<string, object>;
                if (mon == null) return list;
                var workspaces = mon["workspaces"] as IDictionary<string, object>;
                if (workspaces == null) return list;
                int wsFocused = Convert.ToInt32(workspaces["focused"]);
                var wsElements = workspaces["elements"] as object[];
                if (wsElements == null || wsFocused < 0 || wsFocused >= wsElements.Length) return list;

                var ws = wsElements[wsFocused] as IDictionary<string, object>;
                if (ws == null) return list;

                var containers = ws["containers"] as IDictionary<string, object>;
                if (containers != null)
                {
                    var cElements = containers["elements"] as object[];
                    if (cElements != null)
                    {
                        foreach (object cObj in cElements)
                        {
                            var c = cObj as IDictionary<string, object>;
                            if (c == null) continue;
                            var windows = c["windows"] as IDictionary<string, object>;
                            if (windows == null) continue;
                            var wElements = windows["elements"] as object[];
                            if (wElements == null) continue;
                            foreach (object wObj in wElements)
                            {
                                AddJsonWindow(list, wObj as IDictionary<string, object>);
                            }
                        }
                    }
                }

                var floating = ws["floating_windows"] as object[];
                if (floating != null)
                {
                    foreach (object fObj in floating)
                    {
                        AddJsonWindow(list, fObj as IDictionary<string, object>);
                    }
                }
            }
            catch { }

            return list;
        }

        private static void AddJsonWindow(List<WinEntry> list, IDictionary<string, object> win)
        {
            if (win == null || !win.ContainsKey("hwnd")) return;
            long hwndVal = Convert.ToInt64(win["hwnd"]);
            string exe = win.ContainsKey("exe") && win["exe"] != null ? win["exe"].ToString() : "";
            list.Add(new WinEntry { Hwnd = new IntPtr(hwndVal), Exe = exe });
        }

        private static bool IsCloaked(IntPtr hwnd)
        {
            int cloaked = 0;
            int hr = Native.DwmGetWindowAttribute(hwnd, Native.DWMWA_CLOAKED, out cloaked, sizeof(int));
            return hr == 0 && cloaked != 0;
        }

        private static string GetProcessExeName(IntPtr hwnd)
        {
            try
            {
                uint pid;
                Native.GetWindowThreadProcessId(hwnd, out pid);
                if (pid == 0) return "";
                using (var p = Process.GetProcessById((int)pid))
                {
                    return p.ProcessName + ".exe";
                }
            }
            catch
            {
                return "";
            }
        }

        private static void FocusWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return;
            if (Native.IsIconic(hwnd))
            {
                Native.ShowWindow(hwnd, Native.SW_RESTORE);
            }

            IntPtr fg = Native.GetForegroundWindow();
            uint pid;
            uint fgThread = fg != IntPtr.Zero ? Native.GetWindowThreadProcessId(fg, out pid) : 0;
            uint curThread = Native.GetCurrentThreadId();
            bool attached = false;
            if (fgThread != 0 && fgThread != curThread)
            {
                attached = Native.AttachThreadInput(curThread, fgThread, true);
            }

            Native.BringWindowToTop(hwnd);
            Native.SetForegroundWindow(hwnd);

            if (attached)
            {
                Native.AttachThreadInput(curThread, fgThread, false);
            }
        }
    }
}
