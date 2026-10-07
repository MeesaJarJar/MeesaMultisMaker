using System;
using System.Windows.Forms;

namespace MeesaMultisMaker
{
    internal static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            // Last-resort crash recorder: a full crash-out currently leaves
            // zero evidence. Both handlers append to crash.log next to the
            // exe (temp folder fallback) before the process dies.
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                try { CrashLog("UNHANDLED", e.ExceptionObject as Exception); }
                catch { }
            };
            Application.ThreadException += (s, e) =>
            {
                try
                {
                    CrashLog("UI-THREAD", e.Exception);
                    MessageBox.Show("MeesaMultisMaker hit an error and logged it to:\r\n" + CrashLogPath() +
                        "\r\n\r\n" + e.Exception.Message, "MeesaMultisMaker crashed",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                catch { }
            };
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Application.Run(new Form1());
        }

        private static string CrashLogPath()
        {
            try
            {
                string dir = AppDomain.CurrentDomain.BaseDirectory;
                string p = System.IO.Path.Combine(dir, "crash.log");
                using (var fs = new System.IO.FileStream(p, System.IO.FileMode.Append,
                    System.IO.FileAccess.Write, System.IO.FileShare.Read)) { }
                return p;
            }
            catch { }
            try { return System.IO.Path.Combine(System.IO.Path.GetTempPath(), "MeesaMultisMaker_crash.log"); }
            catch { return "crash.log"; }
        }

        private static void CrashLog(string kind, Exception ex)
        {
            try
            {
                string path = CrashLogPath();
                string text = string.Format("[{0:yyyy-MM-dd HH:mm:ss}] {1}: {2}\r\n{3}\r\n\r\n",
                    DateTime.Now, kind,
                    ex != null ? ex.GetType().FullName + ": " + ex.Message : "(no exception)",
                    ex != null ? ex.ToString() : string.Empty);
                System.IO.File.AppendAllText(path, text);
            }
            catch { }
        }
    }
}
