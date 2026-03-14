using Avalonia;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Text;
using System.Threading.Tasks;

namespace OscVisualizer.Services
{
    internal class WaveFlow3D : IAudioVisualizer
    {

        public string VisualizerName
        {
            get => "Wave Flow 3D";
        }

        const int MaxHistory = 15;
        List<float[]> history = new List<float[]>(MaxHistory);

        // 波形を追加
        void PushWaveform(float[] waveform)
        {
            history.Insert(0, waveform);
            if (history.Count > MaxHistory)
                history.RemoveAt(history.Count - 1);
        }



        float[] Downsample4xAverage(float[] src)
        {
            int outLen = src.Length / 4;
            float[] dst = new float[outLen];

            int i = 0;
            int o = 0;

            // SIMD で 8要素ずつ処理（= 2グループ分）
            int simdCount = (outLen / 2) * 2;

            for (; o < simdCount; o += 2, i += 8)
            {
                // 8要素ロード
                var v = new Vector<float>(src, i);

                // v = [a0 a1 a2 a3 a4 a5 a6 a7]
                // 前半4つと後半4つを平均化
                float avg0 = (v[0] + v[1] + v[2] + v[3]) * 0.25f;
                float avg1 = (v[4] + v[5] + v[6] + v[7]) * 0.25f;

                dst[o] = avg0;
                dst[o + 1] = avg1;
            }

            // 端数（SIMD で処理できない分）
            for (; o < outLen; o++, i += 4)
            {
                dst[o] = (src[i] + src[i + 1] + src[i + 2] + src[i + 3]) * 0.25f;
            }

            return dst;
        }

        public List<Point> ProcessAudio(WasapiCapture capture, WaveInEventArgs e)
        {
            float[] wav = IAudioVisualizer.ConvertToWav1ch(capture,e, 32);

            PushWaveform(wav);

            //PushWaveform(IAudioVisualizer.
            //    Downsample8xAverageAVX2(IAudioVisualizer.
            //    Downsample8xAverageAVX2(IAudioVisualizer.
            //    Downsample8xAverageAVX2(wav))));

            List<Point> points = new List<Point>();

            for (int z = 0; z < history.Count; z++)
            {
                float[] wave = history[z];
                float depth = (float)z / MaxHistory;

                // パース係数
                float scale = 1f - depth * 0.6f;     // 奥ほど小さく
                float offsetY = depth * 0.8f - 0.4f; // 奥→手前の移動

                for (int i = 0; i < wave.Length - 1; i++)
                {
                    // -1〜1 に正規化された波形を想定
                    {
                        float x = (i / (float)(wave.Length - 1)) * 2f - 1f;
                        float y = wave[i];

                        // パース投影
                        float xp = x * scale;
                        float yp = y * (scale * 2) + offsetY;

                        points.Add(new Point(xp, yp));
                    }
                    {
                        float x = ((i + 1) / (float)(wave.Length - 1)) * 2f - 1f;
                        float y = wave[i + 1];

                        // パース投影
                        float xp = x * scale;
                        float yp = y * (scale * 2) + offsetY;

                        points.Add(new Point(xp, yp));
                    }
                }
            }

            return points;
        }
    }
}
