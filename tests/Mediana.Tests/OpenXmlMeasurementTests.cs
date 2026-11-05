// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Documents.OpenXml;
using Xunit;

namespace Mediana.Tests
{
    public class OpenXmlMeasurementTests
    {
        [Theory]
        [InlineData("1440", OpenXmlNumericUnit.Twip, 72)]
        [InlineData("27", OpenXmlNumericUnit.HalfPoint, 13.5)]
        [InlineData("914400", OpenXmlNumericUnit.EnglishMetricUnit, 72)]
        [InlineData("25.4mm", OpenXmlNumericUnit.Twip, 72)]
        [InlineData("2.54cm", OpenXmlNumericUnit.Twip, 72)]
        [InlineData("0.5in", OpenXmlNumericUnit.Twip, 36)]
        [InlineData("9pt", OpenXmlNumericUnit.HalfPoint, 9)]
        [InlineData("1.5pc", OpenXmlNumericUnit.HalfPoint, 18)]
        [InlineData("1.5pi", OpenXmlNumericUnit.HalfPoint, 18)]
        [InlineData("-100", OpenXmlNumericUnit.Twip, -5)]
        public void ConvertsMeasurementsWithoutRounding(string input, OpenXmlNumericUnit unit, double expected) =>
            Assert.Equal(expected, OpenXmlMeasurement.ToPoints(input, unit), 10);

        [Fact]
        public void FractionalImplicitUnitsRequireExplicitPolicyAndReportRecovery()
        {
            Assert.Throws<InvalidDataException>(() => OpenXmlMeasurement.ToPoints("283.4645", OpenXmlNumericUnit.Twip));
            List<string> warnings = [];
            double points = OpenXmlMeasurement.ToPoints("283.4645", OpenXmlNumericUnit.Twip, true, warnings.Add);
            Assert.Equal(14.173225, points, 10);
            Assert.Single(warnings);
            warnings.Clear();
            Assert.Equal(5, OpenXmlMeasurement.ToPoints("100.0", OpenXmlNumericUnit.Twip, warningHandler: warnings.Add));
            Assert.Empty(warnings);
        }

        [Theory]
        [InlineData("NaN")]
        [InlineData("Infinity")]
        [InlineData("1e3")]
        [InlineData("12px")]
        [InlineData("12 pt")]
        [InlineData("1,5")]
        [InlineData("1.5.2")]
        public void RejectsMalformedOrUnknownUnits(string input) =>
            Assert.Throws<InvalidDataException>(() => OpenXmlMeasurement.ToPoints(input, OpenXmlNumericUnit.Twip, true));
    }
}
