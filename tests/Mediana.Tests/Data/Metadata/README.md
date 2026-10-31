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
international QuickTime user-data lists and headerless QuickTime metadata remain unsupported.

Format references: [metadata keys](https://developer.apple.com/documentation/quicktime-file-format/metadata_item_keys_atom),
[data values](https://developer.apple.com/documentation/quicktime-file-format/data_atom),
[declared types](https://developer.apple.com/documentation/quicktime-file-format/well-known_types).

| File | SHA-256 |
| --- | --- |
| `covers.m4a` | `40b36b570c706b82040aa1e67c948ba9870cebee280323683ac62b20bc380edc` |
| `mdta.mp4` | `6316852e87269e6fbd2cbeaf9b838757e1147b962601cab4c5e47b157264a186` |
| `cover.png` | `3d20a35d9ff0be4e351b4545cb78c74647b095c04f2192db029d24092609ce9e` |
| `cover.jpg` | `4d255416a44419f313849fcedbabcd722feb71b81f90eaf5ee78cbc9f133dbfd` |

`track-metadata.mp4` copies AVC and AAC packets from `../Mp4/avc-aac.mp4`,
duplicating the audio track. Its three tracks declare `und`, `rus` and `eng` in
`mdhd`; audio tracks additionally declare `ru-RU` and `en-GB` in `elng`.
Movie, track and media `meta` boxes exercise indexed keys, UTF-8, UTF-16BE,
locales, repeated names, empty text and unknown binary data types. Every metadata
value and each track's complete packet bytes/timing have independent hashes in
`track-metadata.reference.json`. Typed text and key bytes total 326; typed values
total 17. The two original `trak/udta/name` UTF-8 names add 48 text bytes and two
values, giving whole-file limits of 374 bytes and 19 recognized metadata values.

`track-user-data.mp4` retains all three tracks and every packet from that file,
adding movie/track/media user-data names, localized UTF-8 and UTF-16BE `tnam`
payloads, repeated and empty names, an unspecified language code and an unknown
user-data atom. The localized atom payloads follow Apple's
[track name fields](https://developer.apple.com/documentation/quicktime-file-format/track_name_atom).
Its independent reference records all original payload hashes, native language
codes, ordered common tags and every packet's timing/bytes. Whole-file limits are
29 recognized values and 509 decoded text/key bytes. The file SHA-256 is
`0f6c67355bb94fcf88b5e853d5c2645fddf864ba22788abc8105fb92c2c7961d`.
The original FFmpeg titles remain separately verifiable through ffprobe; the
localized `tnam` records use their declared syntax rather than ffprobe tag aliases.

The base copy was produced with FFmpeg 6.1.1:

```sh
ffmpeg -nostdin -v error -i ../Mp4/avc-aac.mp4 -map 0:v -map 0:a -map 0:a -c copy \
  -metadata title='Track metadata' -metadata:s:a:0 language=rus \
  -metadata:s:a:0 title='Основная дорожка' -metadata:s:a:1 language=eng \
  -metadata:s:a:1 title='Alternative audio' -movflags +use_metadata_tags multilingual.mp4
```

Additional `meta` and `elng` boxes were appended inside the trailing `moov`;
media offsets and encoded packet bytes were unchanged. Each media scope has
`mdir/©nam` UTF-8 `Media N` and UTF-16BE `Медиа N` values, with locales 0 and
`0x474215c7`. Track 1 stores that metadata inside media `udta`; tracks 0 and 2
store it directly. Each track has direct `mdta` keys `example.track.title` and
`example.track.blob`. Titles are `Video 🎞`, `Русская дорожка`, and empty text;
their locales are 1, 2, and 3. Blob values have type `0x01000001`, locale N,
and three original bytes `[0, N, 255]`. The blob is excluded from common text tags.

Independent packet references use `ffprobe -ignore_editlist 1 -show_packets
-show_streams -show_data_hash sha256`. A per-track SHA-256 covers each packet's
little-endian signed 64-bit PTS, DTS and duration, unsigned 32-bit payload length,
then its complete encoded payload. FFprobe 6.1.1 reports the older `mdhd` language;
the selected extended language in the reference follows Apple's documented
`elng` priority. The original ffprobe value is retained separately in the reference.

Format references: [metadata locations](https://developer.apple.com/documentation/quicktime-file-format/metadata_atoms_and_types),
[language codes](https://developer.apple.com/documentation/quicktime-file-format/language_code_values),
[extended language tags](https://developer.apple.com/documentation/quicktime-file-format/extended_language_tag_atom),
[BCP 47 syntax](https://www.rfc-editor.org/rfc/rfc5646).
