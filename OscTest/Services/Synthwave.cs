using MathNet.Numerics;
using MathNet.Numerics.IntegralTransforms;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using OscVisualizer.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;

namespace OscVisualizer.Services
{
    internal sealed class Synthwave : IAudioVisualizer
    {
        // ============================================================
        // Constants
        // ============================================================

        private const float HighPassR = 0.995f;

        private const int GridLines = 8;
        private const int VerticalLines = 8;

        private const int SunLines = 12;
        private const float HorizonY = 0.11f;
        private const float SunCenterY = 0.46f;
        private const float SunRadius = 0.35f;

        private const int TailLeftIndex = 14;
        private const int TailRightIndex = 16;
        private const int TailMax = 15;

        private const float ProjectionDistance = 1.0f;

        // ============================================================
        // Audio / Timing state
        // ============================================================

        private float _prevX;
        private float _prevY;

        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private double _lastTime;

        private float _scroll;

        // FFT バッファを毎フレーム new しない
        private Complex32[] _fftBuffer = Array.Empty<Complex32>();
        private float[] _spectrumBuffer = Array.Empty<float>();

        // ============================================================
        // Tail history
        // ============================================================

        private readonly List<(Vector2 Left, Vector2 Right)> _tailHistory =
            new(TailMax);

        // ============================================================
        // Public properties
        // ============================================================

        public string VisualizerName => "Synthwave";

        public string? SelectedDevice { get; set; }

        // ============================================================
        // Audio processing
        // ============================================================

        public List<XYPoint> ProcessAudio(
            WasapiCapture capture,
            WaveInEventArgs e)
        {
            int sampleRate = capture.WaveFormat.SampleRate;

            float[] wav = IAudioVisualizer.ConvertToWav1ch(capture, e);

            if (wav.Length == 0)
                return new List<XYPoint>();

            ApplyHighPass(wav);

            EnsureFftBuffers(wav.Length);

            // FFT入力作成
            for (int i = 0; i < wav.Length; i++)
            {
                _fftBuffer[i] = new Complex32(wav[i], 0f);
            }

            Fourier.Forward(
                _fftBuffer,
                FourierOptions.Matlab);

            // 振幅スペクトル
            int spectrumLength = wav.Length / 2;

            for (int i = 0; i < spectrumLength; i++)
            {
                _spectrumBuffer[i] = _fftBuffer[i].Magnitude;
            }

            double now = _stopwatch.Elapsed.TotalSeconds;
            float time = (float)now;
            float deltaTime = CalculateDeltaTime(now);

            return GenerateXYBuffer(
                _spectrumBuffer,
                time,
                deltaTime,
                sampleRate);
        }

        // ============================================================
        // High-pass filter
        // ============================================================

        private void ApplyHighPass(float[] samples)
        {
            for (int i = 0; i < samples.Length; i++)
            {
                float x = samples[i];

                float y =
                    x -
                    _prevX +
                    HighPassR * _prevY;

                _prevX = x;
                _prevY = y;

                samples[i] = y;
            }
        }

        public void ResetHighPass()
        {
            _prevX = 0f;
            _prevY = 0f;
        }

        // ============================================================
        // FFT buffers
        // ============================================================

        private void EnsureFftBuffers(int sampleCount)
        {
            if (_fftBuffer.Length != sampleCount)
            {
                _fftBuffer = new Complex32[sampleCount];
            }

            int spectrumLength = sampleCount / 2;

            if (_spectrumBuffer.Length != spectrumLength)
            {
                _spectrumBuffer = new float[spectrumLength];
            }
        }

        // ============================================================
        // Timing
        // ============================================================

        private float CalculateDeltaTime(double now)
        {
            // 初回だけ大きな deltaTime が発生するのを防止
            if (_lastTime <= 0)
            {
                _lastTime = now;
                return 0f;
            }

            float delta = (float)(now - _lastTime);
            _lastTime = now;

            // デバッグ停止や負荷スパイクでグリッドが一気に飛ばないよう制限
            return Math.Clamp(delta, 0f, 0.1f);
        }

        public float GetDeltaTime()
        {
            return CalculateDeltaTime(
                _stopwatch.Elapsed.TotalSeconds);
        }

        // ============================================================
        // Synthwave scene
        // ============================================================

        public List<XYPoint> GenerateXYBuffer(
            float[] fft,
            float time,
            float deltaTime,
            int sampleRate)
        {
            // --------------------------------------------------------
            // Audio analysis
            // --------------------------------------------------------

            float kick = IAudioVisualizer.GetBand(
                fft,
                50,
                100,
                sampleRate);

            float snare = IAudioVisualizer.GetBand(
                fft,
                1500,
                3000,
                sampleRate);

            float hat = IAudioVisualizer.GetBand(
                fft,
                6000,
                12000,
                sampleRate);

            kick = Math.Clamp(kick, 0f, 10f);
            snare = Math.Clamp(snare, 0f, 2f);
            hat = Math.Clamp(hat, 0f, 1.5f);

            // あらかじめ適度な容量を確保
            List<XYPoint> segments = new(128);

            // --------------------------------------------------------
            // Camera
            // --------------------------------------------------------

            // 現状はカメラ揺れOFF
            const float camX = 0f;
            const float camY = 0f;

            DrawSun(segments);

            // --------------------------------------------------------
            // Grid scrolling
            // --------------------------------------------------------

            float scrollSpeed = 1.0f + hat * 2.0f;

            _scroll += scrollSpeed * deltaTime;
            _scroll %= GridLines;

            DrawHorizontalGrid(
                segments,
                camX,
                camY);

            DrawVerticalGrid(
                segments,
                camX,
                camY);

            // --------------------------------------------------------
            // Car
            // --------------------------------------------------------

            DrawFerrari3D(
                segments,
                time,
                kick,
                snare,
                hat);

            return segments;
        }

        // ============================================================
        // Sun
        // ============================================================

        private static void DrawSun(List<XYPoint> segments)
        {
            for (int i = 0; i < SunLines; i++)
            {
                float y = HorizonY +
                    (SunRadius * 2f * i / (SunLines - 1));
                float offsetY = y - SunCenterY;
                float halfWidth = MathF.Sqrt(
                    MathF.Max(0f, SunRadius * SunRadius - offsetY * offsetY));

                segments.Add(new XYPoint(-halfWidth, y, 1.2f));
                segments.Add(new XYPoint(halfWidth, y, 1.2f));
            }
        }

        // ============================================================
        // Horizontal grid
        // ============================================================

        private void DrawHorizontalGrid(
            List<XYPoint> segments,
            float camX,
            float camY)
        {
            for (int i = 0; i < GridLines; i++)
            {
                float z = i - _scroll;

                if (z < 0f)
                    z += GridLines;

                float y =
                    -1f +
                    (z / GridLines) * 2f;

                Vector2 p1 = Project(
                    -1f - camX,
                    y - camY,
                    z);

                Vector2 p2 = Project(
                    1f - camX,
                    y - camY,
                    z);

                float intensity =
                    1f / Math.Clamp(z, 0.5f, 10f);

                segments.Add(
                    new XYPoint(
                        -1,
                        p1.Y,
                        intensity));

                segments.Add(
                    new XYPoint(
                        1,
                        p2.Y,
                        intensity));
            }
        }

        // ============================================================
        // Vertical grid
        // ============================================================

        private void DrawVerticalGrid(
            List<XYPoint> segments,
            float camX,
            float camY)
        {
            bool isVst = SelectedDevice == "V.st";

            float nearIntensity =
                isVst ? 0.5f : 2.0f;

            float farIntensity =
                isVst ? 0.5f : 0.1f;

            for (int i = 0; i <= VerticalLines; i++)
            {
                float x =
                    -2f +
                    (i / (float)VerticalLines) * 4f;

                Vector2 near = Project(
                    x - camX,
                    -1f - camY,
                    0f);

                Vector2 far = Project(
                    x - camX,
                    1f - camY,
                    GridLines);

                segments.Add(
                    new XYPoint(
                        near.X,
                        near.Y,
                        nearIntensity));

                segments.Add(
                    new XYPoint(
                        far.X,
                        far.Y,
                        farIntensity));
            }
        }

        // ============================================================
        // Grid projection
        // ============================================================

        private static Vector2 Project(
            float x,
            float y,
            float z)
        {
            float d =
                ProjectionDistance /
                (z + ProjectionDistance);

            return new Vector2(
                x * d,
                y * d);
        }

        // ============================================================
        // Car geometry
        // ============================================================

        private static readonly Vector3[] Car3D =
        {
            // Front
            new(-0.45f, -0.70f, 0.60f), // 0
            new( 0.45f, -0.70f, 0.60f), // 1
            new(-0.50f, -0.90f, 0.60f), // 2
            new( 0.50f, -0.90f, 0.60f), // 3

            // Roof
            new(-0.35f, -0.50f, 0.30f), // 4
            new( 0.35f, -0.50f, 0.30f), // 5
            new(-0.35f, -0.50f, 0.10f), // 6
            new( 0.35f, -0.50f, 0.10f), // 7

            // Rear
            new(-0.45f, -0.70f, 0.00f), // 8
            new( 0.45f, -0.70f, 0.00f), // 9
            new(-0.50f, -0.90f, 0.00f), // 10
            new( 0.50f, -0.90f, 0.00f), // 11

            // Left tail light
            new(-0.40f, -0.80f, 0f), // 12
            new(-0.30f, -0.80f, 0f), // 13
            new(-0.40f, -0.75f, 0f), // 14
            new(-0.30f, -0.75f, 0f), // 15

            // Right tail light
            new( 0.30f, -0.75f, 0f), // 16
            new( 0.40f, -0.75f, 0f), // 17
            new( 0.40f, -0.80f, 0f), // 18
            new( 0.30f, -0.80f, 0f), // 19

            // Left tire
            new(-0.50f, -1.00f, 0f), // 20
            new(-0.25f, -1.00f, 0f), // 21
            new(-0.50f, -0.90f, 0f), // 22
            new(-0.25f, -0.90f, 0f), // 23

            // Right tire
            new( 0.25f, -0.90f, 0f), // 24
            new( 0.50f, -0.90f, 0f), // 25
            new( 0.50f, -1.00f, 0f), // 26
            new( 0.25f, -1.00f, 0f), // 27
        };

        private static readonly (int A, int B)[] CarEdges =
        {
            // Front
            (0, 1),
            (0, 2),
            (1, 3),

            // Roof
            (4, 5),
            (4, 6),
            (5, 7),
            (6, 7),

            // Front -> roof
            (0, 4),
            (1, 5),

            // Front -> rear
            (0, 8),
            (1, 9),
            (2, 10),
            (3, 11),

            // Roof -> rear
            (6, 8),
            (7, 9),

            // Rear
            (8, 9),
            (8, 10),
            (9, 11),
            (10, 11),

            // Tail lights
            (12, 13),
            (13, 15),
            (15, 14),
            (14, 12),

            (16, 17),
            (17, 18),
            (18, 19),
            (19, 16),

            // Tires
            (21, 23),
            (20, 22),
            (20, 21),

            (25, 26),
            (26, 27),
            (27, 24),
        };

        // ============================================================
        // Car drawing
        // ============================================================

        private void DrawFerrari3D(
            List<XYPoint> segments,
            float time,
            float kick,
            float snare,
            float hat)
        {
            float yaw = 0f;

            float pitch =
                MathF.Sin(
                    time * 0.7f +
                    MathF.PI * snare / 8f) *
                0.2f -
                0.2f;

            float roll =
                MathF.Sin(time * 0.7f) *
                0.05f;

            float slide =
                MathF.Sin(time * 0.7f + 0.1f) *
                0.4f;

            // Kickによる上下動
            float bounce =
                kick * 0.005f;

            Span<Vector2> points =
                stackalloc Vector2[Car3D.Length];

            for (int i = 0; i < Car3D.Length; i++)
            {
                Vector3 p = Car3D[i];

                p.X += slide;
                p.Y += bounce;

                p = Rotate(
                    p,
                    yaw,
                    pitch,
                    roll);

                points[i] = Project3D(p);
            }

            DrawCarEdges(
                segments,
                points);

            UpdateTailHistory(
                points[TailLeftIndex],
                points[TailRightIndex]);

            DrawTailTrails(
                segments,
                hat);
        }

        private static void DrawCarEdges(
            List<XYPoint> segments,
            ReadOnlySpan<Vector2> points)
        {
            const float intensity = 2.0f;

            foreach ((int a, int b) in CarEdges)
            {
                Vector2 p1 = points[a];
                Vector2 p2 = points[b];

                segments.Add(
                    new XYPoint(
                        p1.X,
                        p1.Y,
                        intensity));

                segments.Add(
                    new XYPoint(
                        p2.X,
                        p2.Y,
                        intensity));
            }
        }

        // ============================================================
        // Tail trail
        // ============================================================

        private void UpdateTailHistory(
            Vector2 left,
            Vector2 right)
        {
            _tailHistory.Add((left, right));

            if (_tailHistory.Count > TailMax)
            {
                _tailHistory.RemoveAt(0);
            }
        }

        private void DrawTailTrails(
            List<XYPoint> segments,
            float hat)
        {
            int count = _tailHistory.Count;

            if (count < 2)
                return;

            for (int i = 0; i < count - 1; i++)
            {
                float t1 =
                    i / (float)TailMax;

                float t2 =
                    (i + 1) / (float)TailMax;

                float fade1 =
                    MathF.Pow(t1, 2.2f);

                float fade2 =
                    MathF.Pow(t2, 2.2f);

                var current =
                    _tailHistory[i];

                var next =
                    _tailHistory[i + 1];

                float intensity1 =
                    0.1f + fade1 + hat;

                float intensity2 =
                    0.1f + fade2 + hat;

                // Left trail
                segments.Add(
                    new XYPoint(
                        current.Left.X + 0.05f,
                        current.Left.Y - 0.025f,
                        intensity1));

                segments.Add(
                    new XYPoint(
                        next.Left.X - 0.05f,
                        next.Left.Y - 0.025f,
                        intensity2));

                // Right trail
                segments.Add(
                    new XYPoint(
                        current.Right.X + 0.05f,
                        current.Right.Y - 0.025f,
                        intensity1));

                segments.Add(
                    new XYPoint(
                        next.Right.X - 0.05f,
                        next.Right.Y - 0.025f,
                        intensity2));
            }
        }

        // ============================================================
        // 3D math
        // ============================================================

        private static Vector3 Rotate(
            Vector3 p,
            float yaw,
            float pitch,
            float roll)
        {
            // Yaw
            float cy = MathF.Cos(yaw);
            float sy = MathF.Sin(yaw);

            p = new Vector3(
                p.X * cy - p.Z * sy,
                p.Y,
                p.X * sy + p.Z * cy);

            // Pitch
            float cp = MathF.Cos(pitch);
            float sp = MathF.Sin(pitch);

            p = new Vector3(
                p.X,
                p.Y * cp - p.Z * sp,
                p.Y * sp + p.Z * cp);

            // Roll
            float cr = MathF.Cos(roll);
            float sr = MathF.Sin(roll);

            p = new Vector3(
                p.X * cr - p.Y * sr,
                p.X * sr + p.Y * cr,
                p.Z);

            return p;
        }

        private static Vector2 Project3D(Vector3 p)
        {
            float denominator = p.Z + ProjectionDistance;

            // 万一0近辺になったときの発散防止
            if (MathF.Abs(denominator) < 0.001f)
            {
                denominator =
                    MathF.CopySign(0.001f, denominator);
            }

            float d =
                ProjectionDistance /
                denominator;

            return new Vector2(
                p.X * d,
                p.Y * d);
        }
    }
}