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
        private Dictionary<Image, Texture> ImageTextures = new Dictionary<Image, Texture>();

        protected class Texture
        {
            public int Width;
            public int Height;
            public float UVWidth;
            public float UVHeight;
            public int Handle;

            public void Draw(float left, float top, float right, float bottom)
            {
                GL.BindTexture(TextureTarget.Texture2D, Handle);
                GL.Enable(EnableCap.Texture2D);

                GL.Color4(Color.White);

                GL.Begin(PrimitiveType.Quads);

                    var hw = 0.5 / Width;
                    var hh = 0.5 / Height;

                    GL.TexCoord2(hw, hh);
                    GL.Vertex2(left, top);

                    GL.TexCoord2(hw + UVWidth, hh);
                    GL.Vertex2(right, top);

                    GL.TexCoord2(hw + UVWidth, hh + UVHeight);
                    GL.Vertex2(right, bottom);

                    GL.TexCoord2(hw, hh + UVHeight);
                    GL.Vertex2(left, bottom);

                GL.End();

                GL.Disable(EnableCap.Texture2D);
            }
            public void Draw(RectangleF rect)
            {
                Draw(rect.Left, rect.Top, rect.Left + UVWidth * rect.Width, rect.Top + UVHeight * rect.Height);
            }

            public void Draw(PointF position)
            {
                Draw(position.X, position.Y, position.X + Width, position.Y + Height);
            }
        }

        private static uint NextPowerOfTwo(uint n)
        {
            n--;
            n |= n >> 1;
            n |= n >> 2;
            n |= n >> 4;
            n |= n >> 8;
            n |= n >> 16;
            n++;
            return n;
        }

        private Texture CreateTexture(int width, int height, Action<Graphics> fn)
        {
            var bitmap = new Bitmap((int) NextPowerOfTwo((uint) width), (int) NextPowerOfTwo((uint) height));

            using (var gr = Graphics.FromImage(bitmap))
            {
                fn(gr);
            }

            var bitmapData = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), System.Drawing.Imaging.ImageLockMode.ReadOnly, bitmap.PixelFormat);
            var length = bitmapData.Stride * bitmapData.Height;

            byte[] pixels = new byte[length];

            Marshal.Copy(bitmapData.Scan0, pixels, 0, length);
            bitmap.UnlockBits(bitmapData);

            var handle = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, handle);

            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, bitmap.Width, bitmap.Height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);

            GL.TexParameter( TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Clamp );
            GL.TexParameter( TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Clamp );
            GL.TexParameter( TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest );
            GL.TexParameter( TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest );

            return new Texture
            {
                Width = width,
                Height = width,
                UVWidth = (float) width / bitmap.Width,
                UVHeight = (float) height / bitmap.Height,
                Handle = handle,
            };
        }

        private Texture GetStringTexture(string str, Font font, Brush brush)
        {
            Texture tex;
            if (StringTextures.TryGetValue(str, out tex))
                return tex;

            tex = CreateTexture(256, 256, (g) =>
            {
                var sf = new StringFormat(StringFormat.GenericDefault);
                sf.SetMeasurableCharacterRanges(
                      Enumerable.Range(0, str.Length)
                      .Select(i => new CharacterRange(i, 1)).ToArray());

                var targetRectangle = new RectangleF(0, 0, 200, 120);
                g.DrawString(str, font, brush, targetRectangle, sf);
            });

            StringTextures.Add(str, tex);
            return tex;
        }

        private Texture GetImageTexture(Image image)
        {
            Texture tex;
            if (ImageTextures.TryGetValue(image, out tex))
                return tex;

            tex = CreateTexture(image.Width, image.Height, (g) =>
            {
                g.DrawImage(image, 0, 0);
            });

            ImageTextures.Add(image, tex);
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
            GetStringTexture(str, font, brush).Draw(position);
        }
        public void DrawString(String str, Font font, Brush brush, RectangleF bounds, StringFormat format)
        {
        }
        public void DrawImage(Image image, RectangleF rect)
        {
            GetImageTexture(image).Draw(rect);
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
