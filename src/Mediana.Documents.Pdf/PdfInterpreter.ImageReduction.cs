// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Documents.Pdf
{
    internal partial class PdfInterpreter
    {
        private int ImageReduction(PdfObject obj, string codec, int width, int height, int depth)
        {
            if (samplesPerPoint is not double scale || codec != "DCTDecode" || depth != 0
                || Get(obj.Value, "ImageMask").Number != 0 || obj.Value.Get("SMask").Kind != PdfValueKind.Null
                || obj.Value.Get("Mask").Kind != PdfValueKind.Null)
            {
                return 1;
            }
            double targetWidth = Math.Max(1, Math.Sqrt((double)_state.Transform.M11 * _state.Transform.M11
                + (double)_state.Transform.M12 * _state.Transform.M12) * scale);
            double targetHeight = Math.Max(1, Math.Sqrt((double)_state.Transform.M21 * _state.Transform.M21
                + (double)_state.Transform.M22 * _state.Transform.M22) * scale);
            int reduction = 1;
            while (reduction < 8 && (width - 1) / (reduction * 2) + 1 >= targetWidth
                && (height - 1) / (reduction * 2) + 1 >= targetHeight)
            {
                reduction *= 2;
            }
            return reduction;
        }
    }
}
