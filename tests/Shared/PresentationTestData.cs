// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Documents.Presentation;
namespace Mediana.Tests
{
    internal static class PresentationTestData
    {
        private const string P = "http://schemas.openxmlformats.org/presentationml/2006/main";
        private const string A = "http://schemas.openxmlformats.org/drawingml/2006/main";
        internal static byte[] Create(bool picture = false, bool badSecond = false, bool strict = false, string shapeColor = "FF0000", string? colorTransform = null, string? titleText = null, string? titleBodyProperties = null)
        {
            string header = "xmlns:p='" + P + "' xmlns:a='" + A + "' xmlns:r='" + DocumentPackageTestData.Office.TrimEnd('/') + "'";
            Dictionary<string, string> parts = new()
            {
                ["ppt/main.xml"] = "<p:presentation " + header + "><p:sldIdLst><p:sldId id='256' r:id='one'/><p:sldId id='257' r:id='two'/></p:sldIdLst><p:sldSz cx='1828800' cy='1371600'/><p:defaultTextStyle><a:lvl1pPr><a:defRPr sz='1200'><a:latin typeface='+mn-lt'/></a:defRPr></a:lvl1pPr></p:defaultTextStyle></p:presentation>",
                ["ppt/_rels/main.xml.rels"] = DocumentPackageTestData.Relations(("one", "slide", "slides/one.xml"), ("two", "slide", "slides/two.xml")),
                ["ppt/slides/one.xml"] = "<p:sld " + header + "><p:cSld><p:spTree><p:nvGrpSpPr/><p:grpSpPr/>" + Rectangle(0, 0, 12, 12, shapeColor, colorTransform)
                    + "<p:sp><p:nvSpPr><p:cNvPr id='2' name='Title'/><p:cNvSpPr/><p:nvPr><p:ph type='title' idx='4'/></p:nvPr></p:nvSpPr><p:spPr><a:noFill/></p:spPr><p:txBody><a:bodyPr lIns='0' rIns='0' tIns='0' bIns='0'" + (titleBodyProperties ?? "/") + "><a:lstStyle/><a:p><a:r><a:t>" + (titleText ?? "Привет ● ▪") + "</a:t></a:r></a:p></p:txBody></p:sp>"
                    + (picture ? "<p:pic><p:nvPicPr><p:cNvPr id='3' name='Picture'/><p:cNvPicPr/><p:nvPr/></p:nvPicPr><p:blipFill><a:blip r:embed='image'/><a:stretch><a:fillRect/></a:stretch></p:blipFill><p:spPr>" + Transform(120, 60, 10, 10) + "<a:prstGeom prst='rect'/></p:spPr></p:pic>" : "") + "</p:spTree></p:cSld></p:sld>",
                ["ppt/slides/two.xml"] = badSecond ? "<broken" : "<p:sld " + header + "><p:cSld><p:spTree>" + Rectangle(0, 0, 144, 108, "0000FF") + "</p:spTree></p:cSld></p:sld>",
                ["ppt/slides/_rels/one.xml.rels"] = DocumentPackageTestData.Relations(("layout", "slideLayout", "../layouts/main.xml"), ("image", "image", "../media/image.bin")),
                ["ppt/layouts/main.xml"] = "<p:sldLayout " + header + "><p:cSld><p:spTree>" + Rectangle(0, 96, 12, 12, "0000FF")
                    + "<p:sp><p:nvSpPr><p:cNvPr id='1' name='Title'/><p:cNvSpPr/><p:nvPr><p:ph type='title' idx='4'/></p:nvPr></p:nvSpPr><p:spPr>" + Transform(20, 20, 100, 45) + "</p:spPr><p:txBody><a:bodyPr/><a:lstStyle/><a:p><a:r><a:t>Placeholder prompt</a:t></a:r></a:p></p:txBody></p:sp></p:spTree></p:cSld></p:sldLayout>",
                ["ppt/layouts/_rels/main.xml.rels"] = DocumentPackageTestData.Relations(("master", "slideMaster", "../masters/main.xml")),
                ["ppt/masters/main.xml"] = "<p:sldMaster " + header + "><p:cSld><p:bg><p:bgPr><a:solidFill><a:schemeClr val='bg1'/></a:solidFill></p:bgPr></p:bg><p:spTree>" + Rectangle(132, 96, 12, 12, "00FF00") + "</p:spTree></p:cSld><p:clrMap bg1='lt1' tx1='dk1'/></p:sldMaster>",
                ["ppt/masters/_rels/main.xml.rels"] = DocumentPackageTestData.Relations(("theme", "theme", "../theme/main.xml")),
                ["ppt/theme/main.xml"] = "<a:theme xmlns:a='" + A + "'><a:themeElements><a:clrScheme name='Reference'><a:lt1><a:srgbClr val='FFFFFF'/></a:lt1><a:dk1><a:srgbClr val='000000'/></a:dk1></a:clrScheme><a:fontScheme name='Reference'><a:majorFont><a:latin typeface='Canada1500'/></a:majorFont><a:minorFont><a:latin typeface='Canada1500'/></a:minorFont></a:fontScheme></a:themeElements></a:theme>"
            };
            if (strict)
            {
                parts = parts.ToDictionary(item => item.Key, item => item.Value.Replace(P, "http://purl.oclc.org/ooxml/presentationml/main", StringComparison.Ordinal)
                    .Replace(A, "http://purl.oclc.org/ooxml/drawingml/main", StringComparison.Ordinal)
                    .Replace(DocumentPackageTestData.Office, "http://purl.oclc.org/ooxml/officeDocument/relationships/", StringComparison.Ordinal));
                // Relationship attributes do not end with a slash.
                parts = parts.ToDictionary(item => item.Key, item => item.Value.Replace(DocumentPackageTestData.Office.TrimEnd('/'), "http://purl.oclc.org/ooxml/officeDocument/relationships", StringComparison.Ordinal));
            }
            return DocumentPackageTestData.Create(PresentationReader.DocumentContentType, "ppt/main.xml", parts, picture ? new() { ["ppt/media/image.bin"] = "PIX"u8.ToArray() } : null);
        }
        private static string Transform(int x, int y, int width, int height) => "<a:xfrm><a:off x='" + (x * 12700) + "' y='" + (y * 12700) + "'/><a:ext cx='" + (width * 12700) + "' cy='" + (height * 12700) + "'/></a:xfrm>";
        private static string Rectangle(int x, int y, int width, int height, string color, string? colorTransform = null) => "<p:sp><p:nvSpPr><p:cNvPr id='10' name='Rectangle'/><p:cNvSpPr/><p:nvPr/></p:nvSpPr><p:spPr>" + Transform(x, y, width, height) + "<a:prstGeom prst='rect'/><a:solidFill><a:srgbClr val='" + color + "'>" + colorTransform + "</a:srgbClr></a:solidFill></p:spPr></p:sp>";
    }
}
