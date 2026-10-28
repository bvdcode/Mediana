namespace Mediana.Containers.Mp4
{
    internal readonly record struct Mp4DataRange(long Offset, long Length)
    {
        public bool Contains(long offset, long length) =>
            offset >= Offset && offset - Offset <= Length && length <= Length - (offset - Offset);
    }
}
