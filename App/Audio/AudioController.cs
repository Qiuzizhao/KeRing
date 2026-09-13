using System;
using NAudio.CoreAudioApi;

namespace KeRing.App.Audio
{
    /// <summary>系统主音量的快照，用于播报后原样恢复（音量值 + 静音状态）。</summary>
    internal sealed class VolumeSnapshot
    {
        public bool Valid;
        public float MasterVolume;
        public bool Muted;
    }

    /// <summary>
    /// 系统音量控制。约定：播报前临时提升到设定值，播完恢复原样。
    /// 注意：应用级音量乘在主音量之上，突破不了主音量上限，所以"要够响"必须动主音量。
    /// </summary>
    internal sealed class AudioController
    {
        public static string DescribeDefaultDevice()
        {
            try
            {
                using (var enumerator = new MMDeviceEnumerator())
                {
                    var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                    var volume = device.AudioEndpointVolume;
                    return string.Format(
                        "{0}（{1:P0}{2}）",
                        device.FriendlyName,
                        volume.MasterVolumeLevelScalar,
                        volume.Mute ? "，已静音" : string.Empty);
                }
            }
            catch (Exception ex)
            {
                return "无可用播放设备：" + ex.Message;
            }
        }

        /// <summary>状态栏用的短描述，不查设备名。</summary>
        public static string DescribeVolumeShort()
        {
            try
            {
                using (var enumerator = new MMDeviceEnumerator())
                {
                    var volume = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia).AudioEndpointVolume;
                    return string.Format("{0:P0}{1}", volume.MasterVolumeLevelScalar, volume.Mute ? " 已静音" : string.Empty);
                }
            }
            catch
            {
                return "无设备";
            }
        }

        public VolumeSnapshot Capture()
        {
            var snapshot = new VolumeSnapshot();
            try
            {
                using (var enumerator = new MMDeviceEnumerator())
                {
                    var volume = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia).AudioEndpointVolume;
                    snapshot.MasterVolume = volume.MasterVolumeLevelScalar;
                    snapshot.Muted = volume.Mute;
                    snapshot.Valid = true;
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("读取系统音量失败：" + ex.Message);
            }

            return snapshot;
        }

        public void Apply(int percent)
        {
            using (var enumerator = new MMDeviceEnumerator())
            {
                var volume = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia).AudioEndpointVolume;
                if (volume.Mute) { volume.Mute = false; }
                volume.MasterVolumeLevelScalar = Math.Max(0f, Math.Min(1f, percent / 100f));
            }
        }

        public void Restore(VolumeSnapshot snapshot)
        {
            if (snapshot == null || !snapshot.Valid) { return; }

            using (var enumerator = new MMDeviceEnumerator())
            {
                var volume = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia).AudioEndpointVolume;
                volume.MasterVolumeLevelScalar = snapshot.MasterVolume;
                volume.Mute = snapshot.Muted;
            }
        }
    }
}
