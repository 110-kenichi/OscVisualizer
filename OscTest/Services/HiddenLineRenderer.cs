using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace OscVisualizer.Services
{
    #region Data Structures

    public struct Triangle
    {
        public Vector3 V0;
        public Vector3 V1;
        public Vector3 V2;
        public Vector3 Normal;

        public Triangle(Vector3 v0, Vector3 v1, Vector3 v2, Vector3 normal)
        {
            V0 = v0;
            V1 = v1;
            V2 = v2;
            Normal = normal;
        }
    }

    public class StlModel
    {
        public List<Triangle> Triangles { get; } = new List<Triangle>();

        public void NormalizeToUnitCube()
        {
            if (Triangles.Count == 0) return;

            var all = Triangles.SelectMany(t => new[] { t.V0, t.V1, t.V2 }).ToArray();
            float minX = all.Min(v => v.X);
            float minY = all.Min(v => v.Y);
            float minZ = all.Min(v => v.Z);
            float maxX = all.Max(v => v.X);
            float maxY = all.Max(v => v.Y);
            float maxZ = all.Max(v => v.Z);

            Vector3 min = new Vector3(minX, minY, minZ);
            Vector3 max = new Vector3(maxX, maxY, maxZ);
            Vector3 center = (min + max) * 0.5f;
            Vector3 size = max - min;
            float scale = 1.0f / MathF.Max(size.X, MathF.Max(size.Y, size.Z));

            for (int i = 0; i < Triangles.Count; i++)
            {
                var t = Triangles[i];
                t.V0 = (t.V0 - center) * scale;
                t.V1 = (t.V1 - center) * scale;
                t.V2 = (t.V2 - center) * scale;
                t.Normal = Vector3.Normalize(Vector3.Cross(t.V1 - t.V0, t.V2 - t.V0));
                Triangles[i] = t;
            }
        }
    }

    public struct Line2D
    {
        public Vector2 P0;
        public Vector2 P1;

        public Line2D(Vector2 p0, Vector2 p1)
        {
            P0 = p0;
            P1 = p1;
        }
    }

    public struct Segment3D
    {
        public Vector3 P0;
        public Vector3 P1;

        public Segment3D(Vector3 p0, Vector3 p1)
        {
            P0 = p0;
            P1 = p1;
        }
    }

    #endregion

    #region STL Loader

    public static class StlLoader
    {
        public static StlModel Load(string path)
        {
            using var fs = File.OpenRead(path);
            if (IsBinaryStl(fs))
            {
                fs.Position = 0;
                return LoadBinary(fs);
            }
            else
            {
                fs.Position = 0;
                return LoadAscii(fs);
            }
        }

        private static bool IsBinaryStl(Stream stream)
        {
            if (stream.Length < 84) return false;

            long pos = stream.Position;
            try
            {
                byte[] header = new byte[80];
                stream.Read(header, 0, 80);

                byte[] countBytes = new byte[4];
                stream.Read(countBytes, 0, 4);
                uint triCount = BitConverter.ToUInt32(countBytes, 0);

                long expected = 84L + triCount * 50L;
                if (expected == stream.Length) return true;

                string headerText = Encoding.ASCII.GetString(header).TrimStart();
                if (headerText.StartsWith("solid", StringComparison.OrdinalIgnoreCase))
                    return false;

                return true;
            }
            finally
            {
                stream.Position = pos;
            }
        }

        private static StlModel LoadBinary(Stream stream)
        {
            var model = new StlModel();
            using var br = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);

            br.ReadBytes(80);
            uint triCount = br.ReadUInt32();

            for (uint i = 0; i < triCount; i++)
            {
                Vector3 normal = ReadVector3(br);
                Vector3 v0 = ReadVector3(br);
                Vector3 v1 = ReadVector3(br);
                Vector3 v2 = ReadVector3(br);
                br.ReadUInt16(); // attribute byte count

                if (normal.LengthSquared() < 1e-12f)
                    normal = Vector3.Normalize(Vector3.Cross(v1 - v0, v2 - v0));

                model.Triangles.Add(new Triangle(v0, v1, v2, normal));
            }

            return model;
        }

        private static StlModel LoadAscii(Stream stream)
        {
            var model = new StlModel();
            using var sr = new StreamReader(stream, Encoding.ASCII, true, 4096, leaveOpen: true);

            string? line;
            Vector3 normal = Vector3.Zero;
            List<Vector3> verts = new List<Vector3>(3);

            while ((line = sr.ReadLine()) != null)
            {
                line = line.Trim();
                if (line.StartsWith("facet normal", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = SplitTokens(line);
                    normal = new Vector3(
                        ParseFloat(parts[2]),
                        ParseFloat(parts[3]),
                        ParseFloat(parts[4]));
                    verts.Clear();
                }
                else if (line.StartsWith("vertex", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = SplitTokens(line);
                    verts.Add(new Vector3(
                        ParseFloat(parts[1]),
                        ParseFloat(parts[2]),
                        ParseFloat(parts[3])));
                }
                else if (line.StartsWith("endfacet", StringComparison.OrdinalIgnoreCase))
                {
                    if (verts.Count == 3)
                    {
                        if (normal.LengthSquared() < 1e-12f)
                            normal = Vector3.Normalize(Vector3.Cross(verts[1] - verts[0], verts[2] - verts[0]));

                        model.Triangles.Add(new Triangle(verts[0], verts[1], verts[2], normal));
                    }
                }
            }

            return model;
        }

        private static string[] SplitTokens(string s)
            => s.Split((char[])null!, StringSplitOptions.RemoveEmptyEntries);

        private static float ParseFloat(string s)
            => float.Parse(s, CultureInfo.InvariantCulture);

        private static Vector3 ReadVector3(BinaryReader br)
            => new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
    }

    #endregion

    #region Vector Display Interface

    public interface IVectorDisplayDevice
    {
        void BeginFrame();
        void DrawLine(float x0, float y0, float x1, float y1);
        void EndFrame();
    }

    #endregion

    #region Renderer

    public struct IndexedTriangle
    {
        public int I0;
        public int I1;
        public int I2;

        public IndexedTriangle(int i0, int i1, int i2)
        {
            I0 = i0;
            I1 = i1;
            I2 = i2;
        }
    }

    public class MeshEdge
    {
        public int V0;
        public int V1;

        public int TriangleA;
        public int TriangleB; // 境界エッジなら -1

        public MeshEdge(int v0, int v1, int triangleA, int triangleB = -1)
        {
            V0 = v0;
            V1 = v1;
            TriangleA = triangleA;
            TriangleB = triangleB;
        }
    }

    public class IndexedMesh
    {
        public List<Vector3> Vertices { get; } = new();
        public List<IndexedTriangle> Triangles { get; } = new();
        public List<MeshEdge> Edges { get; } = new();
    }


    internal struct QuantizedVertexKey : IEquatable<QuantizedVertexKey>
    {
        public readonly long X;
        public readonly long Y;
        public readonly long Z;

        public QuantizedVertexKey(Vector3 v, float epsilon)
        {
            X = (long)MathF.Round(v.X / epsilon);
            Y = (long)MathF.Round(v.Y / epsilon);
            Z = (long)MathF.Round(v.Z / epsilon);
        }

        public bool Equals(QuantizedVertexKey other)
            => X == other.X && Y == other.Y && Z == other.Z;

        public override bool Equals(object? obj)
            => obj is QuantizedVertexKey other && Equals(other);

        public override int GetHashCode()
            => HashCode.Combine(X, Y, Z);
    }

    internal struct EdgeKey : IEquatable<EdgeKey>
    {
        public readonly int A;
        public readonly int B;

        public EdgeKey(int a, int b)
        {
            if (a < b)
            {
                A = a;
                B = b;
            }
            else
            {
                A = b;
                B = a;
            }
        }

        public bool Equals(EdgeKey other) => A == other.A && B == other.B;
        public override bool Equals(object? obj) => obj is EdgeKey other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(A, B);
    }

    public static class MeshBuilder
    {
        public static IndexedMesh BuildIndexedMesh(StlModel model, float vertexMergeEpsilon = 1e-5f)
        {
            var mesh = new IndexedMesh();

            var vertexMap = new Dictionary<QuantizedVertexKey, int>();
            var edgeMap = new Dictionary<EdgeKey, MeshEdge>();

            int GetOrAddVertex(Vector3 v)
            {
                var key = new QuantizedVertexKey(v, vertexMergeEpsilon);
                if (vertexMap.TryGetValue(key, out int index))
                    return index;

                index = mesh.Vertices.Count;
                mesh.Vertices.Add(v);
                vertexMap[key] = index;
                return index;
            }

            void AddEdge(int ia, int ib, int triIndex)
            {
                var key = new EdgeKey(ia, ib);

                if (edgeMap.TryGetValue(key, out var edge))
                {
                    if (edge.TriangleB == -1)
                    {
                        edge.TriangleB = triIndex;
                    }
                    else
                    {
                        // 非多様体。3面目以降はここでは無視。
                    }
                }
                else
                {
                    edgeMap[key] = new MeshEdge(key.A, key.B, triIndex, -1);
                }
            }

            foreach (var tri in model.Triangles)
            {
                int i0 = GetOrAddVertex(tri.V0);
                int i1 = GetOrAddVertex(tri.V1);
                int i2 = GetOrAddVertex(tri.V2);

                int triIndex = mesh.Triangles.Count;
                mesh.Triangles.Add(new IndexedTriangle(i0, i1, i2));

                AddEdge(i0, i1, triIndex);
                AddEdge(i1, i2, triIndex);
                AddEdge(i2, i0, triIndex);
            }

            mesh.Edges.AddRange(edgeMap.Values);
            return mesh;
        }
    }


    public enum RotationCenterMode
    {
        Origin,
        ModelCenter,
        Custom
    }

    public class SceneMeshInstance
    {
        public IndexedMesh Mesh { get; }
        public List<MeshEdge> RenderEdges { get; }

        public float RotationXDeg { get; set; } = 0f;
        public float RotationYDeg { get; set; } = 0f;
        public float RotationZDeg { get; set; } = 0f;

        public float Scale { get; set; } = 1f;
        public Vector3 Translation { get; set; } = Vector3.Zero;

        public RotationCenterMode RotationCenterMode { get; set; } = RotationCenterMode.ModelCenter;
        public Vector3 CustomRotationCenter { get; set; } = Vector3.Zero;

        public bool InvertFacing { get; set; } = false;
        public bool DrawBoundaryEdges { get; set; } = true;
        public bool Visible { get; set; } = true;

        public SceneMeshInstance(IndexedMesh mesh, List<MeshEdge>? renderEdges = null)
        {
            Mesh = mesh;
            RenderEdges = renderEdges ?? mesh.Edges;
        }

        public Vector3 GetModelCenter()
        {
            if (Mesh.Vertices.Count == 0)
                return Vector3.Zero;

            Vector3 min = Mesh.Vertices[0];
            Vector3 max = Mesh.Vertices[0];

            for (int i = 1; i < Mesh.Vertices.Count; i++)
            {
                var v = Mesh.Vertices[i];

                min.X = MathF.Min(min.X, v.X);
                min.Y = MathF.Min(min.Y, v.Y);
                min.Z = MathF.Min(min.Z, v.Z);

                max.X = MathF.Max(max.X, v.X);
                max.Y = MathF.Max(max.Y, v.Y);
                max.Z = MathF.Max(max.Z, v.Z);
            }

            return (min + max) * 0.5f;
        }

        public Vector3 GetRotationCenter()
        {
            return RotationCenterMode switch
            {
                RotationCenterMode.Origin => Vector3.Zero,
                RotationCenterMode.ModelCenter => GetModelCenter(),
                RotationCenterMode.Custom => CustomRotationCenter,
                _ => Vector3.Zero
            };
        }

        public List<Vector3> TransformVertices()
        {
            var result = new List<Vector3>(Mesh.Vertices.Count);

            float rx = RotationXDeg * MathF.PI / 180f;
            float ry = RotationYDeg * MathF.PI / 180f;
            float rz = RotationZDeg * MathF.PI / 180f;

            Matrix4x4 rot =
                Matrix4x4.CreateRotationX(rx) *
                Matrix4x4.CreateRotationY(ry) *
                Matrix4x4.CreateRotationZ(rz);

            Vector3 center = GetRotationCenter();

            for (int i = 0; i < Mesh.Vertices.Count; i++)
            {
                Vector3 v = Mesh.Vertices[i];
                v -= center;
                v *= Scale;
                v = Vector3.Transform(v, rot);
                v += center;
                v += Translation;
                result.Add(v);
            }

            return result;
        }
    }

    internal class SceneTriangle
    {
        public Vector3 W0;
        public Vector3 W1;
        public Vector3 W2;

        public Vector2 S0;
        public Vector2 S1;
        public Vector2 S2;

        public bool FrontFacing;

        public float MinX;
        public float MinY;
        public float MaxX;
        public float MaxY;

        public float MinZ;
        public float MaxZ;

        public int OwnerInstanceIndex;
        public int OwnerTriangleIndex;

        public void UpdateBounds()
        {
            MinX = MathF.Min(S0.X, MathF.Min(S1.X, S2.X));
            MinY = MathF.Min(S0.Y, MathF.Min(S1.Y, S2.Y));
            MaxX = MathF.Max(S0.X, MathF.Max(S1.X, S2.X));
            MaxY = MathF.Max(S0.Y, MathF.Max(S1.Y, S2.Y));

            MinZ = MathF.Min(W0.Z, MathF.Min(W1.Z, W2.Z));
            MaxZ = MathF.Max(W0.Z, MathF.Max(W1.Z, W2.Z));
        }
    }

    internal struct SceneSegment
    {
        public Vector3 P0_3D;
        public Vector3 P1_3D;
        public Vector2 P0_2D;
        public Vector2 P1_2D;

        public float MinX;
        public float MinY;
        public float MaxX;
        public float MaxY;

        public void UpdateBounds()
        {
            MinX = MathF.Min(P0_2D.X, P1_2D.X);
            MinY = MathF.Min(P0_2D.Y, P1_2D.Y);
            MaxX = MathF.Max(P0_2D.X, P1_2D.X);
            MaxY = MathF.Max(P0_2D.Y, P1_2D.Y);
        }
    }

    internal struct SceneTriangleInfo
    {
        public bool FrontFacing;
    }

    internal class ScreenTriangleGrid
    {
        private readonly int _cols;
        private readonly int _rows;
        private readonly List<int>[,] _cells;

        public ScreenTriangleGrid(int cols, int rows)
        {
            _cols = cols;
            _rows = rows;
            _cells = new List<int>[cols, rows];

            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < cols; x++)
                {
                    _cells[x, y] = new List<int>();
                }
            }
        }

        public void AddTriangle(int triIndex, SceneTriangle tri)
        {
            GetCellRange(tri.MinX, tri.MinY, tri.MaxX, tri.MaxY, out int x0, out int y0, out int x1, out int y1);

            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    _cells[x, y].Add(triIndex);
                }
            }
        }

        public List<int> Query(float minX, float minY, float maxX, float maxY)
        {
            GetCellRange(minX, minY, maxX, maxY, out int x0, out int y0, out int x1, out int y1);

            var set = new HashSet<int>();

            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    var list = _cells[x, y];
                    for (int i = 0; i < list.Count; i++)
                    {
                        set.Add(list[i]);
                    }
                }
            }

            return new List<int>(set);
        }

        private void GetCellRange(float minX, float minY, float maxX, float maxY, out int x0, out int y0, out int x1, out int y1)
        {
            x0 = ToCellX(minX);
            x1 = ToCellX(maxX);
            y0 = ToCellY(minY);
            y1 = ToCellY(maxY);

            if (x0 > x1) (x0, x1) = (x1, x0);
            if (y0 > y1) (y0, y1) = (y1, y0);
        }

        private int ToCellX(float x)
        {
            float u = (x + 1f) * 0.5f;
            int cx = (int)(u * _cols);
            return Math.Clamp(cx, 0, _cols - 1);
        }

        private int ToCellY(float y)
        {
            float v = (y + 1f) * 0.5f;
            int cy = (int)(v * _rows);
            return Math.Clamp(cy, 0, _rows - 1);
        }
    }

    public class HiddenLineSilhouetteSceneRenderer
    {
        private readonly List<SceneMeshInstance> _instances = new();

        public float FocalLength { get; set; } = 1.0f;
        public float ViewportScale { get; set; } = 1.0f;

        public float NearZ { get; set; } = 0.01f;
        public float Epsilon { get; set; } = 1e-5f;

        public bool AutoFitToCrtRange { get; set; } = true;
        public float AutoFitMargin { get; set; } = 0.95f;

        public int GridCols { get; set; } = 24;
        public int GridRows { get; set; } = 24;

        public float SceneRotationXDeg { get; set; } = 0f;
        public float SceneRotationYDeg { get; set; } = 0f;
        public float SceneRotationZDeg { get; set; } = 0f;

        public float SceneScale { get; set; } = 1f;
        public Vector3 SceneTranslation { get; set; } = Vector3.Zero;

        public RotationCenterMode SceneRotationCenterMode { get; set; } = RotationCenterMode.Origin;
        public Vector3 SceneCustomRotationCenter { get; set; } = Vector3.Zero;

        public IList<SceneMeshInstance> Instances => _instances;

        public bool LockSceneModelCenter { get; set; } = true;
        private Vector3? _cachedSceneModelCenter;

        public void AddInstance(SceneMeshInstance instance)
        {
            _instances.Add(instance);
            _cachedSceneModelCenter = null;
        }

        public void ClearInstances()
        {
            _instances.Clear();
            _cachedSceneModelCenter = null;
        }

        public void Render(IVectorDisplayDevice device)
        {
            var lines = GetFrameLines();
            lines = LineOrderingOptimizer.ReorderForVectorDisplay(lines, connectionTolerance: 0.003f);

            device.BeginFrame();
            foreach (var line in lines)
            {
                device.DrawLine(line.P0.X, line.P0.Y, line.P1.X, line.P1.Y);
            }
            device.EndFrame();
        }

        public List<Line2D> GetFrameLines()
        {
            var visibleInstances = new List<SceneMeshInstance>();
            for (int i = 0; i < _instances.Count; i++)
            {
                if (_instances[i].Visible)
                    visibleInstances.Add(_instances[i]);
            }

            if (visibleInstances.Count == 0)
                return new List<Line2D>();

            Vector3 sceneCenter = GetSceneRotationCenter(visibleInstances);

            var transformedVerticesPerInstance = new List<Vector3>[visibleInstances.Count];
            var rawProjectedPerInstance = new Vector2[visibleInstances.Count][];

            object rawProjectedLock = new object();
            var allRawProjected = new List<Vector2>(4096);

            // 1. インスタンスごとの頂点変換・投影を並列化
            Parallel.For(0, visibleInstances.Count, instIndex =>
            {
                var inst = visibleInstances[instIndex];

                var worldVertices = inst.TransformVertices();
                worldVertices = ApplySceneTransform(worldVertices, sceneCenter);
                transformedVerticesPerInstance[instIndex] = worldVertices;

                var rawProjected = new Vector2[worldVertices.Count];
                for (int i = 0; i < worldVertices.Count; i++)
                {
                    rawProjected[i] = ProjectRaw(worldVertices[i]);
                }
                rawProjectedPerInstance[instIndex] = rawProjected;

                lock (rawProjectedLock)
                {
                    allRawProjected.AddRange(rawProjected);
                }
            });

            Matrix3x2 fitTransform = BuildFitTransform(allRawProjected);

            var sceneTriangles = new List<SceneTriangle>(4096);
            var perInstanceTriInfo = new SceneTriangleInfo[visibleInstances.Count][];

            object triLock = new object();

            // 2. 三角形情報生成を並列化
            Parallel.For(0, visibleInstances.Count, instIndex =>
            {
                var inst = visibleInstances[instIndex];
                var worldVertices = transformedVerticesPerInstance[instIndex];
                var rawProjected = rawProjectedPerInstance[instIndex];

                var projected = new Vector2[rawProjected.Length];
                for (int i = 0; i < rawProjected.Length; i++)
                {
                    projected[i] = Vector2.Transform(rawProjected[i], fitTransform);
                }

                var triInfo = BuildTriangleInfo(inst, worldVertices);
                perInstanceTriInfo[instIndex] = triInfo;

                var localTriangles = new List<SceneTriangle>(inst.Mesh.Triangles.Count);

                for (int ti = 0; ti < inst.Mesh.Triangles.Count; ti++)
                {
                    var tri = inst.Mesh.Triangles[ti];

                    var st = new SceneTriangle
                    {
                        W0 = worldVertices[tri.I0],
                        W1 = worldVertices[tri.I1],
                        W2 = worldVertices[tri.I2],
                        S0 = projected[tri.I0],
                        S1 = projected[tri.I1],
                        S2 = projected[tri.I2],
                        FrontFacing = triInfo[ti].FrontFacing,
                        OwnerInstanceIndex = instIndex,
                        OwnerTriangleIndex = ti
                    };
                    st.UpdateBounds();
                    localTriangles.Add(st);
                }

                lock (triLock)
                {
                    sceneTriangles.AddRange(localTriangles);
                }
            });

            // 3. グリッド構築（ここは単純に直列でよい）
            var grid = new ScreenTriangleGrid(GridCols, GridRows);
            for (int i = 0; i < sceneTriangles.Count; i++)
            {
                if (sceneTriangles[i].FrontFacing)
                    grid.AddTriangle(i, sceneTriangles[i]);
            }

            // 4. エッジ処理を並列化
            var allLines = new List<Line2D>[visibleInstances.Count];

            Parallel.For(0, visibleInstances.Count, instIndex =>
            {
                var inst = visibleInstances[instIndex];
                var worldVertices = transformedVerticesPerInstance[instIndex];
                var triInfo = perInstanceTriInfo[instIndex];

                var localLines = new List<Line2D>(Math.Max(16, inst.RenderEdges.Count / 4));

                for (int ei = 0; ei < inst.RenderEdges.Count; ei++)
                {
                    var edge = inst.RenderEdges[ei];

                    if (!IsCandidateEdge(edge, triInfo, inst.DrawBoundaryEdges))
                        continue;

                    Vector3 p0 = worldVertices[edge.V0];
                    Vector3 p1 = worldVertices[edge.V1];

                    if (p0.Z <= NearZ && p1.Z <= NearZ)
                        continue;

                    if (!ClipLineToNearPlane(ref p0, ref p1, NearZ))
                        continue;

                    Vector2 s0 = Vector2.Transform(ProjectRaw(p0), fitTransform);
                    Vector2 s1 = Vector2.Transform(ProjectRaw(p1), fitTransform);

                    var seg = CreateSegment(p0, p1, s0, s1);

                    var candidateTriIndices = grid.Query(seg.MinX, seg.MinY, seg.MaxX, seg.MaxY);
                    var visibleSegments = new List<SceneSegment>(1) { seg };

                    for (int ci = 0; ci < candidateTriIndices.Count; ci++)
                    {
                        int sceneTriIndex = candidateTriIndices[ci];
                        var tri = sceneTriangles[sceneTriIndex];

                        if (tri.OwnerInstanceIndex == instIndex &&
                            (tri.OwnerTriangleIndex == edge.TriangleA || tri.OwnerTriangleIndex == edge.TriangleB))
                            continue;

                        if (!tri.FrontFacing)
                            continue;

                        float segMaxZ = MathF.Max(seg.P0_3D.Z, seg.P1_3D.Z);
                        if (tri.MinZ >= segMaxZ - Epsilon)
                            continue;

                        visibleSegments = SubtractHiddenByTriangle(visibleSegments, tri);
                        if (visibleSegments.Count == 0)
                            break;
                    }

                    for (int i = 0; i < visibleSegments.Count; i++)
                    {
                        var v = visibleSegments[i];
                        if ((v.P1_2D - v.P0_2D).LengthSquared() > 1e-12f)
                        {
                            localLines.Add(new Line2D(v.P0_2D, v.P1_2D));
                        }
                    }
                }

                allLines[instIndex] = localLines;
            });

            // 5. 結合
            var lines = new List<Line2D>(4096);
            for (int i = 0; i < allLines.Length; i++)
            {
                if (allLines[i] != null)
                    lines.AddRange(allLines[i]);
            }

            // 6. クリッピング
            var clipped = new List<Line2D>(lines.Count);
            for (int i = 0; i < lines.Count; i++)
            {
                var l = lines[i];
                if (ClipLineToRect(ref l, -1f, -1f, 1f, 1f))
                    clipped.Add(l);
            }

            return clipped;
        }

        private Vector3 GetSceneRotationCenter(List<SceneMeshInstance> visibleInstances)
        {
            return SceneRotationCenterMode switch
            {
                RotationCenterMode.Origin => Vector3.Zero,
                RotationCenterMode.ModelCenter => GetLockedOrComputedSceneModelCenter(visibleInstances),
                RotationCenterMode.Custom => SceneCustomRotationCenter,
                _ => Vector3.Zero
            };
        }

        private Vector3 GetLockedOrComputedSceneModelCenter(List<SceneMeshInstance> visibleInstances)
        {
            if (!LockSceneModelCenter)
                return ComputeSceneModelCenter(visibleInstances);

            if (_cachedSceneModelCenter.HasValue)
                return _cachedSceneModelCenter.Value;

            _cachedSceneModelCenter = ComputeSceneModelCenter(visibleInstances);
            return _cachedSceneModelCenter.Value;
        }

        private Vector3 ComputeSceneModelCenter(List<SceneMeshInstance> visibleInstances)
        {
            bool first = true;
            Vector3 min = Vector3.Zero;
            Vector3 max = Vector3.Zero;

            for (int instIndex = 0; instIndex < visibleInstances.Count; instIndex++)
            {
                var verts = visibleInstances[instIndex].TransformVertices();
                for (int i = 0; i < verts.Count; i++)
                {
                    if (first)
                    {
                        min = max = verts[i];
                        first = false;
                    }
                    else
                    {
                        min.X = MathF.Min(min.X, verts[i].X);
                        min.Y = MathF.Min(min.Y, verts[i].Y);
                        min.Z = MathF.Min(min.Z, verts[i].Z);

                        max.X = MathF.Max(max.X, verts[i].X);
                        max.Y = MathF.Max(max.Y, verts[i].Y);
                        max.Z = MathF.Max(max.Z, verts[i].Z);
                    }
                }
            }

            return first ? Vector3.Zero : (min + max) * 0.5f;
        }

        private List<Vector3> ApplySceneTransform(List<Vector3> vertices, Vector3 sceneCenter)
        {
            var result = new List<Vector3>(vertices.Count);

            float rx = SceneRotationXDeg * MathF.PI / 180f;
            float ry = SceneRotationYDeg * MathF.PI / 180f;
            float rz = SceneRotationZDeg * MathF.PI / 180f;

            Matrix4x4 rot =
                Matrix4x4.CreateRotationX(rx) *
                Matrix4x4.CreateRotationY(ry) *
                Matrix4x4.CreateRotationZ(rz);

            for (int i = 0; i < vertices.Count; i++)
            {
                Vector3 v = vertices[i];
                v -= sceneCenter;
                v *= SceneScale;
                v = Vector3.Transform(v, rot);
                v += sceneCenter;
                v += SceneTranslation;
                result.Add(v);
            }

            return result;
        }

        private SceneTriangleInfo[] BuildTriangleInfo(SceneMeshInstance inst, List<Vector3> worldVertices)
        {
            var result = new SceneTriangleInfo[inst.Mesh.Triangles.Count];

            for (int i = 0; i < inst.Mesh.Triangles.Count; i++)
            {
                var tri = inst.Mesh.Triangles[i];

                Vector3 w0 = worldVertices[tri.I0];
                Vector3 w1 = worldVertices[tri.I1];
                Vector3 w2 = worldVertices[tri.I2];

                Vector3 n = Vector3.Cross(w2 - w0, w1 - w0);
                if (n.LengthSquared() > 1e-20f)
                    n = Vector3.Normalize(n);
                else
                    n = Vector3.UnitZ;

                Vector3 center = (w0 + w1 + w2) / 3f;

                bool frontFacing = Vector3.Dot(n, center) < 0f;
                if (inst.InvertFacing)
                    frontFacing = !frontFacing;

                result[i] = new SceneTriangleInfo
                {
                    FrontFacing = frontFacing
                };
            }

            return result;
        }

        private bool IsCandidateEdge(MeshEdge edge, SceneTriangleInfo[] triInfo, bool drawBoundaryEdges)
        {
            if (edge.TriangleB < 0)
                return drawBoundaryEdges;

            bool fa = triInfo[edge.TriangleA].FrontFacing;
            bool fb = triInfo[edge.TriangleB].FrontFacing;

            return fa != fb;
        }

        private SceneSegment CreateSegment(Vector3 p0, Vector3 p1, Vector2 s0, Vector2 s1)
        {
            var seg = new SceneSegment
            {
                P0_3D = p0,
                P1_3D = p1,
                P0_2D = s0,
                P1_2D = s1
            };
            seg.UpdateBounds();
            return seg;
        }

        private Matrix3x2 BuildFitTransform(List<Vector2> rawProjected)
        {
            if (!AutoFitToCrtRange || rawProjected.Count == 0)
                return Matrix3x2.Identity;

            float minX = float.PositiveInfinity;
            float minY = float.PositiveInfinity;
            float maxX = float.NegativeInfinity;
            float maxY = float.NegativeInfinity;

            for (int i = 0; i < rawProjected.Count; i++)
            {
                minX = MathF.Min(minX, rawProjected[i].X);
                minY = MathF.Min(minY, rawProjected[i].Y);
                maxX = MathF.Max(maxX, rawProjected[i].X);
                maxY = MathF.Max(maxY, rawProjected[i].Y);
            }

            float centerX = (minX + maxX) * 0.5f;
            float centerY = (minY + maxY) * 0.5f;

            float extentX = MathF.Max(MathF.Abs(minX - centerX), MathF.Abs(maxX - centerX));
            float extentY = MathF.Max(MathF.Abs(minY - centerY), MathF.Abs(maxY - centerY));
            float extent = MathF.Max(extentX, extentY);

            if (extent < 1e-6f)
                return Matrix3x2.Identity;

            float scale = AutoFitMargin / extent;

            return Matrix3x2.CreateTranslation(-centerX, -centerY) *
                   Matrix3x2.CreateScale(scale, scale);
        }

        private Vector2 ProjectRaw(Vector3 p)
        {
            float z = MathF.Max(p.Z, NearZ);
            return new Vector2(
                (p.X / z) * FocalLength * ViewportScale,
                (p.Y / z) * FocalLength * ViewportScale);
        }

        private List<SceneSegment> SubtractHiddenByTriangle(List<SceneSegment> segments, SceneTriangle tri)
        {
            var result = new List<SceneSegment>(segments.Count + 2);

            for (int i = 0; i < segments.Count; i++)
            {
                var parts = SubtractSingleSegmentHiddenByTriangle(segments[i], tri);
                result.AddRange(parts);
            }

            return result;
        }

        private List<SceneSegment> SubtractSingleSegmentHiddenByTriangle(SceneSegment seg, SceneTriangle tri)
        {
            if (seg.MaxX < tri.MinX || seg.MaxY < tri.MinY || seg.MinX > tri.MaxX || seg.MinY > tri.MaxY)
                return new List<SceneSegment>(1) { seg };

            List<float> ts = new List<float>(8) { 0f, 1f };

            var triEdges = new[]
            {
                (tri.S0, tri.S1),
                (tri.S1, tri.S2),
                (tri.S2, tri.S0)
            };

            for (int i = 0; i < triEdges.Length; i++)
            {
                if (TryGetLineIntersectionParam(seg.P0_2D, seg.P1_2D, triEdges[i].Item1, triEdges[i].Item2, out float tSeg))
                {
                    if (tSeg > Epsilon && tSeg < 1f - Epsilon)
                        ts.Add(tSeg);
                }
            }

            if (PointInTriangle2D(seg.P0_2D, tri.S0, tri.S1, tri.S2))
                ts.Add(0f);
            if (PointInTriangle2D(seg.P1_2D, tri.S0, tri.S1, tri.S2))
                ts.Add(1f);

            ts.Sort();

            List<float> uniqueTs = new List<float>(ts.Count);
            for (int i = 0; i < ts.Count; i++)
            {
                if (uniqueTs.Count == 0 || MathF.Abs(ts[i] - uniqueTs[^1]) > Epsilon)
                    uniqueTs.Add(ts[i]);
            }

            var visible = new List<SceneSegment>(4);

            for (int i = 0; i < uniqueTs.Count - 1; i++)
            {
                float t0 = uniqueTs[i];
                float t1 = uniqueTs[i + 1];
                if (t1 - t0 < Epsilon)
                    continue;

                float tm = (t0 + t1) * 0.5f;

                Vector3 pm3 = Vector3.Lerp(seg.P0_3D, seg.P1_3D, tm);
                Vector2 pm2 = Vector2.Lerp(seg.P0_2D, seg.P1_2D, tm);

                bool hidden = false;

                if (PointInTriangle2D(pm2, tri.S0, tri.S1, tri.S2))
                {
                    float zTri = InterpolateTriangleDepthAtScreenPoint(pm2, tri);
                    if (!float.IsNaN(zTri))
                    {
                        if (zTri < pm3.Z - Epsilon)
                            hidden = true;
                    }
                }

                if (!hidden)
                {
                    var part = new SceneSegment
                    {
                        P0_3D = Vector3.Lerp(seg.P0_3D, seg.P1_3D, t0),
                        P1_3D = Vector3.Lerp(seg.P0_3D, seg.P1_3D, t1),
                        P0_2D = Vector2.Lerp(seg.P0_2D, seg.P1_2D, t0),
                        P1_2D = Vector2.Lerp(seg.P0_2D, seg.P1_2D, t1)
                    };
                    part.UpdateBounds();
                    visible.Add(part);
                }
            }

            return visible;
        }

        private float InterpolateTriangleDepthAtScreenPoint(Vector2 p, SceneTriangle tri)
        {
            if (!TryBarycentric(p, tri.S0, tri.S1, tri.S2, out float u, out float v, out float w))
                return float.NaN;

            return tri.W0.Z * u + tri.W1.Z * v + tri.W2.Z * w;
        }

        private bool TryBarycentric(Vector2 p, Vector2 a, Vector2 b, Vector2 c, out float u, out float v, out float w)
        {
            Vector2 v0 = b - a;
            Vector2 v1 = c - a;
            Vector2 v2 = p - a;

            float d00 = Vector2.Dot(v0, v0);
            float d01 = Vector2.Dot(v0, v1);
            float d11 = Vector2.Dot(v1, v1);
            float d20 = Vector2.Dot(v2, v0);
            float d21 = Vector2.Dot(v2, v1);

            float denom = d00 * d11 - d01 * d01;
            if (MathF.Abs(denom) < 1e-12f)
            {
                u = v = w = 0f;
                return false;
            }

            v = (d11 * d20 - d01 * d21) / denom;
            w = (d00 * d21 - d01 * d20) / denom;
            u = 1f - v - w;
            return true;
        }

        private bool PointInTriangle2D(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            if (!TryBarycentric(p, a, b, c, out float u, out float v, out float w))
                return false;

            return u >= -Epsilon && v >= -Epsilon && w >= -Epsilon;
        }

        private bool TryGetLineIntersectionParam(Vector2 p0, Vector2 p1, Vector2 q0, Vector2 q1, out float tP)
        {
            tP = 0f;

            Vector2 r = p1 - p0;
            Vector2 s = q1 - q0;

            float rxs = Cross(r, s);
            Vector2 qp = q0 - p0;
            float qpxr = Cross(qp, r);

            if (MathF.Abs(rxs) < 1e-10f && MathF.Abs(qpxr) < 1e-10f)
                return false;

            if (MathF.Abs(rxs) < 1e-10f)
                return false;

            float t = Cross(qp, s) / rxs;
            float u = Cross(qp, r) / rxs;

            if (t >= -Epsilon && t <= 1f + Epsilon &&
                u >= -Epsilon && u <= 1f + Epsilon)
            {
                tP = Math.Clamp(t, 0f, 1f);
                return true;
            }

            return false;
        }

        private float Cross(Vector2 a, Vector2 b)
        {
            return a.X * b.Y - a.Y * b.X;
        }

        private bool ClipLineToNearPlane(ref Vector3 p0, ref Vector3 p1, float nearZ)
        {
            bool in0 = p0.Z >= nearZ;
            bool in1 = p1.Z >= nearZ;

            if (in0 && in1)
                return true;
            if (!in0 && !in1)
                return false;

            float t = (nearZ - p0.Z) / (p1.Z - p0.Z);
            Vector3 pi = Vector3.Lerp(p0, p1, t);

            if (!in0) p0 = pi;
            else p1 = pi;

            return true;
        }

        private bool ClipLineToRect(ref Line2D line, float xmin, float ymin, float xmax, float ymax)
        {
            float x0 = line.P0.X;
            float y0 = line.P0.Y;
            float x1 = line.P1.X;
            float y1 = line.P1.Y;

            int code0 = ComputeOutCode(x0, y0, xmin, ymin, xmax, ymax);
            int code1 = ComputeOutCode(x1, y1, xmin, ymin, xmax, ymax);

            while (true)
            {
                if ((code0 | code1) == 0)
                {
                    line = new Line2D(new Vector2(x0, y0), new Vector2(x1, y1));
                    return true;
                }

                if ((code0 & code1) != 0)
                    return false;

                int outCode = code0 != 0 ? code0 : code1;
                float x = 0f, y = 0f;

                if ((outCode & 8) != 0)
                {
                    x = x0 + (x1 - x0) * (ymax - y0) / (y1 - y0);
                    y = ymax;
                }
                else if ((outCode & 4) != 0)
                {
                    x = x0 + (x1 - x0) * (ymin - y0) / (y1 - y0);
                    y = ymin;
                }
                else if ((outCode & 2) != 0)
                {
                    y = y0 + (y1 - y0) * (xmax - x0) / (x1 - x0);
                    x = xmax;
                }
                else if ((outCode & 1) != 0)
                {
                    y = y0 + (y1 - y0) * (xmin - x0) / (x1 - x0);
                    x = xmin;
                }

                if (outCode == code0)
                {
                    x0 = x;
                    y0 = y;
                    code0 = ComputeOutCode(x0, y0, xmin, ymin, xmax, ymax);
                }
                else
                {
                    x1 = x;
                    y1 = y;
                    code1 = ComputeOutCode(x1, y1, xmin, ymin, xmax, ymax);
                }
            }
        }

        private int ComputeOutCode(float x, float y, float xmin, float ymin, float xmax, float ymax)
        {
            int code = 0;
            if (x < xmin) code |= 1;
            else if (x > xmax) code |= 2;
            if (y < ymin) code |= 4;
            else if (y > ymax) code |= 8;
            return code;
        }
    }

    #endregion

}