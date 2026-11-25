// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Models.Mesh
{
    internal class StudioLighting
    {
        internal static readonly Vector3 KeyDirection = Vector3.Normalize(new Vector3(-3, -4, 6));
        private static readonly Vector3 FillDirection = Vector3.Normalize(new Vector3(4, -1, 2));
        private static readonly Vector3 RimDirection = Vector3.Normalize(new Vector3(1, 4, 3));
        private readonly float _metallic;
        private readonly float _exposure;
        private readonly Vector3 _baseColor;
        private readonly Vector3 _view;
        private readonly StudioLight _key;
        private readonly StudioLight _fill;
        private readonly StudioLight _rim;
        private readonly StudioShadow? _shadow;

        internal StudioLighting(MeshRenderOptions options, Vector3 view, StudioShadow? shadow)
        {
            _metallic = options.Metallic;
            _exposure = options.Exposure;
            _baseColor = new Vector3(ToLinear(options.BaseColor.X), ToLinear(options.BaseColor.Y), ToLinear(options.BaseColor.Z));
            _view = view;
            Vector3 fresnel = Vector3.Lerp(new Vector3(0.04f), _baseColor, _metallic);
            _key = new StudioLight(KeyDirection, view, fresnel, _baseColor, _metallic, options.Roughness);
            _fill = new StudioLight(FillDirection, view, fresnel, _baseColor, _metallic, options.Roughness);
            _rim = new StudioLight(RimDirection, view, fresnel, _baseColor, _metallic, options.Roughness);
            _shadow = shadow;
        }

        internal Vector3 Shade(Vector3 point, Vector3 normal, float occlusion)
        {
            float hemisphere = (normal.Z * 0.5f) + 0.5f;
            Vector3 ambient = Vector3.Lerp(new Vector3(0.12f, 0.14f, 0.19f), new Vector3(0.38f, 0.43f, 0.55f), hemisphere);
            Vector3 color = _baseColor * ambient * occlusion * (1 - (0.65f * _metallic));
            float nv = MathF.Max(Vector3.Dot(normal, _view), 0.001f);
            float keyDot = Vector3.Dot(normal, KeyDirection);
            color += _key.Evaluate(normal, nv, keyDot) * new Vector3(4.8f, 4.5f, 4.1f) * (_shadow?.Visibility(point, normal, keyDot) ?? 1);
            color += _fill.Evaluate(normal, nv, Vector3.Dot(normal, FillDirection)) * new Vector3(0.85f, 1.05f, 1.5f);
            color += _rim.Evaluate(normal, nv, Vector3.Dot(normal, RimDirection)) * new Vector3(2.3f, 2.55f, 3.1f);
            color *= _exposure;
            return new Vector3(ToneMap(color.X), ToneMap(color.Y), ToneMap(color.Z));
        }

        internal static float ToLinear(float value) => value <= 0.04045f ? value / 12.92f : MathF.Pow((value + 0.055f) / 1.055f, 2.4f);

        internal static byte ToSrgbByte(float value)
        {
            value = Math.Clamp(value, 0, 1);
            float srgb = value <= 0.0031308f ? value * 12.92f : (1.055f * MathF.Pow(value, 1 / 2.4f)) - 0.055f;
            return (byte)Math.Clamp((int)MathF.Round(srgb * 255), 0, 255);
        }

        private static float ToneMap(float value)
        {
            float numerator = value * ((2.51f * value) + 0.03f);
            float denominator = (value * ((2.43f * value) + 0.59f)) + 0.14f;
            return Math.Clamp(numerator / denominator, 0, 1);
        }
    }
}
