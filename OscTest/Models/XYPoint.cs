using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OscVisualizer.Models
{
    public class XYPoint
    {
        /// <summary>
        /// Initializes a new instance of the XYPoint class with the specified coordinates and brightness level.
        /// </summary>
        /// <remarks>The brightness parameter should be within the range 0.0 to 1.0. Values outside this
        /// range may result in undefined behavior.</remarks>
        /// <param name="x">The horizontal position of the point.</param>
        /// <param name="y">The vertical position of the point.</param>
        /// <param name="z"></param>
        /// <param name="intensity">The brightness level of the point. The value muse be greater than 0. Default value is 1.0.</param>
        public XYPoint(double x, double y, double intensity = 1.0, double z = 0.0)
        {
            X = x;
            Y = y;
            Z = z;
            Intensity = intensity;
        }

        /// <summary>
        /// Gets or sets the X coordinate of the point.
        /// </summary>
        public double X { get; set; }

        /// <summary>
        /// Gets or sets the Y coordinate of a point in a two-dimensional space.
        /// </summary>
        public double Y { get; set; }

        /// <summary>
        /// Gets or sets the Z coordinate of a point in a two-dimensional space.
        /// </summary>
        public double Z { get; set; }

        /// <summary>
        /// 
        /// </summary>
        public double Intensity { get; set; }

        public override string ToString()
        {
            return $"({X}, {Y}, {Z}, {Intensity} )";
        }

        public override bool Equals(object? obj)
        {
            if (obj is XYPoint point)
            {
                return X == point.X && Y == point.Y && Z == point.Z && Intensity == point.Intensity; ;
            }
            return false;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(X, Y, Intensity);
        }

        public static XYPoint ScaleAround(XYPoint p, XYPoint center, double s)
        {
            double dx = p.X - center.X;
            double dy = p.Y - center.Y;
            return new XYPoint(center.X + dx * s, center.Y + dy * s, p.Intensity);
        }

        public static XYPoint RotateAround(XYPoint p, XYPoint center, double angle)
        {
            double dx = p.X - center.X;
            double dy = p.Y - center.Y;
            double c = Math.Cos(angle);
            double s = Math.Sin(angle);
            double x = center.X + dx * c - dy * s;
            double y = center.Y + dx * s + dy * c;
            return new XYPoint(x, y, p.Intensity);
        }

    }
}
