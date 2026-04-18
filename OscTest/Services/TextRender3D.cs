using Avalonia.Controls;
using Avalonia.Threading;
using DynamicData;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using OscVisualizer.Models;
using OscVisualizer.ViewModels;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace OscVisualizer.Services
{
    internal class TextRender3D : IAudioVisualizer
    {

        public string VisualizerName
        {
            get => "Text Render 3D";
        }

        private UserControl? _visualizerView;

        /// <summary>
        /// 
        /// </summary>
        public UserControl? VisualizerView
        {
            get
            {
                return _visualizerView;
            }
        }

        private readonly Stopwatch _sw = Stopwatch.StartNew();

        private TextRender3DViewModel settingsViewModel = new TextRender3DViewModel();

        /// <summary>
        /// Initializes a new instance of the TextRender class.
        /// </summary>
        /// <remarks>This constructor sets up the visualizer view for the TextRender instance. Use this
        /// constructor when you need to create a new TextRender with its default visualizer configuration.</remarks>
        public TextRender3D()
        {
            _visualizerView = new TextRenderView();
            settingsViewModel.PropertyChanged += (sender, e) =>
            {
                switch (e.PropertyName)
                {
                    case nameof(TextRender3DViewModel.Text):
                        if (_visualizerView?.DataContext is TextRender3DViewModel vm)
                        {
                            vm.Text = settingsViewModel.Text;
                        }
                        break;
                }
            };
            _visualizerView.DataContext = settingsViewModel;
        }

        public List<XYPoint> ProcessAudio(WasapiCapture capture, WaveInEventArgs e)
        {
            return TextToVectorXYPoints(settingsViewModel.Text, 0.025f, 0.05f);
        }

        // Ramer–Douglas–Peuckerアルゴリズム
        private static List<PointF> RdpSimplify(List<PointF> points, float epsilon)
        {
            if (points.Count < 3)
                return new List<PointF>(points);

            float maxDist = 0;
            int index = 0;
            for (int i = 1; i < points.Count - 1; i++)
            {
                float dist = PerpendicularDistance(points[i], points[0], points[^1]);
                if (dist > maxDist)
                {
                    index = i;
                    maxDist = dist;
                }
            }
            if (maxDist > epsilon)
            {
                var left = RdpSimplify(points.GetRange(0, index + 1), epsilon);
                var right = RdpSimplify(points.GetRange(index, points.Count - index), epsilon);
                left.RemoveAt(left.Count - 1);
                left.AddRange(right);
                return left;
            }
            else
            {
                return new List<PointF> { points[0], points[^1] };
            }
        }

        private static float PerpendicularDistance(PointF pt, PointF lineStart, PointF lineEnd)
        {
            float dx = lineEnd.X - lineStart.X;
            float dy = lineEnd.Y - lineStart.Y;
            if (dx == 0 && dy == 0)
                return MathF.Sqrt((pt.X - lineStart.X) * (pt.X - lineStart.X) + (pt.Y - lineStart.Y) * (pt.Y - lineStart.Y));
            float t = ((pt.X - lineStart.X) * dx + (pt.Y - lineStart.Y) * dy) / (dx * dx + dy * dy);
            float projX = lineStart.X + t * dx;
            float projY = lineStart.Y + t * dy;
            return MathF.Sqrt((pt.X - projX) * (pt.X - projX) + (pt.Y - projY) * (pt.Y - projY));
        }

        private List<XYPoint> TextToVectorXYPoints(string text, float scale = 1.0f, float spacing = 0.1f)
        {
            var points = new List<XYPoint>();
            if (string.IsNullOrEmpty(text))
                return points;

            using var font = new Font("Arial", 9, FontStyle.Regular, GraphicsUnit.Pixel);
            using var path = new GraphicsPath();
            path.AddString(text, font.FontFamily, (int)font.Style, font.Size, new PointF(0, 0), StringFormat.GenericDefault);

            var pts = path.PathPoints;
            var types = path.PathTypes;

            // サブパスごとに分割
            List<List<PointF>> subpaths = new List<List<PointF>>();
            List<PointF> current = new List<PointF>();
            for (int i = 0; i < pts.Length; i++)
            {
                byte type = types[i];
                byte pointType = (byte)(type & 0x7);
                bool isCloseSubpath = (type & 0x80) != 0;
                if (pointType == 0) // Start
                {
                    if (current.Count > 1)
                        subpaths.Add(current);
                    current = new List<PointF> { pts[i] };
                }
                else if (pointType == 1 || pointType == 3) // Line or CloseSubpath
                {
                    current.Add(pts[i]);
                }
                if (isCloseSubpath && current.Count > 1)
                {
                    // 閉じる
                    current.Add(current[0]);
                    subpaths.Add(current);
                    current = new List<PointF>();
                }
            }
            if (current.Count > 1)
                subpaths.Add(current);

            // 各サブパスにRDP適用
            float epsilon = 0.25f; // 誤差許容値（調整可）
            foreach (var sub in subpaths)
            {
                List<PointF> simp;
                if (sub.Count < 10)
                {
                    simp = sub;
                }
                else
                {
                    simp = RdpSimplify(sub, epsilon);
                }
                for (int i = 1; i < simp.Count; i++)
                {
                    float x1 = simp[i - 1].X * scale - 1.0f;
                    float y1 = -simp[i - 1].Y * scale + 1f;
                    float x2 = simp[i].X * scale - 1.0f;
                    float y2 = -simp[i].Y * scale + 1f;
                    points.Add(new XYPoint(x1, y1, 1.0));
                    points.Add(new XYPoint(x2, y2, 1.0));
                }
            }
            return points;
        }

        public void SaveSettings()
        {
            try
            {
                var json = JsonSerializer.Serialize(new { settingsViewModel.Text });

                string settingsPath = IAudioVisualizer.GetSettingsPath(VisualizerName);

                File.WriteAllText(settingsPath, json);
            }
            catch { }
        }

        public void LoadSettings()
        {
            try
            {
                string settingsPath = IAudioVisualizer.GetSettingsPath(VisualizerName);

                if (!File.Exists(settingsPath))
                    return;

                var json = File.ReadAllText(settingsPath);
                var data = JsonSerializer.Deserialize<SettingsData>(json);

                if (data != null)
                {
                    settingsViewModel.Text = data.Text;
                }
            }
            catch { }
        }

        private class SettingsData
        {
            public string Text { get; set; } = "";
        }

    }
}
