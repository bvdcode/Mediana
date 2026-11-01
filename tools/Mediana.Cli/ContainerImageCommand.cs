// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.IO;

namespace Mediana.Cli
{
    internal static class ContainerImageCommand
    {
        public static async Task<int> RunAsync(string inputPath, CommandOutputFile outputFile, ContainerImageSource first,
            TextWriter output, CancellationToken token)
        {
            ContainerImageSearchOptions options = first switch
            {
                ContainerImageSource.Attachment => new([ContainerImageSource.Attachment, ContainerImageSource.Picture]),
                ContainerImageSource.Picture => new([ContainerImageSource.Picture, ContainerImageSource.Attachment]),
                _ => throw new ArgumentOutOfRangeException(nameof(first))
            };
            MediaEngine engine = ImageEngineFactory.AddStillImageDecoders(ContainerEngineFactory.CreateBuilder()).Build();
            await using RandomAccessStreamByteSource source = new(File.OpenRead(inputPath));
            await using ContainerSession session = await engine.OpenContainerAsync(source, cancellationToken: token);
            string selection;
            using (ContainerImage image = await engine.ReadContainerImageAsync(session, options, token)
                ?? throw new NotSupportedException("No registered still-image decoder recognizes an embedded picture or attachment."))
            {
                await session.DisposeAsync();
                selection = $"Image source: {image.Source}; index: {image.Index}; recognized by {image.DecoderImplementationId}; {image.Data.Length} original bytes.";
                await outputFile.WriteAsync(stream => stream.WriteAsync(image.Data, token).AsTask(), token);
            }
            if (session.AllocatedBufferBytes != 0)
            {
                throw new InvalidOperationException("Container image selection retained media buffers.");
            }
            await output.WriteLineAsync(selection);
            await output.WriteLineAsync("Live buffer bytes: 0.");
            return CliExitCodes.Success;
        }
    }
}
