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
            //_totalLength = 10;
        }

        // Liang-Barsky line clipping for rect [-1,1] x [-1,1]
        /// <summary>
        /// Clips a line segment defined by two points to the bounds of a unit square centered at the origin
        /// using the Liang-Barsky algorithm.
        /// </summary>
        /// <param name="p0">The starting point of the line segment to be clipped.</param>
        /// <param name="p1">The ending point of the line segment to be clipped.</param>
        /// <returns>A tuple containing the endpoints of the clipped segment if the original segment intersects the unit square;
        /// otherwise, <see langword="null"/>.</returns>
        private (XYPoint a, XYPoint b)? ClipSegment(XYPoint p0, XYPoint p1)
        {
            double dx = p1.X - p0.X;
            double dy = p1.Y - p0.Y;

            double t0 = 0.0;
            double t1 = 1.0;

            // p, q for each edge: left, right, bottom, top
            ReadOnlySpan<double> p = [  -dx,    dx,   -dy,    dy];
            ReadOnlySpan<double> q = [p0.X + 1, 1 - p0.X, p0.Y + 1, 1 - p0.Y];

            for (int i = 0; i < 4; i++)
            {
                if (p[i] == 0.0)
                {
                    // 線分は境界と平行
                    if (q[i] < 0.0)
                        return null; // 完全に外側
                    // そうでなければこの境界は無視
                }
                else
                {
                    double r = q[i] / p[i];
                    if (p[i] < 0.0)
                    {
                        // 線分が矩形に入る側
                        if (r > t1) return null;
                        if (r > t0) t0 = r;
                    }
                    else
                    {
                        // 線分が矩形から出る側
                        if (r < t0) return null;
                        if (r < t1) t1 = r;
                    }
                }
            }

            double di = p1.Intensity - p0.Intensity;

            var a = new XYPoint(p0.X + t0 * dx, p0.Y + t0 * dy, p0.Intensity + t0 * di);
            var b = new XYPoint(p0.X + t1 * dx, p0.Y + t1 * dy, p0.Intensity + t1 * di);

            return (a, b);
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
            int consecutiveSkips = 0;

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

                    // 全線分が画面外ならループを終了（残りは 0 のまま）
                    if (++consecutiveSkips >= N / 2)
                    {
                        _skipNextProcess = false;
                        break;
                    }
                    i--; // 同じサンプル位置で次の線分を試行
                    continue;
                }

                consecutiveSkips = 0;

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
                double speedFactor = Math.Max(0.000001, Math.Exp(-brightness));

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
