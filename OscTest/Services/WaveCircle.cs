using Avalonia.Controls;
using Avalonia.Threading;
using DynamicData;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using OscVisualizer.Models;
using OscVisualizer.ViewModels;
using OscVisualizer.Views;
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
    internal class WaveCircle : IAudioVisualizer
    {

        public string VisualizerName
        {
            get => "Wave Circle";
        }

        private UserControl? _visualizerView;

        /// <summary>
        /// 
        /// </summary>
        public UserControl? VisualizerView
        {
            get
            {
                return _visualizerView;
            }
        }

        private readonly Stopwatch _sw = Stopwatch.StartNew();

        private WaveCircleViewModel settingsViewModel = new WaveCircleViewModel();

        /// <summary>
        /// Initializes a new instance of the WaveCircle class.
        /// </summary>
        /// <remarks>This constructor sets up the visualizer view for the WaveCircle instance. Use this
        /// constructor when you need to create a new WaveCircle with its default visualizer configuration.</remarks>
        public WaveCircle()
        {
            _visualizerView = new WaveCircleView();
            settingsViewModel.PropertyChanged += (sender, e) =>
            {
                switch (e.PropertyName)
                {
                    case nameof(WaveCircleViewModel.ParameterN):
                    case nameof(WaveCircleViewModel.ParameterD):
                        if (_visualizerView?.DataContext is WaveCircleViewModel vm)
                        {
                            vm.ParameterN = settingsViewModel.ParameterN;
                            vm.ParameterD = settingsViewModel.ParameterD;
                        }
                        break;
                }
            };
            _visualizerView.DataContext = settingsViewModel;
        }

        public List<XYPoint> ProcessAudio(WasapiCapture capture, WaveInEventArgs e)
        {
            float[] wav = IAudioVisualizer.ConvertToWav1ch(capture, e);

            wav = IAudioVisualizer.Downsample8xAverageAVX2(wav);

            int inputSampleRate = capture.WaveFormat.SampleRate;

            var pts = FillCircularWaveformAsync(wav, inputSampleRate, 0.6f, 1f);

            return pts;
        }

        private List<XYPoint> FillCircularWaveformAsync(float[] samples, int sampleRate, float baseRadius = 0.6f, float ampScale = 0.6f)
        {
            List<XYPoint> points = new List<XYPoint>();
            int N = samples.Length;

            float n = settingsViewModel.ParameterN;
            float d = settingsViewModel.ParameterD;
            float time = (float)_sw.Elapsed.TotalSeconds;
            float angle = 0.5f + time * settingsViewModel.RotationSpeed;
            // 回転行列の事前計算
            float cosA = MathF.Cos(angle);
            float sinA = MathF.Sin(angle);

            for (int i = 0; i < N * d; i++)
            {
                {
                    float s = samples[i % N]; // -1..1
                    float theta = 2f * MathF.PI * i / N;
                    float r = baseRadius + ampScale * s;

                    float x = r * MathF.Cos(theta * n / d) * MathF.Cos(theta);
                    float y = r * MathF.Cos(theta * n / d) * MathF.Sin(theta);

                    float rx = x * cosA - y * sinA;
                    float ry = x * sinA + y * cosA;

                    points.Add(new XYPoint(rx, ry));
                }
                if (i > N)
                    points.Add(points[points.Count - 1]);
            }
            points.Add(points[0]);

            return points;
        }


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
    }
}
