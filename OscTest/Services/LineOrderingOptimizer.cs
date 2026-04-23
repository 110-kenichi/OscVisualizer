using System;
using System.Collections.Generic;
using System.Numerics;

namespace OscVisualizer.Services
{
    public static class LineOrderingOptimizer
    {
        public static List<Line2D> ReorderForVectorDisplay(
            IReadOnlyList<Line2D> input,
            float connectionTolerance = 0.002f)
        {
            if (input == null || input.Count == 0)
                return new List<Line2D>();

            int count = input.Count;
            bool[] used = new bool[count];
            var result = new List<Line2D>(count);

            // 最初の線分は適当に0番。必要なら「最も左下」などにもできる
            int currentIndex = FindBestInitialLine(input);
            used[currentIndex] = true;

            Line2D current = input[currentIndex];
            result.Add(current);

            Vector2 currentPos = current.P1;

            for (int step = 1; step < count; step++)
            {
                int bestIndex = -1;
                bool reverseBest = false;
                float bestDist2 = float.PositiveInfinity;
                bool bestIsConnected = false;

                for (int i = 0; i < count; i++)
                {
                    if (used[i]) continue;

                    var line = input[i];

                    float dStart2 = Vector2.DistanceSquared(currentPos, line.P0);
                    float dEnd2 = Vector2.DistanceSquared(currentPos, line.P1);

                    bool startConnected = dStart2 <= connectionTolerance * connectionTolerance;
                    bool endConnected = dEnd2 <= connectionTolerance * connectionTolerance;

                    // つながる線を最優先
                    if (startConnected || endConnected)
                    {
                        float candidateDist2 = MathF.Min(dStart2, dEnd2);
                        bool reverse = dEnd2 < dStart2;

                        if (!bestIsConnected || candidateDist2 < bestDist2)
                        {
                            bestIsConnected = true;
                            bestDist2 = candidateDist2;
                            bestIndex = i;
                            reverseBest = reverse;
                        }

                        continue;
                    }

                    // つながるものが無い場合は最短移動
                    if (!bestIsConnected)
                    {
                        float candidateDist2 = MathF.Min(dStart2, dEnd2);
                        bool reverse = dEnd2 < dStart2;

                        if (candidateDist2 < bestDist2)
                        {
                            bestDist2 = candidateDist2;
                            bestIndex = i;
                            reverseBest = reverse;
                        }
                    }
                }

                if (bestIndex < 0)
                    break;

                used[bestIndex] = true;

                Line2D next = input[bestIndex];
                if (reverseBest)
                    next = new Line2D(next.P1, next.P0);

                result.Add(next);
                currentPos = next.P1;
            }

            return result;
        }

        private static int FindBestInitialLine(IReadOnlyList<Line2D> input)
        {
            int bestIndex = 0;
            float bestScore = float.PositiveInfinity;

            for (int i = 0; i < input.Count; i++)
            {
                var l = input[i];

                // 左下寄りを優先
                float score0 = l.P0.X + l.P0.Y;
                float score1 = l.P1.X + l.P1.Y;
                float score = MathF.Min(score0, score1);

                if (score < bestScore)
                {
                    bestScore = score;
                    bestIndex = i;
                }
            }

            return bestIndex;
        }
    }
}