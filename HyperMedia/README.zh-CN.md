# HyperMedia

基于 libVLC 的 Windows 8.1 全格式媒体播放器，通过 Direct3D 11 SwapChain 实现硬件加速渲染。

## 功能特性

- **广泛格式支持** -- 通过 VLC 解码库支持 MP4、AVI、MKV、WebM、FLV、MOV、WMV、MP3、FLAC、WAV、AAC、OGG、WMA、M4A、3GP、TS、MKA、OPUS 等格式
- **硬件加速解码** -- D3D11VA 视频解码 + SwapChainPanel 渲染
- **键盘快捷键** -- 空格（播放/暂停）、左右方向键（快退/快进 ±10 秒）、上下方向键（音量 ±5%）、F（全屏）、Ctrl+O（打开文件）
- **触控和鼠标交互** -- 点击播放/暂停，移动指针显示控件
- **控件自动隐藏** -- 播放时 3 秒无操作自动隐藏界面

## 环境要求

- **系统**：Windows 8.1 或更高版本
- **SDK**：Windows 8.1 SDK
- **IDE**：Visual Studio 2015（v14）或更高版本
- **工具集**：v120（Visual C++ 2013）

## 构建方法

1. 在 Visual Studio 中打开 `HyperMedia.sln`
2. 选择 **Debug|x86** 或 **Debug|ARM** 配置
3. 生成解决方案（Ctrl+Shift+B）

项目已包含 x86、ARM、Win32（x86 桌面）的 libVLC 预编译二进制文件，无需额外构建步骤。

## 项目结构

```
HyperMedia/
  HyperMedia.Windows/            # C# Windows 8.1 应用商店应用（主播放器界面）
    MainPage.xaml/.cs            # 播放器页面，集成 libVLC
    HomePage.xaml/.cs            # 欢迎/起始页面
  HyperMedia.WindowsPhone/       # C# Windows Phone 8.1 应用（手机端占位）
HyperMedia.MediaCore/
  HyperMedia.MediaCore.Shared/   # C++/CX 原生组件
    LibVlcInterop.h/.cpp         # LibVlcManager 和 LibVlcDecoder WinRT 类
    vlc/                         # libVLC C 头文件
  HyperMedia.MediaCore.Windows/  # C++ WinRT 组件（桌面端）
  HyperMedia.MediaCore.WindowsPhone/ # C++ WinRT 组件（手机端）
libvlc/
  x86/                           # x86 原生二进制文件 + 插件
  ARM/                           # ARM 原生二进制文件 + 插件
  Win32/                         # Win32（x86 桌面）原生二进制文件 + 插件
```

## 架构说明

应用采用双层架构：

- **C# XAML UI** -- 负责文件选择、播放控制、进度条和用户交互
- **libVLCX** -- libVLC 的 WinRT 封装，提供可从 C# 调用的 `Instance`、`MediaPlayer`、`Media` 类型
- **libVLC** -- 原生解码和播放引擎，支持 D3D11 硬件加速

## 许可证

本项目采用 GNU 通用公共许可证 v2.0 -- 详见 [LICENSE](LICENSE)。

libVLC 版权所有 (c) VideoLAN，采用 GPL v2+ 许可证。
