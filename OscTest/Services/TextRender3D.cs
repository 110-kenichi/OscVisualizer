using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using DynamicData;
using MathNet.Numerics;
using MathNet.Numerics.Distributions;
using MathNet.Numerics.IntegralTransforms;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using OscVisualizer.Models;
using OscVisualizer.ViewModels;
using OscVisualizer.Views;
using System;
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

namespace OscVisualizer.Services
{
    internal class TextRender3D : IAudioVisualizer
    {

        public string VisualizerName
        {
            get => "Text Render 3D";
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

        private TextRender3DViewModel settingsViewModel = new TextRender3DViewModel();

        /// <summary>
        /// Initializes a new instance of the TextRender class.
        /// </summary>
        /// <remarks>This constructor sets up the visualizer view for the TextRender instance. Use this
        /// constructor when you need to create a new TextRender with its default visualizer configuration.</remarks>
        public TextRender3D()
        {
            _visualizerView = new TextRender3DView();
            settingsViewModel.PropertyChanged += (sender, e) =>
            {
                if (_visualizerView?.DataContext is TextRender3DViewModel vm)
                {
                    switch (e.PropertyName)
                    {
                        case nameof(TextRender3DViewModel.Text):
                            vm.Text = settingsViewModel.Text;
                            break;
                        case nameof(TextRender3DViewModel.ThetaX):
                        case nameof(TextRender3DViewModel.ThetaY):
                        case nameof(TextRender3DViewModel.ThetaZ):
                            break;
                    }
                }
            };
            _visualizerView.DataContext = settingsViewModel;
        }

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
            // 3Dパラメータ
            //float radius = 2.5f; // カメラの回転半径
            float camX = 0.0f;   // カメラの
            float camY = 0.0f;   // カメラの高さ
            float camZ = 2.5f;   // カメラの
            float d = 8.0f;      // 投影面までの距離

            float thetaX = (float)(_sw.Elapsed.TotalSeconds * settingsViewModel.ThetaX); // 回転角（速度調整可）
            float thetaY = (float)(_sw.Elapsed.TotalSeconds * settingsViewModel.ThetaY); // 回転角（速度調整可）
            float thetaZ = (float)(_sw.Elapsed.TotalSeconds * settingsViewModel.ThetaZ); // 回転角（速度調整可）
            thetaX = (float)(thetaX * Math.PI / 180); // X軸 (Pitch)
            thetaY = (float)(thetaY * Math.PI / 180); // Y軸 (Yaw)
            thetaZ = (float)(thetaZ * Math.PI / 180); // Z軸 (Roll)

            // カメラ位置
            Quaternion rotation = Quaternion.CreateFromYawPitchRoll(thetaY, thetaX, thetaZ);

            Vector3 rotPos = new Vector3(camX, camY, camZ);
            Vector3 camPos = Vector3.Transform(rotPos, rotation);

            // カメラが原点(0,0,0)を見る
            Vector3 camTarget = Vector3.Zero;
            Vector3 camUp = Vector3.UnitY;
            camUp = Vector3.Transform(camUp, rotation);

            // ビュー行列（カメラ座標系への変換）
            var view = CreateLookAt(camPos, camTarget, camUp);

            // 文字アウトラインをXY平面(Z=0)に配置
            var basePoints = TextToVectorXYPoints(settingsViewModel.Text, 1f, 0.05f);

            var fmt = capture.WaveFormat;
            int channels = fmt.Channels;
            int inputSampleRate = fmt.SampleRate;

            float[] wav = IAudioVisualizer.ConvertToWav1ch(capture, e);

            int sampleRate = fmt.SampleRate;
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

            float kick = MathF.Min(GetBand(spectrum, 50, 100, sampleRate), 10f);
            float snare = MathF.Min(GetBand(spectrum, 1500, 3000, sampleRate), 2f);
            float hat = MathF.Min(GetBand(spectrum, 6000, 12000, sampleRate), 2f);

            double scale = 0.25 + kick * 0.25;

            var projected = new List<XYPoint>();
            for (int i = 0; i < basePoints.Count; i += 2)
            {
                // 線分の2点
                var p1 = new Vector3((float)basePoints[i].X, (float)basePoints[i].Y, 0);
                var p2 = new Vector3((float)basePoints[i + 1].X, (float)basePoints[i + 1].Y, 0);

                // カメラ座標系に変換
                var v1 = Vector3.Transform(p1, view);
                var v2 = Vector3.Transform(p2, view);

                // パースペクティブ投影
                var s1 = ProjectToScreen(v1, d);
                var s2 = ProjectToScreen(v2, d);

                projected.Add(new XYPoint(s1.X * scale, s1.Y * scale, hat));
                projected.Add(new XYPoint(s2.X * scale, s2.Y * scale, hat));
            }
            return projected;
        }

        // カメラビュー行列生成
        private static Matrix4x4 CreateLookAt(Vector3 eye, Vector3 target, Vector3 up)
        {
            var z = Vector3.Normalize(eye - target);
            var x = Vector3.Normalize(Vector3.Cross(up, z));
            var y = Vector3.Cross(z, x);

            return new Matrix4x4(
                x.X, y.X, z.X, 0,
                x.Y, y.Y, z.Y, 0,
                x.Z, y.Z, z.Z, 0,
                -Vector3.Dot(x, eye), -Vector3.Dot(y, eye), -Vector3.Dot(z, eye), 1
            );
        }

        // パースペクティブ投影
        private static Vector2 ProjectToScreen(Vector3 v, float d)
        {
            float z = v.Z + d;
            return new Vector2(v.X * d / z, v.Y * d / z);
        }

        // Ramer–Douglas–Peuckerアルゴリズム
        private static List<PointF> RdpSimplify(List<PointF> points, float epsilon)
        {
            if (points.Count < 3)
                return new List<PointF>(points);

            float maxDist = 0;
            int index = 0;
            for (int i = 1; i < points.Count - 1; i++)
            {
                float dist = PerpendicularDistance(points[i], points[0], points[^1]);
                if (dist > maxDist)
                {
                    index = i;
                    maxDist = dist;
                }
            }
            if (maxDist > epsilon)
            {
                var left = RdpSimplify(points.GetRange(0, index + 1), epsilon);
                var right = RdpSimplify(points.GetRange(index, points.Count - index), epsilon);
                left.RemoveAt(left.Count - 1);
                left.AddRange(right);
                return left;
            }
            else
            {
                return new List<PointF> { points[0], points[^1] };
            }
        }

        private static float PerpendicularDistance(PointF pt, PointF lineStart, PointF lineEnd)
        {
            float dx = lineEnd.X - lineStart.X;
            float dy = lineEnd.Y - lineStart.Y;
            if (dx == 0 && dy == 0)
                return MathF.Sqrt((pt.X - lineStart.X) * (pt.X - lineStart.X) + (pt.Y - lineStart.Y) * (pt.Y - lineStart.Y));
            float t = ((pt.X - lineStart.X) * dx + (pt.Y - lineStart.Y) * dy) / (dx * dx + dy * dy);
            float projX = lineStart.X + t * dx;
            float projY = lineStart.Y + t * dy;
            return MathF.Sqrt((pt.X - projX) * (pt.X - projX) + (pt.Y - projY) * (pt.Y - projY));
        }

        private List<XYPoint> TextToVectorXYPoints(string text, float scale = 1.0f, float spacing = 0.1f)
        {
            var points = new List<XYPoint>();
            if (string.IsNullOrEmpty(text))
                return points;

            using var font = new Font("Arial", 9, FontStyle.Regular, GraphicsUnit.Pixel);
            using var path = new GraphicsPath();
            path.AddString(text, font.FontFamily, (int)font.Style, font.Size, new PointF(0, 0), StringFormat.GenericDefault);

            var pts = path.PathPoints;
            var types = path.PathTypes;

            // サブパスごとに分割
            List<List<PointF>> subpaths = new List<List<PointF>>();
            List<PointF> current = new List<PointF>();
            for (int i = 0; i < pts.Length; i++)
            {
                byte type = types[i];
                byte pointType = (byte)(type & 0x7);
                bool isCloseSubpath = (type & 0x80) != 0;
                if (pointType == 0) // Start
                {
                    if (current.Count > 1)
                        subpaths.Add(current);
                    current = new List<PointF> { pts[i] };
                }
                else if (pointType == 1 || pointType == 3) // Line or CloseSubpath
                {
                    current.Add(pts[i]);
                }
                if (isCloseSubpath && current.Count > 1)
                {
                    // 閉じる
                    current.Add(current[0]);
                    subpaths.Add(current);
                    current = new List<PointF>();
                }
            }
            if (current.Count > 1)
                subpaths.Add(current);

            // 各サブパスにRDP適用
            float epsilon = 0.25f; // 誤差許容値（調整可）
            float maxx = float.MinValue;
            float maxy = float.MaxValue;
            foreach (var sub in subpaths)
            {
                List<PointF> simp;
                if (sub.Count < 10)
                {
                    simp = sub;
                }
                else
                {
                    simp = RdpSimplify(sub, epsilon);
                }
                for (int i = 1; i < simp.Count; i++)
                {
                    float x1 = simp[i - 1].X;
                    float y1 = -simp[i - 1].Y;
                    float x2 = simp[i].X;
                    float y2 = -simp[i].Y;
                    maxx = Math.Max(x1, maxx);
                    maxy = Math.Min(y1, maxy);
                    points.Add(new XYPoint(x1, y1, 1.0));
                    points.Add(new XYPoint(x2, y2, 1.0));
                }
                maxx = Math.Max((float)points[points.Count - 1].X, maxx);
                maxy = Math.Min((float)points[points.Count - 1].Y, maxy);
            }
            for (int i = 0; i < points.Count; i++)
            {
                points[i].X -= maxx / 2;
                points[i].Y -= maxy / 2;
            }
            float max = Math.Max(maxx, -maxy);
            for (int i = 0; i < points.Count; i++)
            {
                points[i].X /= max;
                points[i].Y /= max;
                points[i].X *= scale;
                points[i].Y *= scale;
            }
            return points;
        }

        public void SaveSettings()
        {
            try
            {
                var json = JsonSerializer.Serialize(new { settingsViewModel.Text, settingsViewModel.ThetaX, settingsViewModel.ThetaY, settingsViewModel.ThetaZ });

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
                    settingsViewModel.Text = data.Text;
                    settingsViewModel.ThetaX = data.ThetaX;
                    settingsViewModel.ThetaY = data.ThetaY;
                    settingsViewModel.ThetaZ = data.ThetaZ;
                }
            }
            catch { }
        }

        private class SettingsData
        {
            public string Text { get; set; } = "";
            public float ThetaX { get; set; } = 0;
            public float ThetaY { get; set; } = 0;
            public float ThetaZ { get; set; } = 25f;
        }

    }
}
