// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Models.Mesh
{
    internal readonly struct StudioLight
    {
        private readonly Vector3 _halfVector;
        private readonly Vector3 _fresnel;
        private readonly Vector3 _diffuse;
        private readonly float _alphaSquared;
        private readonly float _k;

        internal StudioLight(Vector3 direction, Vector3 view, Vector3 fresnel, Vector3 baseColor, float metallic, float roughness)
        {
            _halfVector = Vector3.Normalize(direction + view);
            float vh = MathF.Max(Vector3.Dot(view, _halfVector), 0);
            float alpha = roughness * roughness;
            _alphaSquared = alpha * alpha;
            _k = ((roughness + 1) * (roughness + 1)) / 8;
            _fresnel = fresnel + ((Vector3.One - fresnel) * MathF.Pow(1 - vh, 5));
            _diffuse = ((Vector3.One - _fresnel) * (1 - metallic) * baseColor) / MathF.PI;
        }

        internal Vector3 Evaluate(Vector3 normal, float nv, float lightDot)
        {
            float nl = MathF.Max(lightDot, 0);
            if (nl == 0)
            {
                return Vector3.Zero;
            }

            float nh = MathF.Max(Vector3.Dot(normal, _halfVector), 0);
            float denominator = (nh * nh * (_alphaSquared - 1)) + 1;
            float distribution = _alphaSquared / (MathF.PI * denominator * denominator);
            float geometry = ((nv / ((nv * (1 - _k)) + _k)) * nl) / ((nl * (1 - _k)) + _k);
            Vector3 specular = _fresnel * ((distribution * geometry) / ((4 * nv * nl) + 0.0001f));
            return (_diffuse + specular) * nl;
        }
    }
}
