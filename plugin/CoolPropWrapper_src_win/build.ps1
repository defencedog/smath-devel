<#
.SYNOPSIS
    Builds the CoolPropWrapper SMath plugin and produces the installable .zip in dist\.

.DESCRIPTION
    Four steps:
      1. make sure packages\ holds the reference assemblies (copied from -PackagesSource
         on first run; see README.md for how to obtain them with nuget.exe)
      2. dotnet build src\CoolPropWrapper.csproj -c Release
      3. run the test harness against the real native engine (unless -SkipTests)
      4. stage the managed DLL with its manifests, the metadata and the native
         engines, and zip them into dist\

    The plugin targets .NET Framework 2.0, so it also loads under Mono on Linux.
    The native engines are platform and bitness specific: native\CoolProp_x86.dll
    (i686, stdcall) and native\CoolProp_x64.dll (x86-64) for Windows,
    native\libCoolProp.so (aarch64) for Linux. All are packaged at the zip root
    so one zip serves any host; the plugin loads the x86 or x64 engine by
    process bitness.

    The win32 engine and the Linux .so are unmodified prebuilt binaries. The
    win64 engine is built from the pinned CoolProp-8.0.0 source with a documented
    MinGW-w64 toolchain - see patches\README.md for the exact recipe and hashes.

.PARAMETER PackagesSource
    Folder holding the extracted NuGet packages. Copied into packages\ if that
    folder does not exist yet. Omit it if packages\ is already populated.

.PARAMETER Dotnet
    Path to the .NET SDK. Defaults to "dotnet" on PATH.

.PARAMETER Python
    Python 3 interpreter used by tools\pack.py. Defaults to "python" on PATH.

.PARAMETER SkipTests
    Do not build and run the test harness.

.EXAMPLE
    .\build.ps1 -PackagesSource ..\nuget-extracted

.EXAMPLE
    .\build.ps1 -Dotnet D:\dotnet\dotnet.exe -Python C:\Python313\python.exe
#>
param(
    [string]$PackagesSource = "",
    [string]$Dotnet = "dotnet",
    [string]$Python = "python",
    [switch]$SkipTests
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

# ---- 2. native engines -------------------------------------------------------
$nativeDir = Join-Path $root "native"
foreach ($f in @("CoolProp_x86.dll", "CoolProp_x64.dll", "libCoolProp.so")) {
    if (-not (Test-Path (Join-Path $nativeDir $f))) {
        throw "native\$f is missing. Put the prebuilt CoolProp C library there (see patches\README.md)."
    }
}

# ---- 3. managed build --------------------------------------------------------
Write-Host "`nBuilding src\CoolPropWrapper.csproj"
& $Dotnet build (Join-Path $root "src\CoolPropWrapper.csproj") -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw "build failed" }

$dll = Join-Path $root "src\bin\Release\CoolPropWrapper.dll"
if (-not (Test-Path $dll)) { throw "build produced no DLL at $dll" }

# ---- 4. tests ----------------------------------------------------------------
if ($SkipTests) {
    Write-Host "`nSkipping tests (-SkipTests)"
}
else {
    Write-Host "`nBuilding and running the test harness"
    & $Dotnet build (Join-Path $root "tests\CoolPropHHarness.csproj") -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw "test build failed" }

    $harness = Join-Path $root "tests\bin\Release\CoolPropHHarness.exe"
    if (-not (Test-Path $harness)) { throw "no test harness at $harness" }

    # The csproj stages both engines into tests\bin\Release\ (root level) - the
    # harness checks that the one matching its bitness is there.
    & $harness
    if ($LASTEXITCODE -ne 0) { throw "tests failed" }
}

# ---- 5. stage and package ---------------------------------------------------
$stage = Join-Path $root "build\package"
if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
New-Item -ItemType Directory -Force -Path $stage | Out-Null

Copy-Item -Force (Join-Path $root "packaging\CoolPropWrapper.dll.ini")    $stage
Copy-Item -Force (Join-Path $root "packaging\CoolPropWrapper.config")     $stage
Copy-Item -Force (Join-Path $root "packaging\CoolPropWrapper.dll.config") $stage
Copy-Item -Force $dll (Join-Path $stage "CoolPropWrapper.dll")

Copy-Item -Recurse -Force (Join-Path $root "packaging\metadata") $stage

# The engines sit next to the wrapper at the package root, under names that
# tell them apart by bitness; the plugin loads the matching one by path
# (EnsureLoaded picks CoolProp_x86.dll or CoolProp_x64.dll). The Linux engine
# stays at the root for the Mono dllmap. Every platform ships in one zip.
Copy-Item -Force (Join-Path $nativeDir "CoolProp_x86.dll") (Join-Path $stage "CoolProp_x86.dll")
Copy-Item -Force (Join-Path $nativeDir "CoolProp_x64.dll") (Join-Path $stage "CoolProp_x64.dll")
Copy-Item -Force (Join-Path $nativeDir "libCoolProp.so")  (Join-Path $stage "libCoolProp.so")

# version comes from the manifest, so there is one place to bump it
$ini = Get-Content (Join-Path $stage "CoolPropWrapper.dll.ini") -Raw
$version = ([regex]'"Version"\s*:\s*"([^"]+)"').Match($ini).Groups[1].Value
if ($version -eq "") { throw "could not read Version from CoolPropWrapper.dll.ini" }

$dist = Join-Path $root "dist"
New-Item -ItemType Directory -Force -Path $dist | Out-Null
$zip = Join-Path $dist ("CoolPropWrapper." + ($version -replace '\.', '_') + ".zip")

& $Python (Join-Path $root "tools\pack.py") $stage $zip
if ($LASTEXITCODE -ne 0) { throw "packaging failed" }

Write-Host "`nDone: $zip"
