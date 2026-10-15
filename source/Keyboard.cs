using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace ClickDeck
{
    public delegate bool KeyboardActionHandler(ActionSpec action, out string error);

    public sealed class FloatingKeyboardForm : Form
    {
        private readonly bool alphabet;
        private readonly KeyboardActionHandler send;
        private readonly Action hide, savePosition;
        private readonly List<ActionTile> keys = new List<ActionTile>();
        private readonly ToolTip tips = new ToolTip();
        private readonly Timer capsTimer = new Timer { Interval = 250 };
        private readonly float scale;
        private bool compact, dragging, caps;
        private Point dragCursor, dragOrigin;
        private ActionTile closeButton, capsButton;
        private string status;
        public IList<ActionTile> Keys { get { return keys.AsReadOnly(); } }
        private int U(int n) { return (int)Math.Round(n * scale); }

        public FloatingKeyboardForm(bool alphabet, KeyboardActionHandler send, Action hide, Action savePosition)
        {
            this.alphabet = alphabet; this.send = send; this.hide = hide; this.savePosition = savePosition;
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) scale = g.DpiX / 96f;
            Text = alphabet ? "点点快捷 · 字母键盘" : "点点快捷 · 数字键盘";
            BackColor = Theme.Paper; Font = Theme.Font(9f, false); FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false; TopMost = true; StartPosition = FormStartPosition.Manual; AutoScaleMode = AutoScaleMode.None;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            status = alphabet ? "英文 / 拼音跟随当前输入法" : "0–9 · 小数点 · 退格";
            capsTimer.Tick += (s, e) => RefreshCaps();
            MouseDown += (s, e) => { if (e.Button == MouseButtons.Left && e.Y < U(42)) { dragging = true; dragCursor = Cursor.Position; dragOrigin = Location; Capture = true; } };
            MouseMove += (s, e) => { if (dragging) { Point p = Cursor.Position; Location = new Point(dragOrigin.X + p.X - dragCursor.X, dragOrigin.Y + p.Y - dragCursor.Y); } };
            MouseUp += (s, e) => { if (dragging) { dragging = false; Capture = false; KeepOnScreen(); savePosition(); } };
            Build();
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        { get { CreateParams p = base.CreateParams; p.ExStyle |= Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW; p.ClassStyle |= 0x20000; return p; } }
        protected override void WndProc(ref Message m)
        { if (m.Msg == Native.WM_MOUSEACTIVATE) { m.Result = (IntPtr)Native.MA_NOACTIVATE; return; } base.WndProc(ref m); }
        protected override void OnVisibleChanged(EventArgs e)
        { base.OnVisibleChanged(e); if (Visible && alphabet) { RefreshCaps(); capsTimer.Start(); } else capsTimer.Stop(); }
        public void SetAppearance(bool compact, int opacity)
        { if (this.compact != compact) { this.compact = compact; Build(); KeepOnScreen(); } Opacity = opacity / 100.0; }
        public void KeepOnScreen()
        {
            Rectangle area = Screen.FromRectangle(Bounds).WorkingArea;
            Location = new Point(Math.Max(area.Left, Math.Min(Left, area.Right - Width)), Math.Max(area.Top, Math.Min(Top, area.Bottom - Height)));
        }
        private void Build()
        {
            SuspendLayout();
            foreach (ActionTile key in keys) key.Dispose(); keys.Clear();
            if (closeButton != null) closeButton.Dispose();
            int height = compact ? 32 : 38, step = height + 5;
            if (alphabet)
            {
                ClientSize = new Size(U(400), U(46 + step * 4 + 27));
                string[] rows = { "QWERTYUIOP", "ASDFGHJKL", "ZXCVBNM" };
                for (int r = 0; r < rows.Length; r++)
                    for (int c = 0; c < rows[r].Length; c++)
                    {
                        string letter = rows[r][c].ToString();
                        AddKey(letter.ToLowerInvariant(), letter, 12 + (r == 1 ? 19 : r == 2 ? 57 : 0) + c * 38, 46 + r * step, 34, height);
                    }
                capsButton = AddKey("大写", "CapsLock", 15, 46 + 2 * step, 50, height);
                AddKey("退格", "Backspace", 335, 46 + 2 * step, 50, height);
                ActionTile ime = AddKey("中/英", "", 12, 46 + 3 * step, 70, height); ime.Action.Kind = "ime-toggle";
                tips.SetToolTip(ime, "发送 Shift，切换当前拼音输入法的中英文模式");
                ActionTile picker = AddKey("输入法", "Win+Space", 86, 46 + 3 * step, 70, height);
                tips.SetToolTip(picker, "切换系统输入法（Win+Space）");
                ActionTile space = AddKey("空格 / 选词", "Space", 160, 46 + 3 * step, 152, height);
                tips.SetToolTip(space, "英文输入空格；拼音输入选择当前候选词");
                AddKey("回车", "Enter", 316, 46 + 3 * step, 72, height);
                RefreshCaps();
            }
            else
            {
                int nh = compact ? 36 : 44, ns = nh + 6;
                ClientSize = new Size(U(204), U(46 + ns * 5 + 8));
                string[] digits = { "789", "456", "123", ".0" };
                for (int r = 0; r < digits.Length; r++)
                    for (int c = 0; c < digits[r].Length; c++)
                    {
                        string digit = digits[r][c].ToString();
                        AddKey(digit, digit == "." ? "OemPeriod" : digit, 12 + c * 62, 46 + r * ns, 56, nh);
                    }
                AddKey("退格", "Backspace", 136, 46 + 3 * ns, 56, nh);
                AddKey("空格", "Space", 12, 46 + 4 * ns, 87, nh);
                AddKey("回车", "Enter", 105, 46 + 4 * ns, 87, nh);
            }
            closeButton = new ActionTile(null) { Small = true, Symbol = "×", Bounds = new Rectangle(Width - U(36), U(10), U(26), U(26)) };
            closeButton.Click += (s, e) => hide(); tips.SetToolTip(closeButton, "隐藏此键盘；可在设置或系统托盘重新开启"); Controls.Add(closeButton);
            Region previous = Region;
            using (GraphicsPath p = Theme.Round(ClientRectangle, U(13))) Region = new Region(p);
            if (previous != null) previous.Dispose();
            ResumeLayout(); Invalidate();
        }
        private ActionTile AddKey(string label, string shortcut, int x, int y, int width, int height)
        {
            ActionTile tile = new ActionTile(ActionSpec.Key(label, shortcut, true)) { ShowHint = false, Bounds = new Rectangle(U(x), U(y), U(width), U(height)) };
            tile.Click += (s, e) =>
            {
                string error;
                if (send(tile.Action, out error)) { tile.Flash(); status = alphabet ? "英文 / 拼音跟随当前输入法" : "0–9 · 小数点 · 退格"; }
                else { status = error; tips.Show(error, this, U(12), Height - U(15), 3500); }
                RefreshCaps(); Invalidate();
            };
            Controls.Add(tile); keys.Add(tile); return tile;
        }
        private void RefreshCaps()
        {
            caps = Control.IsKeyLocked(System.Windows.Forms.Keys.CapsLock);
            foreach (ActionTile key in keys)
            {
                if (key.Action.Shortcut.Length == 1 && Char.IsLetter(key.Action.Shortcut[0]))
                { key.Action.Label = caps ? key.Action.Shortcut : key.Action.Shortcut.ToLowerInvariant(); key.Invalidate(); }
            }
            if (capsButton != null) { capsButton.Latched = caps; capsButton.Invalidate(); }
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); Graphics g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            using (Pen pen = new Pen(Theme.Line))
            using (GraphicsPath p = Theme.Round(new Rectangle(0, 0, Width - 1, Height - 1), U(13))) g.DrawPath(pen, p);
            using (SolidBrush brush = new SolidBrush(Theme.Teal)) g.FillEllipse(brush, U(14), U(21), U(6), U(6));
            using (Font title = Theme.Font(9f, true)) TextRenderer.DrawText(g, alphabet ? "字母键盘" : "数字键盘", title, new Rectangle(U(26), U(10), Width - U(70), U(27)), Theme.Ink, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            if (alphabet) using (Font hint = Theme.Font(8f, false)) TextRenderer.DrawText(g, status, hint, new Rectangle(U(12), Height - U(28), Width - U(24), U(23)), Theme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
        protected override void Dispose(bool disposing)
        { if (disposing) { capsTimer.Dispose(); tips.Dispose(); } base.Dispose(disposing); }
    }
}
