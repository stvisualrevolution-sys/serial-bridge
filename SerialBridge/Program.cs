using System;
using System.Windows.Forms;

namespace SerialBridge
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // 二重起動チェック
            bool createdNew;
            using (var mutex = new System.Threading.Mutex(true, "SerialBridge_KartePaste_SingleInstance", out createdNew))
            {
                if (!createdNew)
                {
                    MessageBox.Show(
                        "SerialBridge はすでに起動しています。\nタスクトレイを確認してください。",
                        "SerialBridge",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return;
                }

                Application.Run(new MainForm());
            }
        }
    }
}
