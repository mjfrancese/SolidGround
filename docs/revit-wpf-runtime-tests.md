# Local WPF runtime checks

`SolidGround.Revit.Tests` is a local Windows-only WPF test lane for the real `SolidGround.Revit` dialog bindings and rendered-control checks. It is intentionally absent from `SolidGround.slnx` and CI. The checks run in a dedicated STA test thread and never start Revit or drive its UI.

Install Revit 2027 locally, then run from the repository root:

```powershell
dotnet restore tests\SolidGround.Revit.Tests\SolidGround.Revit.Tests.csproj --locked-mode
dotnet test tests\SolidGround.Revit.Tests\SolidGround.Revit.Tests.csproj --configuration Release --no-restore
```

The project defaults `RevitInstallDir` to `C:\Program Files\Autodesk\Revit 2027`. Override it only for another local Revit 2027 installation:

```powershell
dotnet test tests\SolidGround.Revit.Tests\SolidGround.Revit.Tests.csproj --configuration Release --no-restore -p:RevitInstallDir='D:\Autodesk\Revit 2027'
```

The installed SDK assemblies are copied only into ignored test build output. Do not commit them. Rendered PNG evidence, when a rendered-control test produces it, is written under ignored `TestResults\` output.