$flag = "C:\Users\Laziko\.config\yasb\show_taskbar.flag"
if (Test-Path $flag) { Remove-Item $flag -Force }

$exe = "C:\Users\Laziko\.config\yasb\taskbar_hider.exe"
if ((Test-Path $exe) -and (-not (Get-Process taskbar_hider -ErrorAction SilentlyContinue))) {
    Start-Process -FilePath $exe -WindowStyle Hidden
}
