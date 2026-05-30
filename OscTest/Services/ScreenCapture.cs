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
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace OscVisualizer.Services
{
    internal class ScreenCapture : IAudioVisualizer
    {
        public string VisualizerName => "Screen Capture";

        private UserControl? _visualizerView;

        public UserControl? VisualizerView => _visualizerView;

        private readonly Stopwatch _sw = Stopwatch.StartNew();
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private ScreenCaptureViewModel settingsViewModel = new ScreenCaptureViewModel();

        // 負荷適応型 Epsilon 自動調整
        private float _autoEpsilon = 0f;
        private const float EpsilonStep = 0.005f;    // 1ステップの増加量（MandelZoomAbyssの5倍）
        private const float EpsilonDecay = 0.001f;   // 1ステップの回復量
        private const float EpsilonMax = 2.0f;       // 最大上乗せ量（座標空間単位、size=256なら素25ピクセル）
        private const double TargetMs = 20.0;         // 目標処理時間 [ms]

        // 輝度グリッドバッファ（フレーム間再利用）
        private float[] _brightness = Array.Empty<float>();
        private int _lastSize = 0;

        // Win32 モニター列挙
        [DllImport("user32.dll")]
        private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);
        private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

        [DllImport("user32.dll")]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        /// <summary>ScreenNo 番目のモニターの仮想スクリーン座標を返す。存在しない場合はプライマリ画面を返す。</summary>
        private static Rectangle GetMonitorBounds(int screenNo)
        {
            var monitors = new List<Rectangle>();

            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (hMon, _, ref rc, _) =>
            {
                monitors.Add(new Rectangle(rc.Left, rc.Top, rc.Right - rc.Left, rc.Bottom - rc.Top));
                return true;
            }, IntPtr.Zero);

            if (monitors.Count == 0)
                return new Rectangle(0, 0, 1920, 1080);

            int idx = Math.Clamp(screenNo, 0, monitors.Count - 1);
            return monitors[idx];
        }

        public ScreenCapture()
        {
            _visualizerView = new ScreenCaptureView();
            settingsViewModel.PropertyChanged += (sender, e) =>
            {
                if (_visualizerView?.DataContext is ScreenCaptureViewModel vm)
                {
                    switch (e.PropertyName)
                    {
                        case nameof(ScreenCaptureViewModel.AppliedThreshold):
                        case nameof(ScreenCaptureViewModel.AppliedEpsilon):
                        case nameof(ScreenCaptureViewModel.PictureSize):
                            break;
                    }
                }
            };
            _visualizerView.DataContext = settingsViewModel;
        }

        public List<XYPoint> ProcessAudio(WasapiCapture capture, WaveInEventArgs e)
        {
            _sw.Restart();

            int size = Math.Max(64, settingsViewModel.PictureSize);
            // AppliedThreshold（0.0〜1.0）を隣接ピクセル輝度差の閾値にスケーリング。
            // 実映像のエッジ輝度差は 0.02〜0.30 程度なので 0〜0.30 の範囲にマップする。
            // スライダー 0.0 → 閾値 0.0（全エッジ）、1.0 → 閾値 0.30（強いエッジのみ）
            float appliedThreshold = settingsViewModel.AppliedThreshold * 0.3f;
            // rdpEps: AppliedEpsilon は「許容するピクセル数」の単位。
            // 座標空間 [-1,+1]（幅2.0）では 1ピクセル = 2.0/size。
            // MandelZoomAbyssと同じ計算式だが、意味を明確化。
            float rdpEps = (settingsViewModel.AppliedEpsilon + _autoEpsilon) * (2f / size);

            // グリッドバッファをサイズ変更時に再確保
            if (_lastSize != size)
            {
                _brightness = new float[size * size];
                _lastSize = size;
            }

            // ── 画面キャプチャ ──────────────────────────────
            var monitorBounds = GetMonitorBounds(settingsViewModel.ScreenNo);

            try
            {
                // ① モニター実解像度でフルキャプチャ
                using var fullBmp = new Bitmap(monitorBounds.Width, monitorBounds.Height, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(fullBmp))
                {
                    g.CopyFromScreen(monitorBounds.Location, System.Drawing.Point.Empty, monitorBounds.Size);
                }

                // ② size×size へリサイズ縮小
                using var resized = new Bitmap(size, size, PixelFormat.Format32bppArgb);
                using (var g2 = Graphics.FromImage(resized))
                {
                    g2.InterpolationMode = InterpolationMode.Bilinear;
                    g2.DrawImage(fullBmp, 0, 0, size, size);
                }

                // ロックしてピクセルデータを輝度グリッドへ変換
                var bmpData = resized.LockBits(new Rectangle(0, 0, size, size), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                try
                {
                    unsafe
                    {
                        byte* ptr = (byte*)bmpData.Scan0;
                        int stride = bmpData.Stride;
                        float[] brightness = _brightness;

                        Parallel.For(0, size, py =>
                        {
                            byte* row = ptr + py * stride;
                            int rowOff = py * size;
                            for (int px = 0; px < size; px++)
                            {
                                int off = px * 4;
                                float b2 = row[off];        // B
                                float g3 = row[off + 1];    // G
                                float r = row[off + 2];     // R
                                // ITU-R BT.601 輝度
                                brightness[rowOff + px] = (0.299f * r + 0.587f * g3 + 0.114f * b2) / 255f;
                            }
                        });
                    }
                }
                finally
                {
                    resized.UnlockBits(bmpData);
                }
            }
            catch
            {
                // キャプチャ失敗時は前フレームの輝度グリッドをそのまま使用
            }

            // ── 輝度グリッドからエッジセグメント抽出 ──────────────
            // ピクセル格子の交点座標を基準とし、隣接ピクセル間に輝度差があれば
            // その境界（格子辺）を線分として追加する。
            // 格子点 (px, py) の座標 = ((px)*2/size - 1, 1 - (py)*2/size)
            // 水平エッジ（左右ピクセル差）→ 列境界の垂直辺: (px+1, py)〜(px+1, py+1)
            // 垂直エッジ（上下ピクセル差）→ 行境界の水平辺: (px, py+1)〜(px+1, py+1)
            float invSize = 1f / size;
            float scale = 2f * invSize;   // 格子1マス分の座標幅
            var segments = new List<(Vector2 A, Vector2 B, float I)>(size * size / 4);
            float[] lum = _brightness;

            // 水平エッジ（左右ピクセル間）→ 垂直な境界辺
            for (int py = 0; py < size; py++)
            {
                int rowOff = py * size;
                // 格子点のY座標（上=+1）
                float y0 = 1f - py * scale;
                float y1 = 1f - (py + 1) * scale;
                for (int px = 0; px < size - 1; px++)
                {
                    float diff = MathF.Abs(lum[rowOff + px] - lum[rowOff + px + 1]);
                    if (diff >= appliedThreshold)
                    {
                        float x = (px + 1) * scale - 1f;   // 格子点X
                        float intensity = Math.Clamp(diff, 0f, 1f);
                        segments.Add((new Vector2(x, y0), new Vector2(x, y1), intensity));
                    }
                }
            }

            // 垂直エッジ（上下ピクセル間）→ 水平な境界辺
            for (int py = 0; py < size - 1; py++)
            {
                float y = 1f - (py + 1) * scale;   // 格子点Y
                for (int px = 0; px < size; px++)
                {
                    float diff = MathF.Abs(lum[py * size + px] - lum[(py + 1) * size + px]);
                    if (diff >= appliedThreshold)
                    {
                        float x0 = px * scale - 1f;         // 格子点X左
                        float x1 = (px + 1) * scale - 1f;   // 格子点X右
                        float intensity = Math.Clamp(diff, 0f, 1f);
                        segments.Add((new Vector2(x0, y), new Vector2(x1, y), intensity));
                    }
                }
            }

            // ── ポリライン結合 → RDP 簡略化 → XYPoint 出力 ──────
            // 格子点は理論上完全一致するが、float 誤差のため scale の 1% を許容
            var polylines = BuildPolylines(segments, scale * 0.01f);
            var result = new List<XYPoint>(polylines.Count * 4);

            foreach (var poly in polylines)
            {
                var pts = SimplifyRdp(poly.Points, rdpEps);
                if (pts.Count < 2) continue;

                double brightness2 = 0.05 + poly.Intensity * 0.20;

                for (int i = 0; i < pts.Count - 1; i++)
                {
                    result.Add(new XYPoint(pts[i].X, pts[i].Y, brightness2));
                    result.Add(new XYPoint(pts[i + 1].X, pts[i + 1].Y, brightness2));
                }
            }

            // 負荷適応型 Epsilon 自動調整
            double elapsedMs = _sw.Elapsed.TotalMilliseconds;
            _autoEpsilon = elapsedMs > TargetMs
                ? MathF.Min(_autoEpsilon + EpsilonStep, EpsilonMax)
                : MathF.Max(_autoEpsilon - EpsilonDecay, 0f);

            return result;
        }

        // ── BuildPolylines / SimplifyRdp（MandelZoomAbyssと同一ロジック）──

        private sealed class PolylineWithIntensity
        {
            public List<Vector2> Points { get; set; } = new();
            public float Intensity { get; set; }
        }

        private static List<PolylineWithIntensity> BuildPolylines(List<(Vector2 A, Vector2 B, float I)> segments, float endpointTolerance)
        {
            var result = new List<PolylineWithIntensity>();
            if (segments.Count == 0) return result;

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
                if (!endpointMap.TryGetValue(key, out var candidates)) return false;

                for (int ci = 0; ci < candidates.Count; ci++)
                {
                    int si = candidates[ci];
                    if (used[si]) continue;

                    var s = segments[si];
                    Vector2 next;
                    if ((s.A - end).LengthSquared() <= endpointTolerance * endpointTolerance)
                        next = s.B;
                    else if ((s.B - end).LengthSquared() <= endpointTolerance * endpointTolerance)
                        next = s.A;
                    else
                        continue;

                    used[si] = true;
                    if (atFront) poly.Insert(0, next);
                    else poly.Add(next);

                    intensitySum += s.I;
                    intensityCount++;
                    return true;
                }
                return false;
            }

            for (int i = 0; i < segments.Count; i++)
            {
                if (used[i]) continue;

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
            if (pts.Count <= 2) return new List<Vector2>(pts);

            float eps2 = epsilon * epsilon;
            var keep = new bool[pts.Count];
            keep[0] = true;
            keep[pts.Count - 1] = true;

            var stack = new Stack<(int S, int E)>();
            stack.Push((0, pts.Count - 1));

            while (stack.Count > 0)
            {
                var (s, e2) = stack.Pop();
                if (e2 <= s + 1) continue;

                Vector2 a = pts[s];
                Vector2 b = pts[e2];
                Vector2 ab = b - a;
                float ab2 = ab.LengthSquared();
                float maxD2 = -1f;
                int maxIdx = -1;

                for (int i = s + 1; i < e2; i++)
                {
                    Vector2 p = pts[i];
                    float d2;
                    if (ab2 < 1e-12f)
                    {
                        d2 = (p - a).LengthSquared();
                    }
                    else
                    {
                        float t = Math.Clamp(Vector2.Dot(p - a, ab) / ab2, 0f, 1f);
                        d2 = (p - (a + ab * t)).LengthSquared();
                    }
                    if (d2 > maxD2) { maxD2 = d2; maxIdx = i; }
                }

                if (maxIdx >= 0 && maxD2 > eps2)
                {
                    keep[maxIdx] = true;
                    stack.Push((s, maxIdx));
                    stack.Push((maxIdx, e2));
                }
            }

            var simplified = new List<Vector2>(pts.Count);
            for (int i = 0; i < pts.Count; i++)
                if (keep[i]) simplified.Add(pts[i]);
            return simplified;
        }

        public void SaveSettings()
        {
            try
            {
                var json = JsonSerializer.Serialize(settingsViewModel, new JsonSerializerOptions { WriteIndented = true });
                string path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Oscilloscope", "ScreenCapture.json");
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
                string path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Oscilloscope", "ScreenCapture.json");
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    var loaded = JsonSerializer.Deserialize<ScreenCaptureViewModel>(json);
                    if (loaded != null)
                    {
                        settingsViewModel.ScreenNo = loaded.ScreenNo;
                        settingsViewModel.Threshold = loaded.Threshold;
                        settingsViewModel.Epsilon = loaded.Epsilon;
                        settingsViewModel.AppliedThreshold = loaded.AppliedThreshold;
                        settingsViewModel.AppliedEpsilon = loaded.AppliedEpsilon;
                        settingsViewModel.PictureSize = loaded.PictureSize;
                    }
                }
            }
            catch { }
        }
    }
}
