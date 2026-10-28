using Mediana.Core.Video;

namespace Mediana.Processing
{
    internal readonly record struct YuvRgbMatrix(double RedWeight, double BlueWeight, double LumaOffset,
        double LumaScale, double ChromaScale)
    {
        public static YuvRgbMatrix Create(MatrixCoefficients matrix, ColorRange range)
        {
            // Rec. ITU-T H.264, Table E-5 and equations E-35 through E-40.
            (double red, double blue) = matrix switch
            {
                MatrixCoefficients.Bt601 => (0.299, 0.114),
                MatrixCoefficients.Bt709 => (0.2126, 0.0722),
                MatrixCoefficients.Fcc => (0.30, 0.11),
                MatrixCoefficients.Smpte240M => (0.212, 0.087),
                MatrixCoefficients.Bt2020NonConstant => (0.2627, 0.0593),
                MatrixCoefficients.Unspecified or MatrixCoefficients.Rgb or MatrixCoefficients.YCgCo or
                    MatrixCoefficients.Bt2020Constant or MatrixCoefficients.Smpte2085 or
                    MatrixCoefficients.ChromaticityNonConstant or MatrixCoefficients.ChromaticityConstant or
                    MatrixCoefficients.ICtCp or MatrixCoefficients.IptC2 or MatrixCoefficients.YCgCoRe or
                    MatrixCoefficients.YCgCoRo => throw new NotSupportedException("This YCbCr matrix is unspecified or not supported."),
                _ => throw new ArgumentOutOfRangeException(nameof(matrix))
            };
            return range switch
            {
                ColorRange.Limited => new(red, blue, 16, 219, 224),
                ColorRange.Full => new(red, blue, 0, 255, 255),
                ColorRange.Unspecified => throw new NotSupportedException("YCbCr conversion requires an explicit color range."),
                _ => throw new ArgumentOutOfRangeException(nameof(range))
            };
        }

        public (double Red, double Green, double Blue) Convert(double luma, double cb, double cr)
        {
            double y = (luma - LumaOffset) / LumaScale;
            double blueDifference = (cb - 128) / ChromaScale;
            double redDifference = (cr - 128) / ChromaScale;
            double greenWeight = 1 - RedWeight - BlueWeight;
            double red = y + 2 * (1 - RedWeight) * redDifference;
            double blue = y + 2 * (1 - BlueWeight) * blueDifference;
            double green = y - 2 * BlueWeight * (1 - BlueWeight) / greenWeight * blueDifference
                - 2 * RedWeight * (1 - RedWeight) / greenWeight * redDifference;
            return (red, green, blue);
        }
    }
}
