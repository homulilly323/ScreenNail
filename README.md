# ScreenNail (屏幕钉) 📌

一个极简的 Windows 窗口置顶与锁定工具。

相比于传统的置顶软件，ScreenNail 不仅能让窗口置顶，还能**彻底锁死窗口位置**，可调透明度，设置鼠标是否穿透以及一键隐藏当前锁定页面的摸鱼功能。


##  快速使用

1. 在 [Releases](../../releases) 页面下载最新版的 `ScreenNail.exe`。
2. 双击运行。
3. 在激活你想钉住的窗口时，按下快捷键 `Ctrl + Shift + P`。

##  快捷键与操作

| 操作 | 说明 |
| --- | --- |
| `Ctrl + Shift + P` | 钉住当前焦点窗口（如果已钉住，则解锁） |
| `Alt + 鼠标滚轮` | 调节被钉住窗口的透明度 |
| `Ctrl + Shift + H` | 一键隐藏当前被钉住的窗口或显示 |

##  本地编译构建

本项目使用 C# (WinForms) 与 .NET 10 开发，纯原生调用 Windows API。

```bash
# 运行测试
dotnet run

# 打包为单文件 exe (开箱即用)
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

##  许可证

本项目基于 [MIT](LICENSE) 许可证开源。
