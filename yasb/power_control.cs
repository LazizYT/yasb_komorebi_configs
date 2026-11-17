using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace YozoraPowerControl
{
    internal static class NativeMethods
    {
        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_TOOLWINDOW = 0x00000080;

        public static readonly Guid SUB_PROCESSOR = new Guid("54533251-82be-4824-96c1-47b60b740d00");
        public static readonly Guid PROCTHROTTLEMAX = new Guid("bc5038f7-23e0-4960-96da-33abaf5935ec");

        [StructLayout(LayoutKind.Sequential)]
        public struct FILETIME
        {
            public uint dwLowDateTime;
            public uint dwHighDateTime;
            public ulong ToUInt64() { return ((ulong)dwHighDateTime << 32) | dwLowDateTime; }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SYSTEM_POWER_STATUS
        {
            public byte ACLineStatus;
            public byte BatteryFlag;
            public byte BatteryLifePercent;
            public byte SystemStatusFlag;
            public int BatteryLifeTime;
            public int BatteryFullLifeTime;
        }

        [DllImport("user32.dll")]
        public static extern int GetWindowLong(IntPtr hwnd, int index);

        [DllImport("user32.dll")]
        public static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetSystemTimes(out FILETIME lpIdleTime, out FILETIME lpKernelTime, out FILETIME lpUserTime);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS lpSystemPowerStatus);

        [DllImport("kernel32.dll")]
        public static extern IntPtr LocalFree(IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr CreateFile(string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool DeviceIoControl(IntPtr hDevice, uint dwIoControlCode, byte[] lpInBuffer, uint nInBufferSize, byte[] lpOutBuffer, uint nOutBufferSize, ref uint lpBytesReturned, IntPtr lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr hObject);

        [DllImport("powrprof.dll")]
        public static extern uint PowerGetActiveScheme(IntPtr UserRootPowerKey, out IntPtr ActivePolicyGuid);

        [DllImport("powrprof.dll")]
        public static extern uint PowerReadACValueIndex(IntPtr RootPowerKey, ref Guid SchemeGuid, ref Guid SubGroupOfPowerSettingsGuid, ref Guid PowerSettingGuid, out uint AcValueIndex);

        [DllImport("powrprof.dll")]
        public static extern uint PowerReadDCValueIndex(IntPtr RootPowerKey, ref Guid SchemeGuid, ref Guid SubGroupOfPowerSettingsGuid, ref Guid PowerSettingGuid, out uint DcValueIndex);

        [DllImport("powrprof.dll")]
        public static extern uint PowerWriteACValueIndex(IntPtr RootPowerKey, ref Guid SchemeGuid, ref Guid SubGroupOfPowerSettingsGuid, ref Guid PowerSettingGuid, uint AcValueIndex);

        [DllImport("powrprof.dll")]
        public static extern uint PowerWriteDCValueIndex(IntPtr RootPowerKey, ref Guid SchemeGuid, ref Guid SubGroupOfPowerSettingsGuid, ref Guid PowerSettingGuid, uint DcValueIndex);

        [DllImport("powrprof.dll")]
        public static extern uint PowerSetActiveScheme(IntPtr UserRootPowerKey, ref Guid SchemeGuid);
    }

    internal static class AsusAcpi
    {
        private const string DevicePath = @"\\.\\ATKACPI";
        private const uint ControlCode = 0x0022240C;
        private const uint DSTS = 0x53545344;
        private const uint DEVS = 0x53564544;

        public const uint CPU_Fan = 0x00110013;
        public const uint GPU_Fan = 0x00110014;
        public const uint PerformanceMode = 0x00120075; // 0=Balanced, 1=Turbo, 2=Silent
        public const uint BatteryLimit = 0x00120057;    // 60, 80, 100

        private static IntPtr OpenDevice()
        {
            return NativeMethods.CreateFile(DevicePath, 0xC0000000, 3, IntPtr.Zero, 3, 0x80, IntPtr.Zero);
        }

        public static int DeviceGet(uint deviceId)
        {
            IntPtr h = OpenDevice();
            if (h == new IntPtr(-1)) return -1;
            try
            {
                byte[] buf = new byte[16];
                BitConverter.GetBytes(DSTS).CopyTo(buf, 0);
                BitConverter.GetBytes((uint)8).CopyTo(buf, 4);
                BitConverter.GetBytes(deviceId).CopyTo(buf, 8);
                byte[] outBuf = new byte[16];
                uint ret = 0;
                if (!NativeMethods.DeviceIoControl(h, ControlCode, buf, 16, outBuf, 16, ref ret, IntPtr.Zero))
                    return -1;
                return BitConverter.ToInt32(outBuf, 0) - 65536;
            }
            finally
            {
                NativeMethods.CloseHandle(h);
            }
        }

        public static bool DeviceSet(uint deviceId, int value)
        {
            IntPtr h = OpenDevice();
            if (h == new IntPtr(-1)) return false;
            try
            {
                byte[] buf = new byte[16];
                BitConverter.GetBytes(DEVS).CopyTo(buf, 0);
                BitConverter.GetBytes((uint)8).CopyTo(buf, 4);
                BitConverter.GetBytes(deviceId).CopyTo(buf, 8);
                BitConverter.GetBytes((uint)value).CopyTo(buf, 12);
                byte[] outBuf = new byte[16];
                uint ret = 0;
                if (!NativeMethods.DeviceIoControl(h, ControlCode, buf, 16, outBuf, 16, ref ret, IntPtr.Zero))
                    return false;
                return BitConverter.ToInt32(outBuf, 0) == 1;
            }
            finally
            {
                NativeMethods.CloseHandle(h);
            }
        }

        public static int GetFanRpm(uint fanEndpoint)
        {
            int raw = DeviceGet(fanEndpoint);
            if (raw < 0) return -1;
            int val = raw & 0xFFFF;
            if (val > 120) return -1;
            return val * 100;
        }
    }

    internal static class PowerState
    {
        private static readonly string StateFile = Path.Combine(@"C:\Users\Laziko\.config\yasb", "power_control_state.txt");

        public static int LastAsusMode = 0;   // 0=Balanced, 1=Turbo, 2=Silent
        public static int LastBatLimit = 100; // 60, 80, 100
        public static bool LibraryMode = false;

        public static void Load()
        {
            try
            {
                if (!File.Exists(StateFile)) return;
                string[] lines = File.ReadAllLines(StateFile);
                foreach (string line in lines)
                {
                    int idx = line.IndexOf('=');
                    if (idx <= 0) continue;
                    string k = line.Substring(0, idx).Trim();
                    string v = line.Substring(idx + 1).Trim();
                    int n;
                    if (k == "asus_mode" && int.TryParse(v, out n)) LastAsusMode = n;
                    else if (k == "bat_limit" && int.TryParse(v, out n)) LastBatLimit = n;
                    else if (k == "library" && int.TryParse(v, out n)) LibraryMode = (n == 1);
                }
            }
            catch { }
        }

        public static void Save()
        {
            try
            {
                File.WriteAllText(StateFile, string.Format("asus_mode={0}\nbat_limit={1}\nlibrary={2}\n",
                    LastAsusMode, LastBatLimit, LibraryMode ? 1 : 0));
            }
            catch { }
        }

        public static bool GetCpuLimits(out int acPct, out int dcPct, out bool onAc, out int batPct)
        {
            acPct = 100;
            dcPct = 100;
            onAc = true;
            batPct = 100;

            NativeMethods.SYSTEM_POWER_STATUS sps;
            if (NativeMethods.GetSystemPowerStatus(out sps))
            {
                onAc = (sps.ACLineStatus == 1);
                if (sps.BatteryLifePercent <= 100) batPct = sps.BatteryLifePercent;
            }

            IntPtr ptr;
            if (NativeMethods.PowerGetActiveScheme(IntPtr.Zero, out ptr) != 0 || ptr == IntPtr.Zero)
                return false;

            try
            {
                Guid scheme = (Guid)Marshal.PtrToStructure(ptr, typeof(Guid));
                Guid sub = NativeMethods.SUB_PROCESSOR;
                Guid setting = NativeMethods.PROCTHROTTLEMAX;
                uint acVal, dcVal;
                if (NativeMethods.PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, out acVal) == 0)
                    acPct = (int)acVal;
                if (NativeMethods.PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, out dcVal) == 0)
                    dcPct = (int)dcVal;
                return true;
            }
            finally
            {
                NativeMethods.LocalFree(ptr);
            }
        }

        public static bool SetCpuLimit(int pct)
        {
            if (pct < 20) pct = 20;
            if (pct > 100) pct = 100;

            IntPtr ptr;
            if (NativeMethods.PowerGetActiveScheme(IntPtr.Zero, out ptr) != 0 || ptr == IntPtr.Zero)
                return false;

            try
            {
                Guid scheme = (Guid)Marshal.PtrToStructure(ptr, typeof(Guid));
                Guid sub = NativeMethods.SUB_PROCESSOR;
                Guid setting = NativeMethods.PROCTHROTTLEMAX;
                uint val = (uint)pct;
                uint r1 = NativeMethods.PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, val);
                uint r2 = NativeMethods.PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, val);
                uint r3 = NativeMethods.PowerSetActiveScheme(IntPtr.Zero, ref scheme);
                return r1 == 0 && r2 == 0 && r3 == 0;
            }
            finally
            {
                NativeMethods.LocalFree(ptr);
            }
        }

        public static bool SetAsusMode(int mode, bool isLibrary)
        {
            bool ok = AsusAcpi.DeviceSet(AsusAcpi.PerformanceMode, mode);
            if (ok)
            {
                LastAsusMode = mode;
                LibraryMode = isLibrary;
                Save();
            }
            return ok;
        }

        public static bool SetBatteryLimit(int limit)
        {
            bool ok = AsusAcpi.DeviceSet(AsusAcpi.BatteryLimit, limit);
            if (ok)
            {
                LastBatLimit = limit;
                Save();
            }
            return ok;
        }

        public static string ApplyLibraryMode()
        {
            bool acpiOk = SetAsusMode(2, true);
            bool cpuOk = SetCpuLimit(60);
            return (acpiOk && cpuOk)
                ? "Библиотека: ASUS Silent + CPU 60%"
                : "Библиотека применена частично";
        }

        public static string CyclePreset()
        {
            Load();
            if (LibraryMode)
            {
                SetAsusMode(2, false);
                SetCpuLimit(99);
                return "Silent • CPU 99% (Без буста)";
            }
            if (LastAsusMode == 2)
            {
                SetAsusMode(0, false);
                SetCpuLimit(100);
                return "Balanced • CPU 100%";
            }
            if (LastAsusMode == 0)
            {
                SetAsusMode(1, false);
                SetCpuLimit(100);
                return "Turbo • CPU 100%";
            }
            ApplyLibraryMode();
            return "Библиотека: Silent • CPU 60%";
        }
    }

    public class PowerControlWindow : Window
    {
        private static readonly Color BgWindow = Color.FromArgb(0xF6, 0x13, 0x11, 0x22);
        private static readonly Color BgCard = Color.FromArgb(0xFF, 0x1A, 0x17, 0x2E);
        private static readonly Color BorderSakura = Color.FromArgb(0x55, 0xFF, 0x7E, 0xB6);
        private static readonly Color BorderSubtle = Color.FromArgb(0x28, 0xB4, 0xBE, 0xFE);
        private static readonly Color AccentSakura = Color.FromArgb(0xFF, 0xFF, 0x7E, 0xB6);
        private static readonly Color AccentCyan = Color.FromArgb(0xFF, 0x6C, 0xE5, 0xFF);
        private static readonly Color AccentGold = Color.FromArgb(0xFF, 0xFF, 0xD4, 0x79);
        private static readonly Color AccentGreen = Color.FromArgb(0xFF, 0x69, 0xF0, 0xAE);
        private static readonly Color TextPrimary = Color.FromArgb(0xFF, 0xF2, 0xEE, 0xFF);
        private static readonly Color TextMuted = Color.FromArgb(0xFF, 0x8E, 0x88, 0xAA);

        private Border _rootBorder;
        private TranslateTransform _slideTransform;
        private TextBlock _cpuLiveText;
        private TextBlock _fanCpuText;
        private TextBlock _fanGpuText;
        private TextBlock _gpuLiveText;
        private TextBlock _limitValueText;
        private Slider _limitSlider;
        private TextBlock _statusText;

        private Button _btnLibrary;
        private Button[] _asusModeBtns; // 0=Silent(2), 1=Balanced(0), 2=Turbo(1)
        private readonly int[] _asusModeIds = new int[] { 2, 0, 1 };
        private Button[] _cpuPresetBtns;
        private readonly int[] _cpuPresetVals = new int[] { 50, 75, 99, 100 };
        private Button[] _batBtns;
        private readonly int[] _batVals = new int[] { 60, 80, 100 };

        private ulong _prevIdle, _prevKernel, _prevUser;
        private bool _updatingSlider = false;
        private bool _isVisible = false;
        private bool _gpuQueryBusy = false;
        private string _lastGpuSummary = "GTX 1650: ...";
        private DispatcherTimer _pollTimer;

        public PowerControlWindow()
        {
            PowerState.Load();

            Title = "ASUS TUF Power & Fan Control";
            Width = 430;
            SizeToContent = SizeToContent.Height;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;

            BuildUi();
            PositionDrawer();

            _pollTimer = new DispatcherTimer();
            _pollTimer.Interval = TimeSpan.FromMilliseconds(1200);
            _pollTimer.Tick += delegate
            {
                if (_isVisible) RefreshTelemetry(true);
            };

            SourceInitialized += OnSourceInitialized;
            PreviewKeyDown += OnWindowKeyDown;
            Deactivated += OnWindowDeactivated;
        }

        private void PositionDrawer()
        {
            Rect wa = SystemParameters.WorkArea;
            Left = wa.Right - Width - 10;
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
            _slideTransform = new TranslateTransform(0, -14);

            _rootBorder = new Border
            {
                Background = new SolidColorBrush(BgWindow),
                BorderBrush = new SolidColorBrush(BorderSakura),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Margin = new Thickness(8),
                Padding = new Thickness(15, 12, 15, 12),
                RenderTransform = _slideTransform,
                Effect = new DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 22,
                    ShadowDepth = 6,
                    Opacity = 0.68
                }
            };

            StackPanel root = new StackPanel();

            // 1. Header
            Grid header = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel titleStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            titleStack.Children.Add(new Border
            {
                Width = 8,
                Height = 8,
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(AccentGreen),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            titleStack.Children.Add(new TextBlock
            {
                Text = "ASUS TUF • ТИШИНА И CPU",
                FontFamily = new FontFamily("Segoe UI Semibold"),
                FontSize = 12,
                Foreground = new SolidColorBrush(TextPrimary),
                VerticalAlignment = VerticalAlignment.Center
            });
            _cpuLiveText = new TextBlock
            {
                Text = "CPU --%",
                FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                FontSize = 11,
                Foreground = new SolidColorBrush(AccentCyan),
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            titleStack.Children.Add(_cpuLiveText);
            Grid.SetColumn(titleStack, 0);
            header.Children.Add(titleStack);

            Button closeBtn = CreateActionButton("Esc", delegate { HideAndExit(); });
            Grid.SetColumn(closeBtn, 1);
            header.Children.Add(closeBtn);
            root.Children.Add(header);

            // 2. Live Fans + GPU Telemetry Card
            Border fanCard = CreateCard(new Thickness(0, 0, 0, 10));
            Grid fanGrid = new Grid();
            fanGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            fanGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            fanGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            fanGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            _fanCpuText = new TextBlock
            {
                Text = "CPU Кулер: -- RPM",
                FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                FontSize = 11.5,
                Foreground = new SolidColorBrush(AccentCyan)
            };
            Grid.SetColumn(_fanCpuText, 0);
            fanGrid.Children.Add(_fanCpuText);

            _fanGpuText = new TextBlock
            {
                Text = "GPU Кулер: -- RPM",
                FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                FontSize = 11.5,
                Foreground = new SolidColorBrush(AccentSakura),
                HorizontalAlignment = HorizontalAlignment.Right
            };
            Grid.SetColumn(_fanGpuText, 1);
            fanGrid.Children.Add(_fanGpuText);

            _gpuLiveText = new TextBlock
            {
                Text = _lastGpuSummary,
                FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                FontSize = 10.5,
                Foreground = new SolidColorBrush(TextMuted),
                Margin = new Thickness(0, 5, 0, 0)
            };
            Grid.SetRow(_gpuLiveText, 1);
            Grid.SetColumnSpan(_gpuLiveText, 2);
            fanGrid.Children.Add(_gpuLiveText);

            fanCard.Child = fanGrid;
            root.Children.Add(fanCard);

            // 3. Library Quick Button + 3 ASUS BIOS Modes Card
            Border asusCard = CreateCard(new Thickness(0, 0, 0, 10));
            StackPanel asusStack = new StackPanel();

            _btnLibrary = CreatePillButton("🔇  Режим библиотеки (ASUS Silent + CPU 60%)", false, AccentGreen, delegate
            {
                _statusText.Text = PowerState.ApplyLibraryMode();
                RefreshTelemetry(false);
            });
            _btnLibrary.HorizontalAlignment = HorizontalAlignment.Stretch;
            _btnLibrary.HorizontalContentAlignment = HorizontalAlignment.Center;
            _btnLibrary.Margin = new Thickness(0, 0, 0, 8);
            asusStack.Children.Add(_btnLibrary);

            asusStack.Children.Add(new TextBlock
            {
                Text = "Режим охлаждения ASUS BIOS (ATKACPI)",
                FontFamily = new FontFamily("Segoe UI Semibold"),
                FontSize = 11,
                Foreground = new SolidColorBrush(TextMuted),
                Margin = new Thickness(0, 0, 0, 6)
            });

            Grid modeGrid = new Grid();
            for (int i = 0; i < 3; i++)
                modeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            string[] modeNames = new string[] { "Silent (Тихий)", "Balanced (Баланс)", "Turbo (Макс)" };
            Color[] modeColors = new Color[] { AccentCyan, AccentGold, AccentSakura };
            _asusModeBtns = new Button[3];
            for (int i = 0; i < 3; i++)
            {
                int modeId = _asusModeIds[i];
                string label = modeNames[i];
                Button mb = CreatePillButton(label, false, modeColors[i], delegate
                {
                    bool ok = PowerState.SetAsusMode(modeId, false);
                    if (modeId == 0 || modeId == 1) PowerState.SetCpuLimit(100);
                    else if (modeId == 2)
                    {
                        int ac, dc, bp; bool onAc;
                        PowerState.GetCpuLimits(out ac, out dc, out onAc, out bp);
                        if ((onAc ? ac : dc) > 99) PowerState.SetCpuLimit(99);
                    }
                    _statusText.Text = ok ? ("Профиль ASUS: " + label) : "Ошибка переключения ATKACPI";
                    RefreshTelemetry(false);
                });
                mb.Margin = new Thickness(i == 0 ? 0 : 3, 0, i == 2 ? 0 : 3, 0);
                mb.HorizontalAlignment = HorizontalAlignment.Stretch;
                mb.HorizontalContentAlignment = HorizontalAlignment.Center;
                Grid.SetColumn(mb, i);
                modeGrid.Children.Add(mb);
                _asusModeBtns[i] = mb;
            }
            asusStack.Children.Add(modeGrid);
            asusCard.Child = asusStack;
            root.Children.Add(asusCard);

            // 4. Global CPU Ceiling Card
            Border cpuCard = CreateCard(new Thickness(0, 0, 0, 10));
            StackPanel cpuStack = new StackPanel();

            Grid cpuHdr = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            cpuHdr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            cpuHdr.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            cpuHdr.Children.Add(new TextBlock
            {
                Text = "Потолок частоты CPU (PROCTHROTTLEMAX)",
                FontFamily = new FontFamily("Segoe UI Semibold"),
                FontSize = 11.5,
                Foreground = new SolidColorBrush(TextPrimary),
                VerticalAlignment = VerticalAlignment.Center
            });
            _limitValueText = new TextBlock
            {
                Text = "100%",
                FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(AccentGold)
            };
            Grid.SetColumn(_limitValueText, 1);
            cpuHdr.Children.Add(_limitValueText);
            cpuStack.Children.Add(cpuHdr);

            _limitSlider = new Slider
            {
                Minimum = 20,
                Maximum = 100,
                Value = 100,
                TickFrequency = 5,
                IsSnapToTickEnabled = true,
                Margin = new Thickness(0, 0, 0, 8),
                Cursor = Cursors.Hand
            };
            _limitSlider.ValueChanged += delegate
            {
                if (_updatingSlider) return;
                int val = (int)Math.Round(_limitSlider.Value);
                _limitValueText.Text = val + "%";
                UpdateCpuPresetHighlights(val);
            };
            _limitSlider.PreviewMouseLeftButtonUp += delegate { ApplyCpuCeiling((int)Math.Round(_limitSlider.Value)); };
            _limitSlider.KeyUp += delegate(object s, KeyEventArgs ke)
            {
                if (ke.Key == Key.Left || ke.Key == Key.Right)
                    ApplyCpuCeiling((int)Math.Round(_limitSlider.Value));
            };
            cpuStack.Children.Add(_limitSlider);

            Grid presetGrid = new Grid();
            for (int i = 0; i < 4; i++)
                presetGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            string[] pNames = new string[] { "50%", "75%", "99% (No Boost)", "100%" };
            _cpuPresetBtns = new Button[4];
            for (int i = 0; i < 4; i++)
            {
                int val = _cpuPresetVals[i];
                Button pb = CreatePillButton(pNames[i], false, AccentSakura, delegate { ApplyCpuCeiling(val); });
                pb.Margin = new Thickness(i == 0 ? 0 : 3, 0, i == 3 ? 0 : 3, 0);
                pb.HorizontalAlignment = HorizontalAlignment.Stretch;
                pb.HorizontalContentAlignment = HorizontalAlignment.Center;
                Grid.SetColumn(pb, i);
                presetGrid.Children.Add(pb);
                _cpuPresetBtns[i] = pb;
            }
            cpuStack.Children.Add(presetGrid);
            cpuCard.Child = cpuStack;
            root.Children.Add(cpuCard);

            // 5. Battery Charge Limit Card
            Border batCard = CreateCard(new Thickness(0, 0, 0, 8));
            Grid batGrid = new Grid();
            batGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            batGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            batGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            batGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            batGrid.Children.Add(new TextBlock
            {
                Text = "Лимит заряда батареи ASUS",
                FontFamily = new FontFamily("Segoe UI Semibold"),
                FontSize = 11.5,
                Foreground = new SolidColorBrush(TextPrimary),
                VerticalAlignment = VerticalAlignment.Center
            });
            _batBtns = new Button[3];
            for (int i = 0; i < 3; i++)
            {
                int lim = _batVals[i];
                Button bb = CreatePillButton(lim + "%", false, AccentCyan, delegate
                {
                    bool ok = PowerState.SetBatteryLimit(lim);
                    _statusText.Text = ok ? ("Лимит заряда батареи: " + lim + "%") : "Не удалось задать лимит заряда";
                    RefreshTelemetry(false);
                });
                bb.Margin = new Thickness(4, 0, 0, 0);
                bb.Padding = new Thickness(10, 4, 10, 4);
                Grid.SetColumn(bb, i + 1);
                batGrid.Children.Add(bb);
                _batBtns[i] = bb;
            }
            batCard.Child = batGrid;
            root.Children.Add(batCard);

            // 6. Footer
            Grid footer = new Grid();
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _statusText = new TextBlock
            {
                Text = "0% CPU в фоне после закрытия  •  ПКМ по иконке: быстрый цикл",
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 10.5,
                Foreground = new SolidColorBrush(TextMuted),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(_statusText, 0);
            footer.Children.Add(_statusText);

            TextBlock brand = new TextBlock
            {
                Text = "Yozora Sakura",
                FontFamily = new FontFamily("Segoe UI Semibold"),
                FontSize = 10.5,
                Foreground = new SolidColorBrush(AccentSakura),
                Opacity = 0.78,
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(brand, 1);
            footer.Children.Add(brand);
            root.Children.Add(footer);

            _rootBorder.Child = root;
            Content = _rootBorder;
        }

        private Border CreateCard(Thickness margin)
        {
            return new Border
            {
                Background = new SolidColorBrush(BgCard),
                BorderBrush = new SolidColorBrush(BorderSubtle),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(11, 9, 11, 9),
                Margin = margin
            };
        }

        private Button CreatePillButton(string text, bool active, Color accent, RoutedEventHandler onClick)
        {
            Button btn = new Button
            {
                Content = text,
                FontFamily = new FontFamily("Segoe UI Semibold"),
                FontSize = 11,
                Padding = new Thickness(8, 5, 8, 5),
                Cursor = Cursors.Hand,
                BorderThickness = new Thickness(1)
            };
            StylePillButton(btn, active, accent);
            btn.Click += onClick;
            return btn;
        }

        private void StylePillButton(Button btn, bool active, Color accent)
        {
            if (active)
            {
                btn.Background = new SolidColorBrush(Color.FromArgb(0x36, accent.R, accent.G, accent.B));
                btn.BorderBrush = new SolidColorBrush(accent);
                btn.Foreground = new SolidColorBrush(TextPrimary);
            }
            else
            {
                btn.Background = new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
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
                Padding = new Thickness(9, 3, 9, 3),
                Background = new SolidColorBrush(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF)),
                BorderBrush = new SolidColorBrush(BorderSubtle),
                BorderThickness = new Thickness(1),
                Foreground = new SolidColorBrush(TextPrimary),
                Cursor = Cursors.Hand
            };
            btn.Click += onClick;
            return btn;
        }

        private void ApplyCpuCeiling(int pct)
        {
            PowerState.LibraryMode = false;
            PowerState.Save();
            bool ok = PowerState.SetCpuLimit(pct);
            _updatingSlider = true;
            _limitSlider.Value = pct;
            _limitValueText.Text = pct + "%";
            _updatingSlider = false;
            UpdateCpuPresetHighlights(pct);
            StylePillButton(_btnLibrary, false, AccentGreen);
            _statusText.Text = ok
                ? string.Format("Потолок CPU: {0}% (AC и батарея)", pct)
                : "Не удалось изменить потолок CPU";
        }

        private void UpdateCpuPresetHighlights(int currentVal)
        {
            for (int i = 0; i < _cpuPresetBtns.Length; i++)
                StylePillButton(_cpuPresetBtns[i], _cpuPresetVals[i] == currentVal, AccentSakura);
        }

        private void RefreshTelemetry(bool queryGpu)
        {
            int ac, dc, batPct;
            bool onAc;
            if (PowerState.GetCpuLimits(out ac, out dc, out onAc, out batPct))
            {
                int activePct = onAc ? ac : dc;
                _updatingSlider = true;
                _limitSlider.Value = activePct;
                _limitValueText.Text = activePct + "%";
                _updatingSlider = false;
                UpdateCpuPresetHighlights(activePct);
            }

            StylePillButton(_btnLibrary, PowerState.LibraryMode, AccentGreen);
            Color[] modeColors = new Color[] { AccentCyan, AccentGold, AccentSakura };
            for (int i = 0; i < _asusModeBtns.Length; i++)
                StylePillButton(_asusModeBtns[i], !PowerState.LibraryMode && PowerState.LastAsusMode == _asusModeIds[i], modeColors[i]);
            for (int i = 0; i < _batBtns.Length; i++)
                StylePillButton(_batBtns[i], PowerState.LastBatLimit == _batVals[i], AccentCyan);

            // System CPU %
            NativeMethods.FILETIME ftIdle, ftKernel, ftUser;
            if (NativeMethods.GetSystemTimes(out ftIdle, out ftKernel, out ftUser))
            {
                ulong idle = ftIdle.ToUInt64();
                ulong kernel = ftKernel.ToUInt64();
                ulong user = ftUser.ToUInt64();
                if (_prevKernel != 0)
                {
                    ulong dIdle = idle - _prevIdle;
                    ulong dTotal = (kernel - _prevKernel) + (user - _prevUser);
                    if (dTotal > 0)
                    {
                        double cpu = Math.Max(0, Math.Min(100, (dTotal - dIdle) * 100.0 / dTotal));
                        _cpuLiveText.Text = string.Format("CPU {0:0}%  •  {1} {2}%", cpu, onAc ? "AC" : "BAT", batPct);
                    }
                }
                _prevIdle = idle;
                _prevKernel = kernel;
                _prevUser = user;
            }

            // ASUS Fan RPMs
            int cpuRpm = AsusAcpi.GetFanRpm(AsusAcpi.CPU_Fan);
            int gpuRpm = AsusAcpi.GetFanRpm(AsusAcpi.GPU_Fan);
            _fanCpuText.Text = cpuRpm >= 0 ? string.Format("CPU Кулер: {0} RPM", cpuRpm) : "CPU Кулер: --";
            _fanGpuText.Text = gpuRpm >= 0 ? string.Format("GPU Кулер: {0} RPM", gpuRpm) : "GPU Кулер: --";

            if (queryGpu && !_gpuQueryBusy)
                QueryNvidiaGpuAsync();
        }

        private void QueryNvidiaGpuAsync()
        {
            _gpuQueryBusy = true;
            ThreadPool.QueueUserWorkItem(delegate
            {
                string summary = null;
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo
                    {
                        FileName = "nvidia-smi",
                        Arguments = "--query-gpu=utilization.gpu,temperature.gpu,power.draw --format=csv,noheader,nounits",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    };
                    using (Process p = Process.Start(psi))
                    {
                        if (p != null)
                        {
                            string line = p.StandardOutput.ReadLine();
                            p.WaitForExit(900);
                            if (!string.IsNullOrEmpty(line))
                            {
                                string[] parts = line.Split(',');
                                if (parts.Length >= 3)
                                {
                                    summary = string.Format("GTX 1650: {0}%  •  {1}°C  •  {2} W",
                                        parts[0].Trim(), parts[1].Trim(), parts[2].Trim());
                                }
                            }
                        }
                    }
                }
                catch { }
                finally
                {
                    Dispatcher.BeginInvoke((Action)delegate
                    {
                        if (!string.IsNullOrEmpty(summary))
                        {
                            _lastGpuSummary = summary;
                            _gpuLiveText.Text = summary;
                        }
                        _gpuQueryBusy = false;
                    });
                }
            });
        }

        private void OnWindowKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                HideAndExit();
                e.Handled = true;
            }
        }

        private void OnWindowDeactivated(object sender, EventArgs e)
        {
            if (_isVisible)
                HideAndExit();
        }

        public void ShowDrawer()
        {
            PositionDrawer();
            _isVisible = true;
            RefreshTelemetry(true);
            _pollTimer.Start();

            Opacity = 0;
            Visibility = Visibility.Visible;
            Show();
            Activate();
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero) NativeMethods.SetForegroundWindow(hwnd);

            DoubleAnimation fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140));
            DoubleAnimation slide = new DoubleAnimation(-14, 0, TimeSpan.FromMilliseconds(170));
            slide.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            BeginAnimation(OpacityProperty, fade);
            _slideTransform.BeginAnimation(TranslateTransform.YProperty, slide);
        }

        public void HideAndExit()
        {
            if (!_isVisible) return;
            _isVisible = false;
            _pollTimer.Stop();

            DoubleAnimation fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(100));
            DoubleAnimation slide = new DoubleAnimation(0, -10, TimeSpan.FromMilliseconds(100));
            fade.Completed += delegate { Application.Current.Shutdown(); };
            BeginAnimation(OpacityProperty, fade);
            _slideTransform.BeginAnimation(TranslateTransform.YProperty, slide);
        }
    }

    public static class Program
    {
        private const string MutexName = @"Local\YASB_PowerControl_Mutex";
        private const string CloseEventName = @"Local\YASB_PowerControl_Close";

        [STAThread]
        public static void Main(string[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], "--cycle", StringComparison.OrdinalIgnoreCase))
                {
                    string msg = PowerState.CyclePreset();
                    ShowToast(msg);
                    return;
                }
                if (string.Equals(args[i], "--library", StringComparison.OrdinalIgnoreCase))
                {
                    string msg = PowerState.ApplyLibraryMode();
                    ShowToast(msg);
                    return;
                }
                if (string.Equals(args[i], "--set", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    int val;
                    if (int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out val))
                        PowerState.SetCpuLimit(val);
                    return;
                }
                if (string.Equals(args[i], "--asus", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    int mode;
                    if (int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out mode))
                        PowerState.SetAsusMode(mode, false);
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

                PowerControlWindow win = new PowerControlWindow();
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

        private static void ShowToast(string message)
        {
            Application app = new Application();
            Rect wa = SystemParameters.WorkArea;
            Window toast = new Window
            {
                Width = 250,
                Height = 40,
                Left = wa.Right - 262,
                Top = wa.Top + 8,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                ShowInTaskbar = false,
                Topmost = true,
                ShowActivated = false,
                Content = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(0xF2, 0x13, 0x11, 0x22)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(0x88, 0x69, 0xF0, 0xAE)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(12, 6, 12, 6),
                    Child = new TextBlock
                    {
                        Text = message,
                        FontFamily = new FontFamily("Segoe UI Semibold"),
                        FontSize = 11.5,
                        Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0x69, 0xF0, 0xAE)),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                }
            };
            DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
            timer.Tick += delegate { timer.Stop(); toast.Close(); };
            toast.Loaded += delegate { timer.Start(); };
            app.Run(toast);
        }
    }
}
