using Avalonia;
using DynamicData;
using MathNet.Numerics;
using MathNet.Numerics.IntegralTransforms;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using OscVisualizer.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace OscVisualizer.Services
{
    internal class BandLevelMeter : IAudioVisualizer
    {
        private const int bars = 16;

        private float[] peak = new float[bars];

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
            get => "Band Level Meter";
        }

        public List<XYPoint> ProcessAudio(WasapiCapture capture, WaveInEventArgs e)
        {
            var fmt = capture.WaveFormat;
            int channels = fmt.Channels;
            int inputSampleRate = fmt.SampleRate;

            float[] wav = IAudioVisualizer.ConvertToWav1ch(capture, e);

            //ハイパスフィルタ
            prevX = 0;
            prevY = 0;
            for (int i = 0; i < wav.Length; i++)
                wav[i] = HighPass(wav[i]);

            //2. FFT（Math.NET が最も簡単
            int fftSize = 2048;
            if (wav.Length < fftSize)
            {
                // 足りない分はゼロパディング
                float[] padded = new float[fftSize];
                Array.Copy(wav, padded, wav.Length);
                wav = padded;
            }

            Complex[] fftBuffer = new Complex[fftSize];
            for (int i = 0; i < fftSize; i++)
            {
                fftBuffer[i] = new Complex(wav[i], 0);
            }

            Fourier.Forward(fftBuffer, FourierOptions.Matlab);

            //3. FFT → 周波数レベル（バー本数に集約）
            float[] levels = new float[bars];

            float f0 = 100f;                 // 最低周波数
            float f1 = 44100 / 2f;     // ナイキスト
            float binHz = inputSampleRate / (float)fftSize;

            for (int i = 0; i < bars; i++)
            {
                // ★ ログスケールで周波数帯を決める
                float fStart = f0 * MathF.Pow(f1 / f0, (float)i / bars);
                float fEnd = f0 * MathF.Pow(f1 / f0, (float)(i + 1) / bars);

                int start = (int)(fStart / binHz);
                int end = (int)(fEnd / binHz);

                // DC除去 & 範囲補正
                if (start < 1) start = 1;
                if (end > fftSize / 2) end = fftSize / 2;
                if (end <= start) end = start + 1;

                double sum = 0;
                for (int j = start; j < end; j++)
                    sum += fftBuffer[j].Magnitude;

                float avg = (float)(sum / (end - start));

                // Log10 圧縮（0〜1に収まる）
                float v = MathF.Log10(1 + avg * 0.25f);

                // ガンマ補正で小音量の反応を弱める
                v = MathF.Pow(v, 1.4f);   // ← ここを調整

                levels[i] = v;
            }

            ////4. レベルを 0〜1 に正規
            //float max = levels.Max();
            //for (int i = 0; i < bars; i++)
            //    levels[i] /= max;

            //レベル計算後にピークホールド処理を入れる
            float decay = 0.01f; // 減衰速度（0.005〜0.02 が使いやすい）
            for (int i = 0; i < bars; i++)
            {
                // 上昇は即時
                if (levels[i] > peak[i])
                    peak[i] = levels[i];
                else
                    peak[i] -= decay; // 下降はゆっくり

                // 下限クリップ
                if (peak[i] < 0)
                    peak[i] = 0;
            }

            //5. XYProcessor 用の座標列に変
            List<XYPoint> xy = new List<XYPoint>();
            int thickness = 3; // 太さ（線の本数）
            float barWidth = 1.5f / bars; // 1バーの幅（-1〜1）
            for (int i = 0; i < bars; i++)
            {
                float centerX = (float)i / bars * 2f - 1f;
                float y = levels[i] * 1.5f - 1f;
                float intensity = (y + 2f);

                for (int t = 0; t < thickness; t++)
                {
                    float offset = ((float)t / (thickness - 1) - 0f) * barWidth * 0.8f;
                    float x = centerX + offset;

                    // 下端 → 上端
                    xy.Add(new XYPoint(x, -1, intensity));
                    xy.Add(new XYPoint(x, y, intensity));
                }
                // ピークホールドの高さ
                float py = peak[i] * 1.5f - 1f;
                if (py > -1f)
                {
                    // 横線（短い線分）
                    float wd = barWidth * 0.8f;
                    xy.Add(new XYPoint(centerX, py, intensity));
                    xy.Add(new XYPoint(centerX + wd, py, intensity));
                }
            }

            return xy;
        }

    }
}
