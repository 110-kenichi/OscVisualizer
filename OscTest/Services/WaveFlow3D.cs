using OscVisualizer.Models;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Text;
using System.Threading.Tasks;

namespace OscVisualizer.Services
{
    internal class WaveFlow3D : IAudioVisualizer
    {

        public string VisualizerName
        {
            get => "Wave Flow 3D";
        }

        const int MaxHistory = 10;
        List<float[]> history = new List<float[]>(MaxHistory);

        // 波形を追加
        void PushWaveform(float[] waveform)
        {
            history.Insert(0, waveform);
            if (history.Count > MaxHistory)
                history.RemoveAt(history.Count - 1);
        }


        public List<XYPoint> ProcessAudio(WasapiCapture capture, WaveInEventArgs e)
        {
            float[] wav = IAudioVisualizer.ConvertToWav1ch(capture, e, 32);

            PushWaveform(wav);

            List<XYPoint> points = new List<XYPoint>();

            for (int z = 0; z < history.Count; z++)
            {
                float[] wave = history[z];
                float depth = (float)z / MaxHistory;

                // パース係数
                float scale = 1f - depth * 0.6f;     // 奥ほど小さく
                float offsetY = depth * 0.8f - 0.4f; // 奥→手前の移動

                var intent = ((float)history.Count - (float)z) / (float)history.Count;
                intent *= intent;
                intent *= 2;

                for (int i = 0; i < wave.Length - 1; i++)
                {
                    // -1〜1 に正規化された波形を想定
                    {
                        float x = (i / (float)(wave.Length - 1)) * 2f - 1f;
                        float y = wave[i];

                        // パース投影
                        float xp = x * scale;
                        float yp = y * (scale * 3) + offsetY;

                        points.Add(new XYPoint(xp, yp, intent));
                    }
                    {
                        float x = ((i + 1) / (float)(wave.Length - 1)) * 2f - 1f;
                        float y = wave[i + 1];

                        // パース投影
                        float xp = x * scale;
                        float yp = y * (scale * 3) + offsetY;

                        points.Add(new XYPoint(xp, yp, intent));
                    }
                }
            }

            return points;
        }
    }
}
