# Project setup, deploy, custom icon

## Project setup

Create a `net7.0-windows` class library and reference **`Betta.Abstractions`** (the SDK contract: attributes + marker), never the runtime `Betta.gha`. Use `ExcludeAssets="runtime"` so your DLL doesn't redistribute a copy.

Package references (both on nuget.org; add `Betta.Preview` only if you need viewport preview, bake, or `BettaImage`):
```xml
<PackageReference Include="Betta.Abstractions" Version="0.8.0" ExcludeAssets="runtime" />
<PackageReference Include="Betta.Preview" Version="0.8.0" ExcludeAssets="runtime" />
```

Project reference (working inside the Betta repo):
```xml
<ProjectReference Include="..\Betta.Abstractions\Betta.Abstractions.csproj">
  <Private>false</Private>
  <ExcludeAssets>runtime</ExcludeAssets>
</ProjectReference>
```

## Deploy

Build, then copy the DLL into `%AppData%\Grasshopper\Libraries\Betta\` (the **Betta subfolder**, not the root Libraries folder). A post-build xcopy is the usual pattern:
```xml
<PostBuildEvent>xcopy /Q/Y "$(TargetDir)MyPack.dll" "%25AppData%25\Grasshopper\Libraries\Betta\" &amp; exit /b 0</PostBuildEvent>
```
Betta watches that folder, so a new DLL is hot-added without restarting Rhino (overwriting an already-loaded DLL still needs a restart).

## Custom icon (opt out of the default betta silhouette)

Ship a PNG as an embedded resource and reference it by name suffix:
```xml
<EmbeddedResource Include="Resources\my_icon.png" />
```
```csharp
[GrasshopperMethod("Foo", IconResource = "my_icon.png")]
double Foo(double x);
```
The runtime matches `IconResource` against any embedded resource name **ending with** the string, so the namespace prefix isn't required. The PNG renders verbatim. Without `IconResource`, the component gets a betta silhouette picked deterministically from its GUID.
