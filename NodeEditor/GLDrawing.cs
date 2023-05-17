using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Data;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using System.Xml;

using OpenTK;
using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL;

namespace NodeEditor
{

    public class GLDrawing
    {
        public static void DrawRectangle(RectangleF rect, Color4 color)
        {
            GL.Begin(PrimitiveType.Lines);

            GL.Color4(color);
            GL.Vertex2(rect.Left, rect.Top);
            GL.Vertex2(rect.Right, rect.Top);

            GL.Vertex2(rect.Right, rect.Top);
            GL.Vertex2(rect.Right, rect.Bottom);

            GL.Vertex2(rect.Right, rect.Bottom);
            GL.Vertex2(rect.Left, rect.Bottom);

            GL.Vertex2(rect.Left, rect.Bottom);
            GL.Vertex2(rect.Left, rect.Top);

            GL.End();
        }
        public static void FillRectangle(RectangleF rect, Color4 color)
        {
            GL.Begin(PrimitiveType.Quads);

            GL.Color4(color);
            GL.Vertex2(rect.Left, rect.Top);
            GL.Vertex2(rect.Right, rect.Top);
            GL.Vertex2(rect.Right, rect.Bottom);
            GL.Vertex2(rect.Left, rect.Bottom);

            GL.End();
        }
        public static Color4 ToColor4(Color c)
        {
            return new Color4(c.R, c.G, c.B, c.A);
        }
    }

    public class GLGraphics
    {
        public InterpolationMode InterpolationMode { get; set; }
        public SmoothingMode SmoothingMode { get; set; }

        public void DrawRectangle(Pen pen, RectangleF rect)
        {
            GLDrawing.DrawRectangle(rect, GLDrawing.ToColor4(pen.Color));
        }

        public void FillRectangle(Brush brush, RectangleF rect)
        {
            switch (brush)
            {
                case SolidBrush b:
                    GLDrawing.FillRectangle(rect, GLDrawing.ToColor4(b.Color));
                    break;
                default:
                    throw new NotImplementedException();
            }
        }
        public void DrawString(String str, Font font, Brush brush, PointF position)
        {
        }
        public void DrawString(String str, Font font, Brush brush, RectangleF bounds, StringFormat format)
        {
        }
        public void DrawImage(Image image, RectangleF rect)
        {
        }
        public void DrawLines(Pen pen, PointF[] points)
        {
            GL.Begin(PrimitiveType.Lines);

            GL.LineWidth(pen.Width);
            GL.Enable(EnableCap.LineSmooth);

            GL.Color4(GLDrawing.ToColor4(pen.Color));
            for (var i = 0; i < points.Length - 2; i++)
            {
                GL.Vertex2(points[i].X, points[i].Y);
                GL.Vertex2(points[i + 1].X, points[i + 1].Y);
            }

            GL.End();
        }
    }
}
