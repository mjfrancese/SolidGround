using System.Globalization;
using System.Text.Json;
using NetTopologySuite.Geometries;
using SolidGround.Core.Aois;

namespace SolidGround.Core.Sources.LocalParcelFile;

/// <summary>
/// Resolves a parcel boundary by reading a user-purchased county parcel export from a configured local path --
/// the vendor-neutral, commercial-data slot (Regrid Data Store's Standard schema is the reference purchase).
/// Never calls the network: reads only a local GeoJSON file, fresh on every <see cref="FindAsync"/> call. Only
/// the field map's own named slots are ever looked up on a feature's <c>properties</c> object -- never a
/// generic enumeration -- so an owner/mailing property physically present in the file can never reach a
/// <see cref="ParcelBoundaryCandidate"/>. See docs/architecture/parcel-boundary-sources.md for the full
/// contract.
/// </summary>
public sealed class LocalParcelFileSource : IParcelBoundarySource
{
    private readonly LocalParcelFileOptions options;

    public LocalParcelFileSource(LocalParcelFileOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Path);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.SourceLabel);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.LicenseDisclaimerText);
        ArgumentNullException.ThrowIfNull(options.FieldMap);
        options.FieldMap.Validate();

        this.options = options;
    }

    /// <exception cref="LocalParcelFileNotFoundException">The configured path does not exist.</exception>
    /// <exception cref="LocalParcelFileAccessException">The path exists but could not be opened or read.</exception>
    /// <exception cref="LocalParcelFileFormatException">The file is not a well-formed GeoJSON FeatureCollection per this reader's contract.</exception>
    public ValueTask<ParcelBoundaryAcquisition> FindAsync(ParcelBoundaryQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        string json = ReadFile();
        using JsonDocument document = ParseDocument(json);
        JsonElement root = document.RootElement;

        ValidateCollectionShape(root);
        ValidateCrsIfPresent(root, "the FeatureCollection root");

        List<ParcelBoundaryCandidate> candidates = [];
        int featureIndex = 0;
        foreach (JsonElement feature in root.GetProperty("features").EnumerateArray())
        {
            ParcelBoundaryCandidate? candidate = TryBuildCandidate(feature, featureIndex, query);
            if (candidate is not null)
            {
                candidates.Add(candidate);
            }

            featureIndex++;
        }

        // A full local read is never paginated, so ResultSetTruncated is always false.
        return ValueTask.FromResult(new ParcelBoundaryAcquisition(candidates));
    }

    private string ReadFile()
    {
        if (!File.Exists(options.Path))
        {
            throw new LocalParcelFileNotFoundException($"The local parcel file '{options.Path}' does not exist.", options.Path);
        }

        try
        {
            return File.ReadAllText(options.Path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new LocalParcelFileAccessException($"The local parcel file '{options.Path}' could not be read: {ex.Message}", options.Path, ex);
        }
    }

    private JsonDocument ParseDocument(string json)
    {
        try
        {
            return JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new LocalParcelFileFormatException($"The local parcel file '{options.Path}' could not be parsed as JSON: {ex.Message}", options.Path, ex);
        }
    }

    private void ValidateCollectionShape(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("type", out JsonElement typeElement)
            || typeElement.ValueKind != JsonValueKind.String
            || typeElement.GetString() != "FeatureCollection")
        {
            throw new LocalParcelFileFormatException($"The local parcel file '{options.Path}' is not a GeoJSON FeatureCollection.", options.Path);
        }

        if (!root.TryGetProperty("features", out JsonElement features) || features.ValueKind != JsonValueKind.Array)
        {
            throw new LocalParcelFileFormatException($"The local parcel file '{options.Path}' has no 'features' array.", options.Path);
        }
    }

    /// <summary>
    /// GeoJSON's own default coordinate reference system is WGS 84; this source accepts an explicit legacy
    /// <c>crs</c> member at the FeatureCollection root or Feature level only when it agrees, reusing the exact
    /// recognition rule <c>ParcelGeometryParser.ValidateCrs</c> already applies to WKT/GeoJSON AOI input --
    /// otherwise a legacy, non-WGS-84 export would be silently misinterpreted.
    /// </summary>
    private void ValidateCrsIfPresent(JsonElement element, string levelDescription)
    {
        if (!element.TryGetProperty("crs", out JsonElement crsElement) || crsElement.ValueKind == JsonValueKind.Null)
        {
            return;
        }

        if (!ParcelGeometryParser.IsRecognizedWgs84Crs(crsElement, ParcelBoundaryWgs84.Reference))
        {
            throw new LocalParcelFileFormatException(
                $"The local parcel file '{options.Path}' declares a 'crs' member at {levelDescription} that SolidGround does not recognize as WGS 84 (CRS84 or EPSG:4326).",
                options.Path);
        }
    }

    private ParcelBoundaryCandidate? TryBuildCandidate(JsonElement feature, int featureIndex, ParcelBoundaryQuery query)
    {
        // A non-object array element can never satisfy any query (there is no properties object to read and
        // no geometry to test a point against), so it can never be "the match" -- it is always skipped, never
        // fatal, unlike every other check below whose fatality depends on this feature turning out to be
        // relevant.
        if (feature.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        LocalParcelFileFieldMap fieldMap = options.FieldMap;

        // Best-effort properties read: an unrelated feature's own missing or malformed 'properties' object
        // (for example a county-wide export's administrative/water/right-of-way row shaped like GeoJSON's own
        // spec-legal "properties": null) must never abort resolution of a different, matching feature, so a
        // failure to read it here means only "no relevance data available" -- not yet a fatal error.
        bool hasProperties = feature.TryGetProperty("properties", out JsonElement properties) && properties.ValueKind == JsonValueKind.Object;
        string? parcelId = hasProperties ? ReadOptionalString(properties, fieldMap.ParcelId) : null;
        string? situsAddress = hasProperties ? ReadOptionalString(properties, fieldMap.SitusAddress) : null;

        // A cheap relevance check against the mapped situs address property, before any of the fatal checks
        // below: an address query never needs them for a feature whose address does not match, so an unrelated
        // feature elsewhere in the file with an incomplete, blank, or entirely missing mapped field can never
        // abort resolution of a different, matching feature.
        if (query is ParcelAddressQuery addressQuery
            && (situsAddress is null || !situsAddress.Contains(addressQuery.SearchText, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        // Best-effort geometry parse, for the same reason: an unrelated feature's own missing or unparsable
        // geometry must never abort a point lookup either, so a parse failure here is treated the same as "does
        // not intersect" -- not yet fatal -- until this exact feature is confirmed to be the query's match.
        PolygonalRegion? boundary = TryParseGeometry(feature, featureIndex, out LocalParcelFileFormatException? geometryError);

        // A point query's own relevance can only be known once geometry is parsed, so it is checked here --
        // still before the fatal checks below -- so a point query gets the same protection as the address
        // query above.
        if (query is ParcelPointQuery pointQuery && (boundary is null || !IntersectsPoint(boundary, pointQuery)))
        {
            return null;
        }

        // From here on, this exact feature IS the query's match (by address or by point, or the query is
        // neither shape and every feature is unconditionally in scope): any structural problem in it is a
        // real, reportable error rather than something to silently skip.
        ValidateCrsIfPresent(feature, $"feature {featureIndex.ToString(CultureInfo.InvariantCulture)}");

        if (!hasProperties)
        {
            throw new LocalParcelFileFormatException(
                $"Feature {featureIndex.ToString(CultureInfo.InvariantCulture)} in '{options.Path}' has no 'properties' object.", options.Path);
        }

        if (boundary is null)
        {
            throw geometryError!;
        }

        if (string.IsNullOrWhiteSpace(parcelId) || string.IsNullOrWhiteSpace(situsAddress))
        {
            throw new LocalParcelFileFormatException(
                $"Feature {featureIndex.ToString(CultureInfo.InvariantCulture)} in '{options.Path}' is missing a required mapped field " +
                $"('{fieldMap.ParcelId}' or '{fieldMap.SitusAddress}').",
                options.Path);
        }

        return BuildCandidate(boundary, parcelId, situsAddress, properties);
    }

    /// <summary>
    /// Best-effort geometry parse used for relevance testing: on any structural problem this returns
    /// <see langword="null"/> rather than throwing immediately, with <paramref name="error"/> set to the
    /// exact <see cref="LocalParcelFileFormatException"/> the caller should throw if this feature turns out to
    /// be the query's actual match. An unrelated feature's own missing or malformed geometry must never abort
    /// resolution of a different, matching feature.
    /// </summary>
    private PolygonalRegion? TryParseGeometry(JsonElement feature, int featureIndex, out LocalParcelFileFormatException? error)
    {
        if (!feature.TryGetProperty("geometry", out JsonElement geometryElement) || geometryElement.ValueKind != JsonValueKind.Object)
        {
            error = new LocalParcelFileFormatException(
                $"Feature {featureIndex.ToString(CultureInfo.InvariantCulture)} in '{options.Path}' has no geometry object.", options.Path);
            return null;
        }

        try
        {
            error = null;
            return ParcelGeometryParser.Parse(ParcelGeometryFormat.GeoJson, geometryElement.GetRawText(), ParcelBoundaryWgs84.Reference);
        }
        catch (ParcelGeometryException ex)
        {
            error = new LocalParcelFileFormatException(
                $"Feature {featureIndex.ToString(CultureInfo.InvariantCulture)} in '{options.Path}' has invalid geometry: {ex.Message}", options.Path, ex);
            return null;
        }
    }

    /// <summary>
    /// A cheap bounding-box reject, then the exact, boundary-inclusive NTS test -- matching the county REST
    /// source's own explicit <c>esriSpatialRelIntersects</c> choice, so the two sources never disagree about a
    /// point that falls exactly on a parcel line.
    /// </summary>
    private static bool IntersectsPoint(PolygonalRegion boundary, ParcelPointQuery pointQuery)
    {
        PlanarEnvelope envelope = boundary.Envelope;
        if (pointQuery.Longitude < envelope.MinX || pointQuery.Longitude > envelope.MaxX
            || pointQuery.Latitude < envelope.MinY || pointQuery.Latitude > envelope.MaxY)
        {
            return false;
        }

        Point testPoint = GeometryInterop.Services.CreateGeometryFactory().CreatePoint(new Coordinate(pointQuery.Longitude, pointQuery.Latitude));
        return boundary.Geometry.Intersects(testPoint);
    }

    private ParcelBoundaryCandidate BuildCandidate(PolygonalRegion boundary, string parcelId, string situsAddress, JsonElement properties)
    {
        LocalParcelFileFieldMap fieldMap = options.FieldMap;
        double areaSquareMeters = ParcelBoundaryWgs84.ComputeAreaSquareMeters(boundary);
        string? subdivision = ReadOptionalString(properties, fieldMap.Subdivision);
        string? lot = ReadOptionalString(properties, fieldMap.Lot);
        string? block = ReadOptionalString(properties, fieldMap.Block);
        string? plat = ReadOptionalString(properties, fieldMap.Plat);
        string? book = ReadOptionalString(properties, fieldMap.Book);
        string? page = ReadOptionalString(properties, fieldMap.Page);
        string? legalDescription = ReadOptionalString(properties, fieldMap.LegalDescription);
        double? reportedAcres = ReadOptionalDouble(properties, fieldMap.ReportedAcres);
        string? zoning = ReadOptionalString(properties, fieldMap.Zoning);
        string? stableParcelId = ReadOptionalString(properties, fieldMap.StableParcelId);

        // Regrid's Standard schema defines book/page as distinct, contractually defined fields (unlike a
        // county registry's possible same-field mapping), so they are never labeled unconfirmed proxies here.
        return new ParcelBoundaryCandidate(
            boundary,
            parcelId,
            areaSquareMeters,
            ParcelBoundarySourceKind.LocalParcelFile,
            options.SourceLabel,
            options.LicenseDisclaimerText,
            situsAddress: situsAddress,
            subdivision: subdivision,
            lot: lot,
            block: block,
            plat: plat,
            book: book,
            page: page,
            bookPageAreUnconfirmedProxies: false,
            legalDescription: legalDescription,
            reportedAcres: reportedAcres,
            zoning: zoning,
            stableParcelId: stableParcelId);
    }

    /// <summary>Reads exactly one of the field map's 11 named optional slots -- never a generic properties enumeration -- so an unmapped property (including any owner/mailing property) can never reach a candidate.</summary>
    private static string? ReadOptionalString(JsonElement properties, string? fieldName)
    {
        if (fieldName is null || !properties.TryGetProperty(fieldName, out JsonElement value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText();
    }

    private static double? ReadOptionalDouble(JsonElement properties, string? fieldName)
    {
        if (fieldName is null || !properties.TryGetProperty(fieldName, out JsonElement value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double numberValue))
        {
            return numberValue;
        }

        if (value.ValueKind == JsonValueKind.String && double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedValue))
        {
            return parsedValue;
        }

        return null;
    }
}
