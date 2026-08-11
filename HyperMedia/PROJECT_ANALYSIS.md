# HyperMedia 项目分析文档

> 本文档记录 HyperMedia 的项目现状盘点、模块成熟度评估与潜在可开发功能清单，作为后续版本迭代的参考依据。

## 1. 项目概况

| 项目 | 内容 |
|---|---|
| 定位 | Windows 8.1 全格式媒体播放器（商店应用 / WinRT） |
| 技术栈 | C#/XAML 前端 + C++/CX MediaCore（libVLC 内核，D3D11 硬解） |
| 工具链 | Visual Studio 2015（v14），v120 工具集，Windows 8.1 SDK |
| 版本控制 | Git，35 个提交，最新里程碑 0.241（2026-08-08） |
| 架构 | 双层：C# UI（文件选择/播放控制/进度条）+ libVLCX 封装 + libVLC 原生引擎 |

### 解决方案结构

```
HyperMedia.sln
├─ HyperMedia.Shared            C# 共享工程 (.projitems)
│     IPlayerBackend / MediaElementBackend / VlcBackend(USE_LIBVLC)
│     PlayHistory / RecentItem / Visualizers / Fft / Converters / PlaybackEngineSettings
├─ HyperMedia.Windows           8.1 桌面 [WINDOWS_APP]  ← 主要功能载体
├─ HyperMedia.WindowsPhone      8.1 手机 [WINDOWS_PHONE_APP; USE_LIBVLC]  ← 精简版
└─ HyperMedia.MediaCore         C++/CX 组件（.vcxitems 共享源）
      ├─ HyperMedia.MediaCore.Windows        （桌面壳）
      ├─ HyperMedia.MediaCore.WindowsPhone   （手机壳）
      └─ LibVlcInterop           libVLC 加载器 + 软解码框架（帧读取/波形扫描/PCM 采集）
```

### 仓库状态异常项

- `ffmpeg/` 目录缺失（符号链接失效，git 状态异常）
- `MIGRATION.md`（UWP 迁移草案）与 `VlcBackend.cs` 等有未提交修改
- 待办：建 `.gitignore`（obj/、bin/、AppPackages/、*.user、UpgradeLog.htm）

## 2. 现有功能模块清单

### 2.1 桌面版（HyperMedia.Windows）

| 模块 | 主要文件 | 规模 | 说明 |
|---|---|---|---|
| 播放页面 | MainPage.xaml.cs | ~6,900 行 | 功能密度最高的单体页面（详见 2.3） |
| 首页 | HomePage.xaml.cs | ~1,720 行 | Panorama 分区、最近播放、媒体库、URL、识别入口 |
| 频谱引擎 | SpectrumEngine.cs | 599 行 | 三级数据源（实时解码/波形预扫/伪频谱）+ BPM + 波形缓存 |
| 可视化渲染 | Shared\Visualizers.cs | 385 行 | 7 种渲染器 + BGRA 像素绘制 |
| FFT | Shared\Fft.cs | 85 行 | 自写 radix-2 FFT + Hann 窗 |
| Shazam 识别 | Shazam*.cs ×3 | 639 行 | 提取→指纹→HTTP 识别全链路 |
| 元数据编辑 | MetadataEditor.cs | 231 行 | 网易/QQ 元数据查询 + 写 tag/封面 |
| 视频缩略图 | VideoThumbnailService.cs | 120 行 | 软解首帧→JPEG 缓存（SHA-1 键） |
| 性能画像 | PerformanceProfile.cs | 63 行 | CPU 基准评级 + 硬解能力分级 |
| 设置 | SettingsPage + CharmSettingsControl | ~970 行 | 17 项设置（两套 UI 同步维护） |
| 本地化 | AppText.cs | 542 行 | 中/英文案 |

### 2.2 共享层与 MediaCore

- **IPlayerBackend**（21 行）：最小抽象——`Play/Pause/Stop/Close`、`OpenPath/OpenUrl/OpenStream`、`Duration/Position/Volume/Rate`、`MediaOpened/MediaEnded/PlaybackFailed`
- **VlcBackend**：libVLCX 封装，Rate 0.25–4.0x 钳制，带初始化失败 HRESULT 诊断
- **PlayHistory**：最近播放（每类 6 条）、续播位置、评分、播放次数、URL 历史、播放列表库、智能列表
- **LibVlcDecoder**（C++/CX）：`OpenFile/ReadNextVideoFrame/ReadNextAudioFrame/CollectAudioPcm/ScanWaveform/SetPlayPause/SeekTo`
- **LibVlcManager**：`Initialize/Shutdown/GetHardwareDecodeGrade`（0–3 级硬解能力）

### 2.3 播放页能力全景（MainPage）

- 播放：打开文件/URL、上下曲、随机/循环、±10s/帧步进/滑杆 seek、自动连播同目录、续播恢复、挂起保存
- 字幕：内嵌轨切换、外挂（.srt/.ass/.ssa/.sub/.vtt→临时副本→重开媒体）、延迟、字号/颜色/描边/边距
- 音频：多音轨切换、音频延迟、输出设备、音量/静音
- 倍速：0.25–4.0x 循环切换
- 均衡器：10 段 EQ + 预设 + 自定义保存/加载
- 视频滤镜：亮度/对比度/色调/饱和度/伽马、宽高比、旋转、裁切、去隔行、夜间模式
- 手势：双击全屏/暂停、拖拽 seek/音量/亮度、触控倾斜音量
- 高级：A-B 断点重复（±帧微调）、书签、截图/连拍/录像、在线+内嵌歌词、照片查看器
- 系统：SMTC、动态磁贴、分享、媒体信息、快捷键帮助

### 2.4 手机版（HyperMedia.WindowsPhone）

精简版：MediaElement 引擎（VLC 默认关闭）、基础播放/列表/历史/睡眠定时器/伪频谱、网络设备发现（桌面已移除）。**缺失**：Shazam、元数据编辑、缩略图、均衡器、视频滤镜、外挂字幕、多音轨、SMTC、Charm 设置。媒体库三页（MediaLibrary/Playlists/OpenUrl）为其独有。

## 3. 模块成熟度评估

| 模块 | 评级 | 说明 |
|---|---|---|
| MainPage 播放/歌词/可视化集成 | ★★★★ | 功能成熟但单体架构风险高（~6,900 行，无 MVVM） |
| SpectrumEngine（三级源+BPM+缓存） | ★★★★ | 结构清晰，接口+实现+缓存分层 |
| Shazam 指纹算法 | ★★★★ | 完整移植，二进制格式+CRC 齐备 |
| Shazam 识别链路 | ★★★ | 依赖非官方端点，无兜底、无麦克风输入 |
| PlayHistory/PlaylistLibrary | ★★★ | 全 LocalSettings 字符串序列化，无数据库 |
| 设置（页面+Charm 双实现） | ★★★ | 两套 UI 有同步维护成本 |
| VlcBackend/IPlayerBackend | ★★ | 接口过简、OpenStream 空实现、抽象被绕过 |
| MetadataEditor/VideoThumbnailService | ★★ | 单点功能，在线依赖，无批量化 |

## 4. 薄弱环节

1. 后端接口抽象与 MainPage 直连 libVLCX 并存——字幕/音轨/EQ 均绕过 `IPlayerBackend`，更换引擎几乎不可能
2. 媒体库无索引/后台扫描；缩略图按文件单拉取
3. 书签单条且不持久化；A-B 循环不记忆；睡眠定时器仅分钟粒度
4. Shazam 无麦克风直连、无识别历史、结果动作单一
5. 字幕仅手动挑选，无在线下载；无音轨记忆偏好
6. 手机版与桌面版功能断层大，Shared 层复用不足
7. UWP 迁移（MIGRATION.md 草案）尚未推进

## 5. 潜在可开发功能（按与现有代码契合度排序）

| # | 功能 | 复用基础 | 难度 | 工作量估 | 改动范围 |
|---|---|---|---|---|---|
| 1 | 麦克风直连识别 | Shazam 全链路 + MediaCapture（8.1 支持） | ★★★★ | 4–7 天 | ShazamAudioExtractor 前段 |
| 2 | 在线字幕下载 | 外挂字幕临时副本流程 | ★★★ | 3–5 天 | SubtitleButton_Click |
| 3 | 媒体库索引+批量缩略图 | VideoThumbnailService + 仿 WaveCache 模式 | ★★★ | 3–5 天 | HomePage/新页 |
| 4 | 多书签持久化 + A-B 记忆 | ToggleBookmark/CheckAbRepeat + LocalSettings | ★ | 1–2 天 | MainPage |
| 5 | 均衡器预设库管理 | EqPresetCombo/LoadCustomEqPreset 骨架 | ★★ | 1–2 天 | MainPage + Settings |
| 6 | 倍速精调 + 记忆 | Rate 0.25–4x 已支持 | ★ | 0.5–1 天 | UI 层 |
| 7 | KRC/QRC 逐字歌词 | LRC 解析与同步渲染管线 | ★★☆ | 2–3 天 | 新增解析器 + MainPage（详见 §7） |
| 8 | 手势扩展 | Manipulation 框架 + AdjustVideoBrightness | ★★ | 1–2 天 | MainPage |
| 9 | 睡眠定时器增强（本曲/本集结束） | OnVlcEndReached 钩子 | ★ | 0.5–1 天 | MainPage |
| 10 | Shazam 结果联动（入列表/搜索/修正重试） | PlaylistLibrary + MetadataEditor | ★★ | 1–2 天 | HomePage |
| 11 | M3U 播放列表进度/重命名/排序 | Import/Export + SaveResumePosition 模型 | ★★☆ | 2–3 天 | Playlists 相关 |

**不建议**（8.1 硬约束下性价比低）：DLNA 投屏、后台播放、本地指纹库、broadFileSystemAccess 相关能力。

## 6. 难易顺序与风险维度

排序依据（易 → 难）：

1. **纯本地、单文件、骨架已存在**（0.5–2 天）：#6 倍速精调 → #9 睡眠定时器 → #4 书签/A-B 持久化
2. **本地但跨组件**（1–2 天）：#8 手势扩展 → #10 Shazam 联动 → #5 EQ 预设库（底层结构都在，主要拼 UI）
3. **需新解析器/动序列化模型**（2–3 天）：#7 逐字歌词 → #11 M3U 增强
4. **外部服务依赖**（3–5 天，#2/#3）：字幕站 API 登录/token、GBK 编码、8.1 内置解压不便（#2）；批量软解缩略图吃 CPU 需节流、首页网格 UI 重构（#3）
5. **实时链路 + 真机调试**（4–7 天）：#1 麦克风识别——WinRT 音频线程 GC 风险、设备差异、无模拟器可测

风险维度：
- **零外部依赖**（#6/9/4/8/5/7）：回归可控，逢改动可立刻验证
- **外部服务强依赖**（#2/10）：API 或站点变更即失效，必须做失败兜底
- **性能敏感**（#1/3）：需按 PerformanceProfile 分级降级（限倍速/降帧）

建议顺序（三周线）：
- **第一周**：#6 → #9 → #4（快速见效，单文件）
- **第二周**：#8 → #10 → #5 → #7
- **第三周**：#11 → #2 → #3
- **独立里程碑**：#1 留足调试时间

## 7. 逐字歌词（KRC/QRC）实现分析

### 7.0 前期准备已落地（2026-08）

两个新模块已加入 Windows 工程并通过 Debug/Release x86 构建：

- **LyricParsers.cs**：`LyricFormat` 枚举、`WordSeg`/`LyricLineModel` 纯数据模型、`TryDecryptKrc`（XOR key 表解密 + 魔数校验）、`ParseKrc`（`[start,dur]字<dur>` 逐字时长累加）、`ParseQrc`（`<开始:结束>`/`<开始,结束>` 双兼容）、`ParseLrc`（从 MainPage 移植，行为一致）、`DetectFormat` 自动探测
- **LyricMatcher.cs**：`LyricCandidate` 候选模型（Source/SongId/Title/Artist/Aliases/StrictMatch/Score/Order/Format/LyricText）、`NormalizeTitle`、`EditDistance`、`ScoreName`/`ScoreTrack` 评分（精确=1.0 > 别名=0.92 > 包含=0.8 > 模糊=0.6）
- **MainPage 集成**：`QueryNeteaseLyric`/`QueryQqLyric` 重构为「候选收集 + 严格匹配包装」（行为与旧版等价：严格候选按数组序取第一个有歌词者）；`TryLoadOnlineLyrics` 改为**合并双源候选**，引入「单候选自动用 / 多候选弹选择器」分流；候选选择器（MenuFlyout：来源徽标 + 曲名 + 艺人 + 推荐标记）支持**点击切换歌词**并按文件记忆上次选择（`Settings_LyricChoice_<文件名>`）；歌词头部新增「选择」按钮
- **XAML/本地化**：`LyricPickBtn` 按钮（多候选时显示）+ 4 个 AppText 键

后续只需：候选行填入 `Words` 后做双层 Clip 渲染（见 7.3 步骤 4），解析器与数据模型已就绪。

### 7.1 现有 LRC 管线（已具备，可直接扩展）

```
DisplayLyrics(string)          # 接受原始文本（行 3094）
  └─ ParseLrc(text)            # [MM:SS.xx]文本 逐行解析，按时间排序（行 3203）
       → List<LyricLine>       # TimeMs / Text / Container / UiElement / TimeIndicator（行 179）
  ├─ 渲染行：TextBlock + 时间列 + 强调条，放入 LyricsLines(StackPanel)（行 3119-3179）
  └─ LyricTimer_Tick（100ms DispatcherTimer）：
       反向扫描找当前行 idx → 仅当 idx 变化时整行样式切换（行 3260-3313）
```

现状局限：`LyricTimer_Tick` 只在**行切换时**刷样式（`if (idx == _currentLyricIndex) return;`），行内无进度；渲染是"整行变白/变灰"的二态，不适合逐字。

### 7.2 词级时间戳格式

| 格式 | 来源 | 结构 | 备注 |
|---|---|---|---|
| KRC | QQ 音乐 | XOR 解密（key 表 64 字节，首 4 字节 `krc` 魔数）→ 头 `[ti:]/[ar:]/[offset:]` → 行 `[start,dur]字<dur>字<dur>...` | 每字后跟**该字显示时长(ms)**；无 `<n>` 的纯文本行则整行均分 |
| QRC | 网易云 | `[offset:]` + 行 `[MM:SS.xx]文本`，词级标签 `<开始:结束>`（或 `<开始,结束>`，mm:ss.xx） | 词对起止时间；网易仅部分歌曲提供词级（新接口 wordLyric） |

### 7.3 实现方案（按数据流）

1. **数据结构**：`LyricLine` 增加 `List<WordSeg> Words`（`public double StartMs; public double EndMs; public string Ch;`）；无词级数据的行保持 `Words == null`（纯 LRC 行为兼容）。
2. **解析器**（新增 `LyricParsers.cs`，Shared 或 Windows 工程）：
   - `TryDecryptKrc(byte[] data)`：验证魔数 → XOR 循环 key → UTF-8 文本
   - `ParseKrc(string)`：`[start,dur]` 行解析，逐字累加得 `WordSeg.StartMs`（= 行起始 + 前面各字时长之和）
   - `ParseQrc(string)`：复用现有 `ParseLrcTimestamp` 思路解析 `<s:e>` 标签成 `WordSeg`
   - 复用现有 `ParseLrc`；`DisplayLyrics` 增加格式自动探测（出现 `krc` 魔数 / 行内 `<数字>` / `<mm:ss.xx>` 即走词级路径）
3. **加载器扩展**（`TryLoadOnlineLyrics`，行 2681）：QQ 接口请求 `format=krc`（或换 `lrc=1` 拿词级字段）、网易 `lyric` 接口读词级字段；保存原始字节而非字符串（KRC 是二进制）。Shazam 兜底链路（行 2735）不变。
4. **逐字渲染**（核心改动，`LyricTimer_Tick`）：
   - 当前行有 `Words` 时：把当前行文本行改为**双层叠加**——底层整行暗色 FontSize=19，上层整行白色同坐标（同一 StackPanel 内对齐），上层加 `Rectangle Clip`（宽度 = 完成比例 × 行宽）
   - 行宽测算：行加载时 `TextBlock.Measure(无限大小)` 一次缓存；每个 tick 只改 Clip 宽度，**无文本重排**，性能可控
   - 300ms 平滑：Clip 宽度按上一字/下一字间距做线性插值（可选）；行切换瞬间快进
   - 独立快进度：词级行激活时把 `_lyricTimer` 从 100ms 临时提到 ~33ms（或第二个高精度定时器），仅当前行需要
5. **兼容规则**：词级数据缺失/解析失败 → 自动回落现有整行高亮路径（`Words==null` 分支原样保留）；KRC 行首 `<0>` 幽灵像素宽处理、QRC 的 offset 头应用与现有 `ParseLrcTimestamp` 一致。

### 7.4 风险与工作量

- 解析器本体简单（半天）；**难点在双层 Clip 渲染与 33ms 定时极致优化**（1 天）
- 词级数据覆盖率受上游限制（QQ KRC 广；网易仅部分歌曲），需设计"词级优先、整行兜底"自动切换
- 8.1 无 Composition API，Clip 近似即可达卡拉 OK 效果；总计 **2–3 天**

## 8. 建议的开发路线

1. 清理仓库状态：修复 `ffmpeg/` 链接、提交 MIGRATION.md、补 `.gitignore`
2. 优先实现高契合度低风险项：#6 倍速记忆、#4 书签/A-B 持久化、#9 睡眠定时器
3. 中期：#7 逐字歌词（解析器已就绪，补渲染）、#8 手势扩展、#10 Shazam 联动、#5 EQ 预设库
4. 后期：#11 M3U 增强、#2 在线字幕、#3 媒体库索引；#1 麦克风识别独立排期
5. 架构健康化：扩充 `IPlayerBackend`（字幕/音轨/EQ 上移），为引擎替换留路
6. 按 MIGRATION.md 路线评估 UWP 迁移，作为独立线推进

## 9. 歌词候选选择与英文名匹配（本期新增）

### 9.1 候选选择的判定规则（已实现）

| 场景 | 行为 |
|---|---|
| 仅 1 个可用候选（有歌词文本） | 自动使用，不弹窗（旧行为），「选择」按钮仍显示可换 |
| ≥2 个可用候选（跨网易/QQ 或同源多版本） | 弹出选择器，用户点选即切换并记忆 |
| 存在记忆的 `Source\|SongId` 且候选命中 | 直接使用记忆项，不弹窗 |
| 无任何可用候选 | 沿旧链路走 Shazam 兜底 |

选择器界面（`LyricPickerOverlay`，右侧滑出面板，与均衡器/滤镜面板同风格）：标题 + 当前曲目名副标题 + 候选列表（`[网易云]/[QQ]` 徽标 + 曲名 - 艺人 + 「推荐」/「逐字」标记，点击即应用并记忆），底部「重新搜索」（重走 LoadLyrics 全链路）与「关闭」按钮；Esc 可关闭。逐字歌词候选附加 `- 逐字` 徽标；选择后 Overlay 提示「已切换歌词」。

**明显入口（双入口）**：① 歌词面板头部「选择歌词」粉色按钮（ZunePrimaryStyle，常显，无候选时点开显示引导 + 重新搜索）；② 控制栏右侧 ♪ 按钮（仅音乐模式显示，与「歌词」切换按钮同组）。任一入口都打开同一选择面板。

**多版本选择（卡拉 OK 场景）**：候选探测已放宽——严格候选全部探测（数组序），非严格候选（Live / instrumental / 翻唱 / 混音等带版本标签的条目）也按分数降序全部探测，3 路并发批量抓歌词，收集满 6 个有歌词候选即停。因此播放器乐版（文件名带 "(Instrumental)"）时，原版歌词作为高分散候选必然进池：单候选自动配对（器乐版 + 原版歌词 = 卡拉 OK），多候选弹选择器供手动挑；反向（播放原版挑 Live 版歌词）同样支持。

### 9.2 识曲得到英文名时如何找到正确曲子

Shazam 返回的 Title/Subtitle 常为罗马化/英文名（如 "Xue Zhiqian - The Actor"），通过四级策略定位中文曲目：

1. **搜索引擎原生能力**：把 `artist + " " + title`（英文原样）直接传给网易/QQ 搜索——两家后端本就做拼音/别名模糊检索，通常返回正确中文条目
2. **候选别名交叉匹配**：网易搜索结果带 `alias` 字段（常含英文名/拼音名），`ScoreTrack` 用 0.92 权重与其比对——英文查询命中中文候选的别名即高分
3. **宽松评分代替硬匹配**：旧代码 `TitlesMatch` 严格相等才收候选，英文名对中文名必失败；新评分体系把「别名=0.92 > 包含=0.8 > 编辑距离≤2=0.6」的候选也收进来排序
4. **人工兜底**：匹配分不可靠时（≤2 个候选且分差小）弹出选择器，用户肉眼挑正确版本——识别结果即时可验证

局限与对策：8.1 无内置拼音转换，**不做本地汉字→拼音表**（体积大且收益低），完全依赖搜索引擎 + alias + 人工选择；后续可把 Shazam 识别结果的 `Key`（曲目 ID）存入候选，未来若有官方/稳定映射接口可直接拉取中文名。

### 9.3 元数据驱动的歌词检索增强（本期新增）

| 增强 | 说明 |
|---|---|
| 专辑元数据参与 | 新增 `_musicAlbum` 字段（来自 libVLC `MediaMeta.Album`）；候选评分 `ScoreTrack` 增加专辑维度——查询专辑与候选专辑归一化匹配时总分 +0.1（封顶 1.0），Live/精选集等版本排序更准 |
| 搜索词组合回退 | 单次搜索失败自动换词：① 艺人+歌名 → ② 歌名+专辑 → ③ 清理后歌名，任一命中即停（`BuildSearchQueries`） |
| 歌名清理 | `CleanSearchTitle` 裁掉 `(feat./(ft/(with/ ft./- live/- remix/(live/(instrumental/(伴奏` 前缀与全部括号块——"演员 (feat. xxx)" 类标签不再拖累命中 |
| 复合元数据拆分 | `NowPlaying` 标签常为 "艺人 - 歌名" 复合串且无独立 Artist 标签——艺人缺失且标题含 ` - ` 时复用文件名解析器拆分后再搜索 |
| 候选展示增强 | 选择器条目显示 `曲名（专辑） - 艺人`，版本分辨更直观 |

效果：标签不完整（只有专辑、或 NowPlaying 复合、或带 feat. 后缀）的文件，在线歌词命中率显著提升；专辑相符的版本在候选排序中前置。