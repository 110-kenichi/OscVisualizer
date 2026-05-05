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
    internal class SolarSystem : IAudioVisualizer
    {

        public string VisualizerName
        {
            get => "Solar System";
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

        private SceneMeshInstance sun_Scene;

        private SceneMeshInstance mercury_Scene;
        private SceneMeshInstance venus_Scene;
        private SceneMeshInstance earth_Scene;
        private SceneMeshInstance moon_Scene;
        private SceneMeshInstance mars_Scene;
        private SceneMeshInstance jupiter_Scene;
        private SceneMeshInstance saturn_Scene;
        private SceneMeshInstance uranus_Scene;
        private SceneMeshInstance neptune_Scene;
        private SceneMeshInstance ganymede_Scene;

        /// <summary>
        /// Initializes a new instance of the PomJuice class.
        /// </summary>
        /// <remarks>This constructor sets up the visualizer view for the SolarSystem instance. Use this
        /// constructor when you need to create a new SolarSystem with its default visualizer configuration.</remarks>
        public SolarSystem()
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

            var sun_model = StlLoader.Load(@"Assets\Solar System - Sun.stl");
            sun_model.NormalizeToUnitCube();
            var sun_mesh = MeshBuilder.BuildIndexedMesh(sun_model, vertexMergeEpsilon: 5e-5f,
                fillAxis: SurfaceFillAxis.Y, fillDensity: 15f);
            sun_Scene = new SceneMeshInstance(sun_mesh);

            var mercury_model = StlLoader.Load(@"Assets\Solar System - Mercury.stl");
            mercury_model.NormalizeToUnitCube();
            var mercury_mesh = MeshBuilder.BuildIndexedMesh(mercury_model, vertexMergeEpsilon: 5e-5f);
            mercury_Scene = new SceneMeshInstance(mercury_mesh);

            var venus_model = StlLoader.Load(@"Assets\Solar System - Venus.stl");
            venus_model.NormalizeToUnitCube();
            var venus_mesh = MeshBuilder.BuildIndexedMesh(venus_model, vertexMergeEpsilon: 5e-5f);
            venus_Scene = new SceneMeshInstance(venus_mesh);

            var earth_model = StlLoader.Load(@"Assets\Solar System - Earth.stl");
            earth_model.NormalizeToUnitCube();
            var earth_mesh = MeshBuilder.BuildIndexedMesh(earth_model, vertexMergeEpsilon: 5e-5f);
            earth_Scene = new SceneMeshInstance(earth_mesh);

            var moon_model = StlLoader.Load(@"Assets\Solar System - Moon.stl");
            moon_model.NormalizeToUnitCube();
            var moon_mesh = MeshBuilder.BuildIndexedMesh(moon_model, vertexMergeEpsilon: 5e-5f);
            moon_Scene = new SceneMeshInstance(moon_mesh);

            var mars_model = StlLoader.Load(@"Assets\Solar System - Mars.stl");
            mars_model.NormalizeToUnitCube();
            var mars_mesh = MeshBuilder.BuildIndexedMesh(mars_model, vertexMergeEpsilon: 5e-5f);
            mars_Scene = new SceneMeshInstance(mars_mesh);

            var jupiter_model = StlLoader.Load(@"Assets\Solar System - Jupiter.stl");
            jupiter_model.NormalizeToUnitCube();
            var jupiter_mesh = MeshBuilder.BuildIndexedMesh(jupiter_model, vertexMergeEpsilon: 5e-5f);
            jupiter_Scene = new SceneMeshInstance(jupiter_mesh);

            var saturn_model = StlLoader.Load(@"Assets\Solar System - Saturn.stl");
            saturn_model.NormalizeToUnitCube();
            var saturn_mesh = MeshBuilder.BuildIndexedMesh(saturn_model, vertexMergeEpsilon: 5e-5f);
            saturn_Scene = new SceneMeshInstance(saturn_mesh);

            var uranus_model = StlLoader.Load(@"Assets\Solar System - Uranus.stl");
            uranus_model.NormalizeToUnitCube();
            var uranus_mesh = MeshBuilder.BuildIndexedMesh(uranus_model, vertexMergeEpsilon: 5e-5f);
            uranus_Scene = new SceneMeshInstance(uranus_mesh);

            var neptune_model = StlLoader.Load(@"Assets\Solar System - Neptune.stl");
            neptune_model.NormalizeToUnitCube();
            var neptune_mesh = MeshBuilder.BuildIndexedMesh(neptune_model, vertexMergeEpsilon: 5e-5f);
            neptune_Scene = new SceneMeshInstance(neptune_mesh);

            var ganymede_model = StlLoader.Load(@"Assets\Solar System - Ganymede.stl");
            ganymede_model.NormalizeToUnitCube();
            var ganymede_mesh = MeshBuilder.BuildIndexedMesh(ganymede_model, vertexMergeEpsilon: 5e-5f);
            ganymede_Scene = new SceneMeshInstance(ganymede_mesh);


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

                SceneRotationCenterMode = RotationCenterMode.Origin,
            };

            _renderer.AddInstance(sun_Scene);
            _renderer.AddInstance(mercury_Scene);
            _renderer.AddInstance(venus_Scene);
            _renderer.AddInstance(earth_Scene);
            _renderer.AddInstance(moon_Scene);
            _renderer.AddInstance(mars_Scene);
            _renderer.AddInstance(jupiter_Scene);
            _renderer.AddInstance(saturn_Scene);
            _renderer.AddInstance(uranus_Scene);
            _renderer.AddInstance(neptune_Scene);
            _renderer.AddInstance(ganymede_Scene);
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
                Points.Add(new XYPoint(x0, y0));
                Points.Add(new XYPoint(x1, y1));
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

        // タイマー（約30〜60fps）で毎回呼び出す処理
        private void UpdateRotation(float kick, float snare, float hat)
        {
            double now = _sw.Elapsed.TotalSeconds;
            float speedMul = 1;// 0.7f + 0.03f * kick + 0.12f * hat;
            float t = (float)(now * speedMul);

            static Vector3 OrbitXZ(float radius, float period, float time, float phaseDeg = 0f, float y = 0f)
            {
                float phase = phaseDeg * MathF.PI / 180f;
                float a = time * (2f * MathF.PI / MathF.Max(0.01f, period)) + phase;
                return new Vector3(radius * MathF.Cos(a), y, radius * MathF.Sin(a));
            }

            // ===== 太陽（中心） =====
            sun_Scene.Translation = Vector3.Zero;
            sun_Scene.Scale = 1.8f;
            sun_Scene.RotationYDeg = (float)((now * 8.0) % 360.0);

            // ===== 惑星（デフォルメ距離） =====
            var mercuryPos = OrbitXZ(2.0f, 4.0f, t, phaseDeg: 10f);
            var venusPos = OrbitXZ(3.2f, 7.0f, t, phaseDeg: 40f);
            var earthPos = OrbitXZ(4.5f, 10.0f, t, phaseDeg: 80f);
            var marsPos = OrbitXZ(6.0f, 15.0f, t, phaseDeg: 130f);
            var jupiterPos = OrbitXZ(8.2f, 28.0f, t, phaseDeg: 170f);
            var saturnPos = OrbitXZ(10.8f, 36.0f, t, phaseDeg: 220f);
            var uranusPos = OrbitXZ(13.5f, 46.0f, t, phaseDeg: 260f);
            var neptunePos = OrbitXZ(16.0f, 56.0f, t, phaseDeg: 300f);

            mercury_Scene.Translation = mercuryPos;
            venus_Scene.Translation = venusPos;
            earth_Scene.Translation = earthPos;
            mars_Scene.Translation = marsPos;
            jupiter_Scene.Translation = jupiterPos;
            saturn_Scene.Translation = saturnPos;
            uranus_Scene.Translation = uranusPos;
            neptune_Scene.Translation = neptunePos;

            mercury_Scene.Scale = 0.30f;
            venus_Scene.Scale = 0.45f;
            earth_Scene.Scale = 0.48f;
            mars_Scene.Scale = 0.38f;
            jupiter_Scene.Scale = 1.10f;
            saturn_Scene.Scale = 0.95f;
            uranus_Scene.Scale = 0.72f;
            neptune_Scene.Scale = 0.70f;

            mercury_Scene.RotationYDeg = (float)((now * 22.0) % 360.0);
            venus_Scene.RotationYDeg = (float)((now * 18.0) % 360.0);
            earth_Scene.RotationYDeg = (float)((now * 30.0) % 360.0);
            mars_Scene.RotationYDeg = (float)((now * 24.0) % 360.0);
            jupiter_Scene.RotationYDeg = (float)((now * 45.0) % 360.0);
            // 土星の輪がXY平面(水平)なので、自転はZ軸回りにする
            saturn_Scene.RotationYDeg = 0f;
            saturn_Scene.RotationZDeg = (float)((now * 36.0) % 360.0);
            uranus_Scene.RotationYDeg = (float)((now * 28.0) % 360.0);
            neptune_Scene.RotationYDeg = (float)((now * 26.0) % 360.0);

            // ===== 衛星 =====
            var moonOffset = OrbitXZ(0.9f, 3.2f, t * 1.8f, phaseDeg: 45f);
            moon_Scene.Translation = earthPos + moonOffset;
            moon_Scene.Scale = 0.16f;
            moon_Scene.RotationYDeg = (float)((now * 40.0) % 360.0);

            var ganymedeOffset = OrbitXZ(1.25f, 5.2f, t * 1.4f, phaseDeg: 120f);
            ganymede_Scene.Translation = jupiterPos + ganymedeOffset;
            ganymede_Scene.Scale = 0.22f;
            ganymede_Scene.RotationYDeg = (float)((now * 34.0) % 360.0);

            // 全体が見えるスケール
            _renderer.SceneScale = 1f + kick * 0.05f;
            //_renderer.SceneScale = 1f;

            // 少し傾けて見やすくする
            _renderer.SceneRotationXDeg = 18f;
            _renderer.SceneRotationYDeg = (float)((now * 2.0) % 360.0);
            _renderer.SceneRotationZDeg = 0f;
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
