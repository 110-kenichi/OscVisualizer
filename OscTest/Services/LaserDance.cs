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

        private enum LaserPattern
        {
            Horizontal,
            Rotating
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
            }

            return seg;
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
