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
    // 参考: https://note.com/cat_code/n/n455d983d78db
    internal class Tunder : IAudioVisualizer
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
        private bool _wasKickActive;
        private int _activeStrike;
        private float _previousKickLevel;
        private readonly List<LightningStrike> _lightningStrikes = new();

        private sealed class LightningStrike
        {
            public int Seed { get; init; }
            public float OriginX { get; init; }
            public float KickLevel { get; init; }
            public float Progress { get; set; }
        }

        public float FallDurationSeconds { get; set; } = 0.125f;

        public string VisualizerName
        {
            get => "Tunder";
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

        private static float Noise(int seed)
        {
            uint value = (uint)seed;
            value ^= value >> 16;
            value *= 0x7feb352d;
            value ^= value >> 15;
            value *= 0x846ca68b;
            value ^= value >> 16;
            return value / (float)uint.MaxValue * 2f - 1f;
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

            // キックを雷の落下トリガーにする。無音時はレーザーを出さない。
            float kickLevel = Math.Clamp(kick / 20f, 0f, 1f);
            float floatingX = MathF.Sin(time * 0.7f) * 0.35f
                + MathF.Sin(time * 1.9f) * 0.08f;
            bool kickActive = kick >= 0.2f;
            bool kickAttack = kickLevel > _previousKickLevel + 0.015f;
            if (kickActive && (!_wasKickActive || kickAttack))
            {
                _activeStrike++;
                _lightningStrikes.Add(new LightningStrike
                {
                    Seed = _activeStrike,
                    OriginX = floatingX,
                    KickLevel = kickLevel
                });

                if (_lightningStrikes.Count > 4)
                    _lightningStrikes.RemoveAt(0);
            }
            _wasKickActive = kickActive;
            _previousKickLevel = kickLevel;

            if (_lightningStrikes.Count > 0)
            {
                float snareLevel = Math.Clamp(snare / 2f, 0f, 1f);
                float hatLevel = Math.Clamp(hat / 1.5f, 0f, 1f);

                float fallDuration = Math.Max(FallDurationSeconds, 0.01f);
                for (int strikeIndex = _lightningStrikes.Count - 1; strikeIndex >= 0; strikeIndex--)
                {
                    LightningStrike activeStrike = _lightningStrikes[strikeIndex];
                    activeStrike.Progress = Math.Clamp(activeStrike.Progress + deltaTime / fallDuration, 0f, 1f);
                    int strike = activeStrike.Seed;
                    float originX = activeStrike.OriginX;
                    float bottomY = -0.95f;
                    int sections = 18;

                    // 音の強さを 0～2 の XY intensity に変換する。
                    float soundLevel = Math.Clamp(MathF.Max(activeStrike.KickLevel, MathF.Max(snareLevel, hatLevel)), 0f, 1f);
                    float intensity = Math.Clamp(0.15f + soundLevel * 1.85f, 0f, 2f);
                    float boltWidth = 1f + activeStrike.KickLevel * 2.5f;

                Vector2[] main = new Vector2[sections + 1];
                main[0] = new Vector2(originX, 0.95f);
                float mainDirection = Noise(strike * 31) * 0.35f;
                for (int i = 1; i <= sections; i++)
                {
                    float progress = i / (float)sections;
                    float y = 0.95f + (bottomY - 0.95f) * progress;
                    float turn = Noise(strike * 97 + i * 43) * (0.22f + snareLevel * 0.38f)
                        + Noise(strike * 53 + i * 29) * hatLevel * 0.12f;
                    mainDirection = Math.Clamp(mainDirection + turn, -1f, 1f);
                    float stepWidth = (0.04f + snareLevel * 0.06f) * (1f - progress * 0.3f);
                    float x = Math.Clamp(main[i - 1].X + mainDirection * stepWidth, -0.92f, 0.92f);
                    main[i] = new Vector2(x, y);
                }

                // 最後の線分を地面へ接続し、すべての稲妻が画面下端まで到達するようにする。
                main[sections] = new Vector2(main[sections].X, bottomY);

                float visibleSections = activeStrike.Progress * sections;
                int completedSections = Math.Min((int)visibleSections, sections);
                for (int i = 1; i <= completedSections; i++)
                {
                    AddLine(seg, main[i - 1], main[i], intensity, boltWidth);
                }

                if (completedSections < sections)
                {
                    float partial = visibleSections - completedSections;
                    if (partial > 0f)
                    {
                        Vector2 end = Vector2.Lerp(main[completedSections], main[completedSections + 1], partial);
                        AddLine(seg, main[completedSections], end, intensity, boltWidth);
                    }
                }

                // 主線から確率的に生まれる、寿命付きの枝雷。
                for (int i = 3; i < completedSections - 2; i++)
                {
                    float branchChance = 0.08f + hatLevel * 0.18f + snareLevel * 0.08f;
                    if (Noise(strike * 13 + i * 71) > branchChance * 2f - 1f)
                        continue;

                    Vector2 current = main[i];
                    float direction = Noise(strike * 19 + i * 29) < 0f ? -0.8f : 0.8f;
                    int life = 3 + (int)((Noise(strike * 23 + i * 37) + 1f) * 2f);
                    float branchIntensity = intensity * 0.62f;

                    while (life-- > 0)
                    {
                        direction = Math.Clamp(
                            direction + Noise(strike * 41 + i * 11 + life) * (0.4f + hatLevel * 0.25f),
                            -1f,
                            1f);
                        float length = 0.065f + hatLevel * 0.045f;
                        Vector2 next = new(
                            Math.Clamp(current.X + direction * length, -0.96f, 0.96f),
                            current.Y - length * (0.7f + MathF.Abs(Noise(strike * 53 + i * 7 + life))));
                        AddLine(seg, current, next, branchIntensity, boltWidth * 0.8f);
                        current = next;
                        branchIntensity *= 0.75f;
                    }
                }

                    if (activeStrike.Progress >= 1f)
                        _lightningStrikes.RemoveAt(strikeIndex);
                }
            }
            else
            {
                seg.Add(new XYPoint(floatingX - 0.01f, 1));
                seg.Add(new XYPoint(floatingX + 0.01f, 1));

                int spark = (int)(time * 8f);
                if (Noise(spark * 67) > 0.72f)
                {
                    int sparkCount = 1 + (int)((Noise(spark * 79) + 1f) * 1.5f);
                    for (int sparkIndex = 0; sparkIndex < sparkCount; sparkIndex++)
                    {
                        float direction = Noise(spark * 31 + sparkIndex * 17) < 0f ? -1f : 1f;
                        float sparkIntensity = Math.Clamp(0.35f + hat / 1.5f * 0.4f, 0f, 2f);
                        Vector2 current = new(floatingX, 0.98f);

                        for (int i = 0; i < 3; i++)
                        {
                            direction = Math.Clamp(
                                direction + Noise(spark * 43 + sparkIndex * 11 + i) * 0.5f,
                                -1f,
                                1f);
                            Vector2 next = new(
                                Math.Clamp(current.X + direction * 0.035f, -0.96f, 0.96f),
                                current.Y - 0.025f);
                            AddLine(seg, current, next, sparkIntensity);
                            current = next;
                            sparkIntensity *= 0.7f;
                        }
                    }
                }
            }

            return seg;
        }

        private static void AddLine(List<XYPoint> points, Vector2 start, Vector2 end, float intensity, float widthScale = 1f)
        {
            float lineIntensity = Math.Clamp(intensity, 0f, 2f);
            points.Add(new XYPoint(start.X, start.Y, lineIntensity));
            points.Add(new XYPoint(end.X, end.Y, lineIntensity));

            float width = (0.006f + lineIntensity * 0.003f) * widthScale;
            float edgeIntensity = lineIntensity * 0.35f;
            points.Add(new XYPoint(start.X - width, start.Y, edgeIntensity));
            points.Add(new XYPoint(end.X - width, end.Y, edgeIntensity));
            points.Add(new XYPoint(start.X + width, start.Y, edgeIntensity));
            points.Add(new XYPoint(end.X + width, end.Y, edgeIntensity));
        }
    }
}
