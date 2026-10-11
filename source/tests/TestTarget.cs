using System;
using System.IO;
using System.Windows.Forms;

class TestTarget
{
    [STAThread]
    static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Form form = new Form { Text = "ClickDeck · 自动化测试窗口", Width = 460, Height = 330, Left = 90, Top = 110, StartPosition = FormStartPosition.Manual };
        TextBox editor = new TextBox { Dock = DockStyle.Fill, Multiline = true, AcceptsReturn = true, AcceptsTab = true };
        editor.KeyDown += (s, e) => File.AppendAllText(args[1], e.KeyCode + "|" + e.Modifiers + "\n");
        form.Controls.Add(editor);
        form.Shown += (s, e) => { editor.Focus(); File.WriteAllText(args[0], form.Handle.ToInt64() + "\n" + editor.Handle.ToInt64()); };
        Application.Run(form);
    }
}
