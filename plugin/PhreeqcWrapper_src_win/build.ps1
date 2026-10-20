<#
.SYNOPSIS
    Builds the PhreeqcWrapper SMath plugin and produces the installable .zip in dist\.

.DESCRIPTION
    Five steps:
      1. make sure packages\ holds the reference assemblies (copied from -PackagesSource
         on first run; see README.md for how to obtain them with nuget.exe)
      2. (optional, -BuildNative) rebuild both Windows engines from the VIPhreeqc
         C++ sources with CMake + MinGW
      3. dotnet build src\PhreeqcWrapper.csproj -c Release
      4. build and run the test harness against the real native engine (unless -SkipTests)
      5. stage the managed DLL with its manifests, the metadata, the databases and
         the three native engines, and zip them into dist\

    The plugin targets .NET Framework 2.0, so it also loads under Mono on Linux.
    Three engines ship flat at the package root: IPhreeqc_x86.dll (i686) and
    IPhreeqc_x64.dll (x86-64) for Windows - the plugin loads the one matching the
    host process bitness (SMath on 64-bit Windows is a 64-bit process) - and
    libIPhreeqc.so for Linux. The databases stay in a databases\ subfolder.

.PARAMETER PackagesSource
    Folder holding the extracted NuGet packages. Copied into packages\ if that
    folder does not exist yet. Omit it if packages\ is already populated.

.PARAMETER Dotnet
    Path to the .NET SDK. Defaults to "dotnet" on PATH.

.PARAMETER Python
    Python 3 interpreter used by tools\pack.py. Defaults to "python" on PATH.

.PARAMETER SkipTests
    Do not build and run the test harness.

.PARAMETER BuildNative
    Also rebuild native\IPhreeqc_x86.dll and native\IPhreeqc_x64.dll from the
    VIPhreeqc C++ sources before packaging.

.PARAMETER PhreeqcSource
    Path to the VIPhreeqc checkout. Required with -BuildNative.

.PARAMETER Toolchain
    Root of the MinGW-w64 i686 toolchain (the folder containing bin\gcc.exe).
    Defaults to C:\mingw32.

.PARAMETER ToolchainX64
    Root of the MinGW-w64 x86-64 toolchain (bin\gcc.exe with an x86_64 triple).
    Defaults to ..\_toolchains\w64devkit.

.EXAMPLE
    .\build.ps1 -PackagesSource ..\nuget-extracted

.EXAMPLE
    .\build.ps1 -BuildNative -PhreeqcSource ..\VIPhreeqc-master
#>
param(
    [string]$PackagesSource = "",
    [string]$Dotnet = "dotnet",
    [string]$Python = "python",
    [switch]$SkipTests,
    [switch]$BuildNative,
    [string]$PhreeqcSource = "",
    [string]$Toolchain = "C:\mingw32",
    [string]$ToolchainX64 = ""
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

# ---- 2. native engines (optional) -------------------------------------------
# Three engines ship flat at the package root: IPhreeqc_x86.dll and
# IPhreeqc_x64.dll (Windows, the plugin loads the one matching the host process
# bitness) and libIPhreeqc.so (Mono). All three are packaged in the one zip.
$nativeDir = Join-Path $root "native"
$engineX86 = Join-Path $nativeDir "IPhreeqc_x86.dll"
$engineX64 = Join-Path $nativeDir "IPhreeqc_x64.dll"
$engineSo  = Join-Path $nativeDir "libIPhreeqc.so"

if ($BuildNative) {
    if ($PhreeqcSource -eq "") {
        throw "-BuildNative needs -PhreeqcSource <path to the VIPhreeqc checkout>."
    }
    if ([System.IO.Path]::DirectorySeparatorChar -ne '\') {
        throw "-BuildNative currently drives CMake for Windows DLLs. On Linux, build libIPhreeqc.so with CMake yourself and drop it in native\."
    }
    if ($ToolchainX64 -eq "") {
        $ToolchainX64 = Join-Path $root "..\_toolchains\w64devkit"
    }
    Write-Host "`nBuilding the x86 engine from $PhreeqcSource"
    & (Join-Path $nativeDir "build-native.ps1") -PhreeqcSource $PhreeqcSource -Toolchain $Toolchain -OutputDir $nativeDir
    if ($LASTEXITCODE -ne 0) { throw "native x86 build failed" }

    Write-Host "`nBuilding the x64 engine from $PhreeqcSource"
    & (Join-Path $nativeDir "build-native.ps1") -PhreeqcSource $PhreeqcSource -Toolchain $ToolchainX64 -OutputDir $nativeDir
    if ($LASTEXITCODE -ne 0) { throw "native x64 build failed" }
}

foreach ($engine in @($engineX86, $engineX64, $engineSo)) {
    if (-not (Test-Path $engine)) {
        throw "$engine is missing. Run with -BuildNative -PhreeqcSource <dir>, or put the prebuilt engine there (see patches\README.md)."
    }
}

# ---- 3. managed build -------------------------------------------------------
Write-Host "`nBuilding src\PhreeqcWrapper.csproj"
& $Dotnet build (Join-Path $root "src\PhreeqcWrapper.csproj") -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw "build failed" }

$dll = Join-Path $root "src\bin\Release\PhreeqcWrapper.dll"
if (-not (Test-Path $dll)) { throw "build produced no DLL at $dll" }

# ---- 4. tests ----------------------------------------------------------------
if ($SkipTests) {
    Write-Host "`nSkipping tests (-SkipTests)"
}
else {
    Write-Host "`nBuilding and running the test harness"
    & $Dotnet build (Join-Path $root "tests\PhreeqcHarness.csproj") -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw "test build failed" }

    $harness = Join-Path $root "tests\bin\Release\PhreeqcHarness.exe"
    if (-not (Test-Path $harness)) { throw "no test harness at $harness" }

    # The csproj stages both engines into tests\bin\Release\ (root level) - the
    # harness checks that the one matching its bitness is there and runs an
    # end-to-end PHREEQC calculation through it.
    & $harness
    if ($LASTEXITCODE -ne 0) { throw "tests failed" }
}

# ---- 5. stage and package ---------------------------------------------------
$stage = Join-Path $root "build\package"
if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
New-Item -ItemType Directory -Force -Path $stage | Out-Null

Copy-Item -Force (Join-Path $root "packaging\PhreeqcWrapper.dll.ini")   $stage
Copy-Item -Force (Join-Path $root "packaging\PhreeqcWrapper.dll.config") $stage
Copy-Item -Force $dll (Join-Path $stage "PhreeqcWrapper.dll")

# Databases live in a subfolder so the plugin directory stays readable; the
# wrapper resolves bare names there first.
New-Item -ItemType Directory -Force -Path (Join-Path $stage "databases") | Out-Null
Copy-Item -Force (Join-Path $root "packaging\databases\*.dat") (Join-Path $stage "databases")

Copy-Item -Recurse -Force (Join-Path $root "packaging\metadata") $stage

# The engines sit next to the wrapper at the package root, under names that
# tell them apart by bitness; the plugin loads the matching one by path
# (EnsureLoaded picks IPhreeqc_x86.dll or IPhreeqc_x64.dll). The Linux engine
# stays at the root for the Mono dllmaps. One zip serves every host.
Copy-Item -Force $engineX86 (Join-Path $stage "IPhreeqc_x86.dll")
Copy-Item -Force $engineX64 (Join-Path $stage "IPhreeqc_x64.dll")
Copy-Item -Force $engineSo  (Join-Path $stage "libIPhreeqc.so")

# version comes from the manifest, so there is one place to bump it
$ini = Get-Content (Join-Path $stage "PhreeqcWrapper.dll.ini") -Raw
$version = ([regex]'"Version"\s*:\s*"([^"]+)"').Match($ini).Groups[1].Value
if ($version -eq "") { throw "could not read Version from PhreeqcWrapper.dll.ini" }

$dist = Join-Path $root "dist"
New-Item -ItemType Directory -Force -Path $dist | Out-Null
$zip = Join-Path $dist ("PhreeqcWrapper." + ($version -replace '\.', '_') + ".zip")

& $Python (Join-Path $root "tools\pack.py") $stage $zip
if ($LASTEXITCODE -ne 0) { throw "packaging failed" }

Write-Host "`nDone: $zip"
