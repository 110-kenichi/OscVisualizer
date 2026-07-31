using NAudio.CoreAudioApi;
using NAudio.Wave;
using OscVisualizer.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace OscVisualizer.Services
{
    internal class Kamon : IAudioVisualizer
    {
        private const double ChangeIntervalSeconds = 8.0;
        private const double TransitionDurationSeconds = 1.5;
        private const double NormalIntensity = 0.8;
        private const double DimIntensity = 0.25;
        private const double CoinThickness = 0.18;
        private const double CoinRimMargin = 0.04;
        private const double MaximumCoinRadius = 0.90;

        private static readonly string[] KamonNames =
        [
            "徳川葵", "丸に立ち葵", "丸に井桁", "抱き稲", "平井筒", "丸に梅鉢", "丸に三つ鱗", "梅の花", "丸に剣梅鉢", "丸に立ち沢瀉",
            "丸に抱き沢瀉", "丸に五本骨扇", "丸に三つ扇", "丸に日の丸扇", "丸に檜扇", "剣唐花", "丸に雁金", "丸に結び雁金", "丸に違い柏", "丸に三つ柏",
            "丸に蔓柏", "丸に抱き柏", "丸に片喰", "丸に剣片喰", "剣片喰", "丸に梶の葉", "五三桐", "五七桐", "丸に五三桐", "丸に桔梗",
            "桔梗", "剣桔梗", "丸に三盛り亀甲花菱", "丸に釘抜き", "違い釘抜き", "源氏車", "轡", "丸に九枚笹", "丸に笹りんどう", "丸に三枚笹",
            "丸に若根笹", "丸に山桜", "丸に桜", "丸に一の字", "丸に州浜", "丸に三本杉", "丸に違い鷹の羽", "丸に並び鷹の羽", "丸に橘", "丸に久世橘",
            "丸に三つ茶の実", "丸に一つ茶の実", "丸に千切り", "丸に違い丁字", "左一つ丁字巴", "浮線蝶", "丸に揚羽蝶", "揚羽蝶", "丸に鬼蔦", "蔦",
            "鬼蔦", "丸に中陰蔦", "糸輪に蔦", "丸に抱き角", "丸に鶴の丸", "丸に左二つ巴", "丸に左三つ巴", "丸に右二つ巴", "丸に右三つ巴", "左三つ巴",
            "右三つ巴", "丸に花菱", "丸に四方花菱", "丸に四方剣花菱", "丸に剣花菱", "丸に抱き柊", "丸に三階菱", "丸に武田菱", "丸に二つ引き", "丸に一つ引き",
            "丸の内に二つ引き", "丸に上り藤", "丸に下がり藤", "上り藤", "下がり藤", "丸に瓶子", "丸に並び瓶子", "丸に三つ星", "丸に九曜星", "丸に渡辺星",
            "丸に七曜星", "丸に右三階松", "丸に左三階松", "丸に三つ松", "丸に抱き茗荷", "丸に隅立て四つ目", "丸に木瓜", "丸に剣木瓜", "丸に違い矢", "丸に並び矢"
        ];

        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private readonly Random _random = new();
        private readonly int[] _order = new int[KamonNames.Length];
        private int _orderIndex;
        private int _currentIndex;
        private double _lastChange;
        private double _lastBeatTime = -1;
        private double _beatInterval = 0.6;
        private double _audioLevel;
        private double _bassLevel;
        private double _beatLevel;
        private double _rotationX;
        private double _rotationY;
        private double _rotationZ;
        private float _previousSample;
        private bool _isTransitioning;
        private double _transitionStart;
        private int _transitionTargetIndex;
        private List<XYPoint>? _transitionFrom;
        private List<XYPoint>? _transitionTo;

        public Kamon()
        {
            for (int i = 0; i < _order.Length; i++)
                _order[i] = i;

            ShuffleOrder();
            _currentIndex = _order[0];
        }

        public string VisualizerName => "Kamon";

        public List<XYPoint> ProcessAudio(WasapiCapture capture, WaveInEventArgs ea)
        {
            UpdateAudioAnimation(IAudioVisualizer.ConvertToWav1ch(capture, ea));

            double now = _stopwatch.Elapsed.TotalSeconds;
            if (!_isTransitioning && (now - _lastChange >= ChangeIntervalSeconds || ConsumeBeatForKamonChange()))
            {
                _lastChange = now;
                _orderIndex++;
                if (_orderIndex >= _order.Length)
                {
                    ShuffleOrder();
                    _orderIndex = 0;
                }

                BeginKamonTransition(_order[_orderIndex], now);
            }

            return GenerateCurrentKamon(now);
        }

        public List<XYPoint> GenerateKamon(float[] waveform, float time)
        {
            return GenerateCurrentKamon(_stopwatch.Elapsed.TotalSeconds);
        }

        private List<XYPoint> GenerateKamonShape(int index)
        {
            var points = new List<XYPoint>(768);
            if (KamonTraces.TryAdd(index, points))
            {
                return points;
            }

            int family = index + 1;

            if (KamonNames[index].Contains("丸") || KamonNames[index].Contains("糸輪") || KamonNames[index].Contains("丸の内"))
            {
                AddCircle(points, 0, 0, 0.88, 48);
                if (KamonNames[index].Contains("中陰"))
                    AddCircle(points, 0, 0, 0.75, 40);
            }

            switch (family)
            {
                case 1: AddAoi(points); break;
                case 2: AddAoi(points); AddCircle(points, 0, 0, 0.58, 32); break;
                case 3: AddDiamond(points, 0, 0, 0.58); break;
                case 4: AddRice(points); break;
                case 5: AddSquare(points, 0, 0, 0.62); break;
                case 6: AddPlum(points, false); break;
                case 7: AddScale(points, 3); break;
                case 8: AddFlower(points, 0, 0, 8, 0.52); break;
                case 9: AddPlum(points, true); break;
                case 10: AddLeaf(points, 0, 0.35, -0.45); break;
                case 11: AddLeaf(points, 0, 0.35, 0.45); break;
                case 12: AddFan(points, 5); break;
                case 13: AddFan(points, 3); break;
                case 14: AddFan(points, 1); break;
                case 15: AddFan(points, 7); break;
                case 16: AddStar(points, 0, 0, 8, 0.62, 0.28); break;
                case 17: AddBird(points, -0.25); AddBird(points, 0.25); break;
                case 18: AddBird(points, 0); AddCircle(points, 0, 0, 0.32, 24); break;
                case 19: AddLeafPair(points, 3); break;
                case 20: AddLeafPair(points, 3); AddCircle(points, 0, 0, 0.28, 20); break;
                case 21: AddLeafPair(points, 5); break;
                case 22: AddLeafPair(points, 4); AddCircle(points, 0, 0, 0.42, 24); break;
                case 23: AddKatabami(points, false); break;
                case 24: AddKatabami(points, true); break;
                case 25: AddKatabami(points, true); break;
                case 26: AddLeafPair(points, 4); break;
                case 27: Add桐(points, 3, 0.5); break;
                case 28: Add桐(points, 5, 0.55); break;
                case 29: Add桐(points, 3, 0.5); AddCircle(points, 0, 0, 0.62, 32); break;
                case 30: AddKikyo(points, false); break;
                case 31: AddKikyo(points, false); break;
                case 32: AddKikyo(points, true); break;
                case 33: AddFlower(points, 0, 0, 6, 0.60); AddDiamond(points, 0, 0, 0.72); break;
                case 34: AddSquare(points, 0, 0, 0.58); AddCircle(points, 0, 0, 0.25, 20); break;
                case 35: AddSquare(points, 0, 0, 0.58); AddSquare(points, 0, 0, 0.30); break;
                case 36: AddWheel(points, 8); break;
                case 37: AddWheel(points, 6); break;
                case 38: AddBamboo(points, 5); break;
                case 39: AddBamboo(points, 3); AddFlower(points, 0, 0, 5, 0.3); break;
                case 40: AddBamboo(points, 3); break;
                case 41: AddBamboo(points, 4); break;
                case 42: AddFlower(points, 0, 0, 6, 0.58); break;
                case 43: AddFlower(points, 0, 0, 5, 0.58); break;
                case 44: AddLine(points, -0.55, 0, 0.55, 0); break;
                case 45: AddWaveRing(points, 0.60, 3, 0.11); break;
                case 46: AddTree(points, 3); break;
                case 47: AddFeatherPair(points, -0.35); AddFeatherPair(points, 0.35); break;
                case 48: AddFeatherPair(points, -0.22); AddFeatherPair(points, 0.22); break;
                case 49: Add橘(points); break;
                case 50: Add橘(points); AddCircle(points, 0, 0, 0.6, 32); break;
                case 51: AddFruit(points, 3); break;
                case 52: AddFruit(points, 1); break;
                case 53: AddCross(points); break;
                case 54: Add丁字(points, 2); break;
                case 55: AddTomoe(points, 1); break;
                case 56: AddButterfly(points, false); break;
                case 57: AddButterfly(points, true); break;
                case 58: AddButterfly(points, false); AddCircle(points, 0, 0, 0.35, 24); break;
                case 59: AddIvy(points, 4); break;
                case 60: AddIvy(points, 5); break;
                case 61: AddIvy(points, 6); break;
                case 62: AddIvy(points, 4); AddCircle(points, 0, 0, 0.62, 32); break;
                case 63: AddIvy(points, 3); break;
                case 64: AddAngle(points); break;
                case 65: AddCrane(points); break;
                case 66: AddTomoe(points, 2); break;
                case 67: AddTomoe(points, 3); break;
                case 68: AddTomoe(points, 2); break;
                case 69: AddTomoe(points, 3); break;
                case 70: AddTomoe(points, 3); break;
                case 71: AddTomoe(points, 3); break;
                case 72: AddHanabishi(points, false); break;
                case 73: AddHanabishi(points, true); break;
                case 74: AddHanabishi(points, true); break;
                case 75: AddHanabishi(points, true); break;
                case 76: AddHolly(points); break;
                case 77: AddDiamondStack(points, 3); break;
                case 78: AddDiamondStack(points, 2); break;
                case 79: AddHorizontalBars(points, 2); break;
                case 80: AddHorizontalBars(points, 1); break;
                case 81: AddHorizontalBars(points, 2); AddCircle(points, 0, 0, 0.62, 32); break;
                case 82: AddWisteriaCluster(points, -0.20, 0.20, 1, 4); AddWisteriaCluster(points, 0.20, 0.20, 1, 4); break;
                case 83: AddWisteriaCluster(points, -0.20, -0.20, -1, 4); AddWisteriaCluster(points, 0.20, -0.20, -1, 4); break;
                case 84: AddWisteriaCluster(points, -0.20, 0.20, 1, 5); AddWisteriaCluster(points, 0.20, 0.20, 1, 5); break;
                case 85: AddWisteriaCluster(points, -0.20, -0.20, -1, 5); AddWisteriaCluster(points, 0.20, -0.20, -1, 5); break;
                case 86: AddBottle(points, 1); break;
                case 87: AddBottle(points, 2); break;
                case 88: AddStars(points, 3); break;
                case 89: AddStars(points, 9); break;
                case 90: AddStars(points, 7); break;
                case 91: AddStars(points, 7); break;
                case 92: AddPine(points, 3); break;
                case 93: AddPine(points, 3); break;
                case 94: AddPine(points, 3); AddCircle(points, 0, 0, 0.35, 24); break;
                case 95: AddGinger(points); break;
                case 96: AddEyes(points); break;
                case 97: AddMokko(points, false); break;
                case 98: AddMokko(points, true); break;
                case 99: AddArrows(points, 2); break;
                case 100: AddArrows(points, 2); AddCircle(points, 0, 0, 0.25, 20); break;
            }

            return points;
        }

        private List<XYPoint> GenerateCurrentKamon(double now)
        {
            List<XYPoint> points;
            if (_isTransitioning && _transitionFrom is not null && _transitionTo is not null)
            {
                double progress = Math.Clamp((now - _transitionStart) / TransitionDurationSeconds, 0, 1);
                points = InterpolateKamon(_transitionFrom, _transitionTo, progress);
                if (progress >= 1)
                {
                    _currentIndex = _transitionTargetIndex;
                    _isTransitioning = false;
                    _transitionFrom = null;
                    _transitionTo = null;
                }
            }
            else
            {
                points = GenerateKamonShape(_currentIndex);
            }

            return CreateCoinProjection(points);
        }

        private List<XYPoint> CreateCoinProjection(IReadOnlyList<XYPoint> source)
        {
            var result = new List<XYPoint>(source.Count + 160);
            double halfThickness = CoinThickness * (0.85 + Math.Clamp(_beatLevel * 0.3, 0, 0.3)) * 0.5;
            double radius = 0;
            for (int i = 0; i < source.Count; i++)
                radius = Math.Max(radius, Math.Sqrt(source[i].X * source[i].X + source[i].Y * source[i].Y));
            radius = Math.Min(radius + CoinRimMargin, MaximumCoinRadius / GetCoinScale());

            _ = ProjectPoint(new XYPoint(0, 0), halfThickness, out double frontCenterDepth);
            _ = ProjectPoint(new XYPoint(0, 0), -halfThickness, out double backCenterDepth);
            bool frontIsVisible = frontCenterDepth <= backCenterDepth;
            double visibleDepth = frontIsVisible ? halfThickness : -halfThickness;

            AddCoinRim(result, radius, halfThickness, frontIsVisible);

            for (int i = 0; i + 1 < source.Count; i += 2)
            {
                XYPoint start = ProjectPoint(source[i], visibleDepth, out _);
                XYPoint end = ProjectPoint(source[i + 1], visibleDepth, out _);
                AddProjectedLine(result, start, end);
            }

            return result;
        }

        private void AddCoinRim(List<XYPoint> points, double radius, double halfThickness, bool frontIsVisible)
        {
            if (radius <= 0)
                return;

            const int segmentCount = 64;
            var front = new XYPoint[segmentCount];
            var back = new XYPoint[segmentCount];
            var sideVisible = new bool[segmentCount];

            for (int i = 0; i < segmentCount; i++)
            {
                double angle = i * Math.PI * 2 / segmentCount;
                var rimPoint = new XYPoint(radius * Math.Cos(angle), radius * Math.Sin(angle));
                front[i] = ProjectPoint(rimPoint, halfThickness, out _);
                back[i] = ProjectPoint(rimPoint, -halfThickness, out _);
                sideVisible[i] = IsCoinSideVisible(angle + Math.PI / segmentCount);
            }

            for (int i = 0; i < segmentCount; i++)
            {
                int next = (i + 1) % segmentCount;
                if (frontIsVisible)
                    AddProjectedLine(points, front[i], front[next]);
                else
                    AddProjectedLine(points, back[i], back[next]);
            }

            for (int i = 0; i < segmentCount; i++)
            {
                int next = (i + 1) % segmentCount;
                if (sideVisible[i])
                {
                    AddProjectedLine(points, front[i], front[next]);
                    AddProjectedLine(points, back[i], back[next]);
                    if (i % 4 == 0)
                        AddProjectedLine(points, front[i], back[i]);

                    if (!sideVisible[(i - 1 + segmentCount) % segmentCount])
                        AddProjectedLine(points, front[i], back[i]);

                    if (!sideVisible[next])
                        AddProjectedLine(points, front[next], back[next]);
                }
            }
        }

        private bool IsCoinSideVisible(double angle)
        {
            double radialX = Math.Cos(angle);
            double radialY = Math.Sin(angle);
            double normalX = radialX * Math.Cos(_rotationY) + radialY * Math.Sin(_rotationX) * Math.Sin(_rotationY);
            double normalY = radialY * Math.Cos(_rotationX);
            double normalZ = -radialX * Math.Sin(_rotationY) + radialY * Math.Sin(_rotationX) * Math.Cos(_rotationY);
            return normalZ < -1e-6;
        }

        private static void AddProjectedLine(List<XYPoint> points, XYPoint start, XYPoint end)
        {
            points.Add(new XYPoint(start.X, start.Y, DimIntensity));
            points.Add(new XYPoint(end.X, end.Y, end.Intensity));
        }

        private void BeginKamonTransition(int targetIndex, double now)
        {
            _transitionFrom = GenerateKamonShape(_currentIndex);
            _transitionTo = GenerateKamonShape(targetIndex);
            int pointCount = Math.Clamp(Math.Max(_transitionFrom.Count, _transitionTo.Count), 96, 640);
            _transitionFrom = ResamplePoints(_transitionFrom, pointCount);
            _transitionTo = ResamplePoints(_transitionTo, pointCount);
            _transitionTargetIndex = targetIndex;
            _transitionStart = now;
            _isTransitioning = true;
        }

        private static List<XYPoint> InterpolateKamon(IReadOnlyList<XYPoint> from, IReadOnlyList<XYPoint> to, double progress)
        {
            double eased = progress * progress * (3 - 2 * progress);
            var result = new List<XYPoint>(from.Count);
            for (int i = 0; i < from.Count; i++)
            {
                XYPoint source = from[i];
                XYPoint target = to[i];
                double spread = Math.Sin(progress * Math.PI) * (0.12 + (i % 7) * 0.025);
                double angle = i * 2.399963229728653;
                double x = source.X + Math.Cos(angle) * spread;
                double y = source.Y + Math.Sin(angle) * spread;
                result.Add(new XYPoint(
                    x + (target.X - x) * eased,
                    y + (target.Y - y) * eased,
                    source.Intensity + (target.Intensity - source.Intensity) * eased));
            }

            return result;
        }

        private static List<XYPoint> ResamplePoints(IReadOnlyList<XYPoint> source, int count)
        {
            var result = new List<XYPoint>(count);
            if (source.Count == 0)
            {
                for (int i = 0; i < count; i++)
                    result.Add(new XYPoint(0, 0, 0.01));
                return result;
            }

            for (int i = 0; i < count; i++)
            {
                double position = i * (source.Count - 1.0) / Math.Max(1, count - 1);
                int lower = (int)position;
                int upper = Math.Min(lower + 1, source.Count - 1);
                double fraction = position - lower;
                XYPoint a = source[lower];
                XYPoint b = source[upper];
                result.Add(new XYPoint(
                    a.X + (b.X - a.X) * fraction,
                    a.Y + (b.Y - a.Y) * fraction,
                    a.Intensity + (b.Intensity - a.Intensity) * fraction));
            }

            return result;
        }

        private XYPoint ProjectPoint(XYPoint point, double depth, out double projectedDepth)
        {
            double scale = GetCoinScale();

            double angleX = _rotationX;
            double angleY = _rotationY;
            double angleZ = _rotationZ;
            double cosZ = Math.Cos(angleZ);
            double sinZ = Math.Sin(angleZ);
            double cosX = Math.Cos(angleX);
            double sinX = Math.Sin(angleX);
            double cosY = Math.Cos(angleY);
            double sinY = Math.Sin(angleY);

            double x = point.X * scale;
            double y = point.Y * scale;
            double z = depth;

            // X軸回転
            double y1 = y * cosX - z * sinX;
            double z1 = y * sinX + z * cosX;

            // Y軸回転
            double x2 = x * cosY + z1 * sinY;
            double z2 = -x * sinY + z1 * cosY;

            // Z軸回転
            double x3 = x2 * cosZ - y1 * sinZ;
            double y3 = x2 * sinZ + y1 * cosZ;

            // 奥行きによる弱い透視投影
            double perspective = 1.0 / (1.0 + z2 * 0.18);
            projectedDepth = z2;
            return new XYPoint(
                Math.Clamp(x3 * perspective, -0.98, 0.98),
                Math.Clamp(y3 * perspective, -0.98, 0.98),
                point.Intensity);
        }

        private double GetCoinScale()
        {
            double audio = Math.Clamp(_audioLevel * 2.8, 0, 1);
            double beat = Math.Clamp(_beatLevel * 3.5, 0, 1);
            return Math.Clamp(0.68 + audio * 0.32 + beat * 1.05, 0.58, 1.95);
        }

        private static void AddRadialLeaf(List<XYPoint> p, double cx, double cy, double angle, double length, double width)
        {
            double cos = Math.Cos(angle);
            double sin = Math.Sin(angle);
            var outline = new List<(double x, double y)>(9);

            for (int i = 0; i <= 4; i++)
            {
                double t = i / 4.0;
                double side = width * Math.Sin(Math.PI * t);
                double x = length * t;
                outline.Add((cx + x * cos - side * sin, cy + x * sin + side * cos));
            }

            for (int i = 4; i >= 0; i--)
            {
                double t = i / 4.0;
                double side = -width * Math.Sin(Math.PI * t);
                double x = length * t;
                outline.Add((cx + x * cos - side * sin, cy + x * sin + side * cos));
            }

            AddCurve(p, outline, true);
            AddLine(p, cx + length * 0.08 * cos, cy + length * 0.08 * sin,
                cx + length * 0.86 * cos, cy + length * 0.86 * sin, DimIntensity);
        }

        private static void AddWaveRing(List<XYPoint> p, double radius, int lobes, double depth)
        {
            var outline = new List<(double x, double y)>(32);
            int count = Math.Max(16, lobes * 8);

            for (int i = 0; i < count; i++)
            {
                double a = i * Math.PI * 2 / count;
                double r = radius - depth * (0.5 + 0.5 * Math.Cos(lobes * a));
                outline.Add((r * Math.Cos(a), r * Math.Sin(a)));
            }

            AddCurve(p, outline, true);
        }

        private static void AddWisteriaCluster(List<XYPoint> p, double cx, double cy, double direction, int berries)
        {
            AddLine(p, cx, cy, cx, cy + direction * 0.25, DimIntensity);

            for (int i = 0; i < berries; i++)
            {
                double t = (i + 0.5) / berries;
                double y = cy + direction * (0.14 + t * 0.42);
                double width = 0.17 * Math.Sin(Math.PI * t);
                AddEllipse(p, cx - width, y, 0.065, 0.085, 8);
                AddEllipse(p, cx + width, y, 0.065, 0.085, 8);
            }
        }

        private static void AddTomoeLobe(List<XYPoint> p, double angle, double radius, double width)
        {
            double cx = radius * Math.Cos(angle);
            double cy = radius * Math.Sin(angle);
            AddRadialLeaf(p, 0, 0, angle, radius + width, width);
            AddCircle(p, cx, cy, width * 0.35, 12);
        }

        private void UpdateAudioAnimation(float[] waveform)
        {
            if (waveform.Length == 0)
                return;

            double sum = 0;
            double bassSum = 0;
            float peak = 0;

            for (int i = 0; i < waveform.Length; i++)
            {
                float sample = waveform[i];
                float absolute = Math.Abs(sample);
                sum += sample * sample;
                peak = Math.Max(peak, absolute);

                // 隣接サンプルの差分が小さい成分を低域として扱う。
                float lowFrequencySample = sample * 0.85f + _previousSample * 0.15f;
                bassSum += lowFrequencySample * lowFrequencySample;
                _previousSample = sample;
            }

            double rms = Math.Sqrt(sum / waveform.Length);
            double bass = Math.Sqrt(bassSum / waveform.Length);
            double beat = Math.Max(0, peak - _audioLevel * 1.35);

            _audioLevel = _audioLevel * 0.82 + rms * 0.18;
            _bassLevel = _bassLevel * 0.86 + bass * 0.14;
            _beatLevel = Math.Max(beat, _beatLevel * 0.82);
            double audioEnergy = Math.Clamp(_audioLevel, 0, 1);
            double bassEnergy = Math.Clamp(_bassLevel, 0, 1);
            double beatEnergy = Math.Clamp(_beatLevel, 0, 1);

            // 各軸を別々の速度・方向で連続回転させる。
            double energy = 1.0 + audioEnergy * 3.0 + bassEnergy * 4.5 + beatEnergy * 12.0;
            _rotationX += 0.018 * energy;
            _rotationY -= 0.027 * (1.0 + audioEnergy * 2.8 + bassEnergy * 2.0 + beatEnergy * 9.0);
            _rotationZ += 0.036 * (1.0 + audioEnergy * 2.0 + bassEnergy * 4.0 + beatEnergy * 8.0);

            _rotationX = WrapAngle(_rotationX);
            _rotationY = WrapAngle(_rotationY);
            _rotationZ = WrapAngle(_rotationZ);
        }

        private bool ConsumeBeatForKamonChange()
        {
            double now = _stopwatch.Elapsed.TotalSeconds;
            double beatThreshold = Math.Max(0.16, _audioLevel * 0.35);
            if (_beatLevel < beatThreshold || now - _lastBeatTime < _beatInterval)
                return false;

            if (_lastBeatTime >= 0)
            {
                double measuredInterval = now - _lastBeatTime;
                _beatInterval = Math.Clamp(_beatInterval * 0.65 + measuredInterval * 0.35, 0.16, 1.5);
            }

            _lastBeatTime = now;
            return true;
        }

        private static double WrapAngle(double angle)
        {
            const double fullTurn = Math.PI * 2;
            angle %= fullTurn;
            return angle < 0 ? angle + fullTurn : angle;
        }

        private static void AddCurve(List<XYPoint> p, IReadOnlyList<(double x, double y)> curve, bool closed = false)
        {
            if (curve.Count < 2)
                return;

            for (int i = 0; i < curve.Count - 1; i++)
                AddLine(p, curve[i].x, curve[i].y, curve[i + 1].x, curve[i + 1].y);

            if (closed)
                AddLine(p, curve[^1].x, curve[^1].y, curve[0].x, curve[0].y);
        }

        private static void AddEllipse(List<XYPoint> p, double cx, double cy, double rx, double ry, int count, double rotation = 0)
        {
            var curve = new List<(double x, double y)>(count);
            double cos = Math.Cos(rotation);
            double sin = Math.Sin(rotation);

            for (int i = 0; i < count; i++)
            {
                double a = i * Math.PI * 2 / count;
                double x = rx * Math.Cos(a);
                double y = ry * Math.Sin(a);
                curve.Add((cx + x * cos - y * sin, cy + x * sin + y * cos));
            }

            AddCurve(p, curve, true);
        }

        private void ShuffleOrder()
        {
            for (int i = _order.Length - 1; i > 0; i--)
            {
                int j = _random.Next(i + 1);
                (_order[i], _order[j]) = (_order[j], _order[i]);
            }
        }

        private static void AddLine(List<XYPoint> p, double x0, double y0, double x1, double y1, double intensity = NormalIntensity)
        {
            p.Add(new XYPoint(x0, y0, DimIntensity));
            p.Add(new XYPoint(x1, y1, intensity));
        }

        private static void AddCircle(List<XYPoint> p, double cx, double cy, double radius, int count)
        {
            for (int i = 0; i < count; i++)
            {
                double a0 = i * Math.PI * 2 / count;
                double a1 = (i + 1) * Math.PI * 2 / count;
                AddLine(p, cx + radius * Math.Cos(a0), cy + radius * Math.Sin(a0), cx + radius * Math.Cos(a1), cy + radius * Math.Sin(a1));
            }
        }

        private static void AddPolygon(List<XYPoint> p, double cx, double cy, double radius, int sides, double rotation = -Math.PI / 2)
        {
            for (int i = 0; i < sides; i++)
            {
                double a0 = rotation + i * Math.PI * 2 / sides;
                double a1 = rotation + (i + 1) * Math.PI * 2 / sides;
                AddLine(p, cx + radius * Math.Cos(a0), cy + radius * Math.Sin(a0), cx + radius * Math.Cos(a1), cy + radius * Math.Sin(a1));
            }
        }

        private static void AddStar(List<XYPoint> p, double cx, double cy, int points, double outer, double inner)
        {
            for (int i = 0; i < points * 2; i++)
            {
                double r0 = (i & 1) == 0 ? outer : inner;
                double r1 = ((i + 1) & 1) == 0 ? outer : inner;
                double a0 = -Math.PI / 2 + i * Math.PI / points;
                double a1 = -Math.PI / 2 + (i + 1) * Math.PI / points;
                AddLine(p, cx + r0 * Math.Cos(a0), cy + r0 * Math.Sin(a0), cx + r1 * Math.Cos(a1), cy + r1 * Math.Sin(a1));
            }
        }

        private static void AddAoi(List<XYPoint> p)
        {
            AddLine(p, 0, -0.62, -0.22, -0.1); AddLine(p, -0.22, -0.1, -0.38, 0.48);
            AddLine(p, 0, -0.62, 0.22, -0.1); AddLine(p, 0.22, -0.1, 0.38, 0.48);
            AddLine(p, -0.38, 0.48, 0, 0.22); AddLine(p, 0, 0.22, 0.38, 0.48);
            AddLine(p, -0.38, 0.48, 0, 0.68); AddLine(p, 0, 0.68, 0.38, 0.48);
        }

        private static void AddDiamond(List<XYPoint> p, double cx, double cy, double r) => AddPolygon(p, cx, cy, r, 4);
        private static void AddSquare(List<XYPoint> p, double cx, double cy, double r) => AddPolygon(p, cx, cy, r, 4, Math.PI / 4);

        private static void AddRice(List<XYPoint> p)
        {
            AddLine(p, -0.55, -0.55, 0.55, 0.55); AddLine(p, -0.55, 0.55, 0.55, -0.55);
            AddLeafPair(p, 3);
        }

        private static void AddFlower(List<XYPoint> p, double cx, double cy, int count, double r)
        {
            for (int i = 0; i < count; i++)
            {
                double a = i * Math.PI * 2 / count;
                AddEllipse(p, cx + Math.Cos(a) * r * 0.38, cy + Math.Sin(a) * r * 0.38, r * 0.30, r * 0.48, 10, a);
            }
            AddCircle(p, cx, cy, r * 0.17, 12);
        }

        private static void AddPetal(List<XYPoint> p, double cx, double cy, double radius, double angle, bool pointed)
        {
            double cos = Math.Cos(angle);
            double sin = Math.Sin(angle);
            var curve = new List<(double x, double y)>(7);

            for (int i = 0; i <= 6; i++)
            {
                double t = i / 6.0;
                double y = radius * Math.Sin(Math.PI * t);
                double x = pointed
                    ? radius * (t - 0.5) * 2.0
                    : radius * 0.9 * Math.Sin(Math.PI * (t - 0.5));
                curve.Add((cx + x * cos - y * sin, cy + x * sin + y * cos));
            }

            AddCurve(p, curve);
            for (int i = 1; i < curve.Count - 1; i += 2)
                AddLine(p, cx, cy, curve[i].x, curve[i].y, DimIntensity);
        }

        private static void AddPlum(List<XYPoint> p, bool sword)
        {
            for (int i = 0; i < 5; i++)
                AddPetal(p, 0, 0, 0.56, -Math.PI / 2 + i * Math.PI * 2 / 5, false);

            AddCircle(p, 0, 0, 0.12, 10);
            if (sword)
                AddStar(p, 0, 0, 5, 0.58, 0.28);
        }

        private static void AddKatabami(List<XYPoint> p, bool sword)
        {
            for (int i = 0; i < 3; i++)
            {
                double angle = -Math.PI / 2 + i * Math.PI * 2 / 3;
                double x = Math.Cos(angle) * 0.25;
                double y = Math.Sin(angle) * 0.25;
                AddPetal(p, x, y, 0.48, angle, false);
                AddLine(p, 0, 0, x, y, DimIntensity);
            }

            if (sword)
                AddStar(p, 0, 0, 3, 0.66, 0.28);
        }

        private static void AddKikyo(List<XYPoint> p, bool sword)
        {
            AddStar(p, 0, 0, 5, 0.60, 0.28);
            for (int i = 0; i < 5; i++)
            {
                double a = -Math.PI / 2 + i * Math.PI * 2 / 5;
                AddLine(p, 0, 0, 0.57 * Math.Cos(a), 0.57 * Math.Sin(a), DimIntensity);
            }

            if (sword)
                AddStar(p, 0, 0, 5, 0.65, 0.18);
        }

        private static void AddHanabishi(List<XYPoint> p, bool sword)
        {
            for (int i = 0; i < 4; i++)
            {
                double a = Math.PI / 4 + i * Math.PI / 2;
                double cx = Math.Cos(a) * 0.28;
                double cy = Math.Sin(a) * 0.28;
                AddDiamond(p, cx, cy, 0.38);
            }

            if (sword)
                AddStar(p, 0, 0, 4, 0.65, 0.22);
        }

        private static void AddScale(List<XYPoint> p, int count)
        {
            for (int i = 0; i < count; i++)
                AddPolygon(p, (i - (count - 1) / 2.0) * 0.28, 0, 0.22, 3);
        }

        private static void AddLeaf(List<XYPoint> p, double x, double y, double angle)
        {
            double length = 0.55;
            double width = 0.22;
            double cos = Math.Cos(angle);
            double sin = Math.Sin(angle);
            var curve = new List<(double x, double y)>(9);

            for (int i = 0; i <= 4; i++)
            {
                double t = i / 4.0;
                double side = width * Math.Sin(Math.PI * t) * (1.0 - 0.18 * t);
                double localX = length * t;
                curve.Add((x + localX * cos - side * sin, y + localX * sin + side * cos));
            }

            for (int i = 4; i >= 0; i--)
            {
                double t = i / 4.0;
                double side = -width * Math.Sin(Math.PI * t) * (1.0 - 0.18 * t);
                double localX = length * t;
                curve.Add((x + localX * cos - side * sin, y + localX * sin + side * cos));
            }

            AddCurve(p, curve, true);

            for (int i = 1; i < 3; i++)
            {
                double t = i / 3.0;
                double centerX = x + length * t * cos;
                double centerY = y + length * t * sin;
                double side = width * Math.Sin(Math.PI * t) * 0.72;
                AddLine(p, centerX, centerY, centerX - side * sin, centerY + side * cos, DimIntensity);
            }
        }

        private static void AddLeafPair(List<XYPoint> p, int count)
        {
            for (int i = 0; i < count; i++)
            {
                double y = -0.35 + i * 0.18;
                AddLeaf(p, -0.08, y, Math.PI + 0.5); AddLeaf(p, 0.08, y, -0.5);
            }
        }

        private static void AddFan(List<XYPoint> p, int count)
        {
            for (int i = 0; i < count; i++)
            {
                double x = (i - (count - 1) / 2.0) * 0.16;
                AddLine(p, 0, -0.42, x, 0.45);
            }
            AddLine(p, -0.48, 0.45, 0.48, 0.45);
        }

        private static void AddBird(List<XYPoint> p, double offset)
        {
            AddLine(p, offset - 0.24, 0, offset, 0.18); AddLine(p, offset, 0.18, offset + 0.24, 0);
            AddLine(p, offset - 0.24, 0, offset - 0.05, -0.12); AddLine(p, offset + 0.24, 0, offset + 0.05, -0.12);
        }

        private static void AddClover(List<XYPoint> p, int count)
        {
            for (int i = 0; i < count; i++)
            {
                double a = i * Math.PI * 2 / count;
                AddCircle(p, Math.Cos(a) * 0.28, Math.Sin(a) * 0.28, 0.27, 12);
            }
        }

        private static void Add桐(List<XYPoint> p, int flowers, double r)
        {
            AddLine(p, 0, -0.55, 0, 0.58);
            for (int i = 0; i < flowers; i++)
            {
                double y = 0.38 - i * 0.28;
                AddCircle(p, -0.22, y, r * 0.25, 10); AddCircle(p, 0.22, y, r * 0.25, 10);
            }
        }

        private static void AddWheel(List<XYPoint> p, int count)
        {
            AddCircle(p, 0, 0, 0.58, 32);
            for (int i = 0; i < count; i++)
            {
                double a = i * Math.PI * 2 / count;
                AddLine(p, 0, 0, 0.58 * Math.Cos(a), 0.58 * Math.Sin(a));
            }
        }

        private static void AddBamboo(List<XYPoint> p, int count)
        {
            AddLine(p, 0, -0.65, 0, 0.65);
            for (int i = 0; i < count; i++)
            {
                double y = -0.5 + i * 1.0 / Math.Max(1, count - 1);
                AddLine(p, -0.3, y, 0, y + 0.12); AddLine(p, 0, y + 0.12, 0.3, y);
            }
        }

        private static void AddWave(List<XYPoint> p)
        {
            for (int i = 0; i < 4; i++)
            {
                double y = -0.4 + i * 0.27;
                for (int j = 0; j < 12; j++)
                {
                    double x0 = -0.6 + j * 0.1, x1 = x0 + 0.1;
                    AddLine(p, x0, y + Math.Sin(j * Math.PI / 3) * 0.08, x1, y + Math.Sin((j + 1) * Math.PI / 3) * 0.08);
                }
            }
        }

        private static void AddTree(List<XYPoint> p, int count)
        {
            AddLine(p, 0, -0.58, 0, 0.35);
            for (int i = 0; i < count; i++)
                AddPolygon(p, (i - (count - 1) / 2.0) * 0.3, 0.42 - Math.Abs(i - (count - 1) / 2.0) * 0.14, 0.3, 3);
        }

        private static void AddFeatherPair(List<XYPoint> p, double offset)
        {
            AddLine(p, offset - 0.28, -0.5, offset + 0.28, 0.5);
            for (int i = 0; i < 5; i++)
            {
                double y = -0.35 + i * 0.18;
                AddLine(p, offset, y, offset - 0.24, y + 0.13); AddLine(p, offset, y, offset + 0.24, y - 0.13);
            }
        }

        private static void Add橘(List<XYPoint> p)
        {
            AddCircle(p, 0, -0.1, 0.32, 20); AddCircle(p, -0.25, 0.18, 0.22, 16); AddCircle(p, 0.25, 0.18, 0.22, 16);
            AddLeaf(p, 0, 0.15, Math.PI / 2);
        }

        private static void AddFruit(List<XYPoint> p, int count)
        {
            for (int i = 0; i < count; i++) AddCircle(p, (i - (count - 1) / 2.0) * 0.3, 0, 0.18, 14);
        }

        private static void AddCross(List<XYPoint> p)
        {
            AddLine(p, -0.58, 0, 0.58, 0); AddLine(p, 0, -0.58, 0, 0.58);
        }

        private static void Add丁字(List<XYPoint> p, int count)
        {
            for (int i = 0; i < count; i++)
            {
                double x = (i - (count - 1) / 2.0) * 0.3;
                AddLine(p, x - 0.18, 0.3, x + 0.18, 0.3); AddLine(p, x, 0.3, x, -0.3);
            }
        }

        private static void AddTomoe(List<XYPoint> p, int count)
        {
            for (int i = 0; i < count; i++)
            {
                double a = i * Math.PI * 2 / count;
                AddTomoeLobe(p, a, 0.42, 0.25);
            }
            AddCircle(p, 0, 0, 0.13, 12);
        }

        private static void AddTomoeSwirl(List<XYPoint> p, double angle, double radius, double width)
        {
            var curve = new List<(double x, double y)>(25);
            double cos = Math.Cos(angle);
            double sin = Math.Sin(angle);

            for (int i = 0; i <= 24; i++)
            {
                double t = i / 24.0;
                double a = Math.PI * 1.35 * t;
                double r = radius * (0.22 + 0.78 * t);
                double x = r * Math.Cos(a);
                double y = width * Math.Sin(Math.PI * t) + r * Math.Sin(a);
                curve.Add((x * cos - y * sin, x * sin + y * cos));
            }

            AddCurve(p, curve);
            AddCircle(p, radius * Math.Cos(angle), radius * Math.Sin(angle), width * 0.32, 16);
        }

        private static void AddButterfly(List<XYPoint> p, bool circle)
        {
            AddLeaf(p, -0.08, 0, -2.4); AddLeaf(p, 0.08, 0, -0.7); AddLeaf(p, -0.08, 0, 2.4); AddLeaf(p, 0.08, 0, 0.7);
            AddEllipse(p, 0, 0, 0.055, 0.40, 18);
            if (circle) AddCircle(p, 0, 0, 0.62, 32);
        }

        private static void AddIvy(List<XYPoint> p, int count)
        {
            for (int i = 0; i < count; i++) AddLeaf(p, -0.2 + i * 0.08, -0.45 + i * 0.16, i % 2 == 0 ? 0.7 : 2.4);
        }

        private static void AddAngle(List<XYPoint> p)
        {
            AddLine(p, -0.5, -0.5, 0, 0); AddLine(p, 0, 0, 0.5, -0.5); AddLine(p, -0.5, 0.5, 0, 0); AddLine(p, 0, 0, 0.5, 0.5);
        }

        private static void AddCrane(List<XYPoint> p)
        {
            AddCircle(p, 0, 0, 0.42, 24); AddLine(p, -0.4, 0.1, 0, -0.2); AddLine(p, 0, -0.2, 0.4, 0.1);
        }

        private static void AddHolly(List<XYPoint> p)
        {
            AddLine(p, 0, -0.55, 0, 0.55); AddLeaf(p, 0, 0, 0.5); AddLeaf(p, 0, 0, 2.6); AddCircle(p, 0, -0.2, 0.12, 12);
        }

        private static void AddDiamondStack(List<XYPoint> p, int count)
        {
            for (int i = 0; i < count; i++) AddDiamond(p, 0, (i - (count - 1) / 2.0) * 0.32, 0.27);
        }

        private static void AddHorizontalBars(List<XYPoint> p, int count)
        {
            for (int i = 0; i < count; i++) AddLine(p, -0.52, (i - (count - 1) / 2.0) * 0.28, 0.52, (i - (count - 1) / 2.0) * 0.28);
        }

        private static void AddWisteria(List<XYPoint> p, bool down)
        {
            double direction = down ? -1 : 1;
            AddLeafPair(p, 4);
            for (int i = 0; i < 4; i++) AddCircle(p, (i - 1.5) * 0.16, direction * (0.15 + i * 0.1), 0.1, 10);
        }

        private static void AddBottle(List<XYPoint> p, int count)
        {
            for (int i = 0; i < count; i++)
            {
                double x = (i - (count - 1) / 2.0) * 0.3;
                AddLine(p, x - 0.12, -0.45, x - 0.18, 0.3); AddLine(p, x - 0.18, 0.3, x - 0.08, 0.48);
                AddLine(p, x - 0.08, 0.48, x + 0.08, 0.48); AddLine(p, x + 0.08, 0.48, x + 0.18, 0.3); AddLine(p, x + 0.18, 0.3, x + 0.12, -0.45); AddLine(p, x + 0.12, -0.45, x - 0.12, -0.45);
            }
        }

        private static void AddStars(List<XYPoint> p, int count)
        {
            for (int i = 0; i < count; i++)
            {
                double a = i * Math.PI * 2 / count;
                AddStar(p, Math.Cos(a) * 0.3, Math.Sin(a) * 0.3, 8, 0.12, 0.045);
            }
        }

        private static void AddPine(List<XYPoint> p, int count)
        {
            AddLine(p, 0, -0.6, 0, 0.3);
            for (int i = 0; i < count; i++) AddPolygon(p, (i - (count - 1) / 2.0) * 0.28, 0.4, 0.24, 3);
        }

        private static void AddGinger(List<XYPoint> p)
        {
            for (int i = 0; i < 5; i++) AddLeaf(p, 0, 0, -Math.PI / 2 + (i - 2) * 0.35);
        }

        private static void AddEyes(List<XYPoint> p)
        {
            for (int x = -1; x <= 1; x++) for (int y = -1; y <= 1; y++) AddSquare(p, x * 0.25, y * 0.25, 0.1);
        }

        private static void AddMokko(List<XYPoint> p, bool sword)
        {
            AddWaveRing(p, 0.58, 4, 0.12);
            for (int i = 0; i < 8; i++)
            {
                double a = i * Math.PI / 4;
                AddLine(p, 0, 0, 0.47 * Math.Cos(a), 0.47 * Math.Sin(a), DimIntensity);
            }
            if (sword) AddStar(p, 0, 0, 4, 0.65, 0.25);
        }

        private static void AddArrows(List<XYPoint> p, int count)
        {
            for (int i = 0; i < count; i++)
            {
                double x = (i - (count - 1) / 2.0) * 0.35;
                AddLine(p, x - 0.18, -0.55, x + 0.18, 0.55); AddLine(p, x + 0.18, 0.55, x + 0.05, 0.35); AddLine(p, x + 0.18, 0.55, x - 0.02, 0.5);
            }
        }
    }
}