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
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace OscVisualizer.Services
{
    internal class PictureRender3D : IAudioVisualizer
    {
        public string VisualizerName => "Picture Render 3D";

        private UserControl? _visualizerView;

        public UserControl? VisualizerView => _visualizerView;

        private readonly Stopwatch _sw = Stopwatch.StartNew();
        private PictureRender3DViewModel settingsViewModel = new PictureRender3DViewModel();

        // 文字アウトラインをXY平面(Z=0)に配置
        private List<XYPoint> basePoints = new List<XYPoint>();
        private List<List<XYPoint>> animationFrames = new List<List<XYPoint>>();
        private List<int> frameDurations = new List<int>(); // GIFフレームの遅延時間（単位: 10ms）
        private double _frameTime = 0;
        private int _currentFrameIndex = 0;
        private Stopwatch _lastFrameTimeStopwatch = Stopwatch.StartNew();

        public PictureRender3D()
        {
            _visualizerView = new PictureRender3DView();
            settingsViewModel.PropertyChanged += (sender, e) =>
            {
                if (_visualizerView?.DataContext is PictureRender3DViewModel vm)
                {
                    switch (e.PropertyName)
                    {
                        case nameof(PictureRender3DViewModel.AppliedThreshold):
                        case nameof(PictureRender3DViewModel.AppliedEpsilon):
                        case nameof(PictureRender3DViewModel.Path):
                            var result = PitcureToVectorXYPoints(settingsViewModel.Path, 1f);
                            if (result.Item1.Count > 1)
                            {
                                animationFrames = result.Item1;
                                frameDurations = result.Item2;
                                _frameTime = 0;
                                _currentFrameIndex = 0;
                                _lastFrameTimeStopwatch.Restart();
                                basePoints.Clear();
                            }
                            else if (result.Item1.Count == 1)
                            {
                                basePoints = result.Item1[0];
                                animationFrames.Clear();
                                frameDurations.Clear();
                                _frameTime = 0;
                                _currentFrameIndex = 0;
                            }
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
            // 表示用の現在フレームを取得
            List<XYPoint> currentFramePoints;

            if (animationFrames.Count > 0)
            {
                // GIFアニメーションの場合、フレームを時間ベースで切り替え
                double elapsedMs = _lastFrameTimeStopwatch.Elapsed.TotalMilliseconds;
                if (frameDurations.Count > _currentFrameIndex)
                {
                    int frameDurationMs = frameDurations[_currentFrameIndex] * 10; // 10ms単位から通常のms単位に変換
                    if (frameDurationMs <= 0) frameDurationMs = 100; // デフォルト100ms

                    if (elapsedMs >= frameDurationMs)
                    {
                        _lastFrameTimeStopwatch.Restart();
                        _currentFrameIndex = (_currentFrameIndex + 1) % animationFrames.Count;
                    }
                }

                currentFramePoints = animationFrames[_currentFrameIndex];
            }
            else
            {
                currentFramePoints = basePoints;
            }

            // 3Dパラメータ
            float camX = 0.0f;
            float camY = 0.0f;
            float camZ = 2.5f;
            float d = 8.0f;

            float thetaX = (float)(_sw.Elapsed.TotalSeconds * settingsViewModel.ThetaX);
            float thetaY = (float)(_sw.Elapsed.TotalSeconds * settingsViewModel.ThetaY);
            float thetaZ = (float)(_sw.Elapsed.TotalSeconds * settingsViewModel.ThetaZ);
            thetaX = (float)(thetaX * Math.PI / 180);
            thetaY = (float)(thetaY * Math.PI / 180);
            thetaZ = (float)(thetaZ * Math.PI / 180);

            // カメラ位置
            Quaternion rotation = Quaternion.CreateFromYawPitchRoll(thetaY, thetaX, thetaZ);
            Vector3 rotPos = new Vector3(camX, camY, camZ);
            Vector3 camPos = Vector3.Transform(rotPos, rotation);

            Vector3 camTarget = Vector3.Zero;
            Vector3 camUp = Vector3.UnitY;
            camUp = Vector3.Transform(camUp, rotation);

            var view = CreateLookAt(camPos, camTarget, camUp);

            var fmt = capture.WaveFormat;
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

            double scale = 0.25 + kick * 0.125;

            var projected = new List<XYPoint>();
            for (int i = 0; i < currentFramePoints.Count; i += 2)
            {
                if (i + 1 >= currentFramePoints.Count)
                    break;

                // 線分の2点
                var p1 = new Vector3((float)currentFramePoints[i].X, (float)currentFramePoints[i].Y, 0);
                var p2 = new Vector3((float)currentFramePoints[i + 1].X, (float)currentFramePoints[i + 1].Y, 0);

                // カメラ座標系に変換
                var v1 = Vector3.Transform(p1, view);
                var v2 = Vector3.Transform(p2, view);

                // パースペクティブ投影
                var s1 = ProjectToScreen(v1, d);
                var s2 = ProjectToScreen(v2, d);

                projected.Add(new XYPoint(s1.X * scale, s1.Y * scale, currentFramePoints[i].Intensity));
                projected.Add(new XYPoint(s2.X * scale, s2.Y * scale, currentFramePoints[i + 1].Intensity));
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

        // GIF対応: 戻り値が (List<List<XYPoint>>, List<int>)
        private (List<List<XYPoint>>, List<int>) PitcureToVectorXYPoints(string path, float scale = 1.0f)
        {
            var allFrames = new List<List<XYPoint>>();
            var allDurations = new List<int>();

            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return (allFrames, allDurations);

            try
            {
                using var src = new Bitmap(path);

                // GIFアニメーション判定
                bool isAnimated = false;
                int frameCount = 1;
                try
                {
                    var timeDimension = new FrameDimension(FrameDimension.Time.Guid);
                    frameCount = src.GetFrameCount(timeDimension);
                    isAnimated = frameCount > 1;
                }
                catch
                {
                    isAnimated = false;
                }

                if (isAnimated && frameCount > 1)
                {
                    // GIFアニメーション対応
                    var timeDimension = new FrameDimension(FrameDimension.Time.Guid);
                    
                    // フレーム遅延情報を取得
                    try
                    {
                        var delayProperty = src.PropertyIdList.FirstOrDefault(p => p == 0x5100); // PropertyTagFrameDelay
                        if (delayProperty > 0)
                        {
                            var delayItem = src.GetPropertyItem(delayProperty);
                            if (delayItem?.Value is byte[] delayData)
                            {
                                for (int i = 0; i < frameCount; i++)
                                {
                                    if (i * 4 + 3 < delayData.Length)
                                    {
                                        int duration = BitConverter.ToInt32(delayData, i * 4);
                                        allDurations.Add(Math.Max(1, duration)); // 単位は10ms
                                    }
                                    else
                                    {
                                        allDurations.Add(10); // デフォルト100ms
                                    }
                                }
                            }
                            else
                            {
                                for (int i = 0; i < frameCount; i++)
                                    allDurations.Add(10); // デフォルト100ms
                            }
                        }
                        else
                        {
                            for (int i = 0; i < frameCount; i++)
                                allDurations.Add(10); // デフォルト100ms
                        }
                    }
                    catch
                    {
                        for (int i = 0; i < frameCount; i++)
                            allDurations.Add(10); // デフォルト100ms
                    }

                    // 各フレームを処理
                    for (int fi = 0; fi < frameCount; fi++)
                    {
                        src.SelectActiveFrame(timeDimension, fi);
                        var framePoints = ProcessSingleFrame(src, scale);
                        allFrames.Add(framePoints);
                    }
                }
                else
                {
                    // 静止画の場合
                    var framePoints = ProcessSingleFrame(src, scale);
                    allFrames.Add(framePoints);
                    allDurations.Add(100); // デフォルト1000ms
                }
            }
            catch
            {
            }

            return (allFrames, allDurations);
        }

        private List<XYPoint> ProcessSingleFrame(Bitmap frameBitmap, float scale)
        {
            var points = new List<XYPoint>();

            const int targetSize = 256;
            float threshold = settingsViewModel.AppliedThreshold;

            using var canvas = new Bitmap(targetSize, targetSize);
            using (var g = Graphics.FromImage(canvas))
            {
                g.Clear(Color.Black);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.SmoothingMode = SmoothingMode.None;

                float ratio = MathF.Min((float)targetSize / frameBitmap.Width, (float)targetSize / frameBitmap.Height);
                int drawW = Math.Max(1, (int)MathF.Round(frameBitmap.Width * ratio));
                int drawH = Math.Max(1, (int)MathF.Round(frameBitmap.Height * ratio));
                int offsetX = (targetSize - drawW) / 2;
                int offsetY = (targetSize - drawH) / 2;
                g.DrawImage(frameBitmap, new Rectangle(offsetX, offsetY, drawW, drawH));
            }

            float[,] luma = new float[targetSize, targetSize];
            bool[,] on = new bool[targetSize, targetSize];
            for (int y = 0; y < targetSize; y++)
            {
                for (int x = 0; x < targetSize; x++)
                {
                    Color c = canvas.GetPixel(x, y);
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
            float rdpEpsilon = unit * settingsViewModel.AppliedEpsilon;

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
                var json = JsonSerializer.Serialize(settingsViewModel, new JsonSerializerOptions { WriteIndented = true });
                string path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Oscilloscope", "PictureRender3D.json");
                string dirPath = System.IO.Path.GetDirectoryName(path)!;
                Directory.CreateDirectory(dirPath);
                File.WriteAllText(path, json);
            }
            catch { }
        }

        public void LoadSettings()
        {
            try
            {
                string path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Oscilloscope", "PictureRender3D.json");
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    var loaded = JsonSerializer.Deserialize<PictureRender3DViewModel>(json);
                    if (loaded != null)
                    {
                        settingsViewModel.Path = loaded.Path;
                        settingsViewModel.Threshold = loaded.Threshold;
                        settingsViewModel.Epsilon = loaded.Epsilon;
                        settingsViewModel.AppliedThreshold = loaded.AppliedThreshold;
                        settingsViewModel.AppliedEpsilon = loaded.AppliedEpsilon;
                        settingsViewModel.ThetaX = loaded.ThetaX;
                        settingsViewModel.ThetaY = loaded.ThetaY;
                        settingsViewModel.ThetaZ = loaded.ThetaZ;
                    }
                }
            }
            catch { }
        }
    }
}
