@echo off
set "KOMOREBI_CONFIG_HOME=C:\Users\Laziko\.config\komorebi"
set "WHKD_CONFIG_HOME=C:\Users\Laziko\.config"
set "PATH=C:\Program Files\komorebi\bin;C:\Users\Laziko\.local\bin;%PATH%"

"C:\Program Files\komorebi\bin\komorebic.exe" stop --whkd >nul 2>&1
taskkill /f /im komorebi.exe >nul 2>&1
taskkill /f /im whkd.exe >nul 2>&1
del /f /q "%LOCALAPPDATA%\komorebi\komorebi.sock" >nul 2>&1

"C:\Program Files\komorebi\bin\komorebic-no-console.exe" start --config "C:\Users\Laziko\.config\komorebi\komorebi.json" --whkd
timeout /t 1 /nobreak >nul
"C:\Program Files\komorebi\bin\komorebic.exe" retile >nul 2>&1
