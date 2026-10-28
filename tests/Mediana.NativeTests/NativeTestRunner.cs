using Mediana.Codecs.H264;
using Mediana.Core.Memory;
using Mediana.Tests;

namespace Mediana.NativeTests
{
    internal static class NativeTestRunner
    {
        public static async Task RunAsync()
        {
            (string Name, string Extension, int Packet)[] cases =
                [("avc-tail", ".mov", 0), ("avc-tail", ".mov", 1), ("avc-tail", ".mov", 2),
                    ("avc-tail", ".mov", 3), ("avc-tail", ".mov", 4), ("avc-tail", ".mov", 5), ("avc-cropped", ".mp4", 0)];
            MediaBufferAllocator allocator = new(1024 * 1024);
            using (H264ReferenceBuffer references = new())
            {
                foreach ((string name, string extension, int packet) in cases)
                {
                    string root = Path.Combine(AppContext.BaseDirectory, "Data", "Mp4");
                    (H264Configuration configuration, byte[] nal) = await H264TestInput.ReadSliceAsync(Path.Combine(root, name + extension), packet);
                    byte[] expected = await File.ReadAllBytesAsync(Path.Combine(root, $"{name}.frame{packet}.yuv"));
                    H264TestInput.VerifyAndStorePicture(nal, configuration, references, allocator, expected);
                    Console.WriteLine($"PASS {name}, packet {packet}: all YUV samples match.");
                }
            }
            if (allocator.AllocatedBytes != 0)
            {
                throw new InvalidOperationException("H.264 reference picture storage was not released.");
            }
        }
    }
}
