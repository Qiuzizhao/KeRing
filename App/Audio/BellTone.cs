using System;
using System.IO;
using System.Text;

namespace KeRing.App.Audio
{
    /// <summary>
    /// 提示音的来源，按优先级：
    ///   1. 数据目录下现场替换过的 audio\ding.wav —— 想换铃声就往那儿放一个同名文件
    ///   2. 内嵌在 exe 里的 Assets\bell.wav —— 仓库里的母本，编译时嵌进去，跟着单文件走
    ///   3. 代码合成的占位"叮"声 —— 前两者都拿不到时的兜底
    /// 返回的流由调用方负责释放。
    /// </summary>
    internal static class BellTone
    {
        private const string ResourceName = "KeRing.Assets.bell.wav";
        private const int SampleRate = 44100;

        /// <summary>现场替换的铃声文件路径（支持 wav/mp3）；没有就返回 null。</summary>
        public static string CustomFilePath()
        {
            try
            {
                var path = AppPaths.BellFile;
                return File.Exists(path) ? path : null;
            }
            catch (Exception ex)
            {
                Logger.Warn("检查自定义铃声失败：" + ex.Message);
                return null;
            }
        }

        /// <summary>内嵌在 exe 里的铃声；拿不到就用代码合成的占位音。返回的流由调用方释放。</summary>
        public static Stream OpenEmbedded()
        {
            var embedded = typeof(BellTone).Assembly.GetManifestResourceStream(ResourceName);
            if (embedded != null) { return embedded; }

            Logger.Warn("内嵌铃声缺失，改用代码合成的占位音");
            return new MemoryStream(ToWav(Render()), false);
        }

        /// <summary>自检用：这个 exe 现在用的是哪一份铃声。</summary>
        public static string DescribeSource()
        {
            var custom = CustomFilePath();
            if (custom != null) { return "自定义文件 " + custom; }

            return typeof(BellTone).Assembly.GetManifestResourceStream(ResourceName) != null
                ? "内嵌在 exe 里"
                : "代码合成的占位音";
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

        private static byte[] ToWav(float[] samples)
        {
            using (var stream = new MemoryStream())
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

                writer.Flush();
                return stream.ToArray();
            }
        }
    }
}
