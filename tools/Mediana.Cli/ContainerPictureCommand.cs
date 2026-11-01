// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers;
using Mediana.Core.Memory;
using Mediana.IO;

namespace Mediana.Cli
{
    internal static class ContainerPictureCommand
    {
        public static async Task<int> RunAsync(string inputPath, CommandOutputFile outputFile, int pictureIndex,
            TextWriter output, string? requiredFormat, CancellationToken cancellationToken)
        {
            MediaEngine engine = ContainerEngineFactory.CreateBuilder().Build();
            await using RandomAccessStreamByteSource source = new(File.OpenRead(inputPath));
            await using ContainerSession session = await engine.OpenContainerAsync(source, cancellationToken: cancellationToken);
            if (requiredFormat is not null && session.Info.FormatId != requiredFormat)
            {
                throw new NotSupportedException($"The selected command requires {requiredFormat} input; the detected container is {session.Info.FormatId}.");
            }
            IReadOnlyList<ContainerPictureInfo> pictures = session.Pictures
                ?? throw new NotSupportedException("The detected container reader does not expose pictures.");
            if (pictures.Count == 0)
            {
                throw new ArgumentException("The input contains no pictures in its inspected metadata.", nameof(pictureIndex));
            }
            ArgumentOutOfRangeException.ThrowIfNegative(pictureIndex);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(pictureIndex, pictures.Count);
            ContainerPictureInfo info = pictures[pictureIndex];
            switch (info.DataKind)
            {
                case PictureDataKind.Embedded:
                    break;
                case PictureDataKind.ExternalReference:
                    throw new NotSupportedException("The selected picture contains an external reference rather than embedded image bytes.");
                default:
                    throw new InvalidOperationException("The container returned an unsupported picture data kind.");
            }
            using (MediaBuffer picture = await session.ReadPictureAsync(pictureIndex, cancellationToken))
            {
                await session.DisposeAsync();
                await outputFile.WriteAsync(stream => stream.WriteAsync(picture.Memory, cancellationToken).AsTask(), cancellationToken);
            }
            if (session.AllocatedBufferBytes != 0)
            {
                throw new InvalidOperationException("Picture extraction retained media buffers.");
            }
            await output.WriteLineAsync($"Picture {pictureIndex}: declared {info.MediaType ?? "unspecified"}; {info.ByteLength} original bytes. Live buffer bytes: 0.");
            return CliExitCodes.Success;
        }
    }
}
