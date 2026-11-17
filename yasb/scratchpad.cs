using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace YozoraScratchpad
{
    internal static class NativeMethods
    {
        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_TOOLWINDOW = 0x00000080;

        [DllImport("user32.dll")]
        public static extern int GetWindowLong(IntPtr hwnd, int index);

        [DllImport("user32.dll")]
        public static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);
    }

    public class ScratchpadWindow : Window
    {
        private static readonly string ConfigDir = @"C:\Users\Laziko\.config\yasb";
        private static readonly string NotesPath = Path.Combine(ConfigDir, "scratchpad_notes.txt");

        private static readonly Color BgWindow = Color.FromArgb(0xF6, 0x13, 0x11, 0x22);
        private static readonly Color BgCard = Color.FromArgb(0xFF, 0x1A, 0x17, 0x2E);
        private static readonly Color BgInput = Color.FromArgb(0xFF, 0x10, 0x0E, 0x1C);
        private static readonly Color BorderSakura = Color.FromArgb(0x55, 0xFF, 0x7E, 0xB6);
        private static readonly Color BorderSubtle = Color.FromArgb(0x28, 0xB4, 0xBE, 0xFE);
        private static readonly Color AccentSakura = Color.FromArgb(0xFF, 0xFF, 0x7E, 0xB6);
        private static readonly Color AccentCyan = Color.FromArgb(0xFF, 0x6C, 0xE5, 0xFF);
        private static readonly Color AccentGold = Color.FromArgb(0xFF, 0xFF, 0xD4, 0x79);
        private static readonly Color TextPrimary = Color.FromArgb(0xFF, 0xF2, 0xEE, 0xFF);
        private static readonly Color TextMuted = Color.FromArgb(0xFF, 0x8E, 0x88, 0xAA);
        private static readonly Color ErrorRed = Color.FromArgb(0xFF, 0xFF, 0x6B, 0x8B);

        private Border _rootBorder;
        private TranslateTransform _slideTransform;
        private Button _tabNotesBtn;
        private Button _tabTermBtn;
        private Grid _notesPanel;
        private Grid _termPanel;
        private TextBlock _statusText;

        // Notes + Calculator
        private TextBox _notesBox;
        private TextBox _calcInput;
        private TextBlock _calcResult;
        private string _lastCalcValue = "";
        private DispatcherTimer _saveTimer;
        private bool _notesDirty = false;

        // Persistent PowerShell
        private TextBox _termOutput;
        private TextBox _termInput;
        private Process _psProcess;
        private readonly object _psLock = new object();
        private readonly List<string> _cmdHistory = new List<string>();
        private int _historyIdx = -1;
        private int _activeTab = 0;
        private bool _isVisible = false;

        public ScratchpadWindow()
        {
            Title = "Yozora Scratchpad";
            Width = 800;
            Height = 470;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;

            PositionDrawer();
            BuildUi();
            LoadNotes();

            _saveTimer = new DispatcherTimer();
            _saveTimer.Interval = TimeSpan.FromMilliseconds(450);
            _saveTimer.Tick += delegate
            {
                _saveTimer.Stop();
                SaveNotes();
            };

            SourceInitialized += OnSourceInitialized;
            PreviewKeyDown += OnWindowKeyDown;
            Deactivated += OnWindowDeactivated;
            Closed += OnWindowClosed;
        }

        private void PositionDrawer()
        {
            Rect wa = SystemParameters.WorkArea;
            Left = wa.Left + (wa.Width - Width) / 2.0;
            Top = wa.Top + 6;
        }

        private void OnSourceInitialized(object sender, EventArgs e)
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            int ex = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
            NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, ex | NativeMethods.WS_EX_TOOLWINDOW);
        }

        private void BuildUi()
        {
            _slideTransform = new TranslateTransform(0, -18);

            _rootBorder = new Border();
            _rootBorder.Background = new SolidColorBrush(BgWindow);
            _rootBorder.BorderBrush = new SolidColorBrush(BorderSakura);
            _rootBorder.BorderThickness = new Thickness(1);
            _rootBorder.CornerRadius = new CornerRadius(14);
            _rootBorder.Margin = new Thickness(8);
            _rootBorder.Padding = new Thickness(16, 12, 16, 12);
            _rootBorder.RenderTransform = _slideTransform;
            _rootBorder.Effect = new DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 22,
                ShadowDepth = 6,
                Opacity = 0.68
            };

            Grid mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Header bar
            Grid header = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel titleStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            Border dot = new Border
            {
                Width = 8,
                Height = 8,
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(AccentSakura),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            TextBlock titleText = new TextBlock
            {
                Text = "SCRATCHPAD",
                FontFamily = new FontFamily("Segoe UI Semibold"),
                FontSize = 12,
                Foreground = new SolidColorBrush(TextPrimary),
                VerticalAlignment = VerticalAlignment.Center
            };
            TextBlock keyHint = new TextBlock
            {
                Text = "Alt + `",
                FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                FontSize = 10.5,
                Foreground = new SolidColorBrush(AccentCyan),
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            titleStack.Children.Add(dot);
            titleStack.Children.Add(titleText);
            titleStack.Children.Add(keyHint);
            Grid.SetColumn(titleStack, 0);
            header.Children.Add(titleStack);

            // Tab switcher
            StackPanel tabs = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            _tabNotesBtn = CreatePillButton("Заметки + Калькулятор", true, delegate { SwitchTab(0); });
            _tabNotesBtn.Margin = new Thickness(0, 0, 6, 0);
            _tabTermBtn = CreatePillButton("Терминал (PS)", false, delegate { SwitchTab(1); });
            tabs.Children.Add(_tabNotesBtn);
            tabs.Children.Add(_tabTermBtn);
            Grid.SetColumn(tabs, 1);
            header.Children.Add(tabs);

            Button closeBtn = CreateActionButton("Скрыть (Esc)", delegate { HideDrawer(); });
            Grid.SetColumn(closeBtn, 2);
            header.Children.Add(closeBtn);

            Grid.SetRow(header, 0);
            mainGrid.Children.Add(header);

            // Content host
            Grid contentHost = new Grid();
            _notesPanel = BuildNotesTab();
            _termPanel = BuildTermTab();
            _termPanel.Visibility = Visibility.Collapsed;
            contentHost.Children.Add(_notesPanel);
            contentHost.Children.Add(_termPanel);
            Grid.SetRow(contentHost, 1);
            mainGrid.Children.Add(contentHost);

            // Footer
            Grid footer = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _statusText = new TextBlock
            {
                Text = "Ctrl+Tab: вкладка  •  Esc: свернуть",
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 11,
                Foreground = new SolidColorBrush(TextMuted),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(_statusText, 0);
            footer.Children.Add(_statusText);

            TextBlock pathText = new TextBlock
            {
                Text = "Yozora Sakura",
                FontFamily = new FontFamily("Segoe UI Semibold"),
                FontSize = 11,
                Foreground = new SolidColorBrush(AccentSakura),
                Opacity = 0.78,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(pathText, 1);
            footer.Children.Add(pathText);

            Grid.SetRow(footer, 2);
            mainGrid.Children.Add(footer);

            _rootBorder.Child = mainGrid;
            Content = _rootBorder;
        }

        private Grid BuildNotesTab()
        {
            Grid g = new Grid();
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // Calculator bar
            Border calcBar = new Border
            {
                Background = new SolidColorBrush(BgCard),
                BorderBrush = new SolidColorBrush(BorderSubtle),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(0, 0, 0, 8)
            };
            Grid cg = new Grid();
            cg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            cg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            cg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            cg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            TextBlock calcIcon = new TextBlock
            {
                Text = "=",
                FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(AccentGold),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(calcIcon, 0);
            cg.Children.Add(calcIcon);

            _calcInput = new TextBox
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = new SolidColorBrush(TextPrimary),
                CaretBrush = new SolidColorBrush(AccentSakura),
                FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                FontSize = 12.5,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "Быстрый калькулятор: (1920 - 16) / 2, 2400 * 1.15"
            };
            _calcInput.TextChanged += delegate { EvaluateCalc(); };
            _calcInput.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.Key == Key.Enter && !string.IsNullOrEmpty(_lastCalcValue))
                {
                    InsertCalcIntoNotes();
                    e.Handled = true;
                }
            };
            Grid.SetColumn(_calcInput, 1);
            cg.Children.Add(_calcInput);

            _calcResult = new TextBlock
            {
                Text = "введите выражение",
                FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                FontSize = 12.5,
                Foreground = new SolidColorBrush(TextMuted),
                Margin = new Thickness(10, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(_calcResult, 2);
            cg.Children.Add(_calcResult);

            Button copyCalc = CreateActionButton("В заметку", delegate { InsertCalcIntoNotes(); });
            Grid.SetColumn(copyCalc, 3);
            cg.Children.Add(copyCalc);

            calcBar.Child = cg;
            Grid.SetRow(calcBar, 0);
            g.Children.Add(calcBar);

            // Notes editor
            Border editorBox = new Border
            {
                Background = new SolidColorBrush(BgInput),
                BorderBrush = new SolidColorBrush(BorderSubtle),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(8)
            };

            Grid eg = new Grid();
            eg.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            eg.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            _notesBox = new TextBox
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = new SolidColorBrush(TextPrimary),
                CaretBrush = new SolidColorBrush(AccentSakura),
                SelectionBrush = new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0x7E, 0xB6)),
                FontFamily = new FontFamily("Cascadia Mono, Consolas, Segoe UI"),
                FontSize = 13,
                AcceptsReturn = true,
                AcceptsTab = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            _notesBox.TextChanged += delegate
            {
                _notesDirty = true;
                _saveTimer.Stop();
                _saveTimer.Start();
                UpdateNotesStatus();
            };
            Grid.SetRow(_notesBox, 0);
            eg.Children.Add(_notesBox);

            StackPanel noteBtns = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 6, 0, 0)
            };
            Button copyBtn = CreateActionButton("Копировать всё", delegate
            {
                try
                {
                    if (!string.IsNullOrEmpty(_notesBox.Text))
                        Clipboard.SetText(_notesBox.Text);
                    _statusText.Text = "Заметки скопированы в буфер";
                }
                catch { }
            });
            copyBtn.Margin = new Thickness(0, 0, 6, 0);
            Button clearBtn = CreateActionButton("Очистить", delegate
            {
                _notesBox.Clear();
                SaveNotes();
            });
            noteBtns.Children.Add(copyBtn);
            noteBtns.Children.Add(clearBtn);
            Grid.SetRow(noteBtns, 1);
            eg.Children.Add(noteBtns);

            editorBox.Child = eg;
            Grid.SetRow(editorBox, 1);
            g.Children.Add(editorBox);

            return g;
        }

        private Grid BuildTermTab()
        {
            Grid g = new Grid();
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            Border outBorder = new Border
            {
                Background = new SolidColorBrush(BgInput),
                BorderBrush = new SolidColorBrush(BorderSubtle),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(8),
                Margin = new Thickness(0, 0, 0, 8)
            };

            _termOutput = new TextBox
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = new SolidColorBrush(TextPrimary),
                SelectionBrush = new SolidColorBrush(Color.FromArgb(0x66, 0x6C, 0xE5, 0xFF)),
                FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                FontSize = 12.2,
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            outBorder.Child = _termOutput;
            Grid.SetRow(outBorder, 0);
            g.Children.Add(outBorder);

            Border inBorder = new Border
            {
                Background = new SolidColorBrush(BgCard),
                BorderBrush = new SolidColorBrush(BorderSubtle),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(10, 6, 8, 6)
            };
            Grid ig = new Grid();
            ig.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            ig.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            ig.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            ig.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            TextBlock prompt = new TextBlock
            {
                Text = "PS>",
                FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                FontSize = 12.5,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(AccentCyan),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(prompt, 0);
            ig.Children.Add(prompt);

            _termInput = new TextBox
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = new SolidColorBrush(TextPrimary),
                CaretBrush = new SolidColorBrush(AccentCyan),
                FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                FontSize = 12.5,
                VerticalAlignment = VerticalAlignment.Center
            };
            _termInput.PreviewKeyDown += OnTermInputKeyDown;
            Grid.SetColumn(_termInput, 1);
            ig.Children.Add(_termInput);

            Button clearTerm = CreateActionButton("Ctrl+L", delegate
            {
                _termOutput.Clear();
            });
            clearTerm.Margin = new Thickness(6, 0, 6, 0);
            Grid.SetColumn(clearTerm, 2);
            ig.Children.Add(clearTerm);

            Button openWt = CreateActionButton("Окно WT", delegate
            {
                try
                {
                    Process.Start(new ProcessStartInfo("wt.exe", "-w new -p \"Windows PowerShell\"") { UseShellExecute = true });
                    HideDrawer();
                }
                catch { }
            });
            Grid.SetColumn(openWt, 3);
            ig.Children.Add(openWt);

            inBorder.Child = ig;
            Grid.SetRow(inBorder, 1);
            g.Children.Add(inBorder);

            return g;
        }

        private Button CreatePillButton(string text, bool active, RoutedEventHandler onClick)
        {
            Button btn = new Button
            {
                Content = text,
                FontFamily = new FontFamily("Segoe UI Semibold"),
                FontSize = 11.5,
                Padding = new Thickness(12, 5, 12, 5),
                Cursor = Cursors.Hand,
                BorderThickness = new Thickness(1)
            };
            StylePillButton(btn, active);
            btn.Click += onClick;
            return btn;
        }

        private void StylePillButton(Button btn, bool active)
        {
            if (active)
            {
                btn.Background = new SolidColorBrush(Color.FromArgb(0x36, 0xFF, 0x7E, 0xB6));
                btn.BorderBrush = new SolidColorBrush(AccentSakura);
                btn.Foreground = new SolidColorBrush(TextPrimary);
            }
            else
            {
                btn.Background = new SolidColorBrush(Color.FromArgb(0x16, 0xFF, 0xFF, 0xFF));
                btn.BorderBrush = new SolidColorBrush(BorderSubtle);
                btn.Foreground = new SolidColorBrush(TextMuted);
            }
        }

        private Button CreateActionButton(string text, RoutedEventHandler onClick)
        {
            Button btn = new Button
            {
                Content = text,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 11,
                Padding = new Thickness(9, 4, 9, 4),
                Background = new SolidColorBrush(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF)),
                BorderBrush = new SolidColorBrush(BorderSubtle),
                BorderThickness = new Thickness(1),
                Foreground = new SolidColorBrush(TextPrimary),
                Cursor = Cursors.Hand
            };
            btn.Click += onClick;
            return btn;
        }

        private void SwitchTab(int idx)
        {
            _activeTab = idx;
            if (idx == 0)
            {
                _notesPanel.Visibility = Visibility.Visible;
                _termPanel.Visibility = Visibility.Collapsed;
                StylePillButton(_tabNotesBtn, true);
                StylePillButton(_tabTermBtn, false);
                UpdateNotesStatus();
                Dispatcher.BeginInvoke(DispatcherPriority.Input, (Action)delegate { _notesBox.Focus(); });
            }
            else
            {
                _notesPanel.Visibility = Visibility.Collapsed;
                _termPanel.Visibility = Visibility.Visible;
                StylePillButton(_tabNotesBtn, false);
                StylePillButton(_tabTermBtn, true);
                EnsurePowerShellStarted();
                _statusText.Text = "Ctrl+L: очистить  •  ↑/↓: история  •  Ctrl+Tab: заметки";
                Dispatcher.BeginInvoke(DispatcherPriority.Input, (Action)delegate { _termInput.Focus(); });
            }
        }

        private void EvaluateCalc()
        {
            string expr = (_calcInput.Text ?? "").Trim();
            if (expr.Length == 0)
            {
                _lastCalcValue = "";
                _calcResult.Text = "введите выражение";
                _calcResult.Foreground = new SolidColorBrush(TextMuted);
                return;
            }

            try
            {
                string sanitized = expr.Replace(',', '.');
                if (!Regex.IsMatch(sanitized, @"^[0-9\+\-\*\/\%\(\)\.\s]+$"))
                {
                    _lastCalcValue = "";
                    _calcResult.Text = "...";
                    _calcResult.Foreground = new SolidColorBrush(TextMuted);
                    return;
                }
                DataTable dt = new DataTable();
                object val = dt.Compute(sanitized, "");
                double num = Convert.ToDouble(val, CultureInfo.InvariantCulture);
                string formatted = Math.Abs(num % 1) < 1e-9
                    ? ((long)Math.Round(num)).ToString(CultureInfo.InvariantCulture)
                    : num.ToString("0.######", CultureInfo.InvariantCulture);

                _lastCalcValue = formatted;
                _calcResult.Text = formatted;
                _calcResult.Foreground = new SolidColorBrush(AccentGold);
            }
            catch
            {
                _lastCalcValue = "";
                _calcResult.Text = "...";
                _calcResult.Foreground = new SolidColorBrush(TextMuted);
            }
        }

        private void InsertCalcIntoNotes()
        {
            string expr = (_calcInput.Text ?? "").Trim();
            if (string.IsNullOrEmpty(expr) || string.IsNullOrEmpty(_lastCalcValue))
                return;
            string line = expr + " = " + _lastCalcValue;
            if (!string.IsNullOrEmpty(_notesBox.Text) && !_notesBox.Text.EndsWith("\n"))
                _notesBox.AppendText(Environment.NewLine);
            _notesBox.AppendText(line + Environment.NewLine);
            try { Clipboard.SetText(_lastCalcValue); } catch { }
            _statusText.Text = "Добавлено и скопировано: " + _lastCalcValue;
            SaveNotes();
        }

        private void LoadNotes()
        {
            try
            {
                if (File.Exists(NotesPath))
                    _notesBox.Text = File.ReadAllText(NotesPath, Encoding.UTF8);
            }
            catch { }
            UpdateNotesStatus();
        }

        private void SaveNotes()
        {
            if (!_notesDirty) return;
            try
            {
                Directory.CreateDirectory(ConfigDir);
                File.WriteAllText(NotesPath, _notesBox.Text ?? "", Encoding.UTF8);
                _notesDirty = false;
                UpdateNotesStatus();
            }
            catch { }
        }

        private void UpdateNotesStatus()
        {
            if (_activeTab != 0) return;
            string t = _notesBox.Text ?? "";
            int lines = t.Length == 0 ? 0 : t.Split('\n').Length;
            _statusText.Text = string.Format("Символов: {0}  •  Строк: {1}  •  Автосохранение", t.Length, lines);
        }

        private void EnsurePowerShellStarted()
        {
            lock (_psLock)
            {
                if (_psProcess != null && !_psProcess.HasExited)
                    return;

                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo();
                    psi.FileName = "powershell.exe";
                    psi.Arguments = "-NoLogo -NoExit -Command \"[Console]::InputEncoding=[Text.Encoding]::UTF8; [Console]::OutputEncoding=[Text.Encoding]::UTF8; $OutputEncoding=[Text.Encoding]::UTF8\"";
                    psi.WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    psi.RedirectStandardInput = true;
                    psi.RedirectStandardOutput = true;
                    psi.RedirectStandardError = true;
                    psi.StandardOutputEncoding = Encoding.UTF8;
                    psi.StandardErrorEncoding = Encoding.UTF8;
                    psi.UseShellExecute = false;
                    psi.CreateNoWindow = true;

                    _psProcess = new Process { StartInfo = psi, EnableRaisingEvents = true };
                    _psProcess.OutputDataReceived += delegate(object s, DataReceivedEventArgs e)
                    {
                        if (e.Data != null) AppendTermLine(e.Data, false);
                    };
                    _psProcess.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e)
                    {
                        if (e.Data != null) AppendTermLine("[ERR] " + e.Data, true);
                    };

                    _psProcess.Start();
                    _psProcess.BeginOutputReadLine();
                    _psProcess.BeginErrorReadLine();
                    AppendTermLine("PowerShell готов. Введите команду ниже и нажмите Enter.", false);
                }
                catch (Exception ex)
                {
                    AppendTermLine("Ошибка запуска PowerShell: " + ex.Message, true);
                }
            }
        }

        private void AppendTermLine(string line, bool isError)
        {
            Dispatcher.BeginInvoke((Action)delegate
            {
                if (_termOutput.Text.Length > 60000)
                    _termOutput.Text = _termOutput.Text.Substring(_termOutput.Text.Length - 30000);
                _termOutput.AppendText(line + Environment.NewLine);
                _termOutput.CaretIndex = _termOutput.Text.Length;
                _termOutput.ScrollToEnd();
            });
        }

        private void OnTermInputKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                string cmd = (_termInput.Text ?? "").Trim();
                _termInput.Clear();
                if (cmd.Length == 0) { e.Handled = true; return; }

                if (string.Equals(cmd, "cls", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(cmd, "clear", StringComparison.OrdinalIgnoreCase))
                {
                    _termOutput.Clear();
                }
                else
                {
                    _cmdHistory.Add(cmd);
                    _historyIdx = _cmdHistory.Count;
                    AppendTermLine("PS> " + cmd, false);
                    EnsurePowerShellStarted();
                    try
                    {
                        if (_psProcess != null && !_psProcess.HasExited)
                        {
                            _psProcess.StandardInput.WriteLine(cmd);
                            _psProcess.StandardInput.Flush();
                        }
                    }
                    catch (Exception ex)
                    {
                        AppendTermLine("Ошибка ввода: " + ex.Message, true);
                    }
                }
                e.Handled = true;
            }
            else if (e.Key == Key.Up)
            {
                if (_cmdHistory.Count > 0 && _historyIdx > 0)
                {
                    _historyIdx--;
                    _termInput.Text = _cmdHistory[_historyIdx];
                    _termInput.CaretIndex = _termInput.Text.Length;
                }
                e.Handled = true;
            }
            else if (e.Key == Key.Down)
            {
                if (_cmdHistory.Count > 0 && _historyIdx < _cmdHistory.Count - 1)
                {
                    _historyIdx++;
                    _termInput.Text = _cmdHistory[_historyIdx];
                    _termInput.CaretIndex = _termInput.Text.Length;
                }
                else
                {
                    _historyIdx = _cmdHistory.Count;
                    _termInput.Clear();
                }
                e.Handled = true;
            }
            else if (e.Key == Key.L && Keyboard.Modifiers == ModifierKeys.Control)
            {
                _termOutput.Clear();
                e.Handled = true;
            }
        }

        private void OnWindowKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                HideDrawer();
                e.Handled = true;
            }
            else if (e.Key == Key.Tab && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                SwitchTab(_activeTab == 0 ? 1 : 0);
                e.Handled = true;
            }
        }

        private void OnWindowDeactivated(object sender, EventArgs e)
        {
            if (_isVisible)
                HideDrawer();
        }

        private void OnWindowClosed(object sender, EventArgs e)
        {
            SaveNotes();
            lock (_psLock)
            {
                try
                {
                    if (_psProcess != null && !_psProcess.HasExited)
                        _psProcess.Kill();
                }
                catch { }
            }
        }

        public void ToggleDrawer()
        {
            if (_isVisible) HideDrawer();
            else ShowDrawer();
        }

        public void ShowDrawer()
        {
            PositionDrawer();
            _isVisible = true;
            Opacity = 0;
            Visibility = Visibility.Visible;
            Show();
            Activate();
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero) NativeMethods.SetForegroundWindow(hwnd);

            DoubleAnimation fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160));
            DoubleAnimation slide = new DoubleAnimation(-18, 0, TimeSpan.FromMilliseconds(190));
            slide.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            BeginAnimation(OpacityProperty, fade);
            _slideTransform.BeginAnimation(TranslateTransform.YProperty, slide);

            Dispatcher.BeginInvoke(DispatcherPriority.Input, (Action)delegate
            {
                if (_activeTab == 0) _notesBox.Focus();
                else _termInput.Focus();
            });
        }

        public void HideDrawer()
        {
            if (!_isVisible) return;
            _isVisible = false;
            SaveNotes();

            DoubleAnimation fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(120));
            DoubleAnimation slide = new DoubleAnimation(0, -14, TimeSpan.FromMilliseconds(120));
            fade.Completed += delegate
            {
                if (!_isVisible) Visibility = Visibility.Hidden;
            };
            BeginAnimation(OpacityProperty, fade);
            _slideTransform.BeginAnimation(TranslateTransform.YProperty, slide);
        }
    }

    public static class Program
    {
        private const string MutexName = @"Local\YASB_Scratchpad_Mutex";
        private const string ToggleEventName = @"Local\YASB_Scratchpad_Toggle";

        [STAThread]
        public static void Main(string[] args)
        {
            bool startHidden = false;
            int requestedTab = -1;
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], "--background", StringComparison.OrdinalIgnoreCase)) startHidden = true;
                else if (string.Equals(args[i], "--notes", StringComparison.OrdinalIgnoreCase)) requestedTab = 0;
                else if (string.Equals(args[i], "--terminal", StringComparison.OrdinalIgnoreCase)) requestedTab = 1;
            }

            bool createdNew;
            using (Mutex mutex = new Mutex(true, MutexName, out createdNew))
            {
                if (!createdNew)
                {
                    try
                    {
                        using (EventWaitHandle ev = EventWaitHandle.OpenExisting(ToggleEventName))
                            ev.Set();
                    }
                    catch { }
                    return;
                }

                EventWaitHandle toggleEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ToggleEventName);

                Application app = new Application();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;

                ScratchpadWindow win = new ScratchpadWindow();
                if (requestedTab >= 0) win.ShowDrawer();
                else if (!startHidden) win.ShowDrawer();

                ThreadPool.RegisterWaitForSingleObject(toggleEvent, delegate(object state, bool timedOut)
                {
                    if (timedOut) return;
                    win.Dispatcher.BeginInvoke((Action)delegate
                    {
                        win.ToggleDrawer();
                    });
                }, null, -1, false);

                app.Run();
                toggleEvent.Close();
            }
        }
    }
}
