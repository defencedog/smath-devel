<#
.SYNOPSIS
    Builds the PsychroLib SMath plugin and produces the installable .zip in dist\.

.DESCRIPTION
    Three steps:
      1. make sure packages\ holds the reference assemblies (copied from -PackagesSource
         on first run; see README.md for how to obtain them with nuget.exe)
      2. dotnet build src\PsychroLibSMath.csproj -c Release
      3. stage the DLL next to the manifests in packaging\ and zip them into dist\

    The plugin targets .NET Framework 2.0, so it also loads under Mono on Linux.

.PARAMETER PackagesSource
    Folder holding the extracted NuGet packages. Copied into packages\ if that
    folder does not exist yet. Omit it if packages\ is already populated.

.PARAMETER Dotnet
    Path to the .NET SDK. Defaults to "dotnet" on PATH.

.PARAMETER Python
    Python 3 interpreter used by tools\pack.py. Defaults to "python" on PATH.

.EXAMPLE
    .\build.ps1 -PackagesSource ..\nuget-extracted
#>
param(
    [string]$PackagesSource = "",
    [string]$Dotnet = "dotnet",
    [string]$Python = "python"
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$packages = Join-Path $root "packages"

# ---- 1. reference assemblies ------------------------------------------------
if (-not (Test-Path $packages)) {
    if ($PackagesSource -eq "") {
        throw "packages\ is missing. Re-run with -PackagesSource <dir> pointing at the extracted NuGet packages, or create packages\ manually (see README.md)."
    }
    if (-not (Test-Path $PackagesSource)) {
        throw "PackagesSource not found: $PackagesSource"
    }
    Write-Host "Copying reference assemblies from $PackagesSource"
    Copy-Item -Recurse -Force $PackagesSource $packages
}

foreach ($needed in @(
    "microsoft.netframework.referenceassemblies.net20.1.0.3",
    "smathstudio.core.ref.1.73.9126",
    "smathstudio.drawing.ref.1.4.0")) {
    if (-not (Test-Path (Join-Path $packages $needed))) {
        throw "packages\$needed is missing - the tree cannot build."
    }
}

# ---- 2. build ---------------------------------------------------------------
Write-Host "`nBuilding src\PsychroLibSMath.csproj"
& $Dotnet build (Join-Path $root "src\PsychroLibSMath.csproj") -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw "build failed" }

$dll = Join-Path $root "src\bin\Release\PsychroLibWrapper.dll"
if (-not (Test-Path $dll)) { throw "build produced no DLL at $dll" }

# ---- 3. stage and package ---------------------------------------------------
$stage = Join-Path $root "build\package"
if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
New-Item -ItemType Directory -Force -Path $stage | Out-Null

Copy-Item -Recurse -Force (Join-Path $root "packaging\*") $stage
Copy-Item -Force $dll (Join-Path $stage "PsychroLibWrapper.dll")

# version comes from the manifest, so there is one place to bump it
$ini = Get-Content (Join-Path $stage "PsychroLibWrapper.dll.ini") -Raw
$version = ([regex]'"Version"\s*:\s*"([^"]+)"').Match($ini).Groups[1].Value
if ($version -eq "") { throw "could not read Version from PsychroLibWrapper.dll.ini" }

$dist = Join-Path $root "dist"
New-Item -ItemType Directory -Force -Path $dist | Out-Null
$zip = Join-Path $dist ("PsychroLibWrapper." + ($version -replace '\.', '_') + ".zip")

& $Python (Join-Path $root "tools\pack.py") $stage $zip
if ($LASTEXITCODE -ne 0) { throw "packaging failed" }

Write-Host "`nDone: $zip"
