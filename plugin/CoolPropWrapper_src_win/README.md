# CoolProp Wrapper for SMath Studio

A native SMath Studio plugin exposing the [CoolProp](http://www.coolprop.org)
thermodynamic and transport-property library as 11 worksheet functions: pure
fluids, pseudo-pure fluids and mixtures through `CoolProp_Props`, humid air
through `CoolProp_HAProps`, fluid constants through `CoolProp_Props1`, plus the
phase identifier, the saturation ancillary curves, the parameter-metadata
queries and the reference-state setters.

| | |
|---|---|
| **Assembly** | `CoolPropWrapper.dll` |
| **Plugin GUID** | `ca92ef03-c7da-4888-98ad-528482733e2f` |
| **Version** | 8.0.0.5 |
| **Target framework** | .NET Framework 2.0 (CLR 2.0) |
| **Prebuilt artifact** | `dist/CoolPropWrapper.8_0_0_5.zip` |
| **Units** | SI in, SI out (K, Pa, J/kg, kg/m³, …) |
| **Native engines** | `CoolProp_x86.dll` (i686, stdcall) + `CoolProp_x64.dll` (x86-64) + `libCoolProp.so` (aarch64) — all at the package root |

Targeting .NET 2.0 is deliberate: SMath Studio runs plugins under Mono on Linux,
and Mono's plugin host there is a CLR 2.0 host. A 2.0 assembly loads on both
Windows and Linux; a 4.x assembly does not.

---

## 1. Install the prebuilt plugin

1. Copy `dist/CoolPropWrapper.8_0_0_5.zip` anywhere convenient.
2. In SMath Studio: **Tools → Plugins → Install…**, select the zip, restart.
3. Or unzip it by hand into

   ```
   %APPDATA%\SMath\extensions\plugins\ca92ef03-c7da-4888-98ad-528482733e2f\8.0.0.5\
   ```

   so that the folder contains `CoolPropWrapper.dll`, `CoolPropWrapper.dll.ini`,
   `metadata\`, **all three native engines** and the two `.config` files:

   ```
   CoolProp_x86.dll        9.1 MB   i686 engine   (32-bit Windows hosts)
   CoolProp_x64.dll       16.4 MB   x86-64 engine (64-bit Windows hosts)
   libCoolProp.so         12.6 MB   aarch64 engine (Linux)
   ```

The 11 functions then appear in the function list under **CoolProp Wrapper**.

> The zip must contain **stored** (uncompressed) entries. A deflated zip fails to
> install with *"Stream length must be non-negative and less than 2^31 - 1 -
> origin"*. `tools/pack.py` enforces this — do not repackage with a generic zip
> tool that compresses.

> The native engines must sit **at the plugin-folder root** — do not move them
> into `win32\`/`win64\` subfolders or rename one to `CoolProp.dll`. SMath's
> Extensions installer rejects a package whose engines live in subfolders
> (*"plugin installation failed"*; two entries with the same `CoolProp.dll`
> basename collide), which is why this package is flat. .NET Framework does not
> probe a plugin's own directory for native dependencies, so the plugin loads
> `CoolProp_x64.dll` or `CoolProp_x86.dll` by absolute path from
> `GetTermsHandled` (`CoolPropEngine.EnsureLoaded()`), and fails with a readable
> message if the engine is missing or cannot be loaded. Each engine has its own
> `DllImport` (`CoolProp_x86` / `CoolProp_x64`, dispatched by process bitness);
> under Mono both `dllmap`s in `CoolPropWrapper.dll.config` resolve to
> `libCoolProp.so`. Copy the wrapper alone and the plugin loads but every call
> fails.

> The GUID is the one shipped with the installed Linux CoolProp plugin on
> purpose: installing this zip **upgrades that plugin in place** rather than
> leaving two copies in the plugin list.

---

## 2. Repository layout

```
CoolPropWrapper_src_win/
├── src/
│   ├── CoolPropEngine.cs        all P/Invoke, in one place — ours
│   ├── CoolPropPlugin.cs        IPlugin + evaluation dispatch — ours
│   ├── Unit.cs                  base-unit tagging table — ours
│   ├── IFunction.cs             one-function-per-class contract — ours
│   ├── Functions/               the 11 function definitions — ours
│   │   ├── CoolProp_Props.cs            PropsSI, 6 args
│   │   ├── CoolProp_HAProps.cs          HAPropsSI, 7 args
│   │   ├── CoolProp_Props1.cs           Props1SI, 2 args
│   │   ├── CoolProp_Phase.cs            PhaseSI, 5 args
│   │   ├── CoolProp_saturation_ancillary.cs   saturation_ancillary, 5 args
│   │   ├── CoolProp_get_parameter_information_string.cs  2 args
│   │   ├── CoolProp_get_param_index.cs                    1 arg
│   │   ├── CoolProp_get_fluid_param_string.cs             2 args
│   │   ├── CoolProp_get_global_param_string.cs             1 arg
│   │   ├── CoolProp_set_reference_stateS.cs               2 args
│   │   └── CoolProp_set_reference_stateD.cs               5 args
│   ├── AssemblyInfo.cs           carries the plugin GUID — ours
│   └── CoolPropWrapper.csproj    legacy (non-SDK) project file
├── native/
│   ├── CoolProp_x86.dll          i686 engine — prebuilt, unmodified
│   ├── CoolProp_x64.dll          x86-64 engine — built from pinned source (§12)
│   └── libCoolProp.so            aarch64 engine — prebuilt, unmodified
├── tests/
│   ├── Program.cs                console harness, 215 checks
│   ├── App.config                lets the net20 exe run on a v4 CLR
│   └── CoolPropHHarness.csproj   x86 by default; x64 with -p:PlatformTarget=x64
├── tools/
│   └── pack.py                   STORED-only zip writer
├── packaging/
│   ├── CoolPropWrapper.dll.ini   plugin manifest (GUID, version, title)
│   ├── CoolPropWrapper.config   plugin settings
│   ├── CoolPropWrapper.dll.config  Mono dllmap for the Linux engine
│   └── metadata/                 localised descriptions (en, ru)
├── patches/
│   └── README.md                 what was changed vs. the Mono build — read this
├── dist/                         installable zip artifacts
├── reference/                    the original Linux/Mono build, kept for comparison
├── build.ps1                     one-shot: build + test + package
└── .gitignore
```

`packages/` is created by `build.ps1` and is **not** committed. `native/` and
`dist/` **are** committed — the engine is a pinned binary, and the zip is the
deliverable.

`reference/` holds the Mono/Linux CoolProp plugin this Windows port was written
against (`*.linux.*` files, plus the upstream cool-prop licence). It is a design
baseline, **not** an install candidate: it tags results with named derived units
and therefore hits Error 74 on Windows (§8). Use the zip in `dist/`.

---

## 3. Prerequisites

| Need | Why | Notes |
|---|---|---|
| .NET SDK 6.0 or newer | provides `dotnet build` / MSBuild | no Visual Studio required |
| `microsoft.netframework.referenceassemblies.net20` | the v2.0 reference assemblies | see §4 |
| `SMathStudio.Core.Ref` 1.73.9126 | `SMath.Manager`, `SMath.Math.Numeric`, `SMath.Math.Symbolic` | see §4 |
| `SMathStudio.Drawing.Ref` 1.4.0 | pulled in by Core.Ref's dependency graph | see §4 |
| Python 3 | runs `tools/pack.py` | any 3.x |
| `native\CoolProp_x86.dll`, `native\CoolProp_x64.dll`, `native\libCoolProp.so` | the engines themselves | already in the tree; see §12 |

The project file is a **legacy, non-SDK** `.csproj` on purpose. An SDK-style
project implies a NuGet restore, which needs working TLS to nuget.org; this one
references assemblies by path and never restores. That is what makes an offline
or proxied build possible.

---

## 4. Get the reference assemblies

Everything the compiler needs lives under `packages/`, laid out exactly as the
NuGet global-packages folder would be:

```
packages/
├── microsoft.netframework.referenceassemblies.net20.1.0.3/
│   └── build/.NETFramework/v2.0/{mscorlib,System,System.Data,System.Xml}.dll
├── smathstudio.core.ref.1.73.9126/
│   └── ref/net20/{SMath.Manager,SMath.Math.Numeric,SMath.Math.Symbolic}.dll
└── smathstudio.drawing.ref.1.4.0/
```

### Option A — with nuget.exe (needs internet)

```bat
nuget install Microsoft.NETFramework.ReferenceAssemblies.net20 -Version 1.0.3 -OutputDirectory packages
nuget install SMathStudio.Core.Ref -Version 1.73.9126 -OutputDirectory packages
nuget install SMathStudio.Drawing.Ref -Version 1.4.0 -OutputDirectory packages
```

If you are behind a proxy, tell NuGet about it once:

```bat
nuget config -Set http_proxy=http://192.168.1.15:8888
```

`build.ps1` copies a pre-populated folder for you:

```powershell
.\build.ps1 -PackagesSource ..\nuget-extracted
```

It copies `-PackagesSource` into `packages\` only when `packages\` does not yet
exist, then verifies the three package folders above are present.

### Option B — no internet

A `.nupkg` is a zip. Download the three packages elsewhere and rename each to
`.zip`, then extract so the folder name matches the package id + version:

```powershell
Expand-Archive microsoft.netframework.referenceassemblies.net20.1.0.3.zip -DestinationPath packages
```

---

## 5. Build

```powershell
.\build.ps1 -PackagesSource ..\nuget-extracted `
            -Dotnet  C:\path\to\dotnet.exe `
            -Python  C:\path\to\python.exe
```

All three parameters are optional. `-Dotnet` defaults to `dotnet` on `PATH`,
`-Python` to `python`. `-SkipTests` builds and packages without running the
harness.

The script does five things, in order:

1. stages `packages\` and verifies the three reference-assembly folders,
2. verifies all three native engines are present in `native\`,
3. `dotnet build src\CoolPropWrapper.csproj -c Release`,
4. builds **and runs** the test harness against the real engine,
5. stages the package (`CoolProp_x86.dll` + `CoolProp_x64.dll` + `libCoolProp.so` at the root) and writes
   `dist\CoolPropWrapper.8_0_0_5.zip`.

If your machine blocks unsigned scripts, run it as

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

### Doing it by hand

```bat
dotnet build src\CoolPropWrapper.csproj -c Release
```

Output: `src\bin\Release\CoolPropWrapper.dll`.

Any of the path properties can be overridden instead of creating `packages\`:

```bat
dotnet build src\CoolPropWrapper.csproj -c Release ^
  -p:NetRefPath=D:\refs\v2.0 ^
  -p:SMathRefPath=D:\smath-refs
```

| Property | Default | Points at |
|---|---|---|
| `PackagesPath` | `<repo>\packages` | root of the package tree |
| `NetRefPath` | `…\net20.1.0.3\build\.NETFramework\v2.0` | the four BCL reference DLLs |
| `SMathRefPath` | `$(PackagesPath)` | the `smathstudio.*` package folders |
| `TargetFrameworkRootPath` | `…\net20.1.0.3\build\` | must end at `build\`; MSBuild appends `.NETFramework\v2.0` |

> `TargetFrameworkRootPath` is the one that trips people up. Point it at the
> `v2.0` folder itself and you get `MSB3644: The reference assemblies for
> .NETFramework,Version=v2.0 were not found`.

### One per bitness: two declarations, one surface

The two Windows engines sit side by side at the package root, so they cannot
share a file name — and the CLR binds a `DllImport` by module base name. Like
the Linux plugin, `src/CoolPropEngine.cs` therefore declares each of the 11
entry points **twice**, once per engine name (`DllImport("CoolProp_x86")` and
`DllImport("CoolProp_x64")`), behind one-line dispatchers that pick the pair by
`IntPtr.Size`; the right engine is loaded by absolute path before the first
call. (The alternative — a single `CoolProp.dll` per bitness in `win32\`/
`win64\` subfolders — is what SMath's installer rejects; see §1. Do not go back
to it.)

Each declaration still uses a plain `EntryPoint` and
`CallingConvention.StdCall`, and that binds because of how the engines name
their exports: the **i686** engine is CoolProp's `COOLPROP_STDCALL_LIBRARY=ON`
build and exports only decorated names (`_PropsSI@32`, `_PhaseSI@36`, …); the
**x86-64** engine exports the same 71 names **plain** (the Win64 ABI has no name
decoration), as does the Linux engine (undecorated cdecl); and the CLR's stdcall
resolver probes the exact name, then `name@<n>`, then `_name@<n>`, where `<n>`
is derived from the **managed** signature. Mono ignores the calling convention
on ELF and matches by name; both engine names reach `libCoolProp.so` through the
two `dllmap` entries. So each declaration serves its engine — **provided the
managed signature mirrors the native one exactly**, so the CLR's computed `<n>`
equals the i686 DLL's decoration. Four of the eleven signatures were wrong in
this port at first, and all four simply never resolved; every signature is now
transcribed from `include/CoolProp/CoolPropLib.h` and pinned by the harness.
(The x86 harness run is what exercises the decorated-name path; the x64 run
exercises the exact-name one.)

Two consequences worth knowing before you touch the file:

* The five functions that return `long` in C are declared **`int`** in C#. MSVC
  `long` is 32 bits while LP64 `long` is 64, and the upper half of the return
  register is undefined on x86. `CoolProp_get_param_index("P")` returning 20 in
  the harness is what catches a regression here.
* The signatures are load-bearing, not cosmetic. See §14.

---

## 6. Test

```bat
dotnet build tests\CoolPropHHarness.csproj -c Release
tests\bin\Release\CoolPropHHarness.exe

:: the x64 engine (what a 64-bit SMath host actually loads):
dotnet build tests\CoolPropHHarness.csproj -c Release -p:PlatformTarget=x64 -t:Rebuild
tests\bin\Release\CoolPropHHarness.exe
```

The harness references the plugin **as a project**, so this one command builds the
plugin first and then links it — a clean checkout needs no prior step, and the
tests can never run against a stale DLL. It calls the **real native engine** and
does **not** need SMath running. Its 215 checks cover:

* platform detection, and that `EnsureLoaded()` binds the engine next to the
  assembly,
* all 11 C-API entry points against reference values from the CoolProp
  documentation — the normal boiling point of water (373.124295847667 K),
  critical point, saturated-liquid density, saturated-vapour enthalpy, humid-air
  enthalpy, the saturation ancillary, `PhaseSI` returning `"twophase"`,
  `get_param_index`, and a reference-state round trip that must leave the
  earlier results bit-identical,
* the reference-state datums themselves — `"ASHRAE"` really puts h = s = 0 at
  −40 °C, `"RESET"` really is a no-op, and `"DEF"` really restores the default
  (§11),
* SMath's `\HHHH\` string transport (`UnescapeMathString`), including the
  documented adjacent-escape limitation,
* the unit table — every parameter's base-unit expression, both spellings,
  every dimensionless and unknown name returning **no** expression, and a scan
  of all table entries that fails if any token other than `kg`, `m`, `s`, `mol`,
  `K` appears,
* reflection registration: all 11 function classes discovered, no duplicate
  names, every arity unique per class.

Expected last line: `=== ALL TESTS PASSED ===` (215 pass, 0 fail, 0 skip).

Two things to know:

* **The harness bitness picks the engine.** `tests/CoolPropHHarness.csproj`
  stages both engines (`CoolProp_x86.dll`, `CoolProp_x64.dll`) next to the
  harness and defaults to `<PlatformTarget>x86</PlatformTarget>` (i686 engine);
  building with `-p:PlatformTarget=x64` (and `-t:Rebuild`, because MSBuild does
  not track `PlatformTarget` as a compiler input) exercises the x64 engine. The
  real SMath host is AnyCPU, i.e. a **64-bit process** on 64-bit Windows, so
  the x64 run is the one that mirrors production; the x86 run keeps the i686
  engine honest.
* **`App.config` is required.** A net20 executable has no CLR 2 runtime on a
  modern Windows box and exits with `0x80131700` unless it declares
  `<supportedRuntime version="v4.0" sku=".NETFramework,Version=v4.0" />`. It is
  committed for that reason — do not delete it.

---

## 7. Package

`build.ps1` does this for you. Manually:

```bat
mkdir build\package
xcopy /E /I packaging\* build\package\
copy src\bin\Release\CoolPropWrapper.dll build\package\
copy native\CoolProp_x86.dll build\package\
copy native\CoolProp_x64.dll build\package\
copy native\libCoolProp.so build\package\
python tools\pack.py build\package dist\CoolPropWrapper.8_0_0_5.zip
```

`tools/pack.py` writes every entry with `ZIP_STORED` and then re-opens the archive
to assert `compress_type == 0` on each entry, so a bad artifact cannot be
produced silently.

The version in the filename comes from `Version` in
`packaging/CoolPropWrapper.dll.ini`. Bump it there **and** in
`src/AssemblyInfo.cs`; the `.ini` is what SMath reads, the assembly attribute is
what the rest of the world sees.

---

## 8. How the units work

SMath's numeric layer keeps a value and a unit syntax tree per number:

* `BaseEntry.Value` is **always in SI base units**,
* `BaseEntry.Units` is the unit AST (`1` = dimensionless),
* `UnitInfo.Factor` does the conversion when a unit token is parsed.

So `3*'MPa` reaches a plugin as `3e6`, and `25*'°C` as `298.15`. The plugin does
not see "MPa"; it sees pascals. This plugin therefore takes and returns SI base
units and nothing else — which is exactly what makes results composable with the
rest of a worksheet.

### No conversion at the boundary — only tagging

Unlike IF97 (whose library works in MPa / kJ/kg) or PsychroLib (whose library
works in °C), **CoolProp's C API is already SI**: `PropsSI` takes and returns
kelvin, pascals, kilograms per cubic metre and joules per kilogram. There is no
scaling step in this plugin — no `ToLibraryUnits`, no `OutputScale`, nothing to
get out by a factor of 1e3 or 1e6.

What the plugin *does* add is the unit AST on the way out, so that
`CoolProp_Props("H"; …)` comes back tagged `'m^2/'s^2` and SMath displays
`2675.47 kJ/kg`. That mapping lives in `src/Unit.cs`.

### Why the results carry base-unit expressions, never `J` or `Pa`

When SMath combines two numbers it compares their unit ASTs for equality
(`BaseEntry.ComUnits`). A result tagged `'J/'kg` is *not* the same AST as a
native `'m^2/'s^2`, so the first subtraction raises **Error 74**. Every result
this plugin emits is therefore tagged with a base-unit product:

| Quantity | Tagged as |
|---|---|
| Temperature (T, Tcrit, Ttriple, wet/dry bulb, dew point) | `'K` |
| Pressure (P, Pcrit, Ptriple, partial pressure, saturation) | `'kg/('m*'s^2)` |
| Specific energy (H, hmass, latent heat, humid-air enthalpy) | `'m^2/'s^2` |
| Molar energy (Hmolar, Gmolar, Amolar) | `'kg*'m^2/('mol*'s^2)` |
| Specific entropy, cp, cv, mass gas constant | `'m^2/('s^2*'K)` |
| Molar entropy, molar gas constant, molar cp | `'kg*'m^2/('mol*'s^2*'K)` |
| Density (D, rhocrit, reducing density) | `'kg/'m^3` |
| Molar density (Dmolar) | `'mol/'m^3` |
| Specific volume (Vda, 1/D) | `'m^3/'kg` |
| Molar mass (M, molemass) | `'kg/'mol` |
| Speed of sound (A, isentropic sonic velocity) | `'m/'s` |
| Thermal conductivity (L, k) | `'kg*'m/('s^3*'K)` |
| Surface tension (I, sigma) | `'kg/'s^2` |
| Viscosity (V, mu, dynamic) | `'kg/('m*'s)` |
| Isobaric expansion coefficient, thermal diffusivity inverse | `1/'K` |
| Isothermal compressibility, 1/P | `'m*'s^2/'kg` |
| Quality, compressibility factor, Prandtl, phase index, humidity ratio, RH, parameter indices | *(no tag — `Units` stays `1`)* |

SMath contracts these for display on its own: a result of `2675470 'm^2/'s^2`
shows as `2675.47 kJ/kg` if you ask for those units. **Do not pre-scale outputs**
in the plugin — that would double-convert.

**A name that is not in the table gets no tag at all.** Not tagging is always
safe; a wrong tag is not. That is why CoolProp's `dipole_moment` (coulomb-metre,
not expressible in the ten base tags SMath's parser handles here) and the
humid-air virial coefficients come back unit-less.

### `"V"` is viscosity, not specific volume

CoolProp accepts `"V"` as a parameter name and it means **kinematic-adjacent
viscosity** (`iviscosity` — a backward-compatibility alias; there is no
specific-volume parameter at all). Specific volume is `1/D`. The Mono build
mis-filed `"V"` as m³/kg; the table in `src/Unit.cs` is transcribed from
CoolProp's own `parameter_info_list` in `src/DataStructures.cpp`, and the
harness pins `("V", false) == "'kg/('m*'s)"` so it cannot drift back.

---

## 9. Function reference

**Reading the tables.** Arguments are listed in the order they must be supplied.
`string` arguments are CoolProp parameter/fluid *names* and are written as text
operands in the worksheet (`"P"`, `"Water"`); `number` arguments are
unit-aware SI values. `-` means dimensionless (a plain number, `Units = 1`).
Where a result is tagged, the quantity column names it.

### 9.1 State-based properties

| Function | Arguments | Returns |
|---|---|---|
| `CoolProp_Props` | `string` Output, `string` Name1, `number` Prop1, `string` Name2, `number` Prop2, `string` FluidName | tagged property |
| `CoolProp_HAProps` | `string` Output, `string` Name1, `number` Prop1, `string` Name2, `number` Prop2, `string` Name3, `number` Prop3 | tagged humid-air property |
| `CoolProp_Props1` | `string` FluidName, `string` Output | tagged fluid constant |

`CoolProp_Props` is the workhorse: `("H"; "T"; 298.15; "P"; 101325; "Water")`.
The usual input names are `T`, `P`, `D`, `H`, `S`, `U`, `Q` (vapour quality),
`Smolar`, `Hmolar`; the usual outputs are those plus `C` (cp), `O` (cv), `A`
(sound speed), `V` (viscosity), `L` (conductivity), `I` (surface tension) and
`Prandtl`. `CoolProp_Props1` covers the state-independent constants —
`Tcrit`, `Pcrit`, `Dcrit`, `Tmin`, `Tmax`, `Ttriple`, `Ptriple`, `M`,
`acentric`, … — and takes `(FluidName, Output)` in that order.

`CoolProp_HAProps` fixes a humid-air state from three pairs. Its accepted
input names are `"T"` (dry-bulb), `"D"` (dew point), `"B"` (wet bulb), `"P"`
(pressure), `"W"` (humidity ratio), `"R"` (relative humidity), `"H"` (enthalpy),
`"S"` (entropy), `"V"` (volume), `"mu"` (viscosity) and `"k"` (conductivity); its
outputs add `"Tdb"`, `"Tdp"`, `"Twb"`, `"RH"`, `"W"` and `"Y"` (mass fraction of
dry air). The full parameter list is CoolProp's `HumidAirProp` table, and a
worksheet can query it through §9.3.

### 9.2 Phase and saturation

| Function | Arguments | Returns |
|---|---|---|
| `CoolProp_Phase` | `string` Name1, `number` Prop1, `string` Name2, `number` Prop2, `string` FluidName | `string` phase |
| `CoolProp_saturation_ancillary` | `string` FluidName, `string` output, `number` Q, `string` input, `number` value | tagged double |

`CoolProp_Phase` names the region: `"liquid"`, `"gas"`, `"twophase"`,
`"supercritical"`, `"supercritical_gas"`, `"supercritical_liquid"`,
`"critical_point"` or `"unknown"`. `CoolProp_saturation_ancillary` evaluates the
per-fluid ancillary curves directly, for pure and pseudo-pure fluids only (the
HelmholtzEOS backend). The engine implements four `(output, input)` pairs:

| output | input | Q |
|---|---|---|
| `"P"` | `"T"` | 0 = liquid, 1 = vapour |
| `"T"` | `"P"` | 0 or 1 (invert) |
| `"Dmolar"` | `"T"` | 0 or 1 |
| `"T"` | `"Dmolar"` | 0 or 1 (invert) |

plus `("…", "surface_tension", <any>, "T", value)`, where the `Q` argument is
ignored. So `CoolProp_saturation_ancillary("Water"; "P"; 0; "T"; 373.124285686)`
returns the saturation pressure at that temperature (101 325 Pa), and swapping
`"P"` for `"T"` inverts the curve. Any other combination raises an error
carrying the native message (`NotImplementedError` for mixtures).

### 9.3 Parameter metadata

| Function | Arguments | Returns |
|---|---|---|
| `CoolProp_get_parameter_information_string` | `string` Key, `string` Kind | `string` |
| `CoolProp_get_param_index` | `string` Name | `number` |
| `CoolProp_get_fluid_param_string` | `string` FluidName, `string` ParamName | `string` |
| `CoolProp_get_global_param_string` | `string` ParamName | `string` |

These read the engine's own tables, so a worksheet can branch on them:
`CoolProp_get_parameter_information_string("P"; "long")` returns `"Pressure"`,
`("P"; "units")` returns `"Pa"`, `("P"; "IO")` returns the I/O class;
`CoolProp_get_param_index("P")` returns `20`, `("T")` returns `19`;
`CoolProp_get_fluid_param_string("Water"; "formula")` returns `"H2O"`;
`CoolProp_get_global_param_string("version")` returns the engine version, and
`("errstring")` the message from the last failed call — that is the one to use
when a property call fails.

### 9.4 Reference state

| Function | Arguments | Returns |
|---|---|---|
| `CoolProp_set_reference_stateS` | `string` FluidName, `string` ReferenceState | `-` |
| `CoolProp_set_reference_stateD` | `string` FluidName, `number` T, `number` RhoMolar, `number` h0, `number` s0 | `-` |

`set_reference_stateS` selects a named reference state — `"IIR"`, `"ASHRAE"`,
`"NBP"`, `"DEF"` or `"RESET"`. `set_reference_stateD` sets one explicitly:
`T` in K, molar density in mol/m³, and h0 / s0 in J/(mol·K) / J/mol. **Both are
global side effects for the rest of the session** — every later call for that
fluid uses the new reference state, and h / s / g / a / f for it shift
accordingly. `("R134a"; "RESET")` (or `"DEF"`) puts things back.

**Total: 11 functions.**

---

## 10. Fluids, backends and validity

### Naming fluids

`FluidName` is any string CoolProp's `get_fluid_param_string` accepts:

| Form | Example | Meaning |
|---|---|---|
| pure fluid | `"Water"`, `"R134a"`, `"Nitrogen"` | described by its Helmholtz EOS |
| backend-prefixed | `"HEOS::Water"` | the same, with the backend chosen explicitly |
| pseudo-pure mixture | `"Air"`, `"R410A"`, `"R404A"` | treated as a single fluid |
| binary mixture | `"R32[0.5]&R125[0.5]"` | real mixture with the given mole fractions |
| predefined mixture | `"Air"`, `"R410A"` | as shipped in the fluids list |

`CoolProp_get_global_param_string("FluidsList")` returns the whole list, and
`CoolProp_get_fluid_param_string("Air"; "aliases")` reveals the spellings of a
predefined mixture — so a worksheet can discover names at run time instead of
hard-coding them (§9.3).

The engine here is CoolProp 8.0.0's default build: the **HEOS** backend for
pure fluids and predefined mixtures, plus the tabular backends. The `REFPROP::`
backend needs NIST's proprietary library and `.FLD` files, which are not part of
the engine, so it is not usable from this plugin — and a `REFPROP::`-prefixed
name will fail with the native error message rather than silently degrade.

### Escaping in text operands

SMath's parser escapes characters that are special to it as `\HHHH\`, and the
plugin decodes them before handing a string to CoolProp. The four that matter
for mixtures — `[`, `]`, `.`, `&` — all arrive correctly, so
`CoolProp_Props("H"; "T"; 250; "P"; 101325; "R32[0.5]&R125[0.5]")` works. The
one documented limitation is two *adjacent* escapes; see
`patches/README.md` §2.5 for the exact behaviour and why it is not "fixed".

### Validity ranges

CoolProp's validity is per fluid, not a global table like IF97's five regions.
Each fluid knows its own limits, and the state-independent constants expose
them:

```text
CoolProp_Props1("Water"; "Tmin")     = 273.16 K      (triple-point liquid)
CoolProp_Props1("Water"; "Tmax")     = 2000 K
CoolProp_Props1("Water"; "Pcrit")    = 22.064 MPa
CoolProp_Props1("Water"; "Ttriple")  = 273.16 K
```

Outside a fluid's range — or for a state the EOS cannot reach, such as an
inconsistent `(T, p)` pair — the C API reports failure by returning **`+Infinity`**
(`_HUGE`) and the message goes into the global `errstring`. The plugin turns
that into an error carrying the native message, so a worksheet shows
`CoolProp: ...` with what CoolProp said instead of a bare `NaN`:

```text
CoolProp_Props("T"; "P"; 101325; "Q"; 0; "NotAFluid")   → error, errstring set
CoolProp_get_global_param_string("errstring")          → "Unable to load fluid ..."
```

A worksheet that prefers a soft answer can test for the error, or query the
validity itself — `CoolProp_Phase` returning `"unknown"` is the other signal
that a state was not reachable.

---

## 11. Worked examples

```text
T := 298.15*'K
p := 101325*'Pa

CoolProp_Props("T"; "P"; p; "Q"; 0; "Water")   = 373.124 K     (tagged K)
CoolProp_Props("D"; "P"; p; "Q"; 0; "Water")   = 958.37 kg/m³
CoolProp_Props("H"; "P"; p; "Q"; 1; "Water")   = 2675.53 kJ/kg
CoolProp_Props("H"; "P"; p; "Q"; 0; "Water")   = 419.06 kJ/kg
CoolProp_Props1("Water"; "Tcrit")              = 647.096 K
CoolProp_Props1("Water"; "Pcrit")              = 22.064 MPa

CoolProp_Phase("P"; p; "Q"; 0; "Water")        = "twophase"
CoolProp_get_param_index("P")                  = 20
CoolProp_get_parameter_information_string("P"; "long")  = "Pressure"
```

Humid air, in the same SI convention (humidity ratio in kg/kg, enthalpy in
J/kg **dry air**):

```text
CoolProp_HAProps("H"; "T"; 298.15; "P"; 101325; "W"; 0.010)  = 50.612 kJ/kg
CoolProp_HAProps("W"; "T"; 298.15; "R"; 0.6; "P"; 101325)    = 0.011949
CoolProp_HAProps("Tdp"; "T"; 298.15; "R"; 0.6; "P"; 101325)  = 289.85 K
```

A mixture is just a fluid name:

```text
CoolProp_Props("H"; "T"; 250; "P"; 101325; "R32[0.5]&R125[0.5]")  = 384.08 kJ/kg
```

Because the results carry real units, these compose directly:

```text
CoolProp_Props("H"; "P"; 101325*'Pa; "Q"; 1; "Water")
- CoolProp_Props("H"; "P"; 101325*'Pa; "Q"; 0; "Water")   = 2256.47 kJ/kg  (h_fg)
```

And the reference state switches enthalpy datums without touching the numbers:

```text
CoolProp_set_reference_stateS("R134a"; "ASHRAE")   // h = 0, s = 0 at −40 °C sat. liq.
CoolProp_Props("H"; "T"; 233.15; "Q"; 0; "R134a")  = 0 kJ/kg
CoolProp_Props("S"; "T"; 233.15; "Q"; 0; "R134a")  = 0 kJ/(kg·K)
CoolProp_set_reference_stateS("R134a"; "DEF")      // back to the library default
CoolProp_Props("H"; "T"; 233.15; "Q"; 0; "R134a")  = 148.14 kJ/kg
```

> **Use `"DEF"`, not `"RESET"`, to restore the default.** In CoolProp 8.0.0 a
> named reference state is applied by *adding* an offset to the current one, and
> `"RESET"` adds `(0, 0)` — i.e. it leaves the active datum in place, verified by
> the harness. `"DEF"` disables the offset and restores the library default.
> Both strings are passed through unchanged, matching the upstream C API.

> A **worksheet** may write derived units freely — SMath reduces them to base
> units before the plugin sees them, so `101325*'Pa`, `101.325*'kPa` and
> `101325` are all the same input. The restriction on derived units applies only
> to the unit ASTs the **plugin** returns.

---

## 12. Regenerating from source

There are two halves to this plugin, and they are refreshed differently.

### The managed half — this repository

Everything under `src/`, `tests/`, `packaging/` and `tools/` is original source
that builds here:

```powershell
.\build.ps1 -PackagesSource ..\nuget-extracted     # -> dist\CoolPropWrapper.8_0_0_5.zip
```

### The native half — pinned binaries plus one built engine

`native/CoolProp_x86.dll` (i686) and `native/libCoolProp.so` (aarch64) are
prebuilt and **unmodified**. `native/CoolProp_x64.dll` (x86-64) is **built from
the pinned CoolProp-8.0.0 source** — no official 64-bit Windows DLL of the C
library is published anywhere (the PyPI wheel hides it inside a `.pyd`, the GUI
installer is statically linked), and the 64-bit Windows host needs it. The exact
recipe, hashes and toolchain are in `patches/README.md`; the short version:

```
w64devkit (GCC 16.2.0, x86_64-w64-mingw32) + CMake + Ninja
MSYSTEM=MINGW64          (satisfies CoolProp's MSYS2 guard)
-DCOOLPROP_SHARED_LIBRARY=ON  -DCOOLPROP_STATIC_LIBRARY=OFF
-DCOOLPROP_CATCH_MODULE=OFF   -DCOOLPROP_NO_EXAMPLES=ON
-DCMAKE_RC_COMPILER=<toolchain>/bin/x86_64-w64-mingw32-windres.exe
nine CPM dependencies supplied offline via -DCPM_<name>_SOURCE=<dir>
-> _win64build/Release/CoolProp.dll   (16.4 MB, imports only KERNEL32 + msvcrt)
```

Pinning the other two is a deliberate choice; `tests/` runs against the real
engine binaries, so a bad pin fails the test suite rather than reaching a user.

To refresh an engine:

1. Replace `native\CoolProp_x86.dll` or `native\CoolProp_x64.dll` (the i686 one
   must be the `COOLPROP_STDCALL_LIBRARY=ON` build), or the aarch64
   `libCoolProp.so`.
2. Re-run both harness bitnesses. If the entry-point set or behaviour changed,
   the 11 entry-point checks fail — that is the point.
3. `.\build.ps1` repackages.

### From nothing — the recipe that produced this plugin

1. Create a legacy `.csproj` with `<TargetFrameworkVersion>v2.0</TargetFrameworkVersion>`
   and direct `<Reference>` + `<HintPath>` entries (see §2). Do **not** use
   `<TargetFramework>net20</TargetFramework>` — that is the SDK-style form and it
   triggers a restore.
2. Set `<TargetFrameworkRootPath>` to the `build\` folder of the net20 reference
   assemblies package.
3. Write `AssemblyInfo.cs` with `[assembly: Guid("ca92ef03-…")]`. **This GUID,
   not `ProjectGuid`, is the install folder name.** Reusing an existing GUID is
   how you upgrade a plugin in place instead of installing a second copy — this
   plugin reuses the GUID of the shipped Linux build on purpose.
4. Put every P/Invoke in one file (`CoolPropEngine.cs`) and transcribe each
   signature from `include/CoolProp/CoolPropLib.h` — see §5 for why the managed
   arity is load-bearing. Declare the five `long`-returning functions as `int`.
5. Call `LoadLibraryA(<plugin dir>\CoolProp_x64.dll | CoolProp_x86.dll)` from
   `GetTermsHandled` (pick by `IntPtr.Size`; throw a readable error when the
   load fails — a silent return turns every later call into a bare
   `BadImageFormatException`) and ship a `CoolPropWrapper.dll.config` with a
   `<dllmap>` per engine name for Mono. Declare each entry point once per
   engine name (`CoolProp_x86` / `CoolProp_x64`) behind a bitness dispatcher —
   SMath's installer rejects engines in `win32\`/`win64\` subfolders.
6. Implement `IPluginLowLevelEvaluationFast.TryEvaluateExpression` and
   `IPluginHandleEvaluation.GetTermsHandled`, returning one `TermInfo` per
   function; register them by reflecting over the `IFunction` types (each carries
   `public static int[] Arguments`).
7. Read arguments with `args[i].obj.ToDouble()` — `TNumber.obj` is a public field
   of type `BaseEntry`; there is no `DoubleVal`. Read strings through
   `TDouble.Text` and decode SMath's `\HHHH\` escapes.
8. Tag every result from the base-unit table (`Unit.cs`); leave unknown
   parameters untagged. Use `ReferenceEquals` for every `MItem` null test —
   `MItem` overloads `==` and dereferences both operands.
9. Wrap every library call in `try/catch` and surface `errstring` on failure.
10. Package with `ZIP_STORED` entries only, shipping the native engine next to
    the wrapper.

---

## 13. Plugin identity and versioning

* `[assembly: Guid("ca92ef03-c7da-4888-98ad-528482733e2f")]` in
  `src/AssemblyInfo.cs` — the plugin's identity. Two zips with the same GUID are
  the same plugin; the higher version wins.
* `Version` in `packaging/CoolPropWrapper.dll.ini` — the version SMath reports and the
  folder name under `extensions\plugins\{GUID}\`.
* `[assembly: AssemblyVersion]` — bump alongside, for consistency.

Keep the GUID stable forever. Changing it orphans existing installations — and
note that this GUID is *shared with* the pre-existing Linux CoolProp plugin: that
is intentional, so this build supersedes it. `Version 8.0.0.5` tracks the
wrapped CoolProp release (8.0.0) plus the wrapper's own revision (`.5`).

---

## 14. Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| `Stream length must be non-negative…` on install | zip entries are DEFLATE | repack with `tools/pack.py` |
| *"plugin installation failed"* in SMath when installing the zip | engine files in `win32\`/`win64\` subfolders (two `CoolProp.dll` basenames collide) | keep the engines flat at the zip root under distinct names (`CoolProp_x86.dll`, `CoolProp_x64.dll`) — a hard constraint of SMath's installer |
| Plugin loads, every call fails | the engine file is missing from the plugin root | ship the engines in the zip; `EnsureLoaded()` now throws the full path when it cannot find or load one |
| `BadImageFormatException` / Win32 error 193 / 0x8007000B | engine bitness ≠ host process bitness (classic: i686 engine in the 64-bit SMath host) | ship both `CoolProp_x86.dll` and `CoolProp_x64.dll`; the loader picks by `IntPtr.Size` |
| `LoadLibraryA` error 126 | path passed was a directory, not the DLL | `Path.Combine(dir, "CoolProp_x64.dll")` |
| An entry point silently never resolves | managed signature ≠ native one, so the CLR's `@<n>` never matches | transcribe from `CoolPropLib.h`; see §5 |
| `get_param_index("P")` returns a huge garbage number | `long` return declared as C# `long` | declare the five `long`-returning functions `int` |
| `get_parameter_information_string` returns `""` with `rc == 1` | output buffer not seeded with the info kind | seed with `"IO"` / `"short"` / `"long"` / `"units"` |
| `saturation_ancillary` returns `+Infinity` | unsupported `(output, input)` pair, or a mixture | only P/T/Dmolar/T pairs — see §9.2 |
| Result is `+Infinity` and the cell shows `CoolProp: …` | the C API's failure report | read `CoolProp_get_global_param_string("errstring")` |
| **Error 74** on first arithmetic with a result | result tagged with a named derived unit (`'Pa`, `'J/'kg`) | tag base units (`'kg/('m*'s^2)`, `'m^2/'s^2`) |
| `"V"` gives a viscosity-sized answer | `"V"` **is** viscosity in CoolProp | use `1/D` for specific volume |
| A named reference state will not go away | `"RESET"` is a no-op in CoolProp 8.0.0 | use `"DEF"` — §11 |
| Mixture string rejected by the engine | `&` / `[` / `]` / `.` arrived mangled | the decoder handles `\HHHH\` escapes; check the raw text operand |
| `NullReferenceException` in `MItem ==` | `MItem` overloads `==` and dereferences both operands | use `ReferenceEquals(x, null)` |
| `CS0535: does not implement IDisposable.Dispose` | `SMath.Manager.IPlugin` derives from `IDisposable` | add a public `Dispose()` |
| `CS0618: 'Decision'/'GlobalParams' is obsolete` | the old API is marked obsolete in 1.73.9126 | use `SMath.Math.Computation` and `SessionProfile.CurrentLanguage` |
| `MSB3644: reference assemblies … not found` | `TargetFrameworkRootPath` wrong | must end at `…\net20.1.0.3\build\` |
| `NETSDK1004: assets file not found` | you converted to SDK-style | keep the legacy `.csproj` |
| Plugin loads, functions missing | `GetTermsHandled` returned nothing, or the `.ini` `Guid` ≠ assembly GUID | check both |
| `0x80131700` when starting the harness | no CLR 2 runtime | keep `App.config` with `supportedRuntime v4.0` |
| Array literal arrives as garbage numbers | Roslyn `InitializeArray` RVA-blob corruption on this toolchain | fill arrays element by element; avoid `new double[]{…}` with 3+ elements and `params double[]` with 3+ args |

---

## 15. Provenance and licence

* Library: **CoolProp** 8.0.0 by Ian Bell and the CoolProp contributors
  (http://www.coolprop.org), MIT licence — `reference/LICENSE_CoolProp_engine.txt`.
* Engines: `native/CoolProp_x86.dll` from the `build_stdc` configuration
  (`COOLPROP_STDCALL_LIBRARY=ON`, the 32-bit variant CMake builds), and
  `native/libCoolProp.so` from the aarch64 build shipped with the Linux plugin —
  both byte-identical to their sources. `native/CoolProp_x64.dll` is built
  from the unmodified CoolProp 8.0.0 source with the toolchain recorded in
  `patches/README.md` (GCC 16.2.0 MinGW-w64 x86-64; recipe reproducible).
* Design baseline: the Mono/Linux CoolProp SMath plugin, kept whole in
  `reference/` (`*.linux.*` files) together with its documentation.
* SMath bridge, unit layer, P/Invoke surface, function table, test harness and
  packaging: original work in this repository.
* SMath Studio itself is © Andrey Ivashov.

All trademarks belong to their owners. Values quoted in §11 were produced by this
plugin's own engine and are checked in by `tests/`.
