# MP4/M4A metadata and cover fixtures

These files contain generated test media and artwork. FFmpeg/ffprobe 6.1.1 prepared
the files and independent references. Mediana and its tests do not invoke those tools.

| File | Content |
| --- | --- |
| `covers.m4a` | One copied AAC track, Unicode iTunes tags and two original PNG/JPEG covers in `covr` data atoms |
| `mdta.mp4` | Copied AVC/AAC tracks and indexed UTF-8 metadata, including custom keys |
| `cover.png` | Generated 24×16 RGB image |
| `cover.jpg` | JPEG encoding of that image |

`references.json` records exact commands, hashes, stream descriptions, global tags
and every independent packet hash/timestamp. Packet references use original media
timestamps (`-ignore_editlist 1`); movie edit-list translation is a separate operation.
Per-track packet order and bytes are checked; the reader merges tracks by exact DTS.

PNG pixels at (x, y) are ((11x+3y) mod 256, (7x+17y) mod 256, (19x+5y) mod 256).
The PNG uses RGB8, filter-zero rows and zlib compression. The JPEG command is in
`references.json`. Independent stream-copy extraction reproduces both image files:

```sh
ffmpeg -hide_banner -v error -nostdin -i covers.m4a -map 0:v:0 -c copy -frames:v 1 extracted.png
ffmpeg -hide_banner -v error -nostdin -i covers.m4a -map 0:v:1 -c copy -frames:v 1 extracted.jpg
```

Managed and Linux Native AOT checks verify Unicode values, metadata namespaces,
declared data types/locales, the original image bytes and cover ownership after
reader disposal. Cover extraction does not create a timed video track or move
packet/source cursors. The retained cover lease accounts for the entire backing
movie metadata allocation. Image codec validation and tag normalization are separate.

Parser checks also cover localized repeated values, UTF-16BE, iTunes freeform keys,
unknown typed bytes, empty values, invalid Unicode, key indices, duplicated boxes
and configured text/value limits. ISO-style `meta` full boxes are supported; legacy
QuickTime user-data text and headerless QuickTime metadata remain unsupported.

Format references: [metadata keys](https://developer.apple.com/documentation/quicktime-file-format/metadata_item_keys_atom),
[data values](https://developer.apple.com/documentation/quicktime-file-format/data_atom),
[declared types](https://developer.apple.com/documentation/quicktime-file-format/well-known_types).

| File | SHA-256 |
| --- | --- |
| `covers.m4a` | `40b36b570c706b82040aa1e67c948ba9870cebee280323683ac62b20bc380edc` |
| `mdta.mp4` | `6316852e87269e6fbd2cbeaf9b838757e1147b962601cab4c5e47b157264a186` |
| `cover.png` | `3d20a35d9ff0be4e351b4545cb78c74647b095c04f2192db029d24092609ce9e` |
| `cover.jpg` | `4d255416a44419f313849fcedbabcd722feb71b81f90eaf5ee78cbc9f133dbfd` |
