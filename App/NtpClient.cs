using System;
using System.Net;
using System.Net.Sockets;

namespace KeRing.App
{
    /// <summary>
    /// 极简 SNTP 客户端。只做一件事：问一次"现在几点"，返回**标准时间减本机时间**的差值。
    ///
    /// 为什么自己写：项目要单文件交付、不引新依赖，而取时间只要收发一个 48 字节的 UDP 包，
    /// 犯不上引 NTP 库。也刻意**不调用系统的时间同步服务**——那要改系统时钟（需要管理员权限），
    /// 我们只想在自己进程里算一个修正量。
    /// </summary>
    internal static class NtpClient
    {
        /// <summary>NTP 纪元（1900-01-01）与本机纪元（0001-01-01）的差。</summary>
        private static readonly DateTime NtpEpoch = new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>查一次 NTP。失败返回 null；成功返回"标准 UTC − 本机 UTC"。</summary>
        public static TimeSpan? Query(string host, int timeoutSeconds)
        {
            if (string.IsNullOrWhiteSpace(host)) { return null; }

            var timeoutMs = Math.Max(1, timeoutSeconds) * 1000;

            try
            {
                var request = new byte[48];
                request[0] = 0x23;      // LI=0、版本 4、模式 3（客户端）

                using (var udp = new UdpClient())
                {
                    udp.Client.ReceiveTimeout = timeoutMs;
                    udp.Client.SendTimeout = timeoutMs;
                    udp.Connect(host.Trim(), 123);

                    var sentUtc = DateTime.UtcNow;
                    WriteTimestamp(request, 40, sentUtc);       // 有些服务器要求发送时刻非零
                    udp.Send(request, request.Length);

                    var remote = new IPEndPoint(IPAddress.Any, 0);
                    var response = udp.Receive(ref remote);
                    var receivedUtc = DateTime.UtcNow;

                    if (response == null || response.Length < 48) { return null; }
                    if ((response[0] & 0x07) != 4) { return null; }   // 模式不是"服务器应答"
                    if (response[1] == 0) { return null; }            // Stratum 0 = kiss-o'-death，服务器拒绝服务

                    var serverReceivedUtc = ReadTimestamp(response, 32);
                    var serverSentUtc = ReadTimestamp(response, 40);
                    if (serverSentUtc == DateTime.MinValue) { return null; }

                    // 标准算法：offset = ((T2 − T1) + (T3 − T4)) / 2
                    // T1=本机发出、T2=服务器收到、T3=服务器发出、T4=本机收到。
                    // 这样能把大部分网络往返时间抵掉，局域网/宽带下误差在毫秒级。
                    var offset = ((serverReceivedUtc - sentUtc) + (serverSentUtc - receivedUtc)).TotalMilliseconds / 2.0;
                    return TimeSpan.FromMilliseconds(offset);
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("NTP 查询失败（" + host + "）：" + ex.Message);
                return null;
            }
        }

        private static DateTime ReadTimestamp(byte[] buffer, int offset)
        {
            var seconds = ((uint)buffer[offset] << 24) | ((uint)buffer[offset + 1] << 16) |
                          ((uint)buffer[offset + 2] << 8) | buffer[offset + 3];
            if (seconds == 0) { return DateTime.MinValue; }

            var fraction = ((uint)buffer[offset + 4] << 24) | ((uint)buffer[offset + 5] << 16) |
                           ((uint)buffer[offset + 6] << 8) | buffer[offset + 7];
            var milliseconds = fraction * 1000.0 / 4294967296.0;

            return NtpEpoch.AddSeconds(seconds).AddMilliseconds(milliseconds);
        }

        private static void WriteTimestamp(byte[] buffer, int offset, DateTime utc)
        {
            var seconds = (uint)(utc - NtpEpoch).TotalSeconds;
            var fraction = (uint)(((utc - NtpEpoch).TotalSeconds - seconds) * 4294967296.0);

            buffer[offset] = (byte)(seconds >> 24);
            buffer[offset + 1] = (byte)(seconds >> 16);
            buffer[offset + 2] = (byte)(seconds >> 8);
            buffer[offset + 3] = (byte)seconds;
            buffer[offset + 4] = (byte)(fraction >> 24);
            buffer[offset + 5] = (byte)(fraction >> 16);
            buffer[offset + 6] = (byte)(fraction >> 8);
            buffer[offset + 7] = (byte)fraction;
        }
    }
}
