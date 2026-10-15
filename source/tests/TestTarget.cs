using System;
using System.IO;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// This interface is used only by the isolated integration-test process.
[ComImport, Guid("71C6E74C-0F28-11D8-A82A-00065B84435C"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface ITestProfileManager
{
    [PreserveSig] int ActivateProfile(uint type, ushort language, ref Guid clsid, ref Guid profile, IntPtr layout, uint flags);
}
class TestEditor : TextBox
{
    public string Log;
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x10D || m.Msg == 0x10F) File.AppendAllText(Log, "IME:" + m.Msg.ToString("X") + "\n");
        base.WndProc(ref m);
    }
}
class TestWindow : Form
{
    public TestEditor Editor;
    public string Log;
    [DllImport("imm32.dll")] static extern IntPtr ImmGetContext(IntPtr window);
    [DllImport("imm32.dll")] static extern bool ImmReleaseContext(IntPtr window, IntPtr context);
    [DllImport("imm32.dll")] static extern bool ImmSetOpenStatus(IntPtr context, bool open);
    [DllImport("imm32.dll")] static extern bool ImmSetConversionStatus(IntPtr context, uint conversion, uint sentence);
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x8033)
        {
            object manager = null;
            try
            {
                InputLanguage language = InputLanguage.FromCulture(CultureInfo.GetCultureInfo("zh-CN"));
                if (language != null) InputLanguage.CurrentInputLanguage = language;
                Editor.ImeMode = ImeMode.On;
                manager = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("33C53A50-F456-4884-B049-85FD643ECFED")));
                Guid service = new Guid("86598FB9-66A2-463E-B9C2-AEB906D477AD"), profile = new Guid("607FDF85-FCC8-4DBD-A365-41296F980C9C");
                int result = ((ITestProfileManager)manager).ActivateProfile(1, 0x0804, ref service, ref profile, IntPtr.Zero, 0x10000000);
                File.AppendAllText(Log, "Pinyin activation: " + result.ToString("X") + "\n");
                IntPtr context = ImmGetContext(Editor.Handle);
                if (context != IntPtr.Zero) { ImmSetOpenStatus(context, true); ImmSetConversionStatus(context, 1, 0); ImmReleaseContext(Editor.Handle, context); }
                m.Result = result == 0 ? (IntPtr)1 : IntPtr.Zero;
            }
            catch (Exception e) { File.AppendAllText(Log, "Pinyin unavailable: " + e.Message + "\n"); m.Result = IntPtr.Zero; }
            finally { if (manager != null) Marshal.ReleaseComObject(manager); }
            return;
        }
        base.WndProc(ref m);
    }
}
class TestTarget
{
    [STAThread]
    static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        TestWindow form = new TestWindow { Text = "ClickDeck · 自动化测试窗口", Width = 460, Height = 330, Left = 90, Top = 110, StartPosition = FormStartPosition.Manual, Log = args[1] };
        TestEditor editor = new TestEditor { Dock = DockStyle.Fill, Multiline = true, AcceptsReturn = true, AcceptsTab = true, ImeMode = ImeMode.Disable, Log = args[1] };
        form.Editor = editor;
        editor.KeyDown += (s, e) => File.AppendAllText(args[1], e.KeyCode + "|" + e.Modifiers + "\n");
        form.Controls.Add(editor);
        form.Shown += (s, e) => { editor.Focus(); File.WriteAllText(args[0], form.Handle.ToInt64() + "\n" + editor.Handle.ToInt64()); };
        Application.Run(form);
    }
}
