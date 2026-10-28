using Mediana.Core.Video;

namespace Mediana.Processing
{
    internal static class SdrTransfer
    {
        public static double Decode(double encoded, TransferFunction transfer)
        {
            double value = Math.Clamp(encoded, 0, 1);
            return transfer switch
            {
                // Ideal BT.1886 reference display: normalized white 1, black 0, gamma 2.4.
                TransferFunction.Bt709 or TransferFunction.Smpte240M => Math.Pow(value, 2.4),
                TransferFunction.Srgb => value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4),
                TransferFunction.Linear => value,
                TransferFunction.Gamma22 => Math.Pow(value, 2.2),
                TransferFunction.Gamma28 => Math.Pow(value, 2.8),
                TransferFunction.Pq or TransferFunction.Hlg => throw new NotSupportedException("HDR conversion requires a tone mapping operation."),
                TransferFunction.Unspecified or TransferFunction.Log100 or TransferFunction.Log316 or TransferFunction.Iec61966_2_4 or
                    TransferFunction.Bt1361Extended or TransferFunction.Smpte428 => throw new NotSupportedException("The source transfer function is unspecified or not supported."),
                _ => throw new ArgumentOutOfRangeException(nameof(transfer))
            };
        }

        // IEC 61966-2-1, as specified by W3C CSS Color 4, section 10.2.
        public static byte EncodeSrgb(double linear)
        {
            double value = Math.Clamp(linear, 0, 1);
            double encoded = value <= 0.0031308 ? 12.92 * value : 1.055 * Math.Pow(value, 1 / 2.4) - 0.055;
            return (byte)Math.Clamp((int)Math.Floor(encoded * 255 + 0.5), 0, 255);
        }
    }
}
