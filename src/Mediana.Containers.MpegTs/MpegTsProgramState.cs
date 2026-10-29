namespace Mediana.Containers.MpegTs
{
    internal readonly record struct MpegTsProgramState(long LastDecodeTime, long? LastPcr, long? LastTables,
        int PatCounter, int PmtCounter);
}
