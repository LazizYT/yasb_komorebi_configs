# YASB + Komorebi + whkdrc Configs

Бэкап конфигов и вспомогательных утилит для **YASB**, **Komorebi** и **whkd** (без файлов фонов/обоев и без логов/личных заметок).

## Содержимое
- `yasb/` — `config.yaml`, `styles.css`, PowerShell/CMD-скрипты и исходники + бинарники кастомных утилит (`komorebi_launcher`, `power_control`, `effects_menu`, `audio_visualizer`, `folder_menu`, `hotkey_cheatsheet`, `scratchpad`, `smart_launcher`, `taskbar_hider`, `wallpaper_gallery`, `live_wallpaper`, `workspace_app`)
- `komorebi/` — `komorebi.json`, `komorebi.bar.json`, `applications.json`
- `whkdrc` — конфиг горячих клавиш `whkd`
- `restore.ps1` — скрипт быстрого восстановления в `%USERPROFILE%\.config`

## Быстрое восстановление
1. Установите **YASB**, **Komorebi** и **whkd**.
2. Клонируйте репозиторий и запустите скрипт:
   ```powershell
   git clone https://github.com/LazizYT/yasb_komorebi_configs.git
   cd yasb_komorebi_configs
   powershell -ExecutionPolicy Bypass -File .\restore.ps1
   ```
3. При необходимости задайте переменную окружения `YASB_WEATHER_API_KEY` для виджета погоды.

> Примечание: обои, видеофоны (`video_thumbs/`), `yasb.log`, `scratchpad_notes.txt` и локальная история запусков намеренно не включены.
