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
using static System.Windows.Forms.VisualStyles.VisualStyleElement.Tab;

namespace OscVisualizer.Services
{
    internal class Moai : IAudioVisualizer
    {

        public string VisualizerName
        {
            get => "Moai";
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

        private SceneMeshInstance bodyaScene;

        private SceneMeshInstance bodybScene;

        private IndexedMesh ringmesh;

        private List<SceneMeshInstance> ringScene = new List<SceneMeshInstance>();

        /// <summary>
        /// Initializes a new instance of the TextRender class.
        /// </summary>
        /// <remarks>This constructor sets up the visualizer view for the Moai instance. Use this
        /// constructor when you need to create a new Moai with its default visualizer configuration.</remarks>
        public Moai()
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

            var bodyamodel = StlLoader.Load(@"Assets\Moai_A.stl");
            var bodyamesh = MeshBuilder.BuildIndexedMesh(bodyamodel, vertexMergeEpsilon: 5e-5f);

            var bodybmodel = StlLoader.Load(@"Assets\Moai_B.stl");
            var bodybmesh = MeshBuilder.BuildIndexedMesh(bodybmodel, vertexMergeEpsilon: 5e-5f);

            var ringmodel = StlLoader.Load(@"Assets\Moai_Ring.stl");
            ringmesh = MeshBuilder.BuildIndexedMesh(ringmodel, vertexMergeEpsilon: 5e-5f);

            bodyaScene = new SceneMeshInstance(bodyamesh);
            bodybScene = new SceneMeshInstance(bodybmesh);

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
                SceneTranslation = new Vector3(0, -6f, 17.0f),

                SceneRotationXDeg = -90f,
                SceneRotationYDeg = 0f,
                SceneRotationZDeg = 0f,

                // シーン全体を全モデルの中心で回す
                SceneRotationCenterMode = RotationCenterMode.Origin,

                SharpEdgeAngleDeg = 50f,
            };

            _renderer.AddInstance(bodyaScene);
            _renderer.AddInstance(bodybScene);
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
                Points.Add(new XYPoint(x0, y0, 0.5));
                Points.Add(new XYPoint(x1, y1, 0.5));
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

        // タイマー（約30〜60fps）で毎回呼び出す処理
        private void UpdateRotation(float kick, float snare, float hat)
        {
            double now = _sw.Elapsed.TotalSeconds;

            // X
            if (Math.Abs(currentAngleX - targetAngleX) < 0.1f)
            {
                if (nextTargetAngleTimeX == 0)
                    nextTargetAngleTimeX = now + 2.0 + random.NextDouble(); // 2～3秒後
                else if (now > nextTargetAngleTimeX)
                {
                    targetAngleX = (float)(random.NextDouble() * 50.0);
                    nextTargetAngleTimeX = 0;
                }
            }
            else
            {
                nextTargetAngleTimeX = 0;
            }

            // Y
            if (Math.Abs(currentAngleY - targetAngleY) < 0.1f)
            {
                if (nextTargetAngleTimeY == 0)
                    nextTargetAngleTimeY = now + 2.0 + random.NextDouble();
                else if (now > nextTargetAngleTimeY)
                {
                    if (random.Next(2) == 0)
                        targetAngleZ = (float)(random.NextDouble() * 170.0 - 175.0); // -175～-5
                    else
                        targetAngleZ = (float)(random.NextDouble() * 170.0 + 5.0);   // 5～175
                    nextTargetAngleTimeY = 0;
                }
            }
            else
            {
                nextTargetAngleTimeY = 0;
            }

            // Z
            if (Math.Abs(currentAngleZ - targetAngleZ) < 0.1f)
            {
                if (nextTargetAngleTimeZ == 0)
                    nextTargetAngleTimeZ = now + 2.0 + random.NextDouble();
                else if (now > nextTargetAngleTimeZ)
                {
                    // -175～-10, 10～175 のいずれかをランダムで選択
                    if (random.Next(2) == 0)
                        targetAngleZ = (float)(random.NextDouble() * 165.0 - 175.0); // -175～-10
                    else
                        targetAngleZ = (float)(random.NextDouble() * 165.0 + 10.0);  // 10～175
                    nextTargetAngleTimeZ = 0;
                }
            }
            else
            {
                nextTargetAngleTimeZ = 0;
            }

            float lerpSpeed = 0.1f + 0.30f * snare;
            currentAngleX += (targetAngleX - currentAngleX) * lerpSpeed;
            currentAngleY += (targetAngleY - currentAngleY) * lerpSpeed;
            currentAngleZ += (targetAngleZ - currentAngleZ) * lerpSpeed;

            bodyaScene.RotationZDeg = currentAngleZ;
            bodybScene.RotationZDeg = currentAngleZ;
        }

        // クラスフィールド
        private double nextHideTime = 0;
        private double hideEndTime = 0;
        private bool isHiding = false;
        private static readonly Random hideRandom = new Random();

        private double nextTargetAngleTimeX = 0;
        private double nextTargetAngleTimeY = 0;
        private double nextTargetAngleTimeZ = 0;

        private float bodyDz = 0;

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

            float kick = MathF.Max(0.5f, MathF.Min(IAudioVisualizer.GetBand(spectrum, 50, 100, inputSampleRate), 20f));
            float snare = MathF.Max(0.5f, IAudioVisualizer.GetBand(spectrum, 1500, 3000, inputSampleRate));
            float hat = MathF.Max(0.5f, IAudioVisualizer.GetBand(spectrum, 6000, 12000, inputSampleRate));
            // レンダリング

            var dt = GetDeltaTime();
            double now = _sw.Elapsed.TotalSeconds;

            if (kick >= 3 && bodyaScene.Translation.Z == 0)
                bodyDz = 3f;
            bodyaScene.Translation = new Vector3(bodyaScene.Translation.X, bodyaScene.Translation.Y, bodyaScene.Translation.Z + bodyDz);
            bodybScene.Translation = new Vector3(bodyaScene.Translation.X, bodyaScene.Translation.Y, bodyaScene.Translation.Z + bodyDz);
            if (bodyaScene.Translation.Z < 0 || (bodyDz < 0 && kick >= 3))
            {
                bodyDz = 0;
                bodyaScene.Translation = new Vector3(bodyaScene.Translation.X, bodyaScene.Translation.Y, 0);
                bodybScene.Translation = new Vector3(bodyaScene.Translation.X, bodyaScene.Translation.Y, 0);
                _renderer.SceneTranslation = new Vector3(0, -7f, 17.0f);
            }
            else
            {
                _renderer.SceneTranslation = new Vector3(0, -6f, 17.0f);
                if (bodyaScene.Translation.Z != 0)
                    bodyDz -= dt * 10;
            }

            // 発射処理
            if (!isHiding)
            {
                if (nextHideTime == 0)
                {
                    // 次の発射間隔
                    nextHideTime = now + 1 / (snare * snare);
                }
                else if (now > nextHideTime)
                {
                    if (Math.Abs(currentAngleZ - targetAngleZ) < 1f)
                    {
                        bodyaScene.Visible = false;
                        bodybScene.Visible = true;
                        isHiding = true;
                        // 一瞬（例: 0.05秒）だけ消灯
                        hideEndTime = now + 0.05;

                        var ring = new SceneMeshInstance(ringmesh)
                        {
                            RotationCenterMode = RotationCenterMode.ModelCenter,
                            RotationZDeg = bodyaScene.RotationZDeg,
                            RotationXDeg = random.Next(45),
                            Translation = new Vector3(0, 0, bodyaScene.Translation.Z),
                        };

                        ringScene.Add(ring);
                        _renderer.AddInstance(ring);
                    }
                    else
                    {
                        // 次の発射間隔
                        nextHideTime = now + 1 / (snare * snare);
                    }
                }
                else
                {
                    bodyaScene.Visible = true;
                    bodybScene.Visible = false;
                }
            }
            else
            {
                if (now > hideEndTime)
                {
                    bodyaScene.Visible = true;
                    bodybScene.Visible = false;
                    isHiding = false;
                    // 次の発射間隔
                    nextHideTime = now + 1 / (snare * snare);
                }
                else
                {
                    bodyaScene.Visible = false;
                    bodybScene.Visible = true;
                }
            }
            for (int i = ringScene.Count - 1; i >= 0; i--)
            {
                var ring = ringScene[i];
                var tdt = dt;
                // 原点からの距離が300以上なら削除
                float dist = MathF.Sqrt(ring.Translation.X * ring.Translation.X + ring.Translation.Y * ring.Translation.Y);
                if (dist >= 300f)
                {
                    _renderer.RemoveInstance(ring);
                    ringScene.RemoveAt(i);
                    continue;
                }
                else if (dist <= 5f)
                {
                    tdt = 0.4f;
                }
                // Z軸→X軸の順で回転を適用
                double rz = (ring.RotationZDeg - 90.0) * Math.PI / 180.0;
                double rx = ring.RotationXDeg * Math.PI / 180.0;

                // 前方ベクトル
                var v = new Vector3(1, 0, 0);
                // Z軸回転
                v = Vector3.Transform(v, Matrix4x4.CreateRotationZ((float)rz));
                // X軸回転
                v = Vector3.Transform(v, Matrix4x4.CreateRotationY((float)rx));

                float dx = v.X * tdt * 50;
                float dy = v.Y * tdt * 50;
                float dz = v.Z * tdt * 50;
                ring.Translation = new Vector3(ring.Translation.X + dx, ring.Translation.Y + dy, ring.Translation.Z + dz);
            }
            UpdateRotation(kick, snare, hat);
            //lightScene.Visible = hat >= 0.75f;
            //_renderer.SceneScale = 0.2f * scale;
            _renderer.Render(displayDevice);
            return new List<XYPoint>(displayDevice.Points);
        }

    }
}
