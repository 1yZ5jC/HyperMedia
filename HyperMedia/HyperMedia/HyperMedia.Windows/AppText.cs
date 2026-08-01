using System;
using System.ComponentModel;
using System.Collections.Generic;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace HyperMedia
{
    public sealed class AppText : INotifyPropertyChanged
    {
        private const string KEY_LANGUAGE = "Settings_Language";
        private string _language = "zh-CN";
        private Dictionary<string, Dictionary<string, string>> _strings;

        public AppText()
        {
            LoadLanguage();
            BuildDictionary();
        }

        public string Language
        {
            get { return _language; }
            set
            {
                if (_language == value) return;
                _language = value;
                try { ApplicationData.Current.LocalSettings.Values[KEY_LANGUAGE] = value; }
                catch { }
                RaiseAll();
                var handler = LanguageChanged;
                if (handler != null) handler(this, EventArgs.Empty);
            }
        }

        public bool IsEnglish { get { return _language != null && _language.StartsWith("en"); } }

        private void LoadLanguage()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                if (settings.Values.ContainsKey(KEY_LANGUAGE))
                    _language = settings.Values[KEY_LANGUAGE] as string;
                if (string.IsNullOrEmpty(_language)) _language = "zh-CN";
            }
            catch { }
        }

        public string T(string key)
        {
            Dictionary<string, string> table;
            if (_strings != null && _strings.TryGetValue(key, out table))
            {
                string v;
                if (table.TryGetValue(_language, out v) && !string.IsNullOrEmpty(v))
                    return v;
                if (table.TryGetValue("zh-CN", out v))
                    return v;
            }
            return key;
        }

        private void BuildDictionary()
        {
            _strings = new Dictionary<string, Dictionary<string, string>>();
            Add("AppName", "HyperMedia", "HyperMedia");
            // Home
            Add("OpenFile", "打开文件", "Open File");
            Add("OpenUrl", "打开网址", "Open URL");
            Add("Library", "媒体库", "Library");
            Add("Playlists", "歌单", "Playlists");
            Add("ClearHistory", "清除历史", "Clear History");
            Add("Settings", "设置", "Settings");
            Add("Overview", "概览", "Overview");
            Add("Back", "返回", "Back");
            Add("Videos", "视频", "Videos");
            Add("Music", "音乐", "Music");
            Add("Photos", "图片", "Photos");
            Add("RecentPlayed", "最近播放", "Recent");
            Add("DragHint", "或将文件拖入窗口", "or drag files here");
            Add("ScrollHint", "滚动浏览", "Scroll to explore");
            Add("Tagline1", "全部播放", "PLAY ALL");
            Add("Tagline2", "随心所欲", "YOUR WAY");
            Add("TaglineDesc", "视频、音频、图片 — 一个播放器搞定所有媒体。",
                "Video, audio and photos — one player for all your media.");
            Add("TaglineSupport", "支持几乎所有媒体格式", "Supports almost every media format");
            Add("NoPlaylists", "在播放器中点击保存歌单后显示", "Playlists you save will appear here");
            Add("SmartPlaylists", "智能播放列表", "Smart Playlists");
            Add("TopRated", "★ 高评分", "★ Top Rated");
            Add("MostPlayed", "🔥 播放最多", "🔥 Most Played");
            Add("RecentlyPlayedSmart", "🕘 最近播放", "🕘 Recent");
            Add("NetworkDevices", "局域网设备 (DLNA/UPnP)", "Network Devices (DLNA/UPnP)");
            Add("Scanning", "正在扫描局域网...", "Scanning network...");
            Add("NoDevices", "未发现设备", "No devices found");
            Add("BrowseFolder", "浏览文件夹媒体", "Browse folder media");
            Add("AddFolder", "添加文件夹浏览", "Add a folder");
            Add("SaveAsPlaylist", "保存为歌单", "Save as Playlist");
            Add("PlaylistName", "歌单名称（如：周杰伦精选）", "Playlist name (e.g. My Favorites)");
            Add("Save", "保存", "Save");
            Add("Cancel", "取消", "Cancel");
            Add("Close", "关闭", "Close");
            Add("Play", "播放", "Play");
            Add("DeletePlaylist", "删除歌单", "Delete Playlist");
            Add("PinToStart", "固定到开始屏幕", "Pin to Start");
            Add("OpenNetworkMedia", "打开网络媒体", "Open Network Media");
            Add("RecentOpened", "最近打开", "Recent");
            // Settings
            Add("SettingsPlayback", "播放", "Playback");
            Add("SettingsAppearance", "外观", "Appearance");
            Add("SettingsLibrary", "媒体库", "Library");
            Add("SettingsAbout", "关于", "About");
            Add("Language", "语言", "Language");
            Add("DefaultVolume", "默认音量", "Default Volume");
            Add("AutoPlay", "自动播放", "Auto Play");
            Add("ResumePlayback", "断点续播", "Resume Playback");
            Add("AutoHideControls", "自动隐藏控制栏", "Auto-hide Controls");
            Add("AutoHideDelay", "自动隐藏延迟", "Auto-hide Delay");
            Add("SubtitleSize", "字幕字号", "Subtitle Size");
            Add("SubtitleColor", "字幕颜色", "Subtitle Color");
            Add("SubtitleMargin", "字幕位置偏移", "Subtitle Position");
            Add("SubtitleOutline", "字幕描边", "Subtitle Outline");
            Add("Deinterlace", "去隔行模式", "Deinterlace");
            Add("Loudness", "响度标准化", "Loudness Normalization");
            Add("EpisodeMode", "同目录连播（剧集模式）", "Same-folder Playback");
            Add("IntroSkip", "自动跳过片头", "Auto Skip Intro");
            Add("LightTheme", "浅色主页", "Light Home");
            Add("SleepTimer", "睡眠定时器", "Sleep Timer");
            Add("ClearResumePositions", "清除续播位置", "Clear Resume Positions");
            Add("ClearPlayHistory", "清除播放历史", "Clear Play History");
            Add("Clear", "清除", "Clear");
            Add("Version", "版本 1.0.0", "Version 1.0.0");
            Add("BasedLibVlc", "基于 libVLC 驱动", "Powered by libVLC");
            Add("GplLicense", "采用 GPL v2 许可证", "GPL v2 licensed");
            Add("BackToHome", "返回主页", "Back to Home");
            // Player tooltips
            Add("ZoomIn", "放大", "Zoom In");
            Add("ZoomOut", "缩小", "Zoom Out");
            Add("ResetZoom", "重置缩放", "Reset Zoom");
            Add("Rotate", "旋转", "Rotate");
            Add("Slideshow", "幻灯片", "Slideshow");
            Add("Close", "关闭", "Close");
            Add("PrevImage", "上一张", "Previous");
            Add("NextImage", "下一张", "Next");
            Add("SaveEqCustom", "保存当前设置为自定义预置", "Save current as custom preset");
            Add("Subtitles", "字幕", "Subtitles");
            Add("AudioTrack", "音轨", "Audio Track");
            Add("MediaInfo", "媒体信息", "Media Info");
            Add("Screenshot", "截图", "Screenshot");
            Add("RecordVideo", "录制当前视频 (转码保存)", "Record current video (transcode)");
            Add("AspectRatio", "画面比例", "Aspect Ratio");
            Add("VideoRotation", "视频旋转", "Rotate Video");
            Add("Crop", "画面裁剪", "Crop");
            Add("NightMode", "夜间护眼模式", "Night Mode");
            Add("Rating", "媒体评分", "Rating");
            Add("AvSync", "音画同步", "A/V Sync");
            Add("AudioDevice", "音频输出设备", "Audio Device");
            Add("Bookmarks", "书签 (Ctrl+B 添加 / Ctrl+Shift+B 查看)", "Bookmarks (Ctrl+B add / Ctrl+Shift+B view)");
            Add("Equalizer", "均衡器", "Equalizer");
            Add("VideoFilter", "视频滤镜", "Video Filters");
            Add("Chapter", "章节", "Chapters");
            Add("StatsOsd", "播放统计 (Ctrl+I)", "Playback Stats (Ctrl+I)");
            Add("OpenFolder", "打开文件所在目录 (Ctrl+E)", "Open Containing Folder (Ctrl+E)");
            Add("PlaybackSpeed", "播放速度", "Playback Speed");
            Add("Fullscreen", "全屏 (F)", "Fullscreen (F)");
            Add("Prev", "上一个", "Previous");
            Add("PlayPauseTip", "播放 / 暂停 (空格)", "Play / Pause (Space)");
            Add("Stop", "停止", "Stop");
            Add("Next", "下一个", "Next");
            Add("RepeatTip", "循环 (关 / 全部 / 单曲)", "Repeat (Off / All / One)");
            Add("Shuffle", "随机播放", "Shuffle");
            Add("Mute", "静音 / 取消静音", "Mute / Unmute");
            Add("Playlist", "播放列表", "Playlist");
            Add("SaveAsPlaylistBtn", "保存为歌单", "Save as Playlist");
            Add("ImportM3u", "导入 M3U 播放列表", "Import M3U Playlist");
            Add("ExportM3u", "导出 M3U 播放列表", "Export M3U Playlist");
            Add("ClearPlaylist", "清空播放列表", "Clear Playlist");
            // Player status messages
            Add("PlaybackComplete", "播放完毕", "Playback finished");
            Add("PlaybackError", "播放错误", "Playback error");
            Add("SleepTimerStopped", "睡眠定时器: 播放已停止", "Sleep timer: playback stopped");
            Add("PlaylistCleared", "播放列表已清空", "Playlist cleared");
            Add("RecordingStarted", "开始录制", "Recording started");
            Add("RecordingSaved", "录制已保存", "Recording saved");
            Add("RecordingFailed", "录制完成，但保存到视频库失败", "Recording done, but failed to save to Videos library");
            Add("ScreenshotSaved", "截图已保存", "Screenshot saved");
            Add("ScreenshotFailed", "截图失败", "Screenshot failed");
            Add("LoadingMedia", "正在加载", "Loading");
            Add("Preparing", "正在准备...", "Preparing...");
            Add("Connecting", "正在连接...", "Connecting...");
            Add("LoadingStream", "正在加载流媒体...", "Loading stream...");
            Add("ResumeRestored", "已恢复播放", "Resume restored");
            Add("IntroSkipped", "已自动跳过片头", "Intro skipped");
            Add("NoMedia", "没有正在播放的媒体", "No media playing");
            Add("NetworkNoRecord", "网络流不支持录制", "Recording not supported for streams");
            Add("NetworkNoRotate", "网络流不支持旋转", "Rotation not supported for streams");
            Add("NetworkNoSnapshot", "网络流不支持截图", "Screenshots not supported for streams");
            Add("NoBookmarks", "无书签 (Ctrl+B 添加)", "No bookmarks (Ctrl+B to add)");
            // Player runtime messages
            Add("LoadingPrefix", "正在加载 ", "Loading ");
            Add("PlaylistEmptyNoSave", "播放列表为空，无法保存", "Playlist is empty, cannot save");
            Add("PlaylistSavedPrefix", "歌单已保存: ", "Playlist saved: ");
            Add("PlaylistSaveFailed", "歌单保存失败", "Failed to save playlist");
            Add("PlaylistExportedPrefix", "播放列表已导出: ", "Playlist exported: ");
            Add("ExportFailedPrefix", "导出失败: ", "Export failed: ");
            Add("ImportedPrefix", "已导入 ", "Imported ");
            Add("ImportedFilesSuffix", " 个文件", " files");
            Add("NoPlayableFiles", "未找到可播放的文件", "No playable files found");
            Add("ImportFailedPrefix", "导入失败: ", "Import failed: ");
            Add("PointAPrefix", "A 点: ", "Point A: ");
            Add("PointBPrefix", "B 点: ", "Point B: ");
            Add("ScreenshotNotReady", "截图失败: 播放器未就绪", "Screenshot failed: player not ready");
            Add("ScreenshotNoNetwork", "截图失败: 网络流不支持截图", "Screenshot failed: not supported for streams");
            Add("TakingScreenshot", "正在截图...", "Taking screenshot...");
            Add("ScreenshotFailedPrefix", "截图失败: ", "Screenshot failed: ");
            Add("BurstNotAvailable", "截图连拍不可用", "Burst capture unavailable");
            Add("BurstPrefix", "连拍 ", "Burst ");
            Add("BurstSuffix", " 张...", " shots...");
            Add("BookmarkRemovedPrefix", "书签已移除: ", "Bookmark removed: ");
            Add("BookmarkAddedPrefix", "书签已添加: ", "Bookmark added: ");
            Add("BookmarkHintSuffix", " (Ctrl+B 查看)", " (Ctrl+B to view)");
            Add("ResumeRestoredPrefix", "已恢复播放 ", "Resumed at ");
            Add("IntroSkippedPrefix", "已自动跳过片头 → ", "Intro skipped -> ");
            Add("OpenImageFailedPrefix", "打开图片失败: ", "Failed to open image: ");
            Add("SubtitlesOff", "字幕已关闭", "Subtitles off");
            Add("SwitchedPrefix", "已切换: ", "Switched: ");
            Add("SubtitleLoadedPrefix", "字幕已加载: ", "Subtitle loaded: ");
            Add("SubtitleErrorPrefix", "字幕错误: ", "Subtitle error: ");
            Add("AudioDelayPrefix", "音频延迟: ", "Audio delay: ");
            Add("SubtitleDelayPrefix", "字幕延迟: ", "Subtitle delay: ");
            Add("BrightnessPrefix", "亮度: ", "Brightness: ");
            Add("ContrastPrefix", "对比度: ", "Contrast: ");
            Add("AudioDelayReset", "音频延迟已重置", "Audio delay reset");
            Add("SubtitleDelayReset", "字幕延迟已重置", "Subtitle delay reset");
            Add("AudioDevicePrefix", "音频设备: ", "Audio device: ");
            Add("AspectRatioPrefix", "画面比例: ", "Aspect ratio: ");
            Add("ScalePrefix", "缩放: ", "Scale: ");
            Add("RotationPrefix", "旋转: ", "Rotation: ");
            Add("CropPrefix", "裁剪: ", "Crop: ");
            Add("NightModeOn", "夜间护眼模式已开启", "Night mode on");
            Add("NightModeOff", "夜间护眼模式已关闭", "Night mode off");
            Add("RatingPrefix", "评分: ", "Rating: ");
            Add("RatingCleared", "已清除评分", "Rating cleared");
            Add("EqualizerErrorPrefix", "均衡器错误: ", "Equalizer error: ");
            Add("EqCustomSaved", "自定义均衡器预置已保存", "Custom equalizer preset saved");
            Add("FilterReset", "视频滤镜已重置", "Video filters reset");
            Add("JumpPrefix", "跳转: ", "Jump: ");
            // Home remaining
            Add("Open", "打开", "Open");
            Add("OpenVideosPlay", "打开视频  ▶  播放", "Open Videos  ▶  Play");
            Add("OpenMusicPlay", "打开音乐  ▶  播放", "Open Music  ▶  Play");
            Add("OpenPhotosView", "打开图片  ▶  查看", "Open Photos  ▶  View");
            Add("DropPlay", "拖放播放", "Drop to Play");
            Add("DropRelease", "释放以开始播放", "Release to start playing");
            Add("OpenNetworkTip", "打开网络媒体 (Ctrl+U)", "Open Network Media (Ctrl+U)");
            Add("SemanticZoomTip", "语义缩放概览 (Ctrl+M)", "Semantic Zoom Overview (Ctrl+M)");
            // Player welcome page
            Add("Welcome", "欢迎", "Welcome");
            Add("WelcomeHint", "拖放文件到这里，按 Ctrl+O 打开，或输入网络地址", "Drop files here, press Ctrl+O to open, or enter a network address");
            Add("ShortcutsTitle", "快捷键", "Shortcuts");
            Add("PlayPauseDesc", "播放 / 暂停", "Play / Pause");
            Add("SeekDesc", "快进 / 快退", "Seek Backward / Forward");
            Add("FullscreenDesc", "切换全屏", "Toggle Fullscreen");
            Add("AbLoopDesc", "A-B 循环播放", "A-B Loop");
            Add("VolumeDesc", "音量增大 / 减小", "Volume Up / Down");
            Add("TaglineFull", "HyperMedia — 您的全能媒体播放器", "HyperMedia — your all-in-one media player");
            Add("SwipeBack", "← 后退", "← Back");
            Add("SwipeFwd", "快进 →", "Fast-forward →");
            Add("LyricsTitle", "歌词", "Lyrics");
            Add("NoLyrics", "暂无歌词", "No lyrics");
            Add("SaveCustom", "保存自定义", "Save Custom");
            Add("Brightness", "亮度", "Brightness");
            Add("Contrast", "对比度", "Contrast");
            Add("Hue", "色调", "Hue");
            Add("Saturation", "饱和度", "Saturation");
            Add("Gamma", "伽马", "Gamma");
            Add("ResetDefault", "重置为默认", "Reset to Default");
            Add("ShortcutPlayback", "播放控制", "Playback");
            Add("ShortcutSync", "同步调节", "Sync");
            Add("ShortcutFunction", "功能", "Actions");
            Add("ShortcutImage", "图像调节", "Image Adjust");
            Add("Ready", "就绪", "Ready");
            Add("EmptyPlaylist", "暂无播放项目", "No items in playlist");
            Add("PlaylistSearchPlaceholder", "搜索播放列表... (Esc 清除)", "Search playlist... (Esc to clear)");
            // Settings remaining
            Add("ControlSection", "控制", "Controls");
            Add("SubtitleSection", "字幕", "Subtitles");
            Add("VideoSection", "视频", "Video");
            Add("SizeSmall", "小", "S");
            Add("SizeMedium", "中", "M");
            Add("SizeLarge", "大", "L");
            Add("SizeXLarge", "特大", "XL");
            Add("ColorWhite", "白色", "White");
            Add("ColorYellow", "黄色", "Yellow");
            Add("ColorCyan", "青色", "Cyan");
            Add("ColorGreen", "绿色", "Green");
            Add("ColorPink", "粉色", "Pink");
            Add("OutlineNone", "无", "None");
            Add("OutlineThin", "细", "Thin");
            Add("OutlineMedium", "中", "Medium");
            Add("OutlineThick", "粗", "Thick");
            Add("DeintAuto", "自动", "Auto");
            Add("DeintOn", "开启", "On");
            Add("DeintOff", "关闭", "Off");
            Add("DescDefaultVolume", "打开新文件时的音量级别", "Volume level for new files");
            Add("DescAutoPlay", "自动播放播放列表中的下一个文件", "Auto-play next file in playlist");
            Add("DescResume", "记住上次播放位置并从断点继续", "Remember position and resume later");
            Add("DescAutoHide", "播放时自动隐藏控制按钮", "Auto-hide controls while playing");
            Add("DescAutoHideDelay", "控制栏自动隐藏前的等待时间", "Delay before auto-hiding controls");
            Add("DescSubtitleSize", "调整外挂字幕的显示大小", "Adjust subtitle display size");
            Add("DescSubtitleColor", "调整字幕文字颜色", "Adjust subtitle text color");
            Add("DescSubtitleOutline", "为字幕添加黑色描边以提高对比度", "Add black outline for contrast");
            Add("DescSubtitleMargin", "上下移动字幕显示位置", "Move subtitle up / down");
            Add("DescDeinterlace", "处理隔行扫描视频（DVD、旧录像等）", "Deinterlace interlaced video (DVD, old recordings)");
            Add("DescLoudness", "自动平衡不同媒体的音量差异（压缩器）", "Normalize volume across media (compressor)");
            Add("DescEpisode", "打开单个文件时自动把同目录其他媒体加入播放列表", "Auto-queue other media in the same folder");
            Add("DescIntroSkip", "记录你手动跳过的片头位置，下次自动跳过", "Remember your manual intro skip and reuse it");
            Add("DescLanguage", "界面语言（立即生效）", "UI language (applies immediately)");
            Add("DescLightTheme", "主页改用浅色主题（播放器保持深色观看体验）", "Light home theme (player stays dark)");
            Add("DescClearHistory", "删除所有最近播放记录", "Delete all recent playback history");
            Add("DescSleepTimer", "播放指定时长后自动停止（0 = 关闭）", "Auto-stop after N minutes (0 = off)");
            Add("DescClearResume", "清除所有已保存的播放进度", "Clear all saved playback positions");
            // Player dynamic UI (dialogs/menus/info panel)
            Add("ShareTitle", "分享媒体文件", "Share Media File");
            Add("ShareDesc", "来自 HyperMedia 播放的媒体", "Media played in HyperMedia");
            Add("Playing", "正在播放", "Playing");
            Add("Paused", "已暂停", "Paused");
            Add("VlcNotInit", "错误: VLC 未初始化", "Error: VLC not initialized");
            Add("ErrorPrefix", "错误: ", "Error: ");
            Add("ClearPlaylistConfirm", "确定要清空播放列表吗？", "Clear the playlist?");
            Add("ClearPlaylistTitle", "清空播放列表", "Clear Playlist");
            Add("ClearBtn", "清空", "Clear");
            Add("TotalPrefix", "共 ", "Total ");
            Add("BookmarkTitle", "书签", "Bookmarks");
            Add("NoBookmarksShort", "无书签", "No bookmarks");
            Add("TitleLabel", "标题: ", "Title: ");
            Add("ArtistLabel", "艺术家: ", "Artist: ");
            Add("AlbumLabel", "专辑: ", "Album: ");
            Add("DateLabel", "日期: ", "Date: ");
            Add("VideoLabel", "视频: ", "Video: ");
            Add("VideoNone", "视频: 无", "Video: none");
            Add("AudioLabel", "音频: ", "Audio: ");
            Add("SubtitleTrackLabel", "字幕轨: ", "Subtitle: ");
            Add("DurationLabel", "时长: ", "Duration: ");
            Add("FileSizeGb", "文件大小: {0:F2} GB", "File size: {0:F2} GB");
            Add("FileSizeMb", "文件大小: {0:F1} MB", "File size: {0:F1} MB");
            Add("FileSizeKb", "文件大小: {0:F0} KB", "File size: {0:F0} KB");
            Add("DisableSubtitles", "关闭字幕", "Disable Subtitles");
            Add("SubtitleTrack", "字幕轨道 ", "Subtitle Track ");
            Add("LoadExternalSubtitle", "加载外部字幕...", "Load External Subtitle...");
            Add("AudioTrackItem", "音轨 ", "Audio Track ");
            Add("NoAudioTracks", "无可用音轨", "No audio tracks");
            Add("MyPlaylists", "我的歌单", "My Playlists");
            Add("AddFolderBtn", "+ 添加文件夹", "+ Add Folder");
            Add("FolderEmpty", "文件夹中没有媒体文件", "No media files in folder");
            Add("NoFolderYet", "尚未添加文件夹 — 点击上方按钮选择", "No folder added — use the button above");
            Add("UrlPlaceholder", "http://example.com/video.mp4 或 rtsp://...", "http://example.com/video.mp4 or rtsp://...");
            Add("NoPlaylistYet", "暂无歌单 — 在播放器播放列表点 💾 保存", "No playlists yet — tap 💾 in the player to save one");
            Add("SmartPlaylistEmpty", "智能播放列表为空", "Smart playlist is empty");
            Add("FileUnavailable", "文件不可用（可能已被移动）", "File unavailable (may have moved)");
            Add("PlaylistEmpty", "歌单为空", "Playlist is empty");
            Add("DeleteFromHistory", "从历史记录删除", "Remove from History");
            Add("PinnedToStart", "已固定到开始屏幕: ", "Pinned to Start: ");
            Add("AboutCharm", "关于 HyperMedia", "About HyperMedia");
            Add("SearchQueryHint", "  — 点按任意项目播放 (Esc 清除)", " — tap any item to play (Esc to clear)");
            Add("PlayedTimesSuffix", " 次", " times");
            Add("PlayCountLabel", "播放 ", "Played ");
            Add("ResumeTextPrefix", "续播 ", "Resume ");
            Add("ClearHistoryConfirm", "确定要清除所有播放历史吗？此操作不可撤销。", "Clear all playback history? This cannot be undone.");
            Add("ClearHistoryTitle", "清除播放历史", "Clear Play History");
            Add("PlaylistHint", "在播放器的播放列表中点击 💾 即可将当前列表保存为歌单", "Tap 💾 in the player's playlist to save it as a playlist");
            Add("PlaylistDeleted", "歌单已删除: ", "Playlist deleted: ");
            Add("PlaylistUnavailable", "歌单文件不可用（可能已被移动）", "Playlist files unavailable (may have moved)");
            Add("ClearResumeConfirm", "确定要清除所有续播位置吗？", "Clear all resume positions?");
            Add("PlaybackStoppedToast", "播放已自动停止", "Playback stopped automatically");
            Add("PlaybackErrorDetail", "无法播放此媒体，可能格式不受支持或文件已损坏", "Cannot play this media. Format may be unsupported or the file may be corrupted");
        }

        private void Add(string key, string zh, string en)
        {
            _strings[key] = new Dictionary<string, string> { { "zh-CN", zh }, { "en-US", en } };
        }

        // Apply language to a page by walking the visual tree and matching
        // current Chinese text against known keys (no XAML changes needed).
        public void ApplyLanguageTo(FrameworkElement root)
        {
            try
            {
                ApplyToElement(root);
                ApplyToChildren(root);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[HyperMedia] ApplyLanguageTo failed: {0}", ex.Message);
            }
        }

        private void ApplyToChildren(DependencyObject parent)
        {
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                ApplyToElement(child);
                ApplyToChildren(child);
            }
        }

        private void ApplyToElement(DependencyObject element)
        {
            var tb = element as TextBlock;
            if (tb != null)
            {
                if (!string.IsNullOrEmpty(tb.Text))
                {
                    string localized = LookupByChinese(tb.Text);
                    if (localized != null)
                        tb.Text = localized;
                }
                return;
            }

            var btn = element as Button;
            if (btn != null)
            {
                var contentText = btn.Content as string;
                if (!string.IsNullOrEmpty(contentText))
                {
                    string localized = LookupByChinese(contentText);
                    if (localized != null)
                        btn.Content = localized;
                }

                // Tooltip content
                try
                {
                    var tipObj = ToolTipService.GetToolTip(btn);
                    string tipText = null;
                    var tipCtl = tipObj as ToolTip;
                    if (tipCtl != null)
                        tipText = tipCtl.Content as string;
                    else
                        tipText = tipObj as string;

                    if (!string.IsNullOrEmpty(tipText))
                    {
                        string localizedTip = LookupByChinese(tipText);
                        if (localizedTip != null)
                            ToolTipService.SetToolTip(btn, localizedTip);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("[HyperMedia] Tooltip l10n failed: {0}", ex.Message);
                }
                return;
            }

            var comboItem = element as ComboBoxItem;
            if (comboItem != null)
            {
                var itemText = comboItem.Content as string;
                if (!string.IsNullOrEmpty(itemText))
                {
                    string localized = LookupByChinese(itemText);
                    if (localized != null)
                        comboItem.Content = localized;
                }
                return;
            }

            var menuItem = element as MenuFlyoutItem;
            if (menuItem != null)
            {
                if (!string.IsNullOrEmpty(menuItem.Text))
                {
                    string localized = LookupByChinese(menuItem.Text);
                    if (localized != null)
                        menuItem.Text = localized;
                }
            }
        }

        private string LookupByChinese(string zh)
        {
            if (string.IsNullOrEmpty(zh) || _strings == null) return null;
            foreach (var kv in _strings)
            {
                Dictionary<string, string> table = kv.Value;
                string zhVal;
                if (table.TryGetValue("zh-CN", out zhVal) && zhVal == zh)
                {
                    string v;
                    if (table.TryGetValue(_language, out v) && !string.IsNullOrEmpty(v))
                        return v;
                }
            }
            return null;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public event EventHandler LanguageChanged;

        private void RaiseAll()
        {
            if (PropertyChanged == null) return;
            PropertyChanged(this, new PropertyChangedEventArgs("T"));
        }
    }
}
