using System;
using System.Collections.Generic;
using System.Management;

namespace SerialBridge
{
    /// <summary>
    /// WMI を使って VID=0x303A (Espressif / TinyUSB) を持つ COM ポートを探す。
    /// Win32_PnPEntity で "VID_303A" を含むデバイスのうち COM ポート名を持つものを返す。
    /// </summary>
    internal static class ComPortFinder
    {
        // AtomS3(ESP32-S3) の Espressif VID
        private const string TargetVid = "VID_303A";

        /// <summary>
        /// 最初に見つかった COM ポート名を返す。見つからなければ null。
        /// </summary>
        public static string FindAtomS3Port()
        {
            var candidates = FindAllAtomS3Ports();
            return candidates.Count > 0 ? candidates[0] : null;
        }

        /// <summary>
        /// 条件に合う COM ポート名をすべて返す（デバッグ・UI表示用）。
        /// </summary>
        public static List<string> FindAllAtomS3Ports()
        {
            var results = new List<string>();
            try
            {
                // Win32_PnPEntity で "VID_303A" を含むデバイスを検索
                // Name に "(COMx)" 形式が含まれているものが CDC デバイス
                using (var searcher = new ManagementObjectSearcher(
                    "SELECT Name, DeviceID FROM Win32_PnPEntity " +
                    "WHERE DeviceID LIKE '%VID_303A%'"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    using (obj)
                    {
                        string name     = obj["Name"]?.ToString() ?? string.Empty;
                        string deviceId = obj["DeviceID"]?.ToString() ?? string.Empty;

                        AppLogger.Info($"WMI found: Name={name}  DeviceID={deviceId}");

                        string comPort = ExtractComPort(name);
                        if (comPort != null && !results.Contains(comPort))
                        {
                            results.Add(comPort);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("WMI query failed", ex);
            }

            return results;
        }

        /// <summary>
        /// "USB Serial Device (COM3)" のような文字列から "COM3" を抽出する。
        /// </summary>
        private static string ExtractComPort(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            int start = name.IndexOf("(COM", StringComparison.OrdinalIgnoreCase);
            if (start < 0) return null;
            int end = name.IndexOf(')', start);
            if (end < 0) return null;
            // "(COM3)" → "COM3"
            return name.Substring(start + 1, end - start - 1).Trim().ToUpperInvariant();
        }
    }
}
