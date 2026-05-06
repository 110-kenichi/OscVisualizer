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
    internal class PictureRender3D : IAudioVisualizer
    {

        public string VisualizerName
        {
            get => "Picture Render 3D";
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

        private PictureRender3DViewModel settingsViewModel = new PictureRender3DViewModel();

        // 文字アウトラインをXY平面(Z=0)に配置
        List<XYPoint> basePoints = new List<XYPoint>();

        /// <summary>
        /// Initializes a new instance of the TextRender class.
        /// </summary>
        /// <remarks>This constructor sets up the visualizer view for the TextRender instance. Use this
        /// constructor when you need to create a new TextRender with its default visualizer configuration.</remarks>
        public PictureRender3D()
        {
            _visualizerView = new PictureRender3DView();
            settingsViewModel.PropertyChanged += (sender, e) =>
            {
                if (_visualizerView?.DataContext is PictureRender3DViewModel vm)
                {
                    switch (e.PropertyName)
                    {
                        case nameof(PictureRender3DViewModel.Path):
                            basePoints = PitcureToVectorXYPoints(settingsViewModel.Path, 1f, 0.05f);
                            break;
                        case nameof(PictureRender3DViewModel.Epsilon):
                            basePoints = PitcureToVectorXYPoints(settingsViewModel.Path, 1f, 0.05f);
                            break;
                        case nameof(PictureRender3DViewModel.ThetaX):
                        case nameof(PictureRender3DViewModel.ThetaY):
                        case nameof(PictureRender3DViewModel.ThetaZ):
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

            float kick = MathF.Min(IAudioVisualizer.GetBand(spectrum, 50, 100, sampleRate), 10f);
            //float snare = MathF.Min(IAudioVisualizer.GetBand(spectrum, 1500, 3000, sampleRate), 2f);
            //float hat = MathF.Min(IAudioVisualizer.GetBand(spectrum, 6000, 12000, sampleRate), 2f);

            double scale = 0.25 + kick * 0.125;

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

                projected.Add(new XYPoint(s1.X * scale, s1.Y * scale, basePoints[i].Intensity));
                projected.Add(new XYPoint(s2.X * scale, s2.Y * scale, basePoints[i + 1].Intensity));
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

        private List<XYPoint> PitcureToVectorXYPoints(string path, float scale = 1.0f, float dotWidth = 1.0f)
        {
            var points = new List<XYPoint>();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return points;

            try
            {
                const int targetSize = 256;
                const float threshold = 0.5f;

                using var src = new Bitmap(path);
                using var canvas = new Bitmap(targetSize, targetSize);
                using (var g = Graphics.FromImage(canvas))
                {
                    g.Clear(Color.Black);
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.SmoothingMode = SmoothingMode.None;

                    float ratio = MathF.Min((float)targetSize / src.Width, (float)targetSize / src.Height);
                    int drawW = Math.Max(1, (int)MathF.Round(src.Width * ratio));
                    int drawH = Math.Max(1, (int)MathF.Round(src.Height * ratio));
                    int offsetX = (targetSize - drawW) / 2;
                    int offsetY = (targetSize - drawH) / 2;
                    g.DrawImage(src, new Rectangle(offsetX, offsetY, drawW, drawH));
                }

                float[,] luma = new float[targetSize, targetSize];
                bool[,] on = new bool[targetSize, targetSize];
                for (int y = 0; y < targetSize; y++)
                {
                    for (int x = 0; x < targetSize; x++)
                    {
                        var c = canvas.GetPixel(x, y);
                        float v = (0.299f * c.R + 0.587f * c.G + 0.114f * c.B) / 255f;
                        luma[x, y] = v;
                        on[x, y] = v >= threshold;
                    }
                }

                float unit = (2.0f / targetSize) * scale;
                var rawSegments = new List<(Vector2 A, Vector2 B, float I)>(targetSize * targetSize / 2);

                void AddVectorSeg(float px0, float py0, float px1, float py1, float intensity)
                {
                    float x0 = (px0 - targetSize * 0.5f) * unit;
                    float y0 = (targetSize * 0.5f - py0) * unit;
                    float x1 = (px1 - targetSize * 0.5f) * unit;
                    float y1 = (targetSize * 0.5f - py1) * unit;
                    rawSegments.Add((new Vector2(x0, y0), new Vector2(x1, y1), intensity));
                }

                for (int y = 0; y < targetSize - 1; y++)
                {
                    for (int x = 0; x < targetSize - 1; x++)
                    {
                        bool tl = on[x, y];
                        bool tr = on[x + 1, y];
                        bool br = on[x + 1, y + 1];
                        bool bl = on[x, y + 1];

                        int idx = (tl ? 8 : 0) | (tr ? 4 : 0) | (br ? 2 : 0) | (bl ? 1 : 0);
                        if (idx == 0 || idx == 15)
                            continue;

                        float intensity = Math.Clamp(((luma[x, y] + luma[x + 1, y] + luma[x + 1, y + 1] + luma[x, y + 1]) * 0.25f) * 2.0f, 0.0f, 2.0f);

                        float xm = x + 0.5f;
                        float ym = y + 0.5f;

                        float lx = x;
                        float rx = x + 1f;
                        float ty = y;
                        float by = y + 1f;

                        switch (idx)
                        {
                            case 1:
                            case 14:
                                AddVectorSeg(lx, ym, xm, by, intensity);
                                break;
                            case 2:
                            case 13:
                                AddVectorSeg(xm, by, rx, ym, intensity);
                                break;
                            case 3:
                            case 12:
                                AddVectorSeg(lx, ym, rx, ym, intensity);
                                break;
                            case 4:
                            case 11:
                                AddVectorSeg(xm, ty, rx, ym, intensity);
                                break;
                            case 5:
                                AddVectorSeg(xm, ty, rx, ym, intensity);
                                AddVectorSeg(lx, ym, xm, by, intensity);
                                break;
                            case 6:
                            case 9:
                                AddVectorSeg(xm, ty, xm, by, intensity);
                                break;
                            case 7:
                            case 8:
                                AddVectorSeg(lx, ym, xm, ty, intensity);
                                break;
                            case 10:
                                AddVectorSeg(lx, ym, xm, ty, intensity);
                                AddVectorSeg(xm, by, rx, ym, intensity);
                                break;
                        }
                    }
                }

                if (rawSegments.Count == 0)
                    return points;

                var polylines = BuildPolylines(rawSegments, endpointTolerance: unit * 0.6f);
                float rdpEpsilon = unit * settingsViewModel.Epsilon;

                foreach (var poly in polylines)
                {
                    if (poly.Points.Count < 2)
                        continue;

                    var simplified = SimplifyRdp(poly.Points, rdpEpsilon);
                    if (simplified.Count < 2)
                        continue;

                    for (int i = 0; i < simplified.Count - 1; i++)
                    {
                        var a = simplified[i];
                        var b = simplified[i + 1];
                        if ((b - a).LengthSquared() < 1e-12f)
                            continue;

                        points.Add(new XYPoint(a.X, a.Y, poly.Intensity));
                        points.Add(new XYPoint(b.X, b.Y, poly.Intensity));
                    }
                }
            }
            catch
            {
            }
            return points;
        }

        private sealed class PolylineWithIntensity
        {
            public List<Vector2> Points { get; set; } = new List<Vector2>();
            public float Intensity { get; set; }
        }

        private static List<PolylineWithIntensity> BuildPolylines(List<(Vector2 A, Vector2 B, float I)> segments, float endpointTolerance)
        {
            var result = new List<PolylineWithIntensity>();
            if (segments.Count == 0)
                return result;

            float invTol = 1f / MathF.Max(endpointTolerance, 1e-6f);
            long Key(Vector2 p)
            {
                long x = (long)MathF.Round(p.X * invTol);
                long y = (long)MathF.Round(p.Y * invTol);
                return (x << 32) ^ (y & 0xffffffffL);
            }

            var endpointMap = new Dictionary<long, List<int>>(segments.Count * 2);
            for (int i = 0; i < segments.Count; i++)
            {
                long k0 = Key(segments[i].A);
                long k1 = Key(segments[i].B);

                if (!endpointMap.TryGetValue(k0, out var l0)) endpointMap[k0] = l0 = new List<int>();
                if (!endpointMap.TryGetValue(k1, out var l1)) endpointMap[k1] = l1 = new List<int>();
                l0.Add(i);
                l1.Add(i);
            }

            var used = new bool[segments.Count];

            bool TryExtend(List<Vector2> poly, bool atFront, ref float intensitySum, ref int intensityCount)
            {
                Vector2 end = atFront ? poly[0] : poly[poly.Count - 1];
                long key = Key(end);
                if (!endpointMap.TryGetValue(key, out var candidates))
                    return false;

                for (int ci = 0; ci < candidates.Count; ci++)
                {
                    int si = candidates[ci];
                    if (used[si])
                        continue;

                    var s = segments[si];
                    Vector2 next;
                    if ((s.A - end).LengthSquared() <= endpointTolerance * endpointTolerance)
                        next = s.B;
                    else if ((s.B - end).LengthSquared() <= endpointTolerance * endpointTolerance)
                        next = s.A;
                    else
                        continue;

                    used[si] = true;
                    if (atFront)
                        poly.Insert(0, next);
                    else
                        poly.Add(next);

                    intensitySum += s.I;
                    intensityCount++;
                    return true;
                }

                return false;
            }

            for (int i = 0; i < segments.Count; i++)
            {
                if (used[i])
                    continue;

                var s = segments[i];
                used[i] = true;

                var poly = new List<Vector2> { s.A, s.B };
                float isum = s.I;
                int icount = 1;

                bool progressed;
                do
                {
                    progressed = false;
                    if (TryExtend(poly, atFront: false, ref isum, ref icount)) progressed = true;
                    if (TryExtend(poly, atFront: true, ref isum, ref icount)) progressed = true;
                }
                while (progressed);

                result.Add(new PolylineWithIntensity
                {
                    Points = poly,
                    Intensity = Math.Clamp(isum / Math.Max(1, icount), 0f, 2f)
                });
            }

            return result;
        }

        private static List<Vector2> SimplifyRdp(List<Vector2> pts, float epsilon)
        {
            if (pts.Count <= 2)
                return new List<Vector2>(pts);

            float eps2 = epsilon * epsilon;
            var keep = new bool[pts.Count];
            keep[0] = true;
            keep[pts.Count - 1] = true;

            var stack = new Stack<(int S, int E)>();
            stack.Push((0, pts.Count - 1));

            while (stack.Count > 0)
            {
                var (s, e) = stack.Pop();
                if (e <= s + 1)
                    continue;

                Vector2 a = pts[s];
                Vector2 b = pts[e];
                Vector2 ab = b - a;
                float ab2 = ab.LengthSquared();

                float maxD2 = -1f;
                int maxIdx = -1;

                for (int i = s + 1; i < e; i++)
                {
                    Vector2 p = pts[i];
                    float d2;
                    if (ab2 < 1e-12f)
                    {
                        d2 = (p - a).LengthSquared();
                    }
                    else
                    {
                        float t = Vector2.Dot(p - a, ab) / ab2;
                        t = Math.Clamp(t, 0f, 1f);
                        Vector2 proj = a + ab * t;
                        d2 = (p - proj).LengthSquared();
                    }

                    if (d2 > maxD2)
                    {
                        maxD2 = d2;
                        maxIdx = i;
                    }
                }

                if (maxIdx >= 0 && maxD2 > eps2)
                {
                    keep[maxIdx] = true;
                    stack.Push((s, maxIdx));
                    stack.Push((maxIdx, e));
                }
            }

            var simplified = new List<Vector2>(pts.Count);
            for (int i = 0; i < pts.Count; i++)
            {
                if (keep[i]) simplified.Add(pts[i]);
            }
            return simplified;
        }

        public void SaveSettings()
        {
            try
            {
                var json = JsonSerializer.Serialize(new
                {
                    settingsViewModel.Path,
                    settingsViewModel.ThetaX,
                    settingsViewModel.ThetaY,
                    settingsViewModel.ThetaZ,
                    settingsViewModel.Epsilon
                });

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
                    settingsViewModel.Path = data.Path;
                    settingsViewModel.ThetaX = data.ThetaX;
                    settingsViewModel.ThetaY = data.ThetaY;
                    settingsViewModel.ThetaZ = data.ThetaZ;
                    settingsViewModel.Epsilon = data.Epsilon;
                }
            }
            catch { }
        }

        private class SettingsData
        {
            public string Path { get; set; } = "";
            public float ThetaX { get; set; } = 0;
            public float ThetaY { get; set; } = 0;
            public float ThetaZ { get; set; } = 25f;
            public float Epsilon { get; set; } = 1.2f;
        }

    }
}
