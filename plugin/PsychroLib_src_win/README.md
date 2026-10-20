# PsychroLib Wrapper for SMath Studio

A native SMath Studio plugin exposing the [ASHRAE 2017 psychrometric
formulations](https://github.com/CoolProp/PsychroLib) as 40 worksheet functions.
The plugin is a thin bridge: `PsychroLib`'s C# port is compiled in unchanged, and a
small SMath layer converts units, calls it, and tags the result.

| | |
|---|---|
| **Assembly** | `PsychroLibWrapper.dll` |
| **Plugin GUID** | `36a0265c-b529-4440-88e1-88f9226cee6a` |
| **Version** | 1.2.0.0 |
| **Target framework** | .NET Framework 2.0 (CLR 2.0) |
| **Prebuilt artifact** | `dist/PsychroLibWrapper.1_2_0_0.zip` |
| **Units** | SI in, SI out (Pa, K, J/kg, kg/m³) |

Targeting .NET 2.0 is deliberate: SMath Studio runs plugins under Mono on Linux,
and Mono's plugin host there is a CLR 2.0 host. A 2.0 assembly loads on both
Windows and Linux; a 4.x assembly does not.

---

## 1. Install the prebuilt plugin

1. Copy `dist/PsychroLibWrapper.1_2_0_0.zip` anywhere convenient.
2. In SMath Studio: **Tools → Plugins → Install…**, select the zip, restart.
3. Or unzip it by hand into

   ```
   %APPDATA%\SMath\extensions\plugins\36a0265c-b529-4440-88e1-88f9226cee6a\1.2.0.0\
   ```

   so that the folder contains `PsychroLibWrapper.dll`, `PsychroLibWrapper.dll.ini`
   and `metadata\`.

The 40 functions then appear in the function list under **PsychroLib Wrapper**.

> The zip must contain **stored** (uncompressed) entries. A deflated zip fails to
> install with *"Stream length must be non-negative and less than 2^31 - 1 -
> origin"*. `tools/pack.py` enforces this — do not repackage with a generic zip
> tool that compresses.

---

## 2. Repository layout

```
PsychroLib_src_win/
├── src/                                  plugin source (this is what compiles)
│   ├── psychrolib.cs                     upstream PsychroLib 2.5.0, verbatim
│   ├── Psy.cs                            thin adapter: SMath TNumber -> double
│   ├── Functions.cs                      the 40 function definitions
│   ├── Units.cs                          lazy base-unit expression cache
│   ├── Plugin.cs                         IPluginHandleEvaluation implementation
│   ├── AssemblyInfo.cs                   carries the plugin GUID
│   └── PsychroLibSMath.csproj            legacy (non-SDK) project file
├── tests/
│   ├── Program.cs                        console harness, 25 checks incl. a 40-function contract sweep
│   ├── App.config                        lets the net20 exe run on a v4 CLR
│   └── PsychroLibHarness.csproj
├── tools/
│   └── pack.py                           STORED-only zip writer
├── packaging/
│   ├── PsychroLibWrapper.dll.ini         plugin manifest (GUID, version, title)
│   └── metadata/                         localised descriptions
├── patches/                              upstream diffs, if any (see patches/README.md)
├── dist/                                 installable zip artifacts
├── reference/                            the original Linux/Mono build, kept for comparison
├── build.ps1                             one-shot: build + package
└── .gitignore
```

`packages/` is created by `build.ps1` and is **not** committed.
`reference/PsychroLibWrapper.1_0_0_1.zip` is the Mono/Linux plugin this Windows
port was matched against — it is a historical baseline, **not** an install
candidate. Use the zip in `dist/`. Two differences worth knowing: it ships the
library as a separate `PsychroLib.dll`, whereas this build compiles the same
source straight into the wrapper (one DLL, no load-order risk); and it carries
the **same GUID**, so installing it would overwrite the Windows build.

---

## 3. Prerequisites

| Need | Why | Notes |
|---|---|---|
| .NET SDK 6.0 or newer | provides `dotnet build` / MSBuild | no Visual Studio required |
| `microsoft.netframework.referenceassemblies.net20` | the v2.0 reference assemblies | see §4 |
| `SMathStudio.Core.Ref` 1.73.9126 | `SMath.Manager`, `SMath.Math.Numeric`, `SMath.Math.Symbolic` | see §4 |
| `SMathStudio.Drawing.Ref` 1.4.0 | pulled in by Core.Ref's dependency graph | see §4 |
| Python 3 | runs `tools/pack.py` | any 3.x |

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
nuget install Microsoft.NETFramework.ReferenceAssemblies.net20 -Version 1.0.3 -OutputDirectory packages -ExcludeVersion:$false
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
`-Python` to `python`.

If your machine blocks unsigned scripts, run it as

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

### Doing it by hand

```bat
dotnet build src\PsychroLibSMath.csproj -c Release
```

Output: `src\bin\Release\PsychroLibWrapper.dll`.

Any of the path properties can be overridden instead of creating `packages\`:

```bat
dotnet build src\PsychroLibSMath.csproj -c Release ^
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

---

## 6. Test

```bat
dotnet build tests\PsychroLibHarness.csproj -c Release
tests\bin\Release\PsychroLibHarness.exe
```

The harness references the plugin **as a project**, so this one command builds the
plugin first and then links it — a clean checkout needs no prior step, and the
tests can never run against a stale DLL. It exercises `EvaluateSi`, the
pure-`double` layer, and does **not** need SMath running. It checks

* the SI contract of all 40 functions (arity, argument kinds, output dimension),
* psychrometric reference values against ASHRAE tables,
* round trips (`RH → W → RH`, `T_dp → W → T_dp`, …),
* that out-of-range input yields `NaN` instead of an exception.

Expected last line: `=== ALL TESTS PASSED ===`.

One thing to know:

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
copy src\bin\Release\PsychroLibWrapper.dll build\package\
python tools\pack.py build\package dist\PsychroLibWrapper.1_2_0_0.zip
```

`tools/pack.py` writes every entry with `ZIP_STORED` and then re-opens the archive
to assert `compress_type == 0` on each entry, so a bad artifact cannot be
produced silently.

The version in the filename comes from `Version` in
`packaging/PsychroLibWrapper.dll.ini`. Bump it there **and** in
`src/AssemblyInfo.cs`; the `.ini` is what SMath reads, the assembly attribute is
what the rest of the world sees.

---

## 8. How the units work

SMath's numeric layer keeps a value and a unit syntax tree per number:

* `BaseEntry.Value` is **always in SI base units**,
* `BaseEntry.Units` is the unit AST (`1` = dimensionless),
* `UnitInfo.Factor` does the conversion when a unit token is parsed.

So `25*'°C` reaches a plugin as `298.15`, and `3*'MPa` as `3e6`. The plugin does
not see "MPa"; it sees pascals. This plugin therefore takes and returns SI base
units and nothing else — which is exactly what makes results composable with the
rest of a worksheet.

### Why the results carry base-unit expressions, never `J` or `Pa`

When SMath combines two numbers it compares their unit ASTs for equality
(`BaseEntry.ComUnits`). A result tagged `'J/'kg` is *not* the same AST as a
native `'m^2/'s^2`, so the first subtraction raises **Error 74**. Every result
this plugin emits is therefore tagged with a base-unit product:

| Quantity | Tagged as |
|---|---|
| Temperature | `'K` |
| Pressure | `'kg/('m*'s^2)` |
| Specific energy (enthalpy, J/kg) | `'m^2/'s^2` |
| Density | `'kg/'m^3` |
| Specific volume | `'m^3/'kg` |
| Dimensionless ratio | *(no tag — `Units` stays `1`)* |

SMath contracts these for display on its own: a result of `55748 'm^2/'s^2`
shows as `55.748 kJ/kg` if you ask for those units. **Do not pre-scale outputs**
in the plugin — that would double-convert.

### Temperature is an offset unit

`25*'°C` arrives as `298.15`, because SMath converts to kelvin. PsychroLib's SI
API takes **degrees Celsius**, so the plugin subtracts 273.15 on the way in and
adds it on the way out. Results are tagged `'K`, never `'°C`, so a downstream
subtraction is a plain difference rather than a shifted one.

### The one conversion the plugin performs

| Boundary | Unit | PsychroLib expects |
|---|---|---|
| dry-bulb / wet-bulb / dew-point temperature | K | °C (`−273.15`) |
| everything else | SI base | unchanged |

That is the entire conversion surface. It is implemented once, in
`PsychroFunctionBase.EvaluateSi`, and covered by the harness.

---

## 9. Function reference

**Reading the tables.** *In* lists the arguments in the order they must be
supplied, with the unit SMath hands to the plugin. *Out* is the unit of the
returned quantity. `-` means dimensionless (a plain number, `Units = 1`).

`W` is the humidity ratio in kg/kg (dry air basis); `RH` is relative humidity as
a fraction `0…1`, not a percentage.

### 9.1 Temperature helpers (2)

These exist for compatibility with earlier versions of this plugin. SMath
converts temperature natively, so in a worksheet you normally write `25*'°C`
instead of calling them.

| Function | In | Out | Notes |
|---|---|---|---|
| `Psy_CelsiusToKelvin` | `T_C` °C (plain number) | K | `Psy_CelsiusToKelvin(25) = 298.15` |
| `Psy_KelvinToCelsius` | `T_K` (K) | °C (plain number) | `Psy_KelvinToCelsius(300) = 26.85` |

### 9.2 Humidity ratio (6)

| Function | In | Out |
|---|---|---|
| `Psy_HumRatio_RH` | `T_dry` K, `RH` -, `p` Pa | kg/kg |
| `Psy_HumRatio_TWetBulb` | `T_dry` K, `T_wet` K, `p` Pa | kg/kg |
| `Psy_HumRatio_TDewPoint` | `T_dew` K, `p` Pa | kg/kg |
| `Psy_HumRatio_VapPres` | `p_vap` Pa, `p` Pa | kg/kg |
| `Psy_HumRatio_SpecHum` | `W_specific` kg/kg | kg/kg |
| `Psy_HumRatio_Enthalpy` | `h` J/kg, `T_dry` K | kg/kg |

### 9.3 Dew point (4)

| Function | In | Out |
|---|---|---|
| `Psy_TDewPoint_RH` | `T_dry` K, `RH` - | K |
| `Psy_TDewPoint_TWetBulb` | `T_dry` K, `T_wet` K, `p` Pa | K |
| `Psy_TDewPoint_VapPres` | `T_dry` K, `p_vap` Pa | K |
| `Psy_TDewPoint_HumRatio` | `T_dry` K, `W` kg/kg, `p` Pa | K |

### 9.4 Wet bulb (3)

| Function | In | Out |
|---|---|---|
| `Psy_TWetBulb_TDewPoint` | `T_dry` K, `T_dew` K, `p` Pa | K |
| `Psy_TWetBulb_RH` | `T_dry` K, `RH` -, `p` Pa | K |
| `Psy_TWetBulb_HumRatio` | `T_dry` K, `W` kg/kg, `p` Pa | K |

### 9.5 Relative humidity (4)

| Function | In | Out |
|---|---|---|
| `Psy_RH_TDewPoint` | `T_dry` K, `T_dew` K | - (0…1) |
| `Psy_RH_TWetBulb` | `T_dry` K, `T_wet` K, `p` Pa | - (0…1) |
| `Psy_RH_VapPres` | `T_dry` K, `p_vap` Pa | - (0…1) |
| `Psy_RH_HumRatio` | `T_dry` K, `W` kg/kg, `p` Pa | - (0…1) |

### 9.6 Vapour pressure (3)

| Function | In | Out |
|---|---|---|
| `Psy_VapPres_RH` | `T_dry` K, `RH` - | Pa |
| `Psy_VapPres_TDewPoint` | `T_dew` K | Pa |
| `Psy_VapPres_HumRatio` | `W` kg/kg, `p` Pa | Pa |

### 9.7 Specific humidity (1)

| Function | In | Out |
|---|---|---|
| `Psy_SpecHum` | `W` kg/kg | kg/kg |

### 9.8 Enthalpy (3)

| Function | In | Out |
|---|---|---|
| `Psy_Enthalpy` | `T_dry` K, `W` kg/kg | J/kg |
| `Psy_DryAirEnthalpy` | `T_dry` K | J/kg |
| `Psy_TDryBulb_Enthalpy` | `h` J/kg, `W` kg/kg | K |

### 9.9 Density and specific volume (5)

| Function | In | Out |
|---|---|---|
| `Psy_AirDensity` | `T_dry` K, `W` kg/kg, `p` Pa | kg/m³ |
| `Psy_AirVolume` | `T_dry` K, `W` kg/kg, `p` Pa | m³/kg |
| `Psy_DryAirDensity` | `T_dry` K, `p` Pa | kg/m³ |
| `Psy_DryAirVolume` | `T_dry` K, `p` Pa | m³/kg |
| `Psy_TDryBulb_Volume` | `v` m³/kg, `W` kg/kg, `p` Pa | K |

### 9.10 Saturation (3)

| Function | In | Out |
|---|---|---|
| `Psy_SatVapPres` | `T_dry` K | Pa |
| `Psy_SatHumRatio` | `T_dry` K, `p` Pa | kg/kg |
| `Psy_SatEnthalpy` | `T_dry` K, `p` Pa | J/kg |

### 9.11 Other moist-air properties (2)

| Function | In | Out |
|---|---|---|
| `Psy_VPD` | `T_dry` K, `W` kg/kg, `p` Pa | Pa |
| `Psy_DegSaturation` | `T_dry` K, `W` kg/kg, `p` Pa | - (0…1) |

### 9.12 Standard atmosphere (4)

| Function | In | Out |
|---|---|---|
| `Psy_AtmPressure` | `altitude` m | Pa |
| `Psy_AtmTemperature` | `altitude` m | K |
| `Psy_SeaLevelPressure` | `p_station` Pa, `altitude` m, `T_dry` K | Pa |
| `Psy_StationPressure` | `p_sea` Pa, `altitude` m, `T_dry` K | Pa |

**Total: 40 functions.**

---

## 10. Worked examples

```text
T  := 25*'°C                     → 298.15 K
p  := 101325*'Pa

W  := Psy_HumRatio_RH(T; 0.5; p)        = 0.009881 kg/kg
h  := Psy_Enthalpy(T; W)                = 50.3 kJ/kg      (tagged m^2/s^2)
Td := Psy_TDewPoint_RH(T; 0.5)          = 287.01 K
Tw := Psy_TWetBulb_RH(T; 0.5; p)        = 291.04 K
ρ  := Psy_AirDensity(T; W; p)           = 1.177 kg/m³
```

Because the results carry real units, these compose directly:

```text
Psy_RH_HumRatio(298.15*'K; 0.00988; 101325*'Pa) - 0.5     → 0     (no Error 74)
```

Passing a temperature in °C or K both work — SMath normalises first:

```text
Psy_Enthalpy(25*'°C; 0.00988)  ≡  Psy_Enthalpy(298.15*'K; 0.00988)
```

Out-of-range input returns `NaN`, which SMath renders as `NaN` in the cell
rather than opening an error dialog. PsychroLib's validity envelope is roughly
−100…200 °C at 1 atm; outside it the correlations are extrapolations, and
beyond their domain the library throws.

---

## 11. Regenerating from source

The whole plugin is reproducible from this tree. There is no patch step today,
because `src/psychrolib.cs` is upstream 2.5.0 **verbatim** and the SMath layer is
original code in this repository. `patches/` exists so that the day an upstream
file does need a local edit, the edit has an obvious home; see
`patches/README.md` for the workflow.

### From this tree

```powershell
.\build.ps1 -PackagesSource ..\nuget-extracted     # → dist\PsychroLibWrapper.1_2_0_0.zip
```

### From upstream plus this tree

1. Download PsychroLib 2.5.0 (`psychrolib-2.5.0.zip`) and extract.
2. Copy its C# sources over `src/psychrolib.cs`:
   ```bat
   copy /Y psychrolib-2.5.0\c_sharp\psychrolib.cs src\psychrolib.cs
   ```
3. Apply any patches:
   ```bat
   git apply --verbose patches\*.patch
   ```
4. Build, test, package as in §5–§7.

### From nothing — the recipe that produced this plugin

1. Create a legacy `.csproj` with `<TargetFrameworkVersion>v2.0</TargetFrameworkVersion>`
   and direct `<Reference>` + `<HintPath>` entries (see §2). Do **not** use
   `<TargetFramework>net20</TargetFramework>` — that is the SDK-style form and it
   triggers a restore.
2. Set `<TargetFrameworkRootPath>` to the `build\` folder of the net20 reference
   assemblies package.
3. Write `AssemblyInfo.cs` with `[assembly: Guid("36a0265c-…")]`. **This GUID,
   not `ProjectGuid`, is the install folder name.** Reusing an existing GUID is
   how you upgrade a plugin in place instead of installing a second copy.
4. Implement `IPluginHandleEvaluation.GetTermsHandled` and return one `TermInfo`
   per function; implement `IPluginMathNumericEvaluation.NumericEvaluation` to
   evaluate it.
5. Read arguments with `args[i].obj.ToDouble()` — `TNumber.obj` is a public field
   of type `BaseEntry`; there is no `DoubleVal`.
6. Build the result as `new TNumber(value)` and assign `result.obj.Units` from a
   lazily-created `MItem`.
7. Wrap every library call in `try/catch` and return `NaN` on failure.
8. Package with `ZIP_STORED` entries only.

---

## 12. Plugin identity and versioning

* `[assembly: Guid("36a0265c-b529-4440-88e1-88f9226cee6a")]` in
  `src/AssemblyInfo.cs` — the plugin's identity. Two zips with the same GUID are
  the same plugin; the higher version wins.
* `Version` in `packaging/PsychroLibWrapper.dll.ini` — the version SMath reports
  and the folder name under `extensions\plugins\{GUID}\`.
* `[assembly: AssemblyVersion]` — bump alongside, for consistency.

Keep the GUID stable forever. Changing it orphans existing installations.

---

## 13. Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| `Stream length must be non-negative…` on install | zip entries are DEFLATE | repack with `tools/pack.py` |
| `MSB3644: reference assemblies … not found` | `TargetFrameworkRootPath` wrong | must end at `…\net20.1.0.3\build\` |
| `NETSDK1004: assets file not found` | you converted to SDK-style | keep the legacy `.csproj` |
| `CS0102: already contains a definition` | nested class name collides with a static field | suffix the class with `Fn` |
| Plugin loads, functions missing | `GetTermsHandled` returned nothing, or the `.ini` `Guid` ≠ assembly GUID | check both |
| Result shows a number but units are wrong | output tagged with a named unit (`'J/'kg`) | tag base units (`'m^2/'s^2`) |
| **Error 74** on first arithmetic with a result | same as above | same fix |
| Cell shows an error dialog instead of `NaN` | library exception escaped | `try/catch` → `NaN` |
| `CS0246: PsychroLibWrapper not found` | the project reference failed to build the plugin | build `src\PsychroLibSMath.csproj` directly and read its error |
| `0x80131700` when starting the harness | no CLR 2 runtime | keep `App.config` with `supportedRuntime v4.0` |
| `CS0305` / `CS0104: Converter is ambiguous` | `Func<>` is .NET 3.5; `Converter` exists twice | declare your own delegate; fully qualify `SMath.Math.Symbolic.Converter` |
| Array literal arrives as garbage numbers | Roslyn `InitializeArray` RVA-blob corruption on this toolchain | fill arrays element by element; avoid `new double[]{…}` with 3+ elements and `params double[]` with 3+ args |

---

## 14. Provenance and licence

* Psychrometric formulations: ASHRAE Handbook — Fundamentals (2017), ch. 1, as
  implemented by [PsychroLib](https://github.com/CoolProp/PsychroLib) 2.5.0
  (MIT licence). `src/psychrolib.cs` is that project's C# port, unmodified.
* SMath bridge, unit layer, function table, tests and packaging: original work in
  this repository.
