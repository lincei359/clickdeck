using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using ClickDeck;

class TestRunner
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageW")]
    static extern IntPtr SetText(IntPtr h, int message, IntPtr w, string text);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageW")]
    static extern IntPtr GetText(IntPtr h, int message, IntPtr w, StringBuilder text);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out Point p);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int left, top, right, bottom; }
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int height, uint flags);
    static int checks;
    static ToolbarForm toolbar;
    static IntPtr target, editor;
    static string eventLog;

    static void Check(bool value, string name)
    { if (!value) throw new Exception("FAIL: " + name); checks++; Console.WriteLine("PASS " + name); }
    static void Pump(int ms)
    { Stopwatch watch = Stopwatch.StartNew(); while (watch.ElapsedMilliseconds < ms) { Application.DoEvents(); Thread.Sleep(10); } }
    static string ReadText()
    { StringBuilder value = new StringBuilder(8192); GetText(editor, 0xD, (IntPtr)value.Capacity, value); return value.ToString(); }
    static void WriteText(string value, int start, int end)
    { SetText(editor, 0xC, IntPtr.Zero, value); Native.SendMessage(editor, 0xB1, (IntPtr)start, (IntPtr)end); }
    static void FocusTarget()
    {
        Native.ShowWindow(target, 9); Native.SetForegroundWindow(target); Pump(120);
        SetWindowPos(target, new IntPtr(-1), 0, 0, 0, 0, 0x43);
        RECT r; GetWindowRect(editor, out r);
        MouseClick(new Point(r.left + 30, r.top + 30)); Pump(180);
        uint foregroundPid; Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out foregroundPid);
        Console.WriteLine("FOCUS target=" + target + " actual=" + Native.GetForegroundWindow() + " foreground process=" + Process.GetProcessById((int)foregroundPid).ProcessName);
        Check(Native.GetForegroundWindow() == target, "test target has foreground focus");
        SetWindowPos(toolbar.Handle, new IntPtr(-1), 0, 0, 0, 0, 0x13);
        if (toolbar.NumericKeyboard != null && toolbar.NumericKeyboard.Visible) SetWindowPos(toolbar.NumericKeyboard.Handle, new IntPtr(-1), 0, 0, 0, 0, 0x13);
        if (toolbar.AlphabetKeyboard != null && toolbar.AlphabetKeyboard.Visible) SetWindowPos(toolbar.AlphabetKeyboard.Handle, new IntPtr(-1), 0, 0, 0, 0, 0x13);
    }
    static void Click(string shortcut)
    {
        ActionTile tile = toolbar.Tiles.First(t => t.Action.Shortcut == shortcut && t.Action.Kind == "shortcut");
        ClickTile(tile);
    }
    static void ClickTile(ActionTile tile)
    {
        Point p = tile.PointToScreen(new Point(tile.Width / 2, tile.Height / 2));
        MouseClick(p);
        Pump(220);
        Check(Native.GetForegroundWindow() == target, "toolbar click preserves target focus");
    }
    static void MouseClick(Point p)
    {
        Native.SetCursorPos(p.X, p.Y);
        Native.INPUT[] inputs = {
            new Native.INPUT { type = 0, data = new Native.InputUnion { mouse = new Native.MOUSEINPUT { flags = 2 } } },
            new Native.INPUT { type = 0, data = new Native.InputUnion { mouse = new Native.MOUSEINPUT { flags = 4 } } }
        };
        Check(Native.SendInput(2, inputs, Marshal.SizeOf(typeof(Native.INPUT))) == 2, "mouse click dispatched");
    }
    static void ParserTests()
    {
        foreach (string text in new[] { "Ctrl+C", "Shift+Enter", "Ctrl+Shift+S", "Win+D", "Alt+Tab", "F24", "Ctrl+1", "Ctrl+Plus", "PageDown", "Backspace" })
        { Chord c; string e; Check(Chord.TryParse(text, out c, out e), "parse " + text); }
        foreach (string text in new[] { "", "Ctrl", "Ctrl+", "Ctrl+Ctrl+C", "A+B", "NoSuchKey", "Ctrl+42", "Ctrl+LButton", "Ctrl+RControlKey" })
        { Chord c; string e; Check(!Chord.TryParse(text, out c, out e), "reject " + text); }
        Check(Marshal.SizeOf(typeof(Native.INPUT)) == (IntPtr.Size == 8 ? 40 : 28), "Win32 INPUT structure layout");
    }
    static void ModelTests(string temp)
    {
        PreferenceStore store = new PreferenceStore(Path.Combine(temp, "settings.json"));
        Preferences prefs = Preferences.Defaults(); prefs.Actions.ForEach(a => a.Enabled = a.Shortcut == "Ctrl+C");
        prefs.Columns = 1; prefs.X = 123; prefs.Y = 234; prefs.HasPosition = true;
        prefs.Actions.Add(new ActionSpec { Label = "中文模板", Kind = "text", Text = "你好\n测试 😀", Enabled = false });
        store.Save(prefs); prefs.Y = 345; store.Save(prefs);
        Preferences loaded = store.Load();
        Check(loaded.Actions.Count(a => a.Enabled) == 1 && loaded.Y == 345 && loaded.Columns == 1, "settings atomically persist visibility, layout and position");
        Check(loaded.Actions.Last().Text == "你好\n测试 😀", "JSON persists Unicode snippets");
        Check(!loaded.ShowNumericKeyboard && !loaded.ShowAlphabetKeyboard, "legacy preferences keep both keyboards hidden");
        prefs.ShowNumericKeyboard = prefs.ShowAlphabetKeyboard = true; prefs.HasNumericPosition = prefs.HasAlphabetPosition = true;
        prefs.NumericX = 200; prefs.NumericY = 300; prefs.AlphabetX = 400; prefs.AlphabetY = 500; store.Save(prefs); loaded = store.Load();
        Check(loaded.ShowNumericKeyboard && loaded.ShowAlphabetKeyboard && loaded.NumericX == 200 && loaded.AlphabetY == 500, "keyboard visibility and independent positions persist");
        Check(!File.Exists(store.FilePath + ".tmp"), "atomic save removes temporary file");
        Preferences clone = prefs.Copy(); clone.Actions[0].Label = "changed";
        Check(prefs.Actions[0].Label == "复制", "cancelable settings use a deep copy");
        int existingBackups = Directory.GetFiles(temp, "settings.json.invalid-*").Length;
        File.WriteAllText(store.FilePath, "broken json");
        Check(store.Load().Actions.Count == 13 && store.LoadNotice != null, "corrupt settings recover to defaults");
        Check(Directory.GetFiles(temp, "settings.json.invalid-*").Length == existingBackups + 1, "original corrupt settings retained");
    }
    static Button Button(SettingsForm form, string text) { return form.Controls.OfType<Button>().Single(b => b.Text == text); }
    static void EditorTests(Preferences prefs)
    {
        toolbar.OpenSettings(); Pump(140);
        SettingsForm settings = Application.OpenForms.OfType<SettingsForm>().Single();
        Button(settings, "只留选中").PerformClick(); Button(settings, "保存并应用").PerformClick(); Pump(140);
        Check(toolbar.Tiles.Count == 1 && toolbar.Tiles[0].Action.Shortcut == "Ctrl+C", "settings UI saves only selected button");
        Check(Native.GetForegroundWindow() == target, "closing settings restores original target");
        toolbar.OpenSettings(); Pump(140); settings = Application.OpenForms.OfType<SettingsForm>().Single();
        Button(settings, "＋ 添加").PerformClick();
        TextBox name = settings.Controls.OfType<TextBox>().Single(t => t.MaxLength == 24);
        TextBox shortcut = settings.Controls.OfType<TextBox>().Single(t => !t.Multiline && t.MaxLength != 24);
        name.Text = "测试录制";
        MouseClick(settings.PointToScreen(new Point(60, 75))); Pump(80);
        Button(settings, "录制按键").PerformClick();
        Native.INPUT[] chord = { Native.Key(0x11, false), Native.Key(0x10, false), Native.Key(0x50, false), Native.Key(0x50, true), Native.Key(0x10, true), Native.Key(0x11, true) };
        Check(Native.SendInput((uint)chord.Length, chord, Marshal.SizeOf(typeof(Native.INPUT))) == chord.Length, "recorded shortcut input dispatched"); Pump(140);
        Check(shortcut.Text == "Ctrl+Shift+P", "settings UI records Ctrl+Shift+P (actual: " + shortcut.Text + ")");
        Button(settings, "保存并应用").PerformClick(); Pump(140);
        Check(toolbar.Preferences.Actions.Any(a => a.Label == "测试录制" && a.Shortcut == "Ctrl+Shift+P"), "settings UI persists added shortcut");
        toolbar.OpenSettings(); Pump(120); settings = Application.OpenForms.OfType<SettingsForm>().Single();
        Button(settings, "＋ 添加").PerformClick();
        name = settings.Controls.OfType<TextBox>().Single(t => t.MaxLength == 24); name.Text = "常用回复";
        settings.Controls.OfType<ComboBox>().Single().SelectedIndex = 1;
        settings.Controls.OfType<TextBox>().Single(t => t.Multiline).Text = "收到，谢谢。";
        Button(settings, "保存并应用").PerformClick(); Pump(100);
        Check(toolbar.Preferences.Actions.Any(a => a.Label == "常用回复" && a.Kind == "text" && a.Text == "收到，谢谢。"), "settings UI persists text button");
        int count = toolbar.Preferences.Actions.Count;
        toolbar.OpenSettings(); Pump(120); settings = Application.OpenForms.OfType<SettingsForm>().Single();
        Button(settings, "＋ 添加").PerformClick(); Button(settings, "取消").PerformClick(); Pump(100);
        Check(toolbar.Preferences.Actions.Count == count, "cancel discards unapplied edits");
        toolbar.ApplyPreferences(prefs);
    }
    static void Preview(string path, string temp)
    {
        Preferences prefs = Preferences.Defaults();
        prefs.ShowNumericKeyboard = prefs.ShowAlphabetKeyboard = true;
        using (ToolbarForm view = new ToolbarForm(prefs, new PreferenceStore(Path.Combine(temp, "preview-settings.json")), false))
        using (SettingsForm settings = new SettingsForm(prefs.Copy(), p => { }))
        {
            view.Show(); settings.Show(); Pump(160);
            settings.ClientSize = new Size(settings.ClientSize.Width, settings.AutoScrollMinSize.Height);
            using (Bitmap left = new Bitmap(view.Width, view.Height))
            using (Bitmap right = new Bitmap(settings.Width, settings.Height))
            using (Bitmap numbers = new Bitmap(view.NumericKeyboard.Width, view.NumericKeyboard.Height))
            using (Bitmap letters = new Bitmap(view.AlphabetKeyboard.Width, view.AlphabetKeyboard.Height))
            {
                view.DrawToBitmap(left, new Rectangle(Point.Empty, view.Size)); settings.DrawToBitmap(right, new Rectangle(Point.Empty, settings.Size));
                view.NumericKeyboard.DrawToBitmap(numbers, new Rectangle(Point.Empty, numbers.Size)); view.AlphabetKeyboard.DrawToBitmap(letters, new Rectangle(Point.Empty, letters.Size));
                int gap = 36, padding = 28;
                int leftWidth = Math.Max(left.Width + gap + numbers.Width, letters.Width);
                int upperHeight = Math.Max(left.Height, numbers.Height);
                using (Bitmap canvas = new Bitmap(leftWidth + right.Width + gap + padding * 2, Math.Max(upperHeight + gap + letters.Height, right.Height) + padding * 2))
                using (Graphics g = Graphics.FromImage(canvas))
                {
                    g.Clear(Color.FromArgb(232, 238, 231));
                    g.DrawImage(left, padding, padding); g.DrawImage(numbers, padding + left.Width + gap, padding);
                    g.DrawImage(letters, padding, padding + upperHeight + gap); g.DrawImage(right, padding + leftWidth + gap, padding);
                    canvas.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                }
            }
            settings.Close(); view.Close();
        }
    }
    static void KeyboardTests(Preferences original)
    {
        Preferences p = original.Copy(); p.ShowNumericKeyboard = true; p.ShowAlphabetKeyboard = true;
        toolbar.ApplyPreferences(p); FocusTarget();
        Check(toolbar.NumericKeyboard.Visible && toolbar.AlphabetKeyboard.Visible, "numeric and alphabet keyboards can be shown together");
        Check(toolbar.AlphabetKeyboard.Keys.Count(k => k.Action.Shortcut.Length == 1 && Char.IsLetter(k.Action.Shortcut[0])) == 26, "QWERTY keyboard contains all 26 letters");
        Check(toolbar.NumericKeyboard.Keys.Count(k => k.Action.Shortcut.Length == 1 && Char.IsDigit(k.Action.Shortcut[0])) == 10, "numeric keyboard contains all ten digits");
        WriteText("", 0, 0);
        foreach (char digit in "0123456789") ClickTile(toolbar.NumericKeyboard.Keys.Single(k => k.Action.Shortcut == digit.ToString()));
        Check(ReadText() == "0123456789", "numeric keys enter digits independently of Num Lock");
        ClickTile(toolbar.NumericKeyboard.Keys.Single(k => k.Action.Shortcut == "OemPeriod"));
        ClickTile(toolbar.NumericKeyboard.Keys.Single(k => k.Action.Shortcut == "Backspace"));
        Check(ReadText() == "0123456789", "numeric decimal point and backspace work");
        bool initialCaps = Control.IsKeyLocked(Keys.CapsLock);
        try
        {
            if (Control.IsKeyLocked(Keys.CapsLock)) ClickTile(toolbar.AlphabetKeyboard.Keys.Single(k => k.Action.Shortcut == "CapsLock"));
            WriteText("", 0, 0); File.WriteAllText(eventLog, "");
            foreach (char letter in "ABCDEFGHIJKLMNOPQRSTUVWXYZ") ClickTile(toolbar.AlphabetKeyboard.Keys.Single(k => k.Action.Shortcut == letter.ToString()));
            Check(ReadText() == "abcdefghijklmnopqrstuvwxyz", "all 26 keys type lowercase English");
            string logged = File.ReadAllText(eventLog);
            Check(logged.Contains("N|None") && !logged.Contains("Packet|"), "letters send physical key events for IME composition");
            ClickTile(toolbar.AlphabetKeyboard.Keys.Single(k => k.Action.Shortcut == "CapsLock"));
            WriteText("", 0, 0); ClickTile(toolbar.AlphabetKeyboard.Keys.Single(k => k.Action.Shortcut == "A"));
            Check(ReadText() == "A", "Caps Lock types uppercase English");
            Pump(300); Check(toolbar.AlphabetKeyboard.Keys.Single(k => k.Action.Shortcut == "A").Action.Label == "A", "letter labels reflect Caps Lock");
        }
        finally
        {
            if (Control.IsKeyLocked(Keys.CapsLock) != initialCaps) ClickTile(toolbar.AlphabetKeyboard.Keys.Single(k => k.Action.Shortcut == "CapsLock"));
        }
        File.WriteAllText(eventLog, ""); ClickTile(toolbar.AlphabetKeyboard.Keys.Single(k => k.Action.Kind == "ime-toggle"));
        Check(File.ReadAllText(eventLog).Contains("ShiftKey|Shift") && (Native.GetAsyncKeyState(0x10) & 0x8000) == 0, "Chinese-English toggle sends and releases Shift");
        WriteText("", 0, 0); ClickTile(toolbar.AlphabetKeyboard.Keys.Single(k => k.Action.Shortcut == "Space"));
        ClickTile(toolbar.AlphabetKeyboard.Keys.Single(k => k.Action.Shortcut == "Enter")); Check(ReadText() == " \r\n", "alphabet keyboard provides space and Enter");
        bool capsBeforePinyin = Control.IsKeyLocked(Keys.CapsLock);
        try
        {
            if (capsBeforePinyin) ClickTile(toolbar.AlphabetKeyboard.Keys.Single(k => k.Action.Shortcut == "CapsLock"));
            WriteText("", 0, 0); File.WriteAllText(eventLog, "");
            if (Native.SendMessage(target, 0x8033, IntPtr.Zero, IntPtr.Zero) == (IntPtr)1)
            {
                Pump(300);
                foreach (char letter in "NIHAO") ClickTile(toolbar.AlphabetKeyboard.Keys.Single(k => k.Action.Shortcut == letter.ToString()));
                ClickTile(toolbar.AlphabetKeyboard.Keys.Single(k => k.Action.Shortcut == "Space")); Pump(400);
                Check(ReadText() == "你好", "Microsoft Pinyin composes nihao and commits 你好 with Space (actual: " + ReadText() + ")");
                Check(File.ReadAllText(eventLog).Contains("IME:"), "real IME composition messages received");
            }
            else Console.WriteLine("SKIP Microsoft Pinyin is not enabled on this Windows profile.");
        }
        finally
        {
            if (Control.IsKeyLocked(Keys.CapsLock) != capsBeforePinyin) ClickTile(toolbar.AlphabetKeyboard.Keys.Single(k => k.Action.Shortcut == "CapsLock"));
        }
        toolbar.ToggleVisible(); Check(!toolbar.NumericKeyboard.Visible && !toolbar.AlphabetKeyboard.Visible, "global hide hides both keyboards");
        toolbar.ToggleVisible(); Check(toolbar.NumericKeyboard.Visible && toolbar.AlphabetKeyboard.Visible, "global show restores enabled keyboards");
        toolbar.SetKeyboardVisible(false, false); Check(!toolbar.NumericKeyboard.Visible && toolbar.AlphabetKeyboard.Visible, "numeric keyboard hides independently");
        toolbar.SetKeyboardVisible(true, false); Check(!toolbar.AlphabetKeyboard.Visible, "alphabet keyboard hides independently");
        toolbar.OpenSettings(); Pump(100); SettingsForm settings = Application.OpenForms.OfType<SettingsForm>().Single();
        settings.Controls.OfType<CheckBox>().Single(c => c.Text == "显示数字键盘").Checked = true;
        settings.Controls.OfType<CheckBox>().Single(c => c.Text == "显示字母键盘").Checked = true;
        Button(settings, "保存并应用").PerformClick(); Pump(150);
        Check(toolbar.NumericKeyboard.Visible && toolbar.AlphabetKeyboard.Visible, "settings UI saves both keyboard switches");
        toolbar.ApplyPreferences(original);
    }
    [STAThread]
    static int Main(string[] args)
    {
        Application.EnableVisualStyles(); Native.SetProcessDPIAware();
        string temp = args[1]; Directory.CreateDirectory(temp);
        Process fixture = null; Point cursor; GetCursorPos(out cursor);
        IntPtr originalForeground = Native.GetForegroundWindow();
        DataObject originalClipboard = new DataObject(); bool hadClipboard = false;
        try
        {
            IDataObject data = Clipboard.GetDataObject();
            if (data != null)
            {
                foreach (string format in data.GetFormats(false)) { object value = data.GetData(format, false); if (value != null) originalClipboard.SetData(format, false, value); }
                hadClipboard = true;
            }
            ParserTests(); ModelTests(temp);
            eventLog = Path.Combine(temp, "keys.log"); string handleFile = Path.Combine(temp, "handles.txt");
            if (File.Exists(handleFile)) File.Delete(handleFile);
            fixture = Process.Start(new ProcessStartInfo(args[0], "\"" + handleFile + "\" \"" + eventLog + "\"") { UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden });
            Stopwatch deadline = Stopwatch.StartNew();
            while (!File.Exists(handleFile) && deadline.ElapsedMilliseconds < 10000) Pump(100);
            Check(File.Exists(handleFile), "isolated editor launched");
            string[] handles = File.ReadAllLines(handleFile); target = new IntPtr(Int64.Parse(handles[0])); editor = new IntPtr(Int64.Parse(handles[1]));
            Preferences prefs = Preferences.Defaults(); prefs.HasPosition = true; prefs.X = 620; prefs.Y = 120;
            prefs.Actions.First(a => a.Shortcut == "Ctrl+Y").Enabled = true;
            toolbar = new ToolbarForm(prefs, new PreferenceStore(Path.Combine(temp, "toolbar.json")), false); toolbar.Show(); Pump(180);
            FocusTarget(); WriteText("alpha βeta", 0, 5); Click("Ctrl+C"); Check(Clipboard.GetText() == "alpha", "Ctrl+C copies from original editor");
            WriteText("", 0, 0); Click("Ctrl+V"); Check(ReadText() == "alpha", "Ctrl+V pastes into original editor");
            Click("Ctrl+Z"); Check(ReadText() == "", "Ctrl+Z undoes paste");
            WriteText("hello world", 1, 1); Click("Ctrl+A");
            long selection = Native.SendMessage(editor, 0xB0, IntPtr.Zero, IntPtr.Zero).ToInt64();
            Check((selection & 0xffff) == 0 && ((selection >> 16) & 0xffff) == 11, "Ctrl+A selects all text");
            WriteText("", 0, 0); File.WriteAllText(eventLog, ""); Click("Enter");
            Check(ReadText() == "\r\n" && File.ReadAllText(eventLog).Contains("Return|None"), "Enter inserts newline without modifiers");
            WriteText("", 0, 0); File.WriteAllText(eventLog, ""); Click("Shift+Enter");
            Check(ReadText() == "\r\n" && File.ReadAllText(eventLog).Contains("Return|Shift"), "Shift+Enter preserves Shift modifier");
            Check((Native.GetAsyncKeyState(0x10) & 0x8000) == 0 && (Native.GetAsyncKeyState(0x11) & 0x8000) == 0, "modifiers released after injection");
            Preferences custom = prefs.Copy();
            custom.Actions.Add(ActionSpec.Key("自定义保存", "Ctrl+Shift+S", true));
            custom.Actions.Add(new ActionSpec { Label = "问候", Kind = "text", Text = "你好，世界😀\n下一行", Enabled = true });
            toolbar.ApplyPreferences(custom); File.WriteAllText(eventLog, ""); Click("Ctrl+Shift+S");
            Check(File.ReadAllText(eventLog).Contains("S|Shift, Control"), "custom Ctrl+Shift+S dispatched");
            WriteText("", 0, 0); ClickTile(toolbar.Tiles.First(t => t.Action.Kind == "text"));
            Check(ReadText() == "你好，世界😀\r\n下一行", "Unicode snippet with emoji and newline");
            Preferences onlyCopy = prefs.Copy(); onlyCopy.Actions.ForEach(a => a.Enabled = a.Shortcut == "Ctrl+C");
            toolbar.ApplyPreferences(onlyCopy); Check(toolbar.Tiles.Count == 1 && toolbar.Width < 250, "one-button compact layout");
            Preferences all = prefs.Copy(); all.Actions.ForEach(a => a.Enabled = true); all.Columns = 4;
            toolbar.ApplyPreferences(all); Check(toolbar.Tiles.Count == 13, "all built-in buttons visible");
            Preferences empty = prefs.Copy(); empty.Actions.ForEach(a => a.Enabled = false);
            toolbar.ApplyPreferences(empty); Check(toolbar.Tiles.Count == 0 && toolbar.Controls.Count >= 3, "empty layout retains settings access");
            Preferences many = prefs.Copy(); while (many.Actions.Count < 80) many.Actions.Add(ActionSpec.Key("测试", "Ctrl+C", true));
            many.Actions.ForEach(a => a.Enabled = true); many.Columns = 1; toolbar.ApplyPreferences(many);
            Check(toolbar.Height < Screen.FromControl(toolbar).WorkingArea.Height, "long button list stays within screen");
            toolbar.ToggleVisible(); Check(!toolbar.Visible, "hide toolbar"); toolbar.ToggleVisible(); Check(toolbar.Visible, "restore toolbar");
            toolbar.ApplyPreferences(prefs);
            FocusTarget(); EditorTests(prefs);
            KeyboardTests(prefs);
            using (SettingsForm own = new SettingsForm(prefs.Copy(), p => { }))
            {
                own.Show(); Pump(120); MouseClick(own.PointToScreen(new Point(own.Width / 2, 80))); Pump(100);
                string error; Check(!Native.SendAction(prefs.Actions[0], out error), "own application never receives injected actions"); own.Close();
            }
            toolbar.Close(); toolbar.Dispose(); toolbar = null;
            Preview(args[2], temp); Check(File.Exists(args[2]), "interface preview rendered");
            Console.WriteLine("TOTAL " + checks + " checks passed."); return 0;
        }
        catch (Exception e) { Console.WriteLine(e.ToString()); return 1; }
        finally
        {
            if (toolbar != null) { toolbar.Close(); toolbar.Dispose(); }
            if (fixture != null && !fixture.HasExited) { fixture.CloseMainWindow(); if (!fixture.WaitForExit(1000)) fixture.Kill(); }
            if (hadClipboard) Clipboard.SetDataObject(originalClipboard, true); else Clipboard.Clear();
            Native.SetCursorPos(cursor.X, cursor.Y);
            if (Native.IsWindow(originalForeground)) Native.SetForegroundWindow(originalForeground);
        }
    }
}
