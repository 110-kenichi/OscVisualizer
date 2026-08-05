using DynamicData.Kernel;
using MathNet.Numerics;
using MathNet.Numerics.IntegralTransforms;
using NAudio.CoreAudioApi;
using NAudio.Gui;
using NAudio.Wave;
using OscVisualizer.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;

namespace OscVisualizer.Services
{
    internal class MatsumotoMeter : IAudioVisualizer
    {
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

        private readonly Stopwatch _sw = Stopwatch.StartNew();
        private readonly Random _random = new();
        private float[] _waveform = Array.Empty<float>();
        private int _mode = -1;
        private double _nextModeChange;
        private float _level;
        private float _peak;
        private float _needleLevel;
        private float _needleHatLevel;
        private int _transitionFromMode = -1;
        private int _transitionToMode = -1;
        private double _transitionStart;
        private const double MeterTransitionDuration = 2.4;
        private readonly List<SonarEcho> _sonarEchoes = new();
        private double _sonarSpawnCooldown;

        private sealed class SonarEcho
        {
            public double X { get; init; }
            public double Y { get; init; }
            public double Size { get; init; }
            public int Shape { get; init; }
            public double Intensity { get; set; } = 2.0;
        }

        public string VisualizerName
        {
            get => "Matsumoto Meter";
        }

        public List<XYPoint> ProcessAudio(WasapiCapture capture, WaveInEventArgs e)
        {
            var fmt = capture.WaveFormat;
            int channels = fmt.Channels;
            int inputSampleRate = fmt.SampleRate;

            float[] wav = IAudioVisualizer.ConvertToWav1ch(capture, e);

            //ハイパスフィルタ
            prevX = 0;
            prevY = 0;
            for (int i = 0; i < wav.Length; i++)
                wav[i] = HighPass(wav[i]);
            _waveform = wav;

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

            float t = (float)_sw.Elapsed.TotalSeconds;


            return GenerateXYBuffer(spectrum, t, GetDeltaTime(), inputSampleRate);
        }

        private double _lastTime = 0;

        public float GetDeltaTime()
        {
            double now = _sw.Elapsed.TotalSeconds;
            float delta = (float)(now - _lastTime);
            _lastTime = now;

            return delta;
        }

        public List<XYPoint> GenerateXYBuffer(float[] fft, float time, float deltaTime, int sampleRate)
        {
            float kick = IAudioVisualizer.GetBand(fft, 50, 100, sampleRate);
            float snare = IAudioVisualizer.GetBand(fft, 1500, 3000, sampleRate);
            float hat = IAudioVisualizer.GetBand(fft, 6000, 12000, sampleRate);

            kick = MathF.Min(kick, 20f);
            snare = MathF.Min(snare, 2f);
            hat = MathF.Min(hat, 1.5f);

            List<XYPoint> seg = new();
            float rms = 0f;
            float peak = 0f;
            for (int i = 0; i < _waveform.Length; i++)
            {
                float s = _waveform[i];
                rms += s * s;
                float a = MathF.Abs(s);
                if (a > peak)
                    peak = a;
            }

            if (_waveform.Length > 0)
                rms = MathF.Sqrt(rms / _waveform.Length);

            _level += (MathF.Min(1f, rms * 4f) - _level) * 0.18f;
            _peak = MathF.Max(peak, _peak * MathF.Pow(0.2f, MathF.Max(0.001f, deltaTime)));
            float kickTarget = Math.Clamp(kick / 8f, 0f, 1f);
            float hatTarget = Math.Clamp(hat / 1.5f, 0f, 1f);
            float needleTarget = Math.Clamp(_level + kickTarget, 0f, 1f);
            float needleRate = needleTarget >= _needleLevel ? 8f : 3.5f;
            float needleSmoothing = 1f - MathF.Exp(-needleRate * Math.Clamp(deltaTime, 0.001f, 0.1f));
            _needleLevel += (needleTarget - _needleLevel) * needleSmoothing;
            _needleHatLevel += (hatTarget - _needleHatLevel) * needleSmoothing;

            if (time >= _nextModeChange)
            {
                int previous = _mode;
                do _mode = _random.Next(4); while (_mode == previous);
                _transitionFromMode = previous < 0 ? _mode : previous;
                _transitionToMode = _mode;
                _transitionStart = time;
                _nextModeChange = time + 5f + (float)_random.NextDouble() * 7f;
            }

            List<XYPoint> currentShape = GenerateMeterShape(_mode, time, rms, kick, snare, hat, deltaTime, true);
            if (_transitionFromMode >= 0 && _transitionToMode >= 0 && _transitionFromMode != _transitionToMode)
            {
                double progress = Math.Clamp((time - _transitionStart) / MeterTransitionDuration, 0, 1);
                if (progress < 1)
                {
                    List<XYPoint> previousShape = GenerateMeterShape(_transitionFromMode, time, rms, kick, snare, hat, deltaTime, false);
                    seg.AddRange(ProjectCylinderTransition(previousShape, currentShape, progress));
                }
                else
                {
                    _transitionFromMode = _transitionToMode;
                    seg.AddRange(currentShape);
                }
            }
            else
            {
                seg.AddRange(currentShape);
            }

            return seg;
        }

        private List<XYPoint> GenerateMeterShape(int mode, double time, float rms, float kick, float snare, float hat, float deltaTime, bool updateState)
        {
            List<XYPoint> shape = new();
            switch (mode)
            {
                case 0: DrawVuMeter(shape, _needleLevel, _needleHatLevel); break;
                case 1: DrawWaveformMeter(shape); break;
                case 2: DrawSonar(shape, time, rms, updateState ? deltaTime : 0); break;
                default: DrawMysteryMeter(shape, time, kick, snare, hat); break;
            }

            return shape;
        }

        private static List<XYPoint> ProjectCylinderTransition(IReadOnlyList<XYPoint> previous, IReadOnlyList<XYPoint> next, double progress)
        {
            double eased = progress * progress * (3 - 2 * progress);
            List<XYPoint> result = new(previous.Count + next.Count);
            AddCylinderShape(result, previous, -Math.PI * 0.5 * eased);
            AddCylinderShape(result, next, Math.PI * 0.5 * (1.0 - eased));
            return result;
        }

        private static void AddCylinderShape(List<XYPoint> result, IReadOnlyList<XYPoint> shape, double angle)
        {
            for (int i = 0; i + 1 < shape.Count; i += 2)
            {
                XYPoint start = ProjectCylinderPoint(shape[i], angle);
                XYPoint end = ProjectCylinderPoint(shape[i + 1], angle);
                double visibility = Math.Cos(angle);
                if (visibility > 0.01)
                    AddLine(result, start.X, start.Y, end.X, end.Y, Math.Max(start.Intensity, end.Intensity));
            }
        }

        private static XYPoint ProjectCylinderPoint(XYPoint point, double angle)
        {
            double projectedX = point.X * Math.Cos(angle);
            return new XYPoint(projectedX, point.Y, point.Intensity * Math.Max(0.0, Math.Cos(angle)));
        }

        private static void AddDoubleFrame(List<XYPoint> points, double cx, double cy, double outerRadius, double innerRadius, int count)
        {
            AddCircle(points, cx, cy, outerRadius, count, 0.85);
            AddCircle(points, cx, cy, innerRadius, count, 0.65);

            int rimSpacing = Math.Max(1, count / 16);
            for (int i = 0; i < count; i += rimSpacing)
            {
                double angle = i * Math.PI * 2 / count;
                AddLine(points,
                    cx + Math.Cos(angle) * innerRadius,
                    cy + Math.Sin(angle) * innerRadius,
                    cx + Math.Cos(angle) * outerRadius,
                    cy + Math.Sin(angle) * outerRadius,
                    0.7);
            }
        }

        private static void AddLine(List<XYPoint> points, double x0, double y0, double x1, double y1, double intensity = 0.8)
        {
            points.Add(new XYPoint(x0, y0, 0.25));
            points.Add(new XYPoint(x1, y1, intensity));
        }

        private static void AddCircle(List<XYPoint> points, double cx, double cy, double radius, int count, double intensity = 0.75)
        {
            for (int i = 0; i < count; i++)
            {
                double a0 = i * Math.PI * 2 / count;
                double a1 = (i + 1) * Math.PI * 2 / count;
                AddLine(points, cx + radius * Math.Cos(a0), cy + radius * Math.Sin(a0),
                    cx + radius * Math.Cos(a1), cy + radius * Math.Sin(a1), intensity);
            }
        }

        private static void AddTick(List<XYPoint> points, double cx, double cy, double angle, double inner, double outer)
        {
            AddLine(points, cx + Math.Cos(angle) * inner, cy + Math.Sin(angle) * inner,
                cx + Math.Cos(angle) * outer, cy + Math.Sin(angle) * outer);
        }

        private static void DrawVuMeter(List<XYPoint> points, float kickLevel, float hatLevel)
        {
            List<XYPoint> inverted = new();
            DrawVuMeterGeometry(inverted, kickLevel, hatLevel);
            foreach (XYPoint point in inverted)
            {
                point.Y = -point.Y;
                points.Add(point);
            }
        }

        private static void DrawVuMeterGeometry(List<XYPoint> points, float kickLevel, float hatLevel)
        {
            AddDoubleFrame(points, 0, 0, 1.0, 0.87, 64);
            for (int i = 0; i <= 12; i++)
            {
                double angle = Math.PI * 0.75 + (12 - i) * Math.PI * 1.5 / 12;
                AddTick(points, 0, 0, angle, 0.76, i % 3 == 0 ? 0.62 : 0.69);
            }

            double kickNeedle = Math.PI * 0.75 + Math.Clamp(kickLevel, 0f, 1f) * Math.PI * 1.5;
            double hatNeedle = Math.PI * 0.75 + Math.Clamp(hatLevel, 0f, 1f) * Math.PI * 1.5;
            AddLine(points, 0, 0, Math.Cos(kickNeedle) * 0.70, Math.Sin(kickNeedle) * 0.70, 1.0);
            AddLine(points, 0, 0, Math.Cos(hatNeedle) * 0.64, Math.Sin(hatNeedle) * 0.64, 0.7);
            AddCircle(points, 0, 0, 0.075, 10, 1.0);
        }

        private void DrawWaveformMeter(List<XYPoint> points)
        {
            AddDoubleFrame(points, 0, 0, 1.0, 0.87, 64);
            AddLine(points, -0.80, 0, 0.80, 0, 0.3);
            for (int i = 0; i <= 10; i++)
            {
                double halfLength = i % 5 == 0 ? 0.07 : 0.042;
                AddTick(points, -0.72 + i * 0.144, 0, Math.PI / 2, -halfLength, halfLength);
            }

            int count = 96;
            for (int i = 0; i < count - 1; i++)
            {
                double t0 = i / (double)(count - 1);
                double t1 = (i + 1) / (double)(count - 1);
                double y0 = SampleWave(t0) * 1.58;
                double y1 = SampleWave(t1) * 1.58;
                AddLine(points, -0.82 + t0 * 1.64, y0, -0.82 + t1 * 1.64, y1, 0.9);
            }
        }

        private double SampleWave(double position)
        {
            if (_waveform.Length == 0)
                return 0;

            int index = Math.Clamp((int)(position * (_waveform.Length - 1)), 0, _waveform.Length - 1);
            return Math.Clamp(_waveform[index] * 3.2f, -1f, 1f);
        }

        private void DrawSonar(List<XYPoint> points, double time, float rms, float deltaTime)
        {
            AddDoubleFrame(points, 0, 0, 1.0, 0.87, 64);
            AddCircle(points, 0, 0, 0.61, 48, 0.38);
            AddCircle(points, 0, 0, 0.30, 32, 0.38);

            double sweep = time * 2.4;
            double halfWidth = 0.46;
            double radius = 0.86;
            int slices = 24;

            for (int i = 0; i < slices; i++)
            {
                double t0 = i / (double)slices;
                double t1 = (i + 1) / (double)slices;
                double a0 = sweep - halfWidth + t0 * halfWidth * (2d + (slices - i) / 5d);
                double intensity = 2d * i / slices;
                AddLine(points, 0, 0, Math.Cos(a0) * radius, Math.Sin(a0) * radius, intensity);
            }

            if (deltaTime > 0)
                UpdateSonarEchoes(sweep, halfWidth, rms, deltaTime);
            foreach (SonarEcho echo in _sonarEchoes)
            {
                DrawSonarEcho(points, echo);
            }
        }

        private void UpdateSonarEchoes(double sweep, double halfWidth, float rms, float deltaTime)
        {
            double dt = Math.Clamp(deltaTime, 0.001f, 0.1f);
            _sonarSpawnCooldown = Math.Max(0, _sonarSpawnCooldown - dt);

            for (int i = _sonarEchoes.Count - 1; i >= 0; i--)
            {
                _sonarEchoes[i].Intensity -= dt * 3.0;
                if (_sonarEchoes[i].Intensity <= 0)
                    _sonarEchoes.RemoveAt(i);
            }

            if (rms < 0.004f || _sonarSpawnCooldown > 0 || _sonarEchoes.Count >= 8)
                return;

            _sonarSpawnCooldown = 0.06 + _random.NextDouble() * 0.12;
            // エコーは扇形の最初のレーダー線が通過した位置で検出する。
            // sweep - halfWidth は扇形の最後の線側になるため、先端側を使用する。
            double angle = sweep + halfWidth;
            double radius = 0.18 + _random.NextDouble() * 0.48;
            _sonarEchoes.Add(new SonarEcho
            {
                X = Math.Cos(angle) * radius,
                Y = Math.Sin(angle) * radius,
                Size = 0.025 + _random.NextDouble() * 0.035,
                Shape = _random.Next(3)
            });
        }

        private static void DrawSonarEcho(List<XYPoint> points, SonarEcho echo)
        {
            if (echo.Shape == 0)
            {
                AddCircle(points, echo.X, echo.Y, echo.Size, 10, echo.Intensity);
                return;
            }

            int sides = echo.Shape == 1 ? 3 : 4;
            double rotation = echo.Shape == 1 ? -Math.PI / 2 : Math.PI / 4;
            for (int i = 0; i < sides; i++)
            {
                double a0 = rotation + i * Math.PI * 2 / sides;
                double a1 = rotation + (i + 1) * Math.PI * 2 / sides;
                AddLine(points,
                    echo.X + Math.Cos(a0) * echo.Size,
                    echo.Y + Math.Sin(a0) * echo.Size,
                    echo.X + Math.Cos(a1) * echo.Size,
                    echo.Y + Math.Sin(a1) * echo.Size,
                    echo.Intensity);
            }
        }

        private static void DrawMysteryMeter(List<XYPoint> points, double time, float kick, float snare, float hat)
        {
            double horizontalLevel = Math.Clamp(kick / 12f, 0f, 1f);
            double verticalLevel = Math.Clamp((snare + hat * 0.35f) / 2.2f, 0f, 1f);
            AddDoubleFrame(points, 0, 0, 1.0, 0.87, 64);

            double left = -0.74;
            double right = 0.74;
            double bottom = -0.74;
            double top = 0.74;
            double centerY = -0.14;
            double centerX = 0.14;

            AddLine(points, left, centerY, right, centerY, 0.35);
            AddLine(points, centerX, bottom, centerX, top, 0.35);
            AddLine(points, left, centerY - 0.08, right, centerY - 0.08, 0.65);
            AddLine(points, centerX + 0.08, bottom, centerX + 0.08, top, 0.65);

            for (int i = 0; i <= 12; i++)
            {
                double x = left + (right - left) * i / 12.0;
                double y = bottom + (top - bottom) * i / 12.0;
                AddLine(points, x, centerY - 0.08, x, centerY - (i % 3 == 0 ? 0.18 : 0.13), 0.75);
                AddLine(points, centerX + 0.08, y, centerX + (i % 3 == 0 ? 0.18 : 0.13), y, 0.75);
            }

            double horizontalEnd = left + (right - left) * horizontalLevel;
            double verticalEnd = bottom + (top - bottom) * verticalLevel;
            AddLine(points, left, centerY + 0.08, horizontalEnd, centerY + 0.08, 1.0);
            AddLine(points, centerX - 0.08, bottom, centerX - 0.08, verticalEnd, 1.0);

            AddLine(points, horizontalEnd, centerY - 0.14, horizontalEnd, centerY + 0.14, 1.0);
            AddLine(points, centerX - 0.14, verticalEnd, centerX + 0.14, verticalEnd, 1.0);
            AddCircle(points, horizontalEnd, centerY + 0.08, 0.035, 8, 1.0);
            AddCircle(points, centerX - 0.08, verticalEnd, 0.035, 8, 1.0);
        }
    }
}
