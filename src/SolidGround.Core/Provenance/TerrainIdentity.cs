using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using SolidGround.Core.Exports;
using SolidGround.Core.Transformations;

namespace SolidGround.Core.Provenance;

/// <summary>Identity kinds deliberately recorded for duplicate-run guarding.</summary>
public enum TerrainIdentityKind
{
    StableParcel,
    BoundingBox,
    Radius,
    Polygon,
}

/// <summary>
/// Stable, non-secret identity and content fingerprints for one completed terrain run. The canonical strings
/// exclude dates, addresses, source paths, and other volatile UI text.
/// </summary>
public sealed record TerrainIdentity(
    string Version,
    TerrainIdentityKind Kind,
    string Stem,
    string ContentSignatureAlgorithm,
    string ContentSignature,
    string PointFrameHash)
{
    public const string CurrentVersion = "terrain-identity-v1";
    public const string CurrentContentSignatureAlgorithm = "sha256-canonical-run-v1";

    public static TerrainIdentity Create(
        TerrainExportPayload payload,
        TerrainIdentityKind fallbackKind,
        string fallbackCanonicalAoi,
        string canonicalLegalTopology,
        double terrainExtensionMeters)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentException.ThrowIfNullOrWhiteSpace(fallbackCanonicalAoi);
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalLegalTopology);
        if (!Enum.IsDefined(fallbackKind) || !double.IsFinite(terrainExtensionMeters) || terrainExtensionMeters < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(fallbackKind));
        }

        ParcelProvenance? parcel = payload.Provenance.AddressParcel?.Parcel;
        TerrainIdentityKind kind = parcel?.StableParcelId is not null ? TerrainIdentityKind.StableParcel : fallbackKind;
        string stemMaterial = parcel?.StableParcelId is not null
            ? string.Create(CultureInfo.InvariantCulture, $"stableParcel|{parcel.SourceIdentity}|{parcel.StableParcelId}")
            : string.Create(CultureInfo.InvariantCulture, $"{kind}|{fallbackCanonicalAoi}");

        string frame = CanonicalFrame(payload);
        string pointFrameHash = Hash(frame + "\npoints\n" + CanonicalPoints(payload));
        string content = string.Create(CultureInfo.InvariantCulture,
            $"topology={canonicalLegalTopology}\nextensionMeters={terrainExtensionMeters:R}\nframe={frame}\n" +
            $"source={payload.Provenance.Source.DatasetIdentifier}\nmethod={payload.Provenance.SimplificationRequest.Method}\n" +
            $"budget={payload.Provenance.SimplificationRequest.PointBudget}\n" +
            $"coverageFloorFraction={CanonicalNullable(payload.Provenance.SimplificationRequest.CoverageFloorFraction)}\n" +
            $"pointFrameHash={pointFrameHash}");

        return new TerrainIdentity(CurrentVersion, kind, Hash(stemMaterial), CurrentContentSignatureAlgorithm, Hash(content), pointFrameHash);
    }

    private static string CanonicalFrame(TerrainExportPayload payload)
    {
        LocalCoordinateFrame frame = payload.Provenance.LocalFrame;
        return string.Create(CultureInfo.InvariantCulture,
            $"crs={frame.ProjectedHorizontalReference.CoordinateReferenceSystem}|datum={frame.ProjectedHorizontalReference.Datum}|" +
            $"vertical={frame.VerticalReference.Datum}|unit={frame.OutputUnit}|origin={frame.Origin.X:R},{frame.Origin.Y:R},{frame.Origin.Elevation:R}");
    }

    private static string CanonicalPoints(TerrainExportPayload payload) => string.Join("\n", payload.Samples
        .Select(sample => sample.Position)
        .OrderBy(point => point.X)
        .ThenBy(point => point.Y)
        .ThenBy(point => point.Elevation)
        .Select(point => string.Create(CultureInfo.InvariantCulture, $"{point.X:R},{point.Y:R},{point.Elevation:R}")));

    private static string CanonicalNullable(double? value) => value is double fraction
        ? fraction.ToString("R", CultureInfo.InvariantCulture)
        : "unknown";

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
