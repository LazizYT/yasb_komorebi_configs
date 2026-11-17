$flag = "C:\Users\Laziko\.config\yasb\show_taskbar.flag"
New-Item -Path $flag -ItemType File -Force | Out-Null

Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;

public static class TaskbarRestoreWin32 {
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern IntPtr FindWindow(string lpClassName, IntPtr lpWindowName);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string lpszClass, IntPtr lpszWindow);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    public const int SW_SHOW = 5;
}
"@

$primary = [TaskbarRestoreWin32]::FindWindow("Shell_TrayWnd", [IntPtr]::Zero)
if ($primary -ne [IntPtr]::Zero) {
    [void][TaskbarRestoreWin32]::ShowWindow($primary, [TaskbarRestoreWin32]::SW_SHOW)
}

$sec = [IntPtr]::Zero
while ($true) {
    $sec = [TaskbarRestoreWin32]::FindWindowEx([IntPtr]::Zero, $sec, "Shell_SecondaryTrayWnd", [IntPtr]::Zero)
    if ($sec -eq [IntPtr]::Zero) { break }
    [void][TaskbarRestoreWin32]::ShowWindow($sec, [TaskbarRestoreWin32]::SW_SHOW)
}
