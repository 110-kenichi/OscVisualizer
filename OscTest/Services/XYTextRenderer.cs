using Avalonia;
using OscVisualizer.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OscVisualizer.Services
{
    public class XYTextRenderer
    {
        public double CharWidth { get; set; } = 0.08;   // 1文字の幅（XY座標）
        public double CharSpacing { get; set; } = 0.02; // 文字間スペース

        // 文字列 → XYProcessor 用の Point リスト
        public List<Point> BuildText(string text, double x, double y, double scale = 1.0)
        {
            var points = new List<Point>();

            double cursorX = x;

            foreach (char rawC in text)
            {
                char c = char.ToUpper(rawC);

                if (!VectorFont.Glyphs.TryGetValue(c, out var glyph))
                {
                    cursorX += (CharWidth + CharSpacing) * scale;
                    continue;
                }

                foreach (var (a, b) in glyph)
                {
                    var p1 = new Point(
                        cursorX + a.X * CharWidth * scale,
                        y + a.Y * CharWidth * scale
                    );

                    var p2 = new Point(
                        cursorX + b.X * CharWidth * scale,
                        y + b.Y * CharWidth * scale
                    );

                    points.Add(p1);
                    points.Add(p2);
                }

                cursorX += (CharWidth + CharSpacing) * scale;
            }

            return points;
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="text"></param>
        /// <param name="scale"></param>
        /// <returns></returns>
        public Rect CalcTextRect(string text, double scale = 1.0)
        {
            double cursorX = 0;
            double y = 0;
            double mx = 0;
            double my = 0;

            foreach (char rawC in text)
            {
                char c = char.ToUpper(rawC);

                if (!VectorFont.Glyphs.TryGetValue(c, out var glyph))
                {
                    cursorX += (CharWidth + CharSpacing) * scale;
                    continue;
                }

                foreach (var (a, b) in glyph)
                {
                    var p1 = new Point(
                        cursorX + a.X * CharWidth * scale,
                        y + a.Y * CharWidth * scale
                    );

                    var p2 = new Point(
                        cursorX + b.X * CharWidth * scale,
                        y + b.Y * CharWidth * scale
                    );

                    mx = Math.Max(mx, p1.X);
                    mx = Math.Max(mx, p2.X);
                    my = Math.Max(my, p1.Y);
                    my = Math.Max(my, p2.Y);
                }

                cursorX += (CharWidth + CharSpacing) * scale;
            }

            return new Rect(0, 0, mx, my);
        }
    }
}
