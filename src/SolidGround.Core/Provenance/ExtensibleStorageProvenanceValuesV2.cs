using System.Globalization;
using SolidGround.Core.Exports;

namespace SolidGround.Core.Provenance;

/// <summary>V2 values composed from the immutable v1 field values plus v2-only provenance and guard data.</summary>
public sealed record ExtensibleStorageProvenanceValuesV2(
    ExtensibleStorageProvenanceValues BaseValues,
    bool HasAddressParcel, string AddressParcelRetrievalDateIso,
    bool HasGeocode, string GeocodeProvider, string GeocodeQueryText, string GeocodeAttribution,
    bool HasParcel, string ParcelSourceKind, string ParcelSourceIdentity, string ParcelId,
    bool HasStableParcelId, string StableParcelId, bool HasLegalDescription, string LegalDescription,
    string ParcelLicenseDisclaimerText, bool HasSourceAttribution, string SourceAttribution,
    double CoverageFloorFraction, string CollectionPeriodAvailability, TerrainIdentity Identity,
    string StoredOriginalUniqueId, string StoredOriginalDocumentCreationGuid)
{
    public static ExtensibleStorageProvenanceValuesV2 From(
        TerrainExportPayload payload, TerrainIdentity identity, double coverageFloorFraction,
        string buildInformationalVersion, string buildModuleVersionId, string buildSha256)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(identity);
        if (!double.IsFinite(coverageFloorFraction) || coverageFloorFraction is < 0d or > 1d)
        {
            throw new ArgumentOutOfRangeException(nameof(coverageFloorFraction));
        }

        ExtensibleStorageProvenanceValues baseValues = ExtensibleStorageProvenanceValues.From(
            payload.Provenance, buildInformationalVersion, buildModuleVersionId, buildSha256);
        AddressParcelProvenance? addressParcel = payload.Provenance.AddressParcel;
        GeocodeProvenance? geocode = addressParcel?.Geocode;
        ParcelProvenance? parcel = addressParcel?.Parcel;
        bool hasAddressParcel = addressParcel is not null;
        bool hasGeocode = geocode is not null;
        bool hasParcel = parcel is not null;
        bool hasStableParcelId = parcel?.StableParcelId is not null;
        bool hasLegalDescription = parcel?.LegalDescription is not null;
        bool hasAttribution = payload.Provenance.Source.Attribution is not null;

        return new(baseValues, hasAddressParcel,
            hasAddressParcel ? addressParcel!.RetrievalDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : string.Empty,
            hasGeocode, hasGeocode ? geocode!.Provider.ToString() : string.Empty, hasGeocode ? geocode!.QueryText : string.Empty, hasGeocode ? geocode!.Attribution : string.Empty,
            hasParcel, hasParcel ? parcel!.SourceKind.ToString() : string.Empty, hasParcel ? parcel!.SourceIdentity : string.Empty, hasParcel ? parcel!.ParcelId : string.Empty,
            hasStableParcelId, hasStableParcelId ? parcel!.StableParcelId! : string.Empty, hasLegalDescription, hasLegalDescription ? parcel!.LegalDescription! : string.Empty,
            hasParcel ? parcel!.LicenseDisclaimerText : string.Empty, hasAttribution, payload.Provenance.Source.Attribution ?? string.Empty,
            coverageFloorFraction, baseValues.HasCollectionPeriod ? "reported" : "notReportedBySource", identity, string.Empty, string.Empty);
    }

    /// <summary>Supplies Revit-owned element/document identities after Core has assembled the portable values.</summary>
    public ExtensibleStorageProvenanceValuesV2 WithStoredOriginalElementIdentity(string uniqueId, string documentCreationGuid)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uniqueId);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentCreationGuid);
        return this with { StoredOriginalUniqueId = uniqueId, StoredOriginalDocumentCreationGuid = documentCreationGuid };
    }
}
