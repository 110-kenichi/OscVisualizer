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
    internal class LaserDance : IAudioVisualizer
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

        private LaserPattern _pattern = LaserPattern.Horizontal;
        private float _nextPatternChange = 7f;

        private float _kickAverage;
        private float _kickPeak;
        private bool _kickArmed = true;
        private float _lastKickTime = float.NegativeInfinity;
        private float _fireStartTime = float.NegativeInfinity;
        private float _fireSeed;
        private const float FireJetDuration = 1.0f;
        private const float FireDuration = 4.2f;

        private enum LaserPattern
        {
            Horizontal,
            Rotating,
            Cone,
            Wave,
            Scissor
        }

        public string VisualizerName
        {
            get => "Laser Dance";
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
            // --- オーディオ解析 ---
            float kick = IAudioVisualizer.GetBand(fft, 50, 100, sampleRate);
            float snare = IAudioVisualizer.GetBand(fft, 1500, 3000, sampleRate);
            float hat = IAudioVisualizer.GetBand(fft, 6000, 12000, sampleRate);

            kick = MathF.Min(kick, 20f);
            snare = MathF.Min(snare, 2f);
            hat = MathF.Min(hat, 1.5f);

            if (time >= _nextPatternChange)
            {
                LaserPattern next;
                do
                {
                    next = (LaserPattern)_random.Next(0, Enum.GetValues<LaserPattern>().Length);
                }
                while (next == _pattern);

                _pattern = next;
                _nextPatternChange = time + 6f + _random.NextSingle() * 4f;
            }

            List<XYPoint> seg = new(144);
            //_pattern = LaserPattern.Rotating;
            switch (_pattern)
            {
                case LaserPattern.Horizontal:
                    DrawHorizontalLasers(seg, time, kick, snare);
                    break;
                case LaserPattern.Rotating:
                    DrawRotatingLasers(seg, time, kick, hat);
                    break;
                case LaserPattern.Cone:
                    DrawConeLasers(seg, time, kick, hat);
                    break;
                case LaserPattern.Wave:
                    DrawWaveLasers(seg, time, kick, snare);
                    break;
                case LaserPattern.Scissor:
                    DrawScissorLasers(seg, time, kick, snare);
                    break;
            }

            UpdateFire(fft, time, deltaTime, sampleRate);
            DrawFireColumns(seg, time);

            return seg;
        }

        private void UpdateFire(float[] spectrum, float time, float deltaTime, int sampleRate)
        {
            if (spectrum.Length == 0 || sampleRate <= 0)
                return;

            // ProcessAudio supplies the positive-frequency half of an unscaled FFT.
            float binHz = sampleRate / (2f * spectrum.Length);
            int first = Math.Max(1, (int)MathF.Ceiling(50f / binHz));
            int last = Math.Min(spectrum.Length - 1, (int)MathF.Floor(100f / binHz));
            float energy = 0f;
            for (int i = first; i <= last; i++)
                energy += spectrum[i] * spectrum[i];
            float level = MathF.Sqrt(energy) / spectrum.Length;

            float dt = float.IsFinite(deltaTime) ? Math.Clamp(deltaTime, 0f, 0.1f) : 0f;
            float threshold = MathF.Max(0.003f, _kickAverage * 1.8f);
            if (level < MathF.Max(0.0015f, _kickPeak * 0.55f))
                _kickArmed = true;

            if (_kickArmed && level > threshold && time - _lastKickTime >= 0.18f)
            {
                _kickArmed = false;
                _lastKickTime = time;
                _kickPeak = level;
                // Only roll for a new flame after the current effect has finished.
                if (time - _fireStartTime >= FireDuration) // && _random.NextSingle() < 0.5f)
                {
                    _fireStartTime = time;
                    _fireSeed = _random.NextSingle() * MathF.Tau;
                }
            }

            _kickPeak = MathF.Max(level, _kickPeak * MathF.Exp(-dt / 0.25f));
            _kickAverage += (level - _kickAverage) * (1f - MathF.Exp(-dt / 0.35f));
        }

        private void DrawFireColumns(List<XYPoint> seg, float time)
        {
            float age = time - _fireStartTime;
            if (age <= 0f || age >= FireDuration)
                return;

            float growth = FireSmooth(age / 0.65f);
            float release = MathF.Max(0f, age - FireJetDuration);
            float separation = FireSmooth(release / 0.3f);
            float cooling = FireSmooth(release / (FireDuration - FireJetDuration));
            float fade = 1f - cooling;

            for (int column = 0; column < 2; column++)
            {
                float centerX = column == 0 ? -0.65f : 0.65f;
                float phase = _fireSeed + column * 2.7f;
                const int steps = 80;
                Vector2[] outline = new Vector2[steps + 1];
                Vector2 previous = FireOutline(0f, centerX, age, phase,
                    growth, release, separation, cooling);
                outline[0] = previous;
                for (int i = 1; i <= steps; i++)
                {
                    Vector2 current = i == steps ? outline[0]
                        : FireOutline(i * MathF.Tau / steps,
                            centerX, age, phase, growth, release, separation, cooling);
                    outline[i] = current;
                    // Stable groups of fragments shorten and fade as the flame cools.
                    float fragmentLife = 0.3f + 0.7f * ((i / 4 * 17 + column * 11) % 23) / 22f;
                    float visibility = 1f - FireSmooth((cooling - fragmentLife + 0.2f) / 0.2f);
                    if (visibility > 0f)
                    {
                        Vector2 end = Vector2.Lerp(previous, current, visibility);
                        float brightness = 0.9f * growth * fade * visibility;
                        seg.Add(new XYPoint(previous.X, previous.Y, brightness));
                        seg.Add(new XYPoint(end.X, end.Y, brightness));
                    }
                    previous = current;
                }
                // Follow the outline's cooling duration, approaching intensity 0.1.
                DrawFireMesh(seg, outline, growth * (0.5f + 0.45f * fade));
            }
        }

        private static void DrawFireMesh(List<XYPoint> seg, Vector2[] outline,
            float brightness)
        {
            if (brightness <= 0.001f)
                return;

            // Crosshatch at +/-45 degrees, clipped to the animated silhouette.
            float spacing = 0.075f * (1f / (brightness * brightness));
            const float diagonal = 0.70710678f;
            List<float> intersections = new(outline.Length);
            for (int direction = -1; direction <= 1; direction += 2)
            {
                Vector2 normal = new(diagonal, direction * diagonal);
                Vector2 tangent = new(-normal.Y, normal.X);
                float min = float.PositiveInfinity;
                float max = float.NegativeInfinity;
                for (int i = 0; i < outline.Length - 1; i++)
                {
                    float projection = Vector2.Dot(outline[i], normal);
                    min = MathF.Min(min, projection);
                    max = MathF.Max(max, projection);
                }

                for (int line = (int)MathF.Ceiling(min / spacing);
                    line * spacing < max; line++)
                {
                    float offset = line * spacing;
                    intersections.Clear();
                    for (int i = 0; i < outline.Length - 1; i++)
                    {
                        Vector2 a = outline[i];
                        Vector2 b = outline[i + 1];
                        float da = Vector2.Dot(a, normal);
                        float db = Vector2.Dot(b, normal);
                        if ((da <= offset && db > offset) || (db <= offset && da > offset))
                            intersections.Add(Vector2.Dot(
                                Vector2.Lerp(a, b, (offset - da) / (db - da)), tangent));
                    }
                    intersections.Sort();
                    for (int i = 0; i + 1 < intersections.Count; i += 2)
                    {
                        Vector2 start = normal * offset + tangent * intersections[i];
                        Vector2 end = normal * offset + tangent * intersections[i + 1];
                        float length = Vector2.Distance(start, end);
                        if (length <= 0.000001f)
                            continue;

                        // A bright rounded core, with a faint fringe beyond the silhouette.
                        float fringe = MathF.Min(0.045f, length * 0.2f);
                        Vector2 axis = (end - start) / length;
                        const int sections = 12;
                        for (int section = 0; section < sections; section++)
                        {
                            float from = -fringe + (length + 2f * fringe) * section / sections;
                            float to = -fringe + (length + 2f * fringe) * (section + 1) / sections;
                            float middle = (from + to) * 0.5f;
                            float shade;
                            if (middle < 0f || middle > length)
                            {
                                float distance = middle < 0f ? -middle : middle - length;
                                shade = 0.18f * (1f - FireSmooth(distance / fringe));
                            }
                            else
                            {
                                shade = 0.18f + 0.82f * MathF.Sin(MathF.PI * middle / length);
                            }

                            Vector2 a = start + axis * from;
                            Vector2 b = start + axis * to;
                            float intensity = brightness * shade;
                            seg.Add(new XYPoint(a.X, a.Y, intensity));
                            seg.Add(new XYPoint(b.X, b.Y, intensity));
                        }
                    }
                }
            }
        }

        private static Vector2 FireOutline(float angle, float centerX, float age,
            float phase, float growth, float release, float separation, float cooling)
        {
            float v = (1f - MathF.Cos(angle)) * 0.5f;
            float bottom = 0.52f * separation;
            float u = bottom + (1f - bottom) * v;
            float side = MathF.Sin(angle) >= 0f ? 1f : -1f;

            // A narrow jet feeds a rounded head with rolling, asymmetric lobes.
            float head = Math.Clamp((u - 0.48f) / 0.52f, 0f, 1f);
            float bulb = MathF.Pow(MathF.Max(0f, MathF.Sin(head * MathF.PI)), 0.65f);
            float neck = 0.016f * (1f - u);
            float width = (neck + 0.17f * bulb) * growth;
            width *= (1f + 0.4f * cooling) * (0.94f + 0.06f * MathF.Sin(phase));
            width *= 1f
                + (0.12f + 0.08f * cooling) * MathF.Sin(u * 29f - age * 7f + phase + side)
                + 0.06f * MathF.Sin(u * 53f - age * 11f + phase * 2f + side);
            // Close the underside while the jet withdraws into the detached head.
            width *= 1f + (FireSmooth(v / 0.12f) - 1f) * separation;

            float sway = (0.024f * MathF.Sin(u * 9f - age * 3f + phase)
                + 0.012f * MathF.Sin(u * 21f - age * 7f + phase)) * u * growth;
            float x = centerX + sway + side * width;
            float height = 1.38f + 0.06f * MathF.Sin(phase);
            float y = -0.96f + height * growth * u + release * 0.19f;
            return new Vector2(x, y);
        }

        private static float FireSmooth(float value)
        {
            float t = Math.Clamp(value, 0f, 1f);
            return t * t * (3f - 2f * t);
        }

        private static readonly Vector2[] LaserOrigins =
        {
            new(0f, -0.25f), new(-0.25f, -0.25f), new(0.25f, -0.25f),
            new(-0.5f, -0.25f), new(0.5f, -0.25f), new(0f, 0.1f),
            new(-0.3f, 0.1f), new(0.3f, 0.1f)
        };

        private void DrawHorizontalLasers(List<XYPoint> seg, float time, float kick, float snare)
        {
            for (int i = 0; i < LaserOrigins.Length; i++)
            {
                Vector2 origin = LaserOrigins[i];
                float phase = time * (0.8f + 0.15f) + 0.8f;
                float pitch = -kick + MathF.Sin(phase) * 3f;
                DrawYawFan(seg, origin.X, origin.Y, MathF.Sin(phase * 0.5f) * 18f, pitch, 8, 15f);
            }
        }

        private void DrawRotatingLasers(List<XYPoint> seg, float time, float kick, float hat)
        {
            float rotation = ToRadians(time * 55f * 4);
            float beamSpacing = 1.5f + MathF.Min(kick, 10f) * 0.3f;

            for (int i = 0; i < LaserOrigins.Length; i++)
            {
                Vector2 origin = LaserOrigins[i];

                for (int beam = 0; beam < 8; beam++)
                {
                    float yaw = (beam - 3.5f) * beamSpacing;
                    RenderRotatedLaser(seg, yaw, rotation, origin.X, origin.Y);
                }
            }
        }

        private void DrawConeLasers(List<XYPoint> seg, float time, float kick, float hat)
        {
            float radius = 10f + kick * 0.4f;
            for (int i = 0; i < LaserOrigins.Length; i++)
            {
                Vector2 origin = LaserOrigins[i];
                float direction = origin.X < 0f ? -1f : 1f;
                float phase = time * 1.8f * direction + i * 0.4f;
                float centerYaw = -origin.X * 18f + MathF.Sin(time * 0.6f) * 6f;
                for (int beam = 0; beam < 8; beam++)
                {
                    float angle = phase + beam * MathF.PI / 4f;
                    float yaw = centerYaw + MathF.Cos(angle) * radius;
                    float pitch = -5f + MathF.Sin(angle) * radius * (0.6f + hat * 0.15f);
                    RenderLaser(seg, yaw, pitch, 0f, origin.X, origin.Y);
                }
            }
        }

        private void DrawWaveLasers(List<XYPoint> seg, float time, float kick, float snare)
        {
            float amplitude = 5f + snare * 4f;
            float width = 24f + kick * 0.5f;
            for (int i = 0; i < LaserOrigins.Length; i++)
            {
                Vector2 origin = LaserOrigins[i];
                for (int beam = 0; beam < 8; beam++)
                {
                    float position = beam / 7f;
                    float phase = position * MathF.PI * 2f + origin.X * 4f - time * 2.5f;
                    float yaw = -origin.X * 12f + (position - 0.5f) * width;
                    float pitch = -5f + MathF.Sin(phase) * amplitude;
                    RenderLaser(seg, yaw, pitch, 0f, origin.X, origin.Y);
                }
            }
        }

        private void DrawScissorLasers(List<XYPoint> seg, float time, float kick, float snare)
        {
            float sweep = MathF.Sin(time * 1.7f);
            float spread = 10f + kick * 0.5f;
            for (int i = 0; i < LaserOrigins.Length; i++)
            {
                Vector2 origin = LaserOrigins[i];
                float side = origin.X == 0f
                    ? (origin.Y < 0f ? -1f : 1f)
                    : MathF.Sign(origin.X);
                for (int beam = 0; beam < 8; beam++)
                {
                    float offset = (beam / 7f - 0.5f) * spread;
                    float yaw = -side * (12f + sweep * 16f) + offset;
                    float pitch = -4f + side * sweep * (6f + snare * 3f) + offset * side * 0.45f;
                    RenderLaser(seg, yaw, pitch, 0f, origin.X, origin.Y);
                }
            }
        }

        private void DrawYawFan(List<XYPoint> seg, float cx, float cy, float centerYaw, float pitch, int count, float width)
        {
            DrawYawFan(seg, cx, cy, centerYaw, pitch, 0f, count, width);
        }

        private void DrawYawFan(List<XYPoint> seg, float cx, float cy, float centerYaw, float pitch, float roll, int count, float width)
        {
            for (int i = 0; i < count; i++)
            {
                float yaw = centerYaw - width / 2f + width * i / (count - 1);
                RenderLaser(seg, yaw, pitch, roll, cx, cy);
            }
        }

        private void RenderRotatedLaser(List<XYPoint> seg, float yawDeg, float rotation, float cx, float cy)
        {
            Matrix4x4 world =
                Matrix4x4.CreateRotationY(ToRadians(yawDeg)) *
                Matrix4x4.CreateRotationZ(rotation) *
                Matrix4x4.CreateTranslation(cx, cy, 0f);
            Vector2 start = ProjectToXY(Vector3.Transform(Vector3.Zero, world), Perspective);
            Vector2 end = ProjectToXY(Vector3.Transform(new Vector3(0f, 0f, LaserLength), world), Perspective);

            seg.Add(new XYPoint(start.X, start.Y, intensity: 2));
            seg.Add(new XYPoint(
                Math.Clamp(end.X, -1f, 1f),
                Math.Clamp(end.Y, -1f, 1f),
                intensity: 0.1));
        }

        // レーザーの長さ（3D 空間）
        public float LaserLength = 1.0f;

        // パース強度
        public float Perspective = 1.2f;

        // XYProcessor への描画
        public void RenderLaser(
            List<XYPoint> seg,
            float yawDeg, float pitchDeg, float rollDeg,
            float cx, float cy)
        {
            // --- 1. 角度をラジアンに変換 ---
            float yaw = ToRadians(yawDeg);
            float pitch = ToRadians(pitchDeg);
            float roll = ToRadians(rollDeg);

            // --- 2. 回転行列を作成 ---
            Vector3 origin = new Vector3(cx, cy, 0); // 発射位置
            Matrix4x4 rot = Matrix4x4.CreateFromYawPitchRoll(yaw, pitch, roll);
            Matrix4x4 trans = Matrix4x4.CreateTranslation(origin);
            Matrix4x4 world = rot * trans; // 回転してから移動

            // --- 3. レーザーの始点と終点（3D 空間） ---
            Vector3 p0 = Vector3.Transform(new Vector3(0, 0, 0), world);
            Vector3 p1 = Vector3.Transform(new Vector3(0, 0, LaserLength), world);

            // --- 4. 3D → 2D パース投影 ---
            Vector2 s0 = ProjectToXY(p0, Perspective);
            Vector2 s1 = ProjectToXY(p1, Perspective);

            // --- 6. XYProcessor に描画 ---
            seg.Add(new XYPoint(s0.X, s0.Y, intensity: 2));
            seg.Add(new XYPoint(s1.X, s1.Y, intensity: 0.1));
        }

        // --- 度→ラジアン ---
        private static float ToRadians(float deg)
        {
            return deg * (MathF.PI / 180f);
        }

        // --- 3D → 2D パース投影 ---
        private Vector2 ProjectToXY(Vector3 p, float perspective)
        {
            // Z が大きいほど小さく見える
            float zFactor = (1.0f + p.Z * perspective);

            float x = p.X * zFactor;
            float y = p.Y * zFactor;

            // XYProcessor の -1〜1 に収める
            return new Vector2(
                Math.Clamp(x, -1f, 1f),
                Math.Clamp(y, -1f, 1f)
            );
        }
    }
}
