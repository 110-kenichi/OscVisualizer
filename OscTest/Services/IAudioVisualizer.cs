using Avalonia.Controls;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using OscVisualizer.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace OscVisualizer.Services
{
    public interface IAudioVisualizer
    {
        /// <summary>
        /// Converts audio data from a WASAPI capture source to a single-channel array of 32-bit floating-point samples
        /// in WAV format.
        /// </summary>
        /// <remarks>If the input audio contains multiple channels, the method averages the channels to
        /// produce a single-channel output. The method supports both PCM and IEEE float input formats, converting all
        /// samples to normalized floating-point values in the range [-1.0, 1.0] as appropriate for WAV audio
        /// processing.</remarks>
        /// <param name="capture">The WASAPI capture instance that provides the audio format and source data.</param>
        /// <param name="e">The event arguments containing the recorded audio buffer and the number of bytes recorded.</param>
        /// <param name="cut">The maximum number of audio frames to convert. If set to 0 or less, all available frames are converted.</param>
        /// <returns>An array of 32-bit floating-point values representing the audio samples in single-channel WAV format.</returns>
        static float[] ConvertToWav1ch(WasapiCapture capture, WaveInEventArgs e, int cut = 0)
        {
            var fmt = capture.WaveFormat;

            int channels = fmt.Channels;
            int inputSampleRate = fmt.SampleRate;
            int bits = fmt.BitsPerSample;
            bool isFloat = fmt.Encoding == WaveFormatEncoding.IeeeFloat;

            int bytesPerSample = bits / 8;
            int bytesPerFrame = channels * bytesPerSample;
            int frameCount = e.BytesRecorded / bytesPerFrame;

            // 汎用 float32 バッファ
            //float[][] ch = new float[channels][];
            //for (int c = 0; c < channels; c++)
            //    ch[c] = new float[frameCount];

            if (cut > 0)
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

        /// <summary>
        /// Calculates the average of each group of eight consecutive elements in the specified array using AVX2 vector
        /// instructions.
        /// </summary>
        /// <remarks>This method uses AVX2 instructions for efficient vectorized processing. To ensure
        /// correct results, the input array length must be a multiple of eight. Using this method on platforms without
        /// AVX support will result in an exception.</remarks>
        /// <param name="src">The source array of single-precision floating-point values to be downsampled. The length of the array must
        /// be a multiple of eight.</param>
        /// <returns>An array of single-precision floating-point values containing the computed averages, with a length equal to
        /// one-eighth of the source array.</returns>
        /// <exception cref="PlatformNotSupportedException">Thrown if the AVX instruction set is not supported on the current platform.</exception>
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
        /// Downsamples the input array by averaging each group of four consecutive elements, returning a new array with
        /// one-fourth the number of elements.
        /// </summary>
        /// <remarks>This method uses SIMD (Single Instruction, Multiple Data) operations to improve
        /// performance by processing multiple elements simultaneously. If the input array length is not a multiple of
        /// eight, the remaining elements are processed individually.</remarks>
        /// <param name="src">The input array of single-precision floating-point values to be downsampled. The length of the array must be
        /// a multiple of four.</param>
        /// <returns>A new array of single-precision floating-point values containing the averaged results. The length of the
        /// returned array is one-fourth the length of the input array.</returns>
        static float[] Downsample4xAverage(float[] src)
        {
            int outLen = src.Length / 4;
            float[] dst = new float[outLen];

            int i = 0;
            int o = 0;

            // SIMD で 8要素ずつ処理（= 2グループ分）
            int simdCount = (outLen / 2) * 2;

            for (; o < simdCount; o += 2, i += 8)
            {
                // 8要素ロード
                var v = new Vector<float>(src, i);

                // v = [a0 a1 a2 a3 a4 a5 a6 a7]
                // 前半4つと後半4つを平均化
                float avg0 = (v[0] + v[1] + v[2] + v[3]) * 0.25f;
                float avg1 = (v[4] + v[5] + v[6] + v[7]) * 0.25f;

                dst[o] = avg0;
                dst[o + 1] = avg1;
            }

            // 端数（SIMD で処理できない分）
            for (; o < outLen; o++, i += 4)
            {
                dst[o] = (src[i] + src[i + 1] + src[i + 2] + src[i + 3]) * 0.25f;
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
        List<XYPoint> ProcessAudio(WasapiCapture sender, WaveInEventArgs e);

        /// <summary>
        /// Gets the user control that provides the visual representation of the data being visualized.
        /// </summary>
        /// <remarks>This property is typically used to embed or display the visualizer's user interface
        /// within a host application. The returned control may present complex data structures or interactive
        /// visualizations, depending on the implementation.</remarks>
        UserControl? VisualizerView
        {
            get
            {
                return null;
            }
        }

        static string GetSettingsPath(string visualizerName)
        {
            var appName = "OscVisualizer";

            var configDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                appName, visualizerName
            );
            Directory.CreateDirectory(configDir);

            var settingsPath = Path.Combine(configDir, "settings.json");
            return settingsPath;
        }

        void SaveSettings()
        {
            // デフォルト実装は何もしない
        }

        void LoadSettings()
        {
            // デフォルト実装は何もしない
        }
    }
}
