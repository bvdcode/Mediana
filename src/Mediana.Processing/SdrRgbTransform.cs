using Mediana.Core.Video;

namespace Mediana.Processing
{
    internal readonly struct SdrRgbTransform
    {
        private readonly RgbMatrix matrix;
        private readonly TransferFunction transfer;

        public SdrRgbTransform(ColorPrimaries primaries, TransferFunction transfer)
        {
            matrix = RgbPrimariesTransform.ToSrgb(primaries);
            SdrTransfer.Decode(0, transfer);
            this.transfer = transfer;
        }

        public void Write(Span<byte> destination, double red, double green, double blue, byte alpha)
        {
            (double r, double g, double b) = matrix.Transform(SdrTransfer.Decode(red, transfer),
                SdrTransfer.Decode(green, transfer), SdrTransfer.Decode(blue, transfer));
            destination[0] = SdrTransfer.EncodeSrgb(r);
            destination[1] = SdrTransfer.EncodeSrgb(g);
            destination[2] = SdrTransfer.EncodeSrgb(b);
            destination[3] = alpha;
        }
    }
}
