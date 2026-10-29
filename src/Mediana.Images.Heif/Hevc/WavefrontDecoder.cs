// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Mediana.Images.Heif.Hevc
{
    /// <summary>
    /// Reconstructs WPP rows with bounded workers and a two-CTU dependency on the row above.
    /// </summary>
    /// <remarks>
    /// Only active workers own reconstruction scratch. Predecessor contexts are released
    /// after handoff. Failure cancels dependent rows, and every worker finishes before
    /// the caller can return the picture's plane rentals.
    /// </remarks>
    internal class WavefrontDecoder(IntraSliceHeader slice, DecodedPicture picture, CodingBlockMap blocks, SaoFilter sao)
    {
        public async Task DecodeAsync(int workerCount, CancellationToken cancellationToken)
        {
            SequenceParameters sequence = slice.Picture.Sequence;
            int ctbSize = 1 << sequence.Log2Ctb;
            int columns = ((sequence.Width + ctbSize) - 1) / ctbSize;
            int rows = slice.RowSubstreams.Count;
            WavefrontRowProgress[] progress = new WavefrontRowProgress[rows];
            IntraCabacReader?[] readers = new IntraCabacReader?[rows];
            using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            int nextRow = -1;
            try
            {
                for (int row = 0; row < rows; row++)
                {
                    progress[row] = new WavefrontRowProgress();
                }
                Task[] workers = new Task[workerCount];
                for (int worker = 0; worker < workerCount; worker++)
                {
                    workers[worker] = Task.Run(DecodeRowsAsync);
                }
                await Task.WhenAll(workers).ConfigureAwait(false);
            }
            finally
            {
                foreach (WavefrontRowProgress row in progress)
                {
                    row?.Dispose();
                }
            }

            async Task DecodeRowsAsync()
            {
                bool completed = false;
                try
                {
                    using ReconstructionWorkspace workspace = new ReconstructionWorkspace();
                    int row;
                    while ((row = Interlocked.Increment(ref nextRow)) < rows)
                    {
                        cancellation.Token.ThrowIfCancellationRequested();
                        IntraCabacReader reader = new IntraCabacReader(new BitReader(slice.RowSubstreams[row]), slice.Qp);
                        readers[row] = reader;
                        QuantizationGroup quantization = new QuantizationGroup(slice, reader, blocks);
                        IntraCodingTreeDecoder tree = new IntraCodingTreeDecoder(slice, reader, blocks, quantization);
                        ResidualDecoder residual = new ResidualDecoder(reader, slice.Picture.TransformSkip, slice.Picture.SignDataHiding, sequence.ChromaFormat == 3);
                        BlockReconstructor reconstruction = new BlockReconstructor(picture, residual, slice.Picture, quantization, workspace);
                        for (int column = 0; column < columns; column++)
                        {
                            cancellation.Token.ThrowIfCancellationRequested();
                            if (row > 0)
                            {
                                await progress[row - 1].WaitForAsync(Math.Min(columns, column + 2), cancellation.Token).ConfigureAwait(false);
                                if (column == 0)
                                {
                                    reader.CopyWavefrontContexts(readers[row - 1]!);
                                    readers[row - 1] = null;
                                }
                            }
                            sao.ReadCtb(column, row, reader);
                            tree.DecodeCtb(column * ctbSize, row * ctbSize, reconstruction);
                            progress[row].Advance(column + 1);
                        }
                    }
                    completed = true;
                }
                finally
                {
                    if (!completed)
                    {
                        await cancellation.CancelAsync().ConfigureAwait(false);
                    }
                }
            }
        }
    }
}
