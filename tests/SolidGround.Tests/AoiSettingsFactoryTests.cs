using SolidGround.Core.Aois;
using SolidGround.Core.Metadata;
using SolidGround.Core.Processing;
using SolidGround.Core.Units;

namespace SolidGround.Tests;

/// <summary>
/// Direct tests for <see cref="AoiSettingsFactory.Build"/>: it reuses the real
/// <c>Wgs84BoundingBoxAoi</c>/<c>Wgs84RadiusAoi</c>/<c>ParcelGeometryAoi</c> constructors for whichever one
/// <see cref="AoiSettings.Kind"/> selects, reading only that form's sub-object.
/// </summary>
public sealed class AoiSettingsFactoryTests
{
    [Fact]
    public void BuildForBoundingBoxProducesAValueEqualWgs84BoundingBoxAoi()
    {
        AoiSettings settings = new()
        {
            Kind = AreaOfInterestKind.BoundingBox,
            BoundingBox = new BoundingBoxAoiSettings { West = -90.5, South = 38.6, East = [withheld], North = 38.7 },
        };

        AreaOfInterest result = AoiSettingsFactory.Build(settings, Wgs84Reference(), parcelGeometryText: null);

        Assert.Equal(new Wgs84BoundingBoxAoi(-90.5, 38.6, [withheld], 38.7), result);
    }

    [Fact]
    public void BuildForRadiusProducesAValueEqualWgs84RadiusAoi()
    {
        AoiSettings settings = new()
        {
            Kind = AreaOfInterestKind.Radius,
            Radius = new RadiusAoiSettings { CenterLatitude = 38.7, CenterLongitude = [withheld], RadiusMeters = 75d },
        };

        AreaOfInterest result = AoiSettingsFactory.Build(settings, Wgs84Reference(), parcelGeometryText: null);

        Assert.Equal(new Wgs84RadiusAoi(38.7, [withheld], LinearDistance.Meters(75d)), result);
    }

    [Fact]
    public void BuildForParcelReprojectsTheSuppliedGeometryText()
    {
        const string geometryText = "POLYGON (([withheld] [withheld], [withheld] [withheld], [withheld] [withheld], [withheld] [withheld], [withheld] [withheld]))";
        AoiSettings settings = new()
        {
            Kind = AreaOfInterestKind.Parcel,
            Parcel = new ParcelAoiSettings { Path = "parcel.wkt", Format = "wkt", BufferMeters = 5d },
        };

        AreaOfInterest result = AoiSettingsFactory.Build(settings, Wgs84Reference(), geometryText);

        ParcelGeometryAoi parcel = Assert.IsType<ParcelGeometryAoi>(result);
        Assert.Same(geometryText, parcel.Geometry);
        Assert.Equal(ParcelGeometryFormat.Wkt, parcel.Format);
        Assert.Equal(Wgs84Reference(), parcel.HorizontalReference);
        Assert.Equal(5d, parcel.Buffer.ToMeters());
    }

    [Fact]
    public void BuildThrowsWhenParcelTextIsRequiredButNull()
    {
        AoiSettings settings = new()
        {
            Kind = AreaOfInterestKind.Parcel,
            Parcel = new ParcelAoiSettings { Path = "parcel.geojson", Format = "geojson" },
        };

        Assert.Throws<FormatException>(() => AoiSettingsFactory.Build(settings, Wgs84Reference(), parcelGeometryText: null));
    }

    [Fact]
    public void BuildThrowsAFormatExceptionWhenParcelPathIsNullAndFormatIsUnset()
    {
        // ParcelAoiSettings.Path is `required`, but System.Text.Json accepts an explicit JSON `null` for a
        // required reference-typed property; without this guard, ResolveParcelFormat's extension-inference
        // fallback would call Path.GetExtension(null) and then NullReferenceException on the result.
        AoiSettings settings = new()
        {
            Kind = AreaOfInterestKind.Parcel,
            Parcel = new ParcelAoiSettings { Path = null!, Format = null },
        };

        Assert.Throws<FormatException>(() => AoiSettingsFactory.Build(settings, Wgs84Reference(), parcelGeometryText: "irrelevant"));
    }

    [Fact]
    public void BuildThrowsAFormatExceptionWhenParcelPathIsBlankEvenWithFormatGivenExplicitly()
    {
        // An explicit Format short-circuits ResolveParcelFormat's extension-inference branch entirely, so a
        // blank Path must be rejected up front rather than silently accepted whenever Format is given.
        AoiSettings settings = new()
        {
            Kind = AreaOfInterestKind.Parcel,
            Parcel = new ParcelAoiSettings { Path = "   ", Format = "wkt" },
        };

        Assert.Throws<FormatException>(() => AoiSettingsFactory.Build(settings, Wgs84Reference(), parcelGeometryText: "irrelevant"));
    }

    [Theory]
    [InlineData("parcel.geojson", ParcelGeometryFormat.GeoJson)]
    [InlineData("parcel.json", ParcelGeometryFormat.GeoJson)]
    [InlineData("parcel.wkt", ParcelGeometryFormat.Wkt)]
    [InlineData(@"C:\SolidGround\PARCEL.WKT", ParcelGeometryFormat.Wkt)]
    public void BuildInfersTheParcelFormatFromThePathsExtensionWhenFormatIsUnset(string path, ParcelGeometryFormat expectedFormat)
    {
        const string geometryText = "irrelevant";
        AoiSettings settings = new()
        {
            Kind = AreaOfInterestKind.Parcel,
            Parcel = new ParcelAoiSettings { Path = path, Format = null },
        };

        AreaOfInterest result = AoiSettingsFactory.Build(settings, Wgs84Reference(), geometryText);

        ParcelGeometryAoi parcel = Assert.IsType<ParcelGeometryAoi>(result);
        Assert.Equal(expectedFormat, parcel.Format);
    }

    [Fact]
    public void BuildThrowsAFormatExceptionWhenFormatIsUnsetAndThePathsExtensionCannotBeInferred()
    {
        AoiSettings settings = new()
        {
            Kind = AreaOfInterestKind.Parcel,
            Parcel = new ParcelAoiSettings { Path = "parcel.txt", Format = null },
        };

        FormatException ex = Assert.Throws<FormatException>(() => AoiSettingsFactory.Build(settings, Wgs84Reference(), parcelGeometryText: "irrelevant"));
        Assert.Contains("cannot be inferred", ex.Message, StringComparison.Ordinal);
    }

    private static HorizontalReference Wgs84Reference() => new(
        "WGS 84", "World Geodetic System 1984", HorizontalReferenceKind.Geographic, HorizontalUnit.DecimalDegrees, HorizontalAxisOrder.LongitudeLatitude);
}
