# HyperMedia — 项目约定（AGENTS.md）

Windows 8.1 商店应用（WinRT）媒体播放器，C# (XAML) + C++/CX MediaCore (libVLC)。

## 平台硬约束（最重要）

- **目标 SDK 是 Windows 8.1**（`TargetPlatformVersion=8.1`，OSMinVersion 6.3）。一切 API 必须以 **Windows Kits\8.1 的 Windows.winmd** 为准，**不得使用 Win10 才有的 API**。
- 以下常用 API 是 **Win10 才有，8.1 编译不过**（遇到即报 CS0117）：
  - `ApplicationView.TryEnterFullScreenMode / ExitFullScreenMode`（8.1 无系统全屏 API；"全屏"= 隐藏控制栏模拟）
  - `Launcher.LaunchFolderAsync`（8.1 无"在资源管理器中显示文件夹"API；做法：弹窗显示路径让用户复制）
  - `broadFileSystemAccess` 完全文件访问（8.1 无；用 FutureAccessList + 库权限 + Picker）
  - 从资源管理器拖文件进窗口（8.1 不支持，已删除拖放功能）
  - 后台播放/锁屏播放（8.1 挂起即冻结；已实现"挂起保存 → 恢复自动续播"替代）
  - `System.Security.Cryptography`（8.1 用 `Windows.Security.Cryptography.Core`）
  - 若拿不准某 API 的 8.1 可用性，先检查 `C:\Program Files (x86)\Windows Kits\8.1\References\CommonConfiguration\Neutral\Windows.winmd` 是否含该方法，再写代码。

## 关键架构事实

- 播放核心：MainPage 用 libVLC（MediaCore `LibVlcManager`/`LibVlcDecoder`），文件先 `CopyAsync` 到 `TemporaryFolder` 再交给 libVLC（沙箱限制）。
- 权限：FutureAccessList token `"PlaybackFile"` 用于跨页面/恢复会话；LocalSettings 存设置与断点。
- 视频软解帧格式 `RV32`（BGRA）。缩略图缺失时 `VideoThumbnailService` 用 `LibVlcDecoder` 软解首帧缓存 JPEG 到 `LocalFolder\ThumbCache`。
- 性能：`PerformanceProfile` 用 CPU 基准评级（Low/Medium/High 限倍速与录像）；`LibVlcManager.GetHardwareDecodeGrade()` 检测 D3D11 硬解能力（0=无/1=H264/2=+HEVC8/3=+HEVC10），仅展示不影响评分。
- 本地化：所有文案走 `AppText` 键（中/英），`L("Key")` 读取。

## 构建与验证

- 命令行构建（工作目录不限）：
  `& "C:\Program Files (x86)\MSBuild\14.0\Bin\MSBuild.exe" "C:\Users\Alan\Documents\Visual Studio 2015\Projects\HyperMedia\HyperMedia\HyperMedia.Windows\HyperMedia.Windows.csproj" /p:Configuration=Debug /p:Platform=x86 /t:Build /nologo /verbosity:minimal /p:OutDir="C:\Users\Alan\Documents\Visual Studio 2015\Projects\HyperMedia\HyperMedia\bin\Debug\x86\"`
- 改完必须同时构建 Debug 与 Release x86。
- 常见坑：`obj\x86` 的过时 .g.cs 会导致伪编译错误，删除 `obj\x86`/`bin\x86` 再重建。
- MediaCore 的 WACK 通过前提：WindowsAppContainer=true、不显式链接桌面 msvcrt/msvcp lib、不引用 `VC\lib` 桌面目录。
