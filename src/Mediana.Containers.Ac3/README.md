# AC-3 elementary streams

`Ac3ReaderFactory` recognizes big-endian AC-3 and E-AC-3 syncframes by content. `Ac3Reader` requires an immutable, known-length random-access source and borrows it without moving its sequential cursor.

Opening reads the eight-byte header of each physical frame and indexes offsets, exact sample counts and cumulative sample positions. The first frame set additionally reads up to four BSI bytes per substream to establish its exact opening channel map. It does not read compressed payloads or verify their CRCs; the decoder verifies complete-frame checksums when decoding. E-AC-3 frames may have different block counts. Codec, sample rate, channel layout and independent substream must remain consistent within one track. An independent E-AC-3 frame and its ordered dependent frames form one logical packet; only the independent frame advances the sample clock. Opening metadata reports the ordered union of their channel locations. Multiple independent programmes require explicit presentation selection.

Packet reads allocate only the requested logical frame set. Opening metadata, indexed timing and seek queries perform no source I/O. A failed or cancelled read preserves the sequential reader cursor, and returned packets survive reader disposal. The reader supplies the shared indexed preroll contract; decoder registration remains the client's responsibility.
