// 程序入口：加载内嵌图标、启动主窗口
using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace FileDiffTool
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // 界面线程里的异常默认会被 WinForms 吞掉（或弹个看不懂的框），一律写进 crash.log：
            // 弹窗/事件处理器里出的问题，靠这个才查得动
            Application.ThreadException += delegate(object sender, System.Threading.ThreadExceptionEventArgs ev)
            {
                try
                {
                    File.WriteAllText(Path.Combine(Application.StartupPath, "crash.log"),
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\r\n" + ev.Exception.ToString());
                }
                catch (Exception) { }
                MessageBox.Show("界面出错：\r\n\r\n" + ev.Exception.Message, "文件比较器",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            };

            // 双实例保护：同一时间只跑一个主窗口
            bool createdNew;
            System.Threading.Mutex mutex = new System.Threading.Mutex(true,
                "Local\\FileDiffTool.SingleInstance", out createdNew);
            if (!createdNew)
            {
                MessageBox.Show("文件比较器已经在运行了。", "文件比较器",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                Application.Run(new MainForm(args));
            }
            catch (Exception ex)
            {
                MessageBox.Show("程序运行出错：\r\n\r\n" + ex.Message + "\r\n\r\n" + ex.StackTrace,
                    "文件比较器", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                GC.KeepAlive(mutex);
            }
        }

        internal static Icon LoadAppIcon()
        {
            try
            {
                Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("app.ico");
                if (s == null) return null;
                return new Icon(s);
            }
            catch (Exception) { return null; }
        }
    }
}
