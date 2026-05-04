using Avalonia;
using Avalonia.Controls;
using Avalonia.Rendering;
using Avalonia.Threading;
using DynamicData;
using MathNet.Numerics;
using MathNet.Numerics.Distributions;
using MathNet.Numerics.IntegralTransforms;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using OpenTK.Windowing.Common.Input;
using OscVisualizer.Models;
using OscVisualizer.ViewModels;
using OscVisualizer.Views;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
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
using System.Windows.Forms.VisualStyles;
using static System.Windows.Forms.Design.AxImporter;

namespace OscVisualizer.Services
{
    internal class PomJuice : IAudioVisualizer
    {

        public string VisualizerName
        {
            get => "POM JUICE";
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

        private TextRender3DViewModel _settingsViewModel = new TextRender3DViewModel();

        private HiddenLineSilhouetteSceneRenderer _renderer;

        private SceneMeshInstance topScene;

        /// <summary>
        /// Initializes a new instance of the PomJuice class.
        /// </summary>
        /// <remarks>This constructor sets up the visualizer view for the PomJuice instance. Use this
        /// constructor when you need to create a new PomJuice with its default visualizer configuration.</remarks>
        public PomJuice()
        {
            //_visualizerView = new TextRender3DView();
            _settingsViewModel.PropertyChanged += (sender, e) =>
            {
                if (_visualizerView?.DataContext is TextRender3DViewModel vm)
                {
                    switch (e.PropertyName)
                    {
                        case nameof(TextRender3DViewModel.Text):
                            break;
                        case nameof(TextRender3DViewModel.ThetaX):
                            _renderer!.SceneRotationXDeg = _settingsViewModel.ThetaX;
                            break;
                        case nameof(TextRender3DViewModel.ThetaY):
                            _renderer!.SceneRotationYDeg = _settingsViewModel.ThetaY;
                            break;
                        case nameof(TextRender3DViewModel.ThetaZ):
                            _renderer!.SceneRotationZDeg = _settingsViewModel.ThetaZ;
                            break;
                    }
                }
            };
            _settingsViewModel.ThetaX = 0;
            _settingsViewModel.ThetaY = 0;
            _settingsViewModel.ThetaZ = 0;
            _visualizerView?.DataContext = _settingsViewModel;

            // STLファイルパス（適宜変更）

            var topmodel = StlLoader.Load(@"Assets\POM JUICE.stl");
            var topmesh = MeshBuilder.BuildIndexedMesh(topmodel, vertexMergeEpsilon: 5e-5f);

            topScene = new SceneMeshInstance(topmesh);

            // ===== シーンレンダラ =====
            _renderer = new HiddenLineSilhouetteSceneRenderer
            {
                // カメラ/投影
                FocalLength = 1.5f,
                ViewportScale = 1.0f,
                NearZ = 0.01f,
                Epsilon = 1e-5f,

                // 出力を [-1,1] に収める
                AutoFitToCrtRange = false,
                AutoFitMargin = 0.95f,

                // 高速化用グリッド
                GridCols = 24,
                GridRows = 24,

                // ===== シーン全体変換 =====
                SceneScale = 0.4f,
                SceneTranslation = new Vector3(0, 0f, 20.0f),

                SceneRotationXDeg = 0f,
                SceneRotationYDeg = 0f,
                SceneRotationZDeg = 0f,

                // シーン全体を全モデルの中心で回す
                SceneRotationCenterMode = RotationCenterMode.ModelCenter,
            };

            _renderer.AddInstance(topScene);
        }

        private class DisplayDevice : IVectorDisplayDevice
        {
            public List<XYPoint> Points
            {
                get;
                init;
            } = new List<XYPoint>();

            public void BeginFrame()
            {
                Points.Clear();
            }

            public void DrawLine(float x0, float y0, float x1, float y1)
            {
                Points.Add(new XYPoint(x0, y0, 0.25));
                Points.Add(new XYPoint(x1, y1, 0.25));
            }

            public void EndFrame()
            {

            }
        }

        private DisplayDevice displayDevice = new DisplayDevice();

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

        private double _lastTime = 0;

        private readonly Stopwatch _sw = Stopwatch.StartNew();

        public float GetDeltaTime()
        {
            double now = _sw.Elapsed.TotalSeconds;
            float delta = (float)(now - _lastTime);
            _lastTime = now;

            return delta;
        }

        private static readonly Random random = new Random(DateTime.Now.Millisecond);

        private float currentAngleX = 0f;  // 現在の角度
        private float targetAngleX = 0f;   // 目標の角度
        private float currentAngleY = 0f;  // 現在の角度
        private float targetAngleY = 0f;   // 目標の角度
        private float currentAngleZ = 0f;  // 現在の角度
        private float targetAngleZ = 0f;   // 目標の角度

        private float currentTopAngleX = 0f;  // 現在の角度
        private float targetTopAngleX = 0f;   // 目標の角度
        private float currentTopAngleY = 0f;  // 現在の角度
        private float targetTopAngleY = 0f;   // 目標の角度

        // タイマー（約30〜60fps）で毎回呼び出す処理
        private void UpdateRotation(float kick, float snare, float hat)
        {
            // 1. 目標角度に近づいたら、新しい目標をランダムに設定（-10〜10度）
            if (Math.Abs(currentAngleX - targetAngleX) < 0.1f)
                targetAngleX = (float)(random.NextDouble() * 50.0 - 25.0);
            if (Math.Abs(currentAngleY - targetAngleY) < 0.1f)
                targetAngleY = (float)(random.NextDouble() * 180.0 - 90.0);
            if (Math.Abs(currentAngleZ - targetAngleZ) < 0.1f)
                targetAngleZ = (float)(random.NextDouble() * 60.0 - 30.0);

            // 2. スムーズに補間する (線形補間の例)
            // 0.05f の値を変えると、追従するスピードが変わります
            float lerpSpeed = 0.1f + 0.15f * snare;
            currentAngleX += (targetAngleX - currentAngleX) * lerpSpeed;
            currentAngleY += (targetAngleY - currentAngleY) * lerpSpeed;
            currentAngleZ += (targetAngleZ - currentAngleZ) * lerpSpeed;

            _renderer.SceneRotationXDeg = currentAngleX;
            _renderer.SceneRotationYDeg = currentAngleY;
            _renderer.SceneRotationZDeg = currentAngleZ;
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

            float kick = MathF.Min(IAudioVisualizer.GetBand(spectrum, 50, 100, inputSampleRate), 20f);
            float snare = MathF.Min(IAudioVisualizer.GetBand(spectrum, 1500, 3000, inputSampleRate), 2f);
            float hat = MathF.Min(IAudioVisualizer.GetBand(spectrum, 6000, 12000, inputSampleRate), 1.5f);
            float scale = 1f + kick / 20f;

            // レンダリング
            UpdateRotation(kick, snare, hat);
            _renderer.SceneScale = 0.2f * scale;
            _renderer.Render(displayDevice);
            return new List<XYPoint>(displayDevice.Points);
        }

    }
}
