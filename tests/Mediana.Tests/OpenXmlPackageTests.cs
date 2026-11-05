// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Documents.OpenXml;
using System.Xml;

namespace Mediana.Tests
{
    public class OpenXmlPackageTests
    {
        [Fact]
        public void PartNamesMatchWithoutCaseAndRetainStoredSpelling()
        {
            using OpenXmlPackage package = OpenXmlPackage.Open(OpenXmlPackageTestInput.Create(target: "CONTENT/REPORT.XML"));
            Assert.Equal("content/report.xml", package.MainPart());
            Assert.Equal("content/report.xml", package.CanonicalPart("CONTENT/report.XML"));
            Assert.Equal("application/xml", package.ContentType("CONTENT/REPORT.XML"));
            Assert.NotNull(package.ReadXml("CONTENT/REPORT.XML").Root);
        }

        [Fact]
        public void CaseEquivalentPartsAreRejected()
        {
            Assert.Throws<InvalidDataException>(() => OpenXmlPackage.Open(OpenXmlPackageTestInput.Create(extra: "CONTENT/REPORT.XML")));
        }

        [Fact]
        public void PartLimitIsCheckedBeforeReadingAndDoesNotConsumeBudget()
        {
            using OpenXmlPackage package = OpenXmlPackage.Open(OpenXmlPackageTestInput.Create());
            Assert.Throws<InvalidDataException>(() => package.ReadPart(package.MainPart(), maximumBytes: 1));
            Assert.Equal("<document/>", System.Text.Encoding.UTF8.GetString(package.ReadPart(package.MainPart())));
        }

        [Fact]
        public void RelatedPartsPreserveSourceMemory() => OpenXmlPackageTestInput.Verify();

        [Theory]
        [InlineData("../../outside.xml")]
        [InlineData("https://example.test/data")]
        [InlineData("../%2e%2e/outside.xml")]
        [InlineData("../%2foutside.xml")]
        [InlineData("../%5coutside.xml")]
        [InlineData("../image.xml#fragment")]
        public void InternalTargetsCannotEscapeOrOpenExternalResources(string target)
        {
            Assert.Throws<InvalidDataException>(() => OpenXmlPackage.Resolve("word/document.xml", new("r1", "image", target, false)));
        }

        [Fact]
        public void ExternalRelationshipsCannotBeOpened() => Assert.Throws<NotSupportedException>(() =>
            OpenXmlPackage.Resolve("word/document.xml", new("r1", "image", "https://example.test/image.png", true)));

        [Theory]
        [InlineData("content/report.xml")]
        [InlineData("content/./report.xml")]
        public void DuplicateAndNoncanonicalNamesAreRejected(string name) => Assert.Throws<InvalidDataException>(() =>
            OpenXmlPackage.Open(OpenXmlPackageTestInput.Create(extra: name)));

        [Fact]
        public void InputAndInflatedPartBoundsAreChecked()
        {
            byte[] encoded = OpenXmlPackageTestInput.Create();
            Assert.Throws<InvalidDataException>(() => OpenXmlPackage.Open(encoded, new() { MaximumInputBytes = encoded.Length - 1 }));
            Assert.Throws<InvalidDataException>(() => OpenXmlPackage.Open(encoded, new() { MaximumParts = 2 }));
            Assert.Throws<InvalidDataException>(() => OpenXmlPackage.Open(encoded, new() { MaximumPartBytes = 10 }));
            Assert.Throws<InvalidDataException>(() => OpenXmlPackage.Open(encoded, new() { MaximumUncompressedBytes = 10 }));
        }

        [Fact]
        public void XmlDtdDepthAndElementLimitsAreCheckedBeforeConsumingNodes()
        {
            using OpenXmlPackage dtd = OpenXmlPackage.Open(OpenXmlPackageTestInput.Create("<!DOCTYPE a [<!ENTITY b 'test'>]><a>&b;</a>"));
            Assert.Throws<XmlException>(() => dtd.ReadXml(dtd.MainPart()));
            using OpenXmlPackage depth = OpenXmlPackage.Open(OpenXmlPackageTestInput.Create("<a><b><c><d/></c></b></a>"), new() { MaximumXmlDepth = 2 });
            Assert.Throws<InvalidDataException>(() => depth.ReadXml(depth.MainPart()));
            Assert.Throws<InvalidDataException>(() => OpenXmlPackage.Open(OpenXmlPackageTestInput.Create(), new() { MaximumXmlElements = 1 }));
        }

        [Fact]
        public void XmlElementBudgetIncludesRepeatedPartReadsAndExcludesAttributes()
        {
            const string xml = "<a xmlns='urn:sample' x='1'><b y='2'/></a>";
            using OpenXmlPackage package = OpenXmlPackage.Open(OpenXmlPackageTestInput.Create(xml), new() { MaximumXmlElements = 7 });
            Assert.Equal("urn:sample", package.ReadXml("content/report.xml").Root!.Name.NamespaceName);
            Assert.Equal("1", package.ReadXml("content/report.xml").Root!.Attribute("x")!.Value);
            Assert.Throws<InvalidDataException>(() => package.ReadXml("content/report.xml"));
        }

        [Fact]
        public void XmlSinglePassPreservesNamespacesWhitespaceAndMixedContent()
        {
            const string xml = "<?xml version='1.0'?><a xmlns='urn:sample' xmlns:p='urn:other' xml:space='preserve'> x<!--note--><![CDATA[y]]><p:b p:c='z'/> </a>";
            using OpenXmlPackage package = OpenXmlPackage.Open(OpenXmlPackageTestInput.Create(xml));
            System.Xml.Linq.XElement element = package.ReadXml("content/report.xml").Root!;
            Assert.Equal(" xy ", element.Value);
            Assert.Equal("preserve", element.Attribute(System.Xml.Linq.XNamespace.Xml + "space")!.Value);
            Assert.Equal("z", element.Element(System.Xml.Linq.XName.Get("b", "urn:other"))!.Attribute(System.Xml.Linq.XName.Get("c", "urn:other"))!.Value);
            Assert.IsType<System.Xml.Linq.XCData>(element.Nodes().ElementAt(2));
        }

        [Fact]
        public void MissingMainPartAndRepeatedReadBudgetAreRejected()
        {
            using OpenXmlPackage package = OpenXmlPackage.Open(OpenXmlPackageTestInput.Create(target: "missing.xml"));
            Assert.Throws<InvalidDataException>(() => package.MainPart());
            using OpenXmlPackage bounded = OpenXmlPackage.Open(OpenXmlPackageTestInput.Create(new string('a', 200)), new() { MaximumUncompressedBytes = 1000 });
            for (int i = 0; i < 3; i++) { bounded.ReadPart("content/report.xml"); }
            Assert.Throws<InvalidDataException>(() => bounded.ReadPart("content/report.xml"));
        }

        [Fact]
        public void DisposalAndCancellationAreExplicit()
        {
            OpenXmlPackage package = OpenXmlPackage.Open(OpenXmlPackageTestInput.Create());
            package.Dispose();
            package.Dispose();
            Assert.Throws<ObjectDisposedException>(() => package.ReadPart("content/report.xml"));
            using CancellationTokenSource cancel = new();
            cancel.Cancel();
            Assert.Throws<OperationCanceledException>(() => OpenXmlPackage.Open(OpenXmlPackageTestInput.Create(), cancellationToken: cancel.Token));
        }
    }
}
