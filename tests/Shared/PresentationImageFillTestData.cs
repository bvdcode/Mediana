// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Png;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Documents.Presentation;

namespace Mediana.Tests
{
    internal static class PresentationImageFillTestData
    {
        private const string P = "http://schemas.openxmlformats.org/presentationml/2006/main";
        private const string A = "http://schemas.openxmlformats.org/drawingml/2006/main";
        private static string Header => "xmlns:p='" + P + "' xmlns:a='" + A + "' xmlns:r='" + DocumentPackageTestData.Office.TrimEnd('/') + "'";

        internal static byte[] Create(PresentationFillLocation location = PresentationFillLocation.Slide, string? mode = "<a:stretch/>",
            string geometry = "rect", string crop = "", string effects = "", int rotation = 0, bool strict = false, bool duplicate = false, byte[]? image = null)
        {
            string fill = "<a:blipFill dpi='72'><a:blip r:embed='texture'>" + effects + "</a:blip>" + crop + mode + "</a:blipFill>";
            string solid = "<a:solidFill><a:srgbClr val='00FF00'/></a:solidFill>";
            string shape = Shape(location == PresentationFillLocation.Slide ? fill : "", geometry, rotation);
            if (location == PresentationFillLocation.Theme) { shape = shape.Replace("</p:spPr>", "</p:spPr><p:style><a:fillRef idx='1'/></p:style>", StringComparison.Ordinal); }
            if (location == PresentationFillLocation.Cell)
            { shape = "<p:graphicFrame><p:nvGraphicFramePr/><p:xfrm>" + Offset() + "</p:xfrm><a:graphic><a:graphicData><a:tbl><a:tblGrid><a:gridCol w='304800'/></a:tblGrid><a:tr h='203200'><a:tc><a:tcPr>" + fill + "</a:tcPr></a:tc></a:tr></a:tbl></a:graphicData></a:graphic></p:graphicFrame>"; }
            if (location == PresentationFillLocation.Picture)
            { shape = "<p:pic><p:nvPicPr/><p:blipFill dpi='72'><a:blip r:embed='texture'>" + effects + "</a:blip>" + crop + mode + "</p:blipFill><p:spPr><a:xfrm rot='" + rotation + "'>" + Offset() + "</a:xfrm><a:prstGeom prst='" + geometry + "'/></p:spPr></p:pic>"; }
            string localRelationship = location is PresentationFillLocation.Slide or PresentationFillLocation.Cell or PresentationFillLocation.Picture ? "../media/pattern.png" : "../media/decoy.png";
            string layoutRelationship = location == PresentationFillLocation.Layout ? "../media/pattern.png" : "../media/decoy.png";
            string masterRelationship = location is PresentationFillLocation.Master or PresentationFillLocation.Background ? "../media/pattern.png" : "../media/decoy.png";
            string masterFill = location switch
            {
                PresentationFillLocation.Master => fill,
                PresentationFillLocation.Slide or PresentationFillLocation.Layout => solid,
                PresentationFillLocation.Theme or PresentationFillLocation.Background or PresentationFillLocation.Cell or PresentationFillLocation.Picture => "",
                _ => throw new ArgumentOutOfRangeException(nameof(location))
            };
            Dictionary<string, string> parts = new()
            {
                ["ppt/main.xml"] = "<p:presentation " + Header + "><p:sldIdLst><p:sldId id='256' r:id='slide'/></p:sldIdLst><p:sldSz cx='812800' cy='609600'/></p:presentation>",
                ["ppt/_rels/main.xml.rels"] = DocumentPackageTestData.Relations(("slide", "slide", "slides/main.xml")),
                ["ppt/slides/main.xml"] = "<p:sld " + Header + "><p:cSld><p:spTree>" + (location == PresentationFillLocation.Background ? "" : shape) + (duplicate ? shape.Replace("x='203200'", "x='533400'", StringComparison.Ordinal) : "") + "</p:spTree></p:cSld></p:sld>",
                ["ppt/slides/_rels/main.xml.rels"] = DocumentPackageTestData.Relations(("layout", "slideLayout", "../layouts/main.xml"), ("texture", "image", localRelationship)),
                ["ppt/layouts/main.xml"] = "<p:sldLayout " + Header + "><p:cSld><p:spTree>" + Shape(location == PresentationFillLocation.Layout ? fill : "", geometry, rotation) + "</p:spTree></p:cSld></p:sldLayout>",
                ["ppt/layouts/_rels/main.xml.rels"] = DocumentPackageTestData.Relations(("master", "slideMaster", "../masters/main.xml"), ("texture", "image", layoutRelationship)),
                ["ppt/masters/main.xml"] = "<p:sldMaster " + Header + "><p:cSld>" + (location == PresentationFillLocation.Background ? "<p:bg><p:bgPr>" + fill + "</p:bgPr></p:bg>" : "") + "<p:spTree>" + Shape(masterFill, geometry, rotation) + "</p:spTree></p:cSld></p:sldMaster>",
                ["ppt/masters/_rels/main.xml.rels"] = DocumentPackageTestData.Relations(("theme", "theme", "../theme/main.xml"), ("texture", "image", masterRelationship)),
                ["ppt/theme/main.xml"] = "<a:theme " + Header + "><a:themeElements><a:fmtScheme><a:fillStyleLst>" + fill + "</a:fillStyleLst></a:fmtScheme></a:themeElements></a:theme>",
                ["ppt/theme/_rels/main.xml.rels"] = DocumentPackageTestData.Relations(("texture", "image", "../media/pattern.png"))
            };
            if (strict)
            {
                parts = parts.ToDictionary(item => item.Key, item => item.Value.Replace(P, "http://purl.oclc.org/ooxml/presentationml/main", StringComparison.Ordinal)
                    .Replace(A, "http://purl.oclc.org/ooxml/drawingml/main", StringComparison.Ordinal)
                    .Replace(DocumentPackageTestData.Office.TrimEnd('/'), "http://purl.oclc.org/ooxml/officeDocument/relationships", StringComparison.Ordinal));
            }
            return DocumentPackageTestData.Create(PresentationReader.DocumentContentType, "ppt/main.xml", parts,
                new() { ["ppt/media/pattern.png"] = image ?? EncodePattern(), ["ppt/media/decoy.png"] = EncodePattern(true) });
        }

        private static string Offset() => "<a:off x='203200' y='152400'/><a:ext cx='304800' cy='203200'/>";
        private static string Shape(string fill, string geometry, int rotation) => "<p:sp><p:nvSpPr><p:cNvPr id='2' name='Texture'/><p:cNvSpPr/><p:nvPr><p:ph type='body' idx='4'/></p:nvPr></p:nvSpPr><p:spPr><a:xfrm rot='" + rotation + "'>" + Offset() + "</a:xfrm><a:prstGeom prst='" + geometry + "'/>" + fill + "</p:spPr></p:sp>";
        private static byte[] EncodePattern(bool decoy = false)
        {
            byte[] pixels = decoy ? Enumerable.Repeat((byte)255, 32).ToArray() :
                [255, 0, 0, 255, 0, 255, 0, 255, 0, 0, 255, 255, 255, 255, 0, 255,
                 0, 255, 255, 255, 255, 0, 255, 255, 0, 0, 0, 255, 255, 255, 255, 255];
            MediaBufferAllocator allocator = new(4096);
            using VideoFrame frame = RgbaFrame.CopyFrom(pixels, 4, 2, VideoColorInfo.Srgb, allocator, SampleAspectRatio.Square);
            using MediaBuffer encoded = PngEncoder.Encode(frame, allocator);
            return encoded.Memory.ToArray();
        }
    }
}
