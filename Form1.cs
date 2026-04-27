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
    
    // --- 新增：用于一键摸鱼的显示/隐藏 API ---
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    private static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_LAYERED = 0x80000;
    private const int WS_EX_TRANSPARENT = 0x20;
    private const int LWA_ALPHA = 0x2;
    private const int WM_HOTKEY = 0x0312;
    private const int WH_MOUSE_LL = 14;
    private const int WM_MOUSEWHEEL = 0x020A;
    
    // 指令常量
    private const int SW_HIDE = 0;
    private const int SW_SHOW = 5;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT { public Point pt; public int mouseData; public int flags; public int time; public IntPtr dwExtraInfo; }

    // ================= 核心状态 =================
    private IntPtr pinnedHwnd = IntPtr.Zero;
    private RECT pinnedRect;
    private byte currentOpacity = 255;
    private bool isBossHidden = false; // 摸鱼状态标识
    
    private BorderForm? borderForm;
    private OsdForm? osdForm; 
    private System.Windows.Forms.Timer lockTimer;
    private CheckBox chkClickThrough;

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);
    private LowLevelMouseProc _mouseProc;
    private IntPtr _mouseHookID = IntPtr.Zero;

    public Form1()
    {
        InitializeComponent();
        this.Text = "ScreenNail ";
        this.Size = new Size(300, 220);
        this.TopMost = true;

        chkClickThrough = new CheckBox { Text = "开启鼠标穿透 (Ghost Mode)", Location = new Point(20, 20), Width = 200 };
        chkClickThrough.CheckedChanged += (s, e) => ApplyWindowStyles();
        this.Controls.Add(chkClickThrough);

        Label lblHelp = new Label { 
            Text = "锁定/解锁: Ctrl + Shift + P\n一键摸鱼: Ctrl + Shift + H\n调节透明度: Alt + 鼠标滚轮", 
            Location = new Point(20, 60), Width = 250, Height = 80 
        };
        this.Controls.Add(lblHelp);

        // 注册快捷键
        RegisterHotKey(this.Handle, 1, 6, 0x50); // Ctrl+Shift+P (Pin)
        RegisterHotKey(this.Handle, 2, 6, 0x48); // Ctrl+Shift+H (Hide/Boss)

        lockTimer = new System.Windows.Forms.Timer { Interval = 10 };
        lockTimer.Tick += LockTimer_Tick;
        lockTimer.Start();

        osdForm = new OsdForm(); 

        _mouseProc = HookCallback;
        using (Process curProcess = Process.GetCurrentProcess())
        using (ProcessModule curModule = curProcess.MainModule!)
        {
            _mouseHookID = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, GetModuleHandle(curModule.ModuleName), 0);
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY)
        {
            int id = m.WParam.ToInt32();
            if (id == 1) TogglePin();
            if (id == 2) ToggleBossMode(); // 处理老板键
        }
        base.WndProc(ref m);
    }

    // --- 一键摸鱼核心逻辑 ---
    private void ToggleBossMode()
    {
        if (pinnedHwnd == IntPtr.Zero || !IsWindow(pinnedHwnd)) return;

        if (!isBossHidden)
        {
            // 开启摸鱼：隐藏窗口和边框
            ShowWindow(pinnedHwnd, SW_HIDE);
            borderForm?.Hide();
            isBossHidden = true;
        }
        else
        {
            // 停止摸鱼：恢复显示
            ShowWindow(pinnedHwnd, SW_SHOW);
            borderForm?.Show();
            isBossHidden = false;
            // 确保窗口回到最顶层
            ApplyWindowStyles();
        }
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
            isBossHidden = false; // 重置摸鱼状态
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
            // 如果在摸鱼状态下解锁，必须强制显示窗口
            if (isBossHidden) ShowWindow(pinnedHwnd, SW_SHOW);

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
        isBossHidden = false;
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

            // 如果处于摸鱼模式，跳过位置同步和边框更新
            if (isBossHidden) return;

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
        if (borderForm != null && pinnedHwnd != IntPtr.Zero && !isBossHidden)
        {
            borderForm.Bounds = new Rectangle(pinnedRect.Left - 5, pinnedRect.Top - 5, (pinnedRect.Right - pinnedRect.Left) + 10, (pinnedRect.Bottom - pinnedRect.Top) + 10);
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam == (IntPtr)WM_MOUSEWHEEL && pinnedHwnd != IntPtr.Zero && !isBossHidden)
        {
            if ((Control.ModifierKeys & Keys.Alt) == Keys.Alt)
            {
                MSLLHOOKSTRUCT hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                int delta = (short)((hookStruct.mouseData >> 16) & 0xFFFF);
                
                int newOpacity = currentOpacity + (delta > 0 ? 15 : -15);
                currentOpacity = (byte)Math.Max(15, Math.Min(255, newOpacity)); 
                ApplyWindowStyles();

                int percent = (int)Math.Round((currentOpacity / 255.0) * 100);
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
        UnlockCurrent(); // 确保退出时恢复所有窗口状态
        UnregisterHotKey(this.Handle, 1);
        UnregisterHotKey(this.Handle, 2);
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
        this.BackColor = Color.Magenta;
        this.TransparencyKey = Color.Magenta; 
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; 

        Rectangle rect = new Rectangle(4, 4, this.Width - 9, this.Height - 9);
        using (LinearGradientBrush brush = new LinearGradientBrush(rect, Color.FromArgb(0, 212, 255), Color.FromArgb(255, 0, 128), 45f))
        using (Pen pen = new Pen(brush, 6)) 
        {
            pen.LineJoin = LineJoin.Round;
            using (GraphicsPath path = GetRoundedRect(rect, 12))
            {
                e.Graphics.DrawPath(pen, path);
            }
        }
    }

    private GraphicsPath GetRoundedRect(Rectangle bounds, int radius)
    {
        int diameter = radius * 2;
        Size size = new Size(diameter, diameter);
        Rectangle arc = new Rectangle(bounds.Location, size);
        GraphicsPath path = new GraphicsPath();
        if (radius == 0) { path.AddRectangle(bounds); return path; }
        path.AddArc(arc, 180, 90); 
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90); 
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90); 
        arc.X = bounds.Left;
        path.AddArc(arc, 90, 90); 
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
        this.Opacity = 0.8; 
        this.Size = new Size(160, 50);
        
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

        hideTimer = new System.Windows.Forms.Timer { Interval = 1500 };
        hideTimer.Tick += (s, e) => {
            this.Hide();
            hideTimer.Stop();
        };
    }

    public void ShowOsd(int percent, int centerX, int cy)
    {
        lblText.Text = $"透明度: {percent}%";
        this.Location = new Point(centerX - this.Width / 2, cy - this.Height / 2);
        this.Show();
        hideTimer.Stop();
        hideTimer.Start();
    }
    
    protected override bool ShowWithoutActivation => true;
}