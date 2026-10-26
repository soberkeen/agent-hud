// hdr-toggle.cs — HDR 开关悬浮小窗（原生 WPF 版，与 hdr-toggle.ps1 行为对齐）
// 用系统自带 csc.exe (.NET Framework 4.x, 仅支持 C#5 语法) 编译，见 build.ps1；Win10/11 零依赖运行
// 用法:
//   hdr-toggle.exe              启动悬浮小窗（单实例）
//   hdr-toggle.exe -state       打印 HDR 状态（退出码 0=ON 1=OFF 2=错误）
//   hdr-toggle.exe -toggle      切换一次（退出码=切换后状态: 0=ON 1=OFF 2=错误）
//   hdr-toggle.exe -on / -off   强制开/关（退出码 0=成功 2=失败）
//   hdr-toggle.exe -quit        关闭运行中的小窗（0=已关闭 1=未找到）
// 交互: 点击切换；拖动移动；拖近屏幕边缘自动吸附并半隐藏（悬停弹出，移开缩回）；右键退出
// 注: winexe 无控制台，CLI 输出经 AttachConsole 写回调用方终端；脚本自动化请用退出码

using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Ellipse = System.Windows.Shapes.Ellipse;

[assembly: AssemblyTitle("HDR Toggle")]
[assembly: AssemblyProduct("HDR 开关")]
[assembly: AssemblyDescription("一键开关主显示器 HDR")]
[assembly: AssemblyCompany("HDR Toggle Project")]
[assembly: AssemblyCopyright("Copyright (C) 2026 HDR Toggle Project")]
[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]

namespace HDRToggle
{
    // ---------- HDR CCD API（与 ps1 版 HdrCtl 语义一致，勿改判定逻辑） ----------
    public static class HdrCtl
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct LUID { public uint LowPart; public int HighPart; }
        [StructLayout(LayoutKind.Sequential)]
        public struct DISPLAYCONFIG_RATIONAL { public uint Numerator; public uint Denominator; }
        [StructLayout(LayoutKind.Sequential)]
        public struct DISPLAYCONFIG_PATH_SOURCE_INFO
        {
            public LUID adapterId; public uint id; public uint modeInfoIdx; public uint statusAndFlags;
        }
        [StructLayout(LayoutKind.Sequential)]
        public struct DISPLAYCONFIG_PATH_TARGET_INFO
        {
            public LUID adapterId; public uint id; public uint modeInfoIdx;
            public uint technology; public uint rotation; public uint scaling;
            public DISPLAYCONFIG_RATIONAL refreshRate; public uint scanLineOrdering;
            public uint targetAvailable; public uint statusAndFlags;
        }
        [StructLayout(LayoutKind.Sequential)]
        public struct DISPLAYCONFIG_PATH_INFO
        {
            public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo;
            public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo;
            public uint flags;
        }
        [StructLayout(LayoutKind.Sequential)]
        public struct DISPLAYCONFIG_DEVICE_INFO_HEADER { public uint type; public uint size; public LUID adapterId; public uint id; }
        [StructLayout(LayoutKind.Sequential)]
        public struct DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO
        {
            public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
            public uint value; public uint colorEncoding; public uint bitsPerPixelFormat;
        }
        [StructLayout(LayoutKind.Sequential)]
        public struct DISPLAYCONFIG_SDR_WHITE_LEVEL
        {
            public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
            public uint SDRWhiteLevel;
        }
        [StructLayout(LayoutKind.Sequential)]
        public struct DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE
        {
            public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
            public uint value;
        }

        [DllImport("user32.dll")] static extern int GetDisplayConfigBufferSizes(uint flags, out int pc, out int mc);
        [DllImport("user32.dll")] static extern int QueryDisplayConfig(uint flags, ref int pc, [In, Out] DISPLAYCONFIG_PATH_INFO[] paths, ref int mc, IntPtr modes, IntPtr topo);
        [DllImport("user32.dll")] static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO req);
        [DllImport("user32.dll")] static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_SDR_WHITE_LEVEL req);
        [DllImport("user32.dll")] static extern int DisplayConfigSetDeviceInfo(ref DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE req);

        private static bool FindPrimary(out DISPLAYCONFIG_PATH_INFO primary)
        {
            primary = new DISPLAYCONFIG_PATH_INFO();
            int pc, mc;
            // 注意: QDC_DATABASE_CURRENT(2) 不能与其他 QDC_* 组合，否则返回 87
            if (GetDisplayConfigBufferSizes(2, out pc, out mc) != 0) return false;
            DISPLAYCONFIG_PATH_INFO[] paths = new DISPLAYCONFIG_PATH_INFO[pc];
            IntPtr buf = Marshal.AllocHGlobal(mc * 64);
            try
            {
                int pn = pc, mn = mc;
                if (QueryDisplayConfig(2, ref pn, paths, ref mn, buf, IntPtr.Zero) != 0) return false;
                for (int i = 0; i < pn; i++)
                {
                    if ((paths[i].sourceInfo.statusAndFlags & 0x1) != 0) { primary = paths[i]; return true; }
                }
                return false;
            }
            finally { Marshal.FreeHGlobal(buf); }
        }

        // 返回: -2 无主显 path, -1 API 调用失败, 1 HDR 关, 2 HDR 开
        // 本机(雷神 P275MS PLUS @240Hz)驱动 quirk: 开=0x3(enabled+supported)；关=0x1——
        // enabled 位切关后滞留为 1 不清零，单独不可信。0x1 时用 SDR 白电平仲裁:
        // HDR 关时恒为 1000，开时随「SDR 内容亮度」抬高。唯一误判窗口: HDR 开且滑块拉到最低。
        public static int GetHdrState()
        {
            DISPLAYCONFIG_PATH_INFO p;
            if (!FindPrimary(out p)) return -2;
            DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO info = new DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO();
            info.header.type = 9;   // DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO（不是 12！）
            info.header.size = (uint)Marshal.SizeOf(typeof(DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO));
            info.header.adapterId = p.targetInfo.adapterId;
            info.header.id = p.targetInfo.id;
            if (DisplayConfigGetDeviceInfo(ref info) != 0) return -1;
            int v = (int)info.value;
            if ((v & 0x3) == 0x3) return 2;
            if ((v & 0x1) != 0) return GetSdrWhite() > 1000 ? 2 : 1;
            return 1;
        }

        // SDR 白电平（1/1000 × 80nit；HDR 关=1000，开=随 SDR 内容亮度抬高）
        public static int GetSdrWhite()
        {
            DISPLAYCONFIG_PATH_INFO p;
            if (!FindPrimary(out p)) return -2;
            DISPLAYCONFIG_SDR_WHITE_LEVEL info = new DISPLAYCONFIG_SDR_WHITE_LEVEL();
            info.header.type = 11;  // DISPLAYCONFIG_DEVICE_INFO_GET_SDR_WHITE_LEVEL
            info.header.size = (uint)Marshal.SizeOf(typeof(DISPLAYCONFIG_SDR_WHITE_LEVEL));
            info.header.adapterId = p.targetInfo.adapterId;
            info.header.id = p.targetInfo.id;
            if (DisplayConfigGetDeviceInfo(ref info) != 0) return -1;
            return (int)info.SDRWhiteLevel;
        }

        // 返回 0 = 成功
        public static int SetHdr(bool enable)
        {
            DISPLAYCONFIG_PATH_INFO p;
            if (!FindPrimary(out p)) return -2;
            DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE pkt = new DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE();
            pkt.header.type = 10;   // DISPLAYCONFIG_DEVICE_INFO_SET_ADVANCED_COLOR_STATE（不是 13！）
            pkt.header.size = (uint)Marshal.SizeOf(typeof(DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE));
            pkt.header.adapterId = p.targetInfo.adapterId;
            pkt.header.id = p.targetInfo.id;
            pkt.value = enable ? 1u : 0u;
            return DisplayConfigSetDeviceInfo(ref pkt);
        }

        // 返回切换后的实测状态: 1(关) / 2(开)，失败或异常返回原状态码
        public static int ToggleHdr()
        {
            int s = GetHdrState();
            if (s == 2) return SetHdr(false) == 0 ? GetHdrState() : s;
            if (s == 1) return SetHdr(true) == 0 ? GetHdrState() : s;
            return s;
        }

        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lp);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lp);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder sb, int max);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wp, IntPtr lp);

        // 关闭正在运行的小窗（标题 HDR Toggle 的可见窗口发 WM_CLOSE）
        public static bool CloseRunningWidget()
        {
            bool found = false;
            EnumWindows(delegate(IntPtr h, IntPtr lp)
            {
                if (IsWindowVisible(h))
                {
                    StringBuilder sb = new StringBuilder(256);
                    GetWindowText(h, sb, 256);
                    if (sb.ToString() == "HDR Toggle") { PostMessage(h, 0x0010, IntPtr.Zero, IntPtr.Zero); found = true; return false; }
                }
                return true;
            }, IntPtr.Zero);
            return found;
        }
    }

    public static class Program
    {
        private static Mutex _mutex;

        [STAThread]
        public static int Main(string[] args)
        {
            try
            {
                // 只把 -/ 开头的参数当 CLI 命令；其余杂散参数（如快捷方式残留）忽略，直接进 GUI
                string first = args.Length > 0 ? args[0] : "";
                if (first.StartsWith("-") || first.StartsWith("/")) return RunCli(first.TrimStart('-', '/').ToLowerInvariant());
                return RunGui();
            }
            catch (Exception ex)
            {
                try { MessageBox(IntPtr.Zero, ex.ToString(), "HDR Toggle", 0x10); } catch { }
                return 3;
            }
        }

        static int RunCli(string arg)
        {
            AttachConsoleOut();
            switch (arg)
            {
                case "state":
                    {
                        int s = HdrCtl.GetHdrState();
                        Out(StateText(s));
                        return s == 2 ? 0 : (s == 1 ? 1 : 2);
                    }
                case "toggle":
                    {
                        int s = HdrCtl.ToggleHdr();
                        Out(StateText(s));
                        return s == 2 ? 0 : (s == 1 ? 1 : 2);
                    }
                case "on":
                case "off":
                    {
                        int r = HdrCtl.SetHdr(arg == "on");
                        Out(r == 0 ? "OK" : "ERROR(" + r + ")");
                        return r == 0 ? 0 : 2;
                    }
                case "quit":
                    {
                        bool ok = HdrCtl.CloseRunningWidget();
                        Out(ok ? "QUIT_OK" : "QUIT_NONE");
                        return ok ? 0 : 1;
                    }
                default:
                    Out("usage: hdr-toggle.exe [-state|-toggle|-on|-off|-quit]");
                    return 9;
            }
        }

        static string StateText(int s)
        {
            if (s == 2) return "ON";
            if (s == 1) return "OFF";
            if (s == -2) return "NO_PRIMARY_DISPLAY";
            return "ERROR(" + s + ")";
        }

        static void Out(string s) { try { Console.WriteLine(s); } catch { } }

        // winexe 默认无控制台：挂到父进程终端后重接 stdout，让 CLI 输出可见
        [DllImport("kernel32.dll")] private static extern bool AttachConsole(int processId);
        [DllImport("user32.dll")] private static extern int MessageBox(IntPtr hWnd, string text, string caption, int type);

        static void AttachConsoleOut()
        {
            try
            {
                if (AttachConsole(-1))
                {
                    StreamWriter w = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
                    Console.SetOut(w);
                }
            }
            catch { }
        }

        static int RunGui()
        {
            _mutex = new Mutex(false, "Local\\HdrToggleWidget");   // 单实例
            if (!_mutex.WaitOne(0)) return 0;
            Widget w = new Widget();
            w.Start();
            return 0;
        }
    }

    // ---------- 悬浮小窗 ----------
    public class Widget : Window
    {
        private const double Sliver = 12;   // 半隐藏时保留可见的宽度
        private const double SnapPx = 28;   // 距边缘多少像素内判定为吸附

        private static readonly string PosFile = Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "hdr-toggle.pos");

        private readonly Ellipse _circle;
        private readonly TextBlock _label;
        private readonly Grid _root;
        private readonly DispatcherTimer _pollTimer;
        private readonly DispatcherTimer _tuckTimer;

        private string _dock = "";       // "" | left | right | top | bottom
        private bool _tucked;
        private int _lastState = -99;
        private DateTime _downAt;

        public Widget()
        {
            Title = "HDR Toggle";
            Width = 64; Height = 64;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;   // 禁用系统贴边分屏放大（Aero Snap）
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;

            _circle = new Ellipse { Fill = Brush(0xE0, 0x33, 0x33, 0x33), Stroke = Brush(0x66, 0xFF, 0xFF, 0xFF), StrokeThickness = 1 };
            _label = new TextBlock
            {
                Text = "HDR",
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Brush(0xFF, 0xAA, 0xAA, 0xAA)
            };
            _root = new Grid { Cursor = Cursors.Hand };
            _root.Children.Add(_circle);
            _root.Children.Add(_label);
            Content = _root;

            ContextMenu menu = new ContextMenu();
            MenuItem exit = new MenuItem { Header = "退出" };
            exit.Click += delegate { Close(); };
            menu.Items.Add(exit);
            ContextMenu = menu;

            try   // 恢复上次位置（含贴边半隐藏坐标）
            {
                if (File.Exists(PosFile))
                {
                    string[] p = File.ReadAllText(PosFile).Trim().Split(',');
                    Left = double.Parse(p[0]);
                    Top = double.Parse(p[1]);
                }
            }
            catch { }

            _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _pollTimer.Tick += delegate { UpdateVisual(); };

            _tuckTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            _tuckTimer.Tick += delegate
            {
                _tuckTimer.Stop();
                if (_dock.Length > 0 && !_tucked && !_root.IsMouseOver) SetDockState(_dock, true, true);
            };

            MouseEnter += delegate
            {
                if (_dock.Length > 0)
                {
                    _tuckTimer.Stop();
                    if (_tucked) SetDockState(_dock, false, true);   // 悬停弹出
                }
            };
            MouseLeave += delegate
            {
                if (_dock.Length > 0 && !_tucked) { _tuckTimer.Stop(); _tuckTimer.Start(); }   // 移开延迟缩回
            };
            MouseLeftButtonDown += OnDown;
            StateChanged += delegate
            {
                if (WindowState != WindowState.Normal) WindowState = WindowState.Normal;   // 兜底弹回
            };
            Closed += delegate
            {
                _pollTimer.Stop();
                try { File.WriteAllText(PosFile, ((int)Left).ToString() + "," + ((int)Top).ToString()); } catch { }
            };
        }

        public void Start()
        {
            UpdateVisual();
            _pollTimer.Start();
            UpdateDock(false);   // 上次关闭时若贴边，启动即恢复半隐藏
            ShowDialog();
        }

        private void UpdateVisual()
        {
            int s = HdrCtl.GetHdrState();
            if (s == _lastState) return;
            _lastState = s;
            if (s == 2) { _circle.Fill = Brush(0xE0, 0x17, 0x7B, 0x4D); _label.Foreground = Brush(0xFF, 0x7C, 0xFC, 0x9E); _label.Text = "HDR"; }
            else if (s == 1) { _circle.Fill = Brush(0xE0, 0x33, 0x33, 0x33); _label.Foreground = Brush(0xFF, 0xAA, 0xAA, 0xAA); _label.Text = "HDR"; }
            else { _circle.Fill = Brush(0xE0, 0x6B, 0x4A, 0x00); _label.Foreground = Brush(0xFF, 0xFF, 0xD2, 0x4A); _label.Text = "?"; }
        }

        private static SolidColorBrush Brush(byte a, byte r, byte g, byte b)
        {
            SolidColorBrush b2 = new SolidColorBrush(Color.FromArgb(a, r, g, b));
            b2.Freeze();
            return b2;
        }

        // ---------- 边缘吸附 + 半隐藏 ----------
        // 释放 Left/Top 上的 HoldEnd 动画并把当前值固化为基础值，避免回跳/与 DragMove 打架
        private void StopPosAnim()
        {
            DependencyProperty lp = Window.LeftProperty;
            DependencyProperty tp = Window.TopProperty;
            double x = Left, y = Top;
            BeginAnimation(lp, null);
            BeginAnimation(tp, null);
            Left = x; Top = y;
        }

        private void MoveWidgetTo(double x, double y, bool animate)
        {
            if (!animate) { Left = x; Top = y; return; }
            Duration dur = new Duration(TimeSpan.FromMilliseconds(180));
            CubicEase ease = new CubicEase();
            ease.EasingMode = EasingMode.EaseOut;
            DoubleAnimation ax = new DoubleAnimation(Left, x, dur);
            DoubleAnimation ay = new DoubleAnimation(Top, y, dur);
            ax.EasingFunction = ease; ay.EasingFunction = ease;
            BeginAnimation(Window.LeftProperty, ax);
            BeginAnimation(Window.TopProperty, ay);
        }

        // dock 边上的目标坐标：hidden=只露 sliver；revealed=完全贴边可见；另一轴夹在工作区内
        private Point GetDockPos(string edge, bool hidden)
        {
            Rect wa = SystemParameters.WorkArea;
            double w = Width, h = Height;
            double cx = Math.Max(wa.Left, Math.Min(Left, wa.Right - w));
            double cy = Math.Max(wa.Top, Math.Min(Top, wa.Bottom - h));
            double s = hidden ? Sliver : Math.Max(w, h);
            switch (edge)
            {
                case "left": return new Point(wa.Left + s - w, cy);
                case "right": return new Point(wa.Right - s, cy);
                case "top": return new Point(cx, wa.Top + s - h);
                default: return new Point(cx, wa.Bottom - s);
            }
        }

        private void SetDockState(string edge, bool hidden, bool animate)
        {
            _dock = edge; _tucked = hidden;
            Point p = GetDockPos(edge, hidden);
            MoveWidgetTo(p.X, p.Y, animate);
        }

        // 按当前窗口位置评估吸附：与某边缘的有符号距离 < SnapPx（负值=已越出边缘）即吸附并半隐藏
        private void UpdateDock(bool animate)
        {
            Rect wa = SystemParameters.WorkArea;
            double L = Left, T = Top, w = Width, h = Height;
            string best = ""; double bestV = double.MaxValue;
            if (L - wa.Left < SnapPx && L - wa.Left < bestV) { best = "left"; bestV = L - wa.Left; }
            if (wa.Right - (L + w) < SnapPx && wa.Right - (L + w) < bestV) { best = "right"; bestV = wa.Right - (L + w); }
            if (T - wa.Top < SnapPx && T - wa.Top < bestV) { best = "top"; bestV = T - wa.Top; }
            if (wa.Bottom - (T + h) < SnapPx && wa.Bottom - (T + h) < bestV) { best = "bottom"; bestV = wa.Bottom - (T + h); }
            if (best.Length == 0) { _dock = ""; _tucked = false; }
            else SetDockState(best, true, animate);
        }

        // 拖动 vs 点击：DragMove() 的模态循环会吞掉 MouseLeftButtonUp，
        // 只能在 DragMove 返回后比较窗口位置——位置未变（且按下 <1.5s）视为点击
        private void OnDown(object sender, MouseButtonEventArgs e)
        {
            StopPosAnim();
            _downAt = DateTime.Now;
            double preX = Left, preY = Top;
            try { DragMove(); } catch { }
            double moved = Math.Abs(Left - preX) + Math.Abs(Top - preY);
            if (moved < 4 && (DateTime.Now - _downAt).TotalMilliseconds < 1500)
            {
                if (_tucked) { SetDockState(_dock, false, true); return; }   // 半隐藏时第一击只唤醒
                HdrCtl.ToggleHdr();
                Thread.Sleep(800);   // 等 Windows 完成模式切换
                UpdateVisual();
            }
            else
            {
                UpdateDock(true);    // 拖放结束：贴近边缘则吸附+半隐藏
            }
        }
    }
}
