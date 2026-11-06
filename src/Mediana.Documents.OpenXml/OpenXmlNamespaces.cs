// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Documents.OpenXml
{
    public static class OpenXmlNamespaces
    {
        public const string OfficeRelationships = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        public const string StrictOfficeRelationships = "http://purl.oclc.org/ooxml/officeDocument/relationships";
        public const string Spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        public const string StrictSpreadsheet = "http://purl.oclc.org/ooxml/spreadsheetml/main";
        public const string Presentation = "http://schemas.openxmlformats.org/presentationml/2006/main";
        public const string StrictPresentation = "http://purl.oclc.org/ooxml/presentationml/main";
        public const string Drawing = "http://schemas.openxmlformats.org/drawingml/2006/main";
        public const string StrictDrawing = "http://purl.oclc.org/ooxml/drawingml/main";
        public static bool IsOfficeRelationship(string type, string name) => type == OfficeRelationships + "/" + name
            || type == StrictOfficeRelationships + "/" + name;
    }
}
