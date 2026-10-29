using System.Numerics;
using System.Runtime.InteropServices;
using Mediana.Core.Memory;

namespace Mediana.Codecs.Aac
{
    internal class AacDecoderWorkspace : IDisposable
    {
        private const int OutputOffset = 2048 * sizeof(double);
        private const int FourierOffset = OutputOffset + 1024 * sizeof(double);
        private readonly WritableMediaBuffer storage;

        public AacDecoderWorkspace(MediaBufferAllocator allocator, int channels)
        {
            bool succeeded = false;
            try
            {
                Left = new AacChannelState(allocator);
                if (channels == 2)
                {
                    Right = new AacChannelState(allocator);
                }
                storage = allocator.Allocate(FourierOffset + 512 * 2 * sizeof(double));
                succeeded = true;
            }
            finally
            {
                if (!succeeded)
                {
                    Left?.Dispose();
                    Right?.Dispose();
                }
            }
        }

        public AacChannelState Left { get; }
        public AacChannelState? Right { get; }
        public Span<double> Time => MemoryMarshal.Cast<byte, double>(storage.Memory.Span[..OutputOffset]);
        public Span<double> Output => MemoryMarshal.Cast<byte, double>(storage.Memory.Span.Slice(OutputOffset, 1024 * sizeof(double)));
        public Span<Complex> Fourier => MemoryMarshal.Cast<byte, Complex>(storage.Memory.Span[FourierOffset..]);

        public void Dispose()
        {
            Left.Dispose();
            Right?.Dispose();
            storage.Dispose();
        }
    }
}
