using System;
using System.IO;
using System.Text;

namespace KeRing.App.Audio
{
    /// <summary>
    /// 提示音文件：程序目录下的 audio\ding.wav。
    ///
    /// 编译时会把仓库里的 Assets\bell.wav 放到这个位置，所以铃声是跟着程序走的，
    /// 拷到教室机器上就有。现场要换成学校自己的铃声，直接替换 audio\ding.wav 即可
    /// （已存在的文件不会被覆盖），程序不用改。只有文件缺失时才用下面的代码合成占位音。
    /// </summary>
    internal static class BellTone
    {
        private const int SampleRate = 44100;

        public static string EnsureBellFile()
        {
            AppPaths.EnsureDirectories();
            var path = AppPaths.BellFile;
            if (File.Exists(path)) { return path; }

            WriteWav(path, Render());
            Logger.Info("已生成占位提示音：" + path);
            return path;
        }

        private static float[] Render()
        {
            const double duration = 1.6;
            var count = (int)(SampleRate * duration);
            var samples = new float[count];

            // 三个泛音 + 指数衰减，凑一个"叮"的音色
            double[] partials = { 1046.5, 1568.0, 2093.0 };
            double[] gains = { 1.0, 0.45, 0.20 };
            double[] decays = { 2.2, 3.4, 5.0 };

            for (var i = 0; i < count; i++)
            {
                var t = (double)i / SampleRate;
                double value = 0;
                for (var p = 0; p < partials.Length; p++)
                {
                    value += gains[p] * Math.Exp(-decays[p] * t) * Math.Sin(2 * Math.PI * partials[p] * t);
                }

                var attack = Math.Min(1.0, t / 0.005); // 5ms 淡入，避免爆音
                samples[i] = (float)(value * attack * 0.6);
            }

            return samples;
        }

        private static void WriteWav(string path, float[] samples)
        {
            using (var stream = File.Create(path))
            using (var writer = new BinaryWriter(stream))
            {
                var dataSize = samples.Length * 2;

                writer.Write(Encoding.ASCII.GetBytes("RIFF"));
                writer.Write(36 + dataSize);
                writer.Write(Encoding.ASCII.GetBytes("WAVE"));
                writer.Write(Encoding.ASCII.GetBytes("fmt "));
                writer.Write(16);                  // fmt 块长度
                writer.Write((short)1);            // PCM
                writer.Write((short)1);            // 单声道
                writer.Write(SampleRate);
                writer.Write(SampleRate * 2);      // 字节率
                writer.Write((short)2);            // 块对齐
                writer.Write((short)16);           // 位深
                writer.Write(Encoding.ASCII.GetBytes("data"));
                writer.Write(dataSize);

                foreach (var sample in samples)
                {
                    var clamped = Math.Max(-1f, Math.Min(1f, sample));
                    writer.Write((short)(clamped * short.MaxValue));
                }
            }
        }
    }
}
