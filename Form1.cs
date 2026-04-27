using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ScreenNail;

public partial class Form1 : Form
{
    // ================= API 声明 =================
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")] private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")] private static extern IntPtr GetModuleHandle(string lpModuleName);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hWnd);

    private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    private static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_LAYERED = 0x80000;
    private const int WS_EX_TRANSPARENT = 0x20;
    private const int LWA_ALPHA = 0x2;
    private const int WM_HOTKEY = 0x0312;
    private const int WH_MOUSE_LL = 14;
    private const int WM_MOUSEWHEEL = 0x020A;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT { public Point pt; public int mouseData; public int flags; public int time; public IntPtr dwExtraInfo; }

    // ================= 核心状态 =================
    private IntPtr pinnedHwnd = IntPtr.Zero;
    private RECT pinnedRect;
    private byte currentOpacity = 255;
    
    private BorderForm? borderForm;
    private OsdForm? osdForm; // 透明度提示面板
    private System.Windows.Forms.Timer lockTimer;
    private CheckBox chkClickThrough;

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);
    private LowLevelMouseProc _mouseProc;
    private IntPtr _mouseHookID = IntPtr.Zero;

    public Form1()
    {
        InitializeComponent();
        this.Text = "ScreenNail ";
        this.Size = new Size(300, 200);
        this.TopMost = true;

        chkClickThrough = new CheckBox { Text = "开启鼠标穿透 (Ghost Mode)", Location = new Point(20, 20), Width = 200 };
        chkClickThrough.CheckedChanged += (s, e) => ApplyWindowStyles();
        this.Controls.Add(chkClickThrough);

        Label lblHelp = new Label { Text = "快捷键: Ctrl + Shift + P\n按住 Alt + 滚轮调节透明度", Location = new Point(20, 60), Width = 250, Height = 50 };
        this.Controls.Add(lblHelp);

        RegisterHotKey(this.Handle, 1, 6, 0x50);

        lockTimer = new System.Windows.Forms.Timer { Interval = 10 };
        lockTimer.Tick += LockTimer_Tick;
        lockTimer.Start();

        osdForm = new OsdForm(); // 初始化指示器

        _mouseProc = HookCallback;
        using (Process curProcess = Process.GetCurrentProcess())
        using (ProcessModule curModule = curProcess.MainModule!)
        {
            _mouseHookID = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, GetModuleHandle(curModule.ModuleName), 0);
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == 1) TogglePin();
        base.WndProc(ref m);
    }

    private void TogglePin()
    {
        IntPtr fg = GetForegroundWindow();
        if (fg == this.Handle) return;

        if (fg == pinnedHwnd)
        {
            UnlockCurrent();
            return;
        }

        if (pinnedHwnd != IntPtr.Zero) UnlockCurrent();

        if (fg != IntPtr.Zero)
        {
            pinnedHwnd = fg;
            GetWindowRect(pinnedHwnd, out pinnedRect);
            currentOpacity = 255;
            ApplyWindowStyles();

            borderForm = new BorderForm();
            UpdateBorderPosition();
            borderForm.Show();
        }
    }

    private void UnlockCurrent()
    {
        if (pinnedHwnd == IntPtr.Zero) return;
        
        if (IsWindow(pinnedHwnd))
        {
            SetWindowPos(pinnedHwnd, HWND_NOTOPMOST, 0, 0, 0, 0, 3);
            int style = GetWindowLong(pinnedHwnd, GWL_EXSTYLE);
            SetWindowLong(pinnedHwnd, GWL_EXSTYLE, style & ~WS_EX_LAYERED & ~WS_EX_TRANSPARENT);
            SetLayeredWindowAttributes(pinnedHwnd, 0, 255, LWA_ALPHA);
        }

        if (borderForm != null)
        {
            borderForm.Close();
            borderForm.Dispose();
            borderForm = null;
        }
        pinnedHwnd = IntPtr.Zero;
    }

    private void ApplyWindowStyles()
    {
        if (pinnedHwnd == IntPtr.Zero || !IsWindow(pinnedHwnd)) return;

        SetWindowPos(pinnedHwnd, HWND_TOPMOST, pinnedRect.Left, pinnedRect.Top, pinnedRect.Right - pinnedRect.Left, pinnedRect.Bottom - pinnedRect.Top, 0);

        int style = GetWindowLong(pinnedHwnd, GWL_EXSTYLE) | WS_EX_LAYERED;
        if (chkClickThrough.Checked) style |= WS_EX_TRANSPARENT; 
        else style &= ~WS_EX_TRANSPARENT; 

        SetWindowLong(pinnedHwnd, GWL_EXSTYLE, style);
        SetLayeredWindowAttributes(pinnedHwnd, 0, currentOpacity, LWA_ALPHA);
    }

    private void LockTimer_Tick(object? sender, EventArgs e)
    {
        if (pinnedHwnd != IntPtr.Zero)
        {
            if (!IsWindow(pinnedHwnd))
            {
                UnlockCurrent(); 
                return;
            }

            GetWindowRect(pinnedHwnd, out RECT curr);
            if (curr.Left != pinnedRect.Left || curr.Top != pinnedRect.Top || curr.Right != pinnedRect.Right || curr.Bottom != pinnedRect.Bottom)
            {
                SetWindowPos(pinnedHwnd, HWND_TOPMOST, pinnedRect.Left, pinnedRect.Top, pinnedRect.Right - pinnedRect.Left, pinnedRect.Bottom - pinnedRect.Top, 0);
            }
            UpdateBorderPosition();
        }
    }

    private void UpdateBorderPosition()
    {
        if (borderForm != null && pinnedHwnd != IntPtr.Zero)
        {
            borderForm.Bounds = new Rectangle(pinnedRect.Left - 5, pinnedRect.Top - 5, (pinnedRect.Right - pinnedRect.Left) + 10, (pinnedRect.Bottom - pinnedRect.Top) + 10);
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam == (IntPtr)WM_MOUSEWHEEL && pinnedHwnd != IntPtr.Zero)
        {
            if ((Control.ModifierKeys & Keys.Alt) == Keys.Alt)
            {
                MSLLHOOKSTRUCT hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                int delta = (short)((hookStruct.mouseData >> 16) & 0xFFFF);
                
                int newOpacity = currentOpacity + (delta > 0 ? 15 : -15);
                currentOpacity = (byte)Math.Max(15, Math.Min(255, newOpacity)); // 限制范围
                ApplyWindowStyles();

                // 计算百分比并显示指示器
                int percent = (int)Math.Round((currentOpacity / 255.0) * 100);
                
                // 在目标窗口的中央偏下位置显示OSD
                int cx = pinnedRect.Left + (pinnedRect.Right - pinnedRect.Left) / 2;
                int cy = pinnedRect.Top + (pinnedRect.Bottom - pinnedRect.Top) / 2;
                osdForm?.ShowOsd(percent, cx, cy);

                return (IntPtr)1;
            }
        }
        return CallNextHookEx(_mouseHookID, nCode, wParam, lParam);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        UnlockCurrent();
        UnregisterHotKey(this.Handle, 1);
        UnhookWindowsHookEx(_mouseHookID);
        base.OnFormClosing(e);
    }
}

// ================= 霓虹渐变 + 圆角边框 =================
public class BorderForm : Form
{
    public BorderForm()
    {
        this.FormBorderStyle = FormBorderStyle.None;
        this.ShowInTaskbar = false;
        this.TopMost = true;
        // 使用洋红色作为透明色，避免抗锯齿边缘产生难看的黑边
        this.BackColor = Color.Magenta;
        this.TransparencyKey = Color.Magenta; 
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; // 开启抗锯齿，使圆角平滑

        // 绘制霓虹渐变（从蓝到紫红）
        Rectangle rect = new Rectangle(4, 4, this.Width - 9, this.Height - 9);
        using (LinearGradientBrush brush = new LinearGradientBrush(rect, Color.FromArgb(0, 212, 255), Color.FromArgb(255, 0, 128), 45f))
        using (Pen pen = new Pen(brush, 6)) // 边框粗细
        {
            pen.LineJoin = LineJoin.Round;
            // 构造带圆角的路径 (Radius = 12 适配 Win11)
            using (GraphicsPath path = GetRoundedRect(rect, 12))
            {
                e.Graphics.DrawPath(pen, path);
            }
        }
    }

    // 辅助方法：生成圆角矩形路径
    private GraphicsPath GetRoundedRect(Rectangle bounds, int radius)
    {
        int diameter = radius * 2;
        Size size = new Size(diameter, diameter);
        Rectangle arc = new Rectangle(bounds.Location, size);
        GraphicsPath path = new GraphicsPath();

        if (radius == 0) { path.AddRectangle(bounds); return path; }

        path.AddArc(arc, 180, 90); // 左上角
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90); // 右上角
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90); // 右下角
        arc.X = bounds.Left;
        path.AddArc(arc, 90, 90); // 左下角
        path.CloseFigure();
        return path;
    }
}

// ================= OSD 透明度提示面板 =================
public class OsdForm : Form
{
    private Label lblText;
    private System.Windows.Forms.Timer hideTimer;

    public OsdForm()
    {
        this.FormBorderStyle = FormBorderStyle.None;
        this.ShowInTaskbar = false;
        this.TopMost = true;
        this.BackColor = Color.Black;
        this.Opacity = 0.8; // 面板自身的半透明
        this.Size = new Size(160, 50);
        
        // 让窗体圆角（使用简单的Region裁剪）
        GraphicsPath p = new GraphicsPath();
        p.AddArc(0, 0, 15, 15, 180, 90);
        p.AddArc(this.Width - 15, 0, 15, 15, 270, 90);
        p.AddArc(this.Width - 15, this.Height - 15, 15, 15, 0, 90);
        p.AddArc(0, this.Height - 15, 15, 15, 90, 90);
        p.CloseFigure();
        this.Region = new Region(p);

        lblText = new Label
        {
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 14, FontStyle.Bold),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter
        };
        this.Controls.Add(lblText);

        // 初始化隐藏定时器（1.5秒后隐藏）
        hideTimer = new System.Windows.Forms.Timer { Interval = 1500 };
        hideTimer.Tick += (s, e) => {
            this.Hide();
            hideTimer.Stop();
        };
    }

    // 外部调用：更新数值并显示
    public void ShowOsd(int percent, int centerX, int cy)
    {
        lblText.Text = $"透明度: {percent}%";
        // 居中显示
        this.Location = new Point(centerX - this.Width / 2, cy - this.Height / 2);
        this.Show();
        // 每次触发都重置隐藏倒计时
        hideTimer.Stop();
        hideTimer.Start();
    }
    
    // 让OSD面板不抢夺焦点（不可被激活）
    protected override bool ShowWithoutActivation => true;
}