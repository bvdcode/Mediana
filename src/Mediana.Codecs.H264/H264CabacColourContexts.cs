// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    /// <summary>Initializes independent 4:4:4 states with the repeated m/n pairs in H.264 tables 9-25 through 9-33.</summary>
    internal static class H264CabacColourContexts
    {
        public const int First = 460;
        public const int Count = 564;

        public static void Initialize(Span<H264CabacContext> contexts, H264SliceKind kind, ReadOnlySpan<H264CabacContext> main,
            ReadOnlySpan<H264CabacContext> transform, ReadOnlySpan<H264CabacContext> field)
        {
            if (contexts.Length != Count)
            {
                throw new ArgumentException("The 4:4:4 CABAC state extent is invalid.", nameof(contexts));
            }
            for (int index = 0; index < contexts.Length; index++)
            {
                int source = InitializationSource(index + First);
                contexts[index] = source switch
                {
                    >= 277 and <= 398 or >= 436 and <= 459 => field[H264CabacFieldContexts.GetIndex(source)],
                    >= 402 and <= 435 => transform[source - 402],
                    _ => main[kind switch
                    {
                        H264SliceKind.I => H264CabacIntraContexts.GetIndex(source),
                        H264SliceKind.P or H264SliceKind.B => H264CabacPredictiveContexts.GetIndex(source),
                        _ => throw new ArgumentOutOfRangeException(nameof(kind))
                    }]
                };
            }
        }

        private static int InitializationSource(int context) => context switch
        {
            >= 460 and <= 483 => 85 + (context - 460) % 12,
            >= 484 and <= 571 => 105 + (context - 484) % 44,
            >= 572 and <= 659 => 166 + (context - 572) % 44,
            >= 660 and <= 674 => 402 + context - 660,
            >= 675 and <= 689 => 436 + context - 675,
            >= 690 and <= 698 => 417 + context - 690,
            >= 699 and <= 707 => 451 + context - 699,
            >= 708 and <= 717 => 426 + context - 708,
            >= 718 and <= 775 => InitializationSource(context - 58),
            >= 776 and <= 863 => 277 + (context - 776) % 44,
            >= 864 and <= 951 => 338 + (context - 864) % 44,
            >= 952 and <= 1011 => 227 + (context - 952) % 30,
            >= 1012 and <= 1023 => 93 + (context - 1012) % 4,
            _ => throw new ArgumentOutOfRangeException(nameof(context))
        };
    }
}
