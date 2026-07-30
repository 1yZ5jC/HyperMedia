# HyperMedia

A full-format media player for Windows 8.1, powered by libVLC with hardware-accelerated rendering via Direct3D 11 SwapChain.

## Features

- **Broad format support** -- plays MP4, AVI, MKV, WebM, FLV, MOV, WMV, MP3, FLAC, WAV, AAC, OGG, WMA, M4A, 3GP, TS, MKA, OPUS and more through VLC's codec library
- **Hardware-accelerated decoding** -- D3D11VA video decoding with SwapChainPanel rendering
- **Keyboard shortcuts** -- Space (play/pause), Left/Right (seek +/-10s), Up/Down (volume +/-5%), F (fullscreen), Ctrl+O (open file)
- **Touch and mouse interaction** -- tap to play/pause, pointer movement reveals controls
- **Auto-hiding controls** -- overlay UI hides after 3 seconds during playback

## Requirements

- **OS**: Windows 8.1 or later
- **SDK**: Windows 8.1 SDK
- **IDE**: Visual Studio 2015 (v14) or later
- **Toolset**: v120 (Visual C++ 2013)

## Building

1. Open `HyperMedia.sln` in Visual Studio
2. Select the **Debug|x86** or **Debug|ARM** configuration
3. Build the solution (Ctrl+Shift+B)

The project includes pre-built libVLC binaries for x86, ARM, and Win32 (x86 desktop). No external build steps are required.

## Project Structure

```
HyperMedia/
  HyperMedia.Windows/            # C# UWP app (main player UI)
    MainPage.xaml/.cs            # Player page with libVLC integration
    HomePage.xaml/.cs            # Welcome/landing screen
  HyperMedia.WindowsPhone/       # C# UWP app (phone stub)
HyperMedia.MediaCore/
  HyperMedia.MediaCore.Shared/   # C++/CX native components
    LibVlcInterop.h/.cpp         # LibVlcManager and LibVlcDecoder WinRT classes
    vlc/                         # libVLC C headers
  HyperMedia.MediaCore.Windows/  # C++ WinRT component (desktop)
  HyperMedia.MediaCore.WindowsPhone/ # C++ WinRT component (phone)
libvlc/
  x86/                           # x86 native binaries + plugins
  ARM/                           # ARM native binaries + plugins
  Win32/                         # Win32 (x86 desktop) native binaries + plugins
```

## Architecture

The app uses a two-layer architecture:

- **C# XAML UI** -- handles file picking, playback controls, seek bar, and user interaction
- **libVLCX** -- WinRT wrapper around libVLC providing `Instance`, `MediaPlayer`, and `Media` types callable from C#
- **libVLC** -- native decoding and playback engine with D3D11 hardware acceleration

## License

This project is licensed under the GNU General Public License v2.0 -- see [LICENSE](LICENSE) for details.

libVLC is Copyright (c) VideoLAN and licensed under GPL v2+.
