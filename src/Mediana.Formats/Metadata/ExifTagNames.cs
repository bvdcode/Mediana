// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Globalization;

namespace Mediana.Formats.Metadata
{
    internal static class ExifTagNames
    {
        internal static string Get(string scope, ushort tag)
        {
            if (scope == "GPS")
            {
                return tag switch
                {
                    0 => "GPSVersionID",
                    1 => "GPSLatitudeRef",
                    2 => "GPSLatitude",
                    3 => "GPSLongitudeRef",
                    4 => "GPSLongitude",
                    5 => "GPSAltitudeRef",
                    6 => "GPSAltitude",
                    7 => "GPSTimeStamp",
                    18 => "GPSMapDatum",
                    29 => "GPSDateStamp",
                    _ => tag.ToString("X4", CultureInfo.InvariantCulture)
                };
            }
            return tag switch
            {
                254 => "NewSubfileType",
                256 => "ImageWidth",
                257 => "ImageLength",
                258 => "BitsPerSample",
                259 => "Compression",
                262 => "PhotometricInterpretation",
                270 => "ImageDescription",
                271 => "Make",
                272 => "Model",
                273 => "StripOffsets",
                274 => "Orientation",
                277 => "SamplesPerPixel",
                278 => "RowsPerStrip",
                279 => "StripByteCounts",
                282 => "XResolution",
                283 => "YResolution",
                296 => "ResolutionUnit",
                305 => "Software",
                306 => "DateTime",
                315 => "Artist",
                531 => "YCbCrPositioning",
                322 => "TileWidth",
                323 => "TileLength",
                324 => "TileOffsets",
                325 => "TileByteCounts",
                330 => "SubIFDs",
                33421 => "CFARepeatPatternDim",
                33422 => "CFAPattern",
                33432 => "Copyright",
                33434 => "ExposureTime",
                33437 => "FNumber",
                34665 => "ExifIFDPointer",
                34850 => "ExposureProgram",
                34853 => "GPSInfoIFDPointer",
                34855 => "PhotographicSensitivity",
                36864 => "ExifVersion",
                36867 => "DateTimeOriginal",
                36868 => "DateTimeDigitized",
                36880 => "OffsetTime",
                36881 => "OffsetTimeOriginal",
                36882 => "OffsetTimeDigitized",
                37377 => "ShutterSpeedValue",
                37378 => "ApertureValue",
                37380 => "ExposureBiasValue",
                37383 => "MeteringMode",
                37384 => "LightSource",
                37385 => "Flash",
                37386 => "FocalLength",
                37500 => "MakerNote",
                37510 => "UserComment",
                40960 => "FlashpixVersion",
                40961 => "ColorSpace",
                40962 => "PixelXDimension",
                40963 => "PixelYDimension",
                40965 => "InteroperabilityIFDPointer",
                41986 => "ExposureMode",
                41987 => "WhiteBalance",
                41988 => "DigitalZoomRatio",
                41989 => "FocalLengthIn35mmFilm",
                42033 => "BodySerialNumber",
                42034 => "LensSpecification",
                42035 => "LensMake",
                42036 => "LensModel",
                42037 => "LensSerialNumber",
                50706 => "DNGVersion",
                50707 => "DNGBackwardVersion",
                50708 => "UniqueCameraModel",
                50710 => "CFAPlaneColor",
                50711 => "CFALayout",
                50713 => "BlackLevelRepeatDim",
                50714 => "BlackLevel",
                50717 => "WhiteLevel",
                50718 => "DefaultScale",
                50719 => "DefaultCropOrigin",
                50720 => "DefaultCropSize",
                50721 => "ColorMatrix1",
                50722 => "ColorMatrix2",
                50728 => "AsShotNeutral",
                50778 => "CalibrationIlluminant1",
                50779 => "CalibrationIlluminant2",
                50829 => "ActiveArea",
                _ => tag.ToString("X4", CultureInfo.InvariantCulture)
            };
        }
    }
}
