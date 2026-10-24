<#
.SYNOPSIS
    Builds the OptimWrapper SMath plugin and produces the installable .zip in dist\.

.DESCRIPTION
    Five steps:
      1. make sure packages\ holds the reference assemblies (copied from -PackagesSource
         on first run)
      2. (optional, -BuildNative) rebuild both Windows engines from OptimLib +
         Eigen with MinGW
      3. dotnet build src\OptimWrapper.csproj -c Release
      4. build and run the test harness against the real native engine (unless -SkipTests)
      5. stage the managed DLL with its manifests, metadata, licence files and
         native engines, and zip them into dist\

    The plugin targets .NET Framework 2.0, so it also loads under Mono on Linux
    (the aarch64 libOptim.so is built separately, see patches\README.md). Three
    engines ship flat at the package root: Optim_x86.dll (i686), Optim_x64.dll
    (x86-64) and libOptim.so (aarch64) - the plugin loads the one matching the
    host process bitness (SMath on 64-bit Windows is a 64-bit process), and
    Mono resolves both Windows names to the .so via dllmap.

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
    Also rebuild native\Optim_x86.dll and native\Optim_x64.dll from the OptimLib
    sources before packaging.

.PARAMETER OptimSource
    Path to the OptimLib checkout. Required with -BuildNative.

.PARAMETER EigenPath
    Path to the Eigen 3.4 headers. Required with -BuildNative.

.PARAMETER Toolchain
    Root of the MinGW-w64 i686 toolchain (the folder containing bin\gcc.exe).
    Defaults to C:\mingw32.

.PARAMETER ToolchainX64
    Root of the MinGW-w64 x86-64 toolchain (bin\gcc.exe with an x86_64 triple).
    Defaults to ..\_toolchains\w64devkit.

.EXAMPLE
    .\build.ps1 -PackagesSource ..\nuget-extracted -Dotnet ..\dotnet\dotnet.exe

.EXAMPLE
    .\build.ps1 -BuildNative -OptimSource ..\optim-master -EigenPath ..\_odeps\eigen-3.4.0
#>
param(
    [string]$PackagesSource = "",
    [string]$Dotnet = "dotnet",
    [string]$Python = "python",
    [switch]$SkipTests,
    [switch]$BuildNative,
    [string]$OptimSource = "",
    [string]$EigenPath = "",
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
# The two Windows engines ship flat at the package root: Optim_x86.dll and Optim_x64.dll.
# libOptim.so is optional and only present if someone built it separately.
$nativeDir = Join-Path $root "native"
$engineX86 = Join-Path $nativeDir "Optim_x86.dll"
$engineX64 = Join-Path $nativeDir "Optim_x64.dll"
$engineSo  = Join-Path $nativeDir "libOptim.so"

if ($BuildNative) {
    if ($OptimSource -eq "") {
        throw "-BuildNative needs -OptimSource <path to the OptimLib checkout>."
    }
    if ($EigenPath -eq "") {
        throw "-BuildNative needs -EigenPath <path to the Eigen 3.4 headers>."
    }
    if ([System.IO.Path]::DirectorySeparatorChar -ne '\') {
        throw "-BuildNative currently drives MinGW for Windows DLLs. On Linux, build libOptim.so with the same optim_shim.cpp and drop it in native\."
    }
    if ($ToolchainX64 -eq "") {
        $ToolchainX64 = Join-Path $root "..\_toolchains\w64devkit"
    }
    Write-Host "`nBuilding the x86 engine from $OptimSource"
    & (Join-Path $nativeDir "build-native.ps1") -OptimSource $OptimSource -EigenPath $EigenPath -Toolchain $Toolchain -OutputDir $nativeDir
    if ($LASTEXITCODE -ne 0) { throw "native x86 build failed" }

    Write-Host "`nBuilding the x64 engine from $OptimSource"
    & (Join-Path $nativeDir "build-native.ps1") -OptimSource $OptimSource -EigenPath $EigenPath -Toolchain $ToolchainX64 -OutputDir $nativeDir
    if ($LASTEXITCODE -ne 0) { throw "native x64 build failed" }
}

foreach ($engine in @($engineX86, $engineX64)) {
    if (-not (Test-Path $engine)) {
        throw "$engine is missing. Run with -BuildNative -OptimSource <dir> -EigenPath <dir>, or put the prebuilt engine there (see patches\README.md)."
    }
}

# ---- 3. managed build -------------------------------------------------------
Write-Host "`nBuilding src\OptimWrapper.csproj"
& $Dotnet build (Join-Path $root "src\OptimWrapper.csproj") -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw "build failed" }

$dll = Join-Path $root "src\bin\Release\OptimWrapper.dll"
if (-not (Test-Path $dll)) { throw "build produced no DLL at $dll" }

# ---- 4. tests ----------------------------------------------------------------
if ($SkipTests) {
    Write-Host "`nSkipping tests (-SkipTests)"
}
else {
    Write-Host "`nBuilding and running the test harness"
    # -t:Rebuild + explicit PlatformTarget: PlatformTarget is not a tracked
    # msbuild input, so an incremental build would keep whatever bitness the
    # previous run produced (x64 after a manual x64 check).
    & $Dotnet build (Join-Path $root "tests\OptimHarness.csproj") -c Release -p:PlatformTarget=x86 -t:Rebuild --nologo
    if ($LASTEXITCODE -ne 0) { throw "test build failed" }

    $harness = Join-Path $root "tests\bin\Release\OptimHarness.exe"
    if (-not (Test-Path $harness)) { throw "no test harness at $harness" }

    # The csproj stages both Windows engines into tests\bin\Release\ (root level) - the
    # harness checks that the one matching its bitness is there and runs real
    # optimisation problems through it.
    & $harness
    if ($LASTEXITCODE -ne 0) { throw "tests failed" }
}

# The x86 harness build above passes PlatformTarget=x86 to its ProjectReference,
# which rewrites src\bin\Release\OptimWrapper.dll as 32BITREQUIRED. A 64-bit
# SMath host then refuses to load the plugin ("incorrect format"). Rebuild the
# managed assembly as AnyCPU before staging, and verify the COR flags so this
# cannot regress silently.
Write-Host "`nRebuilding the managed plugin as AnyCPU for packaging"
& $Dotnet build (Join-Path $root "src\OptimWrapper.csproj") -c Release -p:PlatformTarget=AnyCPU -t:Rebuild --nologo
if ($LASTEXITCODE -ne 0) { throw "AnyCPU rebuild failed" }

function Test-AnyCpuAssembly([string]$path) {
    $bytes = [System.IO.File]::ReadAllBytes($path)
    $pe = [System.BitConverter]::ToInt32($bytes, 0x3C)
    $opt = $pe + 24
    $corRva = [System.BitConverter]::ToInt32($bytes, $opt + 208)
    $numSections = [System.BitConverter]::ToUInt16($bytes, $pe + 6)
    $optSize = [System.BitConverter]::ToUInt16($bytes, $pe + 20)
    $section = $opt + $optSize
    $corOffset = -1
    for ($i = 0; $i -lt $numSections; $i++) {
        $s = $section + $i * 40
        $va = [System.BitConverter]::ToInt32($bytes, $s + 12)
        $raw = [System.BitConverter]::ToInt32($bytes, $s + 20)
        $size = [System.BitConverter]::ToInt32($bytes, $s + 16)
        if ($corRva -ge $va -and $corRva -lt ($va + $size)) {
            $corOffset = $raw + ($corRva - $va)
            break
        }
    }
    if ($corOffset -lt 0) { throw "cannot locate the CLR header in $path" }
    $flags = [System.BitConverter]::ToUInt32($bytes, $corOffset + 16)
    return (($flags -band 1) -ne 0) -and (($flags -band 2) -eq 0)
}

if (-not (Test-AnyCpuAssembly $dll)) {
    throw "Managed plugin is not AnyCPU (32BITREQUIRED is set); it will not load in 64-bit SMath."
}

# ---- 5. stage and package ---------------------------------------------------
$stage = Join-Path $root "build\package"
if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
New-Item -ItemType Directory -Force -Path $stage | Out-Null

Copy-Item -Force (Join-Path $root "packaging\OptimWrapper.dll.ini")    $stage
Copy-Item -Force (Join-Path $root "packaging\OptimWrapper.dll.config") $stage
Copy-Item -Force $dll (Join-Path $stage "OptimWrapper.dll")

Copy-Item -Recurse -Force (Join-Path $root "packaging\metadata") $stage

Copy-Item -Force (Join-Path $root "packaging\LICENSE")          $stage
Copy-Item -Force (Join-Path $root "packaging\NOTICE")           $stage
Copy-Item -Force (Join-Path $root "packaging\LICENSE-Eigen.txt") $stage

# The engines sit next to the wrapper at the package root, under names that
# tell them apart by bitness; the plugin loads the matching one by path.
Copy-Item -Force $engineX86 (Join-Path $stage "Optim_x86.dll")
Copy-Item -Force $engineX64 (Join-Path $stage "Optim_x64.dll")
if (Test-Path $engineSo) { Copy-Item -Force $engineSo (Join-Path $stage "libOptim.so") }

# version comes from the manifest, so there is one place to bump it
$ini = Get-Content (Join-Path $stage "OptimWrapper.dll.ini") -Raw
$version = ([regex]'"Version"\s*:\s*"([^"]+)"').Match($ini).Groups[1].Value
if ($version -eq "") { throw "could not read Version from OptimWrapper.dll.ini" }

$dist = Join-Path $root "dist"
New-Item -ItemType Directory -Force -Path $dist | Out-Null
$zip = Join-Path $dist ("OptimWrapper." + ($version -replace '\.', '_') + ".zip")

& $Python (Join-Path $root "tools\pack.py") $stage $zip
if ($LASTEXITCODE -ne 0) { throw "packaging failed" }

Write-Host "`nDone: $zip"
