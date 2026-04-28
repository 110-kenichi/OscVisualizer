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
    internal class Tron : IAudioVisualizer
    {

        public string VisualizerName
        {
            get => "Tron(1982)";
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

        private SceneMeshInstance bodyScene;

        private SceneMeshInstance wallScene;

        private List<SceneMeshInstance> wallScenes = new List<SceneMeshInstance>();

        private List<SceneMeshInstance> floorScenes = new List<SceneMeshInstance>();

        /// <summary>
        /// Initializes a new instance of the TextRender class.
        /// </summary>
        /// <remarks>This constructor sets up the visualizer view for the Tron instance. Use this
        /// constructor when you need to create a new Tron with its default visualizer configuration.</remarks>
        public Tron()
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

            var bodymodel = StlLoader.Load(@"Assets\TRON_Body.stl");
            //bodymodel.NormalizeToUnitCube();
            var bodymesh = MeshBuilder.BuildIndexedMesh(bodymodel, vertexMergeEpsilon: 5e-5f);

            var wallmodel = StlLoader.Load(@"Assets\TRON_Wall.stl");
            var wallmesh = MeshBuilder.BuildIndexedMesh(wallmodel, vertexMergeEpsilon: 5e-5f);

            var floormodel = StlLoader.Load(@"Assets\TRON_Floor.stl");
            var floormesh = MeshBuilder.BuildIndexedMesh(floormodel, vertexMergeEpsilon: 5e-5f);

            var edgemodel = StlLoader.Load(@"Assets\TRON_Edge.stl");
            var edgemesh = MeshBuilder.BuildIndexedMesh(edgemodel, vertexMergeEpsilon: 5e-5f);

            bodyScene = new SceneMeshInstance(bodymesh)
            {
                RotationCenterMode = RotationCenterMode.ModelCenter,
            };

            wallScene = new SceneMeshInstance(wallmesh);
            for (int i = 0; i < 10; i++)
            {
                wallScenes.Add(new SceneMeshInstance(wallmesh)
                {
                    Translation = new Vector3(0, 60f * i, 0f),
                });
            }

            for (int x = 0; x < 4; x++)
            {
                for (int y = 0; y < 4; y++)
                {
                    floorScenes.Add(new SceneMeshInstance(floormesh)
                    {
                        Translation = new Vector3((200f * x) - (200f * 2), (200f * y) - (200f * 2), 0f),
                    });
                }
            }

            var edgeScene1 = new SceneMeshInstance(edgemesh)
            {
                Translation = new Vector3(0f, -800f, 0f),
            };
            var edgeScene2 = new SceneMeshInstance(edgemesh)
            {
                Translation = new Vector3(-200f, -800f, 0f),
            };
            var edgeScene3 = new SceneMeshInstance(edgemesh)
            {
                Translation = new Vector3(200f, -800f, 0f),
            };

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
                SceneTranslation = new Vector3(0, -10f, 30.0f),

                SceneRotationXDeg = -90f,
                SceneRotationYDeg = 0f,
                SceneRotationZDeg = 0f,

                // シーン全体を全モデルの中心で回す
                SceneRotationCenterMode = RotationCenterMode.Origin,
            };

            _renderer.AddInstance(bodyScene);
            _renderer.AddInstance(wallScene);
            foreach (var ws in wallScenes)
                _renderer.AddInstance(ws);
            foreach (var fs in floorScenes)
                _renderer.AddInstance(fs);

            //_renderer.AddInstance(edgeScene1);
            //_renderer.AddInstance(edgeScene2);
            //_renderer.AddInstance(edgeScene3);
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

        // クラスフィールド
        private enum CameraMode { Fixed, Rotating }
        private CameraMode cameraMode = CameraMode.Rotating;
        private double nextCameraModeChangeTime = 0;
        private float fixedYAngle = 0;
        private float rotateYAngle = 0;

        private static readonly Vector3[] CameraPositions = new Vector3[]
        {
            new Vector3(0, -10f, 30.0f),
            new Vector3(0, -30f, 60.0f),
            new Vector3(0, -1, 60.0f),
            new Vector3(0, -1, 30.0f),
            // ...他にも「映える」位置を追加
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
            var time = _sw.Elapsed.TotalMilliseconds;

            //壁をスクロールする
            for (int i = 0; i < wallScenes.Count; i++)
            {
                wallScenes[i].Translation = new Vector3(0, (60f * i) + ((float)(time / 2.5) % 60f), 0f);
            }
            //床をスクロールする
            int floorIndex = 0;
            for (int x = 0; x < 4; x++)
            {
                for (int y = 0; y < 4; y++)
                {
                    floorScenes[floorIndex].Translation = new Vector3((200f * x) - (200f * 2), (200f * y) - (200f * 2) + ((float)(time / 2.5) % 200f), 0f);
                    floorIndex++;
                }
            }
            //カメラの位置をランダムに切り替える
            double now = _sw.Elapsed.TotalSeconds;
            if (now > nextCameraModeChangeTime)
            {
                // 10〜20秒後に次回切り替え
                nextCameraModeChangeTime = now + 10.0 + cameraRandom.NextDouble() * 10.0;

                int next;
                //do
                //{
                //    next = cameraRandom.Next(CameraPositions.Length);
                //} while (next == currentCameraIndex); // 直前と同じ位置は避ける
                next = cameraRandom.Next(CameraPositions.Length);
                currentCameraIndex = next;

                // ランダムでA/B切り替え
                if (cameraRandom.Next(2) == 0)
                {
                    cameraMode = CameraMode.Fixed;
                    fixedYAngle = (float)(1.0 + cameraRandom.NextDouble() * 358.0); // 1〜359度
                }
                else
                {
                    cameraMode = CameraMode.Rotating;
                    rotateYAngle = (float)(7.0 + cameraRandom.NextDouble() * 13.0); // 7〜13度
                }
            }

            // カメラ回転
            if (cameraMode == CameraMode.Fixed)
            {
                _renderer.SceneRotationYDeg = fixedYAngle;
            }
            else
            {
                _renderer.SceneRotationYDeg += dt * rotateYAngle; // ゆっくり回転
            }

            _renderer.SceneTranslation = CameraPositions[currentCameraIndex];
            _renderer.Render(displayDevice);
            return new List<XYPoint>(displayDevice.Points);
        }

    }
}
