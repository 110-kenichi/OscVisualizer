using Avalonia;
using MathNet.Numerics;
using MathNet.Numerics.IntegralTransforms;
using NAudio.CoreAudioApi;
using NAudio.Dsp;
using NAudio.Wave;
using OpenTK.Windowing.Common.Input;
using OscVisualizer.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using static System.Net.Mime.MediaTypeNames;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.TaskbarClock;

namespace OscVisualizer.Services
{
    internal class Matrix3D : IAudioVisualizer
    {
        private readonly Stopwatch _sw = Stopwatch.StartNew();

        private static readonly Random random = new Random(DateTime.Now.Millisecond);

        public string VisualizerName
        {
            get => "Matrix 3D";
        }

        private float prevX = 0;
        private float prevY = 0;
        private float R = 0.995f; // カットオフ調整

        private List<RaindropText> _raindropText = new();

        /// <summary>
        /// 
        /// </summary>
        public Matrix3D()
        {
            for (int i = 0; i < 10; i++)
            {
                RaindropText newText = new RaindropText();
                initRaindropText(newText);
                _raindropText.Add(newText);
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

        public float GetDeltaTime()
        {
            double now = _sw.Elapsed.TotalSeconds;
            float delta = (float)(now - _lastTime);
            _lastTime = now;

            return delta;
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

            float t = (float)_sw.Elapsed.TotalSeconds;

            List<XYPoint> points = GenerateMatrixWaveform(spectrum, t, GetDeltaTime(), inputSampleRate);
            return points;
        }

        private float currentAngleX = 0f;  // 現在の角度
        private float targetAngleX = 0f;   // 目標の角度
        private float currentAngleY = 0f;  // 現在の角度
        private float targetAngleY = 0f;   // 目標の角度
        private float currentAngleZ = 0f;  // 現在の角度
        private float targetAngleZ = 0f;   // 目標の角度

        // タイマー（約30〜60fps）で毎回呼び出す処理
        private void UpdateRotation()
        {
            // 1. 目標角度に近づいたら、新しい目標をランダムに設定（-10〜10度）
            if (Math.Abs(currentAngleX - targetAngleX) < 0.1f)
                targetAngleX = (float)(random.NextDouble() * 20.0 - 10.0);
            if (Math.Abs(currentAngleY - targetAngleY) < 0.1f)
                targetAngleY = (float)(random.NextDouble() * 20.0 - 10.0);
            if (Math.Abs(currentAngleZ - targetAngleZ) < 0.1f)
                targetAngleZ = (float)(random.NextDouble() * 20.0 - 10.0);

            // 2. スムーズに補間する (線形補間の例)
            // 0.05f の値を変えると、追従するスピードが変わります
            float lerpSpeed = 0.02f;
            currentAngleX += (targetAngleX - currentAngleX) * lerpSpeed;
            currentAngleY += (targetAngleY - currentAngleY) * lerpSpeed;
            currentAngleZ += (targetAngleZ - currentAngleZ) * lerpSpeed;

            // 3. この currentAngle を描画時の回転行列に適用する
            // (例: graphics.RotateTransform(currentAngle); )
        }

        public List<XYPoint> GenerateMatrixWaveform(float[] fft, float time, float deltaTime, int sampleRate)
        {
            var projected = new List<XYPoint>();

            float kick = MathF.Min(IAudioVisualizer.GetBand(fft, 50, 100, sampleRate), 20f);
            //float snare = MathF.Min(IAudioVisualizer.GetBand(fft, 1500, 3000, sampleRate), 2f);
            //float hat = MathF.Min(IAudioVisualizer.GetBand(fft, 6000, 12000, sampleRate), 1.5f);
            float scale = 1f + kick / 20f;

            UpdateRotation();

            // 3Dパラメータ
            //float radius = 2.5f; // カメラの回転半径
            float camX = 0.0f;   // カメラの
            float camY = 0.0f;   // カメラの高さ
            float camZ = 2.5f;   // カメラの
            float d = 8.0f;      // 投影面までの距離

            float thetaX = (float)(currentAngleX); // 回転角（速度調整可）
            float thetaY = (float)(currentAngleY); // 回転角（速度調整可）
            float thetaZ = (float)(currentAngleZ); // 回転角（速度調整可）
            thetaX = (float)(thetaX * Math.PI / 180); // X軸 (Pitch)
            thetaY = (float)(thetaY * Math.PI / 180); // Y軸 (Yaw)
            thetaZ = (float)(thetaZ * Math.PI / 180); // Z軸 (Roll)

            // カメラ位置
            Quaternion rotation = Quaternion.CreateFromYawPitchRoll(thetaY, thetaX, thetaZ);

            Vector3 rotPos = new Vector3(camX, camY, camZ);
            Vector3 camPos = Vector3.Transform(rotPos, rotation);

            // カメラが原点(0,0,0)を見る
            Vector3 camTarget = Vector3.Zero;
            Vector3 camUp = Vector3.UnitY;
            camUp = Vector3.Transform(camUp, rotation);

            // ビュー行列（カメラ座標系への変換）
            var view = CreateLookAt(camPos, camTarget, camUp);

            // 文字アウトラインをXY平面(Z=0)に配置
            foreach (var rt in _raindropText)
            {
                var basePoints = TextToVectorXYPoints(rt, 0.01f, 0.05f);
                var transformedPoints = new List<XYPoint>();
                bool cameraOut = false;
                for (int i = 0; i < basePoints.Count; i += 2)
                {
                    // 線分の2点
                    var p1 = new Vector3((float)basePoints[i].X, (float)basePoints[i].Y, rt.Z);
                    var p2 = new Vector3((float)basePoints[i + 1].X, (float)basePoints[i + 1].Y, rt.Z);

                    // カメラ座標系に変換
                    var v1 = Vector3.Transform(p1, view);
                    var v2 = Vector3.Transform(p2, view);

                    // パースペクティブ投影
                    var s1 = ProjectToScreen(v1, d);
                    var s2 = ProjectToScreen(v2, d);
                    if (s1 != null && s2 != null)
                    {
                        transformedPoints.Add(new XYPoint(s1.Value.X, s1.Value.Y, scale * basePoints[i].Intensity / 2));
                        transformedPoints.Add(new XYPoint(s2.Value.X, s2.Value.Y, scale * basePoints[i + 1].Intensity / 2));
                    }
                    else
                    {
                        cameraOut = true;
                    }
                }
                cameraOut |= transformedPoints.TrueForAll(p => p.X < -1 || p.X > 1 || p.Y < -1);
                if (cameraOut)
                {
                    initRaindropText(rt);
                }
                else
                {
                    projected.AddRange(transformedPoints);
                }
            }
            return projected;
        }

        private static void initRaindropText(RaindropText newText)
        {
            newText.X = -100f + (random.NextSingle() * 200f);
            newText.Y = -250f + (random.NextSingle() * 250f);
            newText.Z = 10f - (random.NextSingle() * 10f);
            newText.Counter = 0;
            newText.OffsetY = 0;
            newText.Text = GenerateRandomString(10);
        }

        // カメラビュー行列生成
        private static Matrix4x4 CreateLookAt(Vector3 eye, Vector3 target, Vector3 up)
        {
            var z = Vector3.Normalize(eye - target);
            var x = Vector3.Normalize(Vector3.Cross(up, z));
            var y = Vector3.Cross(z, x);

            return new Matrix4x4(
                x.X, y.X, z.X, 0,
                x.Y, y.Y, z.Y, 0,
                x.Z, y.Z, z.Z, 0,
                -Vector3.Dot(x, eye), -Vector3.Dot(y, eye), -Vector3.Dot(z, eye), 1
            );
        }

        // パースペクティブ投影
        private static Vector2? ProjectToScreen(Vector3 v, float d)
        {
            float z = v.Z + d;

            // カメラのすぐ手前（例えば 0.1px 以上の距離）にない場合は描画しない
            if (z <= 0.1f)
                return null;

            return new Vector2(v.X * d / z, v.Y * d / z);
        }

        private List<XYPoint> TextToVectorXYPoints(RaindropText text, float scale = 1.0f, float spacing = 0.1f)
        {
            var points = new List<XYPoint>();

            using var font = new System.Drawing.Font("Arial", 9, FontStyle.Regular, GraphicsUnit.Pixel);
            using var path = new GraphicsPath();
            float currentY = text.Y + (text.OffsetY * font.Size);
            float firstY = currentY;
            foreach (char c in text.Text)
            {
                // 1文字ずつ、座標を指定してPathに追加
                SizeF size = TextRenderer.MeasureText(c.ToString(), font);
                path.AddString(
                    c.ToString(),
                    font.FontFamily,
                    (int)font.Style,
                    font.Size,
                    new PointF(text.X + (-size.Width / 2), currentY),
                    StringFormat.GenericDefault
                );
                // 次の文字のY座標を更新
                currentY += font.Size;
            }
            text.Counter++;
            if (text.Counter > 0)
            {
                text.Counter = 0;
                text.OffsetY++;
                text.Text = text.Text.Substring(1, text.Text.Length - 1) + GenerateRandomString(1);
            }

            var pts = path.PathPoints;
            var types = path.PathTypes;

            // サブパスごとに分割
            List<List<PointF>> subpaths = new List<List<PointF>>();
            List<PointF> current = new List<PointF>();
            for (int i = 0; i < pts.Length; i++)
            {
                byte type = types[i];
                byte pointType = (byte)(type & 0x7);
                bool isCloseSubpath = (type & 0x80) != 0;
                if (pointType == 0) // Start
                {
                    if (current.Count > 1)
                        subpaths.Add(current);
                    current = new List<PointF> { pts[i] };
                }
                else if (pointType == 1 || pointType == 3) // Line or CloseSubpath
                {
                    current.Add(pts[i]);
                }
                if (isCloseSubpath && current.Count > 1)
                {
                    // 閉じる
                    current.Add(current[0]);
                    subpaths.Add(current);
                    current = new List<PointF>();
                }
            }
            if (current.Count > 1)
                subpaths.Add(current);

            text.Z -= 0.4f;

            // 各サブパスにRDP適用
            float epsilon = (text.Z < 0 ? 0 : text.Z / 5f) + 0.1f; // 誤差許容値（調整可）
            foreach (var sub in subpaths)
            {
                List<PointF> simp;
                if (sub.Count < 10)
                {
                    simp = sub;
                }
                else
                {
                    simp = IAudioVisualizer.RdpSimplify(sub, epsilon);
                }
                for (int i = 1; i < simp.Count; i++)
                {
                    float x1 = simp[i - 1].X * scale;
                    float y1 = -simp[i - 1].Y * scale;
                    float x2 = simp[i].X * scale;
                    float y2 = -simp[i].Y * scale;
                    points.Add(new XYPoint(x1, y1, 1.5 * (simp[i - 1].Y - firstY) / (currentY - firstY)));
                    points.Add(new XYPoint(x2, y2, 1.5 * (simp[i].Y - firstY) / (currentY - firstY)));
                }
            }
            return points;
        }

        public static string GenerateRandomString(int length)
        {
            // 使用する文字のセット（英数字＋カタカナ）
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789" +
                                 "アイウエオカキクケコサシスセソタチツテトナニヌネノハヒフヘホマミムメモヤユヨラリルレロワヲン";

            // ランダムに文字を選択して結合
            return new string(Enumerable.Repeat(chars, length)
                .Select(s => s[random.Next(s.Length)]).ToArray());
        }

        private class RaindropText
        {
            public float X
            {
                get; set;
            }

            public float Y
            {
                get; set;
            }

            public float Z
            {
                get; set;
            }

            public float OffsetY
            {
                get; set;
            } = 0f;

            public string Text
            {
                get; set;
            } = GenerateRandomString(10);

            public int Counter
            {
                get; set;
            }

        }

    }
}
