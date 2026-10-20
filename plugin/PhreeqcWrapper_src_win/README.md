# PhreeqcWrapper for SMath Studio

A native SMath Studio plugin exposing **USGS PHREEQC** aqueous geochemistry —
speciation, saturation indices, ion activities, elemental totals — as 12
worksheet functions.

The chemistry runs in a native C++ engine (`VIPhreeqc`, the Vitens fork of
IPhreeqc that adds direct accessors). This plugin is a thin managed shell around
its C ABI, so results are exactly PHREEQC's, not a reimplementation.

| | |
|---|---|
| **Assembly** | `PhreeqcWrapper.dll` |
| **Plugin GUID** | `8ead8647-c30a-4f13-8cc7-5f8ac4f3c066` |
| **Version** | 1.1.0.0 |
| **Target framework** | .NET Framework 2.0 (CLR 2.0) |
| **Native engines** | `IPhreeqc_x86.dll` + `IPhreeqc_x64.dll` (Windows, both at the package root) / `libIPhreeqc.so` (Linux, shipped aarch64) |
| **Prebuilt artifact** | `dist/PhreeqcWrapper.1_1_0_0.zip` (28 entries, 20.2 MB) |
| **Bundled databases** | 21 `.dat` files, 4.1 MB |

---

## 1. Install the prebuilt plugin

1. Copy `dist/PhreeqcWrapper.1_1_0_0.zip` anywhere convenient.
2. In SMath Studio: **Tools → Plugins → Install…**, select the zip, restart.
3. Or unzip it by hand into

   ```
   %APPDATA%\SMath\extensions\plugins\8ead8647-c30a-4f13-8cc7-5f8ac4f3c066\1.1.0.0\
   ```

   so the folder contains `PhreeqcWrapper.dll`, `IPhreeqc_x86.dll`,
   `IPhreeqc_x64.dll`, `PhreeqcWrapper.dll.ini`, `databases\` and `metadata\`.
   The wrapper loads the engine matching the host process bitness; on Linux the
   same folder holds `libIPhreeqc.so` and the wrapper picks by platform.

The 12 functions then appear in the function list under **Phreeqc Wrapper**.

> The zip must contain **stored** (uncompressed) entries. A deflated zip fails to
> install with *"Stream length must be non-negative and less than 2^31 - 1 -
> origin"*. `tools/pack.py` enforces this.

---

## 2. Repository layout

```
PhreeqcWrapper_src_win/
├── src/
│   ├── PhreeqcWrapper.cs            managed plugin: P/Invoke, script handling, 12 functions
│   └── PhreeqcWrapper.csproj        legacy (non-SDK) project file
├── native/
│   ├── IPhreeqc_x86.dll             prebuilt 32-bit Windows engine (i686, GCC 12)
│   ├── IPhreeqc_x64.dll             x86-64 Windows engine, built from VIPhreeqc (GCC 16.2)
│   ├── libIPhreeqc.so               prebuilt Linux engine (aarch64 — swap for x86_64)
│   ├── build-native.ps1             rebuild either Windows engine from the VIPhreeqc sources
│   └── _build_x86/, _build_x64/     CMake scratch dirs (git-ignored)
├── tests/
│   ├── Program.cs                   console harness, 29 checks
│   ├── App.config                   lets the net20 exe run on a v4 CLR
│   └── PhreeqcHarness.csproj        builds x86 (x64 via -p:PlatformTarget=x64), stages both engines + databases
├── tools/
│   └── pack.py                      STORED-only zip writer
├── packaging/
│   ├── PhreeqcWrapper.dll.ini       plugin manifest (GUID, version, title)
│   ├── PhreeqcWrapper.dll.config    Mono dllmaps (both engine names → libIPhreeqc.so)
│   ├── metadata/                    localised descriptions
│   └── databases/                   the 21 thermodynamic databases
├── patches/                         upstream diffs, if any (see patches/README.md)
├── dist/                            installable zip artifacts
├── reference/                       the original Linux/Mono build, kept for comparison
├── build.ps1                        one-shot: build + stage + package
└── .gitignore
```

`packages/` is created by `build.ps1` and is **not** committed.

---

## 3. The native library — what you must supply

This is the only part that is not plain C#. The wrapper contains **no**
chemistry; it calls into a native engine.

### Platform and bitness selection is automatic

`NativeLibraryLoader` picks the file name from the platform **and the process
bitness**, and the plugin looks for it **in its own folder**:

| Platform | File it opens | How |
|---|---|---|
| Windows x64 | `IPhreeqc_x64.dll` | `LoadLibraryA(<plugin>\IPhreeqc_x64.dll)` |
| Windows x86 | `IPhreeqc_x86.dll` | `LoadLibraryA(<plugin>\IPhreeqc_x86.dll)` |
| Linux / Mono | `libIPhreeqc.so` | `dlopen(<plugin>/libIPhreeqc.so)`, then Mono's `dllmap` |

The two Windows engines sit side by side at the package root under names that
tell them apart by bitness: SMath's Extensions installer rejects engine
subfolders whose files collide by basename (that is what *"plugin installation
failed"* means), and the CLR binds a `DllImport` by the module base name, so
the files cannot share `IPhreeqc.dll`. Every entry point is therefore declared
once per engine name (`DllImport("IPhreeqc_x86")`, `DllImport("IPhreeqc_x64")`)
behind a one-line dispatcher that picks by `IntPtr.Size`. On Mono both names
are `<dllmap>`'d to `libIPhreeqc.so`.

If the engine is missing, the plugin throws a message naming the exact path it
tried — it will not silently fall back to a system-wide copy.

### Where the binaries come from

All three engines are prebuilt and committed, so you need **no C++ toolchain**
to use this plugin:

| File | Built for | Notes |
|---|---|---|
| `native/IPhreeqc_x86.dll` | Windows i686, statically linked (GCC 12) | depends on `KERNEL32.dll` + the OS-provided UCRT |
| `native/IPhreeqc_x64.dll` | Windows x86-64, statically linked (GCC 16.2, w64devkit) | depends on `KERNEL32.dll` + `msvcrt.dll` only |
| `native/libIPhreeqc.so` | Linux **aarch64** | ELF64, dynamically linked against glibc |

`build.ps1` packages all three into the one zip — each platform only ever opens
its own file.

> **x86_64 Linux users:** the committed `.so` is an **aarch64** build. It works
> as-is on ARM Linux (Raspberry Pi, Graviton, Apple-silicon VMs), but on an
> x86_64 machine you must replace `native/libIPhreeqc.so` with an x86_64 build
> before packaging. Building it is the same CMake invocation as the DLL, minus
> the MinGW flags:
>
> ```bash
> cmake -S VIPhreeqc -B build -DCMAKE_BUILD_TYPE=Release \
>       -DBUILD_SHARED_LIBS=ON -DBUILD_TESTING=OFF -DIPHREEQC_ENABLE_MODULE=ON \
>       -DIPHREEQC_FORTRAN_TESTING=OFF
> cmake --build build -j
> cp build/libIPhreeqc.so native/libIPhreeqc.so
> ```
>
> Check what you have before shipping:
>
> ```bash
> readelf -h libIPhreeqc.so | grep Machine    # aarch64 → AArch64, x86_64 → Advanced Micro Devices X86-64
> ```

### ⚠️ Bitness must match the host process

The plugin ships an engine for either host bitness, so a stock install cannot
mismatch. This matters when **rebuilding**: the i686 toolchain produces
`IPhreeqc_x86.dll` and the x86-64 toolchain `IPhreeqc_x64.dll`, and the plugin
loads whichever matches the SMath process (on 64-bit Windows that is the x64
one — the host exe is ILONLY/AnyCPU and runs 64-bit there). A wrong-bitness
engine fails with `BadImageFormatException` / Win32 error 193. On Linux the
`.so` must match whatever architecture your Mono is (x86_64 or aarch64).

Check any engine before shipping it:

```bat
objdump -f IPhreeqc_x64.dll | findstr /i "pei-x86-64"   :: 64-bit engine
objdump -f IPhreeqc_x86.dll | findstr /i "pei-i386"     :: 32-bit engine
```

---

## 4. Prerequisites

| Need | Why | Notes |
|---|---|---|
| .NET SDK 6.0 or newer | provides `dotnet build` / MSBuild | no Visual Studio required |
| `microsoft.netframework.referenceassemblies.net20` | the v2.0 reference assemblies | see §5 |
| `SMathStudio.Core.Ref` 1.73.9126 | `SMath.Manager`, `SMath.Math.Numeric`, `SMath.Math.Symbolic`, **`SMath.Controls`** | see §5 |
| `SMathStudio.Drawing.Ref` 1.4.0 | pulled in by Core.Ref's dependency graph | see §5 |
| Python 3 | runs `tools/pack.py` | any 3.x |
| **MinGW-w64 (i686 and x86-64) + CMake + Ninja** | **only** if you rebuild the native engines | see §7 |

`SMath.Controls` is required and non-obvious: these functions take **string**
arguments, so they are evaluated through `IPluginLowLevelEvaluationFast`, which
lives there rather than in `SMath.Manager`.

---

## 5. Get the reference assemblies

Everything the compiler needs lives under `packages/`, laid out as the NuGet
global-packages folder would be:

```
packages/
├── microsoft.netframework.referenceassemblies.net20.1.0.3/
│   └── build/.NETFramework/v2.0/{mscorlib,System,System.Data,System.Xml}.dll
├── smathstudio.core.ref.1.73.9126/
│   └── ref/net20/{SMath.Manager,SMath.Math.Numeric,SMath.Math.Symbolic,SMath.Controls}.dll
└── smathstudio.drawing.ref.1.4.0/
```

### Option A — with nuget.exe (needs internet)

```bat
nuget install Microsoft.NETFramework.ReferenceAssemblies.net20 -Version 1.0.3 -OutputDirectory packages
nuget install SMathStudio.Core.Ref -Version 1.73.9126 -OutputDirectory packages
nuget install SMathStudio.Drawing.Ref -Version 1.4.0 -OutputDirectory packages
```

Behind a proxy:

```bat
nuget config -Set http_proxy=http://192.168.1.15:8888
```

Or let `build.ps1` copy a pre-populated folder:

```powershell
.\build.ps1 -PackagesSource ..\nuget-extracted
```

### Option B — no internet

A `.nupkg` is a zip. Download the three packages elsewhere, rename each to
`.zip`, and extract so the folder name matches the package id + version.

---

## 6. Build

```powershell
.\build.ps1 -PackagesSource ..\nuget-extracted `
            -Dotnet  C:\path\to\dotnet.exe `
            -Python  C:\path\to\python.exe
```

All parameters are optional. `-Dotnet` defaults to `dotnet` on `PATH`,
`-Python` to `python`.

If your machine blocks unsigned scripts:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

### Doing it by hand

```bat
dotnet build src\PhreeqcWrapper.csproj -c Release
```

Output: `src\bin\Release\PhreeqcWrapper.dll`.

| Property | Default | Points at |
|---|---|---|
| `PackagesPath` | `<repo>\packages` | root of the package tree |
| `NetRefPath` | `…\net20.1.0.3\build\.NETFramework\v2.0` | the four BCL reference DLLs |
| `SMathRefPath` | `$(PackagesPath)` | the `smathstudio.*` package folders |
| `TargetFrameworkRootPath` | `…\net20.1.0.3\build\` | must end at `build\`; MSBuild appends `.NETFramework\v2.0` |

---

## 7. Rebuilding the native library

Only needed if you change the C++ side or want a different architecture. Each
run rebuilds one engine; `build.ps1 -BuildNative` runs both toolchains back to
back. Requires **MinGW-w64** (GCC 12 for i686 and w64devkit GCC 16 for x86-64
are the known-good pair), CMake ≥ 3.21 and Ninja — the
[WinLibs personal build](https://winlibs.com/) and w64devkit both bundle all
three in one zip.

```powershell
.\build.ps1 -BuildNative -PhreeqcSource ..\VIPhreeqc-master
```

or directly, one engine at a time:

```powershell
.\native\build-native.ps1 -PhreeqcSource ..\VIPhreeqc-master -Toolchain C:\mingw32
.\native\build-native.ps1 -PhreeqcSource ..\VIPhreeqc-master -Toolchain ..\_toolchains\w64devkit
```

The toolchain's triple decides the output name (`i686-w64-mingw32` →
`IPhreeqc_x86.dll`, `x86_64-w64-mingw32` → `IPhreeqc_x64.dll`).

Three flags in that script are load-bearing:

| Flag | Why |
|---|---|
| `-DBUILD_TESTING=OFF` | upstream `FetchContent`s GoogleTest over the network when tests are on; the configure step fails offline |
| `-DIPHREEQC_ENABLE_MODULE=ON` | **keep this on.** Turning it off defines `IPHREEQC_NO_FORTRAN_MODULE`, which selects an overload in `IPhreeqc.cpp` that passes `char*` where `const char*` is expected. MSVC only warns; GCC errors out. With the module on, the correct overload is used and **no source patch is needed**. |
| `-DCMAKE_SHARED_LINKER_FLAGS="-static …"` | links libstdc++, libgcc and winpthread into the DLL, so there are no MinGW runtime DLLs to resolve at load time |

The result is a self-contained DLL either way — the i686 build's only imports
are `KERNEL32.dll` and the OS-provided UCRT (`api-ms-win-crt-*`), the x86-64
build's only `KERNEL32.dll` and `msvcrt.dll`.

---

## 8. Test

```bat
dotnet build tests\PhreeqcHarness.csproj -c Release
tests\bin\Release\PhreeqcHarness.exe

:: the x64 engine, same checks:
dotnet build tests\PhreeqcHarness.csproj -c Release -p:PlatformTarget=x64 -t:Rebuild
tests\bin\Release\PhreeqcHarness.exe
```

The harness references the plugin **as a project**, so one command builds the
plugin first and then links it. Its post-build step copies both Windows engines
and `databases\` next to the executable, which is exactly the layout of an
installed plugin — so the test exercises the real loader path, not a special
case. `build.ps1` runs the x86 pass for you (skip with `-SkipTests`).

It covers 29 checks:

* platform detection and the resolved native file name,
* database resolution by bare name, and the error when one is missing,
* script sanitisation (SMath rational fractions) and string unescaping,
* a real Ca–Na–Cl–HCO₃ solution: pH, SC, density, ionic strength, temperature,
  water mass, SI(Calcite), Ca²⁺ molality, Ca²⁺ activity, total Ca, and the
  γ = a/m identity,
* an explicit `pitzer.dat` argument,
* multi-`SOLUTION` scripts (the last block wins),
* three repeated calls, proving each evaluation is isolated.

Expected last line: `=== ALL TESTS PASSED ===`.

The harness defaults to **x86** (`<PlatformTarget>x86</PlatformTarget>`), which
exercises `IPhreeqc_x86.dll`; `-p:PlatformTarget=x64 -t:Rebuild` switches it to
`IPhreeqc_x64.dll`. Always rebuild with `-t:Rebuild` when switching bitness —
the incremental build does not track `PlatformTarget`.

---

## 9. Package

`build.ps1` does this for you. Manually:

```bat
mkdir build\package\databases
copy packaging\PhreeqcWrapper.dll.ini    build\package\
copy packaging\PhreeqcWrapper.dll.config build\package\
copy src\bin\Release\PhreeqcWrapper.dll  build\package\
copy native\IPhreeqc_x86.dll             build\package\
copy native\IPhreeqc_x64.dll             build\package\
copy native\libIPhreeqc.so               build\package\
copy packaging\databases\*.dat           build\package\databases\
xcopy /E /I packaging\metadata           build\package\metadata
python tools\pack.py build\package dist\PhreeqcWrapper.1_1_0_0.zip
```

`tools/pack.py` writes every entry with `ZIP_STORED` and re-opens the archive to
assert `compress_type == 0` on each entry.

A packaged zip contains **28 entries** — the DLL, `.ini`, `.config`, the three
native engines, 2 metadata files and 21 databases — and is about 20.2 MB.

The version comes from `Version` in `packaging/PhreeqcWrapper.dll.ini`. Bump it
there **and** in the `AssemblyVersion` attribute.

---

## 10. How units work — and why this plugin does not tag them

The other plugins in this collection return unit-bearing quantities. **This one
does not, by design.**

PHREEQC takes a *text script* whose units are declared inside the script
(`units mmol/kgw`, `temp 25`, …), not by SMath's numeric layer. There is no
meaningful way to map an SMath unit onto a string argument, and the returned
numbers span dimensionless indices, °C, µS/cm, kg/L and mol/kgw depending on the
function. So each function returns a plain double, and the table below is the
contract for what that number means.

**Strip units before building a script.** If you interpolate `25*'°C` into a
script with `num2str()`, SMath embeds the unit symbol and PHREEQC's parser
rejects it:

```smath
sv(x) := x * (UnitsOf(x)^(-1))          // unit stripper

script := strrep(tmpl; "TEMP_VAL"; num2str(sv(T_solution)))
script := strrep(script; "CA_MOL"; num2str(sv(c_calcium)))
pH     := phreeqc_pH(script)
```

`num2str()` also renders division as an exact rational fraction
(`358293780948959/100000000000000`), which PHREEQC cannot parse. The plugin
rewrites those to decimals automatically before running the script, so that
particular case needs no manual care.

---

## 11. Function reference

**Reading the tables.** *In* lists the arguments in the order they must be
supplied. `database` is optional and defaults to `phreeqc.dat`; pass a bundled
name (`"pitzer.dat"`) or an absolute path. `-` means a plain dimensionless number.

Argument separator in SMath is `;`.

### 11.1 Solution state (7)

| Function | In | Out | Unit of the result |
|---|---|---|---|
| `phreeqc_pH` | `script` [, `database`] | pH | - |
| `phreeqc_sc` | `script` [, `database`] | specific conductance | µS/cm |
| `phreeqc_density` | `script` [, `database`] | solution density | kg/L (≡ g/cm³) |
| `phreeqc_mu` | `script` [, `database`] | ionic strength | mol/kgw |
| `phreeqc_pe` | `script` [, `database`] | redox potential pe | - |
| `phreeqc_temp` | `script` [, `database`] | equilibrium temperature | °C |
| `phreeqc_water_mass` | `script` [, `database`] | solvent water mass | kg |

### 11.2 Saturation diagnostics (2)

| Function | In | Out | Unit of the result |
|---|---|---|---|
| `phreeqc_si` | `phase`, `script` [, `database`] | SI = log₁₀(IAP/K) | - |
| `phreeqc_sr` | `phase`, `script` [, `database`] | SR = 10^SI = IAP/K | - |

`SI > 0` supersaturated (scaling), `SI = 0` equilibrium, `SI < 0` undersaturated
(corrosive). `phase` must match a phase name in the database, e.g. `"Calcite"`,
`"Gypsum"`, `"Halite"`, `"Barite"`, `"CO2(g)"`.

### 11.3 Speciation (3)

| Function | In | Out | Unit of the result |
|---|---|---|---|
| `phreeqc_molality` | `species`, `script` [, `database`] | molal concentration | mol/kgw |
| `phreeqc_activity` | `species`, `script` [, `database`] | thermodynamic activity | mol/kgw |
| `phreeqc_total` | `element`, `script` [, `database`] | total dissolved element | mol/kgw |

`species` uses PHREEQC naming: `"Ca+2"`, `"Na+"`, `"Cl-"`, `"SO4-2"`, `"CaSO4"`.
`element` uses master-element naming, including redox states: `"Ca"`, `"S(6)"`,
`"C(4)"`, `"Alkalinity"`.

**Total: 12 functions** (each accepted at two arities, 24 signatures).

### Note on ionic strength

`phreeqc_mu` returns **molality-based** ionic strength (mol/kgw of solvent
water), not molarity (mol/L). They coincide in dilute solutions and diverge
substantially in brines.

---

## 12. Bundled databases

All 21 are installed into `databases\` inside the plugin folder and can be named
directly as the last argument.

| Database | Use |
|---|---|
| `phreeqc.dat` | **default** — general purpose, the reference database |
| `pitzer.dat` | high-salinity brines (Pitzer specific-ion interaction) |
| `sit.dat` | SIT model, moderate-to-high ionic strength |
| `wateq4f.dat` | trace elements, classic water analysis |
| `llnl.dat` | nuclear / high-temperature, very large |
| `minteq.dat`, `minteq.v4.dat` | EPA MINTEQA2 compatibility |
| `frezchem.dat` | freezing / sub-zero |
| `iso.dat` | isotope fractionation |
| `core10.dat` | compact core database |
| `Amm.dat` | ammonia/ammonium chemistry |
| `Tipping_Hurley.dat` | humic substances |
| `ColdChem.dat` | cold-region chemistry |
| `Concrete_PHR.dat`, `Concrete_PZ.dat` | cement / concrete porewater |
| `Kinec.v2.dat`, `Kinec_v3.dat` | kinetics |
| `phreeqc_rates.dat` | rate expressions |
| `stimela.dat` | geothermal |
| `PHREEQC_ThermoddemV1.10_15Dec2020.dat` | Thermoddem v1.10 |
| `minimum.dat` | minimal test database |

Resolution order for a bare name: current working directory → `<plugin>\databases\`
→ `<plugin>\`. An absolute path is always honoured.

---

## 13. Worked examples

```text
script := "SOLUTION 1
    temp      25
    pH        7.0
    units     mmol/kgw
    Ca        1.0
    C         1.0 as HCO3
    Na        1.0
    Cl        1.0
END"

pH   := phreeqc_pH(script)              = 7.000
SC   := phreeqc_sc(script)              = 267.54     µS/cm
rho  := phreeqc_density(script)         = 0.99717    kg/L
mu   := phreeqc_mu(script)              = 0.003407   mol/kgw

si   := phreeqc_si("Calcite"; script)   = -1.0702    (undersaturated)
sr   := phreeqc_sr("Calcite"; script)   = 0.0851

mCa  := phreeqc_molality("Ca+2"; script) = 9.9706e-4 mol/kgw
aCa  := phreeqc_activity("Ca+2"; script) = 7.7735e-4 mol/kgw
gCa  := aCa / mCa                        = 0.7796     (γ, dimensionless)

tCa  := phreeqc_total("Ca"; script)      = 1.0000e-3 mol/kgw
```

Scaling tendency as a function of temperature, using a parametric sweep:

```text
T := 25; 30; … ; 90

siCalcite(T) := phreeqc_si("Calcite";
    strrep(strrep(tmpl; "TEMP_VAL"; num2str(sv(T))); "PH_VAL"; "7.0"))
```

Choosing a different database is the last argument:

```text
pH_brine := phreeqc_pH(script; "pitzer.dat")
pH_abs   := phreeqc_pH(script; "C:\models\my_custom.dat")
```

---

## 14. Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| `DllNotFoundException: Native engine not found: …\IPhreeqc_x64.dll` (or `_x86`) | engine missing from the plugin folder | reinstall the zip — it ships `IPhreeqc_x86.dll` and `IPhreeqc_x64.dll` at the root |
| `DllNotFoundException` naming `libIPhreeqc.so` on Linux | `.so` absent, or wrong architecture for this machine | put a matching `libIPhreeqc.so` in the plugin folder (`readelf -h` to check) |
| `wrong ELF class` / `invalid ELF header` in a Mono trace | `.so` architecture ≠ host (e.g. aarch64 build on x86_64) | rebuild the `.so` for the host, see §3 |
| `BadImageFormatException` / Win32 error 193 | engine bitness ≠ host process (typically a mixed install over the old single-engine package) | reinstall the zip; it ships both bitnesses and picks the right one |
| `PHREEQC database not found: 'x.dat'` | name not bundled and no such file | use a bundled name or an absolute path; the message lists every path tried |
| `Stream length must be non-negative…` on install | zip entries are DEFLATE | repack with `tools/pack.py` |
| `MSB3644: reference assemblies … not found` | `TargetFrameworkRootPath` wrong | must end at `…\net20.1.0.3\build\` |
| `CS0246: IPluginLowLevelEvaluationFast` | `SMath.Controls` not referenced | it is required; add the reference (see §4) |
| Functions missing from the list | `.ini` `Guid` ≠ assembly GUID | both must be `8ead8647-c30a-4f13-8cc7-5f8ac4f3c066` |
| Script rejected by PHREEQC | a unit symbol leaked into the text | wrap interpolated values in `sv()` |
| Harness exits `0x80131700` | no CLR 2 runtime | keep `App.config` with `supportedRuntime v4.0` |
| Harness cannot load the engine | stale exe after swapping bitness | rebuild with `-t:Rebuild` (x86 default; `-p:PlatformTarget=x64` for the x64 engine) |

---

## 15. Provenance and licence

* Engine: USGS PHREEQC / IPhreeqc, via the [VIPhreeqc](https://github.com/Vitens/VIPhreeqc)
  fork (v3.8.7), which adds the direct accessors (`GetPH`, `GetSI`, …) this
  plugin relies on. Upstream is public domain / USGS. The i686 engine is a
  pinned prebuilt; the x86-64 engine is built in-tree from the pinned source
  (zero patches — see §7) with w64devkit.
* Thermodynamic databases: as distributed with PHREEQC.
* Managed wrapper, platform loader, tests and packaging: original work in this
  repository.
