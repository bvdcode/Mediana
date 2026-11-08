// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Codecs.Aac
{
    internal static class AacPsMixing
    {
        public const int MaximumBands = 34;
        public const int CoefficientCount = 4 * MaximumBands;
        private static ReadOnlySpan<double> CoarseIntensity => [-25, -18, -14, -10, -7, -4, -2, 0, 2, 4, 7, 10, 14, 18, 25];
        private static ReadOnlySpan<double> FineIntensity => [-50, -45, -40, -35, -30, -25, -22, -19, -16, -13,
            -10, -8, -6, -4, -2, 0, 2, 4, 6, 8, 10, 13, 16, 19, 22, 25, 30, 35, 40, 45, 50];
        private static ReadOnlySpan<double> Coherence => [1, 0.937, 0.84118, 0.60092, 0.36764, 0, -0.589, -1];

        public static void Calculate(AacPsData data, AacPsEnvelope envelope, Span<Complex> coefficients,
            Span<Complex> phaseHistory, Span<double> realCoefficients, bool advancePhase = true)
        {
            AacPsHeader header = data.Header ?? throw new InvalidOperationException("The PS header is missing.");
            int bands = data.StereoBands;
            Span<int> indices = stackalloc int[CoefficientCount];
            Span<int> intensity = indices[..MaximumBands];
            Span<int> coherence = indices.Slice(MaximumBands, MaximumBands);
            Span<int> phase = indices.Slice(2 * MaximumBands, MaximumBands);
            Span<int> overall = indices[(3 * MaximumBands)..];
            int intensityBands = header.IntensityMode is int iidMode ? AacPsHeader.ParameterCount(iidMode) : 20;
            int coherenceBands = header.CoherenceMode is int iccMode ? AacPsHeader.ParameterCount(iccMode) : 20;
            AacPsBandMapping.Map<int>(envelope.Intensity, intensityBands, intensity, bands);
            AacPsBandMapping.Map<int>(envelope.Coherence, coherenceBands, coherence, bands);
            Span<int> padded = stackalloc int[MaximumBands];
            padded.Clear();
            envelope.PhaseDifference.CopyTo(padded);
            AacPsBandMapping.Map<int>(padded, intensityBands, phase, bands);
            padded.Clear();
            envelope.OverallPhase.CopyTo(padded);
            AacPsBandMapping.Map<int>(padded, intensityBands, overall, bands);
            bool fine = header.IntensityMode >= 3;
            bool procedureB = header.CoherenceMode >= 3;
            Span<double> real = stackalloc double[4];
            for (int band = 0; band < bands; band++)
            {
                double decibels = fine ? FineIntensity[intensity[band] + 15] : CoarseIntensity[intensity[band] + 7];
                double ratio = Math.Pow(10, decibels / 20);
                double rho = Coherence[coherence[band]];
                if (procedureB)
                {
                    ProcedureB(ratio, rho, real);
                }
                else
                {
                    ProcedureA(ratio, rho, real);
                }
                Complex ipd = UnitPhase(phase[band]);
                Complex opd = UnitPhase(overall[band]);
                Complex smoothedIpd = Complex.One;
                Complex smoothedOpd = Complex.One;
                if (data.PhaseEnabled && advancePhase)
                {
                    smoothedIpd = Smooth(ipd, phaseHistory.Slice(band, 1), phaseHistory.Slice(MaximumBands + band, 1));
                    smoothedOpd = Smooth(opd, phaseHistory.Slice(2 * MaximumBands + band, 1), phaseHistory.Slice(3 * MaximumBands + band, 1));
                }
                Complex leftPhase = data.PhaseEnabled ? smoothedOpd : Complex.One;
                Complex rightPhase = data.PhaseEnabled ? smoothedOpd * Complex.Conjugate(smoothedIpd) : Complex.One;
                coefficients[band] = real[0] * leftPhase;
                coefficients[MaximumBands + band] = real[1] * rightPhase;
                coefficients[2 * MaximumBands + band] = real[2] * leftPhase;
                coefficients[3 * MaximumBands + band] = real[3] * rightPhase;
                for (int coefficient = 0; coefficient < 4; coefficient++)
                {
                    realCoefficients[coefficient * MaximumBands + band] = real[coefficient];
                }
            }
        }

        private static void ProcedureA(double ratio, double rho, Span<double> output)
        {
            double first = Math.Sqrt(2 / (1 + ratio * ratio));
            double second = first * ratio;
            double alpha = 0.5 * Math.Acos(rho);
            double beta = alpha * (first - second) / Math.Sqrt(2);
            (double sineLeft, double cosineLeft) = Math.SinCos(alpha + beta);
            (double sineRight, double cosineRight) = Math.SinCos(beta - alpha);
            output[0] = cosineLeft * second;
            output[1] = cosineRight * first;
            output[2] = sineLeft * second;
            output[3] = sineRight * first;
        }

        private static void ProcedureB(double ratio, double rho, Span<double> output)
        {
            rho = Math.Max(rho, 0.05);
            double alpha = 0.5 * Math.Atan2(2 * ratio * rho, ratio * ratio - 1);
            double sum = ratio + 1 / ratio;
            double root = Math.Sqrt(Math.Max(0, 1 + (4 * rho * rho - 4) / (sum * sum)));
            double gamma = Math.Atan(Math.Sqrt(Math.Max(0, (1 - root) / (1 + root))));
            (double sineAlpha, double cosineAlpha) = Math.SinCos(alpha);
            (double sineGamma, double cosineGamma) = Math.SinCos(gamma);
            double scale = Math.Sqrt(2);
            output[0] = scale * cosineAlpha * cosineGamma;
            output[1] = scale * sineAlpha * cosineGamma;
            output[2] = -scale * sineAlpha * sineGamma;
            output[3] = scale * cosineAlpha * sineGamma;
        }

        private static Complex UnitPhase(int index)
        {
            (double sine, double cosine) = Math.SinCos((index & 7) * Math.PI / 4);
            return new Complex(cosine, sine);
        }

        private static Complex Smooth(Complex value, Span<Complex> older, Span<Complex> previous)
        {
            Complex sum = older[0] * 0.25 + previous[0] * 0.5 + value;
            older[0] = previous[0];
            previous[0] = value;
            return sum / sum.Magnitude;
        }
    }
}
