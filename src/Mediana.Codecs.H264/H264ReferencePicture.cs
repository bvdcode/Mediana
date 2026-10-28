namespace Mediana.Codecs.H264
{
    internal record H264ReferencePicture(H264Picture Picture, int FrameNumber, int? LongTermIndex = null)
    {
        public int PictureNumber(int currentFrameNumber, int maximumFrameNumber) =>
            FrameNumber > currentFrameNumber ? FrameNumber - maximumFrameNumber : FrameNumber;
    }
}
