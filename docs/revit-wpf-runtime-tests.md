# Local WPF runtime checks

`SolidGround.Revit.Tests` is a local Windows-only WPF test lane for the real `SolidGround.Revit` dialog bindings, settings recovery controls, and WPF control resources. It is intentionally absent from `SolidGround.slnx` and CI. The checks run in a dedicated STA test thread and never start Revit or drive its UI.

Install Revit 2027 locally, then run from the repository root:

```powershell
dotnet restore tests\SolidGround.Revit.Tests\SolidGround.Revit.Tests.csproj --locked-mode
dotnet test --project tests\SolidGround.Revit.Tests\SolidGround.Revit.Tests.csproj --configuration Release --no-restore
```

The project defaults `RevitInstallDir` to `C:\Program Files\Autodesk\Revit 2027`. Override it only for another local Revit 2027 installation:

```powershell
dotnet test --project tests\SolidGround.Revit.Tests\SolidGround.Revit.Tests.csproj --configuration Release --no-restore -p:RevitInstallDir='D:\Autodesk\Revit 2027'
```

The installed SDK assemblies are copied only into ignored test build output. Do not commit them.

The lane checks actual binding status and notification recovery, the production light/dark/high-contrast palette selector, effective control values, and the real Settings control inventory. It also has a strict rendered-pixel gate: it first renders a red WPF reference visual. If that reference is all-transparent, the rendered-control case is explicitly skipped and no PNG is evidence. This can occur in a non-interactive test session. In that case, use the Issue #60 manual runtime checklist for theme, scale, and visual-state inspection; do not report this local lane as visual rendering proof.
