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
    internal class WaveCircle : IAudioVisualizer
    {

        public string VisualizerName
        {
            get => "Wave Circle";
        }

        public List<Point> ProcessAudio(WasapiCapture capture, WaveInEventArgs e)
        {
            float[] wav = IAudioVisualizer.ConvertToWav1ch(capture, e);

            wav = IAudioVisualizer.Downsample8xAverageAVX2(wav);

            List<Point> points = new List<Point>();
            FillCircularWaveform(wav, points, 0.6f, 1f);
            return points;
        }


        void FillCircularWaveform(float[] samples, List<Point> points,
                          float baseRadius = 0.6f, float ampScale = 0.6f)
        {
            int N = samples.Length;

            for (int i = 0; i < N; i++)
            {
                {
                    float s = samples[i]; // -1..1
                    float theta = 2f * MathF.PI * i / N;
                    float r = baseRadius + ampScale * s;

                    float x = r * MathF.Cos(theta);
                    float y = r * MathF.Sin(theta);

                    points.Add(new Point(x, y));
                }
                if (i < N - 1)
                {
                    float s = samples[i + 1]; // -1..1
                    float theta = 2f * MathF.PI * (i + 1) / N;
                    float r = baseRadius + ampScale * s;

                    float x = r * MathF.Cos(theta);
                    float y = r * MathF.Sin(theta);

                    points.Add(new Point(x, y));
                }
                else
                {
                    float s = samples[0]; // -1..1
                    float theta = 2f * MathF.PI * 0 / N;
                    float r = baseRadius + ampScale * s;

                    float x = r * MathF.Cos(theta);
                    float y = r * MathF.Sin(theta);

                    points.Add(new Point(x, y));
                }
            }
        }

    }
}
