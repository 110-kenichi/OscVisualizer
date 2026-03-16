using Avalonia;
using Avalonia.Rendering;
using DynamicData;
using MathNet.Numerics;
using MathNet.Numerics.IntegralTransforms;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using OscVisualizer.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace OscVisualizer.Services
{
    internal class RetroCarStereo : IAudioVisualizer
    {
        private readonly Stopwatch _sw = Stopwatch.StartNew();

        public string VisualizerName
        {
            get => "Retro Car Stereo";
        }

        // ==== 設定パラメータ ====
        private float[] _levels = new float[NumBars];
        private float[] _peaks = new float[NumBars];

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

        public const int NumBars = 16;
        public const int SegmentsPerBar = 16;
        public float BarWidthScale { get; set; } = 0.8f;
        public float DepthScale { get; set; } = 0.8f;
        public float PerspectiveK { get; set; } = 1.2f;

        private XYTextRenderer renderer = new XYTextRenderer();

        // ==== 内部状態 ====

        public List<XYPoint> ProcessAudio(WasapiCapture capture, WaveInEventArgs e)
        {
            float[] wav = IAudioVisualizer.ConvertToWav1ch(capture, e, 32);

            // audioFrame は float[]（キャプチャ済み 48kHz の一部フレーム）
            UpdateLevels(capture, e, wav);

            List<XYPoint> points = new List<XYPoint>();

            var barXY = BuildBarsXY();
            points.Add(barXY);
            var peakXY = BuildPeakXY();
            points.Add(peakXY);

            float now = (float)_sw.Elapsed.TotalSeconds;
            points.Add(BuildCassetteFrame(now));

            string time = DateTime.Now.ToString("HH:mm:ss");
            var rect = renderer.CalcTextRect(time, 1.5f);
            var xy = renderer.BuildText(
                time,
                x: -rect.Width / 2,
                y: -1f + rect.Height,
                scale: 1.5f
                );
            points.Add(xy);

            var rectAM = renderer.CalcTextRect("AM", 1f);
            xy = renderer.BuildText("AM", -1 + 0.2, -0.1 - rectAM.Height, 1f);
            points.Add(xy);

            var rectFM = renderer.CalcTextRect("FM", 1f);
            xy = renderer.BuildText("FM", -1 + 0.2, -0.2 - rectFM.Height - rectAM.Height, 1f);
            points.Add(xy);

            var rectTAPE = renderer.CalcTextRect("TAPE", 1f);
            xy = renderer.BuildText("TAPE", 1 - rectTAPE.Width - 0.1, -0.1 - rectAM.Height, 1f).ToList();
            foreach (var p in xy)
                p.Intensity = 2;
            points.Add(xy);

            var rectCD = renderer.CalcTextRect("CD", 1f);
            xy = renderer.BuildText("CD", 1 - rectTAPE.Width - 0.1, -0.2 - rectFM.Height - rectAM.Height, 1f);
            points.Add(xy);

            return points;
        }

        IEnumerable<XYPoint> MakeRect(double cx, double cy, double w, double h)
        {
            double x0 = cx - w / 2;
            double x1 = cx + w / 2;
            double y0 = cy - h / 2;
            double y1 = cy + h / 2;

            yield return new XYPoint(x0, y0);
            yield return new XYPoint(x1, y0);

            yield return new XYPoint(x1, y0);
            yield return new XYPoint(x1, y1);

            yield return new XYPoint(x1, y1);
            yield return new XYPoint(x0, y1);

            yield return new XYPoint(x0, y1);
            yield return new XYPoint(x0, y0); // クローズ
        }

        IEnumerable<XYPoint> MakeCircle(double cx, double cy, double r, int segments)
        {
            for (int i = 0; i <= segments; i++)
            {
                {
                    double t = (double)i / segments * 2.0 * Math.PI;
                    double x = cx + r * Math.Cos(t);
                    double y = cy + r * Math.Sin(t);
                    yield return new XYPoint(x, y);
                }
                {
                    double t = (double)(i + 1) / segments * 2.0 * Math.PI;
                    double x = cx + r * Math.Cos(t);
                    double y = cy + r * Math.Sin(t);
                    yield return new XYPoint(x, y);
                }
            }
        }

        IEnumerable<XYPoint> MakeSpokes(double cx, double cy, double rInner, double rOuter, int spokeCount)
        {
            for (int i = 0; i < spokeCount; i++)
            {
                double t = (double)i / spokeCount * 2.0 * Math.PI;
                double x0 = cx + rInner * Math.Cos(t);
                double y0 = cy + rInner * Math.Sin(t);
                double x1 = cx + rOuter * Math.Cos(t);
                double y1 = cy + rOuter * Math.Sin(t);

                yield return new XYPoint(x0, y0);
                yield return new XYPoint(x1, y1);
                // ペンアップしたいならここで NaN ブレイクなど
            }
        }

        IEnumerable<XYPoint> RotateReel(IEnumerable<XYPoint> src, XYPoint center, double angle)
            => src.Select(p => XYPoint.RotateAround(p, center, angle));

        IEnumerable<XYPoint> BuildCassetteFrame(double time)
        {
            double tapeAngle = time * 0.3;
            double reelAngleL = time * 4.0;
            double reelAngleR = time * 4.0;

            var leftCenter = new XYPoint(-0.4, -0.35);
            var rightCenter = new XYPoint(0.4, -0.35);
            double reelRadius = 0.18;

            // 本体
            var body = MakeRect(0, 0 - 0.35, 1.6, 0.9);
            var window = MakeRect(0, 0 - 0.35, 0.9, 0.4);
            var label = MakeRect(0, 0.25 - 0.35, 1.2, 0.2);

            // リール
            var leftReelCircle = MakeCircle(leftCenter.X, leftCenter.Y, reelRadius, 8);
            var rightReelCircle = MakeCircle(rightCenter.X, rightCenter.Y, reelRadius, 8);
            var leftReelSpokes = MakeSpokes(leftCenter.X, leftCenter.Y, 0.05, reelRadius, 6);
            var rightReelSpokes = MakeSpokes(rightCenter.X, rightCenter.Y, 0.05, reelRadius, 6);

            // リール回転
            leftReelCircle = RotateReel(leftReelCircle, leftCenter, reelAngleL);
            rightReelCircle = RotateReel(rightReelCircle, rightCenter, reelAngleR);
            leftReelSpokes = RotateReel(leftReelSpokes, leftCenter, reelAngleL);
            rightReelSpokes = RotateReel(rightReelSpokes, rightCenter, reelAngleR);

            // ここで全部マージ
            var all = Enumerable.Empty<XYPoint>()
                .Concat(body)
                .Concat(window)
                .Concat(label)
                .Concat(leftReelCircle)
                .Concat(rightReelCircle)
                .Concat(leftReelSpokes)
                .Concat(rightReelSpokes);

            // カセット全体を回転させたいなら最後に適用
            //all = RotateAll(all, tapeAngle);

            double scale = 0.6; // 60% に縮小
            all = all.Select(p => XYPoint.ScaleAround(p, new XYPoint(0, -0.35), scale));

            return all;
        }

        // ==== レベル更新 ====
        public void UpdateLevels(WasapiCapture capture, WaveInEventArgs e, float[] audioFrame)
        {
            var fmt = capture.WaveFormat;
            int channels = fmt.Channels;
            int sampleRate = fmt.SampleRate;

            float[] wav = IAudioVisualizer.ConvertToWav1ch(capture, e);

            //ハイパスフィルタ
            prevX = 0;
            prevY = 0;
            for (int i = 0; i < wav.Length; i++)
                wav[i] = HighPass(wav[i]);

            //2. FFT（Math.NET が最も簡単
            int fftSize = 2048;
            if (wav.Length < fftSize)
            {
                // 足りない分はゼロパディング
                float[] padded = new float[fftSize];
                Array.Copy(wav, padded, wav.Length);
                wav = padded;
            }

            Complex[] fftBuffer = new Complex[fftSize];
            for (int i = 0; i < fftSize; i++)
            {
                fftBuffer[i] = new Complex(wav[i], 0);
            }

            Fourier.Forward(fftBuffer, FourierOptions.Matlab);

            //3. FFT → 周波数レベル（バー本数に集約）


            float f0 = 100f;                 // 最低周波数
            float f1 = 44100 / 2f;     // ナイキスト
            float binHz = sampleRate / (float)fftSize;

            for (int i = 0; i < NumBars; i++)
            {
                // ★ ログスケールで周波数帯を決める
                float fStart = f0 * MathF.Pow(f1 / f0, (float)i / NumBars);
                float fEnd = f0 * MathF.Pow(f1 / f0, (float)(i + 1) / NumBars);

                int start = (int)(fStart / binHz);
                int end = (int)(fEnd / binHz);

                // DC除去 & 範囲補正
                if (start < 1) start = 1;
                if (end > fftSize / 2) end = fftSize / 2;
                if (end <= start) end = start + 1;

                double sum = 0;
                for (int j = start; j < end; j++)
                    sum += fftBuffer[j].Magnitude;

                float avg = (float)(sum / (end - start));

                // Log10 圧縮（0〜1に収まる）
                float v = MathF.Log10(1 + avg * 0.25f);

                // ガンマ補正で小音量の反応を弱める
                v = MathF.Pow(v, 1.4f);   // ← ここを調整

                _levels[i] = v;
            }

            //レベル計算後にピークホールド処理を入れる
            float decay = 0.02f; // 減衰速度（0.005〜0.02 が使いやすい）
            for (int i = 0; i < NumBars; i++)
            {
                // 上昇は即時
                if (_levels[i] > _peaks[i])
                    _peaks[i] = _levels[i];
                else
                    _peaks[i] -= decay; // 下降はゆっくり

                // 下限クリップ
                if (_peaks[i] < 0)
                    _peaks[i] = 0;
            }

        }

        // ==== パース変換 ====
        private XYPoint ApplyPerspective(float x, float y, float z)
        {
            // 消失点（画面上のどこに収束するか）
            const float vx = 0f;   // 横方向の消失点（中央）
            const float vy = -1.0f;  // 地面の高さ（固定したい y）

            // z が大きいほど p が小さくなる → 消失点に寄る
            float p = 1f / (1f + PerspectiveK * (z + 1f) / 2f);

            // 地面 y = -1 を基準にして縮める
            float xp = vx + (x - vx) * p;
            float yp = vy + (y - vy) * p;

            return new XYPoint(xp, yp + 1.0f);
        }

        // ==== 1 セグメント長方形を XY ポリラインに変換 ====
        private void AddRect(List<XYPoint> list, float xL, float xR, float y0, float y1, bool skipUnderLine = false, float intensity = 1.0f)
        {
            float z = y1 * DepthScale;
            float z2 = y0 * DepthScale;

            var LB = ApplyPerspective(xL, y0, z2);
            LB.Intensity = intensity;
            var RB = ApplyPerspective(xR, y0, z2);
            RB.Intensity = intensity;
            var RT = ApplyPerspective(xR, y1, z);
            RT.Intensity = intensity;
            var LT = ApplyPerspective(xL, y1, z);
            LT.Intensity = intensity;
            if (!skipUnderLine)
            {
                list.Add(LB); list.Add(RB);
            }
            list.Add(RB); list.Add(RT);
            list.Add(RT); list.Add(LT);
            list.Add(LT); list.Add(LB);
        }

        // ==== セグメントバー生成 ====
        public List<XYPoint> BuildBarsXY()
        {
            var points = new List<XYPoint>();
            float barWidth = 2f / NumBars; // 1バーの幅（-1〜1）
            float segHeight = 1.25f / SegmentsPerBar;

            for (int i = 0; i < NumBars; i++)
            {
                float xc = (float)i / NumBars * 2f - 1f;
                float w = barWidth;

                float xL = xc;
                float xR = xc + w;

                float level = _levels[i];

                int activeSegs = (int)(level * SegmentsPerBar);

                for (int s = 0; s < activeSegs; s++)
                {
                    float y0 = s * segHeight;
                    float y1 = y0 + segHeight;
                    if (s > 10)
                        AddRect(points, xL, xR, y0 - 1f, y1 - 1f, s != 0, intensity: 2);
                    else
                        AddRect(points, xL, xR, y0 - 1f, y1 - 1f, s != 0);
                }
            }

            return points;
        }

        // ==== ピークバー（1 セグメント分） ====
        public List<XYPoint> BuildPeakXY()
        {
            var points = new List<XYPoint>();
            float barWidth = 2f / NumBars; // 1バーの幅（-1〜1）
            float segHeight = 1.25f / SegmentsPerBar;

            for (int i = 0; i < NumBars; i++)
            {
                float xc = (float)i / NumBars * 2f - 1f;
                float w = barWidth;

                float xL = xc;
                float xR = xc + w;

                float peak = _peaks[i];

                int segIndex = (int)(peak * SegmentsPerBar);
                float y0 = segIndex * segHeight;
                float y1 = y0 + segHeight;
                if(segIndex > 10)
                    AddRect(points, xL, xR, y0 - 1f, y1 - 1f, intensity: 2);
                else
                    AddRect(points, xL, xR, y0 - 1f, y1 - 1f);
            }

            return points;
        }

    }
}
