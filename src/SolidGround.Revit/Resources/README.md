# Ribbon icon placeholders

`SolidGround.16.png` (16x16) and `SolidGround.32.png` (32x32) are **placeholder** ribbon icons for the
`CreateToposolidCommand` button: a simple green terrain-hill silhouette with a darker green parcel outline,
generated deterministically with `System.Drawing` from PowerShell (32-bit ARGB, transparent background).

They exist so the initial milestone (Issue #14) ships a real, working icon-loading path end to end, per
`docs/architecture/revit-add-in-conventions.md` owner decision 5 ("Ship an icon for `CreateToposolidCommand`
in the initial milestone; Issue #19 designs it"). **Issue #19 replaces these bytes** with a designed icon;
the embedded-resource mechanism, logical names, and `SolidGroundApplication`'s loading code do not need to
change when it does.
