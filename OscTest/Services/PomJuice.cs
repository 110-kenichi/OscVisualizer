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
            var topmesh = MeshBuilder.BuildIndexedMesh(topmodel, vertexMergeEpsilon: 5e-5f,
                fillAxis: SurfaceFillAxis.Y, fillDensity: 1f);

            var bdmodel = StlLoader.Load(@"Assets\POM JUICE_Border.stl");
            var bdmesh = MeshBuilder.BuildIndexedMesh(bdmodel, vertexMergeEpsilon: 5e-5f);

            topScene = new SceneMeshInstance(topmesh);
            var bdScene = new SceneMeshInstance(bdmesh);

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
                SceneScale = 0.2f,
                SceneTranslation = new Vector3(0, 0f, 30.0f),

                SceneRotationXDeg = 0f,
                SceneRotationYDeg = 0f,
                SceneRotationZDeg = 0f,

                // シーン全体を全モデルの中心で回す
                SceneRotationCenterMode = RotationCenterMode.ModelCenter,
            };

            _renderer.AddInstance(topScene);
            _renderer.AddInstance(bdScene);
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
                Points.Add(new XYPoint(x0, y0, 0));
                Points.Add(new XYPoint(x1, y1, 0));
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

        private float currentScale = 0.2f;
        private float startScale = 0.2f;
        private float targetScale = 0.25f;
        private double changeStartTime = 0;
        private float changeDuration = 3.0f;
        private double nextChangeTime = 0;

        // タイマー（約30〜60fps）で毎回呼び出す処理
        private void UpdateRotation(float kick, float snare, float hat)
        {
            double now = _sw.Elapsed.TotalSeconds;

            // 10秒前後のランダム間隔で次の変化を開始
            if (nextChangeTime == 0)
            {
                nextChangeTime = now + 8.0 + random.NextDouble() * 4.0; // 8〜12秒
            }
            else if (now >= nextChangeTime)
            {
                targetAngleX = (float)(random.NextDouble() * 25.0 - 12.5);
                targetAngleY = (float)(random.NextDouble() * 40.0 - 20.0);
                targetAngleZ = (float)(random.NextDouble() * 30.0 - 15.0);

                startScale = 0.25f;
                // 変化開始時から終了時までに少しずつ大きくする
                float scaleStep = 0.03f + (float)random.NextDouble() * 0.07f; // +0.03〜+0.10
                targetScale = Math.Clamp(startScale + scaleStep, 0.2f, 0.6f);

                changeStartTime = now;
                changeDuration = 2.5f + (float)random.NextDouble() * 1.5f; // 2.5〜4.0秒
                nextChangeTime = now + 8.0 + random.NextDouble() * 4.0; // 次の8〜12秒
            }

            // 角度は緩やかに補間
            float angleLerp = 1f;
            currentAngleX += (targetAngleX - currentAngleX) * angleLerp;
            currentAngleY += (targetAngleY - currentAngleY) * angleLerp;
            currentAngleZ += (targetAngleZ - currentAngleZ) * angleLerp;

            // スケールは変化区間で少しずつ増やす
            if (changeDuration > 0f)
            {
                float t = Math.Clamp((float)((now - changeStartTime) / changeDuration), 0f, (float)nextChangeTime);
                currentScale = startScale + (targetScale - startScale) * t;
            }

            _renderer.SceneRotationXDeg = currentAngleX;
            _renderer.SceneRotationYDeg = currentAngleY;
            _renderer.SceneRotationZDeg = currentAngleZ;
            _renderer.SceneScale = currentScale;
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

            // レンダリング
            UpdateRotation(kick, snare, hat);
            _renderer.Render(displayDevice);
            return new List<XYPoint>(displayDevice.Points);
        }

    }
}
