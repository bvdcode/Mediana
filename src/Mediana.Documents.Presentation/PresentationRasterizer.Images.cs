// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Core.Video;
using Mediana.Drawing;
using Mediana.Imaging;
using System.Numerics;
using System.Xml.Linq;
namespace Mediana.Documents.Presentation
{
    internal partial class PresentationRasterizer
    {
        private async ValueTask PictureAsync(XElement picture, double width, double height, Matrix3x2 transform)
        {
            XElement fill = picture.Elements().FirstOrDefault(item => item.Name.LocalName == "blipFill") ?? throw new InvalidDataException("Presentation picture has no image fill.");
            XElement? properties = PresentationXml.Properties(picture);
            XElement? geometry = properties?.Elements().FirstOrDefault(item => item.Name.LocalName is "prstGeom" or "custGeom");
            VectorPath path = PresentationGeometry.Path(geometry, width, height, false);
            await ImageFillAsync(path, fill, transform).ConfigureAwait(false);
            await PaintAsync(path, Fill(properties), transform).ConfigureAwait(false);
        }
        private async ValueTask TableAsync(XElement frame, Matrix3x2 transform)
        {
            XNamespace a = PresentationXml.Drawing(frame);
            XElement data = frame.Element(a + "graphic")?.Element(a + "graphicData") ?? throw new InvalidDataException("Empty presentation graphic frame.");
            XElement table = data.Element(a + "tbl") ?? throw new NotSupportedException("Only tables are supported in presentation graphic frames.");
            if (table.Element(a + "tblPr")?.Element(a + "tableStyleId") is not null)
            { throw new NotSupportedException("Presentation table theme styles require a table style definition."); }
            double[] columns = (table.Element(a + "tblGrid")?.Elements(a + "gridCol") ?? []).Select(item => PresentationXml.Points(item, "w")).ToArray();
            double y = 0;
            foreach (XElement row in table.Elements(a + "tr"))
            {
                token.ThrowIfCancellationRequested();
                double rowHeight = PresentationXml.Points(row, "h"), x = 0;
                int column = 0;
                foreach (XElement cell in row.Elements(a + "tc"))
                {
                    if (column >= columns.Length) { throw new InvalidDataException("Presentation table row exceeds its grid."); }
                    int span = checked((int)PresentationXml.Number(cell, "gridSpan", 1));
                    if (span < 1 || span > columns.Length - column) { throw new InvalidDataException("Invalid presentation table cell span."); }
                    double cellWidth = columns.AsSpan(column, span).ToArray().Sum();
                    Matrix3x2 cellTransform = Matrix3x2.CreateTranslation((float)x, (float)y) * transform;
                    await PaintAsync(new VectorPath().Rectangle(0, 0, (float)cellWidth, (float)rowHeight), Fill(cell.Element(a + "tcPr")), cellTransform).ConfigureAwait(false);
                    if (cell.Element(a + "txBody") is XElement text) { DrawText(text, [], cellWidth, rowHeight, cellTransform); }
                    x += columns[column++];
                }
                y += rowHeight;
            }
        }
    }
}
