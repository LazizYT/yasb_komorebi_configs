using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace YasbLiveWallpaper
{
    internal static class Native
    {
        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_TRANSPARENT = 0x00000020;
        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int WS_EX_LAYERED = 0x00080000;
        public const int WS_EX_NOACTIVATE = 0x08000000;

        public const int WM_MOUSEACTIVATE = 0x0021;
        public const int MA_NOACTIVATE = 3;
        public const int WM_WINDOWPOSCHANGING = 0x0046;
        public const int WM_NCHITTEST = 0x0084;
        public static readonly IntPtr HTTRANSPARENT = new IntPtr(-1);

        public static readonly IntPtr HWND_BOTTOM = new IntPtr(1);
        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOACTIVATE = 0x0010;
        public const uint GW_HWNDNEXT = 2;
        public const int DWMWA_CLOAKED = 14;

        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        public struct WINDOWPOS
        {
            public IntPtr hwnd;
            public IntPtr hwndInsertAfter;
            public int x;
            public int y;
            public int cx;
            public int cy;
            public uint flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X, Y;
        }

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        private static extern IntPtr GetWindowLong32(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
        private static extern IntPtr SetWindowLong32(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        public static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex)
        {
            return IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : GetWindowLong32(hWnd, nIndex);
        }

        public static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
        {
            return IntPtr.Size == 8 ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong) : SetWindowLong32(hWnd, nIndex, dwNewLong);
        }

        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        public static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        [DllImport("user32.dll")]
        public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("dwmapi.dll")]
        public static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

        public const int JobObjectExtendedLimitInformation = 9;
        public const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000;

        [StructLayout(LayoutKind.Sequential)]
        public struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct IO_COUNTERS
        {
            public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount;
            public ulong ReadTransferCount, WriteTransferCount, OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS IoInfo;
            public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string lpName);

        [DllImport("kernel32.dll")]
        public static extern bool SetInformationJobObject(IntPtr hJob, int infoClass, ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION info, int length);

        [DllImport("kernel32.dll")]
        public static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);
    }

    internal class WallpaperConfig
    {
        public static readonly string ConfigDir = @"C:\Users\Laziko\.config\yasb";
        public static readonly string ConfigPath = Path.Combine(ConfigDir, "live_wallpaper.cfg");
        public static readonly string DefaultPhotoFolder = @"C:\Users\Laziko\Pictures\1. Nature Wallpapers";
        public static readonly string DefaultVideoFolder = @"C:\Users\Laziko\Pictures\LiveWallpapers";

        // Modes: 1 = Photo (with circle reveal + parallax), 3 = Video (with circle reveal)
        public int Mode = 3;
        public string PhotoFolder = DefaultPhotoFolder;
        public string PhotoPath = "";
        public string VideoPath = "";
        public bool VideoMuted = true;
        public bool AutoPause = true;
        public bool VideoSlideshow = true;
        public int VideoSlideshowSec = 180;

        public static int NormalizeMode(int m)
        {
            return (m == 1) ? 1 : 3;
        }

        public static WallpaperConfig Load()
        {
            WallpaperConfig cfg = new WallpaperConfig();
            try
            {
                if (!Directory.Exists(DefaultVideoFolder))
                    Directory.CreateDirectory(DefaultVideoFolder);
            }
            catch { }

            try
            {
                if (File.Exists(ConfigPath))
                {
                    string[] lines = File.ReadAllLines(ConfigPath, Encoding.UTF8);
                    for (int i = 0; i < lines.Length; i++)
                    {
                        string line = lines[i].Trim();
                        int eq = line.IndexOf('=');
                        if (eq <= 0) continue;
                        string k = line.Substring(0, eq).Trim().ToLowerInvariant();
                        string v = line.Substring(eq + 1).Trim();
                        if (k == "mode")
                        {
                            int m;
                            if (int.TryParse(v, out m)) cfg.Mode = NormalizeMode(m);
                        }
                        else if (k == "photo_folder" && v.Length > 0) cfg.PhotoFolder = v;
                        else if (k == "photo_path") cfg.PhotoPath = v;
                        else if (k == "video_path") cfg.VideoPath = v;
                        else if (k == "video_muted") cfg.VideoMuted = (v != "0" && v.ToLowerInvariant() != "false");
                        else if (k == "auto_pause") cfg.AutoPause = (v != "0" && v.ToLowerInvariant() != "false");
                        else if (k == "video_slideshow") cfg.VideoSlideshow = (v != "0" && v.ToLowerInvariant() != "false");
                        else if (k == "video_slideshow_sec")
                        {
                            int sec;
                            if (int.TryParse(v, out sec) && sec >= 15 && sec <= 3600) cfg.VideoSlideshowSec = sec;
                        }
                    }
                }
            }
            catch { }
            return cfg;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(ConfigDir);
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("mode=" + Mode.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("photo_folder=" + (PhotoFolder ?? DefaultPhotoFolder));
                sb.AppendLine("photo_path=" + (PhotoPath ?? ""));
                sb.AppendLine("video_path=" + (VideoPath ?? ""));
                sb.AppendLine("video_muted=" + (VideoMuted ? "1" : "0"));
                sb.AppendLine("auto_pause=" + (AutoPause ? "1" : "0"));
                sb.AppendLine("video_slideshow=" + (VideoSlideshow ? "1" : "0"));
                sb.AppendLine("video_slideshow_sec=" + VideoSlideshowSec.ToString(CultureInfo.InvariantCulture));
                File.WriteAllText(ConfigPath, sb.ToString(), Encoding.UTF8);
            }
            catch { }
        }
    }

    internal class LiveWallpaperWindow : Window
    {
        private readonly WallpaperConfig _cfg;
        private readonly Grid _rootGrid;
        private readonly Image _prevPhotoImage;
        private readonly Image _photoImage;
        private readonly ScaleTransform _photoScale;
        private readonly TranslateTransform _photoPan;
        private readonly Image _prevVideoImage;
        private readonly Image _videoImage;

        private const int VidW = 1280;
        private const int VidH = 720;
        private WriteableBitmap _prevVideoBitmap;
        private WriteableBitmap _videoBitmap;
        private byte[] _videoFrontBuf;
        private byte[] _videoBackBuf;
        private readonly object _videoBufLock = new object();
        private volatile bool _videoFrameReady = false;
        private bool _hasRenderedAnyVideoFrame = false;
        private bool _pendingCircleReveal = false;
        private int _revealGen = 0;
        private int _photoRevealGen = 0;
        private Process _ffmpegProc;
        private Thread _ffmpegThread;
        private readonly object _ffmpegLock = new object();
        private volatile int _ffmpegGen = 0;
        private IntPtr _jobHandle = IntPtr.Zero;

        private readonly DispatcherTimer _envTimer;
        private readonly DispatcherTimer _slideshowTimer;
        private readonly List<string> _photoList = new List<string>();
        private readonly List<string> _videoList = new List<string>();
        private int _photoIndex = 0;
        private int _videoIndex = 0;
        private string _currentPhotoPath = "";
        private string _currentVideoPath = "";
        private DateTime _lastSlideUtc = DateTime.UtcNow;

        private IntPtr _selfHwnd = IntPtr.Zero;
        private readonly uint _selfPid;
        private bool _isAutoPaused = false;
        private bool _hasLoadedVideo = false;
        private double _smoothPx = 0, _smoothPy = 0;

        private static readonly HashSet<string> OverlayProcesses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Rainmeter", "audio_visualizer"
        };

        private static readonly HashSet<string> IgnoredProcesses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "yasb", "Rainmeter", "komorebi", "explorer", "SearchHost", "ShellExperienceHost",
            "StartMenuExperienceHost", "TextInputHost", "LockApp", "audio_visualizer", "live_wallpaper"
        };

        private static readonly HashSet<string> IgnoredClasses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "ForegroundStaging",
            "ApplicationManager_DesktopShellWindow", "Windows.UI.Core.CoreWindow", "EdgeUiInputTopWndClass"
        };

        public LiveWallpaperWindow(WallpaperConfig cfg)
        {
            _cfg = cfg;
            _selfPid = (uint)Process.GetCurrentProcess().Id;
            InitJobObject();

            Title = "YASB Live Wallpaper";
            WindowStyle = WindowStyle.None;
            // AllowsTransparency = true guarantees WS_EX_LAYERED + WS_EX_TRANSPARENT pass clicks cleanly to the desktop!
            AllowsTransparency = true;
            Background = new SolidColorBrush(Color.FromRgb(10, 8, 20));
            Topmost = false;
            ShowInTaskbar = false;
            ShowActivated = false;
            Focusable = false;
            IsHitTestVisible = false;
            ResizeMode = ResizeMode.NoResize;

            SyncBounds();

            _rootGrid = new Grid();
            _rootGrid.Background = new SolidColorBrush(Color.FromRgb(10, 8, 20));

            // Layer 1: Photo Parallax + Previous Photo for Circle Transition
            _photoScale = new ScaleTransform(1.03, 1.03);
            _photoPan = new TranslateTransform(0, 0);
            TransformGroup tg = new TransformGroup();
            tg.Children.Add(_photoScale);
            tg.Children.Add(_photoPan);

            _prevPhotoImage = new Image
            {
                Stretch = Stretch.UniformToFill,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = tg,
                Visibility = Visibility.Collapsed
            };
            RenderOptions.SetBitmapScalingMode(_prevPhotoImage, BitmapScalingMode.LowQuality);
            _rootGrid.Children.Add(_prevPhotoImage);

            _photoImage = new Image
            {
                Stretch = Stretch.UniformToFill,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = tg,
                Visibility = Visibility.Collapsed
            };
            RenderOptions.SetBitmapScalingMode(_photoImage, BitmapScalingMode.LowQuality);
            _rootGrid.Children.Add(_photoImage);

            // Layer 2: FFmpeg Video Surface + Previous Frame for Circle Transition
            _prevVideoBitmap = new WriteableBitmap(VidW, VidH, 96, 96, PixelFormats.Bgr24, null);
            _prevVideoImage = new Image
            {
                Source = _prevVideoBitmap,
                Stretch = Stretch.UniformToFill,
                Visibility = Visibility.Collapsed
            };
            RenderOptions.SetBitmapScalingMode(_prevVideoImage, BitmapScalingMode.LowQuality);
            _rootGrid.Children.Add(_prevVideoImage);

            _videoBitmap = new WriteableBitmap(VidW, VidH, 96, 96, PixelFormats.Bgr24, null);
            int frameBytes = VidW * VidH * 3;
            _videoFrontBuf = new byte[frameBytes];
            _videoBackBuf = new byte[frameBytes];

            _videoImage = new Image
            {
                Source = _videoBitmap,
                Stretch = Stretch.UniformToFill,
                Visibility = Visibility.Collapsed
            };
            RenderOptions.SetBitmapScalingMode(_videoImage, BitmapScalingMode.LowQuality);
            _rootGrid.Children.Add(_videoImage);

            Content = _rootGrid;

            Closed += delegate { StopVideoPlayback(); };
            AppDomain.CurrentDomain.ProcessExit += delegate { StopVideoPlayback(); };

            _envTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
            _envTimer.Tick += OnEnvTick;
            _envTimer.Start();

            _slideshowTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _slideshowTimer.Tick += delegate
            {
                if (!IsVisible || _isAutoPaused) return;
                double elapsed = (DateTime.UtcNow - _lastSlideUtc).TotalSeconds;
                if (_cfg.Mode == 1 && _cfg.VideoSlideshow && _photoList.Count > 1 && elapsed >= _cfg.VideoSlideshowSec)
                {
                    StepPhoto(1);
                }
                else if (_cfg.Mode == 3 && _cfg.VideoSlideshow && _videoList.Count > 1 && elapsed >= _cfg.VideoSlideshowSec)
                {
                    StepVideo(1);
                }
            };
            _slideshowTimer.Start();

            CompositionTarget.Rendering += OnParallaxRender;
        }

        private void SyncBounds()
        {
            Left = SystemParameters.VirtualScreenLeft;
            Top = SystemParameters.VirtualScreenTop;
            Width = SystemParameters.VirtualScreenWidth;
            Height = SystemParameters.VirtualScreenHeight;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            _selfHwnd = new WindowInteropHelper(this).Handle;
            if (_selfHwnd != IntPtr.Zero)
            {
                int ex = Native.GetWindowLongPtr(_selfHwnd, Native.GWL_EXSTYLE).ToInt32();
                ex |= Native.WS_EX_TRANSPARENT | Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
                Native.SetWindowLongPtr(_selfHwnd, Native.GWL_EXSTYLE, new IntPtr(ex));
                SendToBottom();

                HwndSource src = HwndSource.FromHwnd(_selfHwnd);
                if (src != null) src.AddHook(WndProc);
            }
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == Native.WM_NCHITTEST)
            {
                handled = true;
                return Native.HTTRANSPARENT;
            }
            if (msg == Native.WM_MOUSEACTIVATE)
            {
                handled = true;
                return new IntPtr(Native.MA_NOACTIVATE);
            }
            if (msg == Native.WM_WINDOWPOSCHANGING)
            {
                Native.WINDOWPOS wp = (Native.WINDOWPOS)Marshal.PtrToStructure(lParam, typeof(Native.WINDOWPOS));
                wp.hwndInsertAfter = Native.HWND_BOTTOM;
                Marshal.StructureToPtr(wp, lParam, true);
            }
            return IntPtr.Zero;
        }

        private void SendToBottom()
        {
            if (_selfHwnd != IntPtr.Zero)
            {
                Native.SetWindowPos(_selfHwnd, Native.HWND_BOTTOM, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            }
        }

        private void InitJobObject()
        {
            try
            {
                _jobHandle = Native.CreateJobObject(IntPtr.Zero, null);
                if (_jobHandle != IntPtr.Zero)
                {
                    Native.JOBOBJECT_EXTENDED_LIMIT_INFORMATION info = new Native.JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
                    info.BasicLimitInformation.LimitFlags = Native.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
                    int len = Marshal.SizeOf(typeof(Native.JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
                    Native.SetInformationJobObject(_jobHandle, Native.JobObjectExtendedLimitInformation, ref info, len);
                }
            }
            catch { }
        }

        private void OnParallaxRender(object sender, EventArgs e)
        {
            if (!IsVisible || _isAutoPaused) return;

            if (_cfg.Mode == 3 && _videoFrameReady && _videoImage.Visibility == Visibility.Visible)
            {
                bool triggerReveal = false;
                lock (_videoBufLock)
                {
                    if (_videoFrameReady)
                    {
                        _videoBitmap.Lock();
                        Marshal.Copy(_videoFrontBuf, 0, _videoBitmap.BackBuffer, _videoFrontBuf.Length);
                        _videoBitmap.AddDirtyRect(new Int32Rect(0, 0, VidW, VidH));
                        _videoBitmap.Unlock();
                        _videoFrameReady = false;
                        _hasRenderedAnyVideoFrame = true;
                        if (_pendingCircleReveal)
                        {
                            _pendingCircleReveal = false;
                            triggerReveal = true;
                        }
                    }
                }
                if (triggerReveal) StartVideoCircleReveal();
            }

            if (_cfg.Mode == 1 && _photoImage.Visibility == Visibility.Visible)
            {
                Native.POINT pt;
                if (Native.GetCursorPos(out pt))
                {
                    double nx = ((pt.X - Left) / Math.Max(1.0, Width) - 0.5) * 2.0;
                    double ny = ((pt.Y - Top) / Math.Max(1.0, Height) - 0.5) * 2.0;
                    nx = Math.Max(-1.0, Math.Min(1.0, nx));
                    ny = Math.Max(-1.0, Math.Min(1.0, ny));
                    if (Math.Abs(nx - _smoothPx) > 0.004 || Math.Abs(ny - _smoothPy) > 0.004)
                    {
                        _smoothPx += (nx - _smoothPx) * 0.14;
                        _smoothPy += (ny - _smoothPy) * 0.14;
                        _photoPan.X = -_smoothPx * 14.0;
                        _photoPan.Y = -_smoothPy * 9.0;
                    }
                }
            }
        }

        private void OnEnvTick(object sender, EventArgs e)
        {
            if (!IsVisible) return;

            EnsureBelowOverlays();

            bool covered = _cfg.AutoPause && IsScreenCovered();
            if (covered != _isAutoPaused)
            {
                _isAutoPaused = covered;
                if (_cfg.Mode == 3)
                {
                    if (covered) StopFfmpegProcess();
                    else StartVideoPlayback(true);
                }
            }
        }

        private void EnsureBelowOverlays()
        {
            if (_selfHwnd == IntPtr.Zero) return;
            IntPtr cur = Native.GetWindow(_selfHwnd, Native.GW_HWNDNEXT);
            int guard = 0;
            while (cur != IntPtr.Zero && guard < 2000)
            {
                guard++;
                if (Native.IsWindowVisible(cur))
                {
                    uint pid;
                    Native.GetWindowThreadProcessId(cur, out pid);
                    if (pid != 0 && pid != _selfPid)
                    {
                        try
                        {
                            using (Process p = Process.GetProcessById((int)pid))
                            {
                                if (OverlayProcesses.Contains(p.ProcessName))
                                {
                                    SendToBottom();
                                    return;
                                }
                            }
                        }
                        catch { }
                    }
                }
                cur = Native.GetWindow(cur, Native.GW_HWNDNEXT);
            }
        }

        private bool IsScreenCovered()
        {
            bool covered = false;
            double screenArea = Math.Max(1.0, SystemParameters.PrimaryScreenWidth * SystemParameters.PrimaryScreenHeight);
            StringBuilder cls = new StringBuilder(128);

            Native.EnumWindows(delegate(IntPtr hwnd, IntPtr lParam)
            {
                if (hwnd == _selfHwnd) return true;
                if (!Native.IsWindowVisible(hwnd)) return true;
                if (Native.IsIconic(hwnd)) return true;

                int cloaked = 0;
                Native.DwmGetWindowAttribute(hwnd, Native.DWMWA_CLOAKED, out cloaked, 4);
                if (cloaked != 0) return true;

                int exStyle = Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE).ToInt32();
                if ((exStyle & Native.WS_EX_TOOLWINDOW) != 0) return true;

                cls.Clear();
                Native.GetClassName(hwnd, cls, cls.Capacity);
                if (IgnoredClasses.Contains(cls.ToString())) return true;

                Native.RECT rect;
                if (!Native.GetWindowRect(hwnd, out rect)) return true;
                double w = Math.Max(0, rect.Right - rect.Left);
                double h = Math.Max(0, rect.Bottom - rect.Top);
                if ((w * h) / screenArea < 0.80) return true;

                uint pid;
                Native.GetWindowThreadProcessId(hwnd, out pid);
                if (pid == _selfPid) return true;

                try
                {
                    using (Process proc = Process.GetProcessById((int)pid))
                    {
                        if (IgnoredProcesses.Contains(proc.ProcessName))
                        {
                            if (!string.Equals(proc.ProcessName, "explorer", StringComparison.OrdinalIgnoreCase) ||
                                !string.Equals(cls.ToString(), "CabinetWClass", StringComparison.OrdinalIgnoreCase))
                            {
                                return true;
                            }
                        }
                    }
                }
                catch { return true; }

                covered = true;
                return false;
            }, IntPtr.Zero);

            return covered;
        }

        public void ApplyMode()
        {
            _cfg.Mode = WallpaperConfig.NormalizeMode(_cfg.Mode);
            if (_cfg.Mode != 3)
            {
                StopVideoPlayback();
            }

            if (_cfg.Mode == 1)
            {
                _photoImage.Visibility = Visibility.Visible;
                RefreshPhotoList(true);
            }
            else
            {
                _photoImage.Clip = null;
                _photoImage.Visibility = Visibility.Collapsed;
                _prevPhotoImage.Visibility = Visibility.Collapsed;
            }

            if (_cfg.Mode == 3)
            {
                StartVideoPlayback(false);
            }

            if (IsVisible) SendToBottom();
        }

        private void RefreshPhotoList(bool loadCurrent)
        {
            _photoList.Clear();
            string dir = _cfg.PhotoFolder;
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                dir = WallpaperConfig.DefaultPhotoFolder;

            try
            {
                if (Directory.Exists(dir))
                {
                    string[] files = Directory.GetFiles(dir);
                    Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                    for (int i = 0; i < files.Length; i++)
                    {
                        string ext = Path.GetExtension(files[i]).ToLowerInvariant();
                        if (ext == ".jpg" || ext == ".jpeg" || ext == ".png" || ext == ".bmp" || ext == ".webp")
                            _photoList.Add(files[i]);
                    }
                }
            }
            catch { }

            if (loadCurrent && _photoList.Count > 0)
            {
                string target = (!string.IsNullOrEmpty(_cfg.PhotoPath) && File.Exists(_cfg.PhotoPath))
                    ? _cfg.PhotoPath
                    : _photoList[_photoIndex % _photoList.Count];

                for (int i = 0; i < _photoList.Count; i++)
                {
                    if (string.Equals(_photoList[i], target, StringComparison.OrdinalIgnoreCase))
                    {
                        _photoIndex = i;
                        break;
                    }
                }
                if (!string.Equals(_currentPhotoPath, target, StringComparison.OrdinalIgnoreCase) || _photoImage.Source == null)
                    LoadPhoto(target);
            }
        }

        private void LoadPhoto(string path)
        {
            try
            {
                BitmapImage bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bmp.DecodePixelWidth = (int)Math.Max(1280, SystemParameters.PrimaryScreenWidth);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();

                bool hadPrev = (_photoImage.Source != null && !string.IsNullOrEmpty(_currentPhotoPath));
                if (hadPrev)
                {
                    _prevPhotoImage.Source = _photoImage.Source;
                    _prevPhotoImage.Visibility = Visibility.Visible;
                }

                _currentPhotoPath = path;
                if (!string.Equals(_cfg.PhotoPath, path, StringComparison.OrdinalIgnoreCase))
                {
                    _cfg.PhotoPath = path;
                    _cfg.Save();
                }

                _photoImage.Source = bmp;
                _photoImage.Visibility = Visibility.Visible;
                StartPhotoCircleReveal();
            }
            catch { }
        }

        private void StartPhotoCircleReveal()
        {
            _photoRevealGen++;
            int myGen = _photoRevealGen;
            double w = Width > 0 ? Width : SystemParameters.PrimaryScreenWidth;
            double h = Height > 0 ? Height : SystemParameters.PrimaryScreenHeight;
            double maxR = Math.Sqrt(w * w + h * h) * 0.55;

            EllipseGeometry clip = new EllipseGeometry(new Point(w * 0.5, h * 0.5), 0, 0);
            _photoImage.Clip = clip;

            DoubleAnimation anim = new DoubleAnimation(0, maxR, TimeSpan.FromMilliseconds(480));
            Timeline.SetDesiredFrameRate(anim, 60);
            anim.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut };
            anim.Completed += delegate
            {
                if (myGen == _photoRevealGen)
                {
                    _photoImage.Clip = null;
                    _prevPhotoImage.Visibility = Visibility.Collapsed;
                }
            };
            clip.BeginAnimation(EllipseGeometry.RadiusXProperty, anim);
            clip.BeginAnimation(EllipseGeometry.RadiusYProperty, anim);
        }

        public void StepPhoto(int delta)
        {
            RefreshPhotoList(false);
            if (_photoList.Count == 0) return;

            int idx = -1;
            if (!string.IsNullOrEmpty(_cfg.PhotoPath))
            {
                for (int i = 0; i < _photoList.Count; i++)
                {
                    if (string.Equals(_photoList[i], _cfg.PhotoPath, StringComparison.OrdinalIgnoreCase))
                    {
                        idx = i;
                        break;
                    }
                }
            }
            if (idx < 0) idx = _photoIndex;
            idx = (idx + delta) % _photoList.Count;
            if (idx < 0) idx += _photoList.Count;

            _photoIndex = idx;
            _cfg.PhotoPath = _photoList[idx];
            if (_cfg.Mode != 1) _cfg.Mode = 1;
            _cfg.Save();
            _lastSlideUtc = DateTime.UtcNow;
            if (!IsVisible) ShowWallpaper();
            else ApplyMode();
        }

        private void RefreshVideoList()
        {
            _videoList.Clear();
            try
            {
                string dir = WallpaperConfig.DefaultVideoFolder;
                if (Directory.Exists(dir))
                {
                    string[] files = Directory.GetFiles(dir);
                    Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                    for (int i = 0; i < files.Length; i++)
                    {
                        string ext = Path.GetExtension(files[i]).ToLowerInvariant();
                        if (ext == ".mp4" || ext == ".mkv" || ext == ".webm" || ext == ".mov" || ext == ".wmv" || ext == ".avi" || ext == ".gif")
                            _videoList.Add(files[i]);
                    }
                }
            }
            catch { }
        }

        public void StepVideo(int delta)
        {
            RefreshVideoList();
            if (_videoList.Count == 0) return;

            int idx = -1;
            if (!string.IsNullOrEmpty(_cfg.VideoPath))
            {
                for (int i = 0; i < _videoList.Count; i++)
                {
                    if (string.Equals(_videoList[i], _cfg.VideoPath, StringComparison.OrdinalIgnoreCase))
                    {
                        idx = i;
                        break;
                    }
                }
            }
            if (idx < 0) idx = _videoIndex;
            idx = (idx + delta) % _videoList.Count;
            if (idx < 0) idx += _videoList.Count;

            _videoIndex = idx;
            _cfg.VideoPath = _videoList[idx];
            if (_cfg.Mode != 3) _cfg.Mode = 3;
            _cfg.Save();
            _lastSlideUtc = DateTime.UtcNow;
            if (!IsVisible) ShowWallpaper();
            else ApplyMode();
        }

        public void StepWallpaper(int delta)
        {
            if (_cfg.Mode == 1) StepPhoto(delta);
            else StepVideo(delta);
        }

        private static string FindFfmpegPath()
        {
            string p1 = @"C:\Users\Laziko\AppData\Roaming\Python\Python315\Scripts\ffmpeg.exe";
            if (File.Exists(p1)) return p1;

            string pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
            string[] dirs = pathEnv.Split(';');
            for (int i = 0; i < dirs.Length; i++)
            {
                string d = dirs[i].Trim();
                if (d.Length == 0) continue;
                try
                {
                    string cand = Path.Combine(d, "ffmpeg.exe");
                    if (File.Exists(cand)) return cand;
                }
                catch { }
            }
            return null;
        }

        private void StopFfmpegProcess()
        {
            lock (_ffmpegLock)
            {
                _ffmpegGen++;
                _videoFrameReady = false;
                if (_ffmpegProc != null)
                {
                    try
                    {
                        if (!_ffmpegProc.HasExited) _ffmpegProc.Kill();
                    }
                    catch { }
                    try { _ffmpegProc.Dispose(); } catch { }
                    _ffmpegProc = null;
                }
            }
        }

        private void StartVideoCircleReveal()
        {
            _revealGen++;
            int myGen = _revealGen;
            double w = Width > 0 ? Width : SystemParameters.PrimaryScreenWidth;
            double h = Height > 0 ? Height : SystemParameters.PrimaryScreenHeight;
            double maxR = Math.Sqrt(w * w + h * h) * 0.55;

            EllipseGeometry clip = new EllipseGeometry(new Point(w * 0.5, h * 0.5), 0, 0);
            _videoImage.Clip = clip;

            DoubleAnimation anim = new DoubleAnimation(0, maxR, TimeSpan.FromMilliseconds(480));
            Timeline.SetDesiredFrameRate(anim, 60);
            anim.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut };
            anim.Completed += delegate
            {
                if (myGen == _revealGen)
                {
                    _videoImage.Clip = null;
                    _prevVideoImage.Visibility = Visibility.Collapsed;
                }
            };
            clip.BeginAnimation(EllipseGeometry.RadiusXProperty, anim);
            clip.BeginAnimation(EllipseGeometry.RadiusYProperty, anim);
        }

        private void StopVideoPlayback()
        {
            StopFfmpegProcess();
            _hasLoadedVideo = false;
            _hasRenderedAnyVideoFrame = false;
            _pendingCircleReveal = false;
            _revealGen++;
            _currentVideoPath = "";
            _videoImage.Clip = null;
            _videoImage.Visibility = Visibility.Collapsed;
            _prevVideoImage.Visibility = Visibility.Collapsed;
        }

        private void StartVideoPlayback(bool forceRestart)
        {
            RefreshVideoList();
            string vid = FindVideoPath();
            string ffmpeg = FindFfmpegPath();

            if (string.IsNullOrEmpty(vid) || !File.Exists(vid) || string.IsNullOrEmpty(ffmpeg))
            {
                StopVideoPlayback();
                return;
            }

            if (!string.Equals(_cfg.VideoPath, vid, StringComparison.OrdinalIgnoreCase))
            {
                _cfg.VideoPath = vid;
                _cfg.Save();
            }
            for (int i = 0; i < _videoList.Count; i++)
            {
                if (string.Equals(_videoList[i], vid, StringComparison.OrdinalIgnoreCase))
                {
                    _videoIndex = i;
                    break;
                }
            }

            lock (_ffmpegLock)
            {
                if (!forceRestart && _hasLoadedVideo &&
                    string.Equals(_currentVideoPath, vid, StringComparison.OrdinalIgnoreCase) &&
                    _ffmpegProc != null && !_ffmpegProc.HasExited)
                {
                    return;
                }
            }

            bool switchingVideo = _hasLoadedVideo && _hasRenderedAnyVideoFrame &&
                !string.Equals(_currentVideoPath, vid, StringComparison.OrdinalIgnoreCase);

            if (switchingVideo)
            {
                lock (_videoBufLock)
                {
                    _prevVideoBitmap.Lock();
                    Marshal.Copy(_videoFrontBuf, 0, _prevVideoBitmap.BackBuffer, _videoFrontBuf.Length);
                    _prevVideoBitmap.AddDirtyRect(new Int32Rect(0, 0, VidW, VidH));
                    _prevVideoBitmap.Unlock();
                }
                _prevVideoImage.Visibility = Visibility.Visible;
                double w = Width > 0 ? Width : SystemParameters.PrimaryScreenWidth;
                double h = Height > 0 ? Height : SystemParameters.PrimaryScreenHeight;
                _videoImage.Clip = new EllipseGeometry(new Point(w * 0.5, h * 0.5), 0, 0);
                _pendingCircleReveal = true;
            }
            else if (!_hasLoadedVideo)
            {
                double w = Width > 0 ? Width : SystemParameters.PrimaryScreenWidth;
                double h = Height > 0 ? Height : SystemParameters.PrimaryScreenHeight;
                _videoImage.Clip = new EllipseGeometry(new Point(w * 0.5, h * 0.5), 0, 0);
                _pendingCircleReveal = true;
            }

            StopFfmpegProcess();
            _currentVideoPath = vid;
            _hasLoadedVideo = true;
            _videoImage.Visibility = Visibility.Visible;
            _lastSlideUtc = DateTime.UtcNow;

            if (_isAutoPaused) return;

            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = ffmpeg;
                psi.Arguments = "-hide_banner -loglevel error -hwaccel auto -re -stream_loop -1 -i \"" + vid +
                    "\" -an -vf \"scale=" + VidW + ":" + VidH + ":force_original_aspect_ratio=increase,crop=" + VidW + ":" + VidH +
                    "\" -r 30 -f rawvideo -pix_fmt bgr24 pipe:1";
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.CreateNoWindow = true;

                Process proc = new Process { StartInfo = psi };
                proc.Start();
                try { proc.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
                try
                {
                    if (_jobHandle != IntPtr.Zero) Native.AssignProcessToJobObject(_jobHandle, proc.Handle);
                }
                catch { }

                int myGen;
                lock (_ffmpegLock)
                {
                    _ffmpegProc = proc;
                    myGen = _ffmpegGen;
                }

                Stream outStream = proc.StandardOutput.BaseStream;
                Thread t = new Thread(delegate()
                {
                    int expected = VidW * VidH * 3;
                    byte[] readBuf = new byte[expected];
                    try
                    {
                        while (true)
                        {
                            lock (_ffmpegLock)
                            {
                                if (myGen != _ffmpegGen) break;
                            }
                            int offset = 0;
                            while (offset < expected)
                            {
                                int n = outStream.Read(readBuf, offset, expected - offset);
                                if (n <= 0) break;
                                offset += n;
                            }
                            if (offset < expected) break;

                            lock (_videoBufLock)
                            {
                                if (myGen != _ffmpegGen) break;
                                Buffer.BlockCopy(readBuf, 0, _videoBackBuf, 0, expected);
                                byte[] tmp = _videoFrontBuf;
                                _videoFrontBuf = _videoBackBuf;
                                _videoBackBuf = tmp;
                                _videoFrameReady = true;
                            }
                        }
                    }
                    catch { }
                });
                t.IsBackground = true;
                _ffmpegThread = t;
                t.Start();
            }
            catch
            {
                StopVideoPlayback();
            }
        }

        private string FindVideoPath()
        {
            if (!string.IsNullOrEmpty(_cfg.VideoPath) && File.Exists(_cfg.VideoPath))
                return _cfg.VideoPath;

            if (_videoList.Count > 0)
                return _videoList[0];

            return "";
        }

        public void ShowWallpaper()
        {
            SyncBounds();
            Show();
            ApplyMode();
            SendToBottom();
        }

        public void HideWallpaper()
        {
            StopFfmpegProcess();
            Hide();
        }

        public void ToggleWallpaper()
        {
            if (IsVisible) HideWallpaper();
            else ShowWallpaper();
        }

        public void CycleMode()
        {
            if (!IsVisible)
            {
                ShowWallpaper();
                return;
            }
            _cfg.Mode = (_cfg.Mode == 3) ? 1 : 3;
            _cfg.Save();
            ApplyMode();
        }

        public void CycleThreeState()
        {
            if (!IsVisible)
            {
                _cfg.Mode = 3;
                _cfg.Save();
                ShowWallpaper();
                return;
            }
            if (_cfg.Mode == 3)
            {
                _cfg.Mode = 1;
                _cfg.Save();
                ApplyMode();
            }
            else
            {
                HideWallpaper();
                Application app = Application.Current;
                if (app != null) app.Shutdown();
            }
        }

        public void ReloadFromDisk()
        {
            WallpaperConfig fresh = WallpaperConfig.Load();
            _cfg.Mode = fresh.Mode;
            _cfg.PhotoFolder = fresh.PhotoFolder;
            _cfg.PhotoPath = fresh.PhotoPath;
            _cfg.VideoPath = fresh.VideoPath;
            _cfg.VideoMuted = fresh.VideoMuted;
            _cfg.AutoPause = fresh.AutoPause;
            _cfg.VideoSlideshow = fresh.VideoSlideshow;
            _cfg.VideoSlideshowSec = fresh.VideoSlideshowSec;
            if (!IsVisible) ShowWallpaper();
            else ApplyMode();
        }
    }

    public static class Program
    {
        private const string MutexName = @"Local\YASB_LiveWallpaper_Mutex";
        private const string ToggleEventName = @"Local\YASB_LiveWallpaper_Toggle";
        private const string ModeEventName = @"Local\YASB_LiveWallpaper_Mode";
        private const string CycleStateEventName = @"Local\YASB_LiveWallpaper_CycleState";
        private const string ReloadEventName = @"Local\YASB_LiveWallpaper_Reload";
        private const string NextEventName = @"Local\YASB_LiveWallpaper_Next";
        private const string PrevEventName = @"Local\YASB_LiveWallpaper_Prev";
        private const string ExitEventName = @"Local\YASB_LiveWallpaper_Exit";

        [STAThread]
        public static void Main(string[] args)
        {
            WallpaperConfig cfg = WallpaperConfig.Load();
            bool wantToggle = false;
            bool wantMode = false;
            bool wantCycleState = false;
            bool wantReload = false;
            bool wantNext = false;
            bool wantPrev = false;
            bool wantExit = false;
            bool backgroundOnly = false;

            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (string.Equals(a, "--toggle", StringComparison.OrdinalIgnoreCase)) wantToggle = true;
                else if (string.Equals(a, "--mode", StringComparison.OrdinalIgnoreCase)) wantMode = true;
                else if (string.Equals(a, "--cycle-state", StringComparison.OrdinalIgnoreCase)) wantCycleState = true;
                else if (string.Equals(a, "--background", StringComparison.OrdinalIgnoreCase)) backgroundOnly = true;
                else if (string.Equals(a, "--off", StringComparison.OrdinalIgnoreCase)) wantExit = true;
                else if (string.Equals(a, "--next-photo", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(a, "--next-video", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(a, "--next", StringComparison.OrdinalIgnoreCase)) wantNext = true;
                else if (string.Equals(a, "--prev-video", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(a, "--prev", StringComparison.OrdinalIgnoreCase)) wantPrev = true;
                else if (string.Equals(a, "--mute-toggle", StringComparison.OrdinalIgnoreCase))
                {
                    cfg.VideoMuted = !cfg.VideoMuted;
                    cfg.Save();
                    wantReload = true;
                }
                else if (string.Equals(a, "--slideshow-toggle", StringComparison.OrdinalIgnoreCase))
                {
                    cfg.VideoSlideshow = !cfg.VideoSlideshow;
                    cfg.Save();
                    wantReload = true;
                }
                else if (string.Equals(a, "--set-mode", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    int m;
                    if (int.TryParse(args[++i], out m))
                    {
                        cfg.Mode = WallpaperConfig.NormalizeMode(m);
                        cfg.Save();
                        wantReload = true;
                    }
                }
                else if (string.Equals(a, "--set-video", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    string vp = args[++i];
                    if (File.Exists(vp))
                    {
                        cfg.VideoPath = vp;
                        cfg.Mode = 3;
                        cfg.Save();
                        wantReload = true;
                    }
                }
                else if (string.Equals(a, "--set-photo", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    string pp = args[++i];
                    if (File.Exists(pp))
                    {
                        cfg.PhotoPath = pp;
                        cfg.PhotoFolder = Path.GetDirectoryName(pp);
                        cfg.Mode = 1;
                        cfg.Save();
                        wantReload = true;
                    }
                }
                else if (string.Equals(a, "--pick-video", StringComparison.OrdinalIgnoreCase))
                {
                    OpenFileDialog dlg = new OpenFileDialog();
                    dlg.Title = "Выберите видео или GIF для живых обоев";
                    dlg.Filter = "Видео и GIF|*.mp4;*.mkv;*.webm;*.mov;*.wmv;*.avi;*.gif|Все файлы|*.*";
                    if (Directory.Exists(WallpaperConfig.DefaultVideoFolder))
                        dlg.InitialDirectory = WallpaperConfig.DefaultVideoFolder;
                    if (dlg.ShowDialog() == true)
                    {
                        cfg.VideoPath = dlg.FileName;
                        cfg.Mode = 3;
                        cfg.Save();
                        wantReload = true;
                    }
                    else
                    {
                        return;
                    }
                }
                else if (string.Equals(a, "--pick-folder", StringComparison.OrdinalIgnoreCase))
                {
                    OpenFileDialog dlg = new OpenFileDialog();
                    dlg.Title = "Выберите любую картинку из нужной папки обоев";
                    dlg.Filter = "Изображения|*.jpg;*.jpeg;*.png;*.webp;*.bmp|Все файлы|*.*";
                    string initDir = Directory.Exists(cfg.PhotoFolder) ? cfg.PhotoFolder : @"C:\Users\Laziko\Pictures";
                    if (Directory.Exists(initDir)) dlg.InitialDirectory = initDir;
                    if (dlg.ShowDialog() == true)
                    {
                        cfg.PhotoFolder = Path.GetDirectoryName(dlg.FileName);
                        cfg.PhotoPath = dlg.FileName;
                        cfg.Mode = 1;
                        cfg.Save();
                        wantReload = true;
                    }
                    else
                    {
                        return;
                    }
                }
            }

            bool createdNew;
            using (Mutex mutex = new Mutex(true, MutexName, out createdNew))
            {
                if (!createdNew)
                {
                    string evName = ToggleEventName;
                    if (wantExit) evName = ExitEventName;
                    else if (wantReload) evName = ReloadEventName;
                    else if (wantPrev) evName = PrevEventName;
                    else if (wantNext) evName = NextEventName;
                    else if (wantCycleState) evName = CycleStateEventName;
                    else if (wantMode) evName = ModeEventName;
                    else if (wantToggle) evName = ToggleEventName;

                    try
                    {
                        using (EventWaitHandle ev = EventWaitHandle.OpenExisting(evName))
                            ev.Set();
                    }
                    catch { }
                    return;
                }

                if (wantExit) return;

                if (wantCycleState)
                {
                    cfg.Mode = 3;
                    cfg.Save();
                }

                EventWaitHandle toggleEv = new EventWaitHandle(false, EventResetMode.AutoReset, ToggleEventName);
                EventWaitHandle modeEv = new EventWaitHandle(false, EventResetMode.AutoReset, ModeEventName);
                EventWaitHandle cycleStateEv = new EventWaitHandle(false, EventResetMode.AutoReset, CycleStateEventName);
                EventWaitHandle reloadEv = new EventWaitHandle(false, EventResetMode.AutoReset, ReloadEventName);
                EventWaitHandle nextEv = new EventWaitHandle(false, EventResetMode.AutoReset, NextEventName);
                EventWaitHandle prevEv = new EventWaitHandle(false, EventResetMode.AutoReset, PrevEventName);
                EventWaitHandle exitEv = new EventWaitHandle(false, EventResetMode.AutoReset, ExitEventName);

                Application app = new Application();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;

                LiveWallpaperWindow win = new LiveWallpaperWindow(cfg);
                if (!backgroundOnly) win.ShowWallpaper();

                ThreadPool.RegisterWaitForSingleObject(toggleEv, delegate(object s, bool timedOut)
                {
                    if (!timedOut) win.Dispatcher.BeginInvoke((Action)delegate { win.ToggleWallpaper(); });
                }, null, -1, false);

                ThreadPool.RegisterWaitForSingleObject(modeEv, delegate(object s, bool timedOut)
                {
                    if (!timedOut) win.Dispatcher.BeginInvoke((Action)delegate { win.CycleMode(); });
                }, null, -1, false);

                ThreadPool.RegisterWaitForSingleObject(cycleStateEv, delegate(object s, bool timedOut)
                {
                    if (!timedOut) win.Dispatcher.BeginInvoke((Action)delegate { win.CycleThreeState(); });
                }, null, -1, false);

                ThreadPool.RegisterWaitForSingleObject(reloadEv, delegate(object s, bool timedOut)
                {
                    if (!timedOut) win.Dispatcher.BeginInvoke((Action)delegate { win.ReloadFromDisk(); });
                }, null, -1, false);

                ThreadPool.RegisterWaitForSingleObject(nextEv, delegate(object s, bool timedOut)
                {
                    if (!timedOut) win.Dispatcher.BeginInvoke((Action)delegate { win.StepWallpaper(1); });
                }, null, -1, false);

                ThreadPool.RegisterWaitForSingleObject(prevEv, delegate(object s, bool timedOut)
                {
                    if (!timedOut) win.Dispatcher.BeginInvoke((Action)delegate { win.StepWallpaper(-1); });
                }, null, -1, false);

                ThreadPool.RegisterWaitForSingleObject(exitEv, delegate(object s, bool timedOut)
                {
                    if (!timedOut) win.Dispatcher.BeginInvoke((Action)delegate { app.Shutdown(); });
                }, null, -1, false);

                app.Run();

                toggleEv.Close();
                modeEv.Close();
                cycleStateEv.Close();
                reloadEv.Close();
                nextEv.Close();
                prevEv.Close();
                exitEv.Close();
            }
        }
    }
}
