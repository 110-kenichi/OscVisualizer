using OscVisualizer.Models;
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

        public List<XYPoint> ProcessAudio(WasapiCapture capture, WaveInEventArgs e)
        {
            int sampleRate = capture.WaveFormat.SampleRate;
            float[] wav = IAudioVisualizer.ConvertToWav1ch(capture, e);

            //ハイパスフィルタ
            for (int i = 0; i < wav.Length; i++)
                wav[i] = HighPass(wav[i]);

            return ProcessAudio(wav, sampleRate);
        }

        private List<XYPoint> ProcessAudio(float[] pcm, int sampleRate)
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
            return SendSpectrumToXY(spectrum, sampleRate);
        }

        private List<XYPoint> SendSpectrumToXY(float[] spectrum, int sampleRate)
        {
            int fftSize = spectrum.Length * 2; // 元の FFT サイズ
            int barCount = 200;                // XY に描くバー数（自由に調整）

            // 周波数レンジ
            float fMin = 100f;
            float fMax = sampleRate / 2f;

            // 出力バー
            float[] bars = new float[barCount];

            for (int b = 0; b < barCount; b++)
            {
                // ★ ログスケールで周波数を割り当て
                float t0 = (float)b / barCount;
                float t1 = (float)(b + 1) / barCount;

                float f0 = fMin * MathF.Pow(fMax / fMin, t0);
                float f1 = fMin * MathF.Pow(fMax / fMin, t1);

                // FFT ビン範囲へ変換
                int i0 = (int)(f0 / fMax * (spectrum.Length - 1));
                int i1 = (int)(f1 / fMax * (spectrum.Length - 1));
                if (i1 <= i0) i1 = i0 + 1;
                if (i1 >= spectrum.Length) i1 = spectrum.Length - 1;

                // ★ このバーの平均振幅
                float sum = 0f;
                int count = 0;
                for (int i = i0; i <= i1; i++)
                {
                    sum += spectrum[i];
                    count++;
                }

                float avg = sum / count;

                // ★ 対数圧縮（高周波が見えるようになる）
                bars[b] = MathF.Log10(1f + avg * 9f);
            }

            // 正規化
            float max = bars.Max();
            if (max < 1e-6f) max = 1e-6f;

            // XYProcessor 用に変換
            List<XYPoint> points = new();

            for (int b = 0; b < barCount-1; b++)
            {
                {
                    float x = (float)b / (barCount - 1); // 0〜1
                    float y = bars[b] / max;             // 0〜1

                    // XYProcessor は -1〜1
                    x = x * 2f - 1f;
                    y = y * 2f - 1f;

                    points.Add(new XYPoint(x, y, 0.5));
                }
                b++;
                {
                    float x = (float)b / (barCount - 1); // 0〜1
                    float y = bars[b] / max;             // 0〜1

                    // XYProcessor は -1〜1
                    x = x * 2f - 1f;
                    y = y * 2f - 1f;

                    points.Add(new XYPoint(x, y, 0.5));
                }
                b--;
            }

            return points;
        }

    }
}
