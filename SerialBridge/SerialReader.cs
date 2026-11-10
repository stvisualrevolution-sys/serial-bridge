using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace SerialBridge
{
    /// <summary>
    /// バックグラウンドスレッドで COM ポートを読み続け、
    /// 0x04(EOT) で区切られた "SOAP:..." メッセージを解析して
    /// クリップボードに設定するクラス。
    ///
    /// メッセージ形式: SOAP:{UTF-8 本文}0x04
    /// プレフィックス "SOAP:" を除いた本文をクリップボードへ。
    /// </summary>
    internal sealed class SerialReader : IDisposable
    {
        private const byte EOT          = 0x04;
        private const string SoapPrefix = "SOAP:";
        private const int BaudRate      = 115200;
        private const int ReconnectMs   = 3000;   // 切断後の再接続待機
        private const int ReadTimeoutMs = 5000;   // 受信タイムアウト（スレッドのブロック解除用）

        // UI スレッドへの通知用デリゲート
        public delegate void StatusChangedHandler(string portName, bool connected, string lastMessage, int count);
        public event StatusChangedHandler StatusChanged;

        // クリップボード設定用（STAスレッド=UIスレッド）の制御
        private readonly Control _uiControl;

        private Thread        _thread;
        private volatile bool _running;
        private SerialPort    _port;

        // COM ポートを探す関数（外部から注入してテスト容易性を上げる）
        private readonly Func<string> _portFinder;

        private string _currentPort = null;
        private int    _receiveCount = 0;

        public SerialReader(Control uiControl, Func<string> portFinder)
        {
            _uiControl  = uiControl  ?? throw new ArgumentNullException(nameof(uiControl));
            _portFinder = portFinder ?? throw new ArgumentNullException(nameof(portFinder));
        }

        /// <summary>バックグラウンド受信スレッドを開始する。</summary>
        public void Start()
        {
            if (_running) return;
            _running = true;
            _thread  = new Thread(ReceiveLoop)
            {
                IsBackground = true,
                Name         = "SerialBridgeReceiver"
            };
            _thread.Start();
        }

        /// <summary>受信スレッドを停止して COM ポートを閉じる。</summary>
        public void Stop()
        {
            _running = false;
            ClosePort();
        }

        public void Dispose() => Stop();

        // ===================================================================
        // メインループ
        // ===================================================================

        private void ReceiveLoop()
        {
            while (_running)
            {
                // ① COM ポートを探す
                string portName = null;
                try { portName = _portFinder(); }
                catch (Exception ex) { AppLogger.Error("Port finder threw", ex); }

                if (portName == null)
                {
                    AppLogger.Info("AtomS3 not found. Retrying in 3s...");
                    NotifyStatus(null, false, "AtomS3 が見つかりません。USB 接続を確認してください。", _receiveCount);
                    Sleep(ReconnectMs);
                    continue;
                }

                // ② ポートを開く
                try
                {
                    _port = new SerialPort(portName, BaudRate, Parity.None, 8, StopBits.One)
                    {
                        Encoding        = Encoding.UTF8,
                        ReadTimeout     = ReadTimeoutMs,
                        WriteTimeout    = 1000,
                        // いったん両方 OFF で開く（下で ON にする「順番」を確実にするため）
                        DtrEnable       = false,
                        RtsEnable       = false,
                    };
                    _port.Open();
                    // AtomS3(ESP32 TinyUSB) は DTR と RTS が両方 ON のときだけ「PC 接続済み」とみなし CDC 送信する
                    // （OFF のままだと HID 打鍵に切り替わる）。
                    // 順番が大事: 必ず DTR → RTS の順に ON にする。RTS だけが先に ON になると、ESP32 側が
                    // 「書き込みモードへの再起動手順の途中」と解釈し、接続済みにならないことがある。
                    _port.DtrEnable = true;
                    _port.RtsEnable = true;
                    _currentPort = portName;
                    AppLogger.Info($"Opened {portName} at {BaudRate} baud");
                    NotifyStatus(portName, true, "接続中", _receiveCount);
                }
                catch (Exception ex)
                {
                    AppLogger.Error($"Failed to open {portName}", ex);
                    ClosePort();
                    Sleep(ReconnectMs);
                    continue;
                }

                // ③ 受信ループ
                var buffer = new List<byte>(4096);
                try
                {
                    while (_running)
                    {
                        int b;
                        try
                        {
                            b = _port.ReadByte();
                        }
                        catch (TimeoutException)
                        {
                            // タイムアウトは正常（ポーリング代わり）
                            continue;
                        }

                        if (b < 0) break; // ポートが閉じた

                        if (b == EOT)
                        {
                            // メッセージ確定
                            ProcessMessage(buffer.ToArray());
                            buffer.Clear();
                        }
                        else
                        {
                            buffer.Add((byte)b);
                            // バッファ肥大化防止（最大 64KB）
                            if (buffer.Count > 65536)
                            {
                                AppLogger.Warn("Buffer overflow. Clearing.");
                                buffer.Clear();
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Error($"Read error on {portName}", ex);
                }
                finally
                {
                    ClosePort();
                    NotifyStatus(null, false, "切断されました。再接続を試みています…", _receiveCount);
                    AppLogger.Info($"Port {portName} closed.");
                }

                Sleep(ReconnectMs);
            }
        }

        // ===================================================================
        // メッセージ処理
        // ===================================================================

        private void ProcessMessage(byte[] raw)
        {
            if (raw == null || raw.Length == 0) return;

            string text;
            try
            {
                text = Encoding.UTF8.GetString(raw);
            }
            catch (Exception ex)
            {
                AppLogger.Error("UTF-8 decode failed", ex);
                return;
            }

            // "SOAP:" プレフィックスを検証
            if (!text.StartsWith(SoapPrefix, StringComparison.Ordinal))
            {
                // 途中から届いたカルテ本文の可能性があるため、中身はログに残さない。文字数だけを記録する。
                AppLogger.Warn($"Unknown prefix. Ignoring. ({text.Length} chars)");
                return;
            }

            string body = text.Substring(SoapPrefix.Length);
            if (body.Length == 0)
            {
                AppLogger.Warn("Empty SOAP body. Ignoring.");
                return;
            }

            // カルテ本文（患者情報）はログに残さない。文字数だけを記録する。
            AppLogger.Info($"SOAP received ({body.Length} chars)");

            // クリップボードは STA スレッドで設定する
            SetClipboard(body);

            _receiveCount++;
            string preview = Truncate(body, 40);
            NotifyStatus(_currentPort, true, $"受信: {preview}", _receiveCount);
        }

        // ===================================================================
        // クリップボード（STA スレッド = UI スレッドへ Invoke）
        // ===================================================================

        private void SetClipboard(string text)
        {
            try
            {
                if (_uiControl == null || _uiControl.IsDisposed || !_uiControl.IsHandleCreated)
                    return;

                if (_uiControl.InvokeRequired)
                {
                    _uiControl.Invoke(new Action(() => SetClipboardOnSta(text)));
                }
                else
                {
                    SetClipboardOnSta(text);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("Clipboard invoke failed", ex);
            }
        }

        private static void SetClipboardOnSta(string text)
        {
            try
            {
                // ExternalException が稀に出るためリトライ
                for (int i = 0; i < 3; i++)
                {
                    try
                    {
                        Clipboard.SetText(text, TextDataFormat.UnicodeText);
                        AppLogger.Info("Clipboard set successfully.");
                        return;
                    }
                    catch (System.Runtime.InteropServices.ExternalException)
                    {
                        Thread.Sleep(50);
                    }
                }
                AppLogger.Warn("Clipboard set failed after 3 retries.");
            }
            catch (Exception ex)
            {
                AppLogger.Error("SetClipboardOnSta failed", ex);
            }
        }

        // ===================================================================
        // ユーティリティ
        // ===================================================================

        private void NotifyStatus(string port, bool connected, string message, int count)
        {
            try
            {
                StatusChanged?.Invoke(port, connected, message, count);
            }
            catch { }
        }

        private void ClosePort()
        {
            try
            {
                _port?.Close();
                _port?.Dispose();
            }
            catch { }
            finally
            {
                _port        = null;
                _currentPort = null;
            }
        }

        private void Sleep(int ms)
        {
            // _running が false になったらすぐ抜けられるよう小刻みに待つ
            int elapsed = 0;
            while (_running && elapsed < ms)
            {
                Thread.Sleep(200);
                elapsed += 200;
            }
        }

        private static string Truncate(string s, int maxLen)
            => s == null ? "" : (s.Length <= maxLen ? s : s.Substring(0, maxLen) + "…");
    }
}
