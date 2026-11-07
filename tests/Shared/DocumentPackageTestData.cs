// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.IO.Compression;
using System.Text;
namespace Mediana.Tests
{
    internal static class DocumentPackageTestData
    {
        internal const string Relationships = "http://schemas.openxmlformats.org/package/2006/relationships";
        internal const string Office = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";
        internal static byte[] Create(string contentType, string main, Dictionary<string, string> parts, Dictionary<string, byte[]>? binaries = null)
        {
            using MemoryStream buffer = new();
            using (ZipArchive archive = new(buffer, ZipArchiveMode.Create, true))
            {
                Write("[Content_Types].xml", Encoding.UTF8.GetBytes("<Types xmlns='http://schemas.openxmlformats.org/package/2006/content-types'><Default Extension='xml' ContentType='application/xml'/><Default Extension='rels' ContentType='application/vnd.openxmlformats-package.relationships+xml'/><Default Extension='bin' ContentType='application/octet-stream'/><Override PartName='/" + main + "' ContentType='" + contentType + "'/></Types>"));
                Write("_rels/.rels", Encoding.UTF8.GetBytes("<Relationships xmlns='" + Relationships + "'><Relationship Id='main' Type='" + Office + "officeDocument' Target='" + main + "'/></Relationships>"));
                foreach ((string name, string xml) in parts) { Write(name, Encoding.UTF8.GetBytes(xml)); }
                if (binaries is not null) { foreach ((string name, byte[] bytes) in binaries) { Write(name, bytes); } }
                void Write(string name, byte[] bytes) { using Stream stream = archive.CreateEntry(name).Open(); stream.Write(bytes); }
            }
            return buffer.ToArray();
        }
        internal static string Relations(params (string Id, string Kind, string Target)[] entries) => "<Relationships xmlns='" + Relationships + "'>"
            + string.Concat(entries.Select(item => "<Relationship Id='" + item.Id + "' Type='" + Office + item.Kind + "' Target='" + item.Target + "'/>")) + "</Relationships>";
    }
}
