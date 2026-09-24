# Ribbon icons

`SolidGround.16.png` (16x16) and `SolidGround.32.png` (32x32) are the ribbon icons for the
`CreateToposolidCommand` button: an isometric terrain block with an undulating green top, a closed 1px
orange property-line loop around the top perimeter, and soil-brown sides (32px adds a stratum line and a
bevel highlight) — a terrain-and-parcel metaphor for "clip terrain to one parcel," Issue #19's design brief.
They replace the placeholder pair Issue #14 shipped (a simple green hill silhouette generated with
`System.Drawing` from PowerShell).

## Format contract

Both files are 8-bit-per-channel RGBA (PNG colour type 6), non-interlaced, with exactly three chunks in file
order — `IHDR`, `IDAT`, `IEND` — and no `pHYs`/`gAMA`/`sRGB`/`cHRM`/`iCCP` chunk.
`tests/SolidGround.Tests/RevitHostFilesTests.cs` enforces this offline: IHDR bit depth/colour type/interlace,
the exact chunk list, a transparent background behind an opaque glyph (including all four corners), exact
pixel dimensions, and — as an allow-list, so a stray design-exploration file can never land here unnoticed —
that this directory holds only these two PNGs and this README. See that test file's "(f) Ribbon icons"
region.

## Resource convention (unchanged by this issue)

Both files are embedded resources with fixed `LogicalName`s (`SolidGround.Revit.csproj`), loaded by
`SolidGroundApplication.LoadIcon` through `GetManifestResourceStream` and `BitmapFrame.Create`, and assigned
to `PushButtonData.Image`/`.LargeImage`. Issue #19 replaced only these two files' bytes; the embedded-resource
mechanism, logical names, and loading code are unchanged from Issue #14.

## Generation notes (brief)

Every pixel in both files was hand-authored in text pixel grids by Claude Sonnet subagents (orchestrated by
Claude Fable) and rendered by the throwaway `icontool` renderer (not committed), a bespoke, offline,
standard-library-only .NET tool built for this issue — no NuGet imaging package, and no AI-generated pixel in
either shipped file. Some of the out-of-repo, not-committed grid-geometry tooling that produced and revised
those text pixel grids was written in small Python helper scripts; see
`docs/architecture/revit-ribbon-icons.md`'s "Design process" section for the full disclosure. PixelLab's
`create_image_pixen` was called nine times during the design process as an
optional, capped, non-authoritative concept reference only (never traced, downloaded, or otherwise
incorporated into a deliverable); no PixelLab job ID, download URL, or account identifier is recorded anywhere
in this repository (seeds, where applicable, are recorded in `docs/architecture/revit-ribbon-icons.md` per
Issue #19's AC5). Every edit was made by Claude agents iterating against measured contrast/silhouette/connectivity
checks, not human cleanup of generated pixels — there was no generated output to clean up. On 2026-09-24,
the owner was shown the final pair in chat and replied "go," requesting no change; no separate formal sign-off
beyond that reply is recorded. The full design lineage (four initial concepts, refinement,
polish, two rejected revisions, the chosen 32px design, two competing 16px companions, prompt intents,
seeds, palette, and WCAG contrast evidence) is recorded in `docs/architecture/revit-ribbon-icons.md`.

## SHA-256

- `SolidGround.16.png`: `529a5b02b0161927cfc53bebe433977b9611f50e24995ea7a922e808e4cfe26d`
- `SolidGround.32.png`: `1be0c1d3a230fa931385609eeb072bc3ec69705d302a6c1edc753916cef073b5`

See `docs/architecture/revit-ribbon-icons.md` for the full design record, palette, contrast evidence, and
the manual evidence plan for Revit 2027's light/dark ribbon contexts.
