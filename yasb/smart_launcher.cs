using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using YasbSearch;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;
using Orientation = System.Windows.Controls.Orientation;

namespace YasbSmartLauncher
{
    internal enum EntryKind
    {
        App,
        Setting,
        Command,
        Window,
        Calculator,
        WebSearch
    }

    internal sealed class LauncherEntry
    {
        public string Id;
        public string Title;
        public string Subtitle;
        public string LaunchTarget;
        public string LaunchArgs;
        public string ResolvedExePath;
        public string Keywords;
        public string Glyph;
        public EntryKind Kind;
        public int BasePriority;
        public IntPtr TargetHwnd;
        public IndexedText SearchTitle;
        public IndexedText SearchKeywords;
        public ImageSource CachedIcon;
        public bool IconLoaded;
    }

    internal sealed class ScoredEntry
    {
        public LauncherEntry Entry;
        public int TotalScore;
        public string InterpretedAs;
        public string MatchKind;
    }

    internal static class NativeWin
    {
        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        public static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        public static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        public static extern IntPtr GetShellWindow();

        [DllImport("user32.dll")]
        public static extern bool DestroyIcon(IntPtr hIcon);

        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
            public string szTypeName;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

        public const uint SHGFI_ICON = 0x000000100;
        public const uint SHGFI_LARGEICON = 0x000000000;
        public const int SW_RESTORE = 9;
    }

    internal static class MathEvaluator
    {
        public static bool TryEvaluate(string raw, out string formattedResult)
        {
            formattedResult = null;
            if (string.IsNullOrWhiteSpace(raw)) return false;

            string expr = raw.Trim();
            if (expr.StartsWith("="))
                expr = expr.Substring(1).Trim();
            else
            {
                bool hasDigit = false;
                bool hasOp = false;
                for (int i = 0; i < expr.Length; i++)
                {
                    char c = expr[i];
                    if (char.IsDigit(c)) hasDigit = true;
                    else if (c == '+' || c == '-' || c == '*' || c == '/' || c == '%' || c == '^') hasOp = true;
                }
                if (!hasDigit || !hasOp) return false;
            }

            if (expr.Length == 0) return false;
            expr = expr.Replace(',', '.').Replace(" ", "");

            try
            {
                int pos = 0;
                double val = ParseExpr(expr, ref pos);
                if (pos != expr.Length || double.IsNaN(val) || double.IsInfinity(val))
                    return false;

                double rounded = Math.Round(val, 10);
                if (Math.Abs(rounded - Math.Round(rounded)) < 1e-10)
                    formattedResult = ((long)Math.Round(rounded)).ToString(CultureInfo.InvariantCulture);
                else
                    formattedResult = rounded.ToString("0.##########", CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static double ParseExpr(string s, ref int pos)
        {
            double left = ParseTerm(s, ref pos);
            while (pos < s.Length && (s[pos] == '+' || s[pos] == '-'))
            {
                char op = s[pos++];
                double right = ParseTerm(s, ref pos);
                left = (op == '+') ? left + right : left - right;
            }
            return left;
        }

        private static double ParseTerm(string s, ref int pos)
        {
            double left = ParsePower(s, ref pos);
            while (pos < s.Length && (s[pos] == '*' || s[pos] == '/' || s[pos] == '%'))
            {
                char op = s[pos++];
                double right = ParsePower(s, ref pos);
                if (op == '*') left *= right;
                else if (op == '/')
                {
                    if (Math.Abs(right) < 1e-15) throw new DivideByZeroException();
                    left /= right;
                }
                else left %= right;
            }
            return left;
        }

        private static double ParsePower(string s, ref int pos)
        {
            double left = ParseUnary(s, ref pos);
            if (pos < s.Length && s[pos] == '^')
            {
                pos++;
                double right = ParsePower(s, ref pos);
                return Math.Pow(left, right);
            }
            return left;
        }

        private static double ParseUnary(string s, ref int pos)
        {
            if (pos < s.Length && s[pos] == '+') { pos++; return ParseUnary(s, ref pos); }
            if (pos < s.Length && s[pos] == '-') { pos++; return -ParseUnary(s, ref pos); }
            return ParsePrimary(s, ref pos);
        }

        private static double ParsePrimary(string s, ref int pos)
        {
            if (pos >= s.Length) throw new FormatException();

            if (s[pos] == '(')
            {
                pos++;
                double v = ParseExpr(s, ref pos);
                if (pos >= s.Length || s[pos] != ')') throw new FormatException();
                pos++;
                return v;
            }

            if (char.IsLetter(s[pos]))
            {
                int start = pos;
                while (pos < s.Length && char.IsLetter(s[pos])) pos++;
                string fn = s.Substring(start, pos - start).ToLowerInvariant();
                if (fn == "pi") return Math.PI;
                if (fn == "e") return Math.E;
                if (pos >= s.Length || s[pos] != '(') throw new FormatException();
                pos++;
                double arg = ParseExpr(s, ref pos);
                if (pos >= s.Length || s[pos] != ')') throw new FormatException();
                pos++;
                if (fn == "sqrt") return Math.Sqrt(arg);
                if (fn == "abs") return Math.Abs(arg);
                if (fn == "round") return Math.Round(arg);
                if (fn == "sin") return Math.Sin(arg);
                if (fn == "cos") return Math.Cos(arg);
                if (fn == "tan") return Math.Tan(arg);
                throw new FormatException();
            }

            int numStart = pos;
            bool dotSeen = false;
            while (pos < s.Length && (char.IsDigit(s[pos]) || (s[pos] == '.' && !dotSeen)))
            {
                if (s[pos] == '.') dotSeen = true;
                pos++;
            }
            if (pos == numStart) throw new FormatException();
            return double.Parse(s.Substring(numStart, pos - numStart), CultureInfo.InvariantCulture);
        }
    }

    public class SmartLauncherApp : Application
    {
        private const string MutexName = @"Local\YASB_SmartLauncher_Mutex";
        private const string ToggleEventName = @"Local\YASB_SmartLauncher_Toggle";
        private static Mutex _mutex;
        private EventWaitHandle _toggleEvent;
        private SmartLauncherWindow _window;

        [STAThread]
        public static void Main(string[] args)
        {
            if (args != null && args.Any(a => a.Equals("--test-search", StringComparison.OrdinalIgnoreCase)))
            {
                int exit = RunSelfTests();
                Environment.Exit(exit);
                return;
            }

            bool ensureBg = args != null && args.Any(a => a.Equals("--ensure-bg", StringComparison.OrdinalIgnoreCase));
            bool backgroundOnly = args != null && args.Any(a => a.Equals("--background", StringComparison.OrdinalIgnoreCase));

            if (ensureBg)
            {
                Mutex existing;
                if (!Mutex.TryOpenExisting(MutexName, out existing))
                {
                    try
                    {
                        var psi = new ProcessStartInfo
                        {
                            FileName = Assembly.GetExecutingAssembly().Location,
                            Arguments = "--background",
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                        Process.Start(psi);
                    }
                    catch { }
                }
                else
                {
                    existing.Dispose();
                }
                Console.WriteLine("ok");
                return;
            }

            bool createdNew;
            _mutex = new Mutex(true, MutexName, out createdNew);
            if (!createdNew)
            {
                if (!backgroundOnly)
                {
                    try
                    {
                        using (var ev = EventWaitHandle.OpenExisting(ToggleEventName))
                            ev.Set();
                    }
                    catch { }
                }
                return;
            }

            var app = new SmartLauncherApp();
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            app.StartApp(backgroundOnly);
            app.Run();
        }

        private void StartApp(bool backgroundOnly)
        {
            _toggleEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ToggleEventName);
            _window = new SmartLauncherWindow();

            var listener = new Thread(() =>
            {
                while (true)
                {
                    _toggleEvent.WaitOne();
                    Dispatcher.BeginInvoke(new Action(() => _window.TogglePopup()));
                }
            })
            { IsBackground = true };
            listener.Start();

            if (!backgroundOnly)
                _window.ShowPopup();
        }

        private static int RunSelfTests()
        {
            try { Console.OutputEncoding = Encoding.UTF8; } catch { }
            var sb = new StringBuilder();
            int failures = 0;

            Action<string, string, string[]> assertTop = (query, expectedTitleSub, sampleTitles) =>
            {
                var items = new List<LauncherEntry>();
                for (int i = 0; i < sampleTitles.Length; i++)
                {
                    items.Add(new LauncherEntry
                    {
                        Id = "test:" + i,
                        Title = sampleTitles[i],
                        Subtitle = "Приложение",
                        Kind = EntryKind.App,
                        BasePriority = IsDemotedName(sampleTitles[i]) ? -250 : 0,
                        SearchTitle = SmartSearchMatcher.BuildIndex(sampleTitles[i]),
                        SearchKeywords = SmartSearchMatcher.BuildIndex("")
                    });
                }

                var scored = SmartLauncherWindow.RankEntries(items, new Dictionary<string, int>(), query);
                string top = scored.Count > 0 ? scored[0].Entry.Title : "<NONE>";
                string interp = scored.Count > 0 ? (scored[0].InterpretedAs ?? "") : "";
                bool ok = top.IndexOf(expectedTitleSub, StringComparison.OrdinalIgnoreCase) >= 0;
                if (!ok) failures++;
                sb.AppendLine(string.Format("{0} q=\"{1}\" -> \"{2}\" (expected~\"{3}\", via=\"{4}\")",
                    ok ? "PASS" : "FAIL", query, top, expectedTitleSub, interp));
            };

            string[] apps = new[]
            {
                "Google Chrome",
                "Microsoft Edge",
                "Visual Studio Code",
                "Telegram Desktop",
                "Деинсталлировать Telegram",
                "Discord",
                "Steam",
                "Counter-Strike 2",
                "Blender 5.2",
                "Notepad",
                "Calculator",
                "Control Panel",
                "Windows PowerShell",
                "Command Prompt",
                "WinRAR",
                "Справка WinRAR",
                "µTorrent",
                "AIMP",
                "DaVinci Resolve",
                "Python 3.15",
                "Adobe Photoshop"
            };

            // Wrong keyboard layout tests
            assertTop("сркщьу", "Google Chrome", apps);
            assertTop("мы сщву", "Visual Studio Code", apps);
            assertTop("еудупкфь", "Telegram Desktop", apps);
            assertTop("вшысщкв", "Discord", apps);
            assertTop("ыеуфь", "Steam", apps);
            assertTop("идутвук", "Blender 5.2", apps);
            assertTop("знерщт", "Python", apps);

            // Russian phonetic & alias tests
            assertTop("хром", "Google Chrome", apps);
            assertTop("гугл", "Google Chrome", apps);
            assertTop("эдж", "Microsoft Edge", apps);
            assertTop("вскод", "Visual Studio Code", apps);
            assertTop("вс код", "Visual Studio Code", apps);
            assertTop("телеграм", "Telegram Desktop", apps);
            assertTop("телега", "Telegram Desktop", apps);
            assertTop("дискорд", "Discord", apps);
            assertTop("стим", "Steam", apps);
            assertTop("кс2", "Counter-Strike 2", apps);
            assertTop("блокнот", "Notepad", apps);
            assertTop("калькулятор", "Calculator", apps);
            assertTop("панель управления", "Control Panel", apps);
            assertTop("терминал", "Windows Terminal", new[] { "Windows Terminal", "Windows PowerShell", "Notepad" });
            assertTop("винрар", "WinRAR", apps);
            assertTop("фотошоп", "Photoshop", apps);
            assertTop("питон", "Python", apps);
            assertTop("давинчи", "DaVinci Resolve", apps);
            assertTop("блендер", "Blender 5.2", apps);
            assertTop("обсидиан", "Obsidian", new[] { "Obsidian", "Visual Studio Code", "Notepad" });
            assertTop("щиышвшфт", "Obsidian", new[] { "Obsidian", "Visual Studio Code", "Notepad" });
            assertTop("слэк", "Slack", new[] { "Slack", "Steam", "Spotify" });
            assertTop("сдфсл", "Slack", new[] { "Slack", "Steam", "Spotify" });

            // Math test
            string mathRes;
            bool mathOk = MathEvaluator.TryEvaluate("= (120 + 30) * 2", out mathRes) && mathRes == "300";
            if (!mathOk) failures++;
            sb.AppendLine((mathOk ? "PASS" : "FAIL") + " math \"= (120 + 30) * 2\" -> " + (mathRes ?? "null"));

            Console.WriteLine(sb.ToString());
            Console.WriteLine("TOTAL_FAILURES=" + failures);
            return failures == 0 ? 0 : 1;
        }

        internal static bool IsDemotedName(string title)
        {
            if (string.IsNullOrEmpty(title)) return false;
            string low = title.ToLowerInvariant();
            return low.Contains("uninstall") || low.Contains("деинсталл") ||
                   low.Contains("удалить") || low.Contains("справка") ||
                   low.Contains("руководство") || low.Contains("новости в последней") ||
                   low.Contains("readme") || low.Contains("documentation") ||
                   low.Contains("manual") || low.Contains("about java");
        }
    }

    internal sealed class SmartLauncherWindow : Window
    {
        private static readonly string ConfigDir = @"C:\Users\Laziko\.config\yasb";
        private static readonly string HistoryPath = Path.Combine(ConfigDir, "smart_launcher_history.txt");
        private static readonly string UwpCacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YASB");
        private static readonly string UwpCachePath = Path.Combine(UwpCacheDir, "smart_launcher_uwp_cache.txt");

        // Yozora Sakura Neon palette
        private static readonly Brush BgWindow = BrushFromHex("#F20F111A");
        private static readonly Brush BgInput = BrushFromHex("#15FFFFFF");
        private static readonly Brush BgItemHover = BrushFromHex("#1AFFFFFF");
        private static readonly Brush BgItemSelected = BrushFromHex("#2DFF7EB6");
        private static readonly Brush BorderGlow = BrushFromHex("#55FF7EB6");
        private static readonly Brush BorderSubtle = BrushFromHex("#1EFFFFFF");
        private static readonly Brush TextPrimary = BrushFromHex("#FFF2F4FF");
        private static readonly Brush TextMuted = BrushFromHex("#FF98A2C3");
        private static readonly Brush AccentSakura = BrushFromHex("#FFFF7EB6");
        private static readonly Brush AccentCyan = BrushFromHex("#FF7CE8FF");
        private static readonly Brush AccentGold = BrushFromHex("#FFFFD479");
        private static readonly Brush AccentJade = BrushFromHex("#FF7AF2A8");

        private TextBox _searchBox;
        private Border _interpretedBanner;
        private TextBlock _interpretedLabel;
        private ListBox _resultsList;
        private TextBlock _statusText;
        private bool _suppressDeactivate;

        private readonly object _indexLock = new object();
        private List<LauncherEntry> _baseEntries = new List<LauncherEntry>();
        private Dictionary<string, int> _history = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private DateTime _lastIndexAt = DateTime.MinValue;

        public SmartLauncherWindow()
        {
            Title = "YASB Smart Launcher";
            Width = 700;
            Height = 490;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;

            LoadHistory();
            BuildUi();
            RebuildIndexAsync(false);

            Deactivated += (s, e) =>
            {
                if (!_suppressDeactivate && IsVisible)
                    HidePopup();
            };

            PreviewKeyDown += OnWindowKeyDown;
        }

        private void BuildUi()
        {
            var outer = new Border
            {
                Background = BgWindow,
                BorderBrush = BorderGlow,
                BorderThickness = new Thickness(1.2),
                CornerRadius = new CornerRadius(14),
                Margin = new Thickness(8)
            };

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Row 0: Search bar
            var searchWrap = new Border
            {
                BorderBrush = BorderSubtle,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(16, 13, 16, 12)
            };

            var searchGrid = new Grid();
            searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var iconSearch = new TextBlock
            {
                Text = "\uE71E",
                FontFamily = new FontFamily("Segoe Fluent Icons"),
                FontSize = 18,
                Foreground = AccentSakura,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0)
            };
            Grid.SetColumn(iconSearch, 0);
            searchGrid.Children.Add(iconSearch);

            var inputContainer = new Grid { VerticalAlignment = VerticalAlignment.Center };
            var placeholder = new TextBlock
            {
                Text = "Поиск программ (РУС / ENG, раскладка, калькулятор =, Google ?)...",
                Foreground = BrushFromHex("#7798A2C3"),
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 15,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false
            };

            _searchBox = new TextBox
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = TextPrimary,
                CaretBrush = AccentSakura,
                SelectionBrush = BrushFromHex("#55FF7EB6"),
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 16,
                FontWeight = FontWeights.Medium,
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(0)
            };
            _searchBox.TextChanged += (s, e) =>
            {
                placeholder.Visibility = string.IsNullOrEmpty(_searchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
                RefreshResults();
            };

            inputContainer.Children.Add(placeholder);
            inputContainer.Children.Add(_searchBox);
            Grid.SetColumn(inputContainer, 1);
            searchGrid.Children.Add(inputContainer);

            var langBadge = new Border
            {
                Background = BgInput,
                BorderBrush = BorderSubtle,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 3, 8, 3),
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            langBadge.Child = new TextBlock
            {
                Text = "RU ↔ EN",
                Foreground = AccentCyan,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold
            };
            Grid.SetColumn(langBadge, 2);
            searchGrid.Children.Add(langBadge);

            searchWrap.Child = searchGrid;
            Grid.SetRow(searchWrap, 0);
            root.Children.Add(searchWrap);

            // Row 1: Interpreted query hint (like Google "Showing results for...")
            _interpretedBanner = new Border
            {
                Background = BrushFromHex("#18FF7EB6"),
                BorderBrush = BrushFromHex("#33FF7EB6"),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(16, 6, 16, 6),
                Visibility = Visibility.Collapsed
            };
            _interpretedLabel = new TextBlock
            {
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 12,
                Foreground = TextMuted
            };
            _interpretedBanner.Child = _interpretedLabel;
            Grid.SetRow(_interpretedBanner, 1);
            root.Children.Add(_interpretedBanner);

            // Row 2: Results list
            _resultsList = new ListBox
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(10, 8, 10, 8),
                ItemContainerStyle = BuildListItemStyle(),
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };
            ScrollViewer.SetHorizontalScrollBarVisibility(_resultsList, ScrollBarVisibility.Disabled);
            _resultsList.MouseDoubleClick += (s, e) => ActivateSelected();
            _resultsList.MouseLeftButtonUp += (s, e) =>
            {
                // Single click launches item if user holds Ctrl or double clicks, or let click select cleanly
            };
            _resultsList.PreviewMouseLeftButtonUp += (s, e) =>
            {
                if (e.OriginalSource is DependencyObject)
                {
                    var item = FindAncestor<ListBoxItem>((DependencyObject)e.OriginalSource);
                    if (item != null)
                    {
                        _resultsList.SelectedItem = item.DataContext;
                        if (Keyboard.Modifiers == ModifierKeys.None && Mouse.LeftButton == MouseButtonState.Released)
                        {
                            // Launch directly on click like Spotlight/QuickLaunch
                            ActivateSelected();
                        }
                    }
                }
            };
            Grid.SetRow(_resultsList, 2);
            root.Children.Add(_resultsList);

            // Row 3: Footer bar
            var footer = new Border
            {
                BorderBrush = BorderSubtle,
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(16, 8, 16, 9),
                Background = BrushFromHex("#0AFFFFFF"),
                CornerRadius = new CornerRadius(0, 0, 13, 13)
            };
            var footerDock = new DockPanel();
            _statusText = new TextBlock
            {
                Foreground = TextMuted,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 11.5,
                VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(_statusText, Dock.Left);
            footerDock.Children.Add(_statusText);

            var hints = new TextBlock
            {
                Text = "Enter открыть  •  Ctrl+Enter папка  •  win <окно>  •  = расчёт  •  ? Google  •  F5 обновить",
                Foreground = BrushFromHex("#8898A2C3"),
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            footerDock.Children.Add(hints);

            footer.Child = footerDock;
            Grid.SetRow(footer, 3);
            root.Children.Add(footer);

            outer.Child = root;
            Content = outer;
        }

        private static T FindAncestor<T>(DependencyObject d) where T : DependencyObject
        {
            while (d != null)
            {
                if (d is T) return (T)d;
                d = VisualTreeHelper.GetParent(d);
            }
            return null;
        }

        private static Style BuildListItemStyle()
        {
            var style = new Style(typeof(ListBoxItem));
            var template = new ControlTemplate(typeof(ListBoxItem));

            var bd = new FrameworkElementFactory(typeof(Border));
            bd.Name = "Bd";
            bd.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(9));
            bd.SetValue(Border.PaddingProperty, new Thickness(10, 7, 12, 7));
            bd.SetValue(Border.MarginProperty, new Thickness(0, 1.5, 0, 1.5));

            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            bd.AppendChild(cp);
            template.VisualTree = bd;

            var selectedTrigger = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
            selectedTrigger.Setters.Add(new Setter(Border.BackgroundProperty, BgItemSelected, "Bd"));
            template.Triggers.Add(selectedTrigger);

            var hoverTrigger = new Trigger { Property = ListBoxItem.IsMouseOverProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(Border.BackgroundProperty, BgItemHover, "Bd"));
            template.Triggers.Add(hoverTrigger);

            style.Setters.Add(new Setter(Control.TemplateProperty, template));
            style.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null));
            return style;
        }

        public void TogglePopup()
        {
            if (IsVisible) HidePopup();
            else ShowPopup();
        }

        public void ShowPopup()
        {
            double sw = SystemParameters.PrimaryScreenWidth;
            double sh = SystemParameters.PrimaryScreenHeight;
            Left = Math.Round((sw - Width) / 2.0);
            Top = Math.Round(sh * 0.17);

            // Background refresh if cache older than 60s
            if ((DateTime.UtcNow - _lastIndexAt).TotalSeconds > 60)
                RebuildIndexAsync(false);

            _searchBox.Text = "";
            RefreshResults();

            Show();
            Activate();
            NativeWin.SetForegroundWindow(new WindowInteropHelper(this).Handle);

            Dispatcher.BeginInvoke(new Action(() =>
            {
                _searchBox.Focus();
                Keyboard.Focus(_searchBox);
            }), DispatcherPriority.Input);
        }

        public void HidePopup()
        {
            Hide();
        }

        private void OnWindowKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                HidePopup();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Down)
            {
                if (_resultsList.Items.Count > 0)
                {
                    int next = (_resultsList.SelectedIndex + 1) % _resultsList.Items.Count;
                    _resultsList.SelectedIndex = next;
                    _resultsList.ScrollIntoView(_resultsList.SelectedItem);
                }
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Up)
            {
                if (_resultsList.Items.Count > 0)
                {
                    int prev = _resultsList.SelectedIndex - 1;
                    if (prev < 0) prev = _resultsList.Items.Count - 1;
                    _resultsList.SelectedIndex = prev;
                    _resultsList.ScrollIntoView(_resultsList.SelectedItem);
                }
                e.Handled = true;
                return;
            }

            if (e.Key == Key.F5)
            {
                RebuildIndexAsync(true);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Return)
            {
                bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
                ActivateSelected(ctrl);
                e.Handled = true;
            }
        }

        private void RefreshResults()
        {
            string rawQuery = _searchBox != null ? (_searchBox.Text ?? "").Trim() : "";
            List<LauncherEntry> snapshot;
            lock (_indexLock)
            {
                snapshot = new List<LauncherEntry>(_baseEntries);
            }

            var dynamicResults = new List<ScoredEntry>();
            string interpretedBanner = null;

            // 1. Explicit web search "? ..."
            if (rawQuery.StartsWith("?") && rawQuery.Length > 1)
            {
                string webQ = rawQuery.Substring(1).Trim();
                dynamicResults.Add(new ScoredEntry
                {
                    TotalScore = 2000,
                    Entry = MakeWebEntry(webQ)
                });
            }
            else
            {
                // 2. Inline calculator (=expr or math expression)
                string calcResult;
                if (MathEvaluator.TryEvaluate(rawQuery, out calcResult))
                {
                    dynamicResults.Add(new ScoredEntry
                    {
                        TotalScore = 1900,
                        Entry = new LauncherEntry
                        {
                            Id = "calc:" + rawQuery,
                            Title = "= " + calcResult,
                            Subtitle = "Калькулятор — нажмите Enter чтобы скопировать результат",
                            LaunchTarget = calcResult,
                            Kind = EntryKind.Calculator,
                            Glyph = "\uE8EF"
                        }
                    });
                }

                // 3. Open windows if empty query doesn't need them, or "win ..." prefix, or typed query >= 2 chars
                string winQuery = rawQuery;
                bool windowsOnly = false;
                if (rawQuery.StartsWith("win ", StringComparison.OrdinalIgnoreCase))
                {
                    windowsOnly = true;
                    winQuery = rawQuery.Substring(4).Trim();
                }

                if (windowsOnly || rawQuery.Length >= 2)
                {
                    var openWins = EnumerateOpenWindows();
                    if (windowsOnly)
                    {
                        snapshot = openWins;
                    }
                    else
                    {
                        // Also allow switching to open windows at slightly lower base priority than launched apps
                        snapshot.AddRange(openWins);
                    }
                }

                if (string.IsNullOrEmpty(rawQuery))
                {
                    // Show MRU + top featured apps
                    var ordered = snapshot
                        .OrderByDescending(e => GetHistoryBoost(e.Id))
                        .ThenByDescending(e => e.BasePriority)
                        .ThenBy(e => e.Title, StringComparer.CurrentCultureIgnoreCase)
                        .Take(25)
                        .ToList();

                    for (int i = 0; i < ordered.Count; i++)
                        dynamicResults.Add(new ScoredEntry { Entry = ordered[i], TotalScore = 100 });
                }
                else
                {
                    var ranked = RankEntries(snapshot, _history, windowsOnly ? winQuery : rawQuery);
                    dynamicResults.AddRange(ranked.Take(28));

                    if (ranked.Count > 0 &&
                        !string.IsNullOrEmpty(ranked[0].InterpretedAs) &&
                        ranked[0].MatchKind != "direct")
                    {
                        interpretedBanner = ranked[0].InterpretedAs;
                    }
                }

                // Always keep Google web search fallback at bottom when query is non-empty
                if (!string.IsNullOrEmpty(rawQuery) && !rawQuery.StartsWith("="))
                {
                    dynamicResults.Add(new ScoredEntry
                    {
                        TotalScore = 10,
                        Entry = MakeWebEntry(rawQuery)
                    });
                }
            }

            if (!string.IsNullOrEmpty(interpretedBanner))
            {
                _interpretedLabel.Text = "Поиск распознал запрос как: «" + interpretedBanner + "»";
                _interpretedBanner.Visibility = Visibility.Visible;
            }
            else
            {
                _interpretedBanner.Visibility = Visibility.Collapsed;
            }

            var uiItems = new List<UIElement>(dynamicResults.Count);
            for (int i = 0; i < dynamicResults.Count; i++)
                uiItems.Add(BuildResultRow(dynamicResults[i]));

            _resultsList.ItemsSource = null;
            _resultsList.Tag = dynamicResults;
            _resultsList.ItemsSource = uiItems;
            if (uiItems.Count > 0)
                _resultsList.SelectedIndex = 0;

            _statusText.Text = string.IsNullOrEmpty(rawQuery)
                ? string.Format("Проиндексировано: {0} объектов", snapshot.Count)
                : string.Format("Найдено: {0}", Math.Max(0, dynamicResults.Count - (rawQuery.StartsWith("?") ? 0 : 1)));
        }

        internal static List<ScoredEntry> RankEntries(
            List<LauncherEntry> entries,
            Dictionary<string, int> history,
            string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return entries
                    .Select(e => new ScoredEntry { Entry = e, TotalScore = e.BasePriority })
                    .ToList();
            }

            var scored = new List<ScoredEntry>();
            for (int i = 0; i < entries.Count; i++)
            {
                LauncherEntry e = entries[i];
                SearchMatch mTitle = SmartSearchMatcher.Match(e.SearchTitle, query);
                SearchMatch mKw = SmartSearchMatcher.Match(e.SearchKeywords, query);

                SearchMatch best = (mKw.Score - 35 > mTitle.Score) ? mKw : mTitle;
                int bestScore = Math.Max(mTitle.Score, mKw.Score - 35);
                if (bestScore <= 0) continue;

                int hist = 0;
                if (history != null && !string.IsNullOrEmpty(e.Id))
                {
                    int h;
                    if (history.TryGetValue(e.Id, out h))
                        hist = Math.Min(180, h * 25);
                }

                int total = bestScore + e.BasePriority + hist;
                if (total > 0)
                {
                    scored.Add(new ScoredEntry
                    {
                        Entry = e,
                        TotalScore = total,
                        InterpretedAs = best.InterpretedAs,
                        MatchKind = best.MatchKind
                    });
                }
            }

            scored.Sort((a, b) =>
            {
                int cmp = b.TotalScore.CompareTo(a.TotalScore);
                if (cmp != 0) return cmp;
                return string.Compare(a.Entry.Title, b.Entry.Title, StringComparison.CurrentCultureIgnoreCase);
            });
            return scored;
        }

        private UIElement BuildResultRow(ScoredEntry se)
        {
            LauncherEntry entry = se.Entry;
            EnsureEntryIcon(entry);

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            if (entry.CachedIcon != null)
            {
                var img = new System.Windows.Controls.Image
                {
                    Source = entry.CachedIcon,
                    Width = 24,
                    Height = 24,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Left
                };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                Grid.SetColumn(img, 0);
                grid.Children.Add(img);
            }
            else
            {
                var iconBox = new Border
                {
                    Width = 26,
                    Height = 26,
                    CornerRadius = new CornerRadius(7),
                    Background = BrushFromHex("#18FFFFFF"),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Brush glyphColor = AccentSakura;
                if (entry.Kind == EntryKind.Setting) glyphColor = AccentCyan;
                else if (entry.Kind == EntryKind.Calculator) glyphColor = AccentJade;
                else if (entry.Kind == EntryKind.Command) glyphColor = AccentGold;
                else if (entry.Kind == EntryKind.Window) glyphColor = AccentCyan;

                iconBox.Child = new TextBlock
                {
                    Text = string.IsNullOrEmpty(entry.Glyph) ? "\uE737" : entry.Glyph,
                    FontFamily = new FontFamily("Segoe Fluent Icons"),
                    FontSize = 14,
                    Foreground = glyphColor,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(iconBox, 0);
                grid.Children.Add(iconBox);
            }

            var textStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            textStack.Children.Add(new TextBlock
            {
                Text = entry.Title,
                Foreground = TextPrimary,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            textStack.Children.Add(new TextBlock
            {
                Text = entry.Subtitle,
                Foreground = TextMuted,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 11.5,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            Grid.SetColumn(textStack, 1);
            grid.Children.Add(textStack);

            var tag = new Border
            {
                Background = BrushFromHex("#12FFFFFF"),
                BorderBrush = BorderSubtle,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(7, 2, 7, 2),
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            tag.Child = new TextBlock
            {
                Text = KindLabel(entry.Kind),
                Foreground = TextMuted,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 10.5
            };
            Grid.SetColumn(tag, 2);
            grid.Children.Add(tag);

            return grid;
        }

        private static string KindLabel(EntryKind k)
        {
            switch (k)
            {
                case EntryKind.App: return "Программа";
                case EntryKind.Setting: return "Параметры";
                case EntryKind.Command: return "Команда";
                case EntryKind.Window: return "Окно";
                case EntryKind.Calculator: return "Расчёт";
                case EntryKind.WebSearch: return "Google";
                default: return "";
            }
        }

        private void ActivateSelected(bool openFolder = false)
        {
            var results = _resultsList.Tag as List<ScoredEntry>;
            int idx = _resultsList.SelectedIndex;
            if (results == null || idx < 0 || idx >= results.Count) return;

            LauncherEntry entry = results[idx].Entry;

            if (openFolder)
            {
                string target = !string.IsNullOrEmpty(entry.ResolvedExePath) && File.Exists(entry.ResolvedExePath)
                    ? entry.ResolvedExePath
                    : (File.Exists(entry.LaunchTarget) ? entry.LaunchTarget : null);

                if (target != null)
                {
                    HidePopup();
                    try
                    {
                        Process.Start("explorer.exe", "/select,\"" + target + "\"");
                    }
                    catch { }
                }
                return;
            }

            HidePopup();

            if (entry.Kind == EntryKind.Calculator)
            {
                try
                {
                    _suppressDeactivate = true;
                    Clipboard.SetText(entry.LaunchTarget);
                }
                catch { }
                finally { _suppressDeactivate = false; }
                return;
            }

            RecordHistory(entry.Id);

            if (entry.Kind == EntryKind.Window && entry.TargetHwnd != IntPtr.Zero)
            {
                if (NativeWin.IsIconic(entry.TargetHwnd))
                    NativeWin.ShowWindow(entry.TargetHwnd, NativeWin.SW_RESTORE);
                NativeWin.SetForegroundWindow(entry.TargetHwnd);
                return;
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = entry.LaunchTarget,
                    Arguments = entry.LaunchArgs ?? "",
                    UseShellExecute = true
                };
                if (!string.IsNullOrEmpty(entry.ResolvedExePath) && File.Exists(entry.ResolvedExePath))
                {
                    psi.WorkingDirectory = Path.GetDirectoryName(entry.ResolvedExePath);
                }
                Process.Start(psi);
            }
            catch { }
        }

        private static LauncherEntry MakeWebEntry(string query)
        {
            string encoded = Uri.EscapeDataString(query);
            return new LauncherEntry
            {
                Id = "web:" + query,
                Title = "Поиск в Google: «" + query + "»",
                Subtitle = "Открыть результаты поиска в браузере",
                LaunchTarget = "https://www.google.com/search?q=" + encoded,
                Kind = EntryKind.WebSearch,
                Glyph = "\uE774"
            };
        }

        private void RebuildIndexAsync(bool refreshUwp)
        {
            new Thread(() =>
            {
                var built = BuildAllEntries();
                lock (_indexLock)
                {
                    _baseEntries = built;
                    _lastIndexAt = DateTime.UtcNow;
                }

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (IsVisible) RefreshResults();
                }));

                // Refresh UWP app list in background if cache missing or forced
                if (refreshUwp || !File.Exists(UwpCachePath) ||
                    (DateTime.UtcNow - File.GetLastWriteTimeUtc(UwpCachePath)).TotalHours > 12)
                {
                    if (RefreshUwpCacheFile())
                    {
                        var updated = BuildAllEntries();
                        lock (_indexLock)
                        {
                            _baseEntries = updated;
                            _lastIndexAt = DateTime.UtcNow;
                        }
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            if (IsVisible) RefreshResults();
                        }));
                    }
                }
            })
            { IsBackground = true }.Start();
        }

        private static List<LauncherEntry> BuildAllEntries()
        {
            var results = new List<LauncherEntry>();
            var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 1. Start Menu & Desktop shortcuts
            string[] dirs = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
                Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)
            };

            object wsh = null;
            try
            {
                Type t = Type.GetTypeFromProgID("WScript.Shell");
                if (t != null) wsh = Activator.CreateInstance(t);
            }
            catch { }

            foreach (string dir in dirs)
            {
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;
                try
                {
                    string[] files = Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories);
                    for (int i = 0; i < files.Length; i++)
                    {
                        string file = files[i];
                        string ext = Path.GetExtension(file).ToLowerInvariant();
                        if (ext != ".lnk" && ext != ".url" && ext != ".exe") continue;

                        string name = Path.GetFileNameWithoutExtension(file);
                        if (string.IsNullOrWhiteSpace(name)) continue;

                        bool isDemoted = SmartLauncherApp.IsDemotedName(name);
                        string resolvedTarget = null;
                        if (ext == ".lnk" && wsh != null)
                        {
                            try
                            {
                                object sc = wsh.GetType().InvokeMember("CreateShortcut",
                                    BindingFlags.InvokeMethod, null, wsh, new object[] { file });
                                resolvedTarget = sc.GetType().InvokeMember("TargetPath",
                                    BindingFlags.GetProperty, null, sc, null) as string;
                            }
                            catch { }
                        }

                        string normKey = SmartSearchMatcher.NormalizeBasic(name);
                        if (!seenNames.Add(normKey)) continue;

                        string extraKw = !string.IsNullOrEmpty(resolvedTarget)
                            ? Path.GetFileNameWithoutExtension(resolvedTarget)
                            : "";

                        var entry = new LauncherEntry
                        {
                            Id = "app:" + normKey,
                            Title = name,
                            Subtitle = !string.IsNullOrEmpty(resolvedTarget) ? resolvedTarget : file,
                            LaunchTarget = file,
                            ResolvedExePath = (!string.IsNullOrEmpty(resolvedTarget) && File.Exists(resolvedTarget))
                                ? resolvedTarget : file,
                            Keywords = extraKw,
                            Glyph = "\uE737",
                            Kind = EntryKind.App,
                            BasePriority = isDemoted ? -250 : 40
                        };
                        FinalizeEntryIndex(entry);
                        results.Add(entry);
                    }
                }
                catch { }
            }

            // 2. Cached UWP / Store apps (Explorer, Settings, Calculator, Camera, Terminal, etc.)
            if (File.Exists(UwpCachePath))
            {
                try
                {
                    string[] lines = File.ReadAllLines(UwpCachePath, Encoding.UTF8);
                    for (int i = 0; i < lines.Length; i++)
                    {
                        int sep = lines[i].IndexOf('|');
                        if (sep <= 0) continue;
                        string appName = lines[i].Substring(0, sep).Trim();
                        string appId = lines[i].Substring(sep + 1).Trim();
                        if (string.IsNullOrEmpty(appName) || string.IsNullOrEmpty(appId)) continue;

                        string normKey = SmartSearchMatcher.NormalizeBasic(appName);
                        if (!seenNames.Add(normKey)) continue;

                        bool isDemoted = SmartLauncherApp.IsDemotedName(appName);
                        var entry = new LauncherEntry
                        {
                            Id = "uwp:" + normKey,
                            Title = appName,
                            Subtitle = "Windows / Store приложение",
                            LaunchTarget = "explorer.exe",
                            LaunchArgs = "shell:AppsFolder\\" + appId,
                            Keywords = appId,
                            Glyph = "\uE74C",
                            Kind = EntryKind.App,
                            BasePriority = isDemoted ? -250 : 30
                        };
                        FinalizeEntryIndex(entry);
                        results.Add(entry);
                    }
                }
                catch { }
            }

            // 3. Core built-in Windows apps & system utilities
            AddBuiltIn(results, seenNames, "Windows Terminal", "Терминал Windows (PowerShell / CMD)", "wt.exe", "", "terminal wt console cmd консоль терминал", "\uE756", EntryKind.App, 55);
            AddBuiltIn(results, seenNames, "Notepad (Блокнот)", "Текстовый редактор Windows", "notepad.exe", "", "notepad блокнот заметки текст", "\uE70B", EntryKind.App, 45);
            AddBuiltIn(results, seenNames, "Calculator (Калькулятор)", "Калькулятор Windows", "calc.exe", "", "calc calculator калькулятор кальк", "\uE8EF", EntryKind.App, 45);
            AddBuiltIn(results, seenNames, "File Explorer (Проводник)", "Проводник Windows", "explorer.exe", "", "explorer проводник файлы папки", "\uEC50", EntryKind.App, 45);
            AddBuiltIn(results, seenNames, "Task Manager (Диспетчер задач)", "Мониторинг процессов и CPU", "taskmgr.exe", "", "taskmgr task manager диспетчер задач процессы", "\uE9D9", EntryKind.App, 40);
            AddBuiltIn(results, seenNames, "Paint (Паинт)", "Графический редактор", "mspaint.exe", "", "paint паинт рисование", "\uE790", EntryKind.App, 30);
            AddBuiltIn(results, seenNames, "Snipping Tool (Ножницы)", "Скриншот экрана", "ms-screenclip:", "", "snip screenshot ножницы скриншот снимок", "\uE8B8", EntryKind.App, 35);
            AddBuiltIn(results, seenNames, "Control Panel (Панель управления)", "Классическая панель управления", "control.exe", "", "control panel панель управления", "\uE90F", EntryKind.Setting, 35);

            // 4. Windows Settings shortcuts
            AddSetting(results, seenNames, "Параметры Windows (Settings)", "Открыть настройки системы", "ms-settings:", "settings параметры настройки конфиг");
            AddSetting(results, seenNames, "Экран и яркость (Display)", "Разрешение, герцовка, масштаб", "ms-settings:display", "display screen экран дисплей монитор яркость");
            AddSetting(results, seenNames, "Звук и устройства (Sound)", "Вывод звука, микрофон, громкость", "ms-settings:sound", "sound audio volume звук громкость аудио микрофон");
            AddSetting(results, seenNames, "Bluetooth и устройства", "Подключение наушников и периферии", "ms-settings:bluetooth", "bluetooth блютуз блютус устройства");
            AddSetting(results, seenNames, "Wi-Fi и Сеть (Network)", "Беспроводные сети и интернет", "ms-settings:network-wifi", "wifi wi-fi network вайфай сеть интернет");
            AddSetting(results, seenNames, "Приложения (Apps)", "Установленные программы и удаление", "ms-settings:appsfeatures", "apps программы приложения удалить");
            AddSetting(results, seenNames, "Персонализация (Personalization)", "Тема, цвета, экран блокировки", "ms-settings:personalization", "personalization theme тема цвета обои");
            AddSetting(results, seenNames, "Центр обновления Windows", "Проверка обновлений", "ms-settings:windowsupdate", "windows update обновления");

            // 5. YASB tools & Power actions
            AddCommand(results, seenNames, "Карусель обоев (Wallpapers)", "Выбор видео или фото обоев", Path.Combine(ConfigDir, "wallpaper_gallery.exe"), "--toggle", "wallpapers carousel обои карусель видео фото", "\uEB9F");
            AddCommand(results, seenNames, "Меню живых обоев (Effects)", "Переключение режимов и эффектов", Path.Combine(ConfigDir, "effects_menu.exe"), "", "effects menu live wallpaper эффекты обои визуализатор", "\uE790");
            AddCommand(results, seenNames, "YASB Finder (Папки и файлы)", "Колоночный проводник (Alt+E)", Path.Combine(ConfigDir, "folder_menu.exe"), "--toggle", "finder folder menu explorer папки файлы проводник", "\uE8B7");
            AddCommand(results, seenNames, "Шпаргалка клавиш (Hotkeys)", "Список всех горячих клавиш (Alt+/)", Path.Combine(ConfigDir, "hotkey_cheatsheet.exe"), "--toggle", "hotkeys cheatsheet шпаргалка клавиши хоткеи", "\uE765");
            AddCommand(results, seenNames, "Scratchpad (Заметки / PS)", "Быстрые заметки и калькулятор (Alt+N)", Path.Combine(ConfigDir, "scratchpad.exe"), "--toggle", "scratchpad notes заметки", "\uE70F");
            AddCommand(results, seenNames, "Блокировка экрана (Lock)", "Заблокировать компьютер", "rundll32.exe", "user32.dll,LockWorkStation", "lock блокировка заблокировать", "\uE72E");
            AddCommand(results, seenNames, "Спящий режим (Sleep)", "Перевести ПК в сон", "rundll32.exe", "powrprof.dll,SetSuspendState 0,1,0", "sleep сон спящий режим", "\uE708");
            AddCommand(results, seenNames, "Перезагрузка ПК (Restart)", "Перезагрузить компьютер", "shutdown.exe", "/r /t 0", "restart reboot перезагрузка перезагрузить", "\uE777");
            AddCommand(results, seenNames, "Выключение ПК (Shutdown)", "Выключить компьютер", "shutdown.exe", "/s /t 0", "shutdown power off выключение выключить", "\uE7E8");

            return results;
        }

        private static void AddBuiltIn(
            List<LauncherEntry> list, HashSet<string> seen,
            string title, string sub, string target, string args,
            string keywords, string glyph, EntryKind kind, int prio)
        {
            string key = SmartSearchMatcher.NormalizeBasic(title);
            if (!seen.Add(key)) return;
            string sysPath = Path.Combine(Environment.SystemDirectory, target);
            var e = new LauncherEntry
            {
                Id = "builtin:" + key,
                Title = title,
                Subtitle = sub,
                LaunchTarget = target,
                LaunchArgs = args,
                ResolvedExePath = File.Exists(sysPath) ? sysPath : null,
                Keywords = keywords,
                Glyph = glyph,
                Kind = kind,
                BasePriority = prio
            };
            FinalizeEntryIndex(e);
            list.Add(e);
        }

        private static void AddSetting(
            List<LauncherEntry> list, HashSet<string> seen,
            string title, string sub, string uri, string keywords)
        {
            string key = SmartSearchMatcher.NormalizeBasic(title);
            if (!seen.Add(key)) return;
            var e = new LauncherEntry
            {
                Id = "setting:" + uri,
                Title = title,
                Subtitle = sub,
                LaunchTarget = uri,
                Keywords = keywords,
                Glyph = "\uE713",
                Kind = EntryKind.Setting,
                BasePriority = 20
            };
            FinalizeEntryIndex(e);
            list.Add(e);
        }

        private static void AddCommand(
            List<LauncherEntry> list, HashSet<string> seen,
            string title, string sub, string exe, string args, string keywords, string glyph)
        {
            string key = SmartSearchMatcher.NormalizeBasic(title);
            if (!seen.Add(key)) return;
            var e = new LauncherEntry
            {
                Id = "cmd:" + key,
                Title = title,
                Subtitle = sub,
                LaunchTarget = exe,
                LaunchArgs = args,
                ResolvedExePath = File.Exists(exe) ? exe : null,
                Keywords = keywords,
                Glyph = glyph,
                Kind = EntryKind.Command,
                BasePriority = 25
            };
            FinalizeEntryIndex(e);
            list.Add(e);
        }

        private static void FinalizeEntryIndex(LauncherEntry e)
        {
            e.SearchTitle = SmartSearchMatcher.BuildIndex(e.Title);
            e.SearchKeywords = SmartSearchMatcher.BuildIndex((e.Keywords ?? "") + " " + (e.Subtitle ?? ""));
        }

        private static bool RefreshUwpCacheFile()
        {
            try
            {
                if (!Directory.Exists(UwpCacheDir))
                    Directory.CreateDirectory(UwpCacheDir);

                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-NoProfile -NonInteractive -WindowStyle Hidden -Command \"[Console]::OutputEncoding=[System.Text.Encoding]::UTF8; Get-StartApps | ForEach-Object { $_.Name + '|' + $_.AppID }\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    StandardOutputEncoding = Encoding.UTF8
                };
                using (var p = Process.Start(psi))
                {
                    string output = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(8000);
                    if (!string.IsNullOrWhiteSpace(output))
                    {
                        File.WriteAllText(UwpCachePath, output, Encoding.UTF8);
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        private static List<LauncherEntry> EnumerateOpenWindows()
        {
            var wins = new List<LauncherEntry>();
            IntPtr shell = NativeWin.GetShellWindow();
            uint selfPid = (uint)Process.GetCurrentProcess().Id;

            NativeWin.EnumWindows((hWnd, lParam) =>
            {
                if (hWnd == shell || !NativeWin.IsWindowVisible(hWnd)) return true;
                int len = NativeWin.GetWindowTextLength(hWnd);
                if (len <= 0) return true;

                uint pid;
                NativeWin.GetWindowThreadProcessId(hWnd, out pid);
                if (pid == selfPid) return true;

                var sb = new StringBuilder(len + 1);
                NativeWin.GetWindowText(hWnd, sb, sb.Capacity);
                string title = sb.ToString().Trim();
                if (string.IsNullOrEmpty(title) || title == "Program Manager") return true;

                string procName = "";
                try
                {
                    using (var p = Process.GetProcessById((int)pid))
                        procName = p.ProcessName;
                }
                catch { }

                var e = new LauncherEntry
                {
                    Id = "win:" + hWnd.ToInt64(),
                    Title = title,
                    Subtitle = "Открытое окно (" + procName + ")",
                    Keywords = procName + " win окно",
                    TargetHwnd = hWnd,
                    Glyph = "\uE737",
                    Kind = EntryKind.Window,
                    BasePriority = -10
                };
                FinalizeEntryIndex(e);
                wins.Add(e);
                return true;
            }, IntPtr.Zero);

            return wins;
        }

        private static void EnsureEntryIcon(LauncherEntry e)
        {
            if (e.IconLoaded) return;
            e.IconLoaded = true;

            string iconPath = !string.IsNullOrEmpty(e.ResolvedExePath) && File.Exists(e.ResolvedExePath)
                ? e.ResolvedExePath
                : (File.Exists(e.LaunchTarget) ? e.LaunchTarget : null);

            if (string.IsNullOrEmpty(iconPath)) return;

            try
            {
                var shfi = new NativeWin.SHFILEINFO();
                IntPtr res = NativeWin.SHGetFileInfo(
                    iconPath,
                    0,
                    ref shfi,
                    (uint)Marshal.SizeOf(typeof(NativeWin.SHFILEINFO)),
                    NativeWin.SHGFI_ICON | NativeWin.SHGFI_LARGEICON);

                if (res != IntPtr.Zero && shfi.hIcon != IntPtr.Zero)
                {
                    try
                    {
                        var src = Imaging.CreateBitmapSourceFromHIcon(
                            shfi.hIcon,
                            Int32Rect.Empty,
                            BitmapSizeOptions.FromEmptyOptions());
                        src.Freeze();
                        e.CachedIcon = src;
                    }
                    finally
                    {
                        NativeWin.DestroyIcon(shfi.hIcon);
                    }
                }
            }
            catch { }
        }

        private int GetHistoryBoost(string id)
        {
            if (string.IsNullOrEmpty(id)) return 0;
            int count;
            return _history.TryGetValue(id, out count) ? count : 0;
        }

        private void LoadHistory()
        {
            try
            {
                if (!File.Exists(HistoryPath)) return;
                foreach (string line in File.ReadAllLines(HistoryPath, Encoding.UTF8))
                {
                    int sep = line.LastIndexOf('|');
                    if (sep <= 0) continue;
                    string id = line.Substring(0, sep);
                    int cnt;
                    if (int.TryParse(line.Substring(sep + 1), out cnt))
                        _history[id] = cnt;
                }
            }
            catch { }
        }

        private void RecordHistory(string id)
        {
            if (string.IsNullOrEmpty(id) || id.StartsWith("win:") || id.StartsWith("web:") || id.StartsWith("calc:"))
                return;
            try
            {
                int cur;
                _history.TryGetValue(id, out cur);
                _history[id] = cur + 1;

                var lines = _history
                    .OrderByDescending(kv => kv.Value)
                    .Take(100)
                    .Select(kv => kv.Key + "|" + kv.Value)
                    .ToArray();
                File.WriteAllLines(HistoryPath, lines, Encoding.UTF8);
            }
            catch { }
        }

        private static Brush BrushFromHex(string hex)
        {
            var b = (SolidColorBrush)new BrushConverter().ConvertFromString(hex);
            b.Freeze();
            return b;
        }
    }
}
