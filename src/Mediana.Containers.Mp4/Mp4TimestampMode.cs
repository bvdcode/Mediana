namespace Mediana.Containers.Mp4
{
    public enum Mp4TimestampMode
    {
        /// <summary>Original media timestamps without edit-list translation.</summary>
        Media,
        /// <summary>Translate one unit-rate media edit, optionally preceded by an empty edit,
        /// into movie time. Retains every encoded sample, including priming and samples outside the edit.</summary>
        LinearMovie
    }
}
