using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace YasbAudioVisualizer
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

        public static readonly IntPtr HWND_BOTTOM = new IntPtr(1);
        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOACTIVATE = 0x0010;

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
    }

    // Minimal CoreAudio WASAPI Loopback COM interfaces
    internal static class WasapiCom
    {
        public static readonly Guid CLSID_MMDeviceEnumerator = new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E");
        public static readonly Guid IID_IAudioClient = new Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2");
        public static readonly Guid IID_IAudioCaptureClient = new Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317");
        public static readonly Guid KSDATAFORMAT_SUBTYPE_IEEE_FLOAT = new Guid("00000003-0000-0010-8000-00AA00389B71");

        public const int CLSCTX_ALL = 23;
        public const int AUDCLNT_SHAREMODE_SHARED = 0;
        public const int AUDCLNT_STREAMFLAGS_LOOPBACK = 0x00020000;
        public const int AUDCLNT_BUFFERFLAGS_SILENT = 0x2;

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IMMDeviceEnumerator
        {
            int EnumAudioEndpoints(int dataFlow, int dwStateMask, out IntPtr ppDevices);
            int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice ppEndpoint);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IMMDevice
        {
            int Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
        }

        [ComImport, Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IAudioClient
        {
            int Initialize(int ShareMode, int StreamFlags, long hnsBufferDuration, long hnsPeriodicity, IntPtr pFormat, IntPtr AudioSessionGuid);
            int GetBufferSize(out uint pNumBufferFrames);
            int GetStreamLatency(out long phnsLatency);
            int GetCurrentPadding(out uint pNumPaddingFrames);
            int IsFormatSupported(int ShareMode, IntPtr pFormat, out IntPtr ppClosestMatch);
            int GetMixFormat(out IntPtr ppDeviceFormat);
            int GetDevicePeriod(out long phnsDefaultDevicePeriod, out long phnsMinimumDevicePeriod);
            int Start();
            int Stop();
            int Reset();
            int SetEventHandle(IntPtr eventHandle);
            int GetService(ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
        }

        [ComImport, Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IAudioCaptureClient
        {
            int GetBuffer(out IntPtr ppData, out uint pNumFramesToRead, out int pdwFlags, out ulong pu64DevicePosition, out ulong pu64QPCPosition);
            int ReleaseBuffer(uint NumFramesRead);
            int GetNextPacketSize(out uint pNumFramesInNextPacket);
        }
    }

    internal sealed class LoopbackAnalyzer : IDisposable
    {
        public const int FftSize = 1024;
        public const int BandCount = 56;

        private readonly float[] _ring = new float[FftSize];
        private int _ringPos = 0;
        private readonly object _ringLock = new object();

        private readonly double[] _window = new double[FftSize];
        private readonly double[] _re = new double[FftSize];
        private readonly double[] _im = new double[FftSize];
        private readonly int[] _bandBinStart = new int[BandCount];
        private readonly int[] _bandBinEnd = new int[BandCount];

        public readonly double[] Bands = new double[BandCount];
        public readonly double[] Peaks = new double[BandCount];

        private volatile bool _running = true;
        private volatile bool _paused = false;
        private int _sampleRate = 48000;
        private double _silenceSeconds = 10.0;
        private Thread _thread;

        public bool IsActiveAudio { get { return _silenceSeconds < 1.8; } }

        public LoopbackAnalyzer()
        {
            for (int i = 0; i < FftSize; i++)
            {
                _window[i] = 0.5 * (1.0 - Math.Cos(2.0 * Math.PI * i / (FftSize - 1)));
            }
            RebuildBandMap(48000);

            _thread = new Thread(CaptureLoop) { IsBackground = true, Priority = ThreadPriority.AboveNormal };
            _thread.SetApartmentState(ApartmentState.MTA);
            _thread.Start();
        }

        public void SetPaused(bool paused)
        {
            _paused = paused;
        }

        private void RebuildBandMap(int sampleRate)
        {
            _sampleRate = Math.Max(16000, sampleRate);
            double minFreq = 42.0;
            double maxFreq = Math.Min(14500.0, _sampleRate * 0.45);
            double binHz = (double)_sampleRate / FftSize;

            for (int b = 0; b < BandCount; b++)
            {
                double t0 = (double)b / BandCount;
                double t1 = (double)(b + 1) / BandCount;
                double f0 = minFreq * Math.Pow(maxFreq / minFreq, t0);
                double f1 = minFreq * Math.Pow(maxFreq / minFreq, t1);
                int k0 = Math.Max(1, (int)Math.Floor(f0 / binHz));
                int k1 = Math.Max(k0, (int)Math.Ceiling(f1 / binHz));
                if (k1 >= FftSize / 2) k1 = FftSize / 2 - 1;
                _bandBinStart[b] = k0;
                _bandBinEnd[b] = k1;
            }
        }

        private void CaptureLoop()
        {
            while (_running)
            {
                if (_paused)
                {
                    Thread.Sleep(150);
                    continue;
                }

                WasapiCom.IMMDeviceEnumerator enumerator = null;
                WasapiCom.IMMDevice device = null;
                WasapiCom.IAudioClient client = null;
                WasapiCom.IAudioCaptureClient capture = null;
                IntPtr fmtPtr = IntPtr.Zero;

                try
                {
                    var type = Type.GetTypeFromCLSID(WasapiCom.CLSID_MMDeviceEnumerator);
                    enumerator = (WasapiCom.IMMDeviceEnumerator)Activator.CreateInstance(type);
                    if (enumerator.GetDefaultAudioEndpoint(0, 1, out device) != 0 || device == null)
                    {
                        Thread.Sleep(1000);
                        continue;
                    }

                    Guid iidClient = WasapiCom.IID_IAudioClient;
                    object objClient;
                    if (device.Activate(ref iidClient, WasapiCom.CLSCTX_ALL, IntPtr.Zero, out objClient) != 0)
                    {
                        Thread.Sleep(1000);
                        continue;
                    }
                    client = (WasapiCom.IAudioClient)objClient;

                    if (client.GetMixFormat(out fmtPtr) != 0 || fmtPtr == IntPtr.Zero)
                    {
                        Thread.Sleep(1000);
                        continue;
                    }

                    ushort formatTag = (ushort)Marshal.ReadInt16(fmtPtr, 0);
                    ushort channels = (ushort)Marshal.ReadInt16(fmtPtr, 2);
                    int sampleRate = Marshal.ReadInt32(fmtPtr, 4);
                    ushort bitsPerSample = (ushort)Marshal.ReadInt16(fmtPtr, 14);
                    int blockAlign = Marshal.ReadInt16(fmtPtr, 12);

                    bool isFloat = false;
                    if (formatTag == 3) isFloat = true;
                    else if (formatTag == 0xFFFE)
                    {
                        byte[] gBytes = new byte[16];
                        Marshal.Copy(IntPtr.Add(fmtPtr, 24), gBytes, 0, 16);
                        isFloat = (new Guid(gBytes) == WasapiCom.KSDATAFORMAT_SUBTYPE_IEEE_FLOAT);
                    }

                    if (sampleRate != _sampleRate) RebuildBandMap(sampleRate);

                    long hnsBuffer = 500000; // 50 ms
                    int initHr = client.Initialize(WasapiCom.AUDCLNT_SHAREMODE_SHARED, WasapiCom.AUDCLNT_STREAMFLAGS_LOOPBACK, hnsBuffer, 0, fmtPtr, IntPtr.Zero);
                    if (initHr != 0)
                    {
                        Thread.Sleep(1000);
                        continue;
                    }

                    Guid iidCapture = WasapiCom.IID_IAudioCaptureClient;
                    object objCap;
                    if (client.GetService(ref iidCapture, out objCap) != 0)
                    {
                        Thread.Sleep(1000);
                        continue;
                    }
                    capture = (WasapiCom.IAudioCaptureClient)objCap;

                    if (client.Start() != 0)
                    {
                        Thread.Sleep(1000);
                        continue;
                    }

                    while (_running && !_paused)
                    {
                        uint packetFrames = 0;
                        int hr = capture.GetNextPacketSize(out packetFrames);
                        if (hr < 0) break;

                        if (packetFrames == 0)
                        {
                            Thread.Sleep(8);
                            continue;
                        }

                        while (packetFrames > 0 && _running && !_paused)
                        {
                            IntPtr dataPtr;
                            uint framesRead;
                            int flags;
                            ulong devPos, qpcPos;
                            hr = capture.GetBuffer(out dataPtr, out framesRead, out flags, out devPos, out qpcPos);
                            if (hr < 0) { packetFrames = 0; break; }

                            bool silent = (flags & WasapiCom.AUDCLNT_BUFFERFLAGS_SILENT) != 0;
                            PushSamples(dataPtr, (int)framesRead, channels, blockAlign, bitsPerSample, isFloat, silent);
                            capture.ReleaseBuffer(framesRead);

                            if (capture.GetNextPacketSize(out packetFrames) < 0) break;
                        }
                    }

                    try { client.Stop(); } catch { }
                }
                catch
                {
                    Thread.Sleep(1000);
                }
                finally
                {
                    if (fmtPtr != IntPtr.Zero) Marshal.FreeCoTaskMem(fmtPtr);
                    if (capture != null) Marshal.FinalReleaseComObject(capture);
                    if (client != null) Marshal.FinalReleaseComObject(client);
                    if (device != null) Marshal.FinalReleaseComObject(device);
                    if (enumerator != null) Marshal.FinalReleaseComObject(enumerator);
                }
            }
        }

        private void PushSamples(IntPtr dataPtr, int frames, int channels, int blockAlign, int bits, bool isFloat, bool silent)
        {
            if (frames <= 0 || channels <= 0) return;
            lock (_ringLock)
            {
                if (silent || dataPtr == IntPtr.Zero)
                {
                    for (int f = 0; f < frames; f++)
                    {
                        _ring[_ringPos] = 0f;
                        _ringPos = (_ringPos + 1) & (FftSize - 1);
                    }
                    return;
                }

                byte[] raw = new byte[frames * blockAlign];
                Marshal.Copy(dataPtr, raw, 0, raw.Length);

                for (int f = 0; f < frames; f++)
                {
                    int off = f * blockAlign;
                    float sum = 0f;
                    int chCount = Math.Min(channels, 2);
                    for (int c = 0; c < chCount; c++)
                    {
                        if (isFloat && bits == 32)
                            sum += BitConverter.ToSingle(raw, off + c * 4);
                        else if (!isFloat && bits == 16)
                            sum += BitConverter.ToInt16(raw, off + c * 2) / 32768f;
                    }
                    _ring[_ringPos] = sum / chCount;
                    _ringPos = (_ringPos + 1) & (FftSize - 1);
                }
            }
        }

        public void ComputeSpectrum(double dt)
        {
            double energySum = 0;
            lock (_ringLock)
            {
                int idx = _ringPos;
                for (int i = 0; i < FftSize; i++)
                {
                    double s = _ring[idx];
                    energySum += s * s;
                    _re[i] = s * _window[i];
                    _im[i] = 0;
                    idx = (idx + 1) & (FftSize - 1);
                }
            }

            double rms = Math.Sqrt(energySum / FftSize);
            if (rms > 0.0002) _silenceSeconds = 0;
            else _silenceSeconds = Math.Min(10.0, _silenceSeconds + dt);

            if (!IsActiveAudio)
            {
                double fade = Math.Max(0, 1.0 - dt * 5.0);
                for (int b = 0; b < BandCount; b++)
                {
                    Bands[b] *= fade;
                    Peaks[b] *= fade;
                }
                return;
            }

            FftInPlace(_re, _im, FftSize);

            double up = Math.Min(1.0, dt * 26.0);
            double down = Math.Min(1.0, dt * 10.0);
            double peakFall = dt * 0.45;

            for (int b = 0; b < BandCount; b++)
            {
                int k0 = _bandBinStart[b];
                int k1 = _bandBinEnd[b];
                double maxMag = 0;
                for (int k = k0; k <= k1; k++)
                {
                    double mag = Math.Sqrt(_re[k] * _re[k] + _im[k] * _im[k]) / (FftSize * 0.35);
                    if (mag > maxMag) maxMag = mag;
                }

                // Slight perceptual tilt toward mids/highs + logarithmic compression
                double tilt = 1.0 + (double)b / BandCount * 1.45;
                double val = Math.Log10(1.0 + maxMag * tilt * 14.0) / Math.Log10(15.0);
                val = Math.Max(0, Math.Min(1.0, val * 1.15));

                double prev = Bands[b];
                Bands[b] = val > prev ? prev + (val - prev) * up : prev + (val - prev) * down;

                if (Bands[b] > Peaks[b]) Peaks[b] = Bands[b];
                else Peaks[b] = Math.Max(0, Peaks[b] - peakFall);
            }
        }

        private static void FftInPlace(double[] re, double[] im, int n)
        {
            int j = 0;
            for (int i = 1; i < n; i++)
            {
                int bit = n >> 1;
                while ((j & bit) != 0)
                {
                    j ^= bit;
                    bit >>= 1;
                }
                j ^= bit;
                if (i < j)
                {
                    double tr = re[i]; re[i] = re[j]; re[j] = tr;
                    double ti = im[i]; im[i] = im[j]; im[j] = ti;
                }
            }

            for (int len = 2; len <= n; len <<= 1)
            {
                double ang = -2.0 * Math.PI / len;
                double wLenRe = Math.Cos(ang);
                double wLenIm = Math.Sin(ang);
                for (int i = 0; i < n; i += len)
                {
                    double wRe = 1.0, wIm = 0.0;
                    int half = len >> 1;
                    for (int k = 0; k < half; k++)
                    {
                        double uRe = re[i + k];
                        double uIm = im[i + k];
                        double vRe = re[i + k + half] * wRe - im[i + k + half] * wIm;
                        double vIm = re[i + k + half] * wIm + im[i + k + half] * wRe;

                        re[i + k] = uRe + vRe;
                        im[i + k] = uIm + vIm;
                        re[i + k + half] = uRe - vRe;
                        im[i + k + half] = uIm - vIm;

                        double nextWRe = wRe * wLenRe - wIm * wLenIm;
                        wIm = wRe * wLenIm + wIm * wLenRe;
                        wRe = nextWRe;
                    }
                }
            }
        }

        public void Dispose()
        {
            _running = false;
        }
    }

    public class VisualizerApp : Application
    {
        private const string MutexName = @"Local\YASB_AudioViz_Mutex";
        private const string ToggleEventName = @"Local\YASB_AudioViz_Toggle";
        private const string ModeEventName = @"Local\YASB_AudioViz_Mode";
        private static Mutex _mutex;
        private VisualizerWindow _window;
        private EventWaitHandle _toggleEvent;
        private EventWaitHandle _modeEvent;

        [STAThread]
        public static void Main(string[] args)
        {
            bool modeOnly = args.Any(a => a.Equals("--mode", StringComparison.OrdinalIgnoreCase));
            bool backgroundOnly = args.Any(a => a.Equals("--background", StringComparison.OrdinalIgnoreCase));

            bool createdNew;
            _mutex = new Mutex(true, MutexName, out createdNew);
            if (!createdNew)
            {
                try
                {
                    using (var ev = EventWaitHandle.OpenExisting(modeOnly ? ModeEventName : ToggleEventName))
                    {
                        ev.Set();
                    }
                }
                catch { }
                return;
            }

            var app = new VisualizerApp();
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            app.StartApp(backgroundOnly);
            app.Run();
        }

        private void StartApp(bool backgroundOnly)
        {
            _toggleEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ToggleEventName);
            _modeEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ModeEventName);
            _window = new VisualizerWindow();

            var t1 = new Thread(() =>
            {
                while (true)
                {
                    _toggleEvent.WaitOne();
                    Dispatcher.BeginInvoke(new Action(() => _window.ToggleOverlay()));
                }
            })
            { IsBackground = true };
            t1.Start();

            var t2 = new Thread(() =>
            {
                while (true)
                {
                    _modeEvent.WaitOne();
                    Dispatcher.BeginInvoke(new Action(() => _window.CycleMode()));
                }
            })
            { IsBackground = true };
            t2.Start();

            if (!backgroundOnly) _window.ShowOverlay();
        }
    }

    internal class VisualizerCanvas : FrameworkElement
    {
        private readonly LoopbackAnalyzer _analyzer;
        private readonly Brush[] _barBrushes = new Brush[LoopbackAnalyzer.BandCount];
        private readonly Brush[] _glowBrushes = new Brush[LoopbackAnalyzer.BandCount];
        private readonly Brush _peakBrush;
        private readonly Pen _wavePen;
        private readonly Brush _waveFillBrush;
        private readonly Stopwatch _watch = new Stopwatch();
        private TimeSpan _lastFrame = TimeSpan.Zero;
        private bool _running = false;
        private int _mode = 0; // 0: Spectrum Bars, 1: Mirror Center Bars, 2: Neon Wave

        public VisualizerCanvas(LoopbackAnalyzer analyzer)
        {
            _analyzer = analyzer;
            IsHitTestVisible = false;

            // Yozora Sakura gradient: Sakura Pink (#FF7EB6) -> Lavender (#B4BEFE) -> Miku Cyan (#6CE5FF)
            Color c1 = Color.FromRgb(255, 126, 182);
            Color c2 = Color.FromRgb(180, 190, 254);
            Color c3 = Color.FromRgb(108, 229, 255);

            for (int i = 0; i < LoopbackAnalyzer.BandCount; i++)
            {
                double t = (double)i / (LoopbackAnalyzer.BandCount - 1);
                Color c = t < 0.5 ? Lerp(c1, c2, t * 2.0) : Lerp(c2, c3, (t - 0.5) * 2.0);
                var b = new SolidColorBrush(Color.FromArgb(215, c.R, c.G, c.B));
                b.Freeze();
                _barBrushes[i] = b;

                var g = new SolidColorBrush(Color.FromArgb(45, c.R, c.G, c.B));
                g.Freeze();
                _glowBrushes[i] = g;
            }

            var pb = new SolidColorBrush(Color.FromArgb(235, 242, 238, 255));
            pb.Freeze();
            _peakBrush = pb;

            var waveGrad = new LinearGradientBrush(Color.FromArgb(235, c1.R, c1.G, c1.B), Color.FromArgb(235, c3.R, c3.G, c3.B), 0);
            waveGrad.Freeze();
            _wavePen = new Pen(waveGrad, 2.4) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            _wavePen.Freeze();

            var fillGrad = new LinearGradientBrush(Color.FromArgb(85, c1.R, c1.G, c1.B), Color.FromArgb(10, c3.R, c3.G, c3.B), new Point(0, 0), new Point(0, 1));
            fillGrad.Freeze();
            _waveFillBrush = fillGrad;
        }

        private static Color Lerp(Color a, Color b, double t)
        {
            return Color.FromRgb(
                (byte)(a.R + (b.R - a.R) * t),
                (byte)(a.G + (b.G - a.G) * t),
                (byte)(a.B + (b.B - a.B) * t));
        }

        public void CycleMode()
        {
            _mode = (_mode + 1) % 3;
            InvalidateVisual();
        }

        public void Start()
        {
            if (_running) return;
            _running = true;
            _analyzer.SetPaused(false);
            _watch.Restart();
            _lastFrame = TimeSpan.Zero;
            CompositionTarget.Rendering += OnFrame;
        }

        public void Stop()
        {
            if (!_running) return;
            _running = false;
            _analyzer.SetPaused(true);
            CompositionTarget.Rendering -= OnFrame;
            _watch.Stop();
        }

        private void OnFrame(object sender, EventArgs e)
        {
            var now = _watch.Elapsed;
            double dt = (now - _lastFrame).TotalSeconds;
            _lastFrame = now;
            if (dt <= 0) return;
            if (dt > 0.05) dt = 0.05;

            _analyzer.ComputeSpectrum(dt);
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth;
            double h = ActualHeight;
            if (w <= 0 || h <= 0) return;

            int n = LoopbackAnalyzer.BandCount;
            double maxBarH = h - 18;
            double bottomY = h - 6;

            // Check if completely silent and settled
            bool any = false;
            for (int i = 0; i < n; i++)
            {
                if (_analyzer.Bands[i] > 0.004 || _analyzer.Peaks[i] > 0.004) { any = true; break; }
            }
            if (!any) return;

            if (_mode == 0)
            {
                double step = w / n;
                double barW = Math.Max(4.0, step * 0.62);
                for (int i = 0; i < n; i++)
                {
                    double val = _analyzer.Bands[i];
                    double bh = Math.Max(2.0, val * maxBarH);
                    double x = i * step + (step - barW) * 0.5;
                    double y = bottomY - bh;

                    dc.DrawRoundedRectangle(_glowBrushes[i], null, new Rect(x - 2, y - 3, barW + 4, bh + 5), 4, 4);
                    dc.DrawRoundedRectangle(_barBrushes[i], null, new Rect(x, y, barW, bh), 3, 3);

                    double py = bottomY - _analyzer.Peaks[i] * maxBarH - 4;
                    dc.DrawRoundedRectangle(_peakBrush, null, new Rect(x, py, barW, 2.2), 1, 1);
                }
            }
            else if (_mode == 1)
            {
                // Symmetric Mirror Spectrum around horizontal center
                double cx = w * 0.5;
                double halfStep = (w * 0.48) / n;
                double barW = Math.Max(3.0, halfStep * 0.62);
                for (int i = 0; i < n; i++)
                {
                    double val = _analyzer.Bands[i];
                    double bh = Math.Max(2.0, val * maxBarH);
                    double dx = i * halfStep + 3;
                    double y = bottomY - bh;
                    dc.DrawRoundedRectangle(_barBrushes[i], null, new Rect(cx + dx, y, barW, bh), 2.5, 2.5);
                    dc.DrawRoundedRectangle(_barBrushes[i], null, new Rect(cx - dx - barW, y, barW, bh), 2.5, 2.5);
                }
            }
            else
            {
                // Neon Smooth Wave
                var geo = new StreamGeometry();
                using (var ctx = geo.Open())
                {
                    double step = w / (n - 1);
                    ctx.BeginFigure(new Point(0, bottomY), true, true);
                    for (int i = 0; i < n; i++)
                    {
                        double x = i * step;
                        double y = bottomY - _analyzer.Bands[i] * maxBarH;
                        ctx.LineTo(new Point(x, y), true, true);
                    }
                    ctx.LineTo(new Point(w, bottomY), false, false);
                }
                geo.Freeze();
                dc.DrawGeometry(_waveFillBrush, _wavePen, geo);
            }
        }
    }

    internal class VisualizerWindow : Window
    {
        private readonly LoopbackAnalyzer _analyzer;
        private readonly VisualizerCanvas _canvas;

        public VisualizerWindow()
        {
            Title = "YASB Audio Visualizer";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = false;
            ShowInTaskbar = false;
            ShowActivated = false;
            Focusable = false;
            IsHitTestVisible = false;
            ResizeMode = ResizeMode.NoResize;

            var wa = SystemParameters.WorkArea;
            Width = Math.Min(1100, wa.Width * 0.72);
            Height = 200;
            Left = wa.Left + (wa.Width - Width) * 0.5;
            Top = wa.Bottom - Height - 14;

            _analyzer = new LoopbackAnalyzer();
            _canvas = new VisualizerCanvas(_analyzer);
            Content = _canvas;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                int exStyle = Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE).ToInt32();
                exStyle |= Native.WS_EX_TRANSPARENT | Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
                Native.SetWindowLongPtr(hwnd, Native.GWL_EXSTYLE, new IntPtr(exStyle));
                Native.SetWindowPos(hwnd, Native.HWND_BOTTOM, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);

                var src = HwndSource.FromHwnd(hwnd);
                if (src != null) src.AddHook(WndProc);
            }
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == Native.WM_MOUSEACTIVATE)
            {
                handled = true;
                return new IntPtr(Native.MA_NOACTIVATE);
            }
            if (msg == Native.WM_WINDOWPOSCHANGING)
            {
                var wp = (Native.WINDOWPOS)Marshal.PtrToStructure(lParam, typeof(Native.WINDOWPOS));
                wp.hwndInsertAfter = Native.HWND_BOTTOM;
                Marshal.StructureToPtr(wp, lParam, true);
            }
            return IntPtr.Zero;
        }

        public void ShowOverlay()
        {
            var wa = SystemParameters.WorkArea;
            Width = Math.Min(1100, wa.Width * 0.72);
            Height = 200;
            Left = wa.Left + (wa.Width - Width) * 0.5;
            Top = wa.Bottom - Height - 14;

            Show();
            _canvas.Start();

            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
                Native.SetWindowPos(hwnd, Native.HWND_BOTTOM, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }

        public void HideOverlay()
        {
            _canvas.Stop();
            Hide();
        }

        public void ToggleOverlay()
        {
            if (IsVisible) HideOverlay();
            else ShowOverlay();
        }

        public void CycleMode()
        {
            if (!IsVisible) ShowOverlay();
            else _canvas.CycleMode();
        }
    }
}
