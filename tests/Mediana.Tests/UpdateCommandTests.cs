// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Globalization;
using Mediana.Cli;

namespace Mediana.Tests
{
    [Collection("CommandLine")]
    public class UpdateCommandTests
    {
        [Theory]
        [InlineData("update")]
        [InlineData("--update")]
        [InlineData("-u")]
        public async Task UpdateHelpExplainsCheckWithoutNetworkAccess(string command)
        {
            using StringWriter output = new(CultureInfo.InvariantCulture);
            using StringWriter error = new(CultureInfo.InvariantCulture);
            int code = await CliApplication.RunAsync([command, "--help"], output, error);
            Assert.Equal(CliExitCodes.Success, code);
            Assert.Contains("--check", output.ToString(), StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("update")]
        [InlineData("--update")]
        [InlineData("-u")]
        public async Task ManagedInvocationCannotReplaceTheHostExecutable(string command)
        {
            using StringWriter output = new(CultureInfo.InvariantCulture);
            using StringWriter error = new(CultureInfo.InvariantCulture);
            int code = await CliApplication.RunAsync([command], output, error);
            Assert.Equal(CliExitCodes.Unsupported, code);
        }

        [Theory]
        [InlineData("update")]
        [InlineData("--update")]
        [InlineData("-u")]
        public async Task MediaArgumentsAreRejectedByUpdate(string command)
        {
            using StringWriter output = new(CultureInfo.InvariantCulture);
            using StringWriter error = new(CultureInfo.InvariantCulture);
            int code = await CliApplication.RunAsync([command, "input.mp4"], output, error);
            Assert.Equal(CliExitCodes.Usage, code);
        }
    }
}
