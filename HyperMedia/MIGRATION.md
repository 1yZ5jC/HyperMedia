# HyperMedia UWP 迁移方案（草案）

> 本次讨论的目标：新增 UWP 目标，同时保持 Windows 8.1（桌面 + 手机）的活跃开发。
> 文档记录"迁移准备"分析结论，供后续按路线逐步执行。

## 1. 现状盘点

```
HyperMedia.sln
├─ HyperMedia.Shared          C# 共享工程 (.projitems)
│     IPlayerBackend / MediaElementBackend / VlcBackend(USE_LIBVLC)
│     PlayHistory / RecentItem / Visualizers / Fft / Converters / PlaybackEngineSettings
├─ HyperMedia.Windows         8.1 桌面   [WINDOWS_APP]
├─ HyperMedia.WindowsPhone    8.1 手机   [WINDOWS_PHONE_APP; USE_LIBVLC]
└─ HyperMedia.MediaCore       C++/CX 组件（vcxitems 共享源）
      ├─ HyperMedia.MediaCore.Windows        （桌面壳）
      ├─ HyperMedia.MediaCore.WindowsPhone   （手机壳）
      └─ LibVlcInterop（libVLC 加载器；手机模拟器路线已确认不通）
```

- 共享层已有条件编译：`#if WINDOWS_APP`（RecentItem.cs）、`#if WINDOWS_PHONE_APP`（PlaybackEngineSettings.cs）。
- 页面级是两套并行实现（HomePage / MainPage / SettingsPage × 桌面 + 手机）。
- 条件符号：Phone = `DEBUG;TRACE;NETFX_CORE;WINDOWS_PHONE_APP;USE_LIBVLC`；
  桌面 = `DEBUG;TRACE;NETFX_CORE;WINDOWS_APP`。

## 2. 迁移难点清单（代码实测）

| 8.1 特有 API | 位置 | UWP 替代 |
|---|---|---|
| `HardwareButtons.BackPressed` | App.xaml.cs:67 OnBackPressed | `SystemNavigationManager.BackRequested` |
| `FileOpenPickerContinuation` / FolderPickerContinuation | App.xaml.cs:126-144 及页面 | UWP 同步 `await` 版本（无 Continuation） |
| `MediaElement`（8.1 语义/事件） | MediaElementBackend + 各页 | 基本兼容，事件差异小 |
| `SwapChainPanel`（VlcBackend 渲染面） | Shared/VlcBackend.cs | UWP 存在；libVLC 桥接方式不同 |
| CommandBar / AppBarButton | 各页 BottomAppBar | 兼容；视觉样式差异大 |
| 语义缩放（ScrollViewer + OverviewView） | HomePage | 控件兼容 |
| WriteableBitmap 像素绘制 | Visualizers / Fft | 兼容（WritePixels 均可用） |
| C++/CX MediaCore 壳工程 | MediaCore.*.vcxproj | 需新增 Win10 SDK 的 UWP 壳工程 |
| libVLCX.winmd + libVLCX.8.1.Windows.dll | 各 csproj 引用（Implementation） | 手机版已判死；UWP 改用官方 libVLC UWP 包重写 VlcBackend |

## 3. 推荐架构

单仓库 + 共享 .projitems/.vcxitems + 三目标：

```
HyperMedia.Shared (.projitems)   ← 8.1桌面 / 8.1手机 / UWP 引用同一份
├─ 目录与播放逻辑（现有，基本原样保留）
└─ PlatformServices/   （新增抽象层 —— 迁移的核心投入）
     ├─ IFilePickerService   （Continuation vs await）
     ├─ IBackNavigation     （HardwareButtons vs BackRequested）
     ├─ ISettingsStore / ISystemInfo（按需展开）
     ├─ Phone 8.1 实现 / Desktop 8.1 实现（由现有代码迁入）
     └─ UWP 实现（新建）
├─ HyperMedia.Windows        （8.1 桌面，继续活跃）
├─ HyperMedia.WindowsPhone   （8.1 手机，继续活跃）
├─ HyperMedia.UWP            （新增）
└─ HyperMedia.MediaCore.UWP  （新增，C++/CX Win10 SDK 壳）
```

原则：页面保持 code-behind 现状，不做 MVVM 重构；平台差异全部收口到 PlatformServices。

## 4. 分阶段路线

1. **版本控制**：项目目录纳入 git（当前非仓库）——三目标共存与试错的前提。
2. **平台服务抽象**：把续传式 FilePicker、硬件返回、设置存储迁入 PlatformServices 接口；
   8.1 双工程改为调用接口（行为不变，回归绿为里程碑）。
3. **条件符号规范化**：引入 `WINDOWS_UWP`；现有 `WINDOWS_APP` 改写为
   `#if WINDOWS_APP && !WINDOWS_UWP` 风格，避免桌面代码被 UWP 误编译。
4. **UWP 壳工程脚手架**：HyperMedia.UWP（引用同 .projitems）+ MediaCore.UWP
   （引用同 .vcxitems，Win10 SDK），先编译通过"空壳 + Shared 纯逻辑"
   （PlayHistory / RecentItem / Fft / Converters）。
5. **逐页适配**：先迁 HomePage；UWP 的 CommandBar/AppBar 样式单独处理；
   `USE_LIBVLC` 在 UWP 首期关闭（MediaElement 引擎顶上）。
6. **回归纪律**：每次改动三目标构建；8.1 模拟器 + 8.1 桌面 + UWP 三线冒烟。

## 5. 风险与决策点

- VLC 在 UWP 上应视为**独立重构**（官方 libVLC UWP 包 API 与 8.1 wrapper 差异大），不阻塞 UWP 首期。
- VS2015 + Windows 10 SDK 可建 UWP（需确认本机 SDK 已装）；C++/CX 在 UWP 继续可用。
- CommandBar/AppBar、App.xaml 的 Zune 风格资源三线各自维护，不做跨平台资源抽象。
- 8.1 手机 libVLC 路线已关闭：HyperMedia.WindowsPhone 应同时把引擎默认值收敛到
  MediaElement（或保留 VLC 选项但标记不可用），避免新用户踩 0x8007007E/0x80040154。

## 6. 后续待办（依序）

- [ ] git init + 首次提交（建 .gitignore：obj/、bin/、AppPackages/、*.user、UpgradeLog.htm 等）
- [ ] 盘点页面/App 中所有 8.1 特有 API 调用点（上表为初步清单，需逐文件核对）
- [ ] 设计并落地 PlatformServices 接口 + 8.1 双实现
- [ ] 条件符号矩阵落地并全量构建验证
- [ ] 建 UWP 壳工程，跑通"空壳 + Shared 纯逻辑"最小构建
- [ ] HomePage 迁 UWP 并冒烟