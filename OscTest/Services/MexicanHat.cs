using Avalonia.Controls;
using Avalonia.Threading;
using DynamicData;
using MathNet.Numerics;
using MathNet.Numerics.IntegralTransforms;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using OscVisualizer.Models;
using OscVisualizer.ViewModels;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace OscVisualizer.Services
{
    internal class MexicanHat : IAudioVisualizer
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

        private readonly Stopwatch _sw = Stopwatch.StartNew();

        public string VisualizerName
        {
            get => "Mexican Hat";
        }

        /// <summary>
        /// Initializes a new instance of the MexicanHat class.
        /// </summary>
        /// <remarks>This constructor sets up the visualizer view for the MexicanHat instance. Use this
        /// constructor when you need to create a new MexicanHat with its default visualizer configuration.</remarks>
        public MexicanHat()
        {

        }

        private double _lastTime = 0;

        public float GetDeltaTime()
        {
            double now = _sw.Elapsed.TotalSeconds;
            float delta = (float)(now - _lastTime);
            _lastTime = now;

            return delta;
        }

        private static float GetBand(float[] fft, int start, int end, int sampleRate)
        {
            int fftSize = fft.Length;
            float binHz = sampleRate / (float)fftSize;

            int i0 = (int)(start / binHz);
            int i1 = (int)(end / binHz);

            float sum = 0;
            for (int i = i0; i <= i1 && i < fftSize; i++)
                sum += fft[i];

            return sum / (i1 - i0 + 1);
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

            float t = (float)_sw.Elapsed.TotalSeconds;

            return DrawMexicanHatAsync(spectrum, t, GetDeltaTime(), inputSampleRate);
        }

        private List<XYPoint> DrawMexicanHatAsync(float[] fft, float time, float deltaTime, int sampleRate)
        {
            var points = new List<XYPoint>();
            int step = 12;

            float kick = MathF.Min(GetBand(fft, 50, 100, sampleRate), 20f);
            float snare = MathF.Min(GetBand(fft, 1500, 3000, sampleRate), 2f);
            float hat = MathF.Min(GetBand(fft, 6000, 12000, sampleRate), 1.5f);

            double scale = 0.5 + snare * 0.25;
            double zScale = 1.0 + kick * 0.15;
            double hatMod = 1.0 + hat * 0.5;

            double rot = time * 0.75; // 土台回転

            double maxAbsX = 0, maxAbsY = 0;
            // 最大値計算
            for (int y = -180; y <= 180; y += step)
            {
                for (int x = -180; x <= 180; x += step)
                {
                    double xr = x * Math.Cos(rot) - y * Math.Sin(rot);
                    double yr = x * Math.Sin(rot) + y * Math.Cos(rot);

                    double r = Math.Sqrt(xr * xr + yr * yr);
                    double rRad = r * (Math.PI / 180.0);
                    double z = (100.0 * Math.Cos(rRad) - 30.0 * Math.Cos(3.0 * rRad * hatMod)) * zScale;

                    double screenX = (xr - yr) * Math.Cos(Math.PI / 6);
                    double screenY = (xr + yr) * Math.Sin(Math.PI / 6) - z;

                    maxAbsX = Math.Max(maxAbsX, Math.Abs(screenX));
                    maxAbsY = Math.Max(maxAbsY, Math.Abs(screenY));
                }
            }
            // 点列生成
            for (int y = -180; y <= 180; y += step)
            {
                for (int x = -180; x <= 180; x += step)
                {
                    double xr = x * Math.Cos(rot) - y * Math.Sin(rot);
                    double yr = x * Math.Sin(rot) + y * Math.Cos(rot);

                    double r = Math.Sqrt(xr * xr + yr * yr);
                    double rRad = r * (Math.PI / 180.0);
                    double z = (100.0 * Math.Cos(rRad) - 30.0 * Math.Cos(3.0 * rRad * hatMod)) * zScale;

                    double screenX = (xr - yr) * Math.Cos(Math.PI / 6);
                    double screenY = (xr + yr) * Math.Sin(Math.PI / 6) - z;

                    double normX = (screenX / maxAbsX) * scale;
                    double normY = (screenY / maxAbsY) * scale;

                    points.Add(new XYPoint(normX, -normY, 1.0));
                }
                y += step;
                for (int x = 180; x >= -180; x -= step)
                {
                    double xr = x * Math.Cos(rot) - y * Math.Sin(rot);
                    double yr = x * Math.Sin(rot) + y * Math.Cos(rot);

                    double r = Math.Sqrt(xr * xr + yr * yr);
                    double rRad = r * (Math.PI / 180.0);
                    double z = (100.0 * Math.Cos(rRad) - 30.0 * Math.Cos(3.0 * rRad * hatMod)) * zScale;

                    double screenX = (xr - yr) * Math.Cos(Math.PI / 6);
                    double screenY = (xr + yr) * Math.Sin(Math.PI / 6) - z;

                    double normX = (screenX / maxAbsX) * scale;
                    double normY = (screenY / maxAbsY) * scale;

                    points.Add(new XYPoint(normX, -normY, 1.0));
                }
            }
            return points;
        }

        /// <summary>
        /// メキシカンハット関数（Rickerウェーブレット）の値を計算します。
        /// </summary>
        /// <param name="t">位置（時間軸）</param>
        /// <param name="sigma">波の広がり（標準偏差に相当）</param>
        /// <returns>計算された振幅</returns>
        public static double Calculate(double t, double sigma = 1.0)
        {
            double t2 = t * t;
            double sigma2 = sigma * sigma;

            // 定数部分: 2 / (sqrt(3 * sigma) * pi^0.25)
            double normalization = 2.0 / (Math.Sqrt(3.0 * sigma) * Math.Pow(Math.PI, 0.25));

            double mainPart = (1.0 - (t2 / sigma2)) * Math.Exp(-t2 / (2.0 * sigma2));

            return normalization * mainPart;
        }

        /*
        public void SaveSettings()
        {
            try
            {
                var json = JsonSerializer.Serialize(new { settingsViewModel.ParameterN, settingsViewModel.ParameterD, settingsViewModel.RotationSpeed });

                string settingsPath = IAudioVisualizer.GetSettingsPath(VisualizerName);

                File.WriteAllText(settingsPath, json);
            }
            catch { }
        }

        public void LoadSettings()
        {
            try
            {
                string settingsPath = IAudioVisualizer.GetSettingsPath(VisualizerName);

                if (!File.Exists(settingsPath))
                    return;

                var json = File.ReadAllText(settingsPath);
                var data = JsonSerializer.Deserialize<SettingsData>(json);

                if (data != null)
                {
                    settingsViewModel.ParameterN = data.ParameterN;
                    settingsViewModel.ParameterD = data.ParameterD;
                    settingsViewModel.RotationSpeed = data.RotationSpeed;
                }
            }
            catch { }
        }

        private class SettingsData
        {
            public int ParameterN { get; set; }
            public int ParameterD { get; set; }
            public float RotationSpeed { get; set; }

        }
        */
    }
}
