using Mediana.Codecs.H264;
using Mediana.Core.Memory;
using Mediana.Tests;

namespace Mediana.NativeTests
{
    internal static class NativeTestRunner
    {
        public static async Task RunAsync()
        {
            (string Name, string Extension, int Packet)[] cases = [("avc-tail", ".mov", 0), ("avc-tail", ".mov", 5), ("avc-cropped", ".mp4", 0)];
            foreach ((string name, string extension, int packet) in cases)
            {
                string root = Path.Combine(AppContext.BaseDirectory, "Data", "Mp4");
                (H264Configuration configuration, byte[] nal) = await H264TestInput.ReadIdrAsync(Path.Combine(root, name + extension), packet);
                byte[] expected = await File.ReadAllBytesAsync(Path.Combine(root, $"{name}.frame{packet}.yuv"));
                MediaBufferAllocator allocator = new(1024 * 1024);
                using (H264Picture picture = H264IntraDecoder.Decode(nal, configuration, allocator))
                {
                    H264TestInput.VerifyPicture(picture, expected);
                    Console.WriteLine($"PASS {name}, packet {packet}: {picture.Sequence.Width}x{picture.Sequence.Height}, all YUV samples match.");
                }
                if (allocator.AllocatedBytes != 0)
                {
                    throw new InvalidOperationException("H.264 picture storage was not released.");
                }
            }
        }
    }
}
