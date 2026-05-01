using MathNet.Numerics;
using MathNet.Numerics.IntegralTransforms;
using NAudio.CoreAudioApi;
using NAudio.Dsp;
using NAudio.Wave;
using OscVisualizer.Models;
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
    internal class Kaleidoscope : IAudioVisualizer
    {
        public string VisualizerName
        {
            get => "Kaleidoscope";
        }

        private float prevX = 0;
        private float prevY = 0;
        private float R = 0.995f; // カットオフ調整

        private float _phase = 0f;
        private float _rotation = 0f;
        private float _patternTimer = 0f;
        private int _patternIndex = 0;

        private static void AddSegment(List<XYPoint> points, float x0, float y0, float x1, float y1, float intensity = 0.7f)
        {
            points.Add(new XYPoint(x0, y0, intensity));
            points.Add(new XYPoint(x1, y1, intensity));
        }

        private static Vector2 Rotate(Vector2 p, float angle)
        {
            float c = MathF.Cos(angle);
            float s = MathF.Sin(angle);
            return new Vector2(
                p.X * c - p.Y * s,
                p.X * s + p.Y * c
            );
        }

        private float HighPass(float x)
        {
            float y = x - prevX + R * prevY;
            prevX = x;
            prevY = y;
            return y;
        }

        private double _lastTime = 0;

        private readonly Stopwatch _sw = Stopwatch.StartNew();

        public float GetDeltaTime()
        {
            double now = _sw.Elapsed.TotalSeconds;
            float delta = (float)(now - _lastTime);
            _lastTime = now;

            return delta;
        }

        public List<XYPoint> ProcessAudio(WasapiCapture capture, WaveInEventArgs ea)
        {
            var fmt = capture.WaveFormat;
            int channels = fmt.Channels;
            int inputSampleRate = fmt.SampleRate;

            float[] wav = IAudioVisualizer.ConvertToWav1ch(capture, ea);

            //ハイパスフィルタ
            prevX = 0;
            prevY = 0;
            for (int i = 0; i < wav.Length; i++)
                wav[i] = HighPass(wav[i]);

            // FFT 用に複素数配列へ
            Complex32[] fft = new Complex32[wav.Length];
            for (int i = 0; i < wav.Length; i++)
                fft[i] = new Complex32(wav[i], 0);

            // FFT 実行
            Fourier.Forward(fft, FourierOptions.Matlab);

            // 振幅スペクトルへ
            float[] spectrum = new float[fft.Length / 2];
            for (int i = 0; i < spectrum.Length; i++)
                spectrum[i] = fft[i].Magnitude;

            float kick = MathF.Min(IAudioVisualizer.GetBand(spectrum, 50, 100, inputSampleRate), 20f);
            float snare = MathF.Min(IAudioVisualizer.GetBand(spectrum, 1500, 3000, inputSampleRate), 2.5f);
            float hat = MathF.Min(IAudioVisualizer.GetBand(spectrum, 6000, 12000, inputSampleRate), 2.0f);

            float k = Math.Clamp(kick / 20f, 0f, 1f);
            float s = Math.Clamp(snare / 2.5f, 0f, 1f);
            float h = Math.Clamp(hat / 2.0f, 0f, 1f);

            float dt = GetDeltaTime();
            _phase += dt * (0.8f + 6.0f * h);
            _rotation += dt * (0.2f + 2.5f * s);

            _patternTimer += dt * (0.6f + 1.4f * h + 0.8f * s);
            if (_patternTimer >= 1.8f)
            {
                _patternTimer = 0f;
                _patternIndex = (_patternIndex + 1) % 8;
            }

            int sectors = 6 + (int)(s * 12f);
            int motifSegments = 5 + (int)(k * 14f);
            int layers = 2 + (int)(h * 2f);

            List<XYPoint> points = new List<XYPoint>(sectors * motifSegments * layers * 6);

            for (int layer = 0; layer < layers; layer++)
            {
                float layerMix = layer / (float)Math.Max(1, layers - 1);
                float maxRadius = (0.28f + 0.58f * k) * (0.75f + 0.35f * layerMix);
                float layerPhase = _phase + layer * 0.9f;

                for (int i = 0; i < motifSegments; i++)
                {
                    float t0 = i / (float)motifSegments;
                    float t1 = (i + 1) / (float)motifSegments;

                    float localA0;
                    float localA1;
                    float r0;
                    float r1;

                    if (_patternIndex == 0)
                    {
                        localA0 = (0.12f + 0.88f * t0) * (MathF.PI / sectors) * (0.25f + 0.75f * (0.5f + 0.5f * MathF.Sin(layerPhase * 0.8f + t0 * 8f)));
                        localA1 = (0.12f + 0.88f * t1) * (MathF.PI / sectors) * (0.25f + 0.75f * (0.5f + 0.5f * MathF.Sin(layerPhase * 0.8f + t1 * 8f)));
                        r0 = 0.04f + maxRadius * t0 + 0.10f * h * MathF.Sin(layerPhase * 2.2f + t0 * 18f);
                        r1 = 0.04f + maxRadius * t1 + 0.10f * h * MathF.Sin(layerPhase * 2.2f + t1 * 18f);
                    }
                    else if (_patternIndex == 1)
                    {
                        localA0 = (0.08f + 0.92f * t0) * (MathF.PI / sectors) * (0.4f + 0.6f * MathF.Abs(MathF.Sin(layerPhase * 1.3f + t0 * 10f)));
                        localA1 = (0.08f + 0.92f * t1) * (MathF.PI / sectors) * (0.4f + 0.6f * MathF.Abs(MathF.Sin(layerPhase * 1.3f + t1 * 10f)));
                        r0 = 0.03f + maxRadius * (0.6f * t0 + 0.4f * t0 * t0) + 0.07f * k * MathF.Cos(layerPhase * 2.7f + t0 * 24f);
                        r1 = 0.03f + maxRadius * (0.6f * t1 + 0.4f * t1 * t1) + 0.07f * k * MathF.Cos(layerPhase * 2.7f + t1 * 24f);
                    }
                    else if (_patternIndex == 2)
                    {
                        localA0 = (0.18f + 0.82f * t0) * (MathF.PI / sectors) * (0.3f + 0.7f * (0.5f + 0.5f * MathF.Cos(layerPhase + t0 * 14f)));
                        localA1 = (0.18f + 0.82f * t1) * (MathF.PI / sectors) * (0.3f + 0.7f * (0.5f + 0.5f * MathF.Cos(layerPhase + t1 * 14f)));
                        r0 = 0.02f + maxRadius * t0 + 0.12f * s * MathF.Sin(layerPhase * 3.4f + t0 * 30f) * (1f - t0 * 0.6f);
                        r1 = 0.02f + maxRadius * t1 + 0.12f * s * MathF.Sin(layerPhase * 3.4f + t1 * 30f) * (1f - t1 * 0.6f);
                    }
                    else if (_patternIndex == 3)
                    {
                        localA0 = (0.1f + 0.9f * t0) * (MathF.PI / sectors) * (0.35f + 0.65f * (0.5f + 0.5f * MathF.Sin(layerPhase * 0.6f + t0 * 6f + k * 4f)));
                        localA1 = (0.1f + 0.9f * t1) * (MathF.PI / sectors) * (0.35f + 0.65f * (0.5f + 0.5f * MathF.Sin(layerPhase * 0.6f + t1 * 6f + k * 4f)));
                        r0 = 0.05f + maxRadius * t0 + 0.09f * (k * 0.5f + h * 0.5f) * MathF.Sin(layerPhase * 4.0f + t0 * 20f);
                        r1 = 0.05f + maxRadius * t1 + 0.09f * (k * 0.5f + h * 0.5f) * MathF.Sin(layerPhase * 4.0f + t1 * 20f);
                    }
                    else if (_patternIndex == 4)
                    {
                        localA0 = (0.14f + 0.86f * t0) * (MathF.PI / sectors) * (0.28f + 0.72f * MathF.Abs(MathF.Cos(layerPhase * 1.7f + t0 * 12f)));
                        localA1 = (0.14f + 0.86f * t1) * (MathF.PI / sectors) * (0.28f + 0.72f * MathF.Abs(MathF.Cos(layerPhase * 1.7f + t1 * 12f)));
                        r0 = 0.03f + maxRadius * t0 + 0.11f * h * MathF.Cos(layerPhase * 3.1f + t0 * 22f);
                        r1 = 0.03f + maxRadius * t1 + 0.11f * h * MathF.Cos(layerPhase * 3.1f + t1 * 22f);
                    }
                    else if (_patternIndex == 5)
                    {
                        localA0 = (0.06f + 0.94f * t0) * (MathF.PI / sectors) * (0.45f + 0.55f * (0.5f + 0.5f * MathF.Sin(layerPhase * 2.0f + t0 * 9f)));
                        localA1 = (0.06f + 0.94f * t1) * (MathF.PI / sectors) * (0.45f + 0.55f * (0.5f + 0.5f * MathF.Sin(layerPhase * 2.0f + t1 * 9f)));
                        r0 = 0.02f + maxRadius * (t0 * t0) + 0.13f * s * MathF.Sin(layerPhase * 5.0f + t0 * 28f);
                        r1 = 0.02f + maxRadius * (t1 * t1) + 0.13f * s * MathF.Sin(layerPhase * 5.0f + t1 * 28f);
                    }
                    else if (_patternIndex == 6)
                    {
                        localA0 = (0.2f + 0.8f * t0) * (MathF.PI / sectors) * (0.3f + 0.7f * (0.5f + 0.5f * MathF.Sin(layerPhase * 0.9f + t0 * 15f + h * 5f)));
                        localA1 = (0.2f + 0.8f * t1) * (MathF.PI / sectors) * (0.3f + 0.7f * (0.5f + 0.5f * MathF.Sin(layerPhase * 0.9f + t1 * 15f + h * 5f)));
                        r0 = 0.04f + maxRadius * t0 + 0.08f * (k + s) * 0.5f * MathF.Cos(layerPhase * 2.4f + t0 * 34f);
                        r1 = 0.04f + maxRadius * t1 + 0.08f * (k + s) * 0.5f * MathF.Cos(layerPhase * 2.4f + t1 * 34f);
                    }
                    else
                    {
                        localA0 = (0.1f + 0.9f * t0) * (MathF.PI / sectors) * (0.22f + 0.78f * (0.5f + 0.5f * MathF.Cos(layerPhase * 1.1f + t0 * 11f)));
                        localA1 = (0.1f + 0.9f * t1) * (MathF.PI / sectors) * (0.22f + 0.78f * (0.5f + 0.5f * MathF.Cos(layerPhase * 1.1f + t1 * 11f)));
                        r0 = 0.03f + maxRadius * (0.75f * t0 + 0.25f * MathF.Sqrt(MathF.Max(0f, t0))) + 0.10f * k * MathF.Sin(layerPhase * 3.8f + t0 * 26f);
                        r1 = 0.03f + maxRadius * (0.75f * t1 + 0.25f * MathF.Sqrt(MathF.Max(0f, t1))) + 0.10f * k * MathF.Sin(layerPhase * 3.8f + t1 * 26f);
                    }

                    var p0 = new Vector2(r0 * MathF.Cos(localA0), r0 * MathF.Sin(localA0));
                    var p1 = new Vector2(r1 * MathF.Cos(localA1), r1 * MathF.Sin(localA1));
                    var p0m = new Vector2(p0.X, -p0.Y);
                    var p1m = new Vector2(p1.X, -p1.Y);

                    float intensity = 0.3f + 0.7f * (0.45f * k + 0.35f * s + 0.2f * h);

                    for (int n = 0; n < sectors; n++)
                    {
                        float a = _rotation + layer * 0.3f + (2f * MathF.PI * n / sectors);

                        var q0 = Rotate(p0, a);
                        var q1 = Rotate(p1, a);
                        AddSegment(points, q0.X, q0.Y, q1.X, q1.Y, intensity);

                        var q0m = Rotate(p0m, a);
                        var q1m = Rotate(p1m, a);
                        AddSegment(points, q0m.X, q0m.Y, q1m.X, q1m.Y, intensity * 0.92f);

                        if (k > 0.25f && i % 2 == 0)
                        {
                            AddSegment(points, q0.X, q0.Y, q0m.X, q0m.Y, intensity * (0.4f + 0.5f * k));
                        }
                    }
                }
            }

            return points;
        }
        
    }
}
