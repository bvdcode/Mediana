# AC-3 elementary streams

`Ac3ReaderFactory` recognizes big-endian AC-3 and E-AC-3 syncframes by content. `Ac3Reader` requires an immutable, known-length random-access source and borrows it without moving its sequential cursor.

Opening reads the eight-byte header of each frame and indexes offsets, exact sample counts and cumulative sample positions. It does not read compressed payloads or verify their CRCs; the decoder verifies complete-frame checksums when decoding. E-AC-3 frames may have different block counts. Codec, sample rate, channel layout and independent substream must remain consistent within one track. Dependent substream composition requires additional presentation semantics and is rejected.

Packet reads allocate only the requested encoded frame. Indexed timing and seek queries perform no source I/O. A failed or cancelled read preserves the sequential reader cursor, and returned packets survive reader disposal. The reader supplies the shared indexed preroll contract; decoder registration remains the client's responsibility.
