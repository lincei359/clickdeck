using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ClickDeck
{
    public static class Native
    {
        public const int WM_MOUSEACTIVATE = 0x21, MA_NOACTIVATE = 3;
        public const int WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOOLWINDOW = 0x80;
        public const int WM_HOTKEY = 0x312, WM_NCLBUTTONDOWN = 0xA1, HTCAPTION = 2;
        public const uint KEYUP = 2, UNICODE = 4, EXTENDED = 1;
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr handle);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr handle);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr handle);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr handle, out uint pid);
        [DllImport("user32.dll")] public static extern bool ReleaseCapture();
        [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, int message, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr h, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr h, int id);
        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")] public static extern uint MapVirtualKey(uint code, uint mapType);
        [DllImport("user32.dll", SetLastError = true)] public static extern uint SendInput(uint count, INPUT[] inputs, int size);
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int command);
        [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern uint RegisterWindowMessage(string name);
        [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr handle, uint message, IntPtr w, IntPtr l);
        public delegate bool EnumWindowCallback(IntPtr handle, IntPtr parameter);
        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowCallback callback, IntPtr parameter);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool SetProp(IntPtr handle, string name, IntPtr value);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr GetProp(IntPtr handle, string name);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr RemoveProp(IntPtr handle, string name);
        public const string ToolbarProperty = "ClickDeck.Toolbar.v1";
        public static readonly uint ShowToolbarMessage = RegisterWindowMessage("ClickDeck.ShowToolbar.v1");

        public static bool ShowExistingToolbar()
        {
            bool sent = false;
            // ShowInTaskbar=false gives this form an invisible owner. A broadcast
            // excludes owned windows, so send the registered message directly.
            EnumWindows((handle, parameter) =>
            {
                if (GetProp(handle, ToolbarProperty) != IntPtr.Zero)
                    sent = PostMessage(handle, ShowToolbarMessage, IntPtr.Zero, IntPtr.Zero) || sent;
                return true;
            }, IntPtr.Zero);
            return sent;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct INPUT { public uint type; public InputUnion data; }
        [StructLayout(LayoutKind.Explicit)]
        public struct InputUnion
        {
            [FieldOffset(0)] public KEYBDINPUT keyboard;
            [FieldOffset(0)] public MOUSEINPUT mouse;
        }
        [StructLayout(LayoutKind.Sequential)]
        public struct KEYBDINPUT { public ushort vk, scan; public uint flags, time; public UIntPtr extra; }
        [StructLayout(LayoutKind.Sequential)]
        public struct MOUSEINPUT { public int x, y; public uint mouseData, flags, time; public UIntPtr extra; }

        public static INPUT Key(ushort key, bool up)
        {
            uint flags = up ? KEYUP : 0;
            if (key == 0x5B || key == 0x5C || key == 0x2D || key == 0x2E ||
                (key >= 0x21 && key <= 0x28)) flags |= EXTENDED;
            return new INPUT { type = 1, data = new InputUnion { keyboard = new KEYBDINPUT { vk = key, scan = (ushort)MapVirtualKey(key, 0), flags = flags } } };
        }

        public static INPUT UnicodeKey(char value, bool up)
        {
            return new INPUT { type = 1, data = new InputUnion { keyboard = new KEYBDINPUT { scan = value, flags = UNICODE | (up ? KEYUP : 0) } } };
        }

        public static bool IsExternalWindow(IntPtr target)
        {
            uint pid;
            return target != IntPtr.Zero && IsWindow(target) && !IsIconic(target) &&
                GetWindowThreadProcessId(target, out pid) != 0 && pid != (uint)Process.GetCurrentProcess().Id;
        }

        public static bool SendAction(ActionSpec action, out string error)
        {
            error = null;
            IntPtr target = GetForegroundWindow();
            if (!IsExternalWindow(target)) { error = "请先点击要操作的窗口"; return false; }
            foreach (int key in new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C })
            {
                if ((GetAsyncKeyState(key) & 0x8000) != 0) { error = "请先松开 Ctrl / Shift / Alt / Win"; return false; }
            }
            List<INPUT> events = new List<INPUT>();
            if (action.Kind == "text")
            {
                string value = (action.Text ?? "").Replace("\r\n", "\n").Replace("\r", "\n");
                foreach (char c in value)
                {
                    if (c == '\n' || c == '\t') { ushort vk = (ushort)(c == '\n' ? 0x0D : 0x09); events.Add(Key(vk, false)); events.Add(Key(vk, true)); }
                    else { events.Add(UnicodeKey(c, false)); events.Add(UnicodeKey(c, true)); }
                }
            }
            else if (action.Kind == "ime-toggle")
            {
                events.Add(Key(0x10, false)); events.Add(Key(0x10, true));
            }
            else
            {
                Chord chord;
                if (!Chord.TryParse(action.Shortcut, out chord, out error)) return false;
                foreach (ushort modifier in chord.Modifiers) events.Add(Key(modifier, false));
                events.Add(Key(chord.KeyCode, false)); events.Add(Key(chord.KeyCode, true));
                for (int i = chord.Modifiers.Count - 1; i >= 0; i--) events.Add(Key(chord.Modifiers[i], true));
            }
            // Chunk long snippets without allowing them to continue in another window.
            for (int i = 0; i < events.Count; i += 64)
            {
                if (GetForegroundWindow() != target) { error = "目标窗口已切换，已停止输入"; return false; }
                INPUT[] chunk = events.GetRange(i, Math.Min(64, events.Count - i)).ToArray();
                uint count = SendInput((uint)chunk.Length, chunk, Marshal.SizeOf(typeof(INPUT)));
                if (count != chunk.Length)
                {
                    List<INPUT> release = new List<INPUT>();
                    foreach (INPUT input in chunk)
                        if ((input.data.keyboard.flags & KEYUP) != 0) release.Add(input);
                    if (release.Count > 0) SendInput((uint)release.Count, release.ToArray(), Marshal.SizeOf(typeof(INPUT)));
                    error = "发送失败；请检查目标窗口是否以管理员身份运行";
                    return false;
                }
            }
            return true;
        }
    }

    public sealed class Chord
    {
        public List<ushort> Modifiers = new List<ushort>();
        public ushort KeyCode;
        public string Display;
        public static bool TryParse(string text, out Chord chord, out string error)
        {
            chord = null; error = null;
            if (String.IsNullOrWhiteSpace(text)) { error = "请输入快捷键，例如 Ctrl+C"; return false; }
            string[] tokens = text.Trim().Split('+');
            Chord result = new Chord();
            List<string> names = new List<string>();
            for (int i = 0; i < tokens.Length; i++)
            {
                string token = tokens[i].Trim(); string lower = token.ToLowerInvariant();
                ushort mod = lower == "ctrl" || lower == "control" ? (ushort)0x11 : lower == "shift" ? (ushort)0x10 : lower == "alt" ? (ushort)0x12 : lower == "win" || lower == "windows" ? (ushort)0x5B : (ushort)0;
                if (i < tokens.Length - 1)
                {
                    if (mod == 0 || result.Modifiers.Contains(mod)) { error = "格式：Ctrl+Shift+S；修饰键不能重复"; return false; }
                    result.Modifiers.Add(mod);
                    names.Add(mod == 0x11 ? "Ctrl" : mod == 0x10 ? "Shift" : mod == 0x12 ? "Alt" : "Win");
                }
                else
                {
                    if (mod != 0 || token.Length == 0) { error = "最后一项需要是具体按键，例如 C 或 Enter"; return false; }
                    Keys key; string normalized = token;
                    switch (lower)
                    {
                        case "return": case "回车": normalized = "Enter"; break;
                        case "escape": normalized = "Escape"; break;
                        case "esc": normalized = "Escape"; break;
                        case "space": case "空格": normalized = "Space"; break;
                        case "backspace": normalized = "Back"; break;
                        case "del": normalized = "Delete"; break;
                        case "pgup": normalized = "PageUp"; break;
                        case "pgdn": normalized = "PageDown"; break;
                        case "plus": normalized = "Oemplus"; break;
                        case "minus": normalized = "OemMinus"; break;
                    }
                    if (normalized.Length == 1 && Char.IsDigit(normalized[0])) normalized = "D" + normalized;
                    int numericKey;
                    if (Int32.TryParse(normalized, out numericKey)) { error = "数字按键请一次输入一位，例如 Ctrl+1"; return false; }
                    if (!Enum.TryParse<Keys>(normalized, true, out key) || !Enum.IsDefined(typeof(Keys), key) ||
                        (int)key <= 7 || (int)key >= 256 || key == Keys.ShiftKey || key == Keys.ControlKey || key == Keys.Menu ||
                        key == Keys.LShiftKey || key == Keys.RShiftKey || key == Keys.LControlKey || key == Keys.RControlKey ||
                        key == Keys.LMenu || key == Keys.RMenu || key == Keys.LWin || key == Keys.RWin)
                    { error = "无法识别按键“" + token + "”；可用 Enter、F1、Delete 等"; return false; }
                    result.KeyCode = (ushort)key;
                    names.Add(key == Keys.Escape ? "Esc" : key == Keys.Back ? "Backspace" : token.Length == 1 ? token.ToUpperInvariant() : key.ToString());
                }
            }
            result.Display = String.Join("+", names); chord = result; return true;
        }
    }
}
