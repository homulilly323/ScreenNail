# ScreenNail (屏幕钉) 📌

一个极简的 Windows 窗口置顶与锁定工具。

ScreenNail 不仅能让窗口置顶，还能**彻底锁死窗口位置**，滚轮可调的透明度选择和一键隐藏当前锁定窗口的**一键摸鱼**功能。



## 使用方法

1. 在 [Releases](../../releases) 页面下载最新版的 `ScreenNail.exe`。
2. 双击运行。
3. 激活你想钉住的窗口，按下快捷键 `Ctrl + Shift + P`。

## 快捷键与操作

| 操作 | 说明 |
| --- | --- |
| `Ctrl + Shift + P` | 钉住/解锁当前焦点窗口 |
| `Ctrl + Shift + H` | 显示/隐藏当前钉住窗口 |
| `Alt + 鼠标滚轮` | 调节被钉住窗口的透明度 |


##  本地编译构建

本项目使用 C# (WinForms) 与 .NET 10 开发，纯原生调用 Windows API。

```bash
# 运行测试
dotnet run

# 打包为单文件 exe (开箱即用)
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

## 许可证

本项目基于 [MIT](LICENSE) 许可证开源。
