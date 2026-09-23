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
    internal sealed record StarCruisingFrameDiagnostics(
        long FrameNumber,
        int StarSegmentCount,
        int ApproachingBodySegmentCount,
        int XWingSegmentCount,
        int TotalSegmentCount,
        long ManagedHeapBytes,
        long WorkingSetBytes,
        double FrameRenderMilliseconds,
        double HiddenLineRenderMilliseconds);

    internal sealed class StarCruising : IAudioVisualizer
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

        private const int StarCount = 64;
        private const float StarNearZ = 0.6f;
        private const float StarFarZ = 10f;
        private const float StarFieldRadius = 5f;

        private const int MaxCelestialBodies = 2;
        private const float BodySpawnZ = 96f;
        private const float BodyDespawnZ = 20f;
        private const float BodyFadeInDistance = 20f;
        private const float WarpFadeDuration = 0.75f;
        private const int DiagnosticLogIntervalFrames = 60;

        // ============================================================
        // Audio / Timing state
        // ============================================================

        private float _prevX;
        private float _prevY;

        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private double _lastTime;

        private float _scroll;

        private readonly Random _random = new();
        private readonly Vector3[] _stars = new Vector3[StarCount];
        private bool _starsInitialized;

        private readonly HiddenLineSilhouetteSceneRenderer _bodyRenderer;
        private readonly SceneMeshInstance[] _bodyModels;
        private readonly SceneMeshInstance _xWing;
        private readonly DisplayDevice _bodyDisplay = new();
        private readonly List<ApproachingBody> _approachingBodies = new(MaxCelestialBodies);
        private float _nextBodySpawnTime = 3f;
        private bool _isHyperspace;
        private float _warpStartTime;
        private float _warpEndTime;
        private float _warpDuration;
        private float _nextWarpTime = 18f;
        private float _xWingWarpStartX;
        private float _xWingWarpY;
        private float _nextXWingJitterTime;
        private Vector2 _xWingJitterOffset;
        private long _frameNumber;

        internal StarCruisingFrameDiagnostics? LastFrameDiagnostics { get; private set; }

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

        public string VisualizerName => "Star Cruising";

        public string? SelectedDevice { get; set; }

        public StarCruising()
        {
            _bodyModels = new[]
            {
                CreateBodyModel(@"Assets\Solar System - Sun.stl", 1.4f),
                CreateBodyModel(@"Assets\Solar System - Earth.stl", 0.8f),
                CreateBodyModel(@"Assets\Solar System - Jupiter.stl", 1.1f),
                CreateBodyModel(@"Assets\Solar System - Saturn.stl", 1.0f)
            };

            _bodyRenderer = new HiddenLineSilhouetteSceneRenderer
            {
                FocalLength = 1.5f,
                ViewportScale = 1.0f,
                NearZ = 0.01f,
                Epsilon = 1e-5f,
                AutoFitToCrtRange = false,
                GridCols = 24,
                GridRows = 24,
                SceneScale = 1f,
                SceneTranslation = Vector3.Zero,
                SceneRotationCenterMode = RotationCenterMode.Origin
            };

            foreach (SceneMeshInstance model in _bodyModels)
            {
                model.Visible = false;
                _bodyRenderer.AddInstance(model);
            }

            _xWing = CreateBodyModel(@"Assets\x-wing.stl", 2.4f, minimumEdgeLength: 0.035f);
            _xWing.RotationYDeg = 90f;
            _xWing.RotationXDeg = 90f;
            _bodyRenderer.AddInstance(_xWing);
        }

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

            long frameStartTimestamp = Stopwatch.GetTimestamp();
            List<XYPoint> segments = new(StarCount * 2);
            UpdateHyperspace(time);
            DrawStarField(segments, deltaTime, kick, hat);
            int starSegmentCount = segments.Count / 2;
            UpdateApproachingBodies(time, deltaTime, kick, hat);
            UpdateXWing(time);
            HiddenLineFrameDiagnostics hiddenLineDiagnostics = DrawApproachingBodies(segments);
            RecordFrameDiagnostics(starSegmentCount, segments.Count / 2, frameStartTimestamp, hiddenLineDiagnostics);

            return segments;
        }

        private void UpdateXWing(float time)
        {
            if (_isHyperspace)
            {
                float transition = Math.Clamp((time - _warpStartTime) / WarpFadeDuration, 0f, 1f);

                if (time >= _nextXWingJitterTime)
                {
                    _xWingJitterOffset = new Vector2(
                        _random.NextSingle() * 0.16f - 0.08f,
                        _random.NextSingle() * 0.12f - 0.06f);
                    _nextXWingJitterTime = time + 0.08f;
                }

                _xWing.Translation = new Vector3(
                    _xWingWarpStartX * (1f - transition) + _xWingJitterOffset.X * transition,
                    _xWingWarpY + _xWingJitterOffset.Y * transition,
                    8f);
                _xWing.RotationZDeg = _xWingJitterOffset.X * 20f;
            }
            else
            {
                _xWing.Translation = new Vector3(
                    MathF.Sin(time * 0.7f) * 1.8f,
                    -1.8f,
                    8f);
                _xWing.RotationZDeg = MathF.Sin(time * 0.7f) * 4f;
            }

            _xWing.RotationYDeg = -90f;
            _xWing.Visible = true;
        }

        private void UpdateHyperspace(float time)
        {
            if (!_isHyperspace && time >= _nextWarpTime)
            {
                _isHyperspace = true;
                _warpStartTime = time;
                _warpDuration = 5f + _random.NextSingle() * 5f;
                _warpEndTime = time + _warpDuration;
                _xWingWarpStartX = MathF.Sin(time * 0.7f) * 1.8f;
                _xWingWarpY = -1.8f;
                _nextXWingJitterTime = time + WarpFadeDuration;
                _xWingJitterOffset = Vector2.Zero;
                _approachingBodies.Clear();
                TrySpawnApproachingBody(isWarpDestination: true);
                return;
            }

            if (_isHyperspace && time >= _warpEndTime)
            {
                _isHyperspace = false;
                _nextWarpTime = time + 16f + _random.NextSingle() * 18f;

                for (int i = 0; i < _stars.Length; i++)
                    ResetStar(i, StarNearZ + _random.NextSingle() * (StarFarZ - StarNearZ));
            }
        }

        private float GetWarpFade(float time)
        {
            if (!_isHyperspace)
                return 1f;

            float fadeIn = Math.Clamp((time - _warpStartTime) / WarpFadeDuration, 0f, 1f);
            float fadeOut = Math.Clamp((_warpEndTime - time) / WarpFadeDuration, 0f, 1f);
            return MathF.Min(fadeIn, fadeOut);
        }

        private void UpdateApproachingBodies(float time, float deltaTime, float kick, float hat)
        {
            for (int i = _approachingBodies.Count - 1; i >= 0; i--)
            {
                ApproachingBody body = _approachingBodies[i];
                body.Position -= Vector3.UnitZ * (body.Speed + kick * 0.08f + hat * 0.12f) * deltaTime;
                body.Rotation = (body.Rotation + body.SpinSpeed * deltaTime) % 360f;

                Vector2 screenPosition = new(body.Position.X / body.Position.Z, body.Position.Y / body.Position.Z);
                if (body.Position.Z < BodyDespawnZ || !IsOnScreen(screenPosition))
                {
                    _approachingBodies.RemoveAt(i);
                    continue;
                }

                _approachingBodies[i] = body;
            }

            if (!_isHyperspace && time >= _nextBodySpawnTime && _approachingBodies.Count < MaxCelestialBodies)
            {
                TrySpawnApproachingBody();
                _nextBodySpawnTime = time + 4f + _random.NextSingle() * 5f;
            }
        }

        private void TrySpawnApproachingBody(bool isWarpDestination = false)
        {
            const float centerExclusionRadius = 0.08f;
            const float minimumBodyDistance = 0.28f;

            Span<int> availableModels = stackalloc int[_bodyModels.Length];
            int availableCount = 0;

            for (int modelIndex = 0; modelIndex < _bodyModels.Length; modelIndex++)
            {
                bool isInUse = false;
                foreach (ApproachingBody body in _approachingBodies)
                {
                    if (body.ModelIndex == modelIndex)
                    {
                        isInUse = true;
                        break;
                    }
                }

                if (!isInUse)
                    availableModels[availableCount++] = modelIndex;
            }

            if (availableCount == 0)
                return;

            for (int attempt = 0; attempt < 12; attempt++)
            {
                Vector2 screenOffset = isWarpDestination
                    ? CreateWarpDestinationOffset()
                    : new Vector2(
                        _random.NextSingle() * 1.1f - 0.55f,
                        _random.NextSingle() * 0.9f - 0.45f);

                if (screenOffset.Length() < centerExclusionRadius)
                    continue;

                bool intersectsExistingBody = false;
                foreach (ApproachingBody existing in _approachingBodies)
                {
                    Vector2 existingOffset = new(
                        existing.Position.X / existing.Position.Z,
                        existing.Position.Y / existing.Position.Z);

                    if (Vector2.Distance(screenOffset, existingOffset) < minimumBodyDistance)
                    {
                        intersectsExistingBody = true;
                        break;
                    }
                }

                if (intersectsExistingBody)
                    continue;

                _approachingBodies.Add(new ApproachingBody
                {
                    ModelIndex = availableModels[_random.Next(availableCount)],
                    Position = new Vector3(screenOffset.X * BodySpawnZ, screenOffset.Y * BodySpawnZ, BodySpawnZ),
                    Scale = (1.2f + _random.NextSingle() * 1.2f) * 4f,
                    Speed = isWarpDestination
                        ? (BodySpawnZ - BodyDespawnZ) / MathF.Max(_warpDuration * 0.9f, 1f)
                        : 2.2f + _random.NextSingle() * 1.6f,
                    SpinSpeed = 20f + _random.NextSingle() * 35f,
                    Rotation = _random.NextSingle() * 360f
                });
                return;
            }
        }

        private Vector2 CreateWarpDestinationOffset()
        {
            float angle = _random.NextSingle() * MathF.Tau;
            float distance = 0.1f + _random.NextSingle() * 0.22f;
            return new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * distance;
        }

        private HiddenLineFrameDiagnostics DrawApproachingBodies(List<XYPoint> segments)
        {
            foreach (SceneMeshInstance model in _bodyModels)
                model.Visible = false;

            if (!_isHyperspace)
            {
                foreach (ApproachingBody body in _approachingBodies)
                {
                    SceneMeshInstance model = _bodyModels[body.ModelIndex];
                    model.Visible = true;
                    model.Translation = body.Position;
                    model.Scale = body.Scale;
                    model.RotationXDeg = body.Rotation * 0.35f;
                    model.RotationYDeg = body.Rotation;
                    model.RotationZDeg = body.Rotation * 0.2f;
                }
            }

            if (_approachingBodies.Count == 0 && !_xWing.Visible)
                return default;

            long renderStartTimestamp = Stopwatch.GetTimestamp();
            _bodyRenderer.Render(_bodyDisplay);
            double renderMilliseconds = Stopwatch.GetElapsedTime(renderStartTimestamp).TotalMilliseconds;

            IReadOnlyList<int> instanceLineCounts = _bodyRenderer.LastFrameInstanceLineCounts;
            int approachingBodySegmentCount = 0;
            for (int i = 0; i < _bodyModels.Length && i < instanceLineCounts.Count; i++)
                approachingBodySegmentCount += instanceLineCounts[i];

            int xWingSegmentCount = instanceLineCounts.Count > _bodyModels.Length
                ? instanceLineCounts[_bodyModels.Length]
                : 0;

            float bodyFadeIn = 1f;
            if (!_isHyperspace)
            {
                foreach (ApproachingBody body in _approachingBodies)
                {
                    bodyFadeIn = MathF.Min(bodyFadeIn, Math.Clamp((BodySpawnZ - body.Position.Z) / BodyFadeInDistance, 0f, 1f));
                }
            }

            foreach (XYPoint point in _bodyDisplay.Points)
                segments.Add(new XYPoint(point.X, point.Y, 0.9f * bodyFadeIn));

            return new HiddenLineFrameDiagnostics(
                approachingBodySegmentCount,
                xWingSegmentCount,
                renderMilliseconds);
        }

        private void RecordFrameDiagnostics(
            int starSegmentCount,
            int totalSegmentCount,
            long frameStartTimestamp,
            HiddenLineFrameDiagnostics hiddenLineDiagnostics)
        {
            long frameNumber = ++_frameNumber;
            long managedHeapBytes = GC.GetTotalMemory(forceFullCollection: false);
            using Process process = Process.GetCurrentProcess();
            long workingSetBytes = process.WorkingSet64;
            double frameRenderMilliseconds = Stopwatch.GetElapsedTime(frameStartTimestamp).TotalMilliseconds;

            LastFrameDiagnostics = new StarCruisingFrameDiagnostics(
                frameNumber,
                starSegmentCount,
                hiddenLineDiagnostics.ApproachingBodySegmentCount,
                hiddenLineDiagnostics.XWingSegmentCount,
                totalSegmentCount,
                managedHeapBytes,
                workingSetBytes,
                frameRenderMilliseconds,
                hiddenLineDiagnostics.RenderMilliseconds);

            if (frameNumber % DiagnosticLogIntervalFrames == 0)
            {
                Debug.WriteLine(
                    $"[StarCruising] frame={frameNumber}, segments(total={totalSegmentCount}, stars={starSegmentCount}, bodies={hiddenLineDiagnostics.ApproachingBodySegmentCount}, xWing={hiddenLineDiagnostics.XWingSegmentCount}), " +
                    $"memory(managed={managedHeapBytes:N0} B, workingSet={workingSetBytes:N0} B), " +
                    $"render={frameRenderMilliseconds:F2} ms, hiddenLine={hiddenLineDiagnostics.RenderMilliseconds:F2} ms");
            }
        }

        private readonly record struct HiddenLineFrameDiagnostics(
            int ApproachingBodySegmentCount,
            int XWingSegmentCount,
            double RenderMilliseconds);

        private void DrawStarField(
            List<XYPoint> segments,
            float deltaTime,
            float kick,
            float hat)
        {
            EnsureStars();

            float speed = 2.4f + kick * 0.12f + hat * 0.2f;
            if (_isHyperspace)
                speed *= 18f;
            float fade = GetWarpFade((float)_stopwatch.Elapsed.TotalSeconds);

            for (int i = 0; i < _stars.Length; i++)
            {
                Vector3 previous = _stars[i];
                Vector3 current = previous;
                current.Z -= speed * deltaTime;

                if (current.Z < StarNearZ)
                {
                    float resetZ = _isHyperspace
                        ? StarNearZ + _random.NextSingle() * (StarFarZ - StarNearZ)
                        : StarFarZ;
                    ResetStar(i, resetZ);
                    continue;
                }

                _stars[i] = current;

                Vector2 start = ProjectStar(previous);
                Vector2 end = ProjectStar(current);

                if (!IsOnScreen(start) && !IsOnScreen(end))
                    continue;

                float intensity = Math.Clamp(0.15f + (1f - current.Z / StarFarZ) * 0.85f, 0.15f, 1f);
                float streakIntensity = (_isHyperspace ? intensity * 0.5f : intensity * 0.35f) * fade;
                segments.Add(new XYPoint(start.X, start.Y, streakIntensity));
                segments.Add(new XYPoint(end.X, end.Y, intensity * fade));
            }
        }

        private void EnsureStars()
        {
            if (_starsInitialized)
                return;

            for (int i = 0; i < _stars.Length; i++)
                ResetStar(i, StarNearZ + _random.NextSingle() * (StarFarZ - StarNearZ));

            _starsInitialized = true;
        }

        private void ResetStar(int index, float z)
        {
            _stars[index] = new Vector3(
                (_random.NextSingle() * 2f - 1f) * StarFieldRadius,
                (_random.NextSingle() * 2f - 1f) * StarFieldRadius,
                z);
        }

        private static Vector2 ProjectStar(Vector3 star) =>
            new(star.X / star.Z, star.Y / star.Z);

        private static bool IsOnScreen(Vector2 point) =>
            point.X is >= -1.2f and <= 1.2f &&
            point.Y is >= -1.2f and <= 1.2f;

        private static SceneMeshInstance CreateBodyModel(string path, float baseScale, float minimumEdgeLength = 0f)
        {
            StlModel model = StlLoader.Load(path);
            model.NormalizeToUnitCube();
            IndexedMesh mesh = MeshBuilder.BuildIndexedMesh(model, vertexMergeEpsilon: 5e-5f);

            if (minimumEdgeLength <= 0f)
            {
                return new SceneMeshInstance(mesh)
                {
                    Scale = baseScale
                };
            }

            float minimumEdgeLengthSquared = minimumEdgeLength * minimumEdgeLength;
            List<MeshEdge> renderEdges = new();

            foreach (MeshEdge edge in mesh.Edges)
            {
                Vector3 edgeVector = mesh.Vertices[edge.V1] - mesh.Vertices[edge.V0];
                if (edgeVector.LengthSquared() >= minimumEdgeLengthSquared)
                    renderEdges.Add(edge);
            }

            return new SceneMeshInstance(mesh, renderEdges)
            {
                Scale = baseScale
            };
        }

        private sealed class ApproachingBody
        {
            public required int ModelIndex { get; init; }
            public required Vector3 Position { get; set; }
            public required float Scale { get; init; }
            public required float Speed { get; init; }
            public required float SpinSpeed { get; init; }
            public float Rotation { get; set; }
        }

        private sealed class DisplayDevice : IVectorDisplayDevice
        {
            public List<XYPoint> Points { get; } = new();

            public void BeginFrame() => Points.Clear();

            public void DrawLine(float x0, float y0, float x1, float y1)
            {
                Points.Add(new XYPoint(x0, y0));
                Points.Add(new XYPoint(x1, y1));
            }

            public void EndFrame()
            {
            }
        }
    }
}