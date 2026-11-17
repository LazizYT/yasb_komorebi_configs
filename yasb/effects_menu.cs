using System;
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
using System.Windows.Media.Effects;

namespace YasbEffectsMenu
{
    internal static class NativeMethods
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

    public class EffectsWindow : Window
    {
        private static readonly string ConfigDir = @"C:\Users\Laziko\.config\yasb";
        private static readonly string VizExe = Path.Combine(ConfigDir, "audio_visualizer.exe");
        private static readonly string WallExe = Path.Combine(ConfigDir, "live_wallpaper.exe");
        private static readonly string GalleryExe = Path.Combine(ConfigDir, "wallpaper_gallery.exe");
        private static readonly string LauncherExe = Path.Combine(ConfigDir, "komorebi_launcher.exe");
        private static readonly string WallCfg = Path.Combine(ConfigDir, "live_wallpaper.cfg");

        private static readonly Color BgWindow = Color.FromArgb(0xF6, 0x13, 0x11, 0x22);
        private static readonly Color BgCard = Color.FromArgb(0xFF, 0x1A, 0x17, 0x2E);
        private static readonly Color BorderSakura = Color.FromArgb(0x55, 0xFF, 0x7E, 0xB6);
        private static readonly Color BorderSubtle = Color.FromArgb(0x28, 0xB4, 0xBE, 0xFE);
        private static readonly Color AccentSakura = Color.FromArgb(0xFF, 0xFF, 0x7E, 0xB6);
        private static readonly Color AccentCyan = Color.FromArgb(0xFF, 0x6C, 0xE5, 0xFF);
        private static readonly Color AccentGold = Color.FromArgb(0xFF, 0xFF, 0xD4, 0x79);
        private static readonly Color TextPrimary = Color.FromArgb(0xFF, 0xF2, 0xEE, 0xFF);
        private static readonly Color TextMuted = Color.FromArgb(0xFF, 0x8E, 0x88, 0xAA);

        private static readonly string[] WallModeLabels = new string[]
        {
            "", "Фото", "", "Видео"
        };

        private Border _rootBorder;
        private TranslateTransform _slideTransform;
        private readonly Button[] _wallModeBtns = new Button[4];
        private Button _staticWallBtn;
        private Button _slideshowBtn;
        private Button _pauseBtn;
        private int _wallMode = 3;
        private bool _videoSlideshow = true;
        private bool _wallRunning = false;
        private bool _isVisible = false;
        private DateTime _shownUtc = DateTime.MinValue;

        public EffectsWindow()
        {
            Title = "Yozora Switcher";
            Width = 356;
            SizeToContent = SizeToContent.Height;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;

            IntPtr lw = NativeMethods.FindWindow(null, "YASB Live Wallpaper");
            _wallRunning = IsRunning("live_wallpaper") && lw != IntPtr.Zero && NativeMethods.IsWindowVisible(lw);
            ReadWallCfg();
            BuildUi();
            PositionDrawer();

            SourceInitialized += OnSourceInitialized;
            PreviewKeyDown += OnWindowKeyDown;
            Deactivated += delegate
            {
                if (_isVisible && (DateTime.UtcNow - _shownUtc).TotalMilliseconds > 350)
                    HideAndExit();
            };
        }

        private void ReadWallCfg()
        {
            _wallMode = 3;
            _videoSlideshow = true;
            try
            {
                if (!File.Exists(WallCfg)) return;
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
                        if (int.TryParse(v, out m)) _wallMode = (m == 1) ? 1 : 3;
                    }
                    else if (k == "video_slideshow")
                    {
                        _videoSlideshow = (v != "0" && v.ToLowerInvariant() != "false");
                    }
                }
            }
            catch { }
        }

        private void PositionDrawer()
        {
            Rect wa = SystemParameters.WorkArea;
            Left = wa.Right - Width - 16;
            Top = wa.Top + 8;
        }

        private void OnSourceInitialized(object sender, EventArgs e)
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            int ex = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
            NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, ex | NativeMethods.WS_EX_TOOLWINDOW);
        }

        private void BuildUi()
        {
            _slideTransform = new TranslateTransform(0, 0);
            _rootBorder = new Border
            {
                Background = new SolidColorBrush(BgWindow),
                BorderBrush = new SolidColorBrush(BorderSakura),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(12),
                Margin = new Thickness(4, 2, 4, 10),
                RenderTransform = _slideTransform,
                Effect = new DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 22,
                    ShadowDepth = 5,
                    Opacity = 0.68
                }
            };

            StackPanel root = new StackPanel();

            // Compact header
            Grid hdr = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            hdr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            hdr.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            hdr.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel titleLeft = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            titleLeft.Children.Add(new Border
            {
                Width = 8,
                Height = 8,
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(AccentSakura),
                Margin = new Thickness(0, 0, 7, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            titleLeft.Children.Add(new TextBlock
            {
                Text = "ПЕРЕКЛЮЧЕНИЕ",
                FontFamily = new FontFamily("Segoe UI Semibold"),
                FontSize = 11.2,
                Foreground = new SolidColorBrush(TextPrimary),
                VerticalAlignment = VerticalAlignment.Center
            });
            hdr.Children.Add(titleLeft);

            Button reloadWhkd = CreateSmallBtn("↻ whkd", delegate
            {
                RunTool(LauncherExe, "--reload-whkd");
                ((Button)reloadWhkdBtnRef).Content = "✓ whkd";
            });
            reloadWhkdBtnRef = reloadWhkd;
            reloadWhkd.Margin = new Thickness(0, 0, 6, 0);
            Grid.SetColumn(reloadWhkd, 1);
            hdr.Children.Add(reloadWhkd);

            Button killAll = CreateBtn("Выкл все", AccentSakura, delegate
            {
                StopAllEffects();
                HideAndExit();
            });
            Grid.SetColumn(killAll, 2);
            hdr.Children.Add(killAll);
            root.Children.Add(hdr);

            // Wallpaper Switcher Card (pure buttons, no filenames)
            Border wallCard = new Border
            {
                Background = new SolidColorBrush(BgCard),
                BorderBrush = new SolidColorBrush(BorderSubtle),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(9),
                Margin = new Thickness(0, 0, 0, 7)
            };
            StackPanel wsp = new StackPanel();

            // Row 1: Видео / Фото / Картина (выкл живые обои)
            Grid modesGrid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            for (int c = 0; c < 3; c++)
                modesGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            int[] modeOrder = new int[] { 3, 1 };
            for (int c = 0; c < 2; c++)
            {
                int m = modeOrder[c];
                Button mb = new Button
                {
                    Content = WallModeLabels[m],
                    Tag = m,
                    FontFamily = new FontFamily("Segoe UI Semibold"),
                    FontSize = 11,
                    Padding = new Thickness(6, 6, 6, 6),
                    Margin = new Thickness(c == 0 ? 0 : 3, 0, 3, 0),
                    BorderThickness = new Thickness(1),
                    Cursor = Cursors.Hand
                };
                mb.Click += delegate(object sender, RoutedEventArgs e)
                {
                    int picked = (int)((Button)sender).Tag;
                    _wallMode = picked;
                    _wallRunning = true;
                    RunTool(WallExe, "--set-mode " + picked.ToString());
                    UpdateButtons();
                };
                _wallModeBtns[m] = mb;
                Grid.SetColumn(mb, c);
                modesGrid.Children.Add(mb);
            }

            _staticWallBtn = MakeCellBtn("Картина (Выкл)", delegate
            {
                _wallRunning = false;
                RunTool(WallExe, "--off");
                UpdateButtons();
            }, 3, 0);
            _staticWallBtn.FontSize = 11;
            _staticWallBtn.Padding = new Thickness(6, 6, 6, 6);
            Grid.SetColumn(_staticWallBtn, 2);
            modesGrid.Children.Add(_staticWallBtn);
            wsp.Children.Add(modesGrid);

            // Row 2: Prev / Next / Auto / Pause
            Grid ctrlGrid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            for (int c = 0; c < 4; c++)
                ctrlGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Button bPrev = MakeCellBtn("◀ Пред.", delegate
            {
                _wallRunning = true;
                RunTool(WallExe, "--prev");
                UpdateButtons();
            }, 0, 2.5);
            Grid.SetColumn(bPrev, 0);
            ctrlGrid.Children.Add(bPrev);

            Button bNext = MakeCellBtn("След. ▶", delegate
            {
                _wallRunning = true;
                RunTool(WallExe, "--next");
                UpdateButtons();
            }, 2.5, 2.5);
            Grid.SetColumn(bNext, 1);
            ctrlGrid.Children.Add(bNext);

            _slideshowBtn = MakeCellBtn(_videoSlideshow ? "Авто: Вкл" : "Авто: Выкл", delegate
            {
                _videoSlideshow = !_videoSlideshow;
                _slideshowBtn.Content = _videoSlideshow ? "Авто: Вкл" : "Авто: Выкл";
                RunTool(WallExe, "--slideshow-toggle");
            }, 2.5, 2.5);
            Grid.SetColumn(_slideshowBtn, 2);
            ctrlGrid.Children.Add(_slideshowBtn);

            _pauseBtn = MakeCellBtn(_wallRunning ? "Пауза" : "Старт", delegate
            {
                _wallRunning = !_wallRunning;
                RunTool(WallExe, "--toggle");
                UpdateButtons();
            }, 2.5, 0);
            Grid.SetColumn(_pauseBtn, 3);
            ctrlGrid.Children.Add(_pauseBtn);

            wsp.Children.Add(ctrlGrid);

            // Row 3: Centered parallelogram carousel button
            Button galleryBtn = CreateBtn("Карусель (Ctrl+Alt+W)", AccentCyan, delegate
            {
                HideAndExit();
                RunTool(GalleryExe, "--toggle");
            });
            galleryBtn.HorizontalAlignment = HorizontalAlignment.Stretch;
            galleryBtn.HorizontalContentAlignment = HorizontalAlignment.Center;
            galleryBtn.Padding = new Thickness(8, 6, 8, 6);
            wsp.Children.Add(galleryBtn);

            wallCard.Child = wsp;
            root.Children.Add(wallCard);

            // Audio Visualizer row
            Border vizCard = new Border
            {
                Background = new SolidColorBrush(BgCard),
                BorderBrush = new SolidColorBrush(IsRunning("audio_visualizer") ? AccentSakura : BorderSubtle),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(9, 7, 9, 7)
            };
            Grid vg = new Grid();
            vg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            vg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            vg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            vg.Children.Add(new TextBlock
            {
                Text = "Визуализатор (Alt+Z)",
                FontFamily = new FontFamily("Segoe UI Semibold"),
                FontSize = 11.2,
                Foreground = new SolidColorBrush(TextPrimary),
                VerticalAlignment = VerticalAlignment.Center
            });

            Button bVizMode = CreateBtn("Режим", AccentCyan, delegate { RunTool(VizExe, "--mode"); });
            bVizMode.Margin = new Thickness(6, 0, 5, 0);
            Grid.SetColumn(bVizMode, 1);
            vg.Children.Add(bVizMode);

            bool vizOn = IsRunning("audio_visualizer");
            Button bVizToggle = CreateBtn(vizOn ? "Выкл" : "Вкл", AccentSakura, delegate
            {
                RunTool(VizExe, "--toggle");
                HideAndExit();
            });
            Grid.SetColumn(bVizToggle, 2);
            vg.Children.Add(bVizToggle);

            vizCard.Child = vg;
            root.Children.Add(vizCard);

            UpdateButtons();
            _rootBorder.Child = root;
            Content = _rootBorder;
        }

        private Button reloadWhkdBtnRef;

        private Button MakeCellBtn(string text, RoutedEventHandler onClick, double ml, double mr)
        {
            Button btn = CreateSmallBtn(text, onClick);
            btn.HorizontalAlignment = HorizontalAlignment.Stretch;
            btn.HorizontalContentAlignment = HorizontalAlignment.Center;
            btn.Padding = new Thickness(3, 5, 3, 5);
            btn.Margin = new Thickness(ml, 0, mr, 0);
            return btn;
        }

        private void UpdateButtons()
        {
            for (int i = 0; i < 4; i++)
            {
                if (_wallModeBtns[i] == null) continue;
                bool sel = _wallRunning && (i == _wallMode);
                _wallModeBtns[i].Background = new SolidColorBrush(sel
                    ? Color.FromArgb(0x3A, 0xFF, 0x7E, 0xB6)
                    : Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
                _wallModeBtns[i].BorderBrush = new SolidColorBrush(sel ? AccentSakura : BorderSubtle);
                _wallModeBtns[i].Foreground = new SolidColorBrush(sel ? TextPrimary : TextMuted);
            }

            if (_staticWallBtn != null)
            {
                bool st = !_wallRunning;
                _staticWallBtn.Background = new SolidColorBrush(st
                    ? Color.FromArgb(0x36, 0xFF, 0xD4, 0x79)
                    : Color.FromArgb(0x16, 0xFF, 0xFF, 0xFF));
                _staticWallBtn.BorderBrush = new SolidColorBrush(st ? AccentGold : BorderSubtle);
                _staticWallBtn.Foreground = new SolidColorBrush(st ? TextPrimary : TextMuted);
            }

            if (_pauseBtn != null)
                _pauseBtn.Content = _wallRunning ? "Пауза" : "Старт";
        }

        private Button CreateBtn(string text, Color accent, RoutedEventHandler onClick)
        {
            Button btn = new Button
            {
                Content = text,
                FontFamily = new FontFamily("Segoe UI Semibold"),
                FontSize = 11,
                Padding = new Thickness(9, 4, 9, 4),
                Background = new SolidColorBrush(Color.FromArgb(0x22, accent.R, accent.G, accent.B)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, accent.R, accent.G, accent.B)),
                BorderThickness = new Thickness(1),
                Foreground = new SolidColorBrush(TextPrimary),
                Cursor = Cursors.Hand
            };
            btn.Click += onClick;
            return btn;
        }

        private Button CreateSmallBtn(string text, RoutedEventHandler onClick)
        {
            Button btn = new Button
            {
                Content = text,
                FontFamily = new FontFamily("Segoe UI Semibold"),
                FontSize = 10,
                Padding = new Thickness(6, 4, 6, 4),
                Background = new SolidColorBrush(Color.FromArgb(0x16, 0xFF, 0xFF, 0xFF)),
                BorderBrush = new SolidColorBrush(BorderSubtle),
                BorderThickness = new Thickness(1),
                Foreground = new SolidColorBrush(TextPrimary),
                Cursor = Cursors.Hand
            };
            btn.Click += onClick;
            return btn;
        }

        private static bool IsRunning(string procName)
        {
            Process[] ps = Process.GetProcessesByName(procName);
            bool any = ps.Length > 0;
            foreach (Process p in ps) p.Dispose();
            return any;
        }

        private static void RunTool(string exePath, string arg)
        {
            try
            {
                if (!File.Exists(exePath)) return;
                Process.Start(new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = arg,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
            }
            catch { }
        }

        public static void StopAllEffects()
        {
            string[] names = new string[] { "audio_visualizer", "live_wallpaper" };
            foreach (string n in names)
            {
                Process[] ps = Process.GetProcessesByName(n);
                foreach (Process p in ps)
                {
                    try { p.Kill(); }
                    catch { }
                    finally { p.Dispose(); }
                }
            }
        }

        private void OnWindowKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                HideAndExit();
                e.Handled = true;
            }
        }

        public void ShowDrawer()
        {
            PositionDrawer();
            _shownUtc = DateTime.UtcNow;
            _isVisible = true;
            Opacity = 0;
            Visibility = Visibility.Visible;
            Show();
            Activate();
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero) NativeMethods.SetForegroundWindow(hwnd);

            DoubleAnimation fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(125));
            DoubleAnimation slide = new DoubleAnimation(-10, 0, TimeSpan.FromMilliseconds(145));
            slide.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            BeginAnimation(OpacityProperty, fade);
            _slideTransform.BeginAnimation(TranslateTransform.YProperty, slide);
        }

        public void HideAndExit()
        {
            if (!_isVisible) return;
            _isVisible = false;
            DoubleAnimation fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(95));
            fade.Completed += delegate { Application.Current.Shutdown(); };
            BeginAnimation(OpacityProperty, fade);
        }
    }

    public static class Program
    {
        private const string MutexName = @"Local\YASB_EffectsMenu_Mutex";
        private const string CloseEventName = @"Local\YASB_EffectsMenu_Close";

        [STAThread]
        public static void Main(string[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], "--off", StringComparison.OrdinalIgnoreCase))
                {
                    EffectsWindow.StopAllEffects();
                    return;
                }
            }

            bool createdNew;
            using (Mutex mutex = new Mutex(true, MutexName, out createdNew))
            {
                if (!createdNew)
                {
                    try
                    {
                        using (EventWaitHandle ev = EventWaitHandle.OpenExisting(CloseEventName))
                            ev.Set();
                    }
                    catch { }
                    return;
                }

                EventWaitHandle closeEvent = new EventWaitHandle(false, EventResetMode.AutoReset, CloseEventName);
                Application app = new Application();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;

                EffectsWindow win = new EffectsWindow();
                win.ShowDrawer();

                ThreadPool.RegisterWaitForSingleObject(closeEvent, delegate(object state, bool timedOut)
                {
                    if (timedOut) return;
                    win.Dispatcher.BeginInvoke((Action)delegate { win.HideAndExit(); });
                }, null, -1, false);

                app.Run();
                closeEvent.Close();
            }
        }
    }
}
