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
    internal class TestSignal : IAudioVisualizer
    {

        public string VisualizerName
        {
            get => "Test Signal";
        }

        public List<XYPoint> ProcessAudio(WasapiCapture capture, WaveInEventArgs e)
        {
            int inputSampleRate = capture.WaveFormat.SampleRate;

            List<XYPoint> points = new();

            points.Add(new XYPoint(0, 0));
            points.Add(new XYPoint(1, -1));

            points.Add(new XYPoint(1, -1));
            points.Add(new XYPoint(0, 0));
            return points;
        }

    }
}
