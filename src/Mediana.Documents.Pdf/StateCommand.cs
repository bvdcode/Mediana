// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;

namespace Mediana.Documents.Pdf
{
    internal record StateCommand(bool Save) : IPdfDrawCommand
    {
        public void Draw(RasterCanvas canvas, PdfDrawingContext context)
        {
            if (Save)
            {
                canvas.Save();
            }
            else
            {
                canvas.Restore();
            }
        }
    }
}
