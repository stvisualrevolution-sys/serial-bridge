using System;
using System.IO;
using System.Text;

namespace SerialBridge
{
    /// <summary>
    /// %APPDATA%\SerialBridge\serial_bridge.log へ追記するシンプルなロガー。
    /// ファイルが 2MB を超えたらローテートする。
    /// </summary>
    internal static class AppLogger
    {
        private static readonly string LogDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SerialBridge");

        private static readonly string LogPath =
            Path.Combine(LogDir, "serial_bridge.log");

        private const long MaxLogBytes = 2 * 1024 * 1024; // 2 MB

        private static readonly object _lock = new object();

        static AppLogger()
        {
            try { Directory.CreateDirectory(LogDir); }
            catch { /* ログフォルダ作成失敗は無視 */ }
        }

        public static void Info(string message)  => Write("INFO ", message);
        public static void Warn(string message)  => Write("WARN ", message);
        public static void Error(string message) => Write("ERROR", message);

        public static void Error(string message, Exception ex)
            => Write("ERROR", $"{message}: {ex.GetType().Name}: {ex.Message}");

        private static void Write(string level, string message)
        {
            try
            {
                lock (_lock)
                {
                    RotateIfNeeded();
                    var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}{Environment.NewLine}";
                    File.AppendAllText(LogPath, line, Encoding.UTF8);
                }
            }
            catch
            {
                // ログ書き込み失敗は握りつぶす（本務に影響させない）
            }
        }

        private static void RotateIfNeeded()
        {
            try
            {
                if (File.Exists(LogPath) && new FileInfo(LogPath).Length > MaxLogBytes)
                {
                    var backup = LogPath + ".bak";
                    if (File.Exists(backup)) File.Delete(backup);
                    File.Move(LogPath, backup);
                }
            }
            catch { }
        }
    }
}
