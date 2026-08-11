#if USE_LIBVLC
using System;
using System.Collections.Generic;
using Windows.UI.Xaml.Controls;
using libVLCX;

namespace HyperMedia
{
    public class VlcBackend : IPlayerBackend
    {
        private readonly SwapChainPanel _surface;
        private Instance _instance;
        private Media _media;
        private MediaPlayer _player;
        private double _duration;
        private bool _openedRaised;

        public VlcBackend(SwapChainPanel surface)
        {
            _surface = surface;
        }

        public void Init()
        {
            try
            {
                string inventory = DumpPackageInventory();
                System.Diagnostics.Debug.WriteLine(inventory);
                _instance = new Instance(MakeVlcArgs(), _surface);
                InitError = null;
                System.Diagnostics.Debug.WriteLine("[HyperMedia] VlcBackend Init OK");
            }
            catch (Exception ex)
            {
                InitError = BuildFailureReport(ex);
                _instance = null;
                System.Diagnostics.Debug.WriteLine(InitError);
            }
        }

        private static string BuildFailureReport(Exception ex)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("[HyperMedia] VlcBackend Init FAILED");
            sb.AppendLine("  type   : " + ex.GetType().FullName);
            sb.AppendLine("  hresult: 0x" + ex.HResult.ToString("X8") + "  (" + MapHr(ex.HResult) + ")");
            sb.AppendLine("  message: " + ex.Message);
            Exception inner = ex.InnerException;
            while (inner != null)
            {
                sb.AppendLine("  inner  : " + inner.GetType().FullName +
                    " 0x" + inner.HResult.ToString("X8") + "  " + inner.Message);
                inner = inner.InnerException;
            }
            try { sb.AppendLine("  stack  : " + ex.StackTrace); } catch { }
            return sb.ToString();
        }

        private static string MapHr(int hr)
        {
            uint u = unchecked((uint)hr);
            switch (u)
            {
                case 0x8007007E: return "MODULE_NOT_FOUND - 缺失 DLL/API set（如 winrt-error-l1-1-1）";
                case 0x80004002: return "E_NOINTERFACE - 工厂接口或激活者不匹配";
                case 0x80040154: return "REGDB_E_CLASSNOTREG - 类未注册";
                case 0x80070032: return "ERROR_NOT_SUPPORTED - 架构/平台不支持";
                case 0x8007000D: return "ERROR_INVALID_DATA - 元数据/数据损坏";
                case 0x80070005: return "E_ACCESSDENIED";
                case 0x80070057: return "E_INVALIDARG";
                case 0x80004005: return "E_FAIL";
                case 0x9EFFFFF:  return "0x9EFFFFF - 激活链失败（见 inner）";
                default: return "未知";
            }
        }

        /// <summary>
        /// Lists every root DLL of the deployed package so we can see, on the
        /// device itself, whether the VLC payload actually made it into the boot
        /// image (P/Invoke is blocked on WP8.1, so this is the loader-less check).
        /// </summary>
        private static string DumpPackageInventory()
        {
            try
            {
                var pkg = Windows.ApplicationModel.Package.Current;
                var run = System.Threading.Tasks.Task.Run(async () =>
                {
                    var outList = new System.Text.StringBuilder();
                    var root = await pkg.InstalledLocation.GetFilesAsync();
                    foreach (var f in root)
                        if (f.Name.EndsWith(".dll") || f.Name.EndsWith(".winmd"))
                        {
                            long sz = 0;
                            try { var p = await f.GetBasicPropertiesAsync(); sz = (long)p.Size; } catch { }
                            outList.AppendLine("  " + f.Name + " (" + sz + " bytes)");
                        }
                    try
                    {
                        var plugins = await pkg.InstalledLocation.GetFolderAsync("plugins");
                        var subs = await plugins.GetFoldersAsync();
                        foreach (var s in subs)
                            outList.AppendLine("  plugins\\" + s.Name + "\\");
                    }
                    catch { }
                    return outList.ToString();
                });
                run.Wait(4000);
                return "[HyperMedia] Package root inventory:" +
                    (run.IsCompleted ? "\n" + run.Result : "\n  (enumeration timed out)");
            }
            catch (Exception ex)
            {
                return "[HyperMedia] inventory failed: " + ex.Message;
            }
        }

        /// <summary>Last libVLCX instance-creation failure text (null when ready).</summary>
        public string InitError { get; private set; }

        public bool IsReady { get { return _instance != null; } }

        private static List<string> MakeVlcArgs()
        {
            var args = new List<string>
            {
                "-I", "dummy",
                "--no-plugins-cache",
                "--no-osd",
                "--no-stats",
                "--no-loop",
                "--no-video-title-show",
                "--drop-late-frames",
                "--avcodec-hw=any",
                "--aout=winstore",
                "--no-keyboard-events",
                "--no-mouse-events",
                "--file-logging",
                "--verbose=2"
            };
            try
            {
                args.Add("--logfile=" + Windows.Storage.ApplicationData.Current.TemporaryFolder.Path + "\\vlc.log");
            }
            catch { }
            try
            {
                var pkg = Windows.ApplicationModel.Package.Current;
                if (pkg != null)
                {
                    string pluginPath = pkg.InstalledLocation.Path + "\\plugins";
                    args.Add("--plugin-path=" + pluginPath);
                }
            }
            catch { }
            return args;
        }

        public double DurationSeconds
        {
            get
            {
                try
                {
                    if (_duration <= 0 && _player != null)
                    {
                        long len = _player.length();
                        if (len > 0) _duration = len / 1000.0;
                    }
                }
                catch { }
                return _duration;
            }
        }

        public double PositionSeconds
        {
            get { try { return _player != null ? _player.time() / 1000.0 : 0; } catch { return 0; } }
            set { try { if (_player != null) _player.setTime((long)(Math.Max(0, value) * 1000)); } catch { } }
        }

        private double _volume = 1;

        public double Volume
        {
            get { return _volume; }
            set
            {
                double v = Math.Max(0, Math.Min(100, value * 100.0));
                _volume = v / 100.0;
                try { if (_player != null) _player.setVolume((int)v); } catch { }
            }
        }

        public double Rate
        {
            get { return 1; }
            set { try { if (_player != null) _player.setRate((float)Math.Max(0.25, Math.Min(4.0, value))); } catch { } }
        }

        public void OpenStream(Windows.Storage.Streams.IRandomAccessStream stream) { }

        public void OpenPath(string path)
        {
            OpenMedia(path, FromType.FromPath);
        }

        public void OpenUrl(string url)
        {
            OpenMedia(url, FromType.FromLocation);
        }

        private void OpenMedia(string target, FromType type)
        {
            if (_instance == null) { var h = PlaybackFailed; if (h != null) h(this, EventArgs.Empty); return; }
            Release();
            _openedRaised = false;
            try
            {
                _media = new Media(_instance, target, type);
                _media.addOption(":avcodec-hw=any");
                _player = new MediaPlayer(_instance);
                _player.setAdjustInt(0, 1);
                _player.setMedia(_media);

                var em = _player.eventManager();
                if (em != null)
                {
                    em.OnEndReached += OnEndReached;
                    em.OnEncounteredError += OnEncounteredError;
                    em.OnLengthChanged += OnLengthChanged;
                }

                _player.play();
            }
            catch { Release(); var h = PlaybackFailed; if (h != null) h(this, EventArgs.Empty); }
        }

        private void OnLengthChanged(long length)
        {
            if (length > 0) _duration = length / 1000.0;
            if (!_openedRaised)
            {
                _openedRaised = true;
                var h = MediaOpened; if (h != null) h(this, EventArgs.Empty);
            }
        }

        private void OnEndReached()
        {
            var h = MediaEnded; if (h != null) h(this, EventArgs.Empty);
        }

        private void OnEncounteredError()
        {
            var h = PlaybackFailed; if (h != null) h(this, EventArgs.Empty);
        }

        public void Close()
        {
            Release();
            try { _duration = 0; } catch { }
        }

        public void Play() { try { if (_player != null) _player.play(); } catch { } }

        public void Pause() { try { if (_player != null) _player.pause(); } catch { } }

        public void Stop() { try { if (_player != null) _player.stop(); } catch { } }

        private void Release()
        {
            try
            {
                if (_player != null)
                {
                    try { _player.stop(); } catch { }
                    var em = _player.eventManager();
                    if (em != null)
                    {
                        try
                        {
                            em.OnEndReached -= OnEndReached;
                            em.OnEncounteredError -= OnEncounteredError;
                            em.OnLengthChanged -= OnLengthChanged;
                        }
                        catch { }
                    }
                    _player = null;
                }
                _media = null;
            }
            catch { }
        }

        public event EventHandler MediaOpened;
        public event EventHandler MediaEnded;
        public event EventHandler PlaybackFailed;
    }
}
#endif