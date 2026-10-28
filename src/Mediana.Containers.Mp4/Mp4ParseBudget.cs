namespace Mediana.Containers.Mp4
{
    internal class Mp4ParseBudget(Mp4ReadOptions options, CancellationToken cancellationToken)
    {
        private int boxes;

        public void TakeBox()
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (boxes == options.MaxBoxes)
            {
                throw new NotSupportedException("The MP4 box count exceeds the configured limit.");
            }
            boxes++;
        }
    }
}
