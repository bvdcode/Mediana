namespace Mediana.Codecs.H264
{
    internal record H264BidirectionalReferences(IReadOnlyList<H264ReferencePicture?> L0, IReadOnlyList<H264ReferencePicture?> L1);
}
