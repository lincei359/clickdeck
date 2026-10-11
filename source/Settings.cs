using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace ClickDeck
{
    public sealed class SettingsForm : Form
    {
        private Preferences draft;
        private readonly Action<Preferences> apply;
        private ListView actions;
        private TextBox labelBox, shortcutBox, snippetBox;
        private ComboBox kindBox;
        private NumericUpDown columns;
        private CheckBox compact, showHints;
        private TrackBar opacity;
        private Label opacityLabel, errorLabel, fieldLabel, hintLabel;
        private Button record, remove;
        private bool binding, capturing;
        private ActionSpec selected;
        private readonly float scale;
        private int U(int n) { return (int)Math.Round(n * scale); }

        public SettingsForm(Preferences prefs, Action<Preferences> apply, bool hotkeyAvailable = true)
        {
            draft = prefs; this.apply = apply;
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) scale = g.DpiX / 96f;
            Text = "点点快捷 · 编辑按钮与外观"; BackColor = Theme.Paper; Font = Theme.Font(9.5f, false);
            AutoScaleMode = AutoScaleMode.None; FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false; MaximizeBox = false; StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            ClientSize = new Size(U(746), Math.Min(U(655), Screen.PrimaryScreen.WorkingArea.Height - U(65)));
            AutoScroll = true; AutoScrollMinSize = new Size(U(746), U(655));
            Label title = LabelAt("把常用操作，留在手边。", 26, 22, 690, 34); title.Font = Theme.Font(18f, true); title.ForeColor = Theme.Ink;
            LabelAt("勾选决定显示哪些按钮；选中一项，在右侧自由编辑。", 28, 66, 690, 24).ForeColor = Theme.Muted;

            LabelAt("屏幕上的按钮", 28, 108, 260, 25).Font = Theme.Font(10f, true);
            LabelAt("按钮内容", 333, 108, 380, 25).Font = Theme.Font(10f, true);
            actions = new ListView { CheckBoxes = true, View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false,
                HeaderStyle = ColumnHeaderStyle.None, BorderStyle = BorderStyle.FixedSingle, BackColor = Color.White, ForeColor = Theme.Ink,
                Bounds = new Rectangle(U(28), U(143), U(276), U(265)) };
            actions.Columns.Add("名称", U(125)); actions.Columns.Add("操作", U(124)); Controls.Add(actions);
            actions.SelectedIndexChanged += (s, e) => { if (!binding) SelectAction(); };
            actions.ItemChecked += (s, e) => { if (!binding && e.Item.Tag != null) ((ActionSpec)e.Item.Tag).Enabled = e.Item.Checked; };
            ButtonAt("全选", 28, 416, 61, 30, (s, e) => SetAll(true));
            ButtonAt("全不选", 97, 416, 76, 30, (s, e) => SetAll(false));
            ButtonAt("只留选中", 181, 416, 123, 30, (s, e) => { if (selected != null) { foreach (ActionSpec a in draft.Actions) a.Enabled = a == selected; Reload(selected.Id); } });
            ButtonAt("＋ 添加", 28, 454, 83, 32, (s, e) => AddAction());
            ButtonAt("↑", 119, 454, 45, 32, (s, e) => MoveAction(-1));
            ButtonAt("↓", 172, 454, 45, 32, (s, e) => MoveAction(1));
            remove = ButtonAt("删除", 225, 454, 79, 32, (s, e) => RemoveAction());

            LabelAt("显示名称", 334, 143, 365, 22);
            labelBox = TextAt(334, 171, 382, 31, false); labelBox.MaxLength = 24;
            LabelAt("操作类型", 334, 216, 360, 22);
            kindBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Bounds = new Rectangle(U(334), U(244), U(382), U(32)), Font = Font };
            kindBox.Items.AddRange(new object[] { "快捷键", "输入文字" }); Controls.Add(kindBox);
            fieldLabel = LabelAt("快捷键", 334, 290, 220, 24);
            record = ButtonAt("录制按键", 606, 286, 110, 29, (s, e) => ToggleCapture());
            shortcutBox = TextAt(334, 320, 382, 32, false);
            snippetBox = TextAt(334, 320, 382, 128, true); snippetBox.MaxLength = 4000; snippetBox.AcceptsReturn = true; snippetBox.AcceptsTab = true; snippetBox.ScrollBars = ScrollBars.Vertical;
            hintLabel = LabelAt("例如 Ctrl+C、Shift+Enter、Alt+Tab、Win+D\n支持字母、数字、F1–F24、方向键等。\nWin 组合键请直接输入。", 334, 361, 383, 74); hintLabel.ForeColor = Theme.Muted;
            errorLabel = LabelAt("", 334, 455, 383, 39); errorLabel.ForeColor = Color.FromArgb(170, 57, 41);
            labelBox.TextChanged += UpdateSelected; shortcutBox.TextChanged += UpdateSelected; snippetBox.TextChanged += UpdateSelected;
            kindBox.SelectedIndexChanged += (s, e) => { UpdateSelected(s, e); UpdateKind(); };

            Panel line = new Panel { BackColor = Theme.Line, Bounds = new Rectangle(U(28), U(503), U(688), U(1)) }; Controls.Add(line);
            LabelAt("外观", 28, 517, 90, 25).Font = Theme.Font(10f, true);
            LabelAt("每行", 110, 517, 43, 25);
            columns = new NumericUpDown { Minimum = 1, Maximum = 6, Value = draft.Columns, Bounds = new Rectangle(U(155), U(516), U(55), U(30)) }; Controls.Add(columns);
            LabelAt("个按钮", 218, 517, 77, 25);
            compact = new CheckBox { Text = "紧凑尺寸", Checked = draft.Compact, ForeColor = Theme.Ink, Bounds = new Rectangle(U(328), U(516), U(145), U(28)) }; Controls.Add(compact);
            showHints = new CheckBox { Text = "显示快捷键提示", Checked = draft.ShowHints, ForeColor = Theme.Ink, Bounds = new Rectangle(U(503), U(516), U(210), U(28)) }; Controls.Add(showHints);
            LabelAt("透明度", 28, 554, 79, 27);
            opacity = new TrackBar { Minimum = 45, Maximum = 100, TickStyle = TickStyle.None, Value = draft.OpacityPercent, Bounds = new Rectangle(U(100), U(551), U(180), U(34)) }; Controls.Add(opacity);
            opacityLabel = LabelAt(draft.OpacityPercent + "%", 282, 554, 63, 27);
            opacity.ValueChanged += (s, e) => opacityLabel.Text = opacity.Value + "%";
            Label hotkey = LabelAt(hotkeyAvailable ? "显示 / 隐藏：Ctrl+Alt+Space" : "显示 / 隐藏：右下角系统托盘", 363, 554, 350, 27); hotkey.ForeColor = Theme.Muted;

            ButtonAt("恢复默认", 28, 602, 110, 34, (s, e) => RestoreDefaults());
            Button cancel = ButtonAt("取消", 500, 602, 92, 34, (s, e) => Close()); CancelButton = cancel;
            Button save = ButtonAt("保存并应用", 606, 602, 110, 34, (s, e) => ApplyDraft());
            save.BackColor = Theme.Teal; save.ForeColor = Color.White; save.FlatAppearance.BorderColor = Theme.Teal; AcceptButton = save;
            Reload(draft.Actions.Count > 0 ? draft.Actions[0].Id : null);
        }
        private Label LabelAt(string text, int x, int y, int w, int h)
        {
            Label label = new Label { Text = text, ForeColor = Theme.Ink, BackColor = Color.Transparent, Bounds = new Rectangle(U(x), U(y), U(w), U(h)), AutoSize = false };
            Controls.Add(label); return label;
        }
        private TextBox TextAt(int x, int y, int w, int h, bool multiline)
        {
            TextBox box = new TextBox { Multiline = multiline, BorderStyle = BorderStyle.FixedSingle, ForeColor = Theme.Ink, BackColor = Color.White, Bounds = new Rectangle(U(x), U(y), U(w), U(h)), Font = Theme.Font(10f, false) };
            Controls.Add(box); return box;
        }
        private Button ButtonAt(string text, int x, int y, int w, int h, EventHandler click)
        {
            Button button = new Button { Text = text, Bounds = new Rectangle(U(x), U(y), U(w), U(h)), FlatStyle = FlatStyle.Flat, BackColor = Color.White, ForeColor = Theme.Ink, Cursor = Cursors.Hand, UseVisualStyleBackColor = false };
            button.FlatAppearance.BorderColor = Theme.Line; button.Click += click; Controls.Add(button); return button;
        }
        private void Reload(string selectedId)
        {
            capturing = false; record.Text = "录制按键"; binding = true;
            actions.BeginUpdate(); actions.Items.Clear();
            foreach (ActionSpec a in draft.Actions)
            {
                ListViewItem item = new ListViewItem(new[] { a.Label, a.Kind == "text" ? "输入文字" : a.Shortcut });
                item.Tag = a; item.Checked = a.Enabled; actions.Items.Add(item);
                if (a.Id == selectedId) { item.Selected = true; item.Focused = true; }
            }
            actions.EndUpdate(); binding = false; SelectAction();
            if (actions.SelectedItems.Count > 0) actions.SelectedItems[0].EnsureVisible();
        }
        private void SelectAction()
        {
            capturing = false; record.Text = "录制按键";
            selected = actions.SelectedItems.Count == 0 ? null : (ActionSpec)actions.SelectedItems[0].Tag;
            binding = true;
            labelBox.Text = selected == null ? "" : selected.Label;
            kindBox.SelectedIndex = selected != null && selected.Kind == "text" ? 1 : 0;
            shortcutBox.Text = selected == null ? "" : selected.Shortcut;
            snippetBox.Text = selected == null ? "" : selected.Text;
            labelBox.Enabled = shortcutBox.Enabled = snippetBox.Enabled = kindBox.Enabled = record.Enabled = remove.Enabled = selected != null;
            binding = false; errorLabel.Text = ""; UpdateKind();
        }
        private void UpdateKind()
        {
            bool text = kindBox.SelectedIndex == 1;
            shortcutBox.Visible = !text; snippetBox.Visible = text; record.Visible = !text;
            fieldLabel.Text = text ? "要输入的文字" : "快捷键";
            hintLabel.Visible = !text;
            if (text) { capturing = false; record.Text = "录制按键"; }
        }
        private void UpdateSelected(object sender, EventArgs e)
        {
            if (binding || selected == null) return;
            selected.Label = labelBox.Text; selected.Shortcut = shortcutBox.Text;
            selected.Text = snippetBox.Text; selected.Kind = kindBox.SelectedIndex == 1 ? "text" : "shortcut";
            foreach (ListViewItem item in actions.Items)
                if (item.Tag == selected) { item.Text = selected.Label; item.SubItems[1].Text = selected.Kind == "text" ? "输入文字" : selected.Shortcut; break; }
            errorLabel.Text = "";
        }
        private void SetAll(bool enabled) { foreach (ActionSpec a in draft.Actions) a.Enabled = enabled; Reload(selected == null ? null : selected.Id); }
        private void AddAction()
        {
            if (draft.Actions.Count >= 80) { errorLabel.Text = "最多支持 80 个按钮"; return; }
            ActionSpec a = ActionSpec.Key("新按钮", "Ctrl+C", true); draft.Actions.Add(a); Reload(a.Id); labelBox.Focus(); labelBox.SelectAll();
        }
        private void RemoveAction()
        {
            if (selected == null) return; int index = draft.Actions.IndexOf(selected); draft.Actions.Remove(selected);
            Reload(draft.Actions.Count == 0 ? null : draft.Actions[Math.Min(index, draft.Actions.Count - 1)].Id);
        }
        private void MoveAction(int delta)
        {
            if (selected == null) return; int index = draft.Actions.IndexOf(selected), next = index + delta;
            if (next < 0 || next >= draft.Actions.Count) return;
            draft.Actions.RemoveAt(index); draft.Actions.Insert(next, selected); Reload(selected.Id);
        }
        private void RestoreDefaults()
        {
            Preferences p = Preferences.Defaults(); p.HasPosition = draft.HasPosition; p.X = draft.X; p.Y = draft.Y; draft = p;
            columns.Value = p.Columns; compact.Checked = p.Compact; showHints.Checked = p.ShowHints; opacity.Value = p.OpacityPercent;
            Reload(p.Actions[0].Id);
        }
        private void ToggleCapture()
        {
            capturing = !capturing; record.Text = capturing ? "按下组合键…" : "录制按键";
            if (capturing) { shortcutBox.Focus(); errorLabel.Text = "按下想使用的组合键；单独按 Esc 取消录制。"; }
        }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (capturing)
            {
                Keys key = keyData & Keys.KeyCode;
                if (keyData == Keys.Escape) { capturing = false; record.Text = "录制按键"; errorLabel.Text = ""; return true; }
                if (key == Keys.ControlKey || key == Keys.ShiftKey || key == Keys.Menu || key == Keys.LWin || key == Keys.RWin) return true;
                string combo = ((keyData & Keys.Control) != 0 ? "Ctrl+" : "") + ((keyData & Keys.Alt) != 0 ? "Alt+" : "") + ((keyData & Keys.Shift) != 0 ? "Shift+" : "") + key;
                Chord chord; string error;
                if (Chord.TryParse(combo, out chord, out error)) { shortcutBox.Text = chord.Display; errorLabel.Text = ""; }
                else errorLabel.Text = error;
                capturing = false; record.Text = "录制按键"; return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
        private void ApplyDraft()
        {
            foreach (ActionSpec a in draft.Actions)
            {
                string error = null;
                if (String.IsNullOrWhiteSpace(a.Label)) error = "按钮名称不能为空";
                else if (a.Kind == "text" && String.IsNullOrEmpty(a.Text)) error = "请输入该按钮要输出的文字";
                else if (a.Kind == "shortcut")
                {
                    Chord chord;
                    if (Chord.TryParse(a.Shortcut, out chord, out error)) a.Shortcut = chord.Display;
                }
                if (error != null) { Reload(a.Id); errorLabel.Text = error; return; }
            }
            draft.Columns = (int)columns.Value; draft.Compact = compact.Checked; draft.ShowHints = showHints.Checked; draft.OpacityPercent = opacity.Value;
            apply(draft.Copy()); Close();
        }
    }
}
