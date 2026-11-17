using System;
using System.Collections.Generic;
using System.Diagnostics;
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
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.VisualBasic.FileIO;
using SearchOption = System.IO.SearchOption;

namespace YasbFolderMenu
{
    internal class FsItem
    {
        public string Name { get; set; }
        public string FullPath { get; set; }
        public bool IsDirectory { get; set; }
        public bool IsDrive { get; set; }
        public bool IsChevron { get; set; }
        public bool IsPinned { get; set; }
        public string IconGlyph { get; set; }
        public string MetaText { get; set; }
    }

    internal static class Native
    {
        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);
    }

    public class FolderMenuApp : Application
    {
        private const string MutexName = @"Local\YASB_FolderMenu_Mutex";
        private const string ToggleEventName = @"Local\YASB_FolderMenu_Toggle";
        private static Mutex _mutex;
        private FolderBrowserWindow _window;
        private EventWaitHandle _toggleEvent;

        [STAThread]
        public static void Main(string[] args)
        {
            bool backgroundOnly = args.Any(a => a.Equals("--background", StringComparison.OrdinalIgnoreCase));

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

            var app = new FolderMenuApp();
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            app.StartApp(backgroundOnly);
            app.Run();
        }

        private void StartApp(bool backgroundOnly)
        {
            _toggleEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ToggleEventName);
            _window = new FolderBrowserWindow();

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
            {
                _window.ShowPopup();
            }
        }
    }

    internal class FolderBrowserWindow : Window
    {
        // Yozora Sakura (Anime Night Neon) palette
        private static readonly Brush BgGlass = BrushFromHex("#F7131122");
        private static readonly Brush BgSurface = BrushFromHex("#FF1B182E");
        private static readonly Brush BgColumn = BrushFromHex("#D4100E1B");
        private static readonly Brush BgPreview = BrushFromHex("#CC151326");
        private static readonly Brush BorderSubtle = BrushFromHex("#44FF7EB6");
        private static readonly Brush BorderCard = BrushFromHex("#24D8B4FE");
        private static readonly Brush AccentBrush = BrushFromHex("#FFFF7EB6");   // Sakura Pink
        private static readonly Brush CyanBrush = BrushFromHex("#FF6CE5FF");     // Miku Cyan
        private static readonly Brush FolderBrush = BrushFromHex("#FFFFD479");   // Kitsune Gold
        private static readonly Brush FileBrush = BrushFromHex("#FF6CE5FF");     // Cyan
        private static readonly Brush PinBrush = BrushFromHex("#FFFF7EB6");      // Sakura
        private static readonly Brush TextPrimary = BrushFromHex("#FFF2EEFF");   // Moonlight White
        private static readonly Brush TextMuted = BrushFromHex("#FF9E95BD");     // Twilight Lavender
        private static readonly Brush ItemHover = BrushFromHex("#24D8B4FE");
        private static readonly Brush ItemSelected = BrushFromHex("#3EFF7EB6");
        private static readonly Brush DangerHover = BrushFromHex("#44FF5D8F");
        private static readonly Brush DangerText = BrushFromHex("#FFFF5D8F");

        private static readonly string PinsFilePath = Path.Combine(
            Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? @"C:\Users\Laziko\.config\yasb",
            "folder_menu_pins.txt");

        private static readonly HashSet<string> ImageExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif", ".ico"
        };

        private static readonly HashSet<string> TextExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".txt", ".md", ".cs", ".py", ".js", ".ts", ".tsx", ".jsx", ".json", ".yaml", ".yml",
            ".css", ".html", ".htm", ".xml", ".ini", ".cfg", ".conf", ".cpp", ".c", ".h", ".hpp",
            ".rs", ".go", ".sh", ".ps1", ".bat", ".log", ".sql", ".gitignore"
        };

        private static readonly Style DarkScrollBarStyle = CreateStyleFromXaml(@"
<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type ScrollBar}'>
    <Setter Property='Stylus.IsFlicksEnabled' Value='False'/>
    <Setter Property='Background' Value='Transparent'/>
    <Setter Property='Width' Value='6'/>
    <Setter Property='MinWidth' Value='6'/>
    <Setter Property='Template'>
        <Setter.Value>
            <ControlTemplate TargetType='{x:Type ScrollBar}'>
                <Grid Background='Transparent' SnapsToDevicePixels='True'>
                    <Track x:Name='PART_Track' IsDirectionReversed='True'>
                        <Track.DecreaseRepeatButton>
                            <RepeatButton Command='ScrollBar.PageUpCommand' Opacity='0' Focusable='False'/>
                        </Track.DecreaseRepeatButton>
                        <Track.Thumb>
                            <Thumb>
                                <Thumb.Style>
                                    <Style TargetType='{x:Type Thumb}'>
                                        <Setter Property='OverridesDefaultStyle' Value='True'/>
                                        <Setter Property='IsTabStop' Value='False'/>
                                        <Setter Property='Template'>
                                            <Setter.Value>
                                                <ControlTemplate TargetType='{x:Type Thumb}'>
                                                    <Border x:Name='thumbBd' Background='#FF423B66' CornerRadius='3' Margin='1,0,1,0'/>
                                                    <ControlTemplate.Triggers>
                                                        <Trigger Property='IsMouseOver' Value='True'>
                                                            <Setter TargetName='thumbBd' Property='Background' Value='#FF786BB0'/>
                                                        </Trigger>
                                                        <Trigger Property='IsDragging' Value='True'>
                                                            <Setter TargetName='thumbBd' Property='Background' Value='#FFFF7EB6'/>
                                                        </Trigger>
                                                    </ControlTemplate.Triggers>
                                                </ControlTemplate>
                                            </Setter.Value>
                                        </Setter>
                                    </Style>
                                </Thumb.Style>
                            </Thumb>
                        </Track.Thumb>
                        <Track.IncreaseRepeatButton>
                            <RepeatButton Command='ScrollBar.PageDownCommand' Opacity='0' Focusable='False'/>
                        </Track.IncreaseRepeatButton>
                    </Track>
                </Grid>
            </ControlTemplate>
        </Setter.Value>
    </Setter>
    <Style.Triggers>
        <Trigger Property='Orientation' Value='Horizontal'>
            <Setter Property='Width' Value='Auto'/>
            <Setter Property='MinWidth' Value='0'/>
            <Setter Property='Height' Value='6'/>
            <Setter Property='MinHeight' Value='6'/>
            <Setter Property='Template'>
                <Setter.Value>
                    <ControlTemplate TargetType='{x:Type ScrollBar}'>
                        <Grid Background='Transparent' SnapsToDevicePixels='True'>
                            <Track x:Name='PART_Track' IsDirectionReversed='False'>
                                <Track.DecreaseRepeatButton>
                                    <RepeatButton Command='ScrollBar.PageLeftCommand' Opacity='0' Focusable='False'/>
                                </Track.DecreaseRepeatButton>
                                <Track.Thumb>
                                    <Thumb>
                                        <Thumb.Style>
                                            <Style TargetType='{x:Type Thumb}'>
                                                <Setter Property='OverridesDefaultStyle' Value='True'/>
                                                <Setter Property='IsTabStop' Value='False'/>
                                                <Setter Property='Template'>
                                                    <Setter.Value>
                                                        <ControlTemplate TargetType='{x:Type Thumb}'>
                                                            <Border x:Name='thumbBd' Background='#FF423B66' CornerRadius='3' Margin='0,1,0,1'/>
                                                            <ControlTemplate.Triggers>
                                                                <Trigger Property='IsMouseOver' Value='True'>
                                                                    <Setter TargetName='thumbBd' Property='Background' Value='#FF786BB0'/>
                                                                </Trigger>
                                                                <Trigger Property='IsDragging' Value='True'>
                                                                    <Setter TargetName='thumbBd' Property='Background' Value='#FFFF7EB6'/>
                                                                </Trigger>
                                                            </ControlTemplate.Triggers>
                                                        </ControlTemplate>
                                                    </Setter.Value>
                                                </Setter>
                                            </Style>
                                        </Thumb.Style>
                                    </Thumb>
                                </Track.Thumb>
                                <Track.IncreaseRepeatButton>
                                    <RepeatButton Command='ScrollBar.PageRightCommand' Opacity='0' Focusable='False'/>
                                </Track.IncreaseRepeatButton>
                            </Track>
                        </Grid>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Trigger>
    </Style.Triggers>
</Style>");

        private static readonly Style DarkToolTipStyle = CreateStyleFromXaml(@"
<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type ToolTip}'>
    <Setter Property='Background' Value='#FF1B182E'/>
    <Setter Property='Foreground' Value='#FFF2EEFF'/>
    <Setter Property='BorderBrush' Value='#66FF7EB6'/>
    <Setter Property='BorderThickness' Value='1'/>
    <Setter Property='Padding' Value='8,4'/>
    <Setter Property='FontSize' Value='11.5'/>
    <Setter Property='HasDropShadow' Value='True'/>
    <Setter Property='Template'>
        <Setter.Value>
            <ControlTemplate TargetType='{x:Type ToolTip}'>
                <Border Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='6' Padding='{TemplateBinding Padding}'>
                    <ContentPresenter/>
                </Border>
            </ControlTemplate>
        </Setter.Value>
    </Setter>
</Style>");

        private static readonly Style DarkContextMenuStyle = CreateStyleFromXaml(@"
<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type ContextMenu}'>
    <Setter Property='Background' Value='#FF1B182E'/>
    <Setter Property='Foreground' Value='#FFF2EEFF'/>
    <Setter Property='BorderBrush' Value='#66FF7EB6'/>
    <Setter Property='BorderThickness' Value='1'/>
    <Setter Property='Padding' Value='5'/>
    <Setter Property='HasDropShadow' Value='True'/>
    <Setter Property='Template'>
        <Setter.Value>
            <ControlTemplate TargetType='{x:Type ContextMenu}'>
                <Border Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='9' Padding='{TemplateBinding Padding}' SnapsToDevicePixels='True'>
                    <StackPanel IsItemsHost='True' KeyboardNavigation.DirectionalNavigation='Cycle'/>
                </Border>
            </ControlTemplate>
        </Setter.Value>
    </Setter>
</Style>");

        private static readonly Style DarkMenuItemStyle = CreateStyleFromXaml(@"
<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type MenuItem}'>
    <Setter Property='Background' Value='Transparent'/>
    <Setter Property='Foreground' Value='#FFF2EEFF'/>
    <Setter Property='FontSize' Value='12.5'/>
    <Setter Property='Padding' Value='12,7,16,7'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='Template'>
        <Setter.Value>
            <ControlTemplate TargetType='{x:Type MenuItem}'>
                <Border x:Name='bd' Background='{TemplateBinding Background}' CornerRadius='6' Padding='{TemplateBinding Padding}' Margin='0,1'>
                    <ContentPresenter ContentSource='Header' VerticalAlignment='Center'/>
                </Border>
                <ControlTemplate.Triggers>
                    <Trigger Property='IsHighlighted' Value='True'>
                        <Setter TargetName='bd' Property='Background' Value='#FF2D274D'/>
                        <Setter Property='Foreground' Value='#FFFF7EB6'/>
                    </Trigger>
                </ControlTemplate.Triggers>
            </ControlTemplate>
        </Setter.Value>
    </Setter>
</Style>");

        private static Style CreateStyleFromXaml(string xaml)
        {
            return (Style)XamlReader.Parse(xaml);
        }

        private readonly StackPanel _columnsPanel;
        private readonly ScrollViewer _columnsScroll;
        private readonly ColumnDefinition _previewColDef;
        private readonly Border _previewHost;
        private readonly Image _previewImage;
        private readonly Border _previewImageBox;
        private readonly TextBlock _previewIcon;
        private readonly TextBlock _previewTitle;
        private readonly TextBlock _previewSubtitle;
        private readonly TextBlock _previewMeta;
        private readonly Border _previewCodeBox;
        private readonly TextBox _previewCodeText;
        private readonly StackPanel _breadcrumbPanel;
        private readonly TextBox _filterBox;
        private readonly TextBlock _statusText;

        // Quick Look overlay
        private readonly Border _quickLookOverlay;
        private readonly Image _quickLookImage;
        private readonly TextBox _quickLookText;
        private readonly TextBlock _quickLookTitle;

        // Inline input modal (New Folder / Rename)
        private readonly Border _promptOverlay;
        private readonly TextBlock _promptLabel;
        private readonly TextBox _promptInput;
        private Action<string> _promptCallback;

        private readonly List<ColumnView> _columns = new List<ColumnView>();
        private FsItem _selectedItem;
        private int _activeColumnIndex = 0;
        private bool _suppressDeactivate = false;
        private bool _showPreview = true;
        private DateTime _lastShownUtc = DateTime.MinValue;

        public FolderBrowserWindow()
        {
            Title = "YASB Finder (Yozora Sakura)";
            Width = 980;
            Height = 530;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;
            FontFamily = new FontFamily("Segoe UI Variable, Segoe UI");

            Resources[typeof(ScrollBar)] = DarkScrollBarStyle;
            Resources[typeof(ToolTip)] = DarkToolTipStyle;
            Resources[typeof(ContextMenu)] = DarkContextMenuStyle;
            Resources[typeof(MenuItem)] = DarkMenuItemStyle;

            var rootBorder = new Border
            {
                Background = BgGlass,
                BorderBrush = BorderSubtle,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(12),
                SnapsToDevicePixels = true
            };

            var rootOverlayGrid = new Grid();

            var mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Top header: breadcrumbs + search + action buttons
            var headerGrid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(185) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var crumbScroll = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalAlignment = VerticalAlignment.Center
            };
            _breadcrumbPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            crumbScroll.Content = _breadcrumbPanel;
            Grid.SetColumn(crumbScroll, 0);
            headerGrid.Children.Add(crumbScroll);

            // Search box
            var searchBorder = new Border
            {
                Background = BgColumn,
                BorderBrush = BorderCard,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(7),
                Margin = new Thickness(8, 0, 8, 0),
                Padding = new Thickness(8, 3, 8, 3)
            };
            var searchDock = new DockPanel();
            var searchIcon = new TextBlock
            {
                Text = "\uE721",
                FontFamily = new FontFamily("Segoe Fluent Icons"),
                FontSize = 12,
                Foreground = TextMuted,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            };
            DockPanel.SetDock(searchIcon, Dock.Left);
            searchDock.Children.Add(searchIcon);

            _filterBox = new TextBox
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = TextPrimary,
                CaretBrush = AccentBrush,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            };
            _filterBox.TextChanged += (s, e) => ApplyFilterToActiveColumn();
            searchDock.Children.Add(_filterBox);
            searchBorder.Child = searchDock;
            Grid.SetColumn(searchBorder, 1);
            headerGrid.Children.Add(searchBorder);

            // Action buttons
            var actionsPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            actionsPanel.Children.Add(CreateHeaderButton("\uE8F4", "Новая папка (Ctrl+Shift+N)", () => PromptNewFolder()));
            actionsPanel.Children.Add(CreateHeaderButton("\uE8B3", "Предпросмотр / Инспектор", () => TogglePreviewPane()));
            actionsPanel.Children.Add(CreateHeaderButton("\uE838", "Открыть в Проводнике", () => OpenInExplorer(TargetFolder())));
            actionsPanel.Children.Add(CreateHeaderButton("\uE943", "Открыть в VS Code", () => OpenInVsCode(TargetPath())));
            actionsPanel.Children.Add(CreateHeaderButton("\uE756", "Открыть Terminal здесь", () => OpenTerminalHere(TargetFolder())));
            actionsPanel.Children.Add(CreateHeaderButton("\uE8C8", "Копировать путь", () => CopyPath(TargetPath())));
            actionsPanel.Children.Add(CreateHeaderButton("\uE897", "Шпаргалка клавиш (Alt+/)", () => OpenCheatsheet()));
            actionsPanel.Children.Add(CreateHeaderButton("\uE8BB", "Закрыть (Esc)", () => HidePopup(), isClose: true));
            Grid.SetColumn(actionsPanel, 2);
            headerGrid.Children.Add(actionsPanel);

            Grid.SetRow(headerGrid, 0);
            mainGrid.Children.Add(headerGrid);

            // Body: Miller Columns + Right Inspector/Preview Pane
            var bodyGrid = new Grid();
            bodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _previewColDef = new ColumnDefinition { Width = new GridLength(255) };
            bodyGrid.ColumnDefinitions.Add(_previewColDef);

            var columnsHost = new Border
            {
                Background = BgColumn,
                BorderBrush = BorderCard,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(4)
            };
            _columnsScroll = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            _columnsPanel = new StackPanel { Orientation = Orientation.Horizontal };
            _columnsScroll.Content = _columnsPanel;
            columnsHost.Child = _columnsScroll;
            Grid.SetColumn(columnsHost, 0);
            bodyGrid.Children.Add(columnsHost);

            // Inspector / Preview Pane
            _previewHost = new Border
            {
                Background = BgPreview,
                BorderBrush = BorderCard,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Margin = new Thickness(8, 0, 0, 0),
                Padding = new Thickness(12)
            };
            var previewStack = new StackPanel();

            _previewImageBox = new Border
            {
                Background = BrushFromHex("#55100E1B"),
                BorderBrush = BorderCard,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Height = 160,
                Margin = new Thickness(0, 0, 0, 10),
                Visibility = Visibility.Collapsed
            };
            _previewImage = new Image
            {
                Stretch = Stretch.Uniform,
                Margin = new Thickness(4)
            };
            _previewImageBox.Child = _previewImage;
            previewStack.Children.Add(_previewImageBox);

            _previewIcon = new TextBlock
            {
                Text = "\uE8B7",
                FontFamily = new FontFamily("Segoe Fluent Icons"),
                FontSize = 38,
                Foreground = FolderBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 16, 0, 10)
            };
            previewStack.Children.Add(_previewIcon);

            _previewTitle = new TextBlock
            {
                Text = "Выберите файл или папку",
                FontSize = 13.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = TextPrimary,
                TextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            previewStack.Children.Add(_previewTitle);

            _previewSubtitle = new TextBlock
            {
                Text = "Пробел — Quick Look",
                FontSize = 11,
                Foreground = CyanBrush,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 3, 0, 10),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            previewStack.Children.Add(_previewSubtitle);

            _previewCodeBox = new Border
            {
                Background = BrushFromHex("#AA0D0B16"),
                BorderBrush = BorderCard,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(8),
                MaxHeight = 200,
                Margin = new Thickness(0, 0, 0, 8),
                Visibility = Visibility.Collapsed
            };
            _previewCodeText = new TextBox
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = TextPrimary,
                FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                FontSize = 10.5,
                IsReadOnly = true,
                TextWrapping = TextWrapping.NoWrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            _previewCodeBox.Child = _previewCodeText;
            previewStack.Children.Add(_previewCodeBox);

            _previewMeta = new TextBlock
            {
                Text = "",
                FontSize = 11.5,
                Foreground = TextMuted,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 18
            };
            previewStack.Children.Add(_previewMeta);

            _previewHost.Child = new ScrollViewer
            {
                Content = previewStack,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            Grid.SetColumn(_previewHost, 1);
            bodyGrid.Children.Add(_previewHost);

            Grid.SetRow(bodyGrid, 1);
            mainGrid.Children.Add(bodyGrid);

            // Footer status
            _statusText = new TextBlock
            {
                Foreground = TextMuted,
                FontSize = 11,
                Margin = new Thickness(4, 8, 4, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
                Text = "Клик: колонка • Перетащить: Drag&Drop • Space: Quick Look • F2: переименовать • Del: в корзину • ПКМ: меню"
            };
            Grid.SetRow(_statusText, 2);
            mainGrid.Children.Add(_statusText);

            rootOverlayGrid.Children.Add(mainGrid);

            // Quick Look Modal Overlay
            _quickLookOverlay = new Border
            {
                Background = BrushFromHex("#EE0E0C1A"),
                BorderBrush = AccentBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(14),
                Margin = new Thickness(28, 36, 28, 24),
                Visibility = Visibility.Collapsed
            };
            var qlGrid = new Grid();
            qlGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            qlGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            var qlTop = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var qlClose = CreateHeaderButton("\uE8BB", "Закрыть Quick Look (Space / Esc)", () => CloseQuickLook(), isClose: true);
            DockPanel.SetDock(qlClose, Dock.Right);
            qlTop.Children.Add(qlClose);
            _quickLookTitle = new TextBlock
            {
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = TextPrimary,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            qlTop.Children.Add(_quickLookTitle);
            Grid.SetRow(qlTop, 0);
            qlGrid.Children.Add(qlTop);

            var qlContentHost = new Grid();
            _quickLookImage = new Image { Stretch = Stretch.Uniform, Visibility = Visibility.Collapsed };
            _quickLookText = new TextBox
            {
                Background = BrushFromHex("#FF141122"),
                Foreground = TextPrimary,
                BorderBrush = BorderCard,
                BorderThickness = new Thickness(1),
                FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                FontSize = 12,
                Padding = new Thickness(10),
                IsReadOnly = true,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                Visibility = Visibility.Collapsed
            };
            qlContentHost.Children.Add(_quickLookImage);
            qlContentHost.Children.Add(_quickLookText);
            Grid.SetRow(qlContentHost, 1);
            qlGrid.Children.Add(qlContentHost);
            _quickLookOverlay.Child = qlGrid;
            rootOverlayGrid.Children.Add(_quickLookOverlay);

            // Inline Input Prompt Overlay (New Folder / Rename)
            _promptOverlay = new Border
            {
                Background = BrushFromHex("#CC0A0814"),
                CornerRadius = new CornerRadius(12),
                Visibility = Visibility.Collapsed
            };
            var promptCard = new Border
            {
                Background = BgSurface,
                BorderBrush = AccentBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(16),
                Width = 380,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            var promptStack = new StackPanel();
            _promptLabel = new TextBlock
            {
                Foreground = TextPrimary,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 10)
            };
            promptStack.Children.Add(_promptLabel);
            _promptInput = new TextBox
            {
                Background = BgColumn,
                Foreground = TextPrimary,
                CaretBrush = AccentBrush,
                BorderBrush = BorderSubtle,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 6, 8, 6),
                FontSize = 13
            };
            _promptInput.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    e.Handled = true;
                    SubmitPrompt(true);
                }
                else if (e.Key == Key.Escape)
                {
                    e.Handled = true;
                    SubmitPrompt(false);
                }
            };
            promptStack.Children.Add(_promptInput);

            var promptHint = new TextBlock
            {
                Text = "Enter — подтвердить • Esc — отмена",
                Foreground = TextMuted,
                FontSize = 11,
                Margin = new Thickness(0, 8, 0, 0)
            };
            promptStack.Children.Add(promptHint);
            promptCard.Child = promptStack;
            _promptOverlay.Child = promptCard;
            rootOverlayGrid.Children.Add(_promptOverlay);

            rootBorder.Child = rootOverlayGrid;
            Content = rootBorder;

            PreviewKeyDown += OnWindowKeyDown;
            Deactivated += (s, e) =>
            {
                if (!_suppressDeactivate && (DateTime.UtcNow - _lastShownUtc).TotalMilliseconds > 300)
                {
                    CloseQuickLook();
                    HidePopup();
                }
            };

            LoadRootColumn();
        }

        public void TogglePopup()
        {
            if (IsVisible)
                HidePopup();
            else
                ShowPopup();
        }

        public void ShowPopup()
        {
            var wa = SystemParameters.WorkArea;
            Left = Math.Max(wa.Left + 8, Math.Min(wa.Left + 180, wa.Right - Width - 8));
            Top = Math.Max(wa.Top + 4, 44);
            _lastShownUtc = DateTime.UtcNow;

            if (_columns.Count == 0)
                LoadRootColumn();

            Show();
            Activate();
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero) Native.SetForegroundWindow(hwnd);
        }

        public void HidePopup()
        {
            _promptOverlay.Visibility = Visibility.Collapsed;
            _quickLookOverlay.Visibility = Visibility.Collapsed;
            Hide();
        }

        private List<string> LoadPinnedPaths()
        {
            var list = new List<string>();
            try
            {
                if (File.Exists(PinsFilePath))
                {
                    foreach (var line in File.ReadAllLines(PinsFilePath, Encoding.UTF8))
                    {
                        var p = (line ?? "").Trim();
                        if (!string.IsNullOrEmpty(p) && Directory.Exists(p) && !list.Contains(p, StringComparer.OrdinalIgnoreCase))
                            list.Add(p);
                    }
                    return list;
                }
            }
            catch { }

            string[] defaults =
            {
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                @"D:\Projects"
            };
            foreach (var d in defaults)
            {
                if (!string.IsNullOrEmpty(d) && Directory.Exists(d) && !list.Contains(d, StringComparer.OrdinalIgnoreCase))
                    list.Add(d);
            }
            SavePinnedPaths(list);
            return list;
        }

        private void SavePinnedPaths(List<string> paths)
        {
            try
            {
                File.WriteAllLines(PinsFilePath, paths, Encoding.UTF8);
            }
            catch { }
        }

        internal bool IsPathPinned(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            return LoadPinnedPaths().Any(p => p.Equals(path, StringComparison.OrdinalIgnoreCase));
        }

        internal void TogglePinPath(string path)
        {
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return;
            var pins = LoadPinnedPaths();
            int idx = pins.FindIndex(p => p.Equals(path, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0) pins.RemoveAt(idx);
            else pins.Insert(0, path);
            SavePinnedPaths(pins);

            string currentRight = _columns.Count > 1 ? _columns[1].DirPath : null;
            LoadRootColumn();
            if (!string.IsNullOrEmpty(currentRight) && Directory.Exists(currentRight))
            {
                AddColumn(Path.GetFileName(currentRight), currentRight, ReadDirectory(currentRight));
            }
        }

        private void LoadRootColumn()
        {
            _columns.Clear();
            _columnsPanel.Children.Clear();
            var rootItems = new List<FsItem>();

            foreach (var pin in LoadPinnedPaths())
            {
                string label = pin.Equals(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), StringComparison.OrdinalIgnoreCase)
                    ? "Пользователь (~)"
                    : Path.GetFileName(pin.TrimEnd('\\'));
                if (string.IsNullOrEmpty(label)) label = pin;
                rootItems.Add(new FsItem
                {
                    Name = label,
                    FullPath = pin,
                    IsDirectory = true,
                    IsPinned = true,
                    IconGlyph = "\uE718",
                    MetaText = "\uE76C",
                    IsChevron = true
                });
            }

            foreach (var drive in DriveInfo.GetDrives())
            {
                if (!drive.IsReady) continue;
                string vol = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? "Локальный диск" : drive.VolumeLabel;
                string letter = drive.Name.TrimEnd('\\');
                rootItems.Add(new FsItem
                {
                    Name = string.Format("{0} ({1})", vol, letter),
                    FullPath = drive.RootDirectory.FullName,
                    IsDirectory = true,
                    IsDrive = true,
                    IconGlyph = "\uEDA2",
                    MetaText = FormatBytes(drive.AvailableFreeSpace)
                });
            }

            AddColumn("Компьютер", null, rootItems);
            UpdateBreadcrumbs();
            UpdatePreview(null);
        }

        private void AddColumn(string title, string dirPath, List<FsItem> items)
        {
            int colIndex = _columns.Count;
            var col = new ColumnView(this, colIndex, title, dirPath, items);
            _columns.Add(col);
            _columnsPanel.Children.Add(col);
            _activeColumnIndex = colIndex;
            Dispatcher.BeginInvoke(new Action(() => _columnsScroll.ScrollToRightEnd()), DispatcherPriority.Loaded);
        }

        internal void OnItemSelected(int columnIndex, FsItem item, bool activateFocus = true)
        {
            _selectedItem = item;
            _activeColumnIndex = columnIndex;

            while (_columns.Count > columnIndex + 1)
            {
                int last = _columns.Count - 1;
                _columnsPanel.Children.Remove(_columns[last]);
                _columns.RemoveAt(last);
            }

            _filterBox.Text = "";

            if (item != null && item.IsDirectory)
            {
                var children = ReadDirectory(item.FullPath);
                AddColumn(item.Name, item.FullPath, children);
                if (!activateFocus) _activeColumnIndex = columnIndex;
                _statusText.Text = item.FullPath;
            }
            else if (item != null)
            {
                _statusText.Text = item.FullPath + (string.IsNullOrEmpty(item.MetaText) ? "" : "  •  " + item.MetaText);
            }

            UpdateBreadcrumbs();
            UpdatePreview(item);
        }

        internal void OnItemDoubleClicked(FsItem item)
        {
            if (item == null) return;
            OpenDefault(item);
        }

        private void TogglePreviewPane()
        {
            _showPreview = !_showPreview;
            _previewHost.Visibility = _showPreview ? Visibility.Visible : Visibility.Collapsed;
            _previewColDef.Width = _showPreview ? new GridLength(255) : new GridLength(0);
        }

        private void UpdatePreview(FsItem item)
        {
            _previewImageBox.Visibility = Visibility.Collapsed;
            _previewImage.Source = null;
            _previewCodeBox.Visibility = Visibility.Collapsed;
            _previewCodeText.Text = "";
            _previewIcon.Visibility = Visibility.Visible;

            if (item == null || string.IsNullOrEmpty(item.FullPath))
            {
                _previewIcon.Text = "\uE8B7";
                _previewIcon.Foreground = FolderBrush;
                _previewTitle.Text = "Finder • Yozora Sakura";
                _previewSubtitle.Text = "Пробел — Quick Look";
                _previewMeta.Text = "Выберите файл или папку слева.\n\n• Перетащите элемент мышкой для Drag & Drop\n• Нажмите Пробел для быстрого просмотра\n• ПКМ или F2 / Del для операций";
                return;
            }

            _previewTitle.Text = item.Name;
            string ext = Path.GetExtension(item.FullPath) ?? "";

            if (item.IsDirectory)
            {
                _previewIcon.Text = item.IconGlyph ?? "\uE8B7";
                _previewIcon.Foreground = item.IsPinned ? PinBrush : FolderBrush;
                _previewSubtitle.Text = item.IsDrive ? "Локальный диск" : "Папка с файлами";
                try
                {
                    var di = new DirectoryInfo(item.FullPath);
                    int subCount = 0;
                    try { subCount = di.EnumerateFileSystemInfos().Take(500).Count(); } catch { }
                    string countText = subCount >= 500 ? "500+" : subCount.ToString();
                    _previewMeta.Text = string.Format("Путь: {0}\nЭлементов: {1}\nИзменено: {2:dd.MM.yyyy HH:mm}",
                        item.FullPath, countText, di.LastWriteTime);
                }
                catch
                {
                    _previewMeta.Text = item.FullPath;
                }
                return;
            }

            try
            {
                var fi = new FileInfo(item.FullPath);
                _previewSubtitle.Text = (string.IsNullOrEmpty(ext) ? "Файл" : ext.ToUpperInvariant().TrimStart('.') + " файл") + " • " + FormatBytes(fi.Length);
                string meta = string.Format("Путь: {0}\nРазмер: {1}\nИзменён: {2:dd.MM.yyyy HH:mm}",
                    fi.FullName, FormatBytes(fi.Length), fi.LastWriteTime);

                if (ImageExts.Contains(ext))
                {
                    var bmp = LoadPreviewBitmap(fi.FullName, 360);
                    if (bmp != null)
                    {
                        _previewIcon.Visibility = Visibility.Collapsed;
                        _previewImage.Source = bmp;
                        _previewImageBox.Visibility = Visibility.Visible;
                        meta = string.Format("Разрешение: {0} × {1} px\n", bmp.PixelWidth, bmp.PixelHeight) + meta;
                    }
                    else
                    {
                        _previewIcon.Text = item.IconGlyph;
                        _previewIcon.Foreground = CyanBrush;
                    }
                }
                else if (TextExts.Contains(ext) || fi.Length < 128 * 1024 && IsLikelyTextFile(fi.FullName))
                {
                    _previewIcon.Text = item.IconGlyph;
                    _previewIcon.Foreground = CyanBrush;
                    string snippet = ReadTextHead(fi.FullName, 35, 2200);
                    if (!string.IsNullOrEmpty(snippet))
                    {
                        _previewCodeText.Text = snippet;
                        _previewCodeBox.Visibility = Visibility.Visible;
                    }
                }
                else
                {
                    _previewIcon.Text = item.IconGlyph;
                    _previewIcon.Foreground = CyanBrush;
                }

                _previewMeta.Text = meta;
            }
            catch (Exception ex)
            {
                _previewMeta.Text = ex.Message;
            }
        }

        private void OpenQuickLook()
        {
            if (_quickLookOverlay.Visibility == Visibility.Visible)
            {
                CloseQuickLook();
                return;
            }
            if (_selectedItem == null || _selectedItem.IsDirectory || !File.Exists(_selectedItem.FullPath))
                return;

            string path = _selectedItem.FullPath;
            string ext = Path.GetExtension(path) ?? "";
            _quickLookTitle.Text = _selectedItem.Name + "  —  " + path;
            _quickLookImage.Visibility = Visibility.Collapsed;
            _quickLookImage.Source = null;
            _quickLookText.Visibility = Visibility.Collapsed;
            _quickLookText.Text = "";

            if (ImageExts.Contains(ext))
            {
                var bmp = LoadPreviewBitmap(path, 1400);
                if (bmp != null)
                {
                    _quickLookImage.Source = bmp;
                    _quickLookImage.Visibility = Visibility.Visible;
                    _quickLookOverlay.Visibility = Visibility.Visible;
                    return;
                }
            }

            if (TextExts.Contains(ext) || IsLikelyTextFile(path))
            {
                _quickLookText.Text = ReadTextHead(path, 250, 24000);
                _quickLookText.Visibility = Visibility.Visible;
                _quickLookOverlay.Visibility = Visibility.Visible;
                return;
            }

            // Fallback metadata view
            var fi = new FileInfo(path);
            _quickLookText.Text = string.Format("Имя: {0}\nПуть: {1}\nРазмер: {2} ({3:N0} байт)\nСоздан: {4}\nИзменён: {5}",
                fi.Name, fi.FullName, FormatBytes(fi.Length), fi.Length, fi.CreationTime, fi.LastWriteTime);
            _quickLookText.Visibility = Visibility.Visible;
            _quickLookOverlay.Visibility = Visibility.Visible;
        }

        private void CloseQuickLook()
        {
            _quickLookOverlay.Visibility = Visibility.Collapsed;
            _quickLookImage.Source = null;
        }

        private static BitmapImage LoadPreviewBitmap(string path, int decodeWidth)
        {
            try
            {
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bi.DecodePixelWidth = decodeWidth;
                bi.UriSource = new Uri(path, UriKind.Absolute);
                bi.EndInit();
                bi.Freeze();
                return bi;
            }
            catch
            {
                return null;
            }
        }

        private static bool IsLikelyTextFile(string path)
        {
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    byte[] buf = new byte[Math.Min(512, fs.Length)];
                    int read = fs.Read(buf, 0, buf.Length);
                    for (int i = 0; i < read; i++)
                    {
                        if (buf[i] == 0) return false;
                    }
                    return read > 0;
                }
            }
            catch { return false; }
        }

        private static string ReadTextHead(string path, int maxLines, int maxChars)
        {
            try
            {
                var sb = new StringBuilder();
                using (var sr = new StreamReader(path, Encoding.UTF8, true))
                {
                    int lines = 0;
                    string line;
                    while (lines < maxLines && sb.Length < maxChars && (line = sr.ReadLine()) != null)
                    {
                        sb.AppendLine(line);
                        lines++;
                    }
                }
                return sb.ToString();
            }
            catch { return ""; }
        }

        // File Operations: New Folder / Rename / Delete to Recycle Bin
        internal void PromptNewFolder()
        {
            string baseDir = ActiveDirectoryPath();
            if (string.IsNullOrEmpty(baseDir) || !Directory.Exists(baseDir)) return;
            ShowPrompt("Новая папка в " + Path.GetFileName(baseDir.TrimEnd('\\')) + ":", "Новая папка", name =>
            {
                string clean = CleanFileName(name);
                if (string.IsNullOrEmpty(clean)) return;
                string full = Path.Combine(baseDir, clean);
                Directory.CreateDirectory(full);
                RefreshDirectoryColumns(baseDir, full);
            });
        }

        internal void PromptRename(FsItem item)
        {
            if (item == null || item.IsDrive || string.IsNullOrEmpty(item.FullPath)) return;
            string parent = Path.GetDirectoryName(item.FullPath);
            if (string.IsNullOrEmpty(parent)) return;

            ShowPrompt("Переименовать:", Path.GetFileName(item.FullPath), newName =>
            {
                string clean = CleanFileName(newName);
                if (string.IsNullOrEmpty(clean) || clean.Equals(Path.GetFileName(item.FullPath), StringComparison.OrdinalIgnoreCase)) return;
                string dest = Path.Combine(parent, clean);
                if (item.IsDirectory) Directory.Move(item.FullPath, dest);
                else File.Move(item.FullPath, dest);
                RefreshDirectoryColumns(parent, dest);
            });
        }

        internal void DeleteToRecycleBin(FsItem item)
        {
            if (item == null || item.IsDrive || string.IsNullOrEmpty(item.FullPath)) return;
            string parent = Path.GetDirectoryName(item.FullPath);
            try
            {
                if (item.IsDirectory)
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(item.FullPath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                else
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(item.FullPath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);

                _statusText.Text = "Перемещено в Корзину: " + item.Name;
                if (!string.IsNullOrEmpty(parent)) RefreshDirectoryColumns(parent, null);
            }
            catch (Exception ex)
            {
                _statusText.Text = "Ошибка удаления: " + ex.Message;
            }
        }

        private string ActiveDirectoryPath()
        {
            if (_selectedItem != null && _selectedItem.IsDirectory && Directory.Exists(_selectedItem.FullPath))
                return _selectedItem.FullPath;
            for (int i = _columns.Count - 1; i >= 0; i--)
            {
                if (!string.IsNullOrEmpty(_columns[i].DirPath) && Directory.Exists(_columns[i].DirPath))
                    return _columns[i].DirPath;
            }
            return null;
        }

        private void RefreshDirectoryColumns(string dirPath, string selectPath)
        {
            int idx = _columns.FindIndex(c => string.Equals(c.DirPath, dirPath, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
            {
                while (_columns.Count > idx + 1)
                {
                    int last = _columns.Count - 1;
                    _columnsPanel.Children.Remove(_columns[last]);
                    _columns.RemoveAt(last);
                }
                _columns[idx].Reload(ReadDirectory(dirPath), selectPath);
            }
            else if (_columns.Count > 0)
            {
                OnItemSelected(0, _selectedItem);
            }
        }

        private void ShowPrompt(string label, string initialValue, Action<string> onConfirm)
        {
            _promptLabel.Text = label;
            _promptInput.Text = initialValue ?? "";
            _promptCallback = onConfirm;
            _promptOverlay.Visibility = Visibility.Visible;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _promptInput.Focus();
                _promptInput.SelectAll();
            }), DispatcherPriority.Input);
        }

        private void SubmitPrompt(bool ok)
        {
            string val = _promptInput.Text;
            var cb = _promptCallback;
            _promptCallback = null;
            _promptOverlay.Visibility = Visibility.Collapsed;
            if (ok && cb != null)
            {
                try { cb(val); }
                catch (Exception ex) { _statusText.Text = ex.Message; }
            }
            if (_activeColumnIndex >= 0 && _activeColumnIndex < _columns.Count)
                _columns[_activeColumnIndex].FocusList();
        }

        private static string CleanFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            return name.Trim();
        }

        private List<FsItem> ReadDirectory(string path)
        {
            var result = new List<FsItem>();
            try
            {
                var dirInfo = new DirectoryInfo(path);
                foreach (var d in dirInfo.EnumerateDirectories().OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
                {
                    if ((d.Attributes & FileAttributes.System) != 0 && (d.Attributes & FileAttributes.Hidden) != 0)
                        continue;
                    result.Add(new FsItem
                    {
                        Name = d.Name,
                        FullPath = d.FullName,
                        IsDirectory = true,
                        IsChevron = true,
                        IconGlyph = "\uE8B7",
                        MetaText = "\uE76C"
                    });
                }

                foreach (var f in dirInfo.EnumerateFiles().OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
                {
                    if ((f.Attributes & FileAttributes.System) != 0 && (f.Attributes & FileAttributes.Hidden) != 0)
                        continue;
                    result.Add(new FsItem
                    {
                        Name = f.Name,
                        FullPath = f.FullName,
                        IsDirectory = false,
                        IconGlyph = FileIconForExt(f.Extension),
                        MetaText = FormatBytes(f.Length)
                    });
                }
            }
            catch (UnauthorizedAccessException)
            {
                result.Add(new FsItem { Name = "Нет доступа", FullPath = path, IsDirectory = false, IconGlyph = "\uE72E", MetaText = "" });
            }
            catch (Exception ex)
            {
                result.Add(new FsItem { Name = ex.Message, FullPath = path, IsDirectory = false, IconGlyph = "\uE783", MetaText = "" });
            }
            return result;
        }

        private void ApplyFilterToActiveColumn()
        {
            if (_activeColumnIndex >= 0 && _activeColumnIndex < _columns.Count)
                _columns[_activeColumnIndex].ApplyFilter(_filterBox.Text);
        }

        private void UpdateBreadcrumbs()
        {
            _breadcrumbPanel.Children.Clear();
            for (int i = 0; i < _columns.Count; i++)
            {
                int colIdx = i;
                var col = _columns[i];
                if (i > 0)
                {
                    _breadcrumbPanel.Children.Add(new TextBlock
                    {
                        Text = "\uE76C",
                        FontFamily = new FontFamily("Segoe Fluent Icons"),
                        FontSize = 10,
                        Foreground = TextMuted,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(4, 0, 4, 0)
                    });
                }

                bool isLast = (i == _columns.Count - 1);
                var btn = new Button
                {
                    Content = col.ColumnTitle,
                    Foreground = isLast ? AccentBrush : TextPrimary,
                    Background = isLast ? BrushFromHex("#24FF7EB6") : Brushes.Transparent,
                    FontWeight = isLast ? FontWeights.SemiBold : FontWeights.Normal,
                    FontSize = 12,
                    Padding = new Thickness(7, 3, 7, 3),
                    BorderThickness = new Thickness(0),
                    Cursor = Cursors.Hand
                };
                btn.Template = FlatButtonTemplate(6);
                btn.Click += (s, e) => JumpToColumn(colIdx);
                _breadcrumbPanel.Children.Add(btn);
            }
        }

        private void JumpToColumn(int colIndex)
        {
            while (_columns.Count > colIndex + 1)
            {
                int last = _columns.Count - 1;
                _columnsPanel.Children.Remove(_columns[last]);
                _columns.RemoveAt(last);
            }
            _activeColumnIndex = colIndex;
            UpdateBreadcrumbs();
        }

        private void OnWindowKeyDown(object sender, KeyEventArgs e)
        {
            if (_promptOverlay.Visibility == Visibility.Visible) return;

            if (_quickLookOverlay.Visibility == Visibility.Visible)
            {
                if (e.Key == Key.Escape || e.Key == Key.Space)
                {
                    CloseQuickLook();
                    e.Handled = true;
                }
                return;
            }

            if (e.Key == Key.Escape)
            {
                if (!string.IsNullOrEmpty(_filterBox.Text))
                    _filterBox.Text = "";
                else
                    HidePopup();
                e.Handled = true;
            }
            else if (e.Key == Key.Space && !_filterBox.IsKeyboardFocused)
            {
                OpenQuickLook();
                e.Handled = true;
            }
            else if (e.Key == Key.F2 && !_filterBox.IsKeyboardFocused)
            {
                PromptRename(_selectedItem);
                e.Handled = true;
            }
            else if (e.Key == Key.Delete && !_filterBox.IsKeyboardFocused)
            {
                DeleteToRecycleBin(_selectedItem);
                e.Handled = true;
            }
            else if (e.Key == Key.N && (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) == (ModifierKeys.Control | ModifierKeys.Shift))
            {
                PromptNewFolder();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter && _selectedItem != null)
            {
                OpenDefault(_selectedItem);
                e.Handled = true;
            }
            else if (e.Key == Key.Right && !_filterBox.IsKeyboardFocused)
            {
                if (_activeColumnIndex + 1 < _columns.Count)
                {
                    _activeColumnIndex++;
                    _columns[_activeColumnIndex].SelectFirstIfNone();
                    _columns[_activeColumnIndex].FocusList();
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.Left && !_filterBox.IsKeyboardFocused)
            {
                if (_activeColumnIndex > 0)
                {
                    _activeColumnIndex--;
                    _columns[_activeColumnIndex].FocusList();
                    e.Handled = true;
                }
            }
        }

        private string TargetPath()
        {
            if (_selectedItem != null && !string.IsNullOrEmpty(_selectedItem.FullPath))
                return _selectedItem.FullPath;
            for (int i = _columns.Count - 1; i >= 0; i--)
                if (!string.IsNullOrEmpty(_columns[i].DirPath)) return _columns[i].DirPath;
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        private string TargetFolder()
        {
            string p = TargetPath();
            if (Directory.Exists(p)) return p;
            return Path.GetDirectoryName(p) ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        internal void OpenDefault(FsItem item)
        {
            try
            {
                if (item.IsDirectory)
                    Process.Start(new ProcessStartInfo("explorer.exe", "\"" + item.FullPath + "\"") { UseShellExecute = true });
                else
                    Process.Start(new ProcessStartInfo(item.FullPath) { UseShellExecute = true });
                HidePopup();
            }
            catch (Exception ex) { _statusText.Text = ex.Message; }
        }

        internal void OpenInExplorer(string path)
        {
            try
            {
                string arg = File.Exists(path) ? "/select,\"" + path + "\"" : "\"" + path + "\"";
                Process.Start(new ProcessStartInfo("explorer.exe", arg) { UseShellExecute = true });
                HidePopup();
            }
            catch { }
        }

        internal void OpenInVsCode(string path)
        {
            try
            {
                string codeExe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Microsoft VS Code\Code.exe");
                if (!File.Exists(codeExe)) codeExe = "code";
                Process.Start(new ProcessStartInfo(codeExe, "-n \"" + path + "\"") { UseShellExecute = true });
                HidePopup();
            }
            catch { }
        }

        internal void OpenTerminalHere(string dir)
        {
            try
            {
                Process.Start(new ProcessStartInfo("wt.exe", "-w new -d \"" + dir + "\" -p \"Windows PowerShell\"") { UseShellExecute = true });
                HidePopup();
            }
            catch { }
        }

        internal void OpenCheatsheet()
        {
            try
            {
                string exe = Path.Combine(
                    Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? @"C:\Users\Laziko\.config\yasb",
                    "hotkey_cheatsheet.exe");
                if (File.Exists(exe))
                {
                    Process.Start(new ProcessStartInfo(exe, "--toggle") { UseShellExecute = true });
                    HidePopup();
                }
            }
            catch { }
        }

        internal void CopyPath(string path)
        {
            try
            {
                Clipboard.SetText(path);
                _statusText.Text = "Скопировано: " + path;
            }
            catch { }
        }

        private Button CreateHeaderButton(string glyph, string tooltip, Action onClick, bool isClose = false)
        {
            var btn = new Button
            {
                Content = glyph,
                ToolTip = tooltip,
                FontFamily = new FontFamily("Segoe Fluent Icons"),
                FontSize = 13,
                Width = 28,
                Height = 28,
                Margin = new Thickness(2, 0, 0, 0),
                Background = Brushes.Transparent,
                Foreground = isClose ? DangerText : TextPrimary,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand
            };
            btn.Template = FlatButtonTemplate(6, isClose ? DangerHover : ItemHover);
            btn.Click += (s, e) => onClick();
            return btn;
        }

        private static ControlTemplate FlatButtonTemplate(double radius, Brush hoverBrush = null)
        {
            if (hoverBrush == null) hoverBrush = ItemHover;
            var template = new ControlTemplate(typeof(Button));
            var borderFactory = new FrameworkElementFactory(typeof(Border));
            borderFactory.Name = "bd";
            borderFactory.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
            borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(radius));
            borderFactory.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Button.PaddingProperty));

            var contentPresenter = new FrameworkElementFactory(typeof(ContentPresenter));
            contentPresenter.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
            contentPresenter.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
            borderFactory.AppendChild(contentPresenter);

            template.VisualTree = borderFactory;

            var trigger = new Trigger { Property = Button.IsMouseOverProperty, Value = true };
            trigger.Setters.Add(new Setter(Border.BackgroundProperty, hoverBrush, "bd"));
            template.Triggers.Add(trigger);
            return template;
        }

        private static string FileIconForExt(string ext)
        {
            switch ((ext ?? "").ToLowerInvariant())
            {
                case ".png":
                case ".jpg":
                case ".jpeg":
                case ".gif":
                case ".webp":
                case ".bmp":
                    return "\uE91B";
                case ".mp3":
                case ".wav":
                case ".flac":
                    return "\uE8D6";
                case ".mp4":
                case ".mkv":
                case ".mov":
                    return "\uE8B2";
                case ".zip":
                case ".rar":
                case ".7z":
                    return "\uF012";
                case ".cs":
                case ".py":
                case ".js":
                case ".ts":
                case ".json":
                case ".yaml":
                case ".yml":
                case ".css":
                case ".html":
                    return "\uE943";
                default:
                    return "\uE8A5";
            }
        }

        private static string FormatBytes(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double val = bytes;
            int idx = 0;
            while (val >= 1024 && idx < units.Length - 1)
            {
                val /= 1024.0;
                idx++;
            }
            return idx == 0 ? string.Format("{0} {1}", (long)val, units[idx]) : string.Format("{0:0.#} {1}", val, units[idx]);
        }

        private static SolidColorBrush BrushFromHex(string hex)
        {
            var b = (SolidColorBrush)new BrushConverter().ConvertFromString(hex);
            b.Freeze();
            return b;
        }

        private class ColumnView : Border
        {
            private readonly FolderBrowserWindow _owner;
            private readonly int _columnIndex;
            private List<FsItem> _allItems;
            private readonly ListBox _listBox;
            private bool _suppressSelectionChanged = false;
            private Point _dragStart;
            private FsItem _dragItem;

            public string ColumnTitle { get; private set; }
            public string DirPath { get; private set; }

            public ColumnView(FolderBrowserWindow owner, int index, string title, string dirPath, List<FsItem> items)
            {
                _owner = owner;
                _columnIndex = index;
                ColumnTitle = title;
                DirPath = dirPath;
                _allItems = items;

                Width = 225;
                Margin = new Thickness(2);
                BorderBrush = BrushFromHex("#1CD8B4FE");
                BorderThickness = new Thickness(0, 0, 1, 0);
                Padding = new Thickness(2, 2, 4, 2);

                _listBox = new ListBox
                {
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Foreground = TextPrimary,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch
                };
                VirtualizingPanel.SetIsVirtualizing(_listBox, true);
                ScrollViewer.SetHorizontalScrollBarVisibility(_listBox, ScrollBarVisibility.Disabled);

                _listBox.ItemContainerStyle = BuildItemStyle();
                _listBox.ItemTemplate = BuildItemTemplate();
                _listBox.ItemsSource = _allItems;

                _listBox.SelectionChanged += (s, e) =>
                {
                    if (_suppressSelectionChanged) return;
                    var item = _listBox.SelectedItem as FsItem;
                    if (item != null) _owner.OnItemSelected(_columnIndex, item);
                };
                _listBox.MouseDoubleClick += (s, e) =>
                {
                    var item = _listBox.SelectedItem as FsItem;
                    if (item != null) _owner.OnItemDoubleClicked(item);
                };
                _listBox.PreviewMouseLeftButtonDown += (s, e) =>
                {
                    _dragStart = e.GetPosition(null);
                    _dragItem = FindFsItem(e.OriginalSource as DependencyObject);
                };
                _listBox.PreviewMouseMove += (s, e) =>
                {
                    if (e.LeftButton != MouseButtonState.Pressed || _dragItem == null || _dragItem.IsDrive)
                        return;
                    Point pos = e.GetPosition(null);
                    if (Math.Abs(pos.X - _dragStart.X) > SystemParameters.MinimumHorizontalDragDistance ||
                        Math.Abs(pos.Y - _dragStart.Y) > SystemParameters.MinimumVerticalDragDistance)
                    {
                        var path = _dragItem.FullPath;
                        _dragItem = null;
                        if (File.Exists(path) || Directory.Exists(path))
                        {
                            _owner._suppressDeactivate = true;
                            try
                            {
                                var data = new DataObject(DataFormats.FileDrop, new[] { path });
                                DragDrop.DoDragDrop(_listBox, data, DragDropEffects.Copy | DragDropEffects.Move);
                            }
                            finally
                            {
                                _owner.Dispatcher.BeginInvoke(new Action(() => _owner._suppressDeactivate = false), DispatcherPriority.Background);
                            }
                        }
                    }
                };
                _listBox.PreviewMouseRightButtonDown += (s, e) =>
                {
                    var lbi = FindListBoxItem(e.OriginalSource as DependencyObject);
                    if (lbi != null)
                    {
                        lbi.IsSelected = true;
                        var item = lbi.DataContext as FsItem;
                        if (item != null) ShowItemContextMenu(lbi, item);
                        e.Handled = true;
                    }
                };

                Child = _listBox;
            }

            public void Reload(List<FsItem> items, string selectFullPath)
            {
                _suppressSelectionChanged = true;
                _allItems = items;
                _listBox.ItemsSource = _allItems;
                if (!string.IsNullOrEmpty(selectFullPath))
                {
                    var found = _allItems.FirstOrDefault(x => x.FullPath.Equals(selectFullPath, StringComparison.OrdinalIgnoreCase));
                    if (found != null) _listBox.SelectedItem = found;
                }
                _suppressSelectionChanged = false;
            }

            private static ListBoxItem FindListBoxItem(DependencyObject dep)
            {
                while (dep != null && !(dep is ListBoxItem))
                    dep = VisualTreeHelper.GetParent(dep);
                return dep as ListBoxItem;
            }

            private static FsItem FindFsItem(DependencyObject dep)
            {
                var lbi = FindListBoxItem(dep);
                return lbi != null ? lbi.DataContext as FsItem : null;
            }

            public void ApplyFilter(string query)
            {
                _suppressSelectionChanged = true;
                if (string.IsNullOrWhiteSpace(query))
                {
                    _listBox.ItemsSource = _allItems;
                }
                else
                {
                    _listBox.ItemsSource = _allItems
                        .Select(x => new { Item = x, Match = YasbSearch.SmartSearchMatcher.Match(x.Name, query) })
                        .Where(x => x.Match.Score > 0)
                        .OrderByDescending(x => x.Match.Score)
                        .ThenBy(x => x.Item.Name, StringComparer.CurrentCultureIgnoreCase)
                        .Select(x => x.Item)
                        .ToList();
                }
                _suppressSelectionChanged = false;
            }

            public void FocusList()
            {
                _listBox.Focus();
                SelectFirstIfNone();
            }

            public void SelectFirstIfNone()
            {
                if (_listBox.SelectedIndex < 0 && _listBox.Items.Count > 0)
                    _listBox.SelectedIndex = 0;
            }

            private void ShowItemContextMenu(FrameworkElement target, FsItem item)
            {
                var menu = new ContextMenu { Style = DarkContextMenuStyle };
                menu.Resources[typeof(MenuItem)] = DarkMenuItemStyle;
                menu.Opened += (s, e) => _owner._suppressDeactivate = true;
                menu.Closed += (s, e) => _owner.Dispatcher.BeginInvoke(new Action(() => _owner._suppressDeactivate = false), DispatcherPriority.Background);

                AddMenuItem(menu, item.IsDirectory ? "Открыть в Проводнике" : "Открыть файл", () => _owner.OpenDefault(item));
                if (!item.IsDirectory)
                {
                    AddMenuItem(menu, "Quick Look (Space)", () => _owner.OpenQuickLook());
                    AddMenuItem(menu, "Показать в Проводнике", () => _owner.OpenInExplorer(item.FullPath));
                }
                AddMenuItem(menu, "Открыть в VS Code", () => _owner.OpenInVsCode(item.FullPath));
                string folder = item.IsDirectory ? item.FullPath : Path.GetDirectoryName(item.FullPath);
                if (!string.IsNullOrEmpty(folder))
                    AddMenuItem(menu, "Открыть Terminal здесь", () => _owner.OpenTerminalHere(folder));
                AddMenuItem(menu, "Копировать путь", () => _owner.CopyPath(item.FullPath));

                if (item.IsDirectory && !item.IsDrive)
                {
                    bool pinned = _owner.IsPathPinned(item.FullPath);
                    AddMenuItem(menu, pinned ? "Убрать из закреплённых" : "Закрепить в начале", () => _owner.TogglePinPath(item.FullPath));
                }

                if (!item.IsDrive)
                {
                    AddMenuItem(menu, "Переименовать (F2)", () => _owner.PromptRename(item));
                    AddMenuItem(menu, "Удалить в Корзину (Del)", () => _owner.DeleteToRecycleBin(item));
                }

                menu.PlacementTarget = target;
                menu.IsOpen = true;
            }

            private static void AddMenuItem(ContextMenu menu, string header, Action onClick)
            {
                var mi = new MenuItem { Header = header, Style = DarkMenuItemStyle };
                mi.Click += (s, e) => onClick();
                menu.Items.Add(mi);
            }

            private static Style BuildItemStyle()
            {
                var style = new Style(typeof(ListBoxItem));
                style.Setters.Add(new Setter(PaddingProperty, new Thickness(6, 5, 6, 5)));
                style.Setters.Add(new Setter(MarginProperty, new Thickness(0, 1, 0, 1)));
                style.Setters.Add(new Setter(BackgroundProperty, Brushes.Transparent));
                style.Setters.Add(new Setter(FocusVisualStyleProperty, null));

                var template = new ControlTemplate(typeof(ListBoxItem));
                var bd = new FrameworkElementFactory(typeof(Border));
                bd.Name = "bd";
                bd.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(BackgroundProperty));
                bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
                bd.SetValue(Border.PaddingProperty, new TemplateBindingExtension(PaddingProperty));
                var cp = new FrameworkElementFactory(typeof(ContentPresenter));
                bd.AppendChild(cp);
                template.VisualTree = bd;

                var hover = new Trigger { Property = IsMouseOverProperty, Value = true };
                hover.Setters.Add(new Setter(Border.BackgroundProperty, ItemHover, "bd"));
                template.Triggers.Add(hover);

                var sel = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
                sel.Setters.Add(new Setter(Border.BackgroundProperty, ItemSelected, "bd"));
                template.Triggers.Add(sel);

                style.Setters.Add(new Setter(TemplateProperty, template));
                return style;
            }

            private static DataTemplate BuildItemTemplate()
            {
                var dt = new DataTemplate(typeof(FsItem));
                var grid = new FrameworkElementFactory(typeof(Grid));

                var c1 = new FrameworkElementFactory(typeof(ColumnDefinition));
                c1.SetValue(ColumnDefinition.WidthProperty, GridLength.Auto);
                var c2 = new FrameworkElementFactory(typeof(ColumnDefinition));
                c2.SetValue(ColumnDefinition.WidthProperty, new GridLength(1, GridUnitType.Star));
                var c3 = new FrameworkElementFactory(typeof(ColumnDefinition));
                c3.SetValue(ColumnDefinition.WidthProperty, GridLength.Auto);
                grid.AppendChild(c1);
                grid.AppendChild(c2);
                grid.AppendChild(c3);

                var icon = new FrameworkElementFactory(typeof(TextBlock));
                icon.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("IconGlyph"));
                icon.SetValue(TextBlock.FontFamilyProperty, new FontFamily("Segoe Fluent Icons"));
                icon.SetValue(TextBlock.FontSizeProperty, 13.0);
                icon.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
                icon.SetValue(TextBlock.MarginProperty, new Thickness(0, 0, 8, 0));
                var styleIcon = new Style(typeof(TextBlock));
                styleIcon.Setters.Add(new Setter(TextBlock.ForegroundProperty, FileBrush));
                var dirTrigger = new DataTrigger { Binding = new System.Windows.Data.Binding("IsDirectory"), Value = true };
                dirTrigger.Setters.Add(new Setter(TextBlock.ForegroundProperty, FolderBrush));
                styleIcon.Triggers.Add(dirTrigger);
                var pinTrigger = new DataTrigger { Binding = new System.Windows.Data.Binding("IsPinned"), Value = true };
                pinTrigger.Setters.Add(new Setter(TextBlock.ForegroundProperty, PinBrush));
                styleIcon.Triggers.Add(pinTrigger);
                icon.SetValue(StyleProperty, styleIcon);
                icon.SetValue(Grid.ColumnProperty, 0);
                grid.AppendChild(icon);

                var name = new FrameworkElementFactory(typeof(TextBlock));
                name.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Name"));
                name.SetValue(TextBlock.FontSizeProperty, 12.0);
                name.SetValue(TextBlock.ForegroundProperty, TextPrimary);
                name.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
                name.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
                name.SetValue(Grid.ColumnProperty, 1);
                grid.AppendChild(name);

                var meta = new FrameworkElementFactory(typeof(TextBlock));
                meta.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("MetaText"));
                meta.SetValue(TextBlock.FontSizeProperty, 10.5);
                meta.SetValue(TextBlock.ForegroundProperty, TextMuted);
                meta.SetValue(TextBlock.MarginProperty, new Thickness(6, 0, 0, 0));
                meta.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
                var metaStyle = new Style(typeof(TextBlock));
                var dirMeta = new DataTrigger { Binding = new System.Windows.Data.Binding("IsChevron"), Value = true };
                dirMeta.Setters.Add(new Setter(TextBlock.FontFamilyProperty, new FontFamily("Segoe Fluent Icons")));
                dirMeta.Setters.Add(new Setter(TextBlock.FontSizeProperty, 9.5));
                metaStyle.Triggers.Add(dirMeta);
                meta.SetValue(StyleProperty, metaStyle);
                meta.SetValue(Grid.ColumnProperty, 2);
                grid.AppendChild(meta);

                dt.VisualTree = grid;
                return dt;
            }
        }
    }
}
