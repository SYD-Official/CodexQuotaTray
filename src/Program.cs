using System;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;

namespace CodexQuotaTray
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            WaitForPreviousVersion(args);
            bool createdNew;
            using (var mutex = new Mutex(true, "CodexQuotaTray.SingleInstance", out createdNew))
            {
                if (!createdNew)
                {
                    MessageBox.Show("Codex 余额已经在运行。", "Codex 余额", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayApplicationContext());
                GC.KeepAlive(mutex);
            }
        }

        private static void WaitForPreviousVersion(string[] args)
        {
            if (args == null || args.Length != 2 || !string.Equals(args[0], "--wait-for-pid", StringComparison.OrdinalIgnoreCase)) return;
            int processId;
            if (!int.TryParse(args[1], out processId) || processId <= 0) return;
            try
            {
                using (var process = Process.GetProcessById(processId)) process.WaitForExit(30000);
            }
            catch
            {
            }
        }
    }
}
