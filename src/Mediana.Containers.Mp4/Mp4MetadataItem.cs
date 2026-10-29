namespace Mediana.Containers.Mp4
{
    /// <summary>One metadata data atom, preserving its key namespace, declared type and raw locale.
    /// Text is decoded only for UTF-8 and UTF-16BE. Other values remain available as encoded bytes.</summary>
    public record Mp4MetadataItem(string Namespace, string Name, uint DataType, uint Locale, int ByteLength, string? Text);
}
