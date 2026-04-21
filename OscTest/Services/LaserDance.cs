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

            List<XYPoint> seg = new();
            float laserN = 8f;
            float laserDeg = 30f;

            DrawLasers(kick, seg, laserN, laserDeg, 0f, -0.25f);
            DrawLasers(kick, seg, laserN, laserDeg, -0.25f, -0.25f);
            DrawLasers(kick, seg, laserN, laserDeg, 0.25f, -0.25f);
            DrawLasers(kick, seg, laserN, laserDeg, -0.5f, -0.25f);
            DrawLasers(kick, seg, laserN, laserDeg, 0.5f, -0.25f);

            DrawLasers(kick, seg, laserN, laserDeg, 0, 0.1f);
            DrawLasers(kick, seg, laserN, laserDeg, -0.3f, 0.1f);
            DrawLasers(kick, seg, laserN, laserDeg, 0.3f, 0.1f);

            return seg;
        }

        private void DrawLasers(float kick, List<XYPoint> seg, float laserN, float laserDeg, float cx, float cy)
        {
            for (int i = 0; i <= laserN; i++)
                RenderLaser(seg, -(laserDeg / 2f) + ((laserDeg * i) / laserN), -kick, 0, cx, cy);
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
