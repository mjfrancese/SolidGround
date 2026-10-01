using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using SolidGround.Core.Aois;
using SolidGround.Core.Metadata;
using SolidGround.Core.Processing;
using SolidGround.Core.Provenance;
using SolidGround.Revit.Settings;

namespace SolidGround.Revit.Processing;

/// <summary>
/// Computes the private, in-memory identity of the inputs a prepared terrain snapshot may safely reuse.
/// This deliberately has no Revit API dependency so callers can capture it before and after managed work.
/// </summary>
internal static class PreparedTerrainBinding
{
    private const string DefaultSourceJsonExtension = ".source.json";

    internal static string Compute(
        RevitSettings settings,
        AreaOfInterest aoi,
        AddressParcelProvenance? addressParcel,
        string documentGuid,
        TargetProjectLevel level,
        long toposolidTypeId,
        double shortCurveToleranceInternal,
        double vertexToleranceInternal,
        long credentialRevision)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(settings.Request);
        ArgumentNullException.ThrowIfNull(aoi);
        ArgumentNullException.ThrowIfNull(documentGuid);
        ArgumentNullException.ThrowIfNull(level);

        CanonicalWriter canonical = new();
        canonical.String("version", "prepared-terrain-binding-v1");
        AppendRequest(canonical, settings.Request);
        canonical.Double("terrainExtensionMeters", settings.TerrainExtensionMeters);
        AppendRuntimeAoi(canonical, aoi);
        AppendAddressParcel(canonical, addressParcel);
        canonical.String("documentGuid", documentGuid);
        canonical.Int64("targetLevelId", level.Id);
        canonical.String("targetLevelUniqueId", level.UniqueId);
        canonical.Double("targetLevelProjectElevationInternal", level.ProjectElevationInternal);
        canonical.Int64("toposolidTypeId", toposolidTypeId);
        canonical.Double("shortCurveToleranceInternal", shortCurveToleranceInternal);
        canonical.Double("vertexToleranceInternal", vertexToleranceInternal);
        canonical.Int64("credentialRevision", credentialRevision);
        return Convert.ToHexString(SHA256.HashData(canonical.ToBytes()));
    }

    private static void AppendRequest(CanonicalWriter canonical, TerrainRequestSettings request)
    {
        canonical.Enum("requestMode", request.Mode);
        AppendConfiguredAoi(canonical, request.AreaOfInterest);
        AppendProcess(canonical, request);
        canonical.Enum("localOriginKind", request.LocalOrigin.Kind);
        canonical.Double("localOriginX", request.LocalOrigin.X);
        canonical.Double("localOriginY", request.LocalOrigin.Y);
        canonical.Double("localOriginZ", request.LocalOrigin.Z);
        canonical.Enum("outputUnit", request.OutputUnit);
        canonical.Enum("simplificationMethod", request.Simplification.Method);
        canonical.Int64("simplificationPointBudget", request.Simplification.PointBudget);
        canonical.Double("simplificationCoverageFloorFraction", request.Simplification.CoverageFloorFraction);
        canonical.String("outputDirectory", request.Output.Directory);
        canonical.String("outputBaseName", request.Output.BaseName);
        canonical.Int64("networkTimeoutSeconds", request.NetworkTimeoutSeconds);
    }

    private static void AppendConfiguredAoi(CanonicalWriter canonical, AoiSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        canonical.Enum("configuredAoiKind", settings.Kind);
        switch (settings.Kind)
        {
            case AreaOfInterestKind.BoundingBox:
                BoundingBoxAoiSettings box = settings.BoundingBox ?? throw new ArgumentException("The configured bounding-box AOI is missing.", nameof(settings));
                canonical.Double("configuredAoiWest", box.West);
                canonical.Double("configuredAoiSouth", box.South);
                canonical.Double("configuredAoiEast", box.East);
                canonical.Double("configuredAoiNorth", box.North);
                break;
            case AreaOfInterestKind.Radius:
                RadiusAoiSettings radius = settings.Radius ?? throw new ArgumentException("The configured radius AOI is missing.", nameof(settings));
                canonical.Double("configuredAoiCenterLatitude", radius.CenterLatitude);
                canonical.Double("configuredAoiCenterLongitude", radius.CenterLongitude);
                canonical.Double("configuredAoiRadiusMeters", radius.RadiusMeters);
                break;
            case AreaOfInterestKind.Parcel:
                ParcelAoiSettings parcel = settings.Parcel ?? throw new ArgumentException("The configured parcel AOI is missing.", nameof(settings));
                // This records the effective request preference without opening its path. The confirmed runtime
                // ParcelGeometryAoi below is the authoritative geometry for a dialog-resolved parcel.
                canonical.String("configuredAoiParcelPath", parcel.Path);
                canonical.NullableString("configuredAoiParcelFormat", parcel.Format);
                canonical.Double("configuredAoiParcelBufferMeters", parcel.BufferMeters);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(settings), settings.Kind, "Unsupported configured area-of-interest kind.");
        }
    }

    private static void AppendProcess(CanonicalWriter canonical, TerrainRequestSettings request)
    {
        if (request.Mode != TerrainAcquisitionMode.Process)
        {
            canonical.String("process", "inactive");
            return;
        }

        ProcessInputSettings process = request.Process ?? throw new ArgumentException("Process settings are required for process mode.", nameof(request));
        canonical.String("process", "active");
        canonical.NullableString("processSourceName", process.SourceName);
        canonical.NullableString("processDataset", process.Dataset);
        canonical.NullableString("processVerticalDatum", process.VerticalDatum);
        canonical.NullableEnum("processVerticalUnit", process.VerticalUnit);
        canonical.NullableString("processGeoid", process.Geoid);
        canonical.NullableString("processCollectionStart", process.CollectionStart);
        canonical.NullableString("processCollectionEnd", process.CollectionEnd);
        canonical.NullableString("processQualityLevel", process.QualityLevel);

        canonical.String("processAscSha256", ReadFileDigest(process.Asc));
        string prjPath = process.Prj ?? Path.ChangeExtension(process.Asc, ".prj");
        canonical.String("processPrjSha256", ReadFileDigest(prjPath));
        if (process.SourceJson is { } explicitSidecarPath)
        {
            canonical.String("processSidecarSelection", "explicit");
            canonical.String("processSidecarSha256", ReadFileDigest(explicitSidecarPath));
            return;
        }

        canonical.String("processSidecarSelection", "default");
        string defaultSidecarPath = Path.ChangeExtension(process.Asc, DefaultSourceJsonExtension);
        if (File.Exists(defaultSidecarPath))
        {
            canonical.String("processSidecarSha256", ReadFileDigest(defaultSidecarPath));
        }
        else
        {
            canonical.String("processSidecarSha256", "missing");
        }
    }

    private static string ReadFileDigest(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using FileStream input = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(input));
    }

    private static void AppendRuntimeAoi(CanonicalWriter canonical, AreaOfInterest aoi)
    {
        switch (aoi)
        {
            case Wgs84BoundingBoxAoi box:
                canonical.String("runtimeAoiKind", "boundingBox");
                canonical.Double("runtimeAoiWest", box.WestLongitude);
                canonical.Double("runtimeAoiSouth", box.SouthLatitude);
                canonical.Double("runtimeAoiEast", box.EastLongitude);
                canonical.Double("runtimeAoiNorth", box.NorthLatitude);
                break;
            case Wgs84RadiusAoi radius:
                canonical.String("runtimeAoiKind", "radius");
                canonical.Double("runtimeAoiLatitude", radius.Latitude);
                canonical.Double("runtimeAoiLongitude", radius.Longitude);
                canonical.Double("runtimeAoiRadiusMeters", radius.Radius.ToMeters());
                break;
            case ParcelGeometryAoi parcel:
                canonical.String("runtimeAoiKind", "parcel");
                canonical.Enum("runtimeAoiParcelFormat", parcel.Format);
                canonical.String("runtimeAoiParcelGeometry", parcel.Geometry);
                AppendHorizontalReference(canonical, "runtimeAoiParcelReference", parcel.HorizontalReference);
                canonical.Double("runtimeAoiParcelBufferMeters", parcel.Buffer.ToMeters());
                break;
            default:
                throw new ArgumentException($"Unsupported area-of-interest type '{aoi.GetType().Name}'.", nameof(aoi));
        }
    }

    private static void AppendHorizontalReference(CanonicalWriter canonical, string prefix, HorizontalReference reference)
    {
        canonical.String(prefix + "CoordinateReferenceSystem", reference.CoordinateReferenceSystem);
        canonical.String(prefix + "Datum", reference.Datum);
        canonical.Enum(prefix + "Kind", reference.Kind);
        canonical.Enum(prefix + "AxisOrder", reference.AxisOrder);
        canonical.Enum(prefix + "UnitReferenceKind", reference.Unit.ReferenceKind);
        canonical.NullableEnum(prefix + "UnitLinearUnit", reference.Unit.LinearUnit);
    }

    private static void AppendAddressParcel(CanonicalWriter canonical, AddressParcelProvenance? addressParcel)
    {
        if (addressParcel is null)
        {
            canonical.String("addressParcel", "none");
            return;
        }

        canonical.String("addressParcel", "present");
        if (addressParcel.Geocode is { } geocode)
        {
            canonical.String("geocode", "present");
            canonical.Enum("geocodeProvider", geocode.Provider);
            canonical.String("geocodeQueryText", geocode.QueryText);
            canonical.String("geocodeAttribution", geocode.Attribution);
        }
        else
        {
            canonical.String("geocode", "none");
        }

        if (addressParcel.Parcel is { } parcel)
        {
            canonical.String("parcel", "present");
            canonical.Enum("parcelSourceKind", parcel.SourceKind);
            canonical.String("parcelSourceIdentity", parcel.SourceIdentity);
            canonical.String("parcelId", parcel.ParcelId);
            canonical.NullableString("parcelStableParcelId", parcel.StableParcelId);
            canonical.NullableString("parcelLegalDescription", parcel.LegalDescription);
            canonical.String("parcelLicenseDisclaimerText", parcel.LicenseDisclaimerText);
        }
        else
        {
            canonical.String("parcel", "none");
        }
    }

    private sealed class CanonicalWriter
    {
        private readonly StringBuilder builder = new();

        internal void String(string name, string value) => Write(name, value);

        internal void NullableString(string name, string? value) => Write(name, value is null ? "null" : "string:" + value);

        internal void Int64(string name, long value) => Write(name, value.ToString(CultureInfo.InvariantCulture));

        internal void Double(string name, double value) => Write(name, value.ToString("R", CultureInfo.InvariantCulture));

        internal void Enum<T>(string name, T value) where T : struct, Enum => Write(name, Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture));

        internal void NullableEnum<T>(string name, T? value) where T : struct, Enum => Write(name, value is null ? "null" : Convert.ToInt64(value.Value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture));

        internal byte[] ToBytes() => Encoding.UTF8.GetBytes(builder.ToString());

        private void Write(string name, string value)
        {
            builder.Append(name.Length.ToString(CultureInfo.InvariantCulture));
            builder.Append(':');
            builder.Append(name);
            builder.Append(value.Length.ToString(CultureInfo.InvariantCulture));
            builder.Append(':');
            builder.Append(value);
        }
    }
}
