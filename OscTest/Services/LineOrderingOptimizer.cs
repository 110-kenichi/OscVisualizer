using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;

namespace OscVisualizer.Services
{
    public static class LineOrderingOptimizer
    {
        private struct Cluster
        {
            public List<Line2D> Lines;
            public Vector2 Min;
            public Vector2 Max;
            public Vector2 Center;
        }

        public static List<Line2D> ReorderForVectorDisplay(
            IReadOnlyList<Line2D> input,
            float connectionTolerance = 0.002f,
            int clusterGridSize = 4,
            int clusterThreshold = 1000)
        {
            if (input == null || input.Count == 0)
                return new List<Line2D>();

            // 少ないときは従来の逐次版で十分
            if (input.Count < clusterThreshold)
                return ReorderSequential(input, connectionTolerance);

            var clusters = BuildClusters(input, clusterGridSize);

            // 各クラスタ内部を順序最適化
            var orderedClusters = new Cluster[clusters.Count];
            Parallel.For(0, clusters.Count, i =>
            {
                var orderedLines = ReorderSequential(clusters[i].Lines, connectionTolerance);

                Cluster c = clusters[i];
                c.Lines = orderedLines;
                RecomputeClusterBounds(ref c);
                orderedClusters[i] = c;
            });

            // クラスタ同士の順序を最適化
            var clusterOrder = ReorderClusters(orderedClusters);

            // 最後にクラスタを連結しつつ、必要ならクラスタ全体を反転
            var result = new List<Line2D>(input.Count);

            Vector2? currentPos = null;

            for (int oi = 0; oi < clusterOrder.Count; oi++)
            {
                int clusterIndex = clusterOrder[oi];
                var cluster = orderedClusters[clusterIndex];

                if (cluster.Lines.Count == 0)
                    continue;

                bool reverseCluster = false;

                if (currentPos.HasValue)
                {
                    var first = cluster.Lines[0];
                    var last = cluster.Lines[cluster.Lines.Count - 1];

                    float dToFirst = Vector2.DistanceSquared(currentPos.Value, first.P0);
                    float dToLast = Vector2.DistanceSquared(currentPos.Value, last.P1);

                    reverseCluster = dToLast < dToFirst;
                }

                if (!reverseCluster)
                {
                    for (int i = 0; i < cluster.Lines.Count; i++)
                        result.Add(cluster.Lines[i]);

                    currentPos = cluster.Lines[cluster.Lines.Count - 1].P1;
                }
                else
                {
                    for (int i = cluster.Lines.Count - 1; i >= 0; i--)
                    {
                        var l = cluster.Lines[i];
                        result.Add(new Line2D(l.P1, l.P0));
                    }

                    currentPos = cluster.Lines[0].P0;
                }
            }

            return result;
        }

        private static List<Cluster> BuildClusters(IReadOnlyList<Line2D> input, int gridSize)
        {
            float minX = float.PositiveInfinity;
            float minY = float.PositiveInfinity;
            float maxX = float.NegativeInfinity;
            float maxY = float.NegativeInfinity;

            for (int i = 0; i < input.Count; i++)
            {
                var l = input[i];
                minX = MathF.Min(minX, MathF.Min(l.P0.X, l.P1.X));
                minY = MathF.Min(minY, MathF.Min(l.P0.Y, l.P1.Y));
                maxX = MathF.Max(maxX, MathF.Max(l.P0.X, l.P1.X));
                maxY = MathF.Max(maxY, MathF.Max(l.P0.Y, l.P1.Y));
            }

            float width = MathF.Max(1e-6f, maxX - minX);
            float height = MathF.Max(1e-6f, maxY - minY);

            var cells = new Dictionary<(int x, int y), List<Line2D>>();

            for (int i = 0; i < input.Count; i++)
            {
                var l = input[i];
                Vector2 mid = (l.P0 + l.P1) * 0.5f;

                int cx = Math.Clamp((int)(((mid.X - minX) / width) * gridSize), 0, gridSize - 1);
                int cy = Math.Clamp((int)(((mid.Y - minY) / height) * gridSize), 0, gridSize - 1);

                var key = (cx, cy);
                if (!cells.TryGetValue(key, out var list))
                {
                    list = new List<Line2D>();
                    cells[key] = list;
                }

                list.Add(l);
            }

            var clusters = new List<Cluster>(cells.Count);

            foreach (var kv in cells)
            {
                var cluster = new Cluster
                {
                    Lines = kv.Value
                };
                RecomputeClusterBounds(ref cluster);
                clusters.Add(cluster);
            }

            return clusters;
        }

        private static void RecomputeClusterBounds(ref Cluster cluster)
        {
            if (cluster.Lines == null || cluster.Lines.Count == 0)
            {
                cluster.Min = Vector2.Zero;
                cluster.Max = Vector2.Zero;
                cluster.Center = Vector2.Zero;
                return;
            }

            float minX = float.PositiveInfinity;
            float minY = float.PositiveInfinity;
            float maxX = float.NegativeInfinity;
            float maxY = float.NegativeInfinity;

            for (int i = 0; i < cluster.Lines.Count; i++)
            {
                var l = cluster.Lines[i];
                minX = MathF.Min(minX, MathF.Min(l.P0.X, l.P1.X));
                minY = MathF.Min(minY, MathF.Min(l.P0.Y, l.P1.Y));
                maxX = MathF.Max(maxX, MathF.Max(l.P0.X, l.P1.X));
                maxY = MathF.Max(maxY, MathF.Max(l.P0.Y, l.P1.Y));
            }

            cluster.Min = new Vector2(minX, minY);
            cluster.Max = new Vector2(maxX, maxY);
            cluster.Center = (cluster.Min + cluster.Max) * 0.5f;
        }

        private static List<int> ReorderClusters(IReadOnlyList<Cluster> clusters)
        {
            int count = clusters.Count;
            bool[] used = new bool[count];
            var result = new List<int>(count);

            int currentIndex = FindBestInitialCluster(clusters);
            used[currentIndex] = true;
            result.Add(currentIndex);

            Vector2 currentPos = clusters[currentIndex].Center;

            for (int step = 1; step < count; step++)
            {
                int bestIndex = -1;
                float bestDist2 = float.PositiveInfinity;

                for (int i = 0; i < count; i++)
                {
                    if (used[i]) continue;

                    float d2 = Vector2.DistanceSquared(currentPos, clusters[i].Center);
                    if (d2 < bestDist2)
                    {
                        bestDist2 = d2;
                        bestIndex = i;
                    }
                }

                if (bestIndex < 0)
                    break;

                used[bestIndex] = true;
                result.Add(bestIndex);
                currentPos = clusters[bestIndex].Center;
            }

            return result;
        }

        private static int FindBestInitialCluster(IReadOnlyList<Cluster> clusters)
        {
            int bestIndex = 0;
            float bestScore = float.PositiveInfinity;

            for (int i = 0; i < clusters.Count; i++)
            {
                float score = clusters[i].Center.X + clusters[i].Center.Y;
                if (score < bestScore)
                {
                    bestScore = score;
                    bestIndex = i;
                }
            }

            return bestIndex;
        }

        private static List<Line2D> ReorderSequential(
            IReadOnlyList<Line2D> input,
            float connectionTolerance)
        {
            int count = input.Count;
            bool[] used = new bool[count];
            var result = new List<Line2D>(count);

            // 最初の線分は適当に0番。必要なら「最も左下」などにもできる
            int currentIndex = FindBestInitialLine(input);
            used[currentIndex] = true;

            Line2D current = input[currentIndex];
            result.Add(current);

            Vector2 currentPos = current.P1;
            float tol2 = connectionTolerance * connectionTolerance;

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

                    bool startConnected = dStart2 <= tol2;
                    bool endConnected = dEnd2 <= tol2;

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