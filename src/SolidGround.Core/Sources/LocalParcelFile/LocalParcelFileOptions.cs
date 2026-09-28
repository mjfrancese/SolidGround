namespace SolidGround.Core.Sources.LocalParcelFile;

/// <summary>The local-file property name for each surfaced <see cref="ParcelBoundaryCandidate"/> attribute. Defaults target Regrid's Standard schema.</summary>
public sealed record LocalParcelFileFieldMap
{
    public string ParcelId { get; init; } = "parcelnumb";
    public string SitusAddress { get; init; } = "address";
    public string? Subdivision { get; init; } = "subdivision";
    public string? Lot { get; init; } = "lot";
    public string? Block { get; init; } = "block";
    public string? Plat { get; init; } = "plat";
    public string? Book { get; init; } = "book";
    public string? Page { get; init; } = "page";
    public string? LegalDescription { get; init; } = "legaldesc";

    /// <summary>Regrid's own calculated acreage field, always present per Regrid's Standard schema.</summary>
    public string? ReportedAcres { get; init; } = "ll_gisacre";

    public string? Zoning { get; init; } = "zoning";
    public string? StableParcelId { get; init; } = "ll_uuid";

    /// <summary>The field map matching Regrid's Standard schema exactly, with no owner/mailing/enhanced-ownership field mapped.</summary>
    public static LocalParcelFileFieldMap RegridStandardDefault { get; } = new();

    /// <exception cref="ArgumentException">A mapped field name is blank, or looks like an owner/mailing field name per <see cref="OwnerFieldNameGuard"/>.</exception>
    public void Validate()
    {
        RequireValid(ParcelId, nameof(ParcelId));
        RequireValid(SitusAddress, nameof(SitusAddress));
        RequireValidIfGiven(Subdivision, nameof(Subdivision));
        RequireValidIfGiven(Lot, nameof(Lot));
        RequireValidIfGiven(Block, nameof(Block));
        RequireValidIfGiven(Plat, nameof(Plat));
        RequireValidIfGiven(Book, nameof(Book));
        RequireValidIfGiven(Page, nameof(Page));
        RequireValidIfGiven(LegalDescription, nameof(LegalDescription));
        RequireValidIfGiven(ReportedAcres, nameof(ReportedAcres));
        RequireValidIfGiven(Zoning, nameof(Zoning));
        RequireValidIfGiven(StableParcelId, nameof(StableParcelId));
    }

    private static void RequireValid(string value, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{propertyName} must not be blank.", propertyName);
        }

        if (OwnerFieldNameGuard.IsOwnerLike(value))
        {
            throw new ArgumentException(
                $"{propertyName} maps to '{value}', which looks like an owner/mailing field name; SolidGround never maps one.", propertyName);
        }
    }

    private static void RequireValidIfGiven(string? value, string propertyName)
    {
        if (value is not null)
        {
            RequireValid(value, propertyName);
        }
    }
}

/// <summary>Configures <see cref="LocalParcelFileSource"/>: which file to read, its field map, and its provenance labels.</summary>
public sealed record LocalParcelFileOptions
{
    /// <summary>The path to a user-purchased county parcel export in GeoJSON format. Read fresh on every <see cref="LocalParcelFileSource.FindAsync"/> call; never cached.</summary>
    public required string Path { get; init; }

    public LocalParcelFileFieldMap FieldMap { get; init; } = LocalParcelFileFieldMap.RegridStandardDefault;

    /// <summary>A human-readable label for this file (for example "Local Regrid Standard export"). Host-supplied.</summary>
    public required string SourceLabel { get; init; }

    /// <summary>The file's own license/disclaimer text, surfaced verbatim. Host-supplied; SolidGround never hardcodes a real license's text.</summary>
    /// <remarks>
    /// If this option is ever configured against a real, purchased Regrid Data Store export, include the
    /// purchase date and the license's own one-year Term, after which it requires the licensee to promptly
    /// cease all use of the data or delete it entirely, directly in this string. This exact string is what the
    /// dialog's provenance/accuracy preview panel displays verbatim, every run (<c>parcelDisclaimer</c> in
    /// <c>SolidGround.Revit/Dialog/SolidGroundDialog.cs</c>), so it is where an operator will actually see that
    /// obligation before the Term lapses. See docs/architecture/source-licensing-and-attribution.md's "Regrid
    /// Data Store obligations" section for the license's exact wording. As of this writing no Regrid data has
    /// ever been purchased, so this remains an instruction for a future real configuration, not a description
    /// of any data shipped here.
    /// </remarks>
    public required string LicenseDisclaimerText { get; init; }
}
