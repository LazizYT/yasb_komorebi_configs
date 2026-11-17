using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace YasbWallpaperGallery
{
    internal static class Native
    {
        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_TOOLWINDOW = 0x00000080;

        [DllImport("user32.dll")]
        public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hWnd);
    }

    public class GalleryWindow : Window
    {
        private static readonly string ConfigDir = @"C:\Users\Laziko\.config\yasb";
        private static readonly string WallExe = System.IO.Path.Combine(ConfigDir, "live_wallpaper.exe");
        private static readonly string WallCfg = System.IO.Path.Combine(ConfigDir, "live_wallpaper.cfg");
        private static readonly string ThumbsDir = System.IO.Path.Combine(ConfigDir, "video_thumbs");
        private static readonly string VideoDir = @"C:\Users\Laziko\Pictures\LiveWallpapers";
        private static readonly string DefaultPhotoDir = @"C:\Users\Laziko\Pictures\1. Nature Wallpapers";

        private static readonly Color BgWindow = Color.FromArgb(0xEA, 0x10, 0x0E, 0x1C);
        private static readonly Color BorderSubtle = Color.FromArgb(0x3E, 0xB4, 0xBE, 0xFE);
        private static readonly Color AccentSakura = Color.FromArgb(0xFF, 0xFF, 0x7E, 0xB6);
        private static readonly Color AccentCyan = Color.FromArgb(0xFF, 0x6C, 0xE5, 0xFF);
        private static readonly Color AccentGold = Color.FromArgb(0xFF, 0xFF, 0xD4, 0x79);

        private const double CardW = 248.0;
        private const double CardH = 278.0;
        private const double SlantDx = 64.0;
        private const double CardGap = 14.0;
        private const double CardStep = CardW - SlantDx + CardGap;

        private Border _rootBorder;
        private ScaleTransform _scaleTransform;
        private ScrollViewer _scroll;
        private StackPanel _strip;

        private readonly Dictionary<string, BitmapImage> _thumbCache = new Dictionary<string, BitmapImage>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _items = new List<string>();
        private readonly List<System.Windows.Shapes.Path> _cardBorders = new List<System.Windows.Shapes.Path>();
        private readonly List<Image> _cardImages = new List<Image>();

        private string _activeVideoPath = "";
        private string _activePhotoPath = "";
        private string _photoFolder = DefaultPhotoDir;
        private int _cfgMode = 3;
        private bool _showingVideo = true;
        private int _selectedIndex = 0;
        private int _loadGen = 0;
        private bool _isVisible = false;
        private DateTime _shownUtc = DateTime.MinValue;

        public GalleryWindow()
        {
            Title = "Yozora Carousel Gallery";
            Height = 326;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;

            BuildUi();
            PositionWindow();

            SourceInitialized += OnSourceInitialized;
            PreviewKeyDown += OnWindowKeyDown;
            Deactivated += delegate
            {
                if (_isVisible && (DateTime.UtcNow - _shownUtc).TotalMilliseconds > 350)
                    HideGallery();
            };
        }

        private static bool IsLiveWallpaperVisible()
        {
            Process[] ps = Process.GetProcessesByName("live_wallpaper");
            bool running = ps.Length > 0;
            foreach (Process p in ps) p.Dispose();
            if (!running) return false;

            IntPtr lw = Native.FindWindow(null, "YASB Live Wallpaper");
            return lw != IntPtr.Zero && Native.IsWindowVisible(lw);
        }

        private void PositionWindow()
        {
            Rect wa = SystemParameters.WorkArea;
            Width = Math.Min(wa.Width - 100, 1200);
            Left = wa.Left + (wa.Width - Width) / 2.0;
            Top = wa.Top + (wa.Height - Height) / 2.0;
        }

        private void OnSourceInitialized(object sender, EventArgs e)
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            int ex = Native.GetWindowLong(hwnd, Native.GWL_EXSTYLE);
            Native.SetWindowLong(hwnd, Native.GWL_EXSTYLE, ex | Native.WS_EX_TOOLWINDOW);
        }

        private void ReadState()
        {
            _cfgMode = 3;
            _activeVideoPath = "";
            _activePhotoPath = "";
            _photoFolder = DefaultPhotoDir;
            try
            {
                if (File.Exists(WallCfg))
                {
                    string[] lines = File.ReadAllLines(WallCfg, Encoding.UTF8);
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
                            if (int.TryParse(v, out m)) _cfgMode = (m == 1) ? 1 : 3;
                        }
                        else if (k == "video_path") _activeVideoPath = v;
                        else if (k == "photo_path") _activePhotoPath = v;
                        else if (k == "photo_folder" && v.Length > 0) _photoFolder = v;
                    }
                }
            }
            catch { }

            _showingVideo = IsLiveWallpaperVisible() && (_cfgMode == 3);
        }

        private void BuildUi()
        {
            _scaleTransform = new ScaleTransform(1.0, 1.0);
            _rootBorder = new Border
            {
                Background = new SolidColorBrush(BgWindow),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0x7E, 0xB6)),
                BorderThickness = new Thickness(1.2),
                CornerRadius = new CornerRadius(18),
                Padding = new Thickness(16, 14, 16, 14),
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = _scaleTransform
            };

            Grid shell = new Grid();

            _scroll = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            _scroll.PreviewMouseWheel += delegate(object sender, MouseWheelEventArgs e)
            {
                MoveSelection(e.Delta < 0 ? 1 : -1);
                e.Handled = true;
            };

            _strip = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(40, 0, 40, 0)
            };
            _scroll.Content = _strip;
            shell.Children.Add(_scroll);

            // Sleek Left / Right navigation buttons
            Button btnLeft = MakeNavButton("‹", HorizontalAlignment.Left, delegate { MoveSelection(-1); });
            Button btnRight = MakeNavButton("›", HorizontalAlignment.Right, delegate { MoveSelection(1); });
            shell.Children.Add(btnLeft);
            shell.Children.Add(btnRight);

            _rootBorder.Child = shell;
            Content = _rootBorder;
        }

        private static Button MakeNavButton(string symbol, HorizontalAlignment align, RoutedEventHandler onClick)
        {
            Button b = new Button
            {
                Content = symbol,
                Width = 38,
                Height = 76,
                HorizontalAlignment = align,
                VerticalAlignment = VerticalAlignment.Center,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 26,
                Foreground = new SolidColorBrush(Color.FromRgb(242, 238, 255)),
                Background = new SolidColorBrush(Color.FromArgb(0xB8, 0x18, 0x15, 0x2B)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 0xB4, 0xBE, 0xFE)),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                Focusable = false
            };
            b.Click += onClick;
            return b;
        }

        private static Geometry CreateParallelogramGeo(double w, double h, double dx)
        {
            StreamGeometry geo = new StreamGeometry();
            using (StreamGeometryContext ctx = geo.Open())
            {
                ctx.BeginFigure(new Point(dx, 0), true, true);
                ctx.LineTo(new Point(w, 0), true, false);
                ctx.LineTo(new Point(w - dx, h), true, false);
                ctx.LineTo(new Point(0, h), true, false);
            }
            geo.Freeze();
            return geo;
        }

        private void PopulateCarousel(int forceKind) // -1 auto, 0 photo, 1 video
        {
            _loadGen++;
            int myGen = _loadGen;

            _strip.Children.Clear();
            _items.Clear();
            _cardBorders.Clear();
            _cardImages.Clear();

            if (forceKind == 1) _showingVideo = true;
            else if (forceKind == 0) _showingVideo = false;

            if (_showingVideo)
            {
                try
                {
                    if (Directory.Exists(VideoDir))
                    {
                        string[] files = Directory.GetFiles(VideoDir);
                        Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                        for (int i = 0; i < files.Length; i++)
                        {
                            string ext = System.IO.Path.GetExtension(files[i]).ToLowerInvariant();
                            if (ext == ".mp4" || ext == ".mkv" || ext == ".webm" || ext == ".mov" || ext == ".wmv" || ext == ".avi" || ext == ".gif")
                                _items.Add(files[i]);
                        }
                    }
                }
                catch { }
            }
            else
            {
                string dir = Directory.Exists(_photoFolder) ? _photoFolder : DefaultPhotoDir;
                try
                {
                    if (Directory.Exists(dir))
                    {
                        string[] files = Directory.GetFiles(dir);
                        Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                        for (int i = 0; i < files.Length; i++)
                        {
                            string ext = System.IO.Path.GetExtension(files[i]).ToLowerInvariant();
                            if (ext == ".jpg" || ext == ".jpeg" || ext == ".png" || ext == ".bmp" || ext == ".webp")
                                _items.Add(files[i]);
                        }
                    }
                }
                catch { }
            }

            Geometry paraGeo = CreateParallelogramGeo(CardW, CardH, SlantDx);
            List<string> missingVideoThumbs = new List<string>();
            List<KeyValuePair<int, string>> asyncLoadQueue = new List<KeyValuePair<int, string>>();

            _selectedIndex = 0;
            for (int i = 0; i < _items.Count; i++)
            {
                string itemPath = _items[i];
                bool active = _showingVideo
                    ? string.Equals(itemPath, _activeVideoPath, StringComparison.OrdinalIgnoreCase)
                    : string.Equals(itemPath, _activePhotoPath, StringComparison.OrdinalIgnoreCase);
                if (active) _selectedIndex = i;
            }

            for (int i = 0; i < _items.Count; i++)
            {
                int idx = i;
                string itemPath = _items[i];
                string thumbPath = _showingVideo
                    ? System.IO.Path.Combine(ThumbsDir, System.IO.Path.GetFileName(itemPath) + ".jpg")
                    : itemPath;

                if (_showingVideo && !File.Exists(thumbPath))
                    missingVideoThumbs.Add(itemPath);

                Grid card = new Grid
                {
                    Width = CardW,
                    Height = CardH,
                    Margin = new Thickness(i == 0 ? 0 : -(SlantDx - CardGap), 0, 0, 0),
                    Clip = paraGeo,
                    Background = new SolidColorBrush(Color.FromRgb(20, 18, 36)),
                    Tag = idx,
                    Cursor = Cursors.Hand
                };

                Image img = new Image
                {
                    Width = CardW,
                    Height = CardH,
                    Stretch = Stretch.UniformToFill
                };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);

                BitmapImage cached;
                if (_thumbCache.TryGetValue(thumbPath, out cached))
                    img.Source = cached;
                else if (File.Exists(thumbPath))
                    asyncLoadQueue.Add(new KeyValuePair<int, string>(idx, thumbPath));

                card.Children.Add(img);
                _cardImages.Add(img);

                System.Windows.Shapes.Path border = new System.Windows.Shapes.Path
                {
                    Data = paraGeo,
                    Stroke = new SolidColorBrush(BorderSubtle),
                    StrokeThickness = 1.5,
                    IsHitTestVisible = false
                };
                card.Children.Add(border);
                _cardBorders.Add(border);

                card.MouseEnter += delegate(object sender, MouseEventArgs e)
                {
                    Grid g = sender as Grid;
                    if (g == null || !(g.Tag is int)) return;
                    int hovered = (int)g.Tag;
                    if (hovered != _selectedIndex)
                    {
                        _selectedIndex = hovered;
                        UpdateSelectionVisuals(false);
                    }
                };

                card.MouseLeftButtonUp += delegate(object sender, MouseButtonEventArgs e)
                {
                    Grid g = sender as Grid;
                    if (g != null && g.Tag is int)
                        _selectedIndex = (int)g.Tag;
                    ApplySelected();
                };

                _strip.Children.Add(card);
            }

            UpdateSelectionVisuals(true);

            // Asynchronously decode & cache thumbnails prioritized outward from _selectedIndex (zero UI freeze!)
            if (asyncLoadQueue.Count > 0)
            {
                int center = _selectedIndex;
                asyncLoadQueue.Sort(delegate(KeyValuePair<int, string> a, KeyValuePair<int, string> b)
                {
                    return Math.Abs(a.Key - center).CompareTo(Math.Abs(b.Key - center));
                });

                Thread loader = new Thread(delegate()
                {
                    for (int q = 0; q < asyncLoadQueue.Count; q++)
                    {
                        if (myGen != _loadGen) return;
                        int cardIdx = asyncLoadQueue[q].Key;
                        string path = asyncLoadQueue[q].Value;
                        BitmapImage bmp = DecodeThumbSafe(path, 320);
                        if (bmp == null || myGen != _loadGen) continue;

                        Dispatcher.BeginInvoke(DispatcherPriority.Background, (Action)delegate
                        {
                            if (myGen != _loadGen) return;
                            _thumbCache[path] = bmp;
                            if (cardIdx >= 0 && cardIdx < _cardImages.Count)
                                _cardImages[cardIdx].Source = bmp;
                        });
                    }
                });
                loader.IsBackground = true;
                loader.Start();
            }

            if (missingVideoThumbs.Count > 0)
            {
                Thread bg = new Thread(delegate() { GenerateMissingThumbs(missingVideoThumbs); });
                bg.IsBackground = true;
                bg.Start();
            }
        }

        private void MoveSelection(int delta)
        {
            if (_items.Count == 0) return;
            _selectedIndex = (_selectedIndex + delta) % _items.Count;
            if (_selectedIndex < 0) _selectedIndex += _items.Count;
            UpdateSelectionVisuals(true);
        }

        private void UpdateSelectionVisuals(bool scrollToSelected)
        {
            string activePath = _showingVideo ? _activeVideoPath : _activePhotoPath;
            for (int i = 0; i < _cardBorders.Count; i++)
            {
                bool isSel = (i == _selectedIndex);
                bool isCurrent = string.Equals(_items[i], activePath, StringComparison.OrdinalIgnoreCase);

                if (isSel)
                {
                    _cardBorders[i].Stroke = new SolidColorBrush(AccentSakura);
                    _cardBorders[i].StrokeThickness = 3.4;
                }
                else if (isCurrent)
                {
                    _cardBorders[i].Stroke = new SolidColorBrush(AccentGold);
                    _cardBorders[i].StrokeThickness = 2.4;
                }
                else
                {
                    _cardBorders[i].Stroke = new SolidColorBrush(BorderSubtle);
                    _cardBorders[i].StrokeThickness = 1.4;
                }
            }

            if (scrollToSelected && _items.Count > 0)
            {
                double cardCenter = 40.0 + _selectedIndex * CardStep + CardW * 0.5;
                double viewW = Math.Max(400.0, Width - 32.0);
                double target = Math.Max(0.0, cardCenter - viewW * 0.5);
                _scroll.ScrollToHorizontalOffset(target);
            }
        }

        private void ApplySelected()
        {
            if (_selectedIndex < 0 || _selectedIndex >= _items.Count) return;
            string sel = _items[_selectedIndex];
            if (string.IsNullOrEmpty(sel)) return;

            if (_showingVideo)
            {
                _activeVideoPath = sel;
                RunWall("--set-video \"" + sel + "\"");
            }
            else
            {
                _activePhotoPath = sel;
                RunWall("--set-photo \"" + sel + "\"");
            }
            HideGallery();
        }

        private void GenerateMissingThumbs(List<string> paths)
        {
            string ff = @"C:\Users\Laziko\AppData\Roaming\Python\Python315\Scripts\ffmpeg.exe";
            if (!File.Exists(ff)) return;
            try { Directory.CreateDirectory(ThumbsDir); } catch { }

            for (int i = 0; i < paths.Count; i++)
            {
                try
                {
                    string vp = paths[i];
                    string outPath = System.IO.Path.Combine(ThumbsDir, System.IO.Path.GetFileName(vp) + ".jpg");
                    if (File.Exists(outPath)) continue;
                    ProcessStartInfo psi = new ProcessStartInfo
                    {
                        FileName = ff,
                        Arguments = "-hide_banner -loglevel error -y -ss 00:00:01 -i \"" + vp + "\" -frames:v 1 -vf \"scale=560:315:force_original_aspect_ratio=increase,crop=560:315\" -q:v 2 \"" + outPath + "\"",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using (Process p = Process.Start(psi))
                        p.WaitForExit(4000);
                }
                catch { }
            }
        }

        private static BitmapImage DecodeThumbSafe(string path, int height)
        {
            try
            {
                BitmapImage bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bmp.DecodePixelHeight = height;
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch { return null; }
        }

        private static void RunWall(string args)
        {
            try
            {
                if (!File.Exists(WallExe)) return;
                Process.Start(new ProcessStartInfo
                {
                    FileName = WallExe,
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
            }
            catch { }
        }

        private void OnWindowKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                HideGallery();
                e.Handled = true;
            }
            else if (e.Key == Key.Left || e.Key == Key.A)
            {
                MoveSelection(-1);
                e.Handled = true;
            }
            else if (e.Key == Key.Right || e.Key == Key.D)
            {
                MoveSelection(1);
                e.Handled = true;
            }
            else if (e.Key == Key.Enter || e.Key == Key.Space)
            {
                ApplySelected();
                e.Handled = true;
            }
        }

        public void ToggleGallery(int forceKind)
        {
            if (_isVisible) HideGallery();
            else ShowGallery(forceKind);
        }

        public void ShowGallery(int forceKind)
        {
            ReadState();
            PopulateCarousel(forceKind);
            PositionWindow();
            _shownUtc = DateTime.UtcNow;
            _isVisible = true;
            Opacity = 0;
            Visibility = Visibility.Visible;
            Show();
            Activate();
            Focus();
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero) Native.SetForegroundWindow(hwnd);

            DoubleAnimation fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(130));
            DoubleAnimation sx = new DoubleAnimation(0.96, 1.0, TimeSpan.FromMilliseconds(150));
            sx.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            BeginAnimation(OpacityProperty, fade);
            _scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, sx);
            _scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, sx);
        }

        public void HideGallery()
        {
            if (!_isVisible) return;
            _isVisible = false;
            DoubleAnimation fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(95));
            fade.Completed += delegate { if (!_isVisible) Visibility = Visibility.Hidden; };
            BeginAnimation(OpacityProperty, fade);
        }
    }

    public static class Program
    {
        private const string MutexName = @"Local\YASB_WallpaperGallery_Mutex";
        private const string ToggleEventName = @"Local\YASB_WallpaperGallery_Toggle";
        private const string VideoEventName = @"Local\YASB_WallpaperGallery_Video";
        private const string PhotoEventName = @"Local\YASB_WallpaperGallery_Photo";

        [STAThread]
        public static void Main(string[] args)
        {
            bool background = false;
            int forceKind = -1;
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], "--background", StringComparison.OrdinalIgnoreCase))
                    background = true;
                else if (string.Equals(args[i], "--video", StringComparison.OrdinalIgnoreCase))
                    forceKind = 1;
                else if (string.Equals(args[i], "--photo", StringComparison.OrdinalIgnoreCase))
                    forceKind = 0;
            }

            bool createdNew;
            using (Mutex mutex = new Mutex(true, MutexName, out createdNew))
            {
                if (!createdNew)
                {
                    string evName = ToggleEventName;
                    if (forceKind == 1) evName = VideoEventName;
                    else if (forceKind == 0) evName = PhotoEventName;
                    try
                    {
                        using (EventWaitHandle ev = EventWaitHandle.OpenExisting(evName))
                            ev.Set();
                    }
                    catch { }
                    return;
                }

                EventWaitHandle toggleEv = new EventWaitHandle(false, EventResetMode.AutoReset, ToggleEventName);
                EventWaitHandle videoEv = new EventWaitHandle(false, EventResetMode.AutoReset, VideoEventName);
                EventWaitHandle photoEv = new EventWaitHandle(false, EventResetMode.AutoReset, PhotoEventName);
                Application app = new Application();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;

                GalleryWindow win = new GalleryWindow();
                if (!background) win.ShowGallery(forceKind);

                ThreadPool.RegisterWaitForSingleObject(toggleEv, delegate(object state, bool timedOut)
                {
                    if (!timedOut) win.Dispatcher.BeginInvoke((Action)delegate { win.ToggleGallery(-1); });
                }, null, -1, false);

                ThreadPool.RegisterWaitForSingleObject(videoEv, delegate(object state, bool timedOut)
                {
                    if (!timedOut) win.Dispatcher.BeginInvoke((Action)delegate { win.ToggleGallery(1); });
                }, null, -1, false);

                ThreadPool.RegisterWaitForSingleObject(photoEv, delegate(object state, bool timedOut)
                {
                    if (!timedOut) win.Dispatcher.BeginInvoke((Action)delegate { win.ToggleGallery(0); });
                }, null, -1, false);

                app.Run();
                toggleEv.Close();
                videoEv.Close();
                photoEv.Close();
            }
        }
    }
}
