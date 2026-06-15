using System;
using System.Drawing;
using System.Windows.Forms;

namespace SerialBridge
{
    /// <summary>
    /// タスクトレイ常駐 + 小さなステータスウィンドウ。
    ///
    /// ・タスクトレイアイコンをダブルクリック → ウィンドウ表示/非表示
    /// ・右クリックメニュー → 「表示」「終了」
    /// ・ウィンドウの×ボタン → トレイに隠れる（終了しない）
    /// </summary>
    internal sealed class MainForm : Form
    {
        // ---- コントロール ----
        private readonly NotifyIcon  _trayIcon;
        private readonly Label       _lblPort;
        private readonly Label       _lblLastMsg;
        private readonly Label       _lblCount;
        private readonly Label       _lblTime;
        private readonly ContextMenuStrip _trayMenu;

        // ---- ビジネスロジック ----
        private readonly SerialReader _reader;

        // ---- 状態 ----
        private int    _count      = 0;
        private string _lastMsg    = "待機中…";
        private string _portName   = null;
        private bool   _connected  = false;
        private bool   _cleanedUp  = false;

        public MainForm()
        {
            SuspendLayout();
            // ---- フォーム設定 ----
            Text            = "KartePaste SerialBridge";
            Size            = new Size(380, 200);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox     = false;
            StartPosition   = FormStartPosition.CenterScreen;
            ShowInTaskbar   = false;   // タスクバーには出さない（トレイのみ）

            // ---- ラベル ----
            _lblPort = new Label
            {
                Location  = new Point(12, 16),
                Size      = new Size(344, 22),
                Font      = new Font("Meiryo UI", 10f, FontStyle.Bold),
                ForeColor = Color.DimGray,
                Text      = "接続: 検索中…"
            };

            _lblLastMsg = new Label
            {
                Location  = new Point(12, 50),
                Size      = new Size(344, 42),
                Font      = new Font("Meiryo UI", 9f),
                ForeColor = Color.Black,
                Text      = "最終受信: —",
                AutoEllipsis = true
            };

            _lblCount = new Label
            {
                Location  = new Point(12, 102),
                Size      = new Size(200, 22),
                Font      = new Font("Meiryo UI", 9f),
                ForeColor = Color.DimGray,
                Text      = "受信件数: 0"
            };

            _lblTime = new Label
            {
                Location  = new Point(12, 126),
                Size      = new Size(200, 22),
                Font      = new Font("Meiryo UI", 9f),
                ForeColor = Color.DimGray,
                Text      = "最終受信時刻: —"
            };

            var btnHide = new Button
            {
                Text     = "トレイに隠す",
                Location = new Point(250, 120),
                Size     = new Size(106, 28)
            };
            btnHide.Click += (_, __) => HideToTray();

            Controls.AddRange(new Control[] { _lblPort, _lblLastMsg, _lblCount, _lblTime, btnHide });
            ResumeLayout();

            // ---- トレイアイコン ----
            _trayMenu = new ContextMenuStrip();
            _trayMenu.Items.Add("表示", null, (_, __) => ShowFromTray());
            _trayMenu.Items.Add(new ToolStripSeparator());
            _trayMenu.Items.Add("終了", null, (_, __) => ExitApp());

            _trayIcon = new NotifyIcon
            {
                Text             = "KartePaste SerialBridge",
                Icon             = SystemIcons.Application,
                ContextMenuStrip = _trayMenu,
                Visible          = true
            };
            _trayIcon.DoubleClick += (_, __) => ShowFromTray();

            // ---- 受信エンジン ----
            _reader = new SerialReader(this, ComPortFinder.FindAtomS3Port);
            _reader.StatusChanged += OnStatusChanged;
            _reader.Start();

            // ---- フォームイベント ----
            FormClosing += OnFormClosing;
            Load        += (_, __) => AppLogger.Info("SerialBridge started.");
        }

        // ===================================================================
        // ステータス更新（バックグラウンドスレッドから呼ばれる）
        // ===================================================================

        private void OnStatusChanged(string portName, bool connected, string message, int count)
        {
            // 破棄済み／ハンドル未作成の場合は何もしない
            if (IsDisposed || !IsHandleCreated) return;

            // UI スレッドへ
            if (InvokeRequired)
            {
                Invoke(new Action(() => OnStatusChanged(portName, connected, message, count)));
                return;
            }

            _portName  = portName;
            _connected = connected;
            _count     = count;
            _lastMsg   = message;

            UpdateUi();
        }

        private void UpdateUi()
        {
            _lblPort.Text = _connected
                ? $"接続: {_portName}  (115200 baud)"
                : "接続: 切断中 / 検索中…";
            _lblPort.ForeColor = _connected ? Color.Green : Color.OrangeRed;

            _lblLastMsg.Text  = $"最終受信: {_lastMsg}";
            _lblCount.Text    = $"受信件数: {_count}";

            if (_connected && _count > 0)
            {
                _lblTime.Text = $"最終受信時刻: {DateTime.Now:HH:mm:ss}";
            }

            // トレイのツールチップも更新
            string tip = _connected
                ? $"SerialBridge — {_portName}  受信: {_count} 件"
                : "SerialBridge — 未接続";
            // NotifyIcon.Text は 64 文字以内の制限がある
            _trayIcon.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;
        }

        // ===================================================================
        // ウィンドウ表示/非表示
        // ===================================================================

        private void ShowFromTray()
        {
            Show();
            WindowState = FormWindowState.Normal;
            BringToFront();
            Activate();
        }

        private void HideToTray() => Hide();

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (WindowState == FormWindowState.Minimized) HideToTray();
        }

        // ===================================================================
        // 終了処理
        // ===================================================================

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            // × ボタンはトレイに隠す（Alt+F4 もユーザーが押した場合は隠す）
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                HideToTray();
                return;
            }
            // Application.Exit() やタスクマネージャー等からは素直に終了
            Cleanup();
        }

        private void ExitApp()
        {
            Cleanup();
            Application.Exit();
        }

        private void Cleanup()
        {
            if (_cleanedUp) return;
            _cleanedUp = true;

            AppLogger.Info("SerialBridge shutting down.");
            _reader?.Stop();
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _reader?.Dispose();
                _trayMenu?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
