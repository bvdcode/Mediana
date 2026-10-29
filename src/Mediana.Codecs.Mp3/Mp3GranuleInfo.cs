namespace Mediana.Codecs.Mp3
{
    internal readonly record struct Mp3GranuleInfo(
        int PartBits,
        int BigValues,
        int GlobalGain,
        int ScaleFactorCompression,
        int BlockType,
        bool MixedBlock,
        int Table0,
        int Table1,
        int Table2,
        int SubblockGain0,
        int SubblockGain1,
        int SubblockGain2,
        int Region0Count,
        int Region1Count,
        bool Preflag,
        bool ScaleFactorScale,
        int Count1Table);
}
