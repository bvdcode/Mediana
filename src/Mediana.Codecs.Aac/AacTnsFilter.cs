namespace Mediana.Codecs.Aac
{
    internal record AacTnsFilter(int Start, int End, bool Backward, double[] Coefficients);
}
