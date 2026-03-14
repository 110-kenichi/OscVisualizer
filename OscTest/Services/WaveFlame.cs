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
    internal class WaveFlame : IAudioVisualizer
    {
        private readonly Stopwatch _sw = Stopwatch.StartNew();

        public string VisualizerName
        {
            get => "Wave Flame";
        }

        public List<Point> ProcessAudio(WasapiCapture capture, WaveInEventArgs ea)
        {
            float[] wav = IAudioVisualizer.ConvertToWav1ch(capture, ea);

            wav = IAudioVisualizer.Downsample8xAverageAVX2(wav);

            float time = (float)_sw.Elapsed.TotalSeconds;

            List<Point> points = GenerateFlameWaveform(wav, time);
            return points;
        }
        public List<Point> GenerateFlameWaveform(
            float[] waveform,
            float time,
            float flameIntensity = 0.15f,
            float noiseFreq = 3.0f,
            float noiseSpeed = 0.8f,
            float stretchAmount = 0.5f)
        {
            int n = waveform.Length;
            var points = new List<Point>();

            for (int i = 0; i < n; i++)
            {
                int j = i;
                {
                    float t = (float)i / (n - 1);

                    // 角度（0〜2π）
                    float theta = t * MathF.Tau;

                    // --- 炎の揺らぎノイズ ---
                    float noise = Simplex.Noise2D(theta * noiseFreq, time * noiseSpeed);

                    // --- 半径 ---
                    float r = 0.4f
                              + waveform[i] * 2f
                              + noise * flameIntensity;

                    // --- 上方向に伸ばす（炎の縦伸び） ---
                    float stretch = 1.0f + MathF.Max(0, MathF.Sin(theta)) * stretchAmount;

                    // --- 極座標 → XY ---
                    float x = MathF.Cos(theta) * r;
                    float y = MathF.Sin(theta) * r * stretch;

                    points.Add(new Point(x, y));
                }
                i++;
                if (i >= n)
                    i = 0;
                {
                    float t = (float)i / (n - 1);

                    // 角度（0〜2π）
                    float theta = t * MathF.Tau;

                    // --- 炎の揺らぎノイズ ---
                    float noise = Simplex.Noise2D(theta * noiseFreq, time * noiseSpeed);

                    // --- 半径 ---
                    float r = 0.4f
                              + waveform[i] * 2f
                              + noise * flameIntensity;

                    // --- 上方向に伸ばす（炎の縦伸び） ---
                    float stretch = 1.0f + MathF.Max(0, MathF.Sin(theta)) * stretchAmount;

                    // --- 極座標 → XY ---
                    float x = MathF.Cos(theta) * r;
                    float y = MathF.Sin(theta) * r * stretch;

                    points.Add(new Point(x, y));
                }
                i = j;
            }

            return points;
        }
    }
}
