using OscVisualizer.Models;
using MathNet.Numerics;
using MathNet.Numerics.IntegralTransforms;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace OscVisualizer.Services
{
    internal class TestPattern : IAudioVisualizer
    {
        private float prevX = 0;
        private float prevY = 0;
        private float R = 0.995f; // カットオフ調整

        private XYTextRenderer txtrender = new XYTextRenderer();

        private float HighPass(float x)
        {
            float y = x - prevX + R * prevY;
            prevX = x;
            prevY = y;
            return y;
        }

        public string VisualizerName
        {
            get => "Test Pattern";
        }

        public List<XYPoint> ProcessAudio(WasapiCapture capture, WaveInEventArgs e)
        {
            int inputSampleRate = capture.WaveFormat.SampleRate;

            List<XYPoint> points = new();

            //クロスハッチ
            for (float x = -1; x <= 1; x += 0.25f)
                points.AddRange(IAudioVisualizer.CreateSegment(x, -1, x, 1, 1));
            for (float y = -1; y <= 1; y += 0.25f)
                points.AddRange(IAudioVisualizer.CreateSegment(-1, y, 1, y, 1));

            //円
            points.AddRange(IAudioVisualizer.CreateCircle(0, 0, 1, 1, 24));

            //文字
            var rect = txtrender.CalcTextRect("Test Pattern", 0.75);
            points.AddRange(txtrender.BuildText("Test Pattern", -rect.Width / 2, -rect.Height / 2, 0.75));

            //バー
            for (float x = -0.5f; x <= 0.5f; x += 0.01f)
                points.AddRange(IAudioVisualizer.CreateSegment(x, 0.75f - 0.25f / 4, x, 0.5f + 0.25f / 4, (x + 0.5f) * 2));
            for (float y = -0.5f; y <= 0.5f; y += 0.01f)
                points.AddRange(IAudioVisualizer.CreateSegment(0.75f - 0.25f / 4, y, 0.5f + 0.25f / 4, y, 2 - (y + 0.5f) * 2));

            return points;
        }

    }
}
