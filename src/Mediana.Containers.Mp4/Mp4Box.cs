namespace Mediana.Containers.Mp4
{
    internal readonly record struct Mp4Box(string Type, ReadOnlyMemory<byte> Payload);
}
