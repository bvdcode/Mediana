// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Formats.Audio
{
    /// <summary>Validated Vorbis setup structure and packet block modes, without expanded vector codebooks.
    /// Timing inspection does not decode spectral data or prove that an audio packet is decodable.</summary>
    public class VorbisSetupInfo
    {
        private readonly bool[] longModes;
        private readonly VorbisIdentification identification;

        private VorbisSetupInfo(VorbisIdentification identification, bool[] longModes)
        {
            this.identification = identification;
            this.longModes = longModes;
        }

        public int ModeCount => longModes.Length;

        public static VorbisSetupInfo Read(VorbisIdentification identification, ReadOnlySpan<byte> setup,
            CancellationToken cancellationToken = default)
            => ReadCore(identification, setup, null, cancellationToken);

        internal static VorbisSetupInfo ReadForDecoding(VorbisIdentification identification, ReadOnlySpan<byte> setup,
            VorbisSetupVisitor visitor, CancellationToken cancellationToken = default)
            => ReadCore(identification, setup, visitor, cancellationToken);

        private static VorbisSetupInfo ReadCore(VorbisIdentification identification, ReadOnlySpan<byte> setup,
            VorbisSetupVisitor? visitor, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(identification);
            if (setup.Length < 7 || setup[0] != 5 || !setup.Slice(1, 6).SequenceEqual("vorbis"u8))
            {
                throw new InvalidDataException("The Vorbis setup packet is absent or truncated.");
            }
            VorbisBitReader reader = new(setup[7..]);
            bool[] modes = VorbisSetupReader.Read(ref reader, identification.ChannelCount, cancellationToken, visitor);
            return new(identification, modes);
        }

        public int GetBlockSize(ReadOnlySpan<byte> packet)
        {
            VorbisBitReader reader = new(packet);
            if (reader.Read(1) != 0) { throw new InvalidDataException("A Vorbis audio packet has a header packet type."); }
            int mode = (int)reader.Read(VorbisBitReader.ILog((uint)longModes.Length - 1));
            if (mode >= longModes.Length) { throw new InvalidDataException("A Vorbis packet refers to an absent mode."); }
            if (!longModes[mode]) { return identification.SmallBlockSize; }
            reader.Read(2);
            return identification.LargeBlockSize;
        }
    }
}
