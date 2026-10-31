// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Globalization;
using Mediana.Cli;
using Mediana.Core.Metadata;

namespace Mediana.Tests
{
    [Collection("CommandLine")]
    public class CliDateTimeTests
    {
        [Theory]
        [InlineData("header-dates.mp4", "2026-03-07T12:34:56.0000000Z")]
        [InlineData("header-dates-future.mov", "2050-02-03T04:05:06.0000000 (time zone unspecified)")]
        [InlineData("header-dates-scopes.mp4", "2021-07-08T09:10:11.0000000Z")]
        public async Task ProbePrintsRealCalendarDatesWithTheirOriginalScopesAndZoneBasis(string file, string date)
        {
            string input = Path.Combine(AppContext.BaseDirectory, "Data", "Metadata", file);
            (int code, string output, string error) = await CliTests.RunAsync(["probe", input]);
            Assert.True(code == 0, error);
            Assert.Contains("Date \"mvhd\"/\"creation_time\" source=\"moov/mvhd\": " + date, output, StringComparison.Ordinal);
            Assert.Contains("source=\"trak/mdia/mdhd\"", output, StringComparison.Ordinal);
            Assert.DoesNotContain("18446744073709551615", output, StringComparison.Ordinal);
        }

        [Fact]
        public async Task GenericDateFormattingRetainsZonePrecisionAndEscapesNativeFields()
        {
            DateTime value = new DateTime(2026, 3, 7, 12, 34, 56, DateTimeKind.Utc).AddTicks(1234567);
            MediaDateTimeTag utc = new("n\n\u001b", "created\t", value, "p\r\u009b");
            MediaDateTimeTag unknown = new("native", "created", DateTime.SpecifyKind(value, DateTimeKind.Unspecified));
            using StringWriter output = new(CultureInfo.GetCultureInfo("ru-RU"));
            await MediaDateTimeFormatter.WriteAsync([utc, unknown], output, "  ", default);
            string[] lines = output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(2, lines.Length);
            Assert.StartsWith("  Date ", lines[0], StringComparison.Ordinal);
            Assert.EndsWith("2026-03-07T12:34:56.1234567Z", lines[0], StringComparison.Ordinal);
            Assert.EndsWith("2026-03-07T12:34:56.1234567 (time zone unspecified)", lines[1], StringComparison.Ordinal);
            Assert.DoesNotContain('\t', lines[0]);
            Assert.DoesNotContain('\r', lines[0]);
            Assert.DoesNotContain('\u001b', lines[0]);
            Assert.DoesNotContain('\u009b', lines[0]);
        }

        [Fact]
        public async Task UnavailableEmptyAndCancelledDateOutputDoesNotInventValues()
        {
            using StringWriter output = new(CultureInfo.InvariantCulture);
            await MediaDateTimeFormatter.WriteAsync(null, output, "", default);
            await MediaDateTimeFormatter.WriteAsync([], output, "", default);
            Assert.Empty(output.ToString());
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => MediaDateTimeFormatter.WriteAsync([], output, "", new CancellationToken(true)));
        }
    }
}
