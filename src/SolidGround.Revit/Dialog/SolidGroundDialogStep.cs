namespace SolidGround.Revit.Dialog;

/// <summary>The three intentional stages of the guided creation task.</summary>
internal enum SolidGroundDialogStep
{
    Location,
    Parcel,
    Review,
}

/// <summary>Retained for command compatibility; guided results either resolve a parcel or use another area option.</summary>
internal enum DialogAoiSource
{
    FindParcel,
    UseSettingsFile,
}

internal enum LocationEntryMode
{
    Address,
    Coordinates,
    BoundingBox,
    Radius,
    LocalGeometry,
}
