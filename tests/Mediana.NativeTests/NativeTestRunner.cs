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
            string root = Path.Combine(AppContext.BaseDirectory, "Data", "Mp4");
            using (H264ReferenceBuffer references = new())
            {
                foreach ((string name, string extension, int packet) in cases)
                {
                    (H264Configuration configuration, byte[] nal) = await H264TestInput.ReadSliceAsync(Path.Combine(root, name + extension), packet);
                    byte[] expected = await File.ReadAllBytesAsync(Path.Combine(root, $"{name}.frame{packet}.yuv"));
                    H264TestInput.VerifyAndStorePicture(nal, configuration, references, allocator, expected);
                    Console.WriteLine($"PASS {name}, packet {packet}: all YUV samples match.");
                }
                byte[] weighted = await File.ReadAllBytesAsync(Path.Combine(root, "avc-weighted.sequence.yuv"));
                const int frameBytes = 94 * 62 * 3 / 2;
                if (weighted.Length != frameBytes * 12)
                {
                    throw new InvalidDataException("The weighted H.264 reference sequence has an unexpected length.");
                }
                for (int index = 0; index < 12; index++)
                {
                    (H264Configuration configuration, byte[] nal) = await H264TestInput.ReadSliceAsync(Path.Combine(root, "avc-weighted.mp4"), index);
                    H264TestInput.VerifyAndStorePicture(nal, configuration, references, allocator, weighted.AsSpan(index * frameBytes, frameBytes));
                    Console.WriteLine($"PASS avc-weighted, packet {index}: all YUV samples match.");
                }
            }
            if (allocator.AllocatedBytes != 0)
            {
                throw new InvalidOperationException("H.264 reference picture storage was not released.");
            }
            await NativePublicVideoTests.RunAsync(root);
            await NativeSrgbTests.RunAsync(root);
            await RotatedPreviewTestInput.VerifyAsync(root);
            Console.WriteLine("PASS rotated MP4 preview: display matrix, square-pixel 49x100 PNG, CRCs and all reference pixels match.");
        }
    }
}
