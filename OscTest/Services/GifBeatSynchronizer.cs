using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace OscVisualizer.Services
{
    // Audio-sample clock and 10 ms analysis windows, independent of callback size.
    internal sealed class GifBeatSynchronizer
    {
        private readonly Queue<double> _intervals = new();
        private readonly Stopwatch _callbackGap = Stopwatch.StartNew();
        private int _sampleRate, _windowSamples;
        private long _samples;
        private double _energy, _low, _previousEnergy, _averageEnergy;
        private double _lastOnset = -1;
        private double _period, _beat;
        private bool? _oneFramePerBeat;

        public int SelectFrame(float[] wav, int sampleRate, IReadOnlyList<int> durations, int count)
        {
            if (sampleRate <= 0 || count <= 0)
                return -1;

            if (_sampleRate != sampleRate || _callbackGap.Elapsed.TotalSeconds > 0.5)
            {
                _sampleRate = sampleRate;
                _samples = 0;
                _windowSamples = 0;
                _energy = _low = _previousEnergy = _averageEnergy = 0;
                _lastOnset = -1;
                // Reset analysis history while retaining the BPM and animation position.
                _intervals.Clear();
            }
            _callbackGap.Restart();

            int windowSize = Math.Max(1, sampleRate / 100);
            double alpha = 1 - Math.Exp(-2 * Math.PI * 180 / sampleRate);
            foreach (float sample in wav)
            {
                _samples++;
                if (_period > 0)
                    _beat += 1.0 / (sampleRate * _period);
                _low += alpha * (sample - _low);
                _energy += _low * _low;
                if (++_windowSamples < windowSize)
                    continue;

                double now = (double)_samples / sampleRate;
                double energy = _energy / _windowSamples;
                bool onset = energy > 0.000001 &&
                    energy > _averageEnergy * 1.6 &&
                    energy > _previousEnergy * 1.25 &&
                    (_lastOnset < 0 || now - _lastOnset >= 0.25);
                _averageEnergy += 0.05 * (energy - _averageEnergy);
                _previousEnergy = energy;
                _energy = 0;
                _windowSamples = 0;

                if (onset)
                {
                    if (_lastOnset >= 0)
                        ObserveInterval(now - _lastOnset);
                    _lastOnset = now;
                }
            }

            if (_period <= 0)
                return -1;

            double total = 0;
            for (int i = 0; i < count; i++)
                total += DurationSeconds(durations, i);

            // Choose the smaller multiplicative change from the original GIF speed.
            // Keep the choice for this animation, avoiding mode flicker near the boundary.
            _oneFramePerBeat ??= Math.Abs(Math.Log(_period / (total / count))) <
                Math.Abs(Math.Log(_period / total));
            if (_oneFramePerBeat.Value)
                return (int)(Math.Floor(_beat) % count);

            double position = (_beat - Math.Floor(_beat)) * total;
            for (int i = 0; i < count; i++)
            {
                position -= DurationSeconds(durations, i);
                if (position < 0)
                    return i;
            }
            return count - 1;
        }

        private void ObserveInterval(double interval)
        {
            // Once locked, allow a missed onset without halving the estimated tempo.
            if (_period > 0 && interval > 1.5 * _period)
            {
                double beats = Math.Round(interval / _period);
                if (beats <= 4 && Math.Abs(interval / beats - _period) < _period * 0.15)
                    interval /= beats;
            }
            // Approximately 60 to 200 BPM, allowing 10 ms window quantization.
            if (interval < 0.29 || interval > 1.01)
            {
                _intervals.Clear();
                return;
            }
            _intervals.Enqueue(interval);
            if (_intervals.Count > 5)
                _intervals.Dequeue();
            if (_intervals.Count < 3)
                return;

            double[] sorted = _intervals.OrderBy(x => x).ToArray();
            double median = sorted[sorted.Length / 2];
            if (sorted.Count(x => Math.Abs(x - median) <= median * 0.15) <
                Math.Max(3, sorted.Length - 1))
                return;

            if (_period <= 0)
            {
                _period = median;
                _beat = 0;
            }
            else
            {
                // Smooth the estimated BPM interval without onset-based phase correction.
                _period = _period * 0.8 + median * 0.2;
            }
        }

        private static double DurationSeconds(IReadOnlyList<int> durations, int index)
        {
            return index < durations.Count && durations[index] > 0
                ? durations[index] * 0.01 : 0.1;
        }
    }
}
