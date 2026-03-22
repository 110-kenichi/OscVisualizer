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
    internal class Synthwave : IAudioVisualizer
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
            get => "Synthwave";
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

        private static Vector2 Project(float x, float y, float z)
        {
            float d = 1.0f / (z + 1.0f); // 簡易パース
            return new Vector2(x * d, y * d);
        }

        private static readonly Vector2[] Ferrari = new Vector2[]
        {
            new(-0.60f, -0.20f),
            new(-0.30f, -0.25f),
            new( 0.10f, -0.25f),
            new( 0.40f, -0.20f),
            new( 0.30f, -0.10f),
            new(-0.40f, -0.10f),
            new(-0.60f, -0.20f)
        };

        private static float GetBand(float[] fft, int start, int end, int sampleRate)
        {
            int fftSize = fft.Length;
            float binHz = sampleRate / (float)fftSize;

            int i0 = (int)(start / binHz);
            int i1 = (int)(end / binHz);

            float sum = 0;
            for (int i = i0; i <= i1 && i < fftSize; i++)
                sum += fft[i];

            return sum / (i1 - i0 + 1);
        }

        private float scroll = 0f;

        public List<XYPoint> GenerateXYBuffer(float[] fft, float time, float deltaTime, int sampleRate)
        {
            // --- オーディオ解析 ---
            float kick = GetBand(fft, 50, 100, sampleRate);
            float snare = GetBand(fft, 1500, 3000, sampleRate);
            float hat = GetBand(fft, 6000, 12000, sampleRate);

            kick = MathF.Min(kick, 10f);
            snare = MathF.Min(snare, 2f);
            hat = MathF.Min(hat, 1.5f);

            List<XYPoint> seg = new();

            // --- カメラ揺れ ---
            float camX = MathF.Sin(time * 0.7f) * 0.05f;
            float camY = MathF.Sin(time * 1.3f) * 0.03f;
            camX = 0f;
            camY = 0f;

            // --- グリッド設定 ---
            int gridLines = 8;
            float scrollSpeed = 1.0f + hat * 2;

            // 奥 → 手前に流すために scroll を減らす
            scroll += scrollSpeed * deltaTime;

            // ループ処理
            scroll %= gridLines;

            // ============================================================
            // ① 横線（奥 → 手前に流れる）
            // ============================================================
            for (int i = 0; i < gridLines; i++)
            {
                float z = i - scroll;
                if (z < 0)
                    z += gridLines;
                z %= gridLines;

                // y は奥行き方向の位置
                float y = -1f + (z / (float)gridLines) * 2f;

                Vector2 p1 = Project(-1f - camX, y - camY, z);
                Vector2 p2 = Project(1f - camX, y - camY, z);

                seg.Add(new XYPoint(
                    -1,
                    p1.Y,
                    intensity: 1 / Math.Clamp(z, 0.5, 10)));// 0.1f + kick));
                seg.Add(new XYPoint(
                    1,
                    p2.Y,
                    intensity: 1 / Math.Clamp(z, 0.5, 10)));// 0.1f + kick));
            }

            // ============================================================
            // ② 縦線（左右方向）
            // ============================================================
            int vLines = 8;

            for (int i = 0; i <= vLines; i++)
            {
                float x = -2f + (i / (float)vLines) * 4f;

                Vector2 p1 = Project(x - camX, -1f - camY, 0);
                Vector2 p2 = Project(x - camX, 1f - camY, gridLines);

                seg.Add(new XYPoint(
                    p1.X,
                    p1.Y,
                    intensity: 2));// 0.1f + snare));
                seg.Add(new XYPoint(
                    p2.X,
                    p2.Y,
                    intensity: 0.1));// 0.1f + snare));
            }

            // ============================================================
            // ③ フェラーリ（ポリライン）
            // ============================================================
            seg.AddRange(DrawFerrari3D(time, kick, snare, hat));

            return seg;
        }

        private static readonly Vector3[] Car3D = new Vector3[]
        {
            // 前（フロント）
            new(-0.45f, -0.7f, 0.6f), // 0 左前上
            new( 0.45f, -0.7f, 0.6f), // 1 右前上
            new(-0.5f, -0.9f, 0.6f), // 2 左前下
            new( 0.5f, -0.9f, 0.6f), // 3 右前下

            // 中央（ルーフ）
            new(-0.35f, -0.5f, 0.3f), // 4 左ルーフ前
            new( 0.35f, -0.5f, 0.3f), // 5 右ルーフ前
            new(-0.35f, -0.5f, 0.10f), // 6 左ルーフ後
            new( 0.35f, -0.5f, 0.10f), // 7 右ルーフ後

            // 後（リア）
            new(-0.45f, -0.7f,  0f), // 8 左後上
            new( 0.45f, -0.7f,  0f), // 9 右後上
            new(-0.5f, -0.9f,  0f), // 10 左後下
            new( 0.5f, -0.9f,  0f), // 11 右後下

            // --- テールライト（左右） ---
            new(-0.4f, -0.8f, 0f),  // 12 左テール
            new(-0.30f, -0.8f, 0f),  // 13 左テール
            new(-0.4f, -0.75f, 0f),  // 14 左テール
            new(-0.30f, -0.75f, 0f),  // 15 左テール
            new( 0.30f, -0.75f, 0f),  // 16 右テール
            new( 0.4f, -0.75f, 0f),  // 17 右テール
            new( 0.4f, -0.8f, 0f),  // 18 右テール
            new( 0.30f, -0.8f, 0f),  // 19 右テール

            // --- タイヤ（左右） ---
            new(-0.5f, -1f, 0f),  // 20 左タイヤ
            new(-0.25f, -1f, 0f),  // 21 左タイヤ
            new(-0.5f, -0.9f, 0f),  // 22 左タイヤ
            new(-0.25f, -0.9f, 0f),  // 23 左タイヤ
            new( 0.25f, -0.9f, 0f),  // 24 右タイヤ
            new( 0.5f, -0.9f, 0f),  // 25 右タイヤ
            new( 0.5f, -1f, 0f),  // 26 右タイヤ
            new( 0.25f, -1f, 0f),  // 27 右タイヤ
        };

        private static readonly (int A, int B)[] CarEdges = new (int, int)[]
        {
            // 前
            (0,1), //(0,2), (1,3), (2,3),
            (0,2), (1,3),

            // ルーフ
            (4,5), (4,6), (5,7), (6,7),

            // 前 → ルーフ
            (0,4), (1,5),
            //(2,4), //(3,5),

            // 前 → 後
            (0,8), (1,9), (2,10), (3,11),

            // ルーフ → 後
            (6,8), (7,9),

            // 後
            (8,9), (8,10), (9,11), (10,11),

            // テールライト
            (12,13), (13,15), (14,15), (14,12),
            (16,17), (17,18), (18,19), (19,16),

            // タイヤ
            (21,23), (20,22), (20,21),
            (25,26), (26,27), (27,24),
        };

        Vector3 Rotate(Vector3 p, float yaw, float pitch, float roll)
        {
            // Yaw
            float cy = MathF.Cos(yaw);
            float sy = MathF.Sin(yaw);
            p = new Vector3(
                p.X * cy - p.Z * sy,
                p.Y,
                p.X * sy + p.Z * cy
            );

            // Pitch
            float cp = MathF.Cos(pitch);
            float sp = MathF.Sin(pitch);
            p = new Vector3(
                p.X,
                p.Y * cp - p.Z * sp,
                p.Y * sp + p.Z * cp
            );

            // Roll
            float cr = MathF.Cos(roll);
            float sr = MathF.Sin(roll);
            p = new Vector3(
                p.X * cr - p.Y * sr,
                p.X * sr + p.Y * cr,
                p.Z
            );

            return p;
        }

        Vector2 Project3D(Vector3 p)
        {
            float d = 1.0f / (p.Z + 1.0f); // 奥行き補正
            return new Vector2(p.X * d, p.Y * d);
        }


        List<XYPoint> DrawFerrari3D(float time, float kick, float snare, float hat)
        {
            List<XYPoint> seg = new List<XYPoint>();

            float yaw = 0f; // MathF.Sin(time * 0.8f) * 0.05f;   // 左右揺れ
            float pitch = MathF.Sin(time * 0.7f +  (float)(Math.PI * (snare / 8f))) * 0.2f - 0.2f;   // 前後揺れ
            float roll = MathF.Sin(time * 0.7f) * 0.05f;  // 車体の傾き

            float slide = MathF.Sin(time * 0.7f + 0.1f) * 0.4f;  // 車体の横位置
            //slide += Math.Sign(slide) * Math.Abs(snare) * 0.1f;

            // Kick で車体が上下に跳ねる
            float bounce = kick * 0.005f;

            // 回転 + 投影した頂点を保存
            Vector2[] pts = new Vector2[Car3D.Length];

            for (int i = 0; i < Car3D.Length; i++)
            {
                Vector3 p = Car3D[i];
                p.X += slide;
                p.Y += bounce;

                p = Rotate(p, yaw, pitch, roll);
                pts[i] = Project3D(p);
            }

            // エッジ描画
            foreach (var (A, B) in CarEdges)
            {
                seg.Add(
                    new XYPoint(
                    pts[A].X,
                    pts[A].Y,
                    intensity: 2.0f));// + bounce));
                seg.Add(
                    new XYPoint(
                    pts[B].X,
                    pts[B].Y,
                    intensity: 2.0f));// + bounce));
            }

            // テールライト残像用の位置を更新
            TailLeftPos = pts[TailLeftIndex];
            TailRightPos = pts[TailRightIndex];

            tailHistory.Add((TailLeftPos, TailRightPos));
            if (tailHistory.Count > TailMax)
                tailHistory.RemoveAt(0);

            for (int i = 0; i < tailHistory.Count - 1; i++)
            {
                float t1 = i / (float)TailMax;
                float fade1 = MathF.Pow(t1, 2.2f);
                float t2 = (i + 1) / (float)TailMax;
                float fade2 = MathF.Pow(t2, 2.2f);

                var (L1, R1) = tailHistory[i];
                var (L2, R2) = tailHistory[i + 1];

                seg.Add(
                    new XYPoint(
                    L1.X + 0.05,
                    L1.Y - 0.025,
                    intensity: 0.1 + fade1 + hat));
                seg.Add(
                    new XYPoint(
                    L2.X - 0.05,
                    L2.Y - 0.025,
                    intensity: 0.1 + fade2 + hat));
            }


            for (int i = 0; i < tailHistory.Count - 1; i++)
            {
                float t1 = i / (float)TailMax;
                float fade1 = MathF.Pow(t1, 2.2f);
                float t2 = (i + 1) / (float)TailMax;
                float fade2 = MathF.Pow(t2, 2.2f);

                var (L1, R1) = tailHistory[i];
                var (L2, R2) = tailHistory[i + 1];

                seg.Add(
                    new XYPoint(
                    R1.X + 0.05,
                    R1.Y - 0.025,
                    intensity: 0.1 + fade1));
                seg.Add(
                    new XYPoint(
                    R2.X - 0.05,
                    R2.Y - 0.025,
                    intensity: 0.1 + fade2));
            }

            return seg;
        }

        private Vector2 TailLeftPos;
        private Vector2 TailRightPos;

        private int TailLeftIndex = 14; // 左後下
        private int TailRightIndex = 16; // 右後下

        private readonly List<(Vector2 L, Vector2 R)> tailHistory = new();
        private const int TailMax = 15; // 残像の長さ

    }
}
