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
using System.Runtime.InteropServices;

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
        private Dictionary<string, Texture> StringTextures = new Dictionary<string, Texture>();

        protected struct Texture
        {
            public float Width;
            public float Height;
            public int Handle;
        }

        private Texture GetStringTexture(string str, Font font, Brush brush)
        {
            Texture tex;
            if (StringTextures.TryGetValue(str, out tex))
                return tex;

            var bitmap = new Bitmap(256, 256);
            var targetRectangle = new RectangleF(0, 0, 200, 120);

            var sf = new StringFormat(StringFormat.GenericDefault);
            sf.SetMeasurableCharacterRanges(
                  Enumerable.Range(0, str.Length)
                  .Select(i => new CharacterRange(i, 1)).ToArray());

            using (var gr = Graphics.FromImage(bitmap))
            {
                gr.DrawString(str, font, brush, targetRectangle, sf);
            }

            var bitmapData = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), System.Drawing.Imaging.ImageLockMode.ReadOnly, bitmap.PixelFormat);
            var length = bitmapData.Stride * bitmapData.Height;

            byte[] pixels = new byte[length];

            Marshal.Copy(bitmapData.Scan0, pixels, 0, length);
            bitmap.UnlockBits(bitmapData);

            var handle = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, handle);

            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, bitmap.Width, bitmap.Height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);

            GL.TexParameter( TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat );
            GL.TexParameter( TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat );
            GL.TexParameter( TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest );
            GL.TexParameter( TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear );

            tex = new Texture
            {
                Width = bitmap.Width,
                Height = bitmap.Height,
                Handle = handle,
            };

            StringTextures.Add(str, tex);
            return tex;
        }

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
            var tex = GetStringTexture(str, font, brush);
            GL.BindTexture(TextureTarget.Texture2D, tex.Handle);
            GL.Enable(EnableCap.Texture2D);

            GL.Color4(Color.White);

            GL.Begin(PrimitiveType.Quads);

                GL.TexCoord2(0, 0);
                GL.Vertex2(position.X, position.Y);

                GL.TexCoord2(1, 0);
                GL.Vertex2(position.X + tex.Width, position.Y);

                GL.TexCoord2(1, 1);
                GL.Vertex2(position.X + tex.Width, position.Y + tex.Height);

                GL.TexCoord2(0, 1);
                GL.Vertex2(position.X, position.Y + tex.Height);

            GL.End();

            GL.Disable(EnableCap.Texture2D);
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
