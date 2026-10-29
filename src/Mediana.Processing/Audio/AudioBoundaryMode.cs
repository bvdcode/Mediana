namespace Mediana.Processing.Audio
{
    public enum AudioBoundaryMode
    {
        Zero,
        /// <summary>Reflects about the first and last sample instants; endpoints are not repeated.
        /// A single-sample input repeats that sample.</summary>
        Reflect
    }
}
