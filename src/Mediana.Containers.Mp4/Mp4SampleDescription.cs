namespace Mediana.Containers.Mp4
{
    internal record Mp4SampleDescription(string SampleEntry, string CodecId, ReadOnlyMemory<byte> Configuration,
        int? Width = null, int? Height = null, int? AudioChannels = null, int? AudioSampleRate = null);
}
