// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Globalization;
using Mediana.Cli;

namespace Mediana.Tests
{
    [Collection("CommandLine")]
    public class UpdateCommandTests
    {
        [Fact]
        public async Task UpdateHelpExplainsCheckWithoutNetworkAccess()
        {
            using StringWriter output = new(CultureInfo.InvariantCulture);
            using StringWriter error = new(CultureInfo.InvariantCulture);
            int code = await CliApplication.RunAsync(["update", "--help"], output, error);
            Assert.Equal(CliExitCodes.Success, code);
            Assert.Contains("--check", output.ToString(), StringComparison.Ordinal);
        }

        [Fact]
        public async Task ManagedInvocationCannotReplaceTheHostExecutable()
        {
            using StringWriter output = new(CultureInfo.InvariantCulture);
            using StringWriter error = new(CultureInfo.InvariantCulture);
            int code = await CliApplication.RunAsync(["update"], output, error);
            Assert.Equal(CliExitCodes.Unsupported, code);
        }

        [Fact]
        public async Task MediaArgumentsAreRejectedByUpdate()
        {
            using StringWriter output = new(CultureInfo.InvariantCulture);
            using StringWriter error = new(CultureInfo.InvariantCulture);
            int code = await CliApplication.RunAsync(["update", "input.mp4"], output, error);
            Assert.Equal(CliExitCodes.Usage, code);
        }
    }
}
