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

//https://github.com/voich2014/mandel_zoom_abyss/tree/main
//Mandel Zoom Abyss
//Author: voich2014 ぼいち
//MIT License

namespace OscVisualizer.Services
{
    internal class MandelZoomAbyss : IAudioVisualizer
    {
        public string VisualizerName => "Mandel Zoom Abyss";

        private UserControl? _visualizerView;

        public UserControl? VisualizerView => _visualizerView;

        private readonly Stopwatch _sw = Stopwatch.StartNew();        // フレーム処理時間計測用（Restartされる）
        private readonly Stopwatch _clock = Stopwatch.StartNew();     // 経過時間計測用（リセットしない）
        private MandelZoomAbyssViewModel settingsViewModel = new MandelZoomAbyssViewModel();

        public MandelZoomAbyss()
        {
            PickRandomSpot();                           // 初期スポットをランダム選択
            _visualizerView = new MandelZoomAbyssView();
            settingsViewModel.PropertyChanged += (sender, e) =>
            {
                if (_visualizerView?.DataContext is MandelZoomAbyssViewModel vm)
                {
                    switch (e.PropertyName)
                    {
                        case nameof(MandelZoomAbyssViewModel.AppliedThreshold):
                        case nameof(MandelZoomAbyssViewModel.AppliedEpsilon):
                        case nameof(MandelZoomAbyssViewModel.PictureSize):
                            break;
                    }
                }
            };
            _visualizerView.DataContext = settingsViewModel;
        }

        /// <summary>巡礼スポット一覧からランダムに1つ選択してズームターゲットを更新する。</summary>
        private void PickRandomSpot()
        {
            var spot = PilgrimageSpots[_rng.Next(PilgrimageSpots.Length)];
            _centerX = spot.X;
            _centerY = spot.Y;
            _currentSpotName = spot.Name;
            _zoom = 1.0;
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

        // ────────────────────────────────────────────────
        // 巡礼スポット一覧（mandelzoomabyss.html より移植）
        // ────────────────────────────────────────────────
        private readonly record struct PilgrimageSpot(string Name, double X, double Y);

        private static readonly PilgrimageSpot[] PilgrimageSpots =
        [
            new("Pale Delta Rift",       -0.738247071430087,    0.14040264324797316),
            new("Cobalt Harbor",         -0.3108634041179903,   0.6255523077305407),
            new("Shadow Delta",          -0.7429057398289443,  -0.11725453405873851),
            new("Opal Highgate",         -0.09713561258045955,  0.8761646560952068),
            new("Black Spiral Gate",     -0.0812963142269291,  -0.6502443872741424),
            new("Ashen Delta",           -0.7398840844854713,  -0.13883713255450128),
            new("Far Night Gate",        -0.7329856632128358,  -0.17263094220217315),
            new("Ivory Spiral",          -0.061377428097184744, 0.6521205542981624),
            new("Pale Needle",           -0.09112262656679376,  0.6506012978148646),
            new("Deep Blue Lace",        -0.5671954074967652,  -0.4610982776479796),
            new("Aurora Chapel",         -0.5469561952073128,   0.49592362358700487),
            new("Blueglass Gate",        -0.43416899812640625,  0.5725895469915122),
            new("Obsidian Stair",        -0.15482315161498264, -0.6533489906578325),
            new("Copper Undertow",       -0.7399145605708473,  -0.12272065519168973),
            new("Scarlet Thread",        -0.7383655528053641,   0.14193097614869477),
            new("Glass Undertow",        -0.7367572101131081,  -0.15324310831259935),
            new("Ruby Needle",           -0.7457233327641152,  -0.11634505394101143),
            new("High Ivory Gate",       -0.5113548594852909,  -0.5244353007525207),
            new("Opal Spiral",           -0.7343182313805446,  -0.15980495634768158),
            new("Green Cathedral",        0.2762610520245507,   0.007559806168079376),
            new("Emerald Chapel",        -0.6259688938525505,  -0.3870711759198457),
            new("Night Delta",           -0.046148857204243526,-0.6895589064434171),
            new("Sable Spiral",          -0.5293304607458413,  -0.49716534439008686),
            new("Deep Indigo",           -0.7472451018951833,   0.09777122625801712),
            new("Charcoal Gate",         -0.7400582375451923,  -0.1274479884710163),
            new("Red Filament",          -0.746270680308342,   -0.12413550636870786),
            new("Dark Undertow",          0.28751575272157787,  0.014898665723856539),
            new("Faded Delta",           -0.06416209208546206,  0.6501383801107296),
            new("Midnight Gate",         -0.1762130484660156,   0.6490455039427616),
            new("Pale Foundry",          -0.6247403357224539,  -0.40200977855362),
            new("Obsidian Lace",         -0.7363797719106078,   0.15344717440009117),
            new("Ivory Forge",           -0.06262918740743771, -0.6522205204120838),
            new("Quiet Delta",           -0.5313837520545348,   0.4970711405947805),
            new("High Lantern",          -0.7376128630116582,  -0.15254746636468916),
            new("Silver Forge",          -0.5718596556503326,  -0.4582356090890244),
            new("Misted Chapel",         -0.744414258055389,    0.14753752469830214),
            new("Cobalt Lace",           -0.5834252178762108,   0.44708613205235453),
            new("Gold Hall",             -0.3109336968860589,  -0.6222128668986262),
            new("Dusky Lace",            -0.5450060634175315,  -0.48804401638917627),
            new("Fine Needle",           -0.7408763917130418,   0.1480325358044356),
            new("Pale Spire",             0.28133995342254636,  0.010734555523376911),
            new("Opal Court",            -0.7401062808930874,  -0.13007069535693153),
            new("Ash Delta",              0.2653629054101184,  -0.003186887823045253),
            new("Frost Spire",           -0.538930618299637,   -0.5426351656671613),
            new("Bright Needle",         -0.3216703899321146,  -0.6255237088818104),
            new("Sable Court",           -0.6362444970663637,  -0.3849488583067432),
            new("High Spiral",           -0.5885990180168301,  -0.4425216132076457),
            new("Glass Edge",            -0.729905533729121,   -0.1679778026146814),
            new("Pale Edge",             -0.7337112178294919,   0.16838020496070383),
            new("Black Undertow",        -0.41917694186326115,  0.5746224248455838),
            new("Still Delta",           -0.5615305225457996,   0.4809965075692162),
            new("Blue Thread",           -0.5395019612228498,   0.4944850535318256),
            new("Quiet Undertow",        -0.056731747841695324,-0.6744297730503604),
            new("White Needle",          -0.7395602505356074,   0.1492170745707117),
            new("North Spiral",          -0.7363899924759753,  -0.15637772043980658),
            new("Copper Gate",           -0.20334685058332977, -0.6509536057943479),
            new("Sable Chapel",          -0.04483691500965506,  0.6519908310263418),
            new("Dark Delta",            -0.5187628035712988,  -0.5099272321956233),
            new("Glass Needle",          -0.050329766924260194, 0.6470483627170325),
            new("Pale Spiral",           -0.055417792906519034,-0.6482785750064067),
            new("Dusk Lace",             -0.5858547779289074,  -0.45669256513006984),
            new("Ivory Delta",            0.28118628585431726, -0.011019947780296207),
            new("Red Wire",               0.2762948145903647,   0.00887723690783605),
            new("Ashen Harbor",          -0.7381754180192948,   0.14988399157719687),
            new("Shadow Wire",           -0.5207382865110413,  -0.5108320126216859),
            new("Gold Pin",              -0.7420568206906318,  -0.1344426790731959),
            new("Quartz Gate",           -0.6343497213814407,   0.3869564129738137),
            new("Blue Rift",             -0.7449550681947731,  -0.12447365097701549),
            new("Night Forge",           -0.7425447186306119,  -0.14819440594967453),
            new("Indigo Thread",         -0.7424873919077217,   0.14785028447303922),
            new("Blue Chapel",           -0.03106501239119097, -0.7208177804946899),
            new("Sable Delta",           -0.7428896194640547,   0.11505558709520847),
            new("Frost Thread",          -0.7415672334926203,  -0.15104766546655446),
            new("Cobalt Gate",           -0.7406495766341686,  -0.14817357789864763),
            new("Ashen Spiral",          -0.6340310875233263,  -0.3881269805738703),
            new("Amber Wire",            -0.732025057225488,   -0.16769773684535175),
            new("Quiet Lace",            -1.2634784933645278,  -0.03944906627293676),
            new("Dark Needle",           -0.7441359162256121,   0.10672255496121943),
            new("Black Delta",           -0.07269087171764113,  0.65120139636565),
            new("Glass Thread",          -0.6337911875639111,   0.37901517513673755),
            new("Violet Harbor",         -0.6523707921197638,  -0.35976334020495415),
            new("Frost Gate",            -0.7368376006688923,  -0.16562798022758216),
            new("Low Lantern",            0.2824236765727401,  -0.011127156399656087),
            new("Blue Edge",             -0.7358852189704775,   0.14517922306247055),
            new("Iron Court",            -0.05396320442203434, -0.6521071484894492),
            new("Silver Delta",          -0.02981080985162407,  0.6476360290125012),
            new("Pale Thread",           -0.7376068650828674,   0.1642504632277414),
            new("Sable Forge",           -0.5097790941270068,   0.5132494744192809),
            new("Opal Needle",           -0.5498482711520046,   0.4803869323572144),
            new("Indigo Gate",           -0.5502872421918437,  -0.4808320642821491),
            new("Ruby Gate",             -0.040926569430157544, 0.6479066563770175),
            new("Glass Court",           -0.06695961585501209, -0.6688936551660299),
            new("Night Spiral",          -0.7353498267233372,  -0.1516216948754154),
            new("Quiet Wire",            -0.7354150959029794,   0.15215214500296861),
            new("Shadow Lace",           -0.7379381968374364,   0.1458711192421615),
            new("Blue Ember",            -0.7429066072404384,  -0.1246715116663836),
            new("Ashen Thread",          -0.6425969849526882,  -0.37641639118548487),
            new("Frost Delta",           -0.5721541235595942,  -0.4694759757583961),
            new("Black Lace",            -0.7458432267387397,   0.10926162003539502),
            new("Pale Ember",            -0.7389068827852606,   0.13944170076027512),
        ];

        private static readonly Random _rng = new();

        // ────────────────────────────────────────────────
        // ズーム状態（現在の巡礼スポットへズーム）
        // ────────────────────────────────────────────────
        private double _zoom = 1.0;
        private const double ZoomTarget = 1e12;       // リセット閾値
        private const double BaseZoomSpeed = 1.012;   // 基礎ズーム速度

        private double _centerX;
        private double _centerY;
        private string _currentSpotName = "";

        private const int MaxIter = 300;
        private const int BandMod = 8;                // イソバンド分割数

        // 反復カウントグリッド（フレーム間再利用、最大サイズで確保）
        private int[] _iters = Array.Empty<int>();
        private int _lastSize = 0;

        // 負荷適応型 Epsilon 自動調整
        // ユーザー設定値を下限とし、処理時間超過時に自動増加・余裕時に徐々に戻す
        private float _autoEpsilon = 0f;              // ユーザー値への上乗せ分
        private const float EpsilonStep = 0.001f;     // 1ステップの増加量
        private const float EpsilonDecay = 0.0005f;   // 1ステップの回復量
        private const float EpsilonMax = 0.5f;       // 最大上乗せ量
        private const double TargetMs = 20.0;         // 目標処理時間 [ms]（これを超えたら Epsilon 増加）

        // 持続回転状態
        private double _rotationAngle = 0.0;          // 現在の累積回転角 [radian]
        private double _lastFrameTime = double.NaN;   // 前フレームのタイムスタンプ

        public List<XYPoint> ProcessAudio(WasapiCapture capture, WaveInEventArgs e)
        {
            var fmt = capture.WaveFormat;
            float[] wav = IAudioVisualizer.ConvertToWav1ch(capture, e);
            int sampleRate = fmt.SampleRate;

            // ハイパスフィルタ
            prevX = 0;
            prevY = 0;
            for (int i = 0; i < wav.Length; i++)
                wav[i] = HighPass(wav[i]);

            // FFT
            Complex32[] fft = new Complex32[wav.Length];
            for (int i = 0; i < wav.Length; i++)
                fft[i] = new Complex32(wav[i], 0);
            Fourier.Forward(fft, FourierOptions.Matlab);

            float[] spectrum = new float[fft.Length / 2];
            for (int i = 0; i < spectrum.Length; i++)
                spectrum[i] = fft[i].Magnitude;

            float kick = IAudioVisualizer.GetBand(spectrum, 50, 100, sampleRate);
            float snare = IAudioVisualizer.GetBand(spectrum, 1500, 3000, sampleRate);
            float hat = IAudioVisualizer.GetBand(spectrum, 6000, 12000, sampleRate);

            // 処理時間計測開始（負荷適応 Epsilon 用）
            _sw.Restart();

            // ViewModel パラメータ取得
            int size = Math.Max(64, settingsViewModel.PictureSize);
            float appliedThreshold = settingsViewModel.AppliedThreshold;   // 0.0～1.0：セグメント輝度閾値
            // _autoEpsilon をユーザー設定値に上乗せしてピクセルスケールに換算
            float rdpEps = (settingsViewModel.AppliedEpsilon + _autoEpsilon) * (1f / size);

            // グリッドバッファをサイズ変更時に再確保
            int totalPixels = size * size;
            if (_lastSize != size)
            {
                _iters = new int[totalPixels];
                _lastSize = size;
            }

            // ズーム更新（kickで速度変調）
            double zoomSpeed = BaseZoomSpeed + kick * 0.01;
            _zoom *= zoomSpeed;
            if (_zoom >= ZoomTarget)
                PickRandomSpot();   // リセット時に巡礼スポットをランダム選択

            // マンデルブロ集合を size×size で計算
            double pixelScale = 3.5 / (_zoom * size);
            int[] iters = _iters;

            Parallel.For(0, size, py =>
            {
                double ci = _centerY + (py - size / 2.0) * pixelScale;
                int rowOff = py * size;
                for (int px = 0; px < size; px++)
                {
                    double cr = _centerX + (px - size / 2.0) * pixelScale;
                    double zr = 0.0, zi = 0.0;
                    int n = 0;
                    while (n < MaxIter)
                    {
                        double zr2 = zr * zr;
                        double zi2 = zi * zi;
                        if (zr2 + zi2 >= 4.0) break;
                        zi = 2.0 * zr * zi + ci;
                        zr = zr2 - zi2 + cr;
                        n++;
                    }
                    iters[rowOff + px] = n;
                }
            });

            // イソライン輪郭セグメント抽出（appliedThreshold: 0.0=全輪郭, 1.0=集合境界のみ）
            float invSize = 1f / size;
            // 集合境界線（n==MaxIter境界）の輝度を1.0、通常輪郭を appliedThresholdで制御
            // appliedThreshold以上の輝度を持つセグメントのみ出力
            var segments = new List<(Vector2 A, Vector2 B, float I)>(size * size / 4);

            for (int py = 0; py < size; py++)
            {
                int rowOff = py * size;
                float y = (py + 0.5f) * invSize * 2f - 1f;
                for (int px = 0; px < size - 1; px++)
                {
                    int na = iters[rowOff + px];
                    int nb = iters[rowOff + px + 1];
                    if (na % BandMod != nb % BandMod)
                    {
                        float intensity = (na == MaxIter || nb == MaxIter) ? 1.0f : 0.4f;
                        if (intensity >= appliedThreshold)
                        {
                            float xMid = (px + 1f) * invSize * 2f - 1f;
                            segments.Add((new Vector2(xMid, y - invSize), new Vector2(xMid, y + invSize), intensity));
                        }
                    }
                }
            }

            for (int py = 0; py < size - 1; py++)
            {
                float yMid = (py + 1f) * invSize * 2f - 1f;
                for (int px = 0; px < size; px++)
                {
                    int na = iters[py * size + px];
                    int nb = iters[(py + 1) * size + px];
                    if (na % BandMod != nb % BandMod)
                    {
                        float intensity = (na == MaxIter || nb == MaxIter) ? 1.0f : 0.4f;
                        if (intensity >= appliedThreshold)
                        {
                            float x = (px + 0.5f) * invSize * 2f - 1f;
                            segments.Add((new Vector2(x - invSize, yMid), new Vector2(x + invSize, yMid), intensity));
                        }
                    }
                }
            }

            // ポリライン結合 → RDP 簡略化 → XYPoint 出力
            var polylines = BuildPolylines(segments, invSize * 1.5f);

            var result = new List<XYPoint>(polylines.Count * 4);

            foreach (var poly in polylines)
            {
                var pts = SimplifyRdp(poly.Points, rdpEps);
                if (pts.Count < 2) continue;

                double brightness = 0.05 + poly.Intensity * 0.20;

                for (int i = 0; i < pts.Count - 1; i++)
                {
                    result.Add(new XYPoint(pts[i].X, pts[i].Y, brightness));
                    result.Add(new XYPoint(pts[i + 1].X, pts[i + 1].Y, brightness));
                }
            }

            // 描画ライン0件 → 別の聖地からズームをやり直す
            if (result.Count == 0)
                PickRandomSpot();

            // 負荷適応型 Epsilon 自動調整
            double elapsedMs = _sw.Elapsed.TotalMilliseconds;
            if (elapsedMs > TargetMs)
            {
                // 処理時間オーバー → Epsilon を増加（線分数を減らして軽量化）
                _autoEpsilon = MathF.Min(_autoEpsilon + EpsilonStep, EpsilonMax);
            }
            else
            {
                // 余裕あり → ユーザー設定値（下限 0）へ徐々に回復
                _autoEpsilon = MathF.Max(_autoEpsilon - EpsilonDecay, 0f);
            }

            // 持続回転: Rotate [deg/s] をフレーム毎に累積
            double nowSec = _clock.Elapsed.TotalSeconds;
            if (!double.IsNaN(_lastFrameTime))
            {
                double dt = Math.Clamp(nowSec - _lastFrameTime, 0.0, 0.2); // max 200ms クランプ
                _rotationAngle += settingsViewModel.Rotate * (Math.PI / 180.0) * dt * snare;
            }
            _lastFrameTime = nowSec;

            // 回転変換（Rotate==0 のときはスキップ）
            if (_rotationAngle != 0.0)
            {
                float cos = (float)Math.Cos(_rotationAngle);
                float sin = (float)Math.Sin(_rotationAngle);
                for (int i = 0; i < result.Count; i++)
                {
                    var p = result[i];
                    float rx = (float)p.X * cos - (float)p.Y * sin;
                    float ry = (float)p.X * sin + (float)p.Y * cos;
                    result[i] = new XYPoint(rx, ry, p.Intensity, p.Z);
                }
            }

            return result;
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
                string path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Oscilloscope", "MandelZoomAbyss.json");
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
                string path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Oscilloscope", "MandelZoomAbyss.json");
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    var loaded = JsonSerializer.Deserialize<MandelZoomAbyssViewModel>(json);
                    if (loaded != null)
                    {
                        settingsViewModel.Threshold = loaded.Threshold;
                        settingsViewModel.Epsilon = loaded.Epsilon;
                        settingsViewModel.AppliedThreshold = loaded.AppliedThreshold;
                        settingsViewModel.AppliedEpsilon = loaded.AppliedEpsilon;
                        settingsViewModel.Rotate = loaded.Rotate;
                        settingsViewModel.PictureSize = loaded.PictureSize;
                    }
                }
            }
            catch { }
        }
    }
}
