namespace SolidGround.Core.Sources.CountyParcels;

/// <summary>
/// Raised by <see cref="AutoGeoidCountyParcelSource"/> itself: either no configured GEOID override was given
/// and the query was not a <see cref="ParcelPointQuery"/> (nothing to resolve a GEOID from), or the underlying
/// <see cref="Census.CensusCountyLookup"/> call raised its own <see cref="Census.CensusCountyLookupException"/>,
/// wrapped here so a caller that catches the base <see cref="ParcelBoundarySourceException"/> type -- as
/// <c>SolidGround.Revit</c>'s interactive dialog does -- observes one consistent exception family regardless of
/// which concrete parcel source is active. Never raised for a genuine cancellation: the caller's own token
/// firing during the wrapped <see cref="Census.CensusCountyLookup.FindCountyGeoidAsync"/> call still propagates
/// as a raw, unwrapped <see cref="OperationCanceledException"/>, exactly mirroring
/// <see cref="CountyParcelRegistrySource"/>'s and every other shipped source's own documented convention.
/// </summary>
public sealed class AutoGeoidCountyParcelSourceException : ParcelBoundarySourceException
{
    public AutoGeoidCountyParcelSourceException(string message, Exception? innerException = null)
        : base("AutoGeoidCountyParcel", message, innerException)
    {
    }
}
