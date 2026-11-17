using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

public static class KomorebiLauncher {
    private const string KomorebiConfig = @"C:\Users\Laziko\.config\komorebi\komorebi.json";
    private const string WhkdExe = @"C:\Users\Laziko\.local\bin\whkd.exe";
    private const string WhkdConfig = @"C:\Users\Laziko\.config\whkdrc";

    private const uint CREATE_NO_WINDOW = 0x08000000;
    private const uint CREATE_BREAKAWAY_FROM_JOB = 0x01000000;
    private const int STARTF_USESHOWWINDOW = 0x00000001;
    private const short SW_HIDE = 0;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO {
        public int cb;
        public string lpReserved;
        public string lpDesktop;
        public string lpTitle;
        public int dwX, dwY, dwXSize, dwYSize;
        public int dwXCountChars, dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateProcess(
        string lpApplicationName,
        string lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        bool bInheritHandles,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string lpCurrentDirectory,
        ref STARTUPINFO lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation
    );

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    private static void ConfigureEnvironment() {
        Environment.SetEnvironmentVariable("KOMOREBI_CONFIG_HOME", @"C:\Users\Laziko\.config\komorebi");
        Environment.SetEnvironmentVariable("WHKD_CONFIG_HOME", @"C:\Users\Laziko\.config");
        string curPath = Environment.GetEnvironmentVariable("PATH") ?? "";
        Environment.SetEnvironmentVariable("PATH", @"C:\Program Files\komorebi\bin;C:\Users\Laziko\.local\bin;" + curPath);
    }

    private static void StartWhkdHidden() {
        if (Process.GetProcessesByName("whkd").Length > 0) return;
        if (!File.Exists(WhkdExe)) return;

        var si = new STARTUPINFO();
        si.cb = Marshal.SizeOf(typeof(STARTUPINFO));
        si.dwFlags = STARTF_USESHOWWINDOW;
        si.wShowWindow = SW_HIDE;

        string cmdLine = "\"" + WhkdExe + "\" -c \"" + WhkdConfig + "\"";
        PROCESS_INFORMATION pi;
        bool ok = CreateProcess(
            WhkdExe,
            cmdLine,
            IntPtr.Zero,
            IntPtr.Zero,
            false,
            CREATE_NO_WINDOW | CREATE_BREAKAWAY_FROM_JOB,
            IntPtr.Zero,
            @"C:\Users\Laziko\.config",
            ref si,
            out pi
        );
        if (!ok) {
            ok = CreateProcess(
                WhkdExe,
                cmdLine,
                IntPtr.Zero,
                IntPtr.Zero,
                false,
                CREATE_NO_WINDOW,
                IntPtr.Zero,
                @"C:\Users\Laziko\.config",
                ref si,
                out pi
            );
        }
        if (ok) {
            CloseHandle(pi.hThread);
            CloseHandle(pi.hProcess);
        }
    }

    private static void KillWhkd() {
        foreach (var proc in Process.GetProcessesByName("whkd")) {
            try {
                proc.Kill();
                proc.WaitForExit(2000);
            } catch {
            } finally {
                proc.Dispose();
            }
        }
    }

    public static void Main(string[] args) {
        try {
            ConfigureEnvironment();

            if (args != null && args.Length > 0) {
                string mode = args[0].Trim().ToLowerInvariant();
                if (mode == "--ensure-whkd") {
                    StartWhkdHidden();
                    Console.WriteLine("ok");
                    return;
                }
                if (mode == "--reload-whkd") {
                    KillWhkd();
                    StartWhkdHidden();
                    return;
                }
            }

            // Wait briefly so YASB reserves top appbar (40px) and taskbar_hider hides bottom bar
            Thread.Sleep(1500);

            if (Process.GetProcessesByName("komorebi").Length == 0) {
                string sock = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"komorebi\komorebi.sock");
                if (File.Exists(sock)) {
                    try { File.Delete(sock); } catch {}
                }

                var startInfo = new ProcessStartInfo {
                    FileName = @"C:\Program Files\komorebi\bin\komorebic-no-console.exe",
                    Arguments = "start --config \"" + KomorebiConfig + "\" --whkd",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                var p = Process.Start(startInfo);
                if (p != null) p.WaitForExit(5000);

                Thread.Sleep(1200);
                var retileInfo = new ProcessStartInfo {
                    FileName = @"C:\Program Files\komorebi\bin\komorebic-no-console.exe",
                    Arguments = "retile",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                Process.Start(retileInfo);
            }

            // Guarantee whkd is running even if komorebi was already alive
            StartWhkdHidden();
        } catch {
        }
    }
}
