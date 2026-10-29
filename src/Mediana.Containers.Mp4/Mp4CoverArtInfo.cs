namespace Mediana.Containers.Mp4
{
    /// <summary>Cover-art metadata. ContentType reflects the declared data type, not image validation.
    /// Null means an unsupported declaration; the original bytes remain available.</summary>
    public record Mp4CoverArtInfo(int MetadataItemIndex, uint DataType, uint Locale, int ByteLength, string? ContentType);
}
