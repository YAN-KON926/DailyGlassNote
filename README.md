# DailyGlassNote

A lightweight frosted-glass daily task and text note app for Windows 10/11.

DailyGlassNote（每日玻璃便签）是一款轻量级 Windows 桌面便签，支持每日任务和自由文字记录。

## 功能

- 任务便签与文字便签两种模式
- 红、绿、蓝三种任务状态
- 未完成任务自动顺延
- 任务备注、右键删除和拖动排序
- 多便签、托盘隐藏和重启恢复
- 字体颜色与玻璃透明度调节
- 近 30 天任务记录

## 下载使用

下载 `dist/每日便签-Windows.zip`，解压后运行 `每日便签.exe`。建议使用 64 位 Windows 10 或 Windows 11。

## 从源码构建

在 Windows PowerShell 中运行：

```powershell
.\build.ps1
```

项目使用 Windows 自带的 .NET Framework C# 编译器，无需额外安装大型开发环境。

## 隐私

任务、备注、文字便签和窗口设置仅保存在本机 `%AppData%\daily-sticky`。本仓库不包含任何用户任务数据或个人便签内容。

## 说明

透明磨砂效果依赖 Windows 桌面合成和显卡驱动，在远程桌面或部分系统配置下可能显示为普通半透明效果。
