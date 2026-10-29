namespace Mediana.Tests
{
    internal record H264BidirectionalFixture(bool Cabac, int Width, int Height, int PictureCount,
        int FrameRate, int BPictureCount, int TemporalPictureCount, int Initialization)
    {
        public int PictureBytes => Width * Height * 3 / 2;

        public static H264BidirectionalFixture Get(string name) => name switch
        {
            "avc-cavlc-b-spatial" => new(false, 96, 64, 18, 10, 11, 0, 0),
            "avc-cavlc-b-temporal" => new(false, 96, 64, 18, 10, 11, 7, 0),
            "avc-aac" => new(true, 64, 48, 8, 5, 2, 0, 0),
            "avc-segments" => new(true, 64, 48, 36, 5, 13, 0, 0),
            _ => throw new ArgumentOutOfRangeException(nameof(name))
        };
    }
}
