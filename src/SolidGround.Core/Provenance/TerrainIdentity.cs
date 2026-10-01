using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using SolidGround.Core.Exports;
using SolidGround.Core.Processing;
using SolidGround.Core.Sources.BuildingOutlines;
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
    public const string FloorReferenceVersion = "terrain-identity-v2";
    public const string FloorReferenceContentSignatureAlgorithm = "sha256-canonical-run-v2";

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

        BuildingFloorReference? floor = payload.Provenance.FloorReference;
        BuildingOutlineProvenance? buildingOutline = payload.Provenance.BuildingOutline;
        if (floor is null && buildingOutline is null)
        {
            // Preserve the byte-for-byte legacy v1 canonical contract for every historical/no-floor run.
            return new TerrainIdentity(CurrentVersion, kind, Hash(stemMaterial), CurrentContentSignatureAlgorithm, Hash(content), pointFrameHash);
        }

        // A floor reference already carries its outline metadata. Keep that established v2 material once;
        // standalone acquisition context uses the explicit field so a source-elevation run remains distinct.
        string standaloneOutline = floor?.BuildingOutline is null
            ? CanonicalBuildingOutline(buildingOutline)
            : "none";
        string v2Content = content + "\nfloorReference=" + (floor is null ? "none" : CanonicalFloorReference(floor)) +
            "\nbuildingOutline=" + standaloneOutline;
        return new TerrainIdentity(FloorReferenceVersion, kind, Hash(stemMaterial), FloorReferenceContentSignatureAlgorithm, Hash(v2Content), pointFrameHash);
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

    private static string CanonicalFloorReference(BuildingFloorReference floor)
    {
        TargetProjectLevel level = floor.TargetLevel;
        string floorHeight = floor.PhysicalFloorElevation is double value
            ? value.ToString("R", CultureInfo.InvariantCulture)
            : "none";
        string point = floor.SelectedGroundPoint is { } pin
            ? string.Create(CultureInfo.InvariantCulture, $"{pin.Wgs84Longitude:R},{pin.Wgs84Latitude:R},{pin.ProjectedPoint.X:R},{pin.ProjectedPoint.Y:R}")
            : "none";
        string sample = floor.GroundSample is { } gridSample
            ? string.Create(CultureInfo.InvariantCulture, $"{gridSample.ProjectedPoint.X:R},{gridSample.ProjectedPoint.Y:R},{gridSample.Elevation:R}|") +
              string.Join(";", gridSample.Support.OrderBy(cell => cell.Row).ThenBy(cell => cell.Column).Select(CanonicalSupport))
            : "none";
        string rise = floor.Rise is { } measured
            ? string.Create(CultureInfo.InvariantCulture, $"{measured.Magnitude:R},{measured.Unit},{measured.BelowGrade}")
            : "none";
        string provisional = floor.ProvisionalGroundElevation is double elevation
            ? string.Create(CultureInfo.InvariantCulture, $"{floor.ProvisionalPolicy},{elevation:R}")
            : "none";
        return string.Create(CultureInfo.InvariantCulture,
            $"mode={floor.Mode}|sourceDatum={floor.SourceReference.Datum}|sourceUnit={floor.SourceReference.Unit}|sourceGeoid={floor.SourceReference.GeoidModel ?? "none"}|" +
            $"physicalFloor={floorHeight}|level={level.Id},{level.UniqueId},{level.ProjectElevationInternal:R}|pin={point}|sample={sample}|rise={rise}|provisional={provisional}|outline={CanonicalBuildingOutline(floor.BuildingOutline)}");
    }

    private static string CanonicalBuildingOutline(BuildingOutlineProvenance? context) => context is null
        ? "none"
        : string.Join("|", context.Provider, context.Release, context.LicenseIdentifier, context.LicenseUrl.AbsoluteUri,
            context.LicenseText, context.Attribution, context.ManifestSha256, string.Join(";", context.TileKeys));

    private static string CanonicalSupport(ElevationGridSupportCell cell) => string.Create(CultureInfo.InvariantCulture,
        $"{cell.Row},{cell.Column},{cell.Center.X:R},{cell.Center.Y:R},{cell.Elevation:R},{cell.Weight:R}");

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
