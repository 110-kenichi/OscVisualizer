using Avalonia;
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

        public List<Point> ProcessAudio(WasapiCapture capture, WaveInEventArgs ea)
        {
            float[] wav = IAudioVisualizer.ConvertToWav1ch(capture, ea);

            wav = IAudioVisualizer.Downsample8xAverageAVX2(wav);

            // 1. サンプルごとに envelope を更新
            for (int i = 0; i < wav.Length; i++)
            {
                UpdateEnvelope(wav[i]);
            }

            float time = (float)_sw.Elapsed.TotalSeconds;

            float angle = 0.5f + time * 3f + envelope * 8f;

            List<Point> points = TwistedWarp(wav, time, angle);
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

        public List<Point> TwistedWarp(
            float[] audio,
            float time,
            float rotationAngle)
        {
            int N = audio.Length;

            // 回転行列の事前計算
            float cosA = MathF.Cos(rotationAngle);
            float sinA = MathF.Sin(rotationAngle);

            List<Point> points = new();

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

                x = MathF.Cos(theta) * r;
                y = MathF.Sin(theta) * r;

                // -----------------------------
                // 3. Noise Distortion（揺らぎ）
                // -----------------------------
                //float n = (float)noise.Noise(x * 2f, y * 2f, time * 0.2f);

                //x += n * 0.10f;
                //y += n * 0.10f;

                // -----------------------------
                // 4. Rotation（XY 回転）
                // -----------------------------
                float rx = x * cosA - y * sinA;
                float ry = x * sinA + y * cosA;

                x = rx;
                y = ry;

                // -----------------------------
                // 5. Clamp（XYProcessor の範囲）
                // -----------------------------
                x = Math.Clamp(x, -1f, 1f);
                y = Math.Clamp(y, -1f, 1f);

                points.Add(new Point(x, y));
                if (i != 0 && i != N - 1)
                    points.Add(new Point(x, y));
            }

            return points;
        }
    }
}
