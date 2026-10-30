// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;
using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Xml.Linq;
using System.Threading.Tasks;

namespace Mediana.Images.Svg
{
    internal static partial class SvgFilterCompiler
    {




        private static PixelBuffer Morphology(XElement p, PixelBuffer input, Matrix3x2 units, bool linear, SceneRenderContext context, bool overwrite)
            => SceneFilterKernels.Morphology(Pair(p, "radius"), Text(p, "operator", "erode") == "dilate", input, units, linear, context, overwrite);

        private static PixelBuffer Offset(PixelBuffer input, Vector2 delta, bool linear, SceneRenderContext context) => ScenePixels.Map(input, (x, y) => ScenePixels.Sample(input, x - delta.X, y - delta.Y, linear), linear, context);




        private static PixelBuffer Convolve(XElement p, PixelBuffer input, Matrix3x2 units, bool linear, SceneRenderContext context)
        {
            (int width, int height, float[] kernel) = ConvolutionKernel(p);
            float divisor = Number(p, "divisor", kernel.Sum());
            divisor = divisor == 0 ? 1 : divisor;
            float bias = Number(p, "bias");
            int tx = (int)Number(p, "targetX", width / 2), ty = (int)Number(p, "targetY", height / 2);
            if (tx < 0 || tx >= width || ty < 0 || ty >= height)
            {
                throw new InvalidDataException("Invalid SVG convolution target.");
            }

            bool preserve = Text(p, "preserveAlpha") == "true";
            Vector2 step = Pair(p, "kernelUnitLength", 1);
            Vector2 dx = p.Attribute("kernelUnitLength") is null ? Vector2.UnitX : Vector2.TransformNormal(new Vector2(step.X, 0), units);
            Vector2 dy = p.Attribute("kernelUnitLength") is null ? Vector2.UnitY : Vector2.TransformNormal(new Vector2(0, step.Y), units);
            string edge = Text(p, "edgeMode", "duplicate");
            return ScenePixels.Map(input, (x, y) =>
            {
                Vector4 sum = Vector4.Zero;
                for (int row = 0; row < height; row++)
                {
                    for (int col = 0; col < width; col++)
                    {
                        Vector2 d = (dx * (col - tx)) + (dy * (row - ty));
                        Vector4 sample = ScenePixels.Sample(input, x + d.X, y + d.Y, linear, edge);
                        if (preserve && sample.W > 0)
                        {
                            sample = new Vector4(new Vector3(sample.X, sample.Y, sample.Z) / sample.W, sample.W);
                        }

                        sum += sample * kernel[(((height - 1 - row) * width) + width) - 1 - col];
                    }
                }

                sum = (sum / divisor) + new Vector4(bias);
                if (preserve)
                {
                    sum.W = input[x, y].A / 255f;
                }

                return sum;
            }, linear, context, !preserve);
        }

        private static (int Width, int Height, float[] Kernel) ConvolutionKernel(XElement p)
        {
            Vector2 order = Pair(p, "order", 3);
            int width = (int)order.X, height = (int)order.Y;
            float[] kernel = SvgNumbers.List(Text(p, "kernelMatrix"));
            if (width <= 0 || height <= 0 || width > 31 || height > 31 || width != order.X || height != order.Y || kernel.Length != width * height)
            {
                throw new InvalidDataException("Invalid SVG convolution kernel (maximum 31 x 31).");
            }

            return (width, height, kernel);
        }

        private static PixelBuffer Displace(XElement p, PixelBuffer input, PixelBuffer second, Matrix3x2 units, bool linear, SceneRenderContext context)
        {
            float scale = Number(p, "scale");
            int xc = Channel(Text(p, "xChannelSelector", "A")), yc = Channel(Text(p, "yChannelSelector", "A"));
            return ScenePixels.Map(input, (x, y) =>
            {
                Vector4 control = ScenePixels.At(second, x, y, linear, premultiplied: false);
                Vector2 delta = Vector2.TransformNormal(new Vector2((control[xc] - .5f) * scale, (control[yc] - .5f) * scale), units);
                return ScenePixels.Sample(input, x + delta.X, y + delta.Y, linear);
            }, linear, context);
        }

        private static int Channel(string name) => name switch
        {
            "R" => 0,
            "G" => 1,
            "B" => 2,
            _ => 3
        };
        private static PixelBuffer Tile(PixelBuffer input, (float X, float Y, float Width, float Height) region, Matrix3x2 matrix, bool linear, SceneRenderContext context)
        {
            if (region.Width <= 0 || region.Height <= 0 || !Matrix3x2.Invert(matrix, out Matrix3x2 inverse))
            {
                return context.CreateBuffer(input.Width, input.Height);
            }

            return ScenePixels.Map(input, (x, y) =>
            {
                Vector2 local = Vector2.Transform(new Vector2(x + .5f, y + .5f), inverse);
                float Wrap(float v, float origin, float size) => origin + ((((v - origin) % size) + size) % size);
                local = new Vector2(Wrap(local.X, region.X, region.Width), Wrap(local.Y, region.Y, region.Height));
                Vector2 point = Vector2.Transform(local, matrix) - new Vector2(.5f);
                return ScenePixels.Sample(input, point.X, point.Y, linear);
            }, linear, context);
        }
    }
}
