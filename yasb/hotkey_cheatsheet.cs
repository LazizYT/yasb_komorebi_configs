using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;

namespace YasbHotkeyCheatsheet
{
    internal class HotkeyEntry
    {
        public string Category { get; set; }
        public string Keys { get; set; }
        public string Description { get; set; }
    }

    internal static class Native
    {
        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);
    }

    public class CheatsheetApp : Application
    {
        private const string MutexName = @"Local\YASB_HotkeyCheatsheet_Mutex";
        private const string ToggleEventName = @"Local\YASB_HotkeyCheatsheet_Toggle";
        private static Mutex _mutex;
        private CheatsheetWindow _window;
        private EventWaitHandle _toggleEvent;

        [STAThread]
        public static void Main(string[] args)
        {
            bool createdNew;
            _mutex = new Mutex(true, MutexName, out createdNew);
            if (!createdNew)
            {
                try
                {
                    using (var ev = EventWaitHandle.OpenExisting(ToggleEventName))
                    {
                        ev.Set();
                    }
                }
                catch { }
                return;
            }

            var app = new CheatsheetApp();
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            app.StartApp(args.Any(a => a.Equals("--background", StringComparison.OrdinalIgnoreCase)));
            app.Run();
        }

        private void StartApp(bool backgroundOnly)
        {
            _toggleEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ToggleEventName);
            _window = new CheatsheetWindow();

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

            if (!backgroundOnly) _window.ShowPopup();
        }
    }

    internal class CheatsheetWindow : Window
    {
        // Yozora Sakura palette
        private static readonly Brush BgGlass = BrushFromHex("#F7131122");
        private static readonly Brush BgCard = BrushFromHex("#CC1B182E");
        private static readonly Brush BgPill = BrushFromHex("#FF282342");
        private static readonly Brush BorderSubtle = BrushFromHex("#48FF7EB6");
        private static readonly Brush BorderCard = BrushFromHex("#24D8B4FE");
        private static readonly Brush AccentSakura = BrushFromHex("#FFFF7EB6");
        private static readonly Brush AccentCyan = BrushFromHex("#FF6CE5FF");
        private static readonly Brush AccentGold = BrushFromHex("#FFFFD479");
        private static readonly Brush AccentJade = BrushFromHex("#FF7BF1A8");
        private static readonly Brush TextPrimary = BrushFromHex("#FFF2EEFF");
        private static readonly Brush TextMuted = BrushFromHex("#FF9E95BD");
        private static readonly Brush DangerHover = BrushFromHex("#44FF5D8F");
        private static readonly Brush DangerText = BrushFromHex("#FFFF5D8F");

        private static readonly string WhkdrcPath = @"C:\Users\Laziko\.config\whkdrc";

        private static readonly Style DarkScrollBarStyle = (Style)XamlReader.Parse(@"
<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type ScrollBar}'>
    <Setter Property='Background' Value='Transparent'/>
    <Setter Property='Width' Value='6'/>
    <Setter Property='MinWidth' Value='6'/>
    <Setter Property='Template'>
        <Setter.Value>
            <ControlTemplate TargetType='{x:Type ScrollBar}'>
                <Grid Background='Transparent'>
                    <Track x:Name='PART_Track' IsDirectionReversed='True'>
                        <Track.DecreaseRepeatButton><RepeatButton Command='ScrollBar.PageUpCommand' Opacity='0' Focusable='False'/></Track.DecreaseRepeatButton>
                        <Track.Thumb>
                            <Thumb>
                                <Thumb.Style>
                                    <Style TargetType='{x:Type Thumb}'>
                                        <Setter Property='OverridesDefaultStyle' Value='True'/>
                                        <Setter Property='Template'>
                                            <Setter.Value>
                                                <ControlTemplate TargetType='{x:Type Thumb}'>
                                                    <Border Background='#FF423B66' CornerRadius='3' Margin='1,0'/>
                                                </ControlTemplate>
                                            </Setter.Value>
                                        </Setter>
                                    </Style>
                                </Thumb.Style>
                            </Thumb>
                        </Track.Thumb>
                        <Track.IncreaseRepeatButton><RepeatButton Command='ScrollBar.PageDownCommand' Opacity='0' Focusable='False'/></Track.IncreaseRepeatButton>
                    </Track>
                </Grid>
            </ControlTemplate>
        </Setter.Value>
    </Setter>
</Style>");

        private readonly TextBox _searchBox;
        private readonly StackPanel _leftCol;
        private readonly StackPanel _rightCol;
        private List<HotkeyEntry> _entries = new List<HotkeyEntry>();
        private DateTime _lastShownUtc = DateTime.MinValue;

        public CheatsheetWindow()
        {
            Title = "Шпаргалка горячих клавиш • Yozora Sakura";
            Width = 900;
            Height = 560;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;
            FontFamily = new FontFamily("Segoe UI Variable, Segoe UI");

            Resources[typeof(ScrollBar)] = DarkScrollBarStyle;

            var root = new Border
            {
                Background = BgGlass,
                BorderBrush = BorderSubtle,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(16),
                SnapsToDevicePixels = true
            };

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Header
            var header = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };

            var closeBtn = new Button
            {
                Content = "\uE8BB",
                FontFamily = new FontFamily("Segoe Fluent Icons"),
                FontSize = 13,
                Width = 28,
                Height = 28,
                Foreground = DangerText,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand
            };
            closeBtn.Template = FlatButtonTemplate(6, DangerHover);
            closeBtn.Click += (s, e) => HidePopup();
            DockPanel.SetDock(closeBtn, Dock.Right);
            header.Children.Add(closeBtn);

            var searchWrap = new Border
            {
                Background = BgCard,
                BorderBrush = BorderCard,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(7),
                Width = 240,
                Margin = new Thickness(0, 0, 10, 0),
                Padding = new Thickness(8, 3, 8, 3)
            };
            var searchDock = new DockPanel();
            var icon = new TextBlock
            {
                Text = "\uE721",
                FontFamily = new FontFamily("Segoe Fluent Icons"),
                FontSize = 12,
                Foreground = TextMuted,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            };
            DockPanel.SetDock(icon, Dock.Left);
            searchDock.Children.Add(icon);
            _searchBox = new TextBox
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = TextPrimary,
                CaretBrush = AccentSakura,
                FontSize = 12.5,
                VerticalAlignment = VerticalAlignment.Center
            };
            _searchBox.TextChanged += (s, e) => RenderColumns();
            searchDock.Children.Add(_searchBox);
            searchWrap.Child = searchDock;
            DockPanel.SetDock(searchWrap, Dock.Right);
            header.Children.Add(searchWrap);

            var titleStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            titleStack.Children.Add(new TextBlock
            {
                Text = "\uE765",
                FontFamily = new FontFamily("Segoe Fluent Icons"),
                FontSize = 17,
                Foreground = AccentSakura,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            });
            titleStack.Children.Add(new TextBlock
            {
                Text = "Шпаргалка горячих клавиш",
                FontSize = 15.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = TextPrimary,
                VerticalAlignment = VerticalAlignment.Center
            });
            titleStack.Children.Add(new TextBlock
            {
                Text = "whkd + Komorebi + YASB Finder",
                FontSize = 11.5,
                Foreground = AccentCyan,
                Margin = new Thickness(10, 2, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            header.Children.Add(titleStack);

            Grid.SetRow(header, 0);
            grid.Children.Add(header);

            // Body ScrollViewer with 2 columns
            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            var cols = new Grid();
            cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            _leftCol = new StackPanel();
            _rightCol = new StackPanel();
            Grid.SetColumn(_leftCol, 0);
            Grid.SetColumn(_rightCol, 2);
            cols.Children.Add(_leftCol);
            cols.Children.Add(_rightCol);
            scroll.Content = cols;
            Grid.SetRow(scroll, 1);
            grid.Children.Add(scroll);

            // Footer
            var footer = new TextBlock
            {
                Text = "Alt + / — открыть/скрыть • Источник: C:\\Users\\Laziko\\.config\\whkdrc • Esc — закрыть",
                Foreground = TextMuted,
                FontSize = 11,
                Margin = new Thickness(2, 10, 2, 0)
            };
            Grid.SetRow(footer, 2);
            grid.Children.Add(footer);

            root.Child = grid;
            Content = root;

            PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    if (!string.IsNullOrEmpty(_searchBox.Text)) _searchBox.Text = "";
                    else HidePopup();
                    e.Handled = true;
                }
            };
            Deactivated += (s, e) =>
            {
                if ((DateTime.UtcNow - _lastShownUtc).TotalMilliseconds > 280)
                    HidePopup();
            };
        }

        public void TogglePopup()
        {
            if (IsVisible) HidePopup();
            else ShowPopup();
        }

        public void ShowPopup()
        {
            ReloadBindings();
            RenderColumns();

            var wa = SystemParameters.WorkArea;
            Left = wa.Left + (wa.Width - Width) / 2.0;
            Top = wa.Top + (wa.Height - Height) / 2.0;
            _lastShownUtc = DateTime.UtcNow;

            Show();
            Activate();
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero) Native.SetForegroundWindow(hwnd);
            Dispatcher.BeginInvoke(new Action(() => _searchBox.Focus()), DispatcherPriority.Input);
        }

        public void HidePopup()
        {
            Hide();
        }

        private void ReloadBindings()
        {
            var list = new List<HotkeyEntry>();
            if (File.Exists(WhkdrcPath))
            {
                foreach (var raw in File.ReadAllLines(WhkdrcPath, Encoding.UTF8))
                {
                    var line = raw.Trim();
                    if (string.IsNullOrEmpty(line) || line.StartsWith("#") || line.StartsWith(".shell"))
                        continue;

                    int colon = line.IndexOf(':');
                    if (colon <= 0) continue;
                    string keyRaw = line.Substring(0, colon).Trim();
                    string cmd = line.Substring(colon + 1).Trim();

                    string prettyKey = FormatKey(keyRaw);
                    string cat;
                    string desc = DescribeCommand(cmd, out cat);
                    list.Add(new HotkeyEntry { Category = cat, Keys = prettyKey, Description = desc });
                }
            }

            // Compact workspace numbers instead of 16 separate noisy lines
            CompressRange(list, "Воркспейсы (1–8)", "Переключить воркспейс", "Alt + 1..8", "Перейти на рабочий стол 1–8");
            CompressRange(list, "Воркспейсы (1–8)", "Переместить окно на воркспейс", "Alt + Shift + 1..8", "Перенести окно на стол 1–8");
            CompressRange(list, "Приложения на воркспейсе", "Переключиться на окно #", "Ctrl + Alt + 1..9", "Фокус на i-ю прогу текущего стола");

            // Add built-in Finder, Carousel & YASB shortcuts
            list.Add(new HotkeyEntry { Category = "Карусель обоев (Ctrl + Alt + W)", Keys = "← / → (A / D)", Description = "Переключить карточку влево / вправо" });
            list.Add(new HotkeyEntry { Category = "Карусель обоев (Ctrl + Alt + W)", Keys = "Enter / Space", Description = "Установить выбранные обои (круговой переход)" });
            list.Add(new HotkeyEntry { Category = "Карусель обоев (Ctrl + Alt + W)", Keys = "Колёсико / ‹ ›", Description = "Листать карточки в карусели" });
            list.Add(new HotkeyEntry { Category = "Карусель обоев (Ctrl + Alt + W)", Keys = "Esc", Description = "Закрыть карусель обоев" });

            list.Add(new HotkeyEntry { Category = "YASB Finder (Alt + E)", Keys = "Space", Description = "Quick Look (превью картинки / кода)" });
            list.Add(new HotkeyEntry { Category = "YASB Finder (Alt + E)", Keys = "Ctrl + Shift + N", Description = "Создать новую папку" });
            list.Add(new HotkeyEntry { Category = "YASB Finder (Alt + E)", Keys = "F2 / Del", Description = "Переименовать / удалить в Корзину" });
            list.Add(new HotkeyEntry { Category = "YASB Finder (Alt + E)", Keys = "Drag & Drop", Description = "Перетащить файл в другое окно" });

            _entries = list;
        }

        private static void CompressRange(List<HotkeyEntry> list, string category, string descPrefix, string mergedKeys, string mergedDesc)
        {
            int removed = list.RemoveAll(e => e.Category == category && e.Description.StartsWith(descPrefix, StringComparison.OrdinalIgnoreCase));
            if (removed > 0)
            {
                list.Add(new HotkeyEntry { Category = category, Keys = mergedKeys, Description = mergedDesc });
            }
        }

        private static string FormatKey(string raw)
        {
            var parts = raw.Split(new[] { '+' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i].Trim().ToLowerInvariant();
                switch (p)
                {
                    case "alt": parts[i] = "Alt"; break;
                    case "ctrl": parts[i] = "Ctrl"; break;
                    case "shift": parts[i] = "Shift"; break;
                    case "win": parts[i] = "Win"; break;
                    case "oem_1": parts[i] = ";"; break;
                    case "oem_2": parts[i] = "/"; break;
                    case "oem_3": parts[i] = "~ (`)"; break;
                    case "oem_4": parts[i] = "["; break;
                    case "oem_6": parts[i] = "]"; break;
                    case "oem_plus": parts[i] = "+"; break;
                    case "oem_minus": parts[i] = "-"; break;
                    case "return": parts[i] = "Enter"; break;
                    case "space": parts[i] = "Space"; break;
                    case "left": parts[i] = "←"; break;
                    case "right": parts[i] = "→"; break;
                    case "up": parts[i] = "↑"; break;
                    case "down": parts[i] = "↓"; break;
                    default: parts[i] = p.ToUpperInvariant(); break;
                }
            }
            return string.Join(" + ", parts);
        }

        private static string DescribeCommand(string cmd, out string category)
        {
            string lower = cmd.ToLowerInvariant();
            if (lower.Contains("smart_launcher.exe")) { category = "Запуск и Утилиты"; return "Умный поиск программ (РУС / ENG / Раскладка / Кальк)"; }
            if (lower.Contains("folder_menu.exe")) { category = "Запуск и Утилиты"; return "YASB Finder (проводник по колонкам)"; }
            if (lower.Contains("hotkey_cheatsheet.exe")) { category = "Запуск и Утилиты"; return "Эта шпаргалка горячих клавиш"; }
            if (lower.Contains("scratchpad.exe")) { category = "Запуск и Утилиты"; return "Выпадающий Scratchpad (Заметки / Калькулятор / PS)"; }
            if (lower.Contains("live_wallpaper.exe --mode")) { category = "Запуск и Утилиты"; return "Режим живых обоев (Видео / Фото)"; }
            if (lower.Contains("live_wallpaper.exe --prev")) { category = "Запуск и Утилиты"; return "Предыдущие живые обои (круговой переход)"; }
            if (lower.Contains("live_wallpaper.exe --next")) { category = "Запуск и Утилиты"; return "Следующие живые обои (круговой переход)"; }
            if (lower.Contains("live_wallpaper.exe")) { category = "Запуск и Утилиты"; return "Вкл/выкл живые обои на рабочем столе"; }
            if (lower.Contains("wallpaper_gallery.exe")) { category = "Запуск и Утилиты"; return "Карусель обоев (Видео или Фото)"; }
            if (lower.Contains("effects_menu.exe")) { category = "Запуск и Утилиты"; return "Меню-переключатель обоев и визуализатора"; }
            if (lower.Contains("audio_visualizer.exe --mode")) { category = "Запуск и Утилиты"; return "Режим аудио-визуализатора (Столбцы / Зеркало / Волна)"; }
            if (lower.Contains("audio_visualizer.exe")) { category = "Запуск и Утилиты"; return "Вкл/выкл живой аудио-визуализатор на рабочем столе"; }
            if (lower.Contains("ms-settings")) { category = "Запуск и Утилиты"; return "Параметры Windows"; }
            if (lower.Contains("whkd.exe") || lower.Contains("--reload-whkd")) { category = "Система и Конфиг"; return "Перезагрузить whkd"; }
            if (lower.Contains("reload-configuration")) { category = "Система и Конфиг"; return "Перезагрузить Komorebi"; }
            if (lower.Contains("toggle-shortcuts")) { category = "Система и Конфиг"; return "Вкл/выкл бинды Komorebi"; }
            if (lower.Contains("retile")) { category = "Система и Конфиг"; return "Перестроить окна (retile)"; }
            if (lower.Contains("toggle-pause")) { category = "Система и Конфиг"; return "Пауза / продолжить Komorebi"; }

            if (lower.Contains("workspace_app.exe"))
            {
                category = "Приложения на воркспейсе";
                if (lower.Contains("cycle next")) return "Следующая прога этого воркспейса";
                if (lower.Contains("cycle prev")) return "Предыдущая прога этого воркспейса";
                if (lower.Contains("slot ")) return "Переключиться на окно # текущего стола";
                if (lower.Contains("windowsterminal")) return "Terminal (фокус здесь или новое окно)";
                if (lower.Contains("explorer.exe")) return "Проводник (фокус здесь или новое окно)";
                if (lower.Contains("msedge.exe")) return "Edge (фокус здесь или новое окно)";
                if (lower.Contains("code.exe")) return "VS Code (фокус здесь или новое окно)";
                return "Запуск / фокус приложения";
            }

            if (lower.Contains("focus-workspace")) { category = "Воркспейсы (1–8)"; return "Переключить воркспейс"; }
            if (lower.Contains("move-to-workspace")) { category = "Воркспейсы (1–8)"; return "Переместить окно на воркспейс"; }

            if (lower.Contains("stack ") || lower.Contains("unstack") || lower.Contains("cycle-stack"))
            {
                category = "Стеки и Раскладка";
                if (lower.Contains("unstack")) return "Разобрать вкладку из стека";
                if (lower.Contains("cycle-stack previous")) return "Предыдущая вкладка стека";
                if (lower.Contains("cycle-stack next")) return "Следующая вкладка стека";
                return "Добавить окно в стек (" + LastWord(cmd) + ")";
            }
            if (lower.Contains("resize-axis") || lower.Contains("flip-layout"))
            {
                category = "Стеки и Раскладка";
                if (lower.Contains("horizontal increase")) return "Расширить окно по горизонтали";
                if (lower.Contains("horizontal decrease")) return "Сузить окно по горизонтали";
                if (lower.Contains("vertical increase")) return "Увеличить окно по вертикали";
                if (lower.Contains("vertical decrease")) return "Уменьшить окно по вертикали";
                if (lower.Contains("flip-layout")) return "Отразить макет (" + LastWord(cmd) + ")";
            }

            category = "Окна и Фокус (Komorebi)";
            if (lower.Contains(" close")) return "Закрыть активное окно";
            if (lower.Contains(" minimize")) return "Свернуть активное окно";
            if (lower.Contains("focus ")) return "Фокус на окно: " + LastWord(cmd);
            if (lower.Contains("cycle-focus previous")) return "Предыдущее окно по кругу";
            if (lower.Contains("cycle-focus next")) return "Следующее окно по кругу";
            if (lower.Contains("move ")) return "Сдвинуть окно: " + LastWord(cmd);
            if (lower.Contains("promote")) return "Сделать окно главным (Promote)";
            if (lower.Contains("toggle-float")) return "Плавающее окно (Float)";
            if (lower.Contains("toggle-monocle")) return "Режим Monocle (на весь экран)";

            return cmd;
        }

        private static string LastWord(string s)
        {
            int comment = s.IndexOf('#');
            if (comment >= 0) s = s.Substring(0, comment).Trim();
            var parts = s.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length > 0 ? parts[parts.Length - 1] : s;
        }

        private void RenderColumns()
        {
            _leftCol.Children.Clear();
            _rightCol.Children.Clear();

            string q = (_searchBox.Text ?? "").Trim();
            var filtered = string.IsNullOrEmpty(q)
                ? _entries
                : _entries
                    .Select(e => new
                    {
                        Entry = e,
                        Match = YasbSearch.SmartSearchMatcher.Match(
                            e.Keys + " " + e.Description + " " + e.Category, q)
                    })
                    .Where(x => x.Match.Score > 0)
                    .OrderByDescending(x => x.Match.Score)
                    .Select(x => x.Entry)
                    .ToList();

            string[] leftCats = { "Запуск и Утилиты", "Приложения на воркспейсе", "YASB Finder (Alt + E)", "Система и Конфиг" };
            var groups = filtered.GroupBy(e => e.Category).ToList();

            foreach (var g in groups)
            {
                var card = BuildCategoryCard(g.Key, g.ToList());
                if (leftCats.Contains(g.Key))
                    _leftCol.Children.Add(card);
                else
                    _rightCol.Children.Add(card);
            }
        }

        private UIElement BuildCategoryCard(string title, List<HotkeyEntry> items)
        {
            var card = new Border
            {
                Background = BgCard,
                BorderBrush = BorderCard,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(0, 0, 0, 10)
            };

            var stack = new StackPanel();
            Brush titleBrush = AccentSakura;
            if (title.Contains("Приложения")) titleBrush = AccentCyan;
            else if (title.Contains("Finder")) titleBrush = AccentGold;
            else if (title.Contains("Воркспейсы")) titleBrush = AccentJade;

            stack.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = titleBrush,
                Margin = new Thickness(0, 0, 0, 8)
            });

            foreach (var item in items)
            {
                var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(148) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var keyBadge = new Border
                {
                    Background = BgPill,
                    BorderBrush = BorderCard,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(5),
                    Padding = new Thickness(7, 3, 7, 3),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Center
                };
                keyBadge.Child = new TextBlock
                {
                    Text = item.Keys,
                    FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = TextPrimary
                };
                Grid.SetColumn(keyBadge, 0);
                row.Children.Add(keyBadge);

                var desc = new TextBlock
                {
                    Text = item.Description,
                    FontSize = 12,
                    Foreground = TextMuted,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(6, 0, 0, 0)
                };
                Grid.SetColumn(desc, 1);
                row.Children.Add(desc);

                stack.Children.Add(row);
            }

            card.Child = stack;
            return card;
        }

        private static ControlTemplate FlatButtonTemplate(double radius, Brush hoverBrush)
        {
            var template = new ControlTemplate(typeof(Button));
            var bd = new FrameworkElementFactory(typeof(Border));
            bd.Name = "bd";
            bd.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
            bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(radius));
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cp.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
            bd.AppendChild(cp);
            template.VisualTree = bd;
            var trg = new Trigger { Property = Button.IsMouseOverProperty, Value = true };
            trg.Setters.Add(new Setter(Border.BackgroundProperty, hoverBrush, "bd"));
            template.Triggers.Add(trg);
            return template;
        }

        private static SolidColorBrush BrushFromHex(string hex)
        {
            var b = (SolidColorBrush)new BrushConverter().ConvertFromString(hex);
            b.Freeze();
            return b;
        }
    }
}
