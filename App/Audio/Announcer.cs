using System;
using System.Globalization;
using System.IO;
using System.Speech.Synthesis;
using System.Threading;
using NAudio.Wave;

namespace KeRing.App.Audio
{
    internal sealed class AnnounceResult
    {
        public bool Ok;
        public string Message;
    }

    /// <summary>
    /// 播报：提示音 + 语音，播报前后临时提升并恢复系统音量。
    /// 音量恢复放在 finally 里——中途出错也必须还原。
    /// 同一条播报串行执行，避免两段声音叠在一起。
    /// </summary>
    internal sealed class Announcer
    {
        /// <summary>连着念几遍时，两遍之间停多久（毫秒）。太短听着像一句，太长又拖沓。</summary>
        private const int PauseBetweenRepeatsMs = 700;

        private readonly AudioController _audio = new AudioController();
        private readonly object _gate = new object();

        public AnnounceResult Announce(string courseName, AppConfig config)
        {
            lock (_gate)
            {
                var result = new AnnounceResult();
                var snapshot = new VolumeSnapshot();

                try
                {
                    snapshot = _audio.Capture();
                    if (snapshot.Valid)
                    {
                        _audio.Apply(config.AnnounceVolumePercent);
                    }
                    else
                    {
                        Logger.Warn("拿不到系统音量，跳过临时提升");
                    }

                    var text = (config.AnnouncePrefix ?? string.Empty) + courseName +
                               (config.AnnounceSuffix ?? string.Empty);

                    // 连念 N 遍，**每遍都是"先响一次提示音，再念这句话"**（叮—念、叮—念…）
                    var repeat = config.AnnounceRepeatCount;
                    if (repeat < 1) { repeat = 1; }

                    for (var i = 0; i < repeat; i++)
                    {
                        if (i > 0) { Thread.Sleep(PauseBetweenRepeatsMs); }
                        PlayBell();
                        Speak(text);
                    }

                    result.Ok = true;
                    result.Message = repeat > 1
                        ? string.Format("已播报（共 {0} 遍）：{1}", repeat, text)
                        : "已播报：" + text;
                }
                catch (Exception ex)
                {
                    result.Ok = false;
                    result.Message = "播报失败：" + ex.Message;
                    Logger.Error("播报失败", ex);
                }
                finally
                {
                    if (snapshot.Valid)
                    {
                        try
                        {
                            _audio.Restore(snapshot);
                        }
                        catch (Exception ex)
                        {
                            Logger.Error("恢复系统音量失败", ex);
                        }
                    }
                }

                return result;
            }
        }

        public static int CountChineseVoices()
        {
            try
            {
                using (var synthesizer = new SpeechSynthesizer())
                {
                    var count = 0;
                    foreach (var voice in synthesizer.GetInstalledVoices())
                    {
                        var culture = voice.VoiceInfo.Culture;
                        if (culture != null && culture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
                        {
                            count++;
                        }
                    }

                    return count;
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("枚举语音失败：" + ex.Message);
                return 0;
            }
        }

        /// <summary>
        /// 响一次提示音：现场替换过的铃声按文件播（wav/mp3 都认），否则播内嵌的那份。
        /// **单独兜底**——铃声缺失或目录不可写时，不能让整条播报（含语音）一起失败。
        /// 连念 N 遍时每遍都会调它一次（"叮—念"重复 N 次）。
        /// </summary>
        private static void PlayBell()
        {
            try
            {
                var custom = BellTone.CustomFilePath();
                if (custom != null)
                {
                    PlayFile(custom);
                }
                else
                {
                    using (var bell = BellTone.OpenEmbedded())
                    {
                        PlayStream(bell);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("提示音播放失败，改为只播语音", ex);
            }
        }

        private static void PlayStream(Stream stream)
        {
            using (var reader = new WaveFileReader(stream))
            using (var output = new WaveOutEvent())
            using (var finished = new ManualResetEventSlim(false))
            {
                output.PlaybackStopped += (sender, args) => finished.Set();
                output.Init(reader);
                output.Play();

                if (!finished.Wait(TimeSpan.FromSeconds(20)))
                {
                    Logger.Warn("提示音播放超时");
                }
            }
        }

        private static void PlayFile(string path)
        {
            using (var reader = new AudioFileReader(path))
            using (var output = new WaveOutEvent())
            using (var finished = new ManualResetEventSlim(false))
            {
                output.PlaybackStopped += (sender, args) => finished.Set();
                output.Init(reader);
                output.Play();

                if (!finished.Wait(TimeSpan.FromSeconds(20)))
                {
                    Logger.Warn("提示音播放超时：" + path);
                }
            }
        }

        private static void Speak(string text)
        {
            using (var synthesizer = new SpeechSynthesizer())
            {
                synthesizer.SetOutputToDefaultAudioDevice();
                TrySelectChineseVoice(synthesizer);
                synthesizer.Speak(text);
            }
        }

        private static void TrySelectChineseVoice(SpeechSynthesizer synthesizer)
        {
            try
            {
                synthesizer.SelectVoiceByHints(VoiceGender.NotSet, VoiceAge.NotSet, 0, new CultureInfo("zh-CN"));
            }
            catch (Exception ex)
            {
                Logger.Warn("没有可用的中文语音，改用默认音色（可能读不出中文）：" + ex.Message);
            }
        }
    }
}
