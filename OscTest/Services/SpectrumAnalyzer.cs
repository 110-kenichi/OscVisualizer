using Avalonia;
using MathNet.Numerics;
using MathNet.Numerics.IntegralTransforms;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace OscVisualizer.Services
{
    internal class SpectrumAnalyzer : IAudioVisualizer
    {
        private float prevX = 0;
        private float prevY = 0;
        private float R = 0.995f; // カットオフ調整

        private float HighPass(float x)
        {
            float y = x - prevX + R * prevY;
            prevX = x;
            prevY = y;
            return y;
        }

        public string VisualizerName
        {
            get => "Spectrum Analyzer";
        }

        public List<Point> ProcessAudio(WasapiCapture capture, WaveInEventArgs e)
        {
            float[] wav = IAudioVisualizer.ConvertToWav1ch(capture, e);

            //ハイパスフィルタ
            for (int i = 0; i < wav.Length; i++)
                wav[i] = HighPass(wav[i]);

            return ProcessAudio(wav);
        }

        private List<Point> ProcessAudio(float[] pcm)
        {
            // FFT 用に複素数配列へ
            Complex32[] fft = new Complex32[pcm.Length];
            for (int i = 0; i < pcm.Length; i++)
                fft[i] = new Complex32(pcm[i], 0);

            // FFT 実行
            Fourier.Forward(fft, FourierOptions.Matlab);

            // 振幅スペクトルへ
            float[] spectrum = new float[fft.Length / 2];
            for (int i = 0; i < spectrum.Length; i++)
                spectrum[i] = fft[i].Magnitude;

            // XYProcessor 用に変換
            return SendSpectrumToXY(spectrum);
        }

        private List<Point> SendSpectrumToXY(float[] spectrum)
        {
            int n = spectrum.Length;

            // 最大値で正規化
            float max = spectrum.Max();
            if (max < 1e-6f) max = 1e-6f;

            List<Point> points = new();

            for (int i = 0; i < n; i++)
            {
                float x = (float)i / (n - 1);   // 0〜1
                float y = spectrum[i] / max;    // 0〜1

                // XYProcessor は -1〜1 が必要
                x = x * 2f - 1f;
                y = y * 2f - 1f;

                points.Add(new Point(x, y));
            }

            return points;
        }

    }
}
