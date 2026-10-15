using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace ClickDeck
{
    public static class Theme
    {
        public static readonly Color Paper = Color.FromArgb(250, 251, 249);
        public static readonly Color Ink = Color.FromArgb(31, 49, 45);
        public static readonly Color Muted = Color.FromArgb(111, 126, 121);
        public static readonly Color Teal = Color.FromArgb(12, 116, 98);
        public static readonly Color Pale = Color.FromArgb(227, 242, 234);
        public static readonly Color Line = Color.FromArgb(219, 225, 219);
        public static Font Font(float size, bool bold) { return new Font("Microsoft YaHei UI", size, bold ? FontStyle.Bold : FontStyle.Regular); }
        public static GraphicsPath Round(Rectangle r, int radius)
        {
            GraphicsPath p = new GraphicsPath(); int d = radius * 2;
            p.AddArc(r.Left, r.Top, d, d, 180, 90); p.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.Left, r.Bottom - d, d, d, 90, 90); p.CloseFigure(); return p;
        }
    }

    public sealed class ActionTile : Control
    {
        public readonly ActionSpec Action;
        public bool ShowHint = true;
        public bool Small;
        public string Symbol;
        public bool Latched;
        private bool hover, pressed, success;
        private Timer flash;
        public ActionTile(ActionSpec action)
        {
            Action = action; Cursor = Cursors.Hand; TabStop = false;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            SetStyle(ControlStyles.Selectable, false);
            Font = Theme.Font(10.5f, true); BackColor = Theme.Paper;
            MouseEnter += (s, e) => { hover = true; Invalidate(); };
            MouseLeave += (s, e) => { hover = false; pressed = false; Invalidate(); };
            MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) { pressed = true; Invalidate(); } };
            MouseUp += (s, e) => { pressed = false; Invalidate(); };
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_MOUSEACTIVATE) { m.Result = (IntPtr)Native.MA_NOACTIVATE; return; }
            base.WndProc(ref m);
        }
        public void Flash()
        {
            success = true; Invalidate();
            if (flash == null) { flash = new Timer { Interval = 240 }; flash.Tick += (s, e) => { flash.Stop(); success = false; Invalidate(); }; }
            flash.Stop(); flash.Start();
        }
        protected override void Dispose(bool disposing)
        { if (disposing && flash != null) flash.Dispose(); base.Dispose(disposing); }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); Graphics g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            bool copy = Action != null && Action.Shortcut == "Ctrl+C" && Action.Kind == "shortcut";
            Color fill = Small ? (hover ? Theme.Pale : Theme.Paper) : success || pressed || Latched ? Theme.Teal : copy ? Theme.Pale : hover ? Color.FromArgb(237, 241, 235) : Color.White;
            Color ink = success || pressed || Latched ? Color.White : Theme.Ink;
            Rectangle box = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath p = Theme.Round(box, Math.Max(4, (int)(Height * .13))))
            using (SolidBrush brush = new SolidBrush(fill))
            using (Pen pen = new Pen(Small ? fill : copy ? Color.FromArgb(190, 220, 207) : Theme.Line)) { g.FillPath(brush, p); g.DrawPath(pen, p); }
            if (Small)
            {
                TextRenderer.DrawText(g, Symbol, Font, box, Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                return;
            }
            int split = ShowHint ? (int)(Height * .51) : Height;
            TextRenderer.DrawText(g, Action.Label, Font, new Rectangle(4, ShowHint ? 4 : 0, Width - 8, split), ink,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if (ShowHint)
            {
                using (Font hint = Theme.Font(8f, false))
                    TextRenderer.DrawText(g, Action.Kind == "text" ? "输入文字" : Action.Shortcut, hint,
                        new Rectangle(3, split, Width - 6, Height - split - 4), success || pressed ? Color.FromArgb(218, 241, 232) : Theme.Muted,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }
        }
    }

    public sealed class ToolbarForm : Form
    {
        public Preferences Preferences;
        private readonly PreferenceStore store;
        private readonly bool trayEnabled;
        private readonly float scale;
        private readonly Panel body = new Panel();
        private readonly ToolTip toolTip = new ToolTip();
        private readonly Timer statusTimer = new Timer { Interval = 2300 };
        private readonly List<ActionTile> tiles = new List<ActionTile>();
        private ActionTile settingsButton, hideButton;
        private NotifyIcon tray;
        private Icon trayIcon;
        private ContextMenuStrip trayMenu;
        private bool hotkeyRegistered;
        private string status = "点击即执行";
        private bool statusError;
        private bool dragging;
        private Point dragCursor, dragOrigin;
        private SettingsForm settings;
        private Label emptyLabel;
        private bool closing;
        private FloatingKeyboardForm numericKeyboard, alphabetKeyboard;
        public FloatingKeyboardForm NumericKeyboard { get { return numericKeyboard; } }
        public FloatingKeyboardForm AlphabetKeyboard { get { return alphabetKeyboard; } }
        public IList<ActionTile> Tiles { get { return tiles.AsReadOnly(); } }
        public string LastStatus { get { return status; } }
        private int U(int n) { return (int)Math.Round(n * scale); }

        public ToolbarForm(Preferences prefs, PreferenceStore store, bool enableTray)
        {
            Preferences = prefs; this.store = store; trayEnabled = enableTray;
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) scale = g.DpiX / 96f;
            Text = "点点快捷"; FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true;
            BackColor = Theme.Paper; Font = Theme.Font(9f, false); AutoScaleMode = AutoScaleMode.None;
            StartPosition = FormStartPosition.Manual;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            body.BackColor = Theme.Paper; body.AutoScroll = true; body.TabStop = false; Controls.Add(body);
            statusTimer.Tick += (s, e) => { statusTimer.Stop(); status = "点击即执行"; statusError = false; Invalidate(); };
            MouseDown += BeginDrag; MouseMove += ContinueDrag; MouseUp += EndDrag;
            Build();
            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            Location = prefs.HasPosition ? new Point(prefs.X, prefs.Y) : new Point(area.Right - Width - U(28), area.Top + U(120));
            KeepOnScreen();
            if (trayEnabled) SetupTray();
            if (!String.IsNullOrEmpty(store.LoadNotice)) Shown += (s, e) => MessageBox.Show(store.LoadNotice, "点点快捷");
        }

        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get { CreateParams p = base.CreateParams; p.ExStyle |= Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW; p.ClassStyle |= 0x20000; return p; }
        }
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.SetProp(Handle, Native.ToolbarProperty, (IntPtr)1);
            if (trayEnabled) hotkeyRegistered = Native.RegisterHotKey(Handle, 1, 0x4003, (uint)Keys.Space);
        }
        protected override void OnHandleDestroyed(EventArgs e)
        {
            if (hotkeyRegistered) { Native.UnregisterHotKey(Handle, 1); hotkeyRegistered = false; }
            Native.RemoveProp(Handle, Native.ToolbarProperty);
            base.OnHandleDestroyed(e);
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_MOUSEACTIVATE) { m.Result = (IntPtr)Native.MA_NOACTIVATE; return; }
            if (m.Msg == Native.WM_HOTKEY && m.WParam == (IntPtr)1) { ToggleVisible(); return; }
            if ((uint)m.Msg == Native.ShowToolbarMessage && Native.ShowToolbarMessage != 0) { Show(); KeepOnScreen(); return; }
            base.WndProc(ref m);
        }
        private void SetupTray()
        {
            string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ClickDeck.ico");
            trayIcon = File.Exists(iconPath) ? new Icon(iconPath) : (Icon)SystemIcons.Application.Clone();
            Icon = trayIcon;
            trayMenu = new ContextMenuStrip();
            trayMenu.Items.Add("显示 / 隐藏", null, (s, e) => ToggleVisible());
            trayMenu.Items.Add("编辑按钮与外观…", null, (s, e) => OpenSettings());
            ToolStripMenuItem numericItem = new ToolStripMenuItem("显示数字键盘") { CheckOnClick = true };
            ToolStripMenuItem alphabetItem = new ToolStripMenuItem("显示字母键盘") { CheckOnClick = true };
            numericItem.Click += (s, e) => SetKeyboardVisible(false, numericItem.Checked);
            alphabetItem.Click += (s, e) => SetKeyboardVisible(true, alphabetItem.Checked);
            trayMenu.Items.Add(numericItem); trayMenu.Items.Add(alphabetItem);
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add("退出点点快捷", null, (s, e) => Close());
            tray = new NotifyIcon { Icon = trayIcon, Text = "点点快捷 · 鼠标点一下", ContextMenuStrip = trayMenu, Visible = true };
            tray.DoubleClick += (s, e) => { Show(); KeepOnScreen(); };
            trayMenu.Opened += (s, e) => { trayMenu.Items[0].Text = Visible ? "隐藏悬浮栏" : "显示悬浮栏"; numericItem.Checked = Preferences.ShowNumericKeyboard; alphabetItem.Checked = Preferences.ShowAlphabetKeyboard; };
        }
        public void ToggleVisible() { if (Visible) Hide(); else { Show(); KeepOnScreen(); } }
        public void ApplyPreferences(Preferences prefs)
        { CaptureKeyboardPositions(prefs); Preferences = prefs; Build(); KeepOnScreen(); SyncKeyboards(); Save(); }
        protected override void OnVisibleChanged(EventArgs e)
        { base.OnVisibleChanged(e); if (IsHandleCreated && !closing) SyncKeyboards(); }
        public void SetKeyboardVisible(bool alphabet, bool visible)
        {
            if (alphabet) Preferences.ShowAlphabetKeyboard = visible; else Preferences.ShowNumericKeyboard = visible;
            if (visible && !Visible) Show();
            SyncKeyboards(); Save();
        }
        private void CaptureKeyboardPositions(Preferences p)
        {
            if (numericKeyboard != null && !numericKeyboard.IsDisposed) { p.HasNumericPosition = true; p.NumericX = numericKeyboard.Left; p.NumericY = numericKeyboard.Top; }
            if (alphabetKeyboard != null && !alphabetKeyboard.IsDisposed) { p.HasAlphabetPosition = true; p.AlphabetX = alphabetKeyboard.Left; p.AlphabetY = alphabetKeyboard.Top; }
        }
        private void SyncKeyboards()
        {
            if (closing) return;
            SyncKeyboard(false, ref numericKeyboard, Preferences.ShowNumericKeyboard);
            SyncKeyboard(true, ref alphabetKeyboard, Preferences.ShowAlphabetKeyboard);
        }
        private void SyncKeyboard(bool alphabet, ref FloatingKeyboardForm form, bool enabled)
        {
            if (enabled && form == null)
            {
                form = new FloatingKeyboardForm(alphabet, ExecuteAction, () => SetKeyboardVisible(alphabet, false), () => Save());
                bool positioned = alphabet ? Preferences.HasAlphabetPosition : Preferences.HasNumericPosition;
                form.Location = positioned ? new Point(alphabet ? Preferences.AlphabetX : Preferences.NumericX, alphabet ? Preferences.AlphabetY : Preferences.NumericY) :
                    alphabet ? new Point(Right - form.Width, Math.Max(Bottom, numericKeyboard != null && Preferences.ShowNumericKeyboard ? numericKeyboard.Bottom : Bottom) + U(12)) : new Point(Left - form.Width - U(12), Top);
                form.KeepOnScreen();
            }
            if (form == null) return;
            form.SetAppearance(Preferences.Compact, Preferences.OpacityPercent);
            if (enabled && Visible) { form.Show(); form.KeepOnScreen(); } else form.Hide();
        }
        private void Build()
        {
            body.SuspendLayout();
            foreach (ActionTile tile in tiles) { body.Controls.Remove(tile); tile.Dispose(); }
            tiles.Clear();
            if (emptyLabel != null) { emptyLabel.Dispose(); emptyLabel = null; }
            if (settingsButton != null) { settingsButton.Dispose(); hideButton.Dispose(); }
            List<ActionSpec> actions = Preferences.Actions.Where(a => a.Enabled).ToList();
            int columns = Math.Min(Preferences.Columns, Math.Max(1, actions.Count));
            int tileW = Preferences.Compact ? 90 : 104;
            int tileH = Preferences.ShowHints ? (Preferences.Compact ? 52 : 64) : (Preferences.Compact ? 38 : 46);
            int rows = (int)Math.Ceiling(actions.Count / (double)columns);
            int width = Math.Max(148, columns * (tileW + 8) + 16);
            int naturalHeight = Math.Max(48, rows * (tileH + 8));
            int maxHeight = Math.Max(U(110), Screen.FromPoint(Location).WorkingArea.Height - U(130));
            int bodyHeight = Math.Min(U(naturalHeight), maxHeight);
            bool scroll = bodyHeight < U(naturalHeight);
            ClientSize = new Size(U(width) + (scroll ? SystemInformation.VerticalScrollBarWidth : 0), U(46 + 32) + bodyHeight);
            body.Bounds = new Rectangle(U(12), U(46), ClientSize.Width - U(24), bodyHeight);
            body.AutoScrollMinSize = new Size(0, U(rows * (tileH + 8)));
            for (int i = 0; i < actions.Count; i++)
            {
                ActionTile tile = new ActionTile(actions[i]) { ShowHint = Preferences.ShowHints };
                tile.Bounds = new Rectangle(U((i % columns) * (tileW + 8)), U((i / columns) * (tileH + 8)), U(tileW), U(tileH));
                tile.Click += (s, e) => Execute(tile);
                toolTip.SetToolTip(tile, actions[i].Kind == "text" ? actions[i].Text : actions[i].Label + " · " + actions[i].Shortcut);
                body.Controls.Add(tile); tiles.Add(tile);
            }
            if (actions.Count == 0)
            {
                emptyLabel = new Label { Text = "点击 ··· 添加按钮", TextAlign = ContentAlignment.MiddleCenter, ForeColor = Theme.Muted, Dock = DockStyle.Fill };
                body.Controls.Add(emptyLabel);
            }
            settingsButton = new ActionTile(null) { Small = true, Symbol = "···", Bounds = new Rectangle(ClientSize.Width - U(68), U(10), U(28), U(26)) };
            hideButton = new ActionTile(null) { Small = true, Symbol = "−", Bounds = new Rectangle(ClientSize.Width - U(36), U(10), U(26), U(26)) };
            settingsButton.Click += (s, e) => OpenSettings(); hideButton.Click += (s, e) => Hide();
            toolTip.SetToolTip(settingsButton, "编辑按钮与外观"); toolTip.SetToolTip(hideButton, "隐藏到系统托盘；双击程序可重新显示");
            Controls.Add(settingsButton); Controls.Add(hideButton);
            Opacity = Preferences.OpacityPercent / 100.0;
            Region old = Region;
            using (GraphicsPath p = Theme.Round(ClientRectangle, U(13))) Region = new Region(p);
            if (old != null) old.Dispose();
            body.ResumeLayout(); Invalidate();
        }
        public bool Execute(ActionTile tile)
        {
            string error;
            bool ok = ExecuteAction(tile.Action, out error);
            if (ok) tile.Flash();
            return ok;
        }
        private bool ExecuteAction(ActionSpec action, out string error)
        {
            if (settings != null && !settings.IsDisposed) { error = "请先关闭设置窗口"; SetStatus(error, true); return false; }
            bool ok = Native.SendAction(action, out error);
            SetStatus(ok ? "已执行 · " + action.Label : error, !ok); return ok;
        }
        private void SetStatus(string value, bool error)
        {
            status = value; statusError = error; toolTip.SetToolTip(this, value);
            statusTimer.Stop(); statusTimer.Interval = error ? 5000 : 2300; statusTimer.Start(); Invalidate();
        }
        private void Save()
        {
            Preferences.HasPosition = true; Preferences.X = Left; Preferences.Y = Top;
            CaptureKeyboardPositions(Preferences);
            try { store.Save(Preferences); }
            catch (Exception e) { if (!(e is IOException || e is UnauthorizedAccessException)) throw; SetStatus("设置未保存：" + e.Message, true); }
        }
        public void KeepOnScreen()
        {
            Rectangle area = Screen.FromRectangle(Bounds).WorkingArea;
            Location = new Point(Math.Max(area.Left, Math.Min(Left, area.Right - Width)), Math.Max(area.Top, Math.Min(Top, area.Bottom - Height)));
        }
        private void BeginDrag(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right) { OpenSettings(); return; }
            if (e.Button != MouseButtons.Left || e.Y > U(44)) return;
            dragging = true; dragCursor = Cursor.Position; dragOrigin = Location; Capture = true;
        }
        private void ContinueDrag(object sender, MouseEventArgs e)
        {
            if (dragging) { Point cursor = Cursor.Position; Location = new Point(dragOrigin.X + cursor.X - dragCursor.X, dragOrigin.Y + cursor.Y - dragCursor.Y); }
        }
        private void EndDrag(object sender, MouseEventArgs e)
        { if (dragging) { dragging = false; Capture = false; KeepOnScreen(); Save(); } }
        public void OpenSettings()
        {
            if (settings != null && !settings.IsDisposed) { settings.Activate(); return; }
            IntPtr previous = Native.GetForegroundWindow();
            if (!Native.IsExternalWindow(previous)) previous = IntPtr.Zero;
            settings = new SettingsForm(Preferences.Copy(), p => ApplyPreferences(p), hotkeyRegistered);
            settings.FormClosed += (s, e) => { settings = null; if (Native.IsExternalWindow(previous)) Native.SetForegroundWindow(previous); };
            settings.Show(); settings.Activate();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); Graphics g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            using (Pen pen = new Pen(Theme.Line))
            using (GraphicsPath p = Theme.Round(new Rectangle(0, 0, Width - 1, Height - 1), U(13))) g.DrawPath(pen, p);
            using (SolidBrush brush = new SolidBrush(Theme.Teal)) g.FillEllipse(brush, U(14), U(21), U(6), U(6));
            using (Font title = Theme.Font(9f, true))
                TextRenderer.DrawText(g, Width < U(180) ? "快捷" : "点点快捷", title, new Rectangle(U(26), U(10), Width - U(98), U(27)), Theme.Ink, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            if (tiles.Count == 0) TextRenderer.DrawText(g, "点击 ··· 添加按钮", Font, body.Bounds, Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            using (Pen pen = new Pen(Theme.Line)) g.DrawLine(pen, U(12), Height - U(31), Width - U(12), Height - U(31));
            using (Font statusFont = Theme.Font(8f, false))
                TextRenderer.DrawText(g, status, statusFont, new Rectangle(U(13), Height - U(26), Width - U(26), U(20)), statusError ? Color.FromArgb(170, 57, 41) : Theme.Muted,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            closing = true;
            if (numericKeyboard != null) numericKeyboard.Hide(); if (alphabetKeyboard != null) alphabetKeyboard.Hide();
            if (settings != null) settings.Close();
            Save(); if (tray != null) tray.Visible = false; base.OnFormClosed(e);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { if (numericKeyboard != null) numericKeyboard.Dispose(); if (alphabetKeyboard != null) alphabetKeyboard.Dispose(); statusTimer.Dispose(); toolTip.Dispose(); if (tray != null) tray.Dispose(); if (trayMenu != null) trayMenu.Dispose(); if (trayIcon != null) trayIcon.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
