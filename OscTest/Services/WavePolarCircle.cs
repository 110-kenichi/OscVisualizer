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
using static System.Windows.Forms.VisualStyles.VisualStyleElement.TaskbarClock;

namespace OscVisualizer.Services
{
    internal class WavePolarCircle : IAudioVisualizer
    {

        public string VisualizerName
        {
            get => "Wave Polar Circle";
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

            List<Point> points = new List<Point>();
            FillPolarWaveform(wav, points, baseR: 0.3f, amp: 1.0f, envelope: envelope);
            return points;
        }

        float envelope = 0f;

        void UpdateEnvelope(float sample, float attack = 10f, float release = 0.2f)
        {
            float rect = MathF.Abs(sample);

            if (rect > envelope)
                envelope += (rect - envelope) * attack;   // Attack
            else
                envelope += (rect - envelope) * release;  // Release
        }

        void FillPolarWaveform(float[] samples, List<Point> pts,
                       float baseR = 0.6f, float amp = 0.6f,
                       float angleMod = 0.02f,
                       float envelope = 0.0f)
        {
            pts.Clear();
            int N = samples.Length;

            for (int i = 0; i < N; i++)
            {
                {
                    float s = samples[i];
                    float theta = 2f * MathF.PI * i / N;

                    // 角度揺らし
                    theta += angleMod * s;

                    // 半径
                    float r = baseR + amp * s + envelope;

                    float x = r * MathF.Cos(theta);
                    float y = r * MathF.Sin(theta);

                    pts.Add(new Point(x, y));
                }
                if (i < N - 1)
                {
                    float s = samples[i + 1];
                    float theta = 2f * MathF.PI * (i + 1) / N;

                    // 角度揺らし
                    theta += angleMod * s;

                    // 半径
                    float r = baseR + amp * s + envelope;

                    float x = r * MathF.Cos(theta);
                    float y = r * MathF.Sin(theta);

                    pts.Add(new Point(x, y));
                }
                else
                {
                    float s = samples[0];
                    float theta = 2f * MathF.PI * 0 / N;

                    // 角度揺らし
                    theta += angleMod * s;

                    // 半径
                    float r = baseR + amp * s + envelope;

                    float x = r * MathF.Cos(theta);
                    float y = r * MathF.Sin(theta);

                    pts.Add(new Point(x, y));
                }
            }
        }

    }
}
