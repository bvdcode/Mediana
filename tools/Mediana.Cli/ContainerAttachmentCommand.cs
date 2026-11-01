// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers;
using Mediana.Core.Memory;
using Mediana.IO;

namespace Mediana.Cli
{
    internal static class ContainerAttachmentCommand
    {
        public static async Task<int> RunAsync(string inputPath, CommandOutputFile outputFile, int index,
            TextWriter output, CancellationToken cancellationToken)
        {
            MediaEngine engine = ContainerEngineFactory.CreateBuilder().Build();
            await using RandomAccessStreamByteSource source = new(File.OpenRead(inputPath));
            await using ContainerSession session = await engine.OpenContainerAsync(source, cancellationToken: cancellationToken);
            IReadOnlyList<ContainerAttachmentInfo> attachments = session.Attachments
                ?? throw new NotSupportedException("The detected container reader does not expose attachments.");
            if (attachments.Count == 0)
            {
                throw new ArgumentException("The input contains no attached files.", nameof(index));
            }
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, attachments.Count);
            long length = attachments[index].ByteLength;
            using (MediaBuffer attachment = await session.ReadAttachmentAsync(index, cancellationToken))
            {
                await session.DisposeAsync();
                await outputFile.WriteAsync(stream => stream.WriteAsync(attachment.Memory, cancellationToken).AsTask(), cancellationToken);
            }
            if (session.AllocatedBufferBytes != 0)
            {
                throw new InvalidOperationException("Attachment extraction retained media buffers.");
            }
            await output.WriteLineAsync($"Attachment {index}: {length} original bytes. Live buffer bytes: 0.");
            return CliExitCodes.Success;
        }
    }
}
