namespace Mediana.Containers.Mp3
{
    /// <summary>APIC declarations; MIME type does not validate image content. A media type of '-->' contains a URL.</summary>
    public record Id3PictureInfo(byte PictureType, string MediaType, string Description, int ByteLength);
}
