using OscVisualizer.Models;
using NAudio.CoreAudioApi;
using NAudio.Dsp;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Text;
using System.Threading.Tasks;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.TaskbarClock;

namespace OscVisualizer.Services
{
    internal class WaveTwistedWarp : IAudioVisualizer
    {
        private readonly Stopwatch _sw = Stopwatch.StartNew();

        public string VisualizerName
        {
            get => "Wave Twisted Warp";
        }

        public List<XYPoint> ProcessAudio(WasapiCapture capture, WaveInEventArgs ea)
        {
            float[] wav = IAudioVisualizer.ConvertToWav1ch(capture, ea);

            wav = IAudioVisualizer.Downsample8xAverageAVX2(wav);

            // 1. サンプルごとに envelope を更新
            for (int i = 0; i < wav.Length; i++)
            {
                UpdateEnvelope(wav[i]);
            }

            float time = (float)_sw.Elapsed.TotalSeconds;

            float angle = 0.5f + time * 3f;

            List<XYPoint> points = TwistedWarp(wav, time, angle, envelope);
            return points;
        }

        float envelope = 0f;

        void UpdateEnvelope(float sample, float attack = 1f, float release = 0.2f)
        {
            float rect = MathF.Abs(sample);

            if (rect > envelope)
                envelope += (rect - envelope) * attack;   // Attack
            else
                envelope += (rect - envelope) * release;  // Release
        }

        private float NoiseFBM(float x, float y, float octaves = 4)
        {
            float sum = 0f;
            float amp = 1f;
            float freq = 1f;

            for (int i = 0; i < octaves; i++)
            {
                sum += Simplex.Noise2D(x * freq, y * freq) * amp;
                freq *= 2f;
                amp *= 0.5f;
            }

            return sum;
        }

        public List<XYPoint> TwistedWarp(
            float[] audio,
            float time,
            float rotationAngle,
            float envelope)
        {
            int N = audio.Length;

            // 回転行列の事前計算
            float cosA = MathF.Cos(rotationAngle);
            float sinA = MathF.Sin(rotationAngle);

            List<XYPoint> points = new();

            // TwistedWarp の中心を揺らす
            //float wobbleX = NoiseFBM(time * 0.7f, 0f) * 0.1f;  // 横揺れ
            //float wobbleY = NoiseFBM(0f, time * 0.9f) * 0.1f;  // 縦揺れ
            float wobbleX = (float)Math.Sin(time) / 20;

            for (int i = 0; i < N; i++)
            {
                float t = (float)i / (N - 1);

                // -----------------------------
                // 1. Time Warp（時間軸ワープ）
                // -----------------------------
                float warpedT = (float)Math.Pow(t - 0.5f, 3) * 4f + 0.5f;
                warpedT = Math.Clamp(warpedT, 0f, 1f);

                float x = warpedT * 2f - 1f;
                float y = audio[i];

                // -----------------------------
                // 2. Twist（空間ねじれ）
                // -----------------------------
                float r = MathF.Sqrt(x * x + y * y);
                float theta = MathF.Atan2(y, x) + r * 10f;

                x = MathF.Cos(theta) * r / 1.5f;
                y = MathF.Sin(theta) * r / 1.5f;

                // -----------------------------
                // 3. Noise Distortion（揺らぎ）
                // -----------------------------
                float n = (float)NoiseFBM(x * 2f, y * 2f, time * 0.1f);

                x += n * 0.01f;
                y += n * 0.01f;

                // -----------------------------
                // 4. Rotation（XY 回転）
                // -----------------------------
                float rx = x * cosA - y * sinA;
                float ry = x * sinA + y * cosA;

                x = rx;
                y = ry;

                // 点を中心基準に移動
                x = x - wobbleX;
                y = y + envelope * 2;

                // -----------------------------
                // 5. Clamp（XYProcessor の範囲）
                // -----------------------------
                x = Math.Clamp(x, -1f, 1f);
                y = Math.Clamp(y, -1f, 1f);

                points.Add(new XYPoint(x, y));
                if (i != 0 && i != N - 1)
                    points.Add(new XYPoint(x, y));
            }

            return points;
        }
    }
}
