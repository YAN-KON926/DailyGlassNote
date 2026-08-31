# DailyGlassNote

A lightweight frosted-glass daily task and text note app for Windows 10/11.

![DailyGlassNote preview](docs/images/dailyglassnote-preview.png)

DailyGlassNote is a portable Windows desktop sticky-note application that combines daily task tracking with free-form text notes. It stays quietly on the desktop while keeping frequently used tasks easy to review and update.

## Features

- Daily task notes and free-form text notes
- Red, green, and blue task states
- Automatic rollover for unfinished tasks
- Task notes and time requirements
- Right-click deletion and drag-to-reorder
- Multiple independent sticky-note windows
- System-tray hiding and restart restoration
- Six text-color levels and adjustable glass transparency
- Up to 30 days of task history
- Portable build with no installer required

## Download and run

Download `dist/每日便签-Windows.zip`, extract it, and run `每日便签.exe`.

Recommended environment: 64-bit Windows 10 or Windows 11.

## Build from source

Run the following command in Windows PowerShell:

```powershell
.\build.ps1
```

The project uses the C# compiler included with .NET Framework on Windows, so a full Visual Studio installation is not required.

## Privacy

Tasks, notes, text content, and window settings are stored locally in `%AppData%\daily-sticky`. This repository does not contain user task data or personal note content.

## Notes

The frosted-glass appearance depends on Windows desktop composition and graphics drivers. Under Remote Desktop or certain system configurations, the effect may fall back to ordinary transparency.

---

## 中文说明

DailyGlassNote（每日玻璃便签）是一款适用于 Windows 10/11 的轻量级透明磨砂桌面便签，支持每日任务、文字记录、三色状态、任务顺延、备注、拖动排序、多便签和外观调节。

下载 `dist/每日便签-Windows.zip`，解压后运行 `每日便签.exe` 即可。任务和便签内容仅保存在本机 `%AppData%\daily-sticky`，仓库中不包含用户数据。
