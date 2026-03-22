using OpenTK.Audio.OpenAL;
using OscVisualizer.Models;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OscVisualizer.Services
{
    public class XYProcessor
    {
        // OpenAL
        private readonly int _source;
        private readonly int _outputTargetSampleRate;

        // JS版の状態
        private List<XYPoint> _points = new();
        private int _index = 0;              // 現在の点インデックス（偶数: p0, 奇数: p1）
        private double _subPos = 0;             // 線分内サンプル位置
        private int _blankSamples = 0;       // 線分間ブランキングサンプル数

        // 距離ベース制御用
        private List<double> _segmentLengths = new(); // 各線分の長さ
        private double _totalLength = 0;             // 全線分の合計長

        // 設定
        private int _minSamplesPerSegment = 2;
        private double _speedScale = 1.0;   // 1.0 = 等速、0.5 = 2倍速、2.0 = 半速
        private int _phaseShift = 0;

        public void SetBlankSamples(int samples) => _blankSamples = Math.Max(0, samples);

        public double SpeedScale
        {
            get => _speedScale;
            set => _speedScale = value;
        }

        public int PhaseShift
        {
            get => _phaseShift;
            set => _phaseShift = value;
        }

        public bool InvertX { get; set; } = false;

        public bool InvertY { get; set; } = false;

        public void SetMinSamplesPerSegment(int v) => _minSamplesPerSegment = Math.Max(1, v);

        private bool _skipNextProcess = false;

        // 1フレームあたりのサンプル数（AudioWorklet の outL.length 相当）
        private readonly int _frameSamples;

        /// <summary>
        /// Initializes a new instance of the XYProcessor class with the specified data source, sample rate, and frame
        /// sample size.
        /// </summary>
        /// <remarks>The XYProcessor is intended for audio or signal processing scenarios. The
        /// frameSamples parameter allows for flexibility in processing different frame sizes, which can affect
        /// performance and analysis granularity.</remarks>
        /// <param name="source">The identifier for the data source to be processed. Must be a valid source identifier.</param>
        /// <param name="outputTargetSampleRate">The sample rate, in hertz (Hz), at which the data will be processed. Must be greater than zero.</param>
        /// <param name="frameSamples">The number of samples per frame. Must be a positive integer. The default value is 1024.</param>
        public XYProcessor(int source, int outputTargetSampleRate, int frameSamples = 1024)
        {
            _source = source;
            _outputTargetSampleRate = outputTargetSampleRate;
            _frameSamples = frameSamples;
            _delayBufferL = new float[_maxDelaySamples];
            _delayBufferR = new float[_maxDelaySamples];
        }

        // JS: setPoints + _setupSegments
        /// <summary>
        /// Sets the collection of points to be processed by the instance.
        /// </summary>
        /// <remarks>This method has no effect if the previous operation was marked to be skipped. After
        /// setting the points, the internal state is initialized for segment processing.</remarks>
        /// <param name="points">The list of points to set. If null, an empty list is used instead.</param>
        public void SetPoints(List<XYPoint> points)
        {
            if (_skipNextProcess)
                return;

            _skipNextProcess = true;
            _points = points ?? new List<XYPoint>();
            _index = 0;
            _subPos = 0;
            SetupSegments(_points);
        }

        /// <summary>
        /// Initializes the segment lengths and calculates the total length of all valid segments formed by pairs of
        /// points in the collection.
        /// </summary>
        /// <remarks>This method clears any existing segment length data and processes the points in the
        /// collection as pairs, treating the collection as circular by connecting the last point back to the first. If
        /// fewer than two points are present, no segments are created. If no valid segments are found, a minimal total
        /// length is assigned to prevent division by zero in subsequent calculations.</remarks>
        private void SetupSegments(List<XYPoint> pts)
        {
            _segmentLengths.Clear();
            _totalLength = 0f;

            int N = pts.Count;
            if (N < 2) return;

            for (int i = 0; i < N; i += 2)
            {
                var p0 = pts[i];
                if (InvertX)
                    p0 = new XYPoint(-p0.X, p0.Y, p0.Intensity);
                if (InvertY)
                    p0 = new XYPoint(p0.X, -p0.Y, p0.Intensity);
                pts[i] = p0;

                var p1 = pts[(i + 1) % N];
                if (InvertX)
                    p1 = new XYPoint(-p1.X, p1.Y, p1.Intensity);
                if (InvertY)
                    p1 = new XYPoint(p1.X, -p1.Y, p1.Intensity);
                pts[(i + 1) % N] = p1;

                var clipped = ClipSegment(p0, p1);
                if (clipped != null)
                {
                    var (a, b) = clipped.Value;
                    double dx = b.X - a.X;
                    double dy = b.Y - a.Y;
                    double len = Math.Sqrt(dx * dx + dy * dy);

                    _segmentLengths.Add(len);
                    _totalLength += len;
                }
            }

            if (_totalLength <= 0f)
                _totalLength = 1e-6f;

            //HACK: 通常はコメントアウトする
            _totalLength = 10;
        }

        // JS: intersect(p0, p1, bound, isX)
        /// <summary>
        /// Calculates the intersection point between a line segment defined by two points and a specified axis-aligned
        /// boundary.
        /// </summary>
        /// <remarks>The method returns <see langword="null"/> if the line segment is parallel to the
        /// specified boundary or if the intersection point lies outside the segment.</remarks>
        /// <param name="p0">The starting point of the line segment.</param>
        /// <param name="p1">The ending point of the line segment.</param>
        /// <param name="bound">The coordinate value of the boundary to intersect with. Interpreted as an x-coordinate if <paramref
        /// name="isX"/> is <see langword="true"/>, or as a y-coordinate if <paramref name="isX"/> is <see
        /// langword="false"/>.</param>
        /// <param name="isX">A value indicating whether the intersection is calculated with respect to the x-axis (<see
        /// langword="true"/>) or the y-axis (<see langword="false"/>).</param>
        /// <returns>A <see cref="Point"/> representing the intersection point if the line segment crosses the specified
        /// boundary; otherwise, <see langword="null"/> if there is no intersection within the segment.</returns>
        private XYPoint? Intersect(XYPoint p0, XYPoint p1, double bound, bool isX)
        {
            double x0 = p0.X, y0 = p0.Y;
            double x1 = p1.X, y1 = p1.Y;

            if (isX)
            {
                double dx = x1 - x0;
                if (dx == 0f) return null;

                double t = (bound - x0) / dx;
                if (t < 0f || t > 1f) return null;

                double y = y0 + (y1 - y0) * t;
                double b = p0.Intensity + (p1.Intensity - p0.Intensity) * t;

                return new XYPoint(bound, y, b);
            }
            else
            {
                double dy = y1 - y0;
                if (dy == 0f) return null;

                double t = (bound - y0) / dy;
                if (t < 0f || t > 1f) return null;

                double x = x0 + (x1 - x0) * t;
                double b = p0.Intensity + (p1.Intensity - p0.Intensity) * t;

                return new XYPoint(x, bound, b);
            }
        }

        // JS: clipSegment(p0, p1)
        /// <summary>
        /// Clips a line segment defined by two points to the bounds of a unit square centered at the origin.
        /// </summary>
        /// <remarks>The unit square is defined by the region where both X and Y coordinates are between
        /// -1 and 1, inclusive. If both endpoints are inside the square, the original segment is returned. If both are
        /// outside and the segment crosses the square, the intersection points with the square's edges are returned. If
        /// only one endpoint is inside, the segment is clipped at the intersection with the square's
        /// boundary.</remarks>
        /// <param name="p0">The starting point of the line segment to be clipped.</param>
        /// <param name="p1">The ending point of the line segment to be clipped.</param>
        /// <returns>A tuple containing the endpoints of the clipped segment if the original segment intersects the unit square;
        /// otherwise, <see langword="null"/>.</returns>
        private (XYPoint a, XYPoint b)? ClipSegment(XYPoint p0, XYPoint p1)
        {
            bool Inside(XYPoint p) =>
                p.X >= -1f && p.X <= 1f && p.Y >= -1f && p.Y <= 1f;

            bool p0Inside = Inside(p0);
            bool p1Inside = Inside(p1);

            var bounds = new List<(double bound, bool isX)>
            {
                (-1, true),  // x = -1
                ( 1, true),  // x =  1
                (-1, false), // y = -1
                ( 1, false), // y =  1
            };

            var intersections = new List<XYPoint>();

            foreach (var (bound, isX) in bounds)
            {
                var p = Intersect(p0, p1, bound, isX);
                if (p != null)
                    intersections.Add(p);
            }

            // ケース1：両端が内側 → そのまま
            if (p0Inside && p1Inside)
                return (p0, p1);

            // ケース2：両端が外側
            if (!p0Inside && !p1Inside)
            {
                if (intersections.Count == 2)
                {
                    intersections.Sort((a, b) =>
                    {
                        double ta = (a.X - p0.X) * (a.X - p0.X) + (a.Y - p0.Y) * (a.Y - p0.Y);
                        double tb = (b.X - p0.X) * (b.X - p0.X) + (b.Y - p0.Y) * (b.Y - p0.Y);
                        return ta.CompareTo(tb);
                    });
                    return (intersections[0], intersections[1]);
                }
                return null;
            }

            // ケース3：p0 外 → p1 内
            if (!p0Inside && p1Inside)
            {
                if (intersections.Count > 0)
                {
                    intersections.Sort((a, b) =>
                    {
                        double ta = (a.X - p1.X) * (a.X - p1.X) + (a.Y - p1.Y) * (a.Y - p1.Y);
                        double tb = (b.X - p1.X) * (b.X - p1.X) + (b.Y - p1.Y) * (b.Y - p1.Y);
                        return ta.CompareTo(tb);
                    });
                    var i0 = intersections[0];
                    return (i0, p1);
                }
                return null;
            }

            // ケース4：p0 内 → p1 外
            if (p0Inside && !p1Inside)
            {
                if (intersections.Count > 0)
                {
                    intersections.Sort((a, b) =>
                    {
                        double ta = (a.X - p0.X) * (a.X - p0.X) + (a.Y - p0.Y) * (a.Y - p0.Y);
                        double tb = (b.X - p0.X) * (b.X - p0.X) + (b.Y - p0.Y) * (b.Y - p0.Y);
                        return ta.CompareTo(tb);
                    });
                    var i0 = intersections[0];
                    return (p0, i0);
                }
                return null;
            }

            return null;
        }

        // JS: process() 相当 → 1フレーム分の PCM を返す
        /// <summary>
        /// Generates a buffer containing a sequence of two-dimensional PCM (Pulse Code Modulation) data points for the
        /// current frame, based on the configured set of points and frame duration.
        /// </summary>
        /// <remarks>This method processes a series of points to produce a two-dimensional PCM
        /// representation suitable for audio or signal processing applications. Segments that are too short or clipped
        /// are represented as zeros in the output. The method maintains internal state to ensure continuity across
        /// frames.</remarks>
        /// <returns>An array of floats representing the PCM data for the current frame. The array contains interleaved X and Y
        /// values. If there are insufficient points or the total length is not positive, the array is filled with
        /// zeros.</returns>
        public float[] GenerateXYBuffer()
        {
            float[] pcm = new float[_frameSamples * 2];

            List<XYPoint> pts = _points;

            int N = pts.Count;
            if (N < 2 || _totalLength <= 0f)
            {
                // 全部 0
                _skipNextProcess = false;
                return pcm;
            }

            double scale = (_frameSamples / _totalLength) * _speedScale / 4;
            _index = _index % N;

            for (int i = 0; i < _frameSamples; i++)
            {
                var p0 = pts[_index];
                var p1 = pts[(_index + 1) % N];

                var clipped = ClipSegment(p0, p1);

                if (clipped == null)
                {
                    _subPos = 0;
                    if (_index + 2 >= N)
                        _skipNextProcess = false;
                    _index = (_index + 2) % N;

                    pcm[i * 2 + 0] = (float)(Random.Shared.NextDouble() * 2f) - 1f;
                    pcm[i * 2 + 1] = (float)(Random.Shared.NextDouble() * 2f) - 1f;
                    continue;
                }

                var (a, b) = clipped.Value;
                double dx = b.X - a.X;
                double dy = b.Y - a.Y;
                double db = b.Intensity - a.Intensity;

                double len = Math.Sqrt(dx * dx + dy * dy);

                if (len <= 1e-9f)
                {
                    //_subPos = 0;
                    //if (_index + 2 >= N)
                    //    _skipNextProcess = false;
                    //_index = (_index + 2) % N;

                    //pcm[i * 2 + 0] = (float)a.X;
                    //pcm[i * 2 + 1] = (float)a.Y;
                    //continue;
                    len = 0.05;
                }

                int samplesPerSegment = Math.Max(
                    _minSamplesPerSegment,
                    (int)Math.Floor(len * scale)
                );

                // t は 0〜1
                double t = _subPos / samplesPerSegment;

                // ★ Brightness を線形補間
                double brightness = a.Intensity + db * t;

                // ★ 速度スケール（Brightness が低いと速くする。高いと遅くする）
                double speedFactor = Math.Max(0.0001, Math.Exp(-brightness));

                // ★ 位置補間
                double x = a.X + dx * t;
                double y = a.Y + dy * t;

                pcm[i * 2 + 0] = (float)x;
                pcm[i * 2 + 1] = (float)y;

                // ★ subPos の進み方に Brightness を反映
                _subPos += speedFactor;

                if (_subPos > samplesPerSegment + _blankSamples)
                {
                    _subPos = 0;
                    if (_index + 2 >= N)
                        _skipNextProcess = false;
                    _index = (_index + 2) % N;
                }
            }

            return pcm;
        }

        // OpenAL の Update ループ用
        /// <summary>
        /// Processes audio buffers in the OpenAL update loop to ensure continuous and responsive audio playback.
        /// </summary>
        /// <remarks>Call this method regularly, such as once per frame, to maintain uninterrupted audio
        /// streaming. The method handles unqueuing processed buffers, generating new audio data, and re-queuing the
        /// updated buffers to the audio source. Failure to call this method frequently enough may result in audio
        /// glitches or playback interruptions.</remarks>
        public void Update()
        {
            AL.GetSource(_source, ALGetSourcei.BuffersProcessed, out int processed);

            while (processed-- > 0)
            {
                int buf = AL.SourceUnqueueBuffer(_source);

                float[] pcm = GenerateXYBuffer();

                int frames = pcm.Length / 2;

                if (PhaseShift > 0)
                {
                    _writePosR = 0;

                    for (int i = 0; i < frames; i++)
                    {
                        float L = pcm[i * 2];

                        // 遅延バッファに書き込み
                        _delayBufferL[_writePosL] = L;

                        // 読み出し位置（動的に変化）
                        int readPos = _writePosL - PhaseShift;
                        if (readPos < 0)
                            readPos += _maxDelaySamples;
                        if (readPos >= _maxDelaySamples)
                            readPos -= _maxDelaySamples;

                        // 遅延された L を取得
                        float delayedL = _delayBufferL[readPos];

                        // L を置き換え
                        pcm[i * 2] = delayedL;

                        // 書き込み位置を進める
                        _writePosL++;
                        if (_writePosL >= _maxDelaySamples)
                            _writePosL = 0;
                    }
                }
                else if (PhaseShift < 0)
                {
                    _writePosL = 0;

                    for (int i = 0; i < frames; i++)
                    {
                        float R = pcm[i * 2 + 1];

                        // 遅延バッファに書き込み
                        _delayBufferR[_writePosR] = R;

                        // 読み出し位置（動的に変化）
                        int readPos = _writePosR + PhaseShift;
                        if (readPos < 0)
                            readPos += _maxDelaySamples;
                        if (readPos >= _maxDelaySamples)
                            readPos -= _maxDelaySamples;

                        // 遅延された R を取得
                        float delayedR = _delayBufferR[readPos];

                        // R を置き換え
                        pcm[i * 2 + 1] = delayedR;

                        // 書き込み位置を進める
                        _writePosR++;
                        if (_writePosR >= _maxDelaySamples)
                            _writePosR = 0;
                    }
                }

                AL.BufferData(buf, ALFormat.StereoFloat32Ext, pcm, _outputTargetSampleRate);

                AL.SourceQueueBuffer(_source, buf);
            }
        }

        private float[] _delayBufferL;
        private int _writePosL = 0;
        private float[] _delayBufferR;
        private int _writePosR = 0;
        private int _maxDelaySamples = 65535; // 最大遅延（必要に応じて調整）
    }
}
