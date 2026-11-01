# Matroska fixtures

`avc-aac-attachment.mkv` copies the authored AVC/AAC streams from
`../Mp4/avc-aac.mp4` and attaches `../Metadata/cover.png`, without re-encoding.
The video is 64 by 48 pixels. The attachment is declared as `image/png` and
retains the filename `cover.png`.

Creation with FFmpeg 6.1.1:

```sh
ffmpeg -v error -i tests/Mediana.Tests/Data/Mp4/avc-aac.mp4 \
  -map 0 -c copy -map_metadata -1 -metadata title='Matroska stream-copy fixture' \
  -attach tests/Mediana.Tests/Data/Metadata/cover.png \
  -metadata:s:t:0 mimetype=image/png -metadata:s:t:0 filename=cover.png \
  -f matroska tests/Mediana.Tests/Data/Matroska/avc-aac-attachment.mkv
```

The committed file is 19,043 bytes, SHA-256
`63f999ab20fc744304bbcb45b5287eb50a94420df5f6b816f2ef19896e5de87c`.
Matroska UIDs make regenerated files differ. The EBML header ends at byte 40;
EBMLVersion is 1, DocTypeVersion is 4 and DocTypeReadVersion is 2.
The internal header tests inspect that header without reading the Segment.
