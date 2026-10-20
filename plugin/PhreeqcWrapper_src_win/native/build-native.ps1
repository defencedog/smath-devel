<#
.SYNOPSIS
    Rebuilds native\IPhreeqc_x86.dll or native\IPhreeqc_x64.dll from the VIPhreeqc C++ sources.

.DESCRIPTION
    Configures and builds the upstream CMake project for a 32-bit or 64-bit
    Windows DLL. The MinGW-w64 toolchain decides the bitness:

      * an i686 triple (C:\mingw32)            -> native\IPhreeqc_x86.dll
      * an x86_64 triple (_toolchains\w64devkit) -> native\IPhreeqc_x64.dll

    Both engines ship at the package root under those names - SMath's installer
    rejects engine subfolders whose files share a basename, and the plugin binds
    one DllImport per name - and the plugin loads the one matching the host
    process bitness (SMath on 64-bit Windows is a 64-bit process, so the x64
    engine is the one production actually loads there).

    No source patches are needed: the tree compiles cleanly with GCC as long
    as IPHREEQC_ENABLE_MODULE is left ON. With it OFF, CMake defines
    IPHREEQC_NO_FORTRAN_MODULE, which selects a signature in IPhreeqc.cpp that
    passes a char* to a const char* parameter - an error under GCC that MSVC
    merely warns about. Leaving the module ON selects the correct overload.

.PARAMETER PhreeqcSource
    Path to the VIPhreeqc checkout (the folder containing CMakeLists.txt).

.PARAMETER Toolchain
    Root of a MinGW-w64 toolchain - the folder containing bin\gcc.exe.
    Use C:\mingw32 for the i686 engine and ..\_toolchains\w64devkit for the
    x86-64 engine.

.PARAMETER OutputDir
    Where to copy the finished DLL. Defaults to the native\ folder.

.EXAMPLE
    .\build-native.ps1 -PhreeqcSource ..\VIPhreeqc-master -Toolchain C:\mingw32
    .\build-native.ps1 -PhreeqcSource ..\VIPhreeqc-master -Toolchain ..\_toolchains\w64devkit
#>
param(
    [Parameter(Mandatory = $true)][string]$PhreeqcSource,
    [string]$Toolchain = "C:\mingw32",
    [string]$OutputDir = ""
)

$ErrorActionPreference = "Stop"

# Resolved here rather than as a parameter default: with several parameters the
# default expression does not see $PSScriptRoot and silently binds "".
if ([string]::IsNullOrEmpty($OutputDir)) { $OutputDir = $PSScriptRoot }
if ([string]::IsNullOrEmpty($OutputDir)) { throw "cannot determine the output folder; pass -OutputDir" }

# Absolute paths: CMake resolves relative compiler paths against the *build*
# directory, not the caller's working directory, which breaks Ninja's compiler
# check inside project().
$Toolchain = (Resolve-Path $Toolchain).Path

$gcc   = Join-Path $Toolchain "bin\gcc.exe"
$gxx   = Join-Path $Toolchain "bin\g++.exe"
$cmake = Join-Path $Toolchain "bin\cmake.exe"
$ninja = Join-Path $Toolchain "bin\ninja.exe"

foreach ($tool in @($gcc, $gxx, $cmake, $ninja)) {
    if (-not (Test-Path $tool)) { throw "toolchain is incomplete, missing: $tool" }
}

$triple = & $gcc -dumpmachine
if ($triple -match "i[3-6]86") { $arch = "x86" }
elseif ($triple -match "x86_64") { $arch = "x64" }
else { throw "toolchain targets '$triple' - need an i686 or x86_64 MinGW-w64 toolchain" }
$name = "IPhreeqc_$arch.dll"
Write-Host "Toolchain: $triple -> $name"

$source = (Resolve-Path $PhreeqcSource).Path
if (-not (Test-Path (Join-Path $source "CMakeLists.txt"))) {
    throw "no CMakeLists.txt in $source"
}

$build = Join-Path $PSScriptRoot ("_build_" + $arch)
if (Test-Path $build) { Remove-Item -Recurse -Force $build }
New-Item -ItemType Directory -Force -Path $build | Out-Null

# -DBUILD_TESTING=OFF : the upstream project fetches GoogleTest over the network
#                       when tests are enabled, which fails on an offline build.
# -static             : link the C++ runtime and winpthread into the DLL, so it
#                       has no MinGW runtime dependencies to resolve at load time.
Write-Host "`nConfiguring"
& $cmake -G Ninja `
    -S $source `
    -B $build `
    -DCMAKE_BUILD_TYPE=Release `
    -DCMAKE_C_COMPILER="$gcc" `
    -DCMAKE_CXX_COMPILER="$gxx" `
    -DCMAKE_MAKE_PROGRAM="$ninja" `
    -DBUILD_SHARED_LIBS=ON `
    -DBUILD_TESTING=OFF `
    -DIPHREEQC_ENABLE_MODULE=ON `
    -DIPHREEQC_FORTRAN_TESTING=OFF `
    -DCMAKE_SHARED_LINKER_FLAGS="-static -static-libgcc -static-libstdc++"
if ($LASTEXITCODE -ne 0) { throw "cmake configure failed" }

Write-Host "`nBuilding"
& $cmake --build $build
if ($LASTEXITCODE -ne 0) { throw "native build failed" }

$built = Join-Path $build "libIPhreeqc.dll"
if (-not (Test-Path $built)) { throw "expected $built" }

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
Copy-Item -Force $built (Join-Path $OutputDir $name)
Write-Host "`nCopied to $(Join-Path $OutputDir $name)"
