using Avalonia;
using Avalonia.Media;
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

namespace OscVisualizer.Services
{
    internal class DiscoBall : IAudioVisualizer
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
            get => "Disco Ball";
        }

        public List<XYPoint> ProcessAudio(WasapiCapture capture, WaveInEventArgs e)
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

            var param = new DiscoBallParams
            {
                Radius = 1.8f,
                Perspective = 0.9f,
                YawSpeed = 0.7f,
                PitchSpeed = 0.25f,
                ZOffset = 2.0f,
                FrontBoost = 1.4f
            };

            int latLines = 15;   // 緯度線
            int lonLines = 20;   // 経度線

            var lines = GenerateDiscoBall3DLines(
                spectrum,
                latLines,
                lonLines,
                t,
                param
            );

            List<XYPoint> points = new List<XYPoint>();
            foreach (var line in lines)
            {
                points.Add(new XYPoint(line.Start.X, line.Start.Y, line.Brightness));
                points.Add(new XYPoint(line.End.X, line.End.Y, line.Brightness));
            }
            return points;
        }

        public class DiscoBallParams
        {
            public float Radius = 0.9f;        // 球の半径（XY空間）
            public float Perspective = 0.8f;   // パース強度（zが大きいほど縮む）
            public float YawSpeed = 0.6f;      // Y軸回転速度
            public float PitchSpeed = 0.2f;    // X軸回転速度
            public float ZOffset = 1.5f;       // 視点からの距離
            public float FrontBoost = 1.2f;    // 手前側の明るさ補正
        }

        public struct XYLineSegment
        {
            public Vector2 Start;
            public Vector2 End;
            public float Brightness;
        }

        public static XYLineSegment[] GenerateDiscoBall3DLines(
            float[] spectrum,          // 0〜1 正規化スペクトラム
            int latLines,              // 緯度方向の線数
            int lonLines,              // 経度方向の線数
            float timeSeconds,         // 経過時間
            DiscoBallParams p)
        {
            if (spectrum == null || spectrum.Length == 0)
                return Array.Empty<XYLineSegment>();

            var lines = new List<XYLineSegment>(latLines * lonLines * 2);

            // 回転角
            float yaw = timeSeconds * p.YawSpeed;   // Y軸
            float pitch = timeSeconds * p.PitchSpeed; // X軸

            float sinY = MathF.Sin(yaw);
            float cosY = MathF.Cos(yaw);
            float sinP = MathF.Sin(pitch);
            float cosP = MathF.Cos(pitch);

            // 簡易スペクトラムインデックス
            int specLen = spectrum.Length;

            // --- 緯度線（水平リング） ---
            for (int i = 1; i < latLines; i++)
            {
                // θ: -π/2〜π/2（南極〜北極）
                float tLat = (float)i / latLines;
                float theta = (tLat - 0.5f) * MathF.PI; // -π/2〜+π/2

                float y = p.Radius * MathF.Sin(theta);
                float r = p.Radius * MathF.Cos(theta); // この緯度での半径

                for (int j = 0; j < lonLines; j++)
                {
                    float t0 = (float)j / lonLines;
                    float t1 = (float)(j + 1) / lonLines;

                    float phi0 = t0 * 2f * MathF.PI;
                    float phi1 = t1 * 2f * MathF.PI;

                    // 球面上の2点（3D）
                    var p0 = new Vector3(
                        r * MathF.Cos(phi0),
                        y,
                        r * MathF.Sin(phi0)
                    );
                    var p1 = new Vector3(
                        r * MathF.Cos(phi1),
                        y,
                        r * MathF.Sin(phi1)
                    );

                    AddProjectedSegment(lines, p0, p1, spectrum, specLen, p,
                        sinY, cosY, sinP, cosP);
                }
            }

            // --- 経度線（縦のライン） ---
            for (int j = 0; j < lonLines; j++)
            {
                float tLon = (float)j / lonLines;
                float phi = tLon * 2f * MathF.PI;

                Vector3? prev = null;

                for (int i = 0; i <= latLines; i++)
                {
                    float tLat = (float)i / latLines;
                    float theta = (tLat - 0.5f) * MathF.PI;

                    float y = p.Radius * MathF.Sin(theta);
                    float r = p.Radius * MathF.Cos(theta);

                    var pos = new Vector3(
                        r * MathF.Cos(phi),
                        y,
                        r * MathF.Sin(phi)
                    );

                    if (prev.HasValue)
                    {
                        AddProjectedSegment(lines, prev.Value, pos, spectrum, specLen, p,
                            sinY, cosY, sinP, cosP);
                    }

                    prev = pos;
                }
            }

            return lines.ToArray();
        }

        private static void AddProjectedSegment(
            List<XYLineSegment> lines,
            Vector3 p0,
            Vector3 p1,
            float[] spectrum,
            int specLen,
            DiscoBallParams p,
            float sinY, float cosY,
            float sinP, float cosP)
        {
            // --- 3D回転（Yaw→Pitch） ---
            p0 = RotateYawPitch(p0, sinY, cosY, sinP, cosP);
            p1 = RotateYawPitch(p1, sinY, cosY, sinP, cosP);

            // 視点からの距離オフセット
            p0.Z += p.ZOffset;
            p1.Z += p.ZOffset;

            // 背面はスキップ（Zが小さすぎると発散する）
            if (p0.Z >= 1.5f || p1.Z >= 1.5f)
                return;

            // --- パース付き投影 ---
            Vector2 s0 = ProjectToXY(p0, p.Perspective);
            Vector2 s1 = ProjectToXY(p1, p.Perspective);

            // --- 明るさ計算 ---
            // zが小さい（手前）ほど明るく、スペクトラムで変調
            float zAvg = (p0.Z + p1.Z) / 2;
            float depthFactor = 4 * (1.5f - zAvg); // 手前ほど大きい

            // 適当なスペクトラムインデックス（x位置から決める例）
            float bandT = ((s0.X + s1.X) / 2f + 1f) / 2f; // -1〜1 → 0〜1
            bandT = Math.Clamp(bandT, 0f, 1f);
            int bandIdx = (int)(bandT * (specLen - 1));
            float mag = spectrum[bandIdx];

            float brightness = mag * depthFactor * p.FrontBoost;
            brightness = Math.Clamp(brightness, 0.1f, 3f);

            //if (brightness <= 0.01f)
            //    return;

            lines.Add(new XYLineSegment
            {
                Start = s0,
                End = s1,
                Brightness = brightness
            });
        }

        private static Vector3 RotateYawPitch(Vector3 v, float sinY, float cosY, float sinP, float cosP)
        {
            // Yaw (Y軸回転)
            float x1 = cosY * v.X + sinY * v.Z;
            float z1 = -sinY * v.X + cosY * v.Z;
            float y1 = v.Y;

            // Pitch (X軸回転)
            float y2 = cosP * y1 - sinP * z1;
            float z2 = sinP * y1 + cosP * z1;

            return new Vector3(x1, y2, z2);
        }

        private static Vector2 ProjectToXY(Vector3 v, float perspective)
        {
            // 簡易パース: x,y を (1 + perspective * z) で割る
            float denom = 1f + perspective * v.Z;
            float x = v.X / denom;
            float y = v.Y / denom;
            return new Vector2(x, y);
        }
    }
}
