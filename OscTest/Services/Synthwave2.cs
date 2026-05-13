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
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms.VisualStyles;
using static System.Windows.Forms.Design.AxImporter;

namespace OscVisualizer.Services
{
    internal class Synthwave2 : IAudioVisualizer
    {

        public string VisualizerName
        {
            get => "Synthwave2";
        }

        private readonly UserControl? _visualizerView;

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

        private List<SceneMeshInstance> tireScenes = new List<SceneMeshInstance>();

        private List<SceneMeshInstance> roadScenes = new List<SceneMeshInstance>();

        /// <summary>
        /// Initializes a new instance of the TextRender class.
        /// </summary>
        /// <remarks>This constructor sets up the visualizer view for the SynthWave2 instance. Use this
        /// constructor when you need to create a new SynthWave2 with its default visualizer configuration.</remarks>
        public Synthwave2()
        {
            _visualizerView = new TextRender3DView();
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
            _settingsViewModel.ThetaX = -90;
            _settingsViewModel.ThetaY = 0;
            _settingsViewModel.ThetaZ = 0;
            _visualizerView?.DataContext = _settingsViewModel;

            // STLファイルパス（適宜変更）

            var bodymesh = MeshBuilder.BuildIndexedMesh(StlLoader.Load(@"Assets\SW2_Testarossa.stl"), vertexMergeEpsilon: 1e-5f);
            var bodyScene = new SceneMeshInstance(bodymesh);

            var tirerlmesh = MeshBuilder.BuildIndexedMesh(StlLoader.Load(@"Assets\SW2_RearL.stl"), vertexMergeEpsilon: 5e-5f);
            tireScenes.Add(new SceneMeshInstance(tirerlmesh) { RotationCenterMode = RotationCenterMode.ModelCenter });
            var tirerrmesh = MeshBuilder.BuildIndexedMesh(StlLoader.Load(@"Assets\SW2_RearR.stl"), vertexMergeEpsilon: 5e-5f);
            tireScenes.Add(new SceneMeshInstance(tirerrmesh) { RotationCenterMode = RotationCenterMode.ModelCenter });
            var tireflmesh = MeshBuilder.BuildIndexedMesh(StlLoader.Load(@"Assets\SW2_FrontL.stl"), vertexMergeEpsilon: 5e-5f);
            tireScenes.Add(new SceneMeshInstance(tireflmesh) { RotationCenterMode = RotationCenterMode.ModelCenter });
            var tirefrmesh = MeshBuilder.BuildIndexedMesh(StlLoader.Load(@"Assets\SW2_FrontR.stl"), vertexMergeEpsilon: 5e-5f);
            tireScenes.Add(new SceneMeshInstance(tirefrmesh) { RotationCenterMode = RotationCenterMode.ModelCenter });

            var roadmesh = MeshBuilder.BuildIndexedMesh(StlLoader.Load(@"Assets\SW2_Road.stl"), vertexMergeEpsilon: 5e-5f);

            var buildmesh = MeshBuilder.BuildIndexedMesh(StlLoader.Load(@"Assets\SW2_Building.stl"), vertexMergeEpsilon: 5e-5f);
            var buildScene = new SceneMeshInstance(buildmesh)
            {
                Translation = new Vector3(0, 3000f, 0f)
            };

            for (int y = -1; y < 5; y++)
            {
                roadScenes.Add(new SceneMeshInstance(roadmesh)
                {
                    Translation = new Vector3(0, 400f * y, 0f),
                });
            }

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
                SceneScale = 0.15f,
                SceneTranslation = new Vector3(0, -10f, 30.0f),

                SceneRotationXDeg = -90f,
                SceneRotationYDeg = 0f,
                SceneRotationZDeg = 0f,

                // シーン全体を全モデルの中心で回す
                SceneRotationCenterMode = RotationCenterMode.Origin,
            };

            _renderer.AddInstance(bodyScene);
            foreach (var ts in tireScenes)
                _renderer.AddInstance(ts);
            foreach (var rs in roadScenes)
                _renderer.AddInstance(rs);

            _renderer.AddInstance(buildScene);
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

        // クラスフィールド
        private double nextCameraModeChangeTime = 0;

        private static readonly float[] SceneYDegs = new float[]
        {
            180,
            270,
            215,
        };
        private int currentCameraIndex = 0;
        private static readonly Random cameraRandom = new Random();

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

            var dt = GetDeltaTime();

            // レンダリング
            var time = _sw.Elapsed.TotalMilliseconds * 0.5;

            foreach (var ts in tireScenes)
            {
                ts.RotationXDeg = (float)(time);
            }

            //床をスクロールする
            for (int y = -1; y < 5; y++)
            {
                roadScenes[y + 1].Translation = new Vector3(0, (400f * y) - ((float)time % 400f), 0f);
            }
            //カメラの位置をランダムに切り替える
            double now = _sw.Elapsed.TotalSeconds;
            if (now > nextCameraModeChangeTime)
            {
                // 10〜20秒後に次回切り替え
                nextCameraModeChangeTime = now + 10.0 + cameraRandom.NextDouble() * 10.0;

                int next = cameraRandom.Next(SceneYDegs.Length);
                currentCameraIndex = next;
            }
            _renderer.SceneRotationYDeg = SceneYDegs[currentCameraIndex];
            _renderer.Render(displayDevice);

            var pts = new List<XYPoint>(displayDevice.Points);
            pts.Add(new XYPoint(-1, -1, 0));
            pts.Add(new XYPoint(1, -1, 0));
            return pts;
        }

    }
}
