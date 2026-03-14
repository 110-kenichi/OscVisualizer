using Avalonia;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace OscVisualizer.Services
{
    public interface IAudioVisualizer
    {
        static float[] ConvertToWav1ch(WasapiCapture capture, WaveInEventArgs e, int cut = 0)
        {
            var fmt = capture.WaveFormat;

            int channels = fmt.Channels;
            int sampleRate = fmt.SampleRate;
            int bits = fmt.BitsPerSample;
            bool isFloat = fmt.Encoding == WaveFormatEncoding.IeeeFloat;

            int bytesPerSample = bits / 8;
            int bytesPerFrame = channels * bytesPerSample;
            int frameCount = e.BytesRecorded / bytesPerFrame;

            // 汎用 float32 バッファ
            //float[][] ch = new float[channels][];
            //for (int c = 0; c < channels; c++)
            //    ch[c] = new float[frameCount];

            if(cut > 0)
                frameCount = Math.Min(cut, frameCount);

            float[] wav = new float[frameCount];

            for (int i = 0; i < frameCount; i++)
            {
                int baseOffset = i * bytesPerFrame;

                float val = 0;
                for (int c = 0; c < channels; c++)
                {
                    int offset = baseOffset + c * bytesPerSample;

                    float value;

                    if (isFloat)
                    {
                        value = BitConverter.ToSingle(e.Buffer, offset);
                    }
                    else
                    {
                        // PCM → float32
                        switch (bits)
                        {
                            case 16:
                                value = BitConverter.ToInt16(e.Buffer, offset) / 32768f;
                                break;
                            case 24:
                                int v24 = (e.Buffer[offset + 2] << 24) |
                                          (e.Buffer[offset + 1] << 16) |
                                          (e.Buffer[offset + 0] << 8);
                                value = v24 / 2147483648f;
                                break;
                            case 32:
                                value = BitConverter.ToInt32(e.Buffer, offset) / 2147483648f;
                                break;
                            default:
                                value = 0;
                                break;
                        }
                    }
                    val += value;
                }
                wav[i] = val / (float)channels;
            }
            return wav;
        }

        static float[] Downsample8xAverageAVX2(float[] src)
        {
            if (!Avx.IsSupported)
                throw new PlatformNotSupportedException("AVX is not supported.");

            int outLen = src.Length / 8;
            float[] dst = new float[outLen];

            int o = 0;
            int i = 0;

            for (; o < outLen; o++, i += 8)
            {
                // ref float を取得
                ref float p = ref src[i];

                // 256bitロード（byte* は不要）
                unsafe
                {
                    Vector256<float> v = Avx.LoadVector256((float*)Unsafe.AsPointer(ref p));

                    // ---- 水平加算 ----
                    Vector256<float> sum1 = Avx.HorizontalAdd(v, v);
                    Vector256<float> sum2 = Avx.HorizontalAdd(sum1, sum1);

                    Vector128<float> low = sum2.GetLower();
                    Vector128<float> high = sum2.GetUpper();
                    Vector128<float> sum128 = Sse.Add(low, high);

                    float sum = sum128.ToScalar();
                    dst[o] = sum * 0.125f;
                }
            }

            return dst;
        }

        /// <summary>
        /// Gets the name of the visualizer currently in use.
        /// </summary>
        /// <remarks>This property provides the name of the visualizer, which can be useful for identifying
        /// the hardware capabilities of the system. It is read-only and reflects the visualizer name as reported by the
        /// operating system.</remarks>
        string VisualizerName
        {
            get;
        }

        /// <summary>
        /// Processes audio data captured from the specified audio input device and returns a collection of points
        /// representing the audio waveform.
        /// </summary>
        /// <remarks>This method is intended for real-time audio processing scenarios and may be called
        /// frequently during audio capture sessions. Ensure that the audio input device is properly initialized before
        /// invoking this method.</remarks>
        /// <param name="sender">The audio capture device that provides the audio data to process. Must not be null.</param>
        /// <param name="e">The event data containing the captured audio buffer and related information. Must not be null.</param>
        /// <returns>A list of Point objects that represent the processed audio waveform data. The list may be empty if no audio
        /// data is available.</returns>
        List<Point> ProcessAudio(WasapiCapture sender, WaveInEventArgs e);
    }
}
