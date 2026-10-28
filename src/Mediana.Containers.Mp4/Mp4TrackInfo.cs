using Mediana.Core;
using Mediana.Core.Timing;

namespace Mediana.Containers.Mp4
{
    /// <summary>Container declarations. Codec configuration may provide different audio channel or sample rate values.
    /// Media duration and packet times precede application of the edit list.</summary>
    public class Mp4TrackInfo
    {
        internal Mp4TrackInfo(uint trackId, EncodedStreamInfo stream, string sampleEntry,
            MediaDuration? mediaDuration, MediaDuration? presentationDuration, IReadOnlyList<Mp4Edit> edits,
            int? width, int? height, int? declaredAudioChannels, int? declaredAudioSampleRate, int sampleCount)
        {
            TrackId = trackId;
            Stream = stream;
            SampleEntry = sampleEntry;
            MediaDuration = mediaDuration;
            PresentationDuration = presentationDuration;
            Edits = edits;
            Width = width;
            Height = height;
            DeclaredAudioChannels = declaredAudioChannels;
            DeclaredAudioSampleRate = declaredAudioSampleRate;
            SampleCount = sampleCount;
        }

        public uint TrackId { get; }
        public EncodedStreamInfo Stream { get; }
        public string SampleEntry { get; }
        public MediaDuration? MediaDuration { get; }
        public MediaDuration? PresentationDuration { get; }
        public IReadOnlyList<Mp4Edit> Edits { get; }
        public int? Width { get; }
        public int? Height { get; }
        public int? DeclaredAudioChannels { get; }
        public int? DeclaredAudioSampleRate { get; }
        public int SampleCount { get; }
    }
}
