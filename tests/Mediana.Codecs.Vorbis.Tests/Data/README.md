Vorbis codebook references use Xiph libvorbis 1.3.7 and libogg 1.3.5.

`codebooks.bin.gz` contains 96 complete setup headers and 49,152 independently encoded scalar symbols with vector lookups. Cases cover ordered and unordered lengths, sparse entries, depths through 32, lookup types 0/1/2, dimensions 1 through 8 and sequenced quantization. Each record contains setup bytes, dimensions, entry count, lookup type, encoded symbols, expected entry indices and the expanded reference vectors. Integers and floats are little endian; arrays begin with a signed 32-bit length. The decompressed file begins with `VORBOOK1` and the case count.

SHA-256: `4ea4aceceddf44511b5a1e8d105e6a81d8553b59fd894c4837ffb43354548248`.
