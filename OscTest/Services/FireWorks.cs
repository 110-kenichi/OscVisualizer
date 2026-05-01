using MathNet.Numerics;
using MathNet.Numerics.Distributions;
using MathNet.Numerics.IntegralTransforms;
using NAudio.CoreAudioApi;
using NAudio.Dsp;
using NAudio.Wave;
using OscVisualizer.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Text;
using System.Threading.Tasks;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.TaskbarClock;

namespace OscVisualizer.Services
{
    internal class FireWorks : IAudioVisualizer
    {
        public string VisualizerName
        {
            get => "Fire Works";
        }

        private float prevX = 0;
        private float prevY = 0;
        private float R = 0.995f; // カットオフ調整

        private readonly Random _random = new Random(DateTime.Now.Millisecond);
        private readonly List<FireBurst> _bursts = new List<FireBurst>();
        private readonly List<FireRocket> _rockets = new List<FireRocket>();
        private float _spawnAccumulator = 0f;
        private int _patternIndex = 0;

        private class FireRocket
        {
            public int Type;
            public float BaseX;
            public float Age;
            public float PositionY;
            public float PrevY;
            public float UpSpeed;
            public float ExplodeY;
            public float SwayAmp;
            public float SwayOffset;
            public float SwayVelocity;
            public float SwayJitter;
            public float BallRadius;
        }

        private class FireParticle
        {
            public Vector2 Position;
            public Vector2 PrevPosition;
            public Vector2 Velocity;
            public float Life;
            public float MaxLife;
            public float Drag;
            public float Gravity;
            public float Brightness;
            public bool IsSparkler;
            public bool BlinkOnFade;
        }

        private class FireBurst
        {
            public int Type;
            public Vector2 Center;
            public float Age;
            public float Duration;
            public List<FireParticle> Particles = new List<FireParticle>();
        }

        private static void AddSegment(List<XYPoint> points, float x0, float y0, float x1, float y1, float intensity = 0.7f)
        {
            points.Add(new XYPoint(x0, y0, intensity));
            points.Add(new XYPoint(x1, y1, intensity));
        }

        private static void AddBall(List<XYPoint> points, Vector2 center, float radius, float intensity)
        {
            const int steps = 10;
            for (int i = 0; i < steps; i++)
            {
                float a0 = 2f * MathF.PI * i / steps;
                float a1 = 2f * MathF.PI * (i + 1) / steps;
                float x0 = center.X + radius * MathF.Cos(a0);
                float y0 = center.Y + radius * MathF.Sin(a0);
                float x1 = center.X + radius * MathF.Cos(a1);
                float y1 = center.Y + radius * MathF.Sin(a1);
                AddSegment(points, x0, y0, x1, y1, intensity);
            }
        }

        private float HighPass(float x)
        {
            float y = x - prevX + R * prevY;
            prevX = x;
            prevY = y;
            return y;
        }

        private double _lastTime = 0;

        private readonly Stopwatch _sw = Stopwatch.StartNew();

        public float GetDeltaTime()
        {
            double now = _sw.Elapsed.TotalSeconds;
            float delta = (float)(now - _lastTime);
            _lastTime = now;

            return delta;
        }

        private Vector2 RandomDir()
        {
            float a = (float)(_random.NextDouble() * Math.PI * 2.0);
            return new Vector2(MathF.Cos(a), MathF.Sin(a));
        }

        private FireParticle NewParticle(Vector2 center, Vector2 vel, float life, float drag, float gravity, float brightness, bool isSparkler = false)
        {
            float gravityJitter = 0.9f + 0.2f * (float)_random.NextDouble(); // 0.9..1.1

            return new FireParticle
            {
                Position = center,
                PrevPosition = center,
                Velocity = vel,
                Life = life,
                MaxLife = life,
                Drag = drag,
                Gravity = gravity * gravityJitter,
                Brightness = brightness,
                IsSparkler = isSparkler,
                BlinkOnFade = _random.NextDouble() < 0.5,
            };
        }

        private void SpawnBurst(float k, float s, float h)
        {
            int type = _patternIndex;
            _patternIndex = (_patternIndex + 1) % 8;

            var center = new Vector2(
                (float)(_random.NextDouble() * 1.5 - 0.75),
                (float)(_random.NextDouble() * 0.9 - 0.2)
            );

            SpawnBurstAt(type, center, k, s, h);
        }

        private void SpawnBurstAt(int type, Vector2 center, float k, float s, float h)
        {
            var burst = new FireBurst
            {
                Type = type,
                Center = center,
                Age = 0f,
                Duration = 1.8f + 1.2f * k,
            };

            float vBase = 0.20f + 0.55f * k;
            float lifeBase = 0.6f + 1.1f * s;

            if (type == 0) // Radial
            {
                int n = 36 + (int)(k * 36f);
                for (int i = 0; i < n; i++)
                {
                    var d = RandomDir();
                    float sp = vBase * (0.7f + 0.6f * (float)_random.NextDouble());
                    burst.Particles.Add(NewParticle(center, d * sp, lifeBase, 0.985f, 0.45f, 0.8f));
                }
            }
            else if (type == 1) // Ring
            {
                int n = 30 + (int)(k * 26f);
                for (int i = 0; i < n; i++)
                {
                    float a = 2f * MathF.PI * i / n;
                    var d = new Vector2(MathF.Cos(a), MathF.Sin(a));
                    burst.Particles.Add(NewParticle(center, d * (vBase * 0.95f), lifeBase * 1.05f, 0.988f, 0.38f, 0.85f));
                }
            }
            else if (type == 2) // Chrysanthemum
            {
                int n = 44 + (int)(k * 40f);
                for (int i = 0; i < n; i++)
                {
                    var d = RandomDir();
                    float sp = vBase * (0.5f + 0.9f * (float)_random.NextDouble());
                    burst.Particles.Add(NewParticle(center, d * sp, lifeBase * 1.2f, 0.99f, 0.30f, 0.9f));
                }
            }
            else if (type == 3) // Palm
            {
                int n = 28 + (int)(k * 24f);
                for (int i = 0; i < n; i++)
                {
                    float a = (float)(_random.NextDouble() * Math.PI * 0.8 + Math.PI * 0.1);
                    var d = new Vector2(MathF.Cos(a), MathF.Sin(a));
                    burst.Particles.Add(NewParticle(center, d * (vBase * 1.05f), lifeBase * 1.35f, 0.992f, 0.62f, 0.75f));
                }
            }
            else if (type == 4) // Peony double layer
            {
                int n = 26 + (int)(k * 24f);
                for (int i = 0; i < n; i++)
                {
                    var d = RandomDir();
                    burst.Particles.Add(NewParticle(center, d * (vBase * 0.7f), lifeBase * 1.0f, 0.986f, 0.40f, 0.72f));
                    burst.Particles.Add(NewParticle(center, d * (vBase * 1.05f), lifeBase * 0.75f, 0.983f, 0.36f, 0.95f));
                }
            }
            else if (type == 5) // Willow
            {
                int n = 32 + (int)(k * 20f);
                for (int i = 0; i < n; i++)
                {
                    float a = (float)(_random.NextDouble() * Math.PI * 0.9 + Math.PI * 0.05);
                    var d = new Vector2(MathF.Cos(a), MathF.Sin(a));
                    burst.Particles.Add(NewParticle(center, d * (vBase * 0.9f), lifeBase * 1.6f, 0.994f, 0.95f, 0.70f));
                }
                burst.Duration += 0.7f;
            }
            else if (type == 6) // Star/Cross
            {
                // 8方向をランダムな角度で少し回転
                float angleOffset = (float)((_random.NextDouble() * 2.0 - 1.0) * (Math.PI / 8.0)); // -22.5° .. 22.5°
                float ca = MathF.Cos(angleOffset);
                float sa = MathF.Sin(angleOffset);

                var axes = new[]
                {
                    new Vector2(1,0), new Vector2(-1,0), new Vector2(0,1), new Vector2(0,-1),
                    Vector2.Normalize(new Vector2(1,1)), Vector2.Normalize(new Vector2(-1,1)),
                    Vector2.Normalize(new Vector2(1,-1)), Vector2.Normalize(new Vector2(-1,-1))
                };

                foreach (var d0 in axes)
                {
                    var d = new Vector2(
                        d0.X * ca - d0.Y * sa,
                        d0.X * sa + d0.Y * ca
                    );

                    for (int j = 0; j < 4 + (int)(k * 4f); j++)
                    {
                        float sp = vBase * (0.6f + 0.5f * j / (4f + k * 4f));
                        burst.Particles.Add(NewParticle(center, d * sp, lifeBase * (0.85f + 0.08f * j), 0.988f, 0.44f, 0.9f));
                    }
                }
            }
            else // 7: 線香花火
            {
                burst.Duration = 2.8f + 1.8f * h;
                int n = 24 + (int)(h * 26f);
                for (int i = 0; i < n; i++)
                {
                    float a = (float)(_random.NextDouble() * Math.PI * 2.0);
                    float sp = 0.03f + 0.12f * (float)_random.NextDouble();
                    var d = new Vector2(MathF.Cos(a), MathF.Sin(a));
                    burst.Particles.Add(NewParticle(center, d * sp, 0.9f + 2.0f * (float)_random.NextDouble(), 0.965f, 0.85f, 1.0f, true));
                }
            }

            _bursts.Add(burst);
            if (_bursts.Count > 14)
                _bursts.RemoveAt(0);
        }

        private void SpawnRocket(float k, float s, float h)
        {
            int type = _patternIndex;
            _patternIndex = (_patternIndex + 1) % 8;

            var rocket = new FireRocket
            {
                Type = type,
                BaseX = (float)(_random.NextDouble() * 1.5 - 0.75),
                PositionY = -1f,
                PrevY = -1f,
                Age = 0f,
                UpSpeed = 1.0f + 0.7f * k + 0.35f * s,
                ExplodeY = -0.2f + 1.0f * (0.35f + 0.65f * h),
                SwayAmp = 0.015f + 0.05f * (0.3f + 0.7f * h),
                SwayOffset = 0f,
                SwayVelocity = (float)(_random.NextDouble() * 0.08 - 0.04),
                SwayJitter = 0.35f + 0.9f * h,
                BallRadius = 0.0015f + 0.02f * (0.3f + 0.7f * k),
            };

            _rockets.Add(rocket);
            if (_rockets.Count > 8)
                _rockets.RemoveAt(0);
        }

        public List<XYPoint> ProcessAudio(WasapiCapture capture, WaveInEventArgs ea)
        {
            var fmt = capture.WaveFormat;
            int channels = fmt.Channels;
            int inputSampleRate = fmt.SampleRate;

            float[] wav = IAudioVisualizer.ConvertToWav1ch(capture, ea);

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

            float kick = MathF.Min(IAudioVisualizer.GetBand(spectrum, 50, 100, inputSampleRate), 20f);
            float snare = MathF.Min(IAudioVisualizer.GetBand(spectrum, 1500, 3000, inputSampleRate), 2.5f);
            float hat = MathF.Min(IAudioVisualizer.GetBand(spectrum, 6000, 12000, inputSampleRate), 2.0f);

            float k = Math.Clamp(kick / 20f, 0f, 1f);
            float s = Math.Clamp(snare / 2.5f, 0f, 1f);
            float h = Math.Clamp(hat / 2.0f, 0f, 1f);

            float dt = Math.Clamp(GetDeltaTime(), 0f, 0.05f);

            _spawnAccumulator += dt * (0.8f + 3.0f * k + 1.4f * s);
            if (h > 0.75f)
                _spawnAccumulator += dt * 0.8f;

            while (_spawnAccumulator >= 1f)
            {
                SpawnRocket(k, s, h);
                _spawnAccumulator -= 1f;
            }

            List<XYPoint> points = new List<XYPoint>(1200);

            for (int b = _bursts.Count - 1; b >= 0; b--)
            {
                var burst = _bursts[b];
                burst.Age += dt;

                for (int i = burst.Particles.Count - 1; i >= 0; i--)
                {
                    var p = burst.Particles[i];
                    p.PrevPosition = p.Position;

                    if (p.IsSparkler)
                    {
                        float jitterA = (float)(_random.NextDouble() * Math.PI * 2.0);
                        float jitterV = 0.12f * h + 0.08f * s;
                        p.Velocity += new Vector2(MathF.Cos(jitterA), MathF.Sin(jitterA)) * jitterV * dt;
                    }

                    p.Position += p.Velocity * dt;
                    p.Velocity *= MathF.Pow(p.Drag, dt * 60f);
                    p.Velocity.Y -= p.Gravity * dt;
                    p.Life -= dt;

                    if (p.Life <= 0f)
                    {
                        burst.Particles.RemoveAt(i);
                        continue;
                    }

                    float life01 = Math.Clamp(p.Life / p.MaxLife, 0f, 1f);
                    float elapsed01 = 1f - life01;
                    float sparkle = 0.75f + 0.25f * MathF.Sin((burst.Age * 24f + i * 0.7f) * (1f + 2f * h));

                    // 生成直後: 2 -> 1
                    const float attack01 = 0.12f;
                    float intensityEnvelope = elapsed01 < attack01
                        ? (2f - (elapsed01 / attack01))
                        : 1f;

                    // 消失直前は減光せず明滅OFFで消える
                    bool isVisible = true;
                    const float fade01 = 0.25f;
                    if (life01 < fade01 && p.BlinkOnFade)
                    {
                        float t = life01 / fade01; // 1 -> 0
                        float wave1 = 0.5f + 0.5f * MathF.Sin((burst.Age * 60f + i * 3.1f) * (1f + 2.5f * h));
                        float wave2 = 0.5f + 0.5f * MathF.Sin((burst.Age * 97f + i * 5.7f));
                        float flicker = wave1 * 0.6f + wave2 * 0.4f;
                        float threshold = 0.25f + 0.7f * t; // 終端ほどOFFが増える
                        isVisible = flicker >= threshold;
                    }

                    float variation = 0.95f + 0.1f * p.Brightness * sparkle;
                    float intensity = Math.Clamp(intensityEnvelope * variation, 1f, 2f);

                    if (isVisible)
                    {
                        AddSegment(points, p.PrevPosition.X, p.PrevPosition.Y, p.Position.X, p.Position.Y, intensity);

                        if ((h > 0.45f || p.IsSparkler) && _random.NextDouble() < dt * (1.5 + 3.5 * h))
                        {
                            var d = RandomDir();
                            float len = 0.015f + 0.035f * h;
                            var ep = p.Position + d * len;
                            AddSegment(points, p.Position.X, p.Position.Y, ep.X, ep.Y, Math.Min(2f, intensity + 0.2f));
                        }
                    }
                }

                if (burst.Particles.Count == 0 || burst.Age > burst.Duration)
                    _bursts.RemoveAt(b);
            }

            for (int r = _rockets.Count - 1; r >= 0; r--)
            {
                var rocket = _rockets[r];
                rocket.Age += dt;

                rocket.PrevY = rocket.PositionY;
                rocket.PositionY += rocket.UpSpeed * dt;

                float prevX = rocket.BaseX + rocket.SwayOffset;

                float randomForce = (float)(_random.NextDouble() * 2.0 - 1.0) * rocket.SwayJitter;
                rocket.SwayVelocity += randomForce * dt;
                rocket.SwayVelocity *= MathF.Pow(0.9f, dt * 60f);
                rocket.SwayOffset += rocket.SwayVelocity * dt;

                if (rocket.SwayOffset > rocket.SwayAmp)
                {
                    rocket.SwayOffset = rocket.SwayAmp;
                    rocket.SwayVelocity *= -0.6f;
                }
                else if (rocket.SwayOffset < -rocket.SwayAmp)
                {
                    rocket.SwayOffset = -rocket.SwayAmp;
                    rocket.SwayVelocity *= -0.6f;
                }

                float currX = rocket.BaseX + rocket.SwayOffset;

                if (rocket.PositionY >= rocket.ExplodeY)
                {
                    SpawnBurstAt(rocket.Type, new Vector2(currX, rocket.ExplodeY), k, s, h);
                    _rockets.RemoveAt(r);
                    continue;
                }

                float life01 = Math.Clamp(rocket.Age / 1.6f, 0f, 1f);
                float intensity = Math.Clamp(1.9f - life01 * 1.2f, 0.5f, 1.9f);

                // 打ち上げ直後のほんの短時間だけ可視化
                const float launchVisibleDuration = 0.075f;
                if (rocket.Age <= launchVisibleDuration)
                {
                    // 打ち上げ軌跡（3倍の長さ・末端に向かって減衰）
                    float trailDx = currX - prevX;
                    float trailDy = rocket.PositionY - rocket.PrevY;
                    float x0 = currX - trailDx * 3f;
                    float y0 = rocket.PositionY - trailDy * 3f;
                    float x1 = currX;
                    float y1 = rocket.PositionY;

                    const int trailSteps = 6;
                    float headIntensity = intensity * 0.8f;
                    for (int t = 0; t < trailSteps; t++)
                    {
                        float u0 = t / (float)trailSteps;
                        float u1 = (t + 1) / (float)trailSteps;

                        // 末端は減光せず非表示
                        if (u1 < 0.45f)
                            continue;

                        float sx = x0 + (x1 - x0) * u0;
                        float sy = y0 + (y1 - y0) * u0;
                        float ex = x0 + (x1 - x0) * u1;
                        float ey = y0 + (y1 - y0) * u1;

                        AddSegment(points, sx, sy, ex, ey, headIntensity);
                    }

                    // 打ち上げ中の大きめの玉
                    float ballSize = rocket.BallRadius * (1.15f - 0.2f * life01);
                    AddBall(points, new Vector2(currX, rocket.PositionY), ballSize, intensity);

                    // 打ち上げ開始時のミニ爆発（玉から放射状）
                    const float launchBurstDuration = 2.035f;
                    if (rocket.Age <= launchBurstDuration)
                    {
                        float burstT = rocket.Age / launchBurstDuration;
                        float burstIntensity = Math.Clamp(2f - burstT, 1f, 2f);
                        int burstLines = 10 + (int)(h * 8f);
                        float burstLen = ballSize * (0.9f + 1.6f * (1f - burstT));

                        for (int bi = 0; bi < burstLines; bi++)
                        {
                            float a = (float)(_random.NextDouble() * Math.PI * 2.0);
                            float len = burstLen * (0.6f + 0.8f * (float)_random.NextDouble());
                            float ex = currX + MathF.Cos(a) * len;
                            float ey = rocket.PositionY + MathF.Sin(a) * len;
                            AddSegment(points, currX, rocket.PositionY, ex, ey, burstIntensity);
                        }
                    }
                }
            }

            AddSegment(points, -1.0f, -1, -0.99f, -1, 1f);
            AddSegment(points, 0.99f, -1, 1, -1, 1f);

            return points;
        }

    }
}
