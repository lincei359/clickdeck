using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace ClickDeck
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            bool created;
            using (Mutex mutex = new Mutex(true, "Local\\ClickDeck.Desktop.v1", out created))
            {
                if (!created)
                {
                    if (!Native.ShowExistingToolbar())
                        MessageBox.Show("点点快捷已经在运行。请在右下角系统托盘中打开它。", "点点快捷");
                    return;
                }
                try
                {
                    Native.SetProcessDPIAware();
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClickDeck", "settings.json");
                    PreferenceStore store = new PreferenceStore(path);
                    Application.Run(new ToolbarForm(store.Load(), store, true));
                }
                catch (Exception e)
                {
                    MessageBox.Show("无法启动点点快捷：\n" + e.Message, "点点快捷", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }
    }
}
