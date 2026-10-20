# IAPWS-IF97 Wrapper for SMath Studio

A native SMath Studio plugin exposing the [IAPWS-IF97 industrial formulation for
water and steam](http://www.iapws.org/relguide/IF97-Rev.html) as 46 worksheet
functions: properties from *(T, p)*, saturated liquid and vapour properties,
backward equations *(p, h)* / *(p, s)* / *(h, s)*, and the region index itself.

| | |
|---|---|
| **Assembly** | `IF97Wrapper.dll` |
| **Plugin GUID** | `bdb8d1d9-11bf-4f8b-aca4-c1fa3f6e13ad` |
| **Version** | 1.1.1.0 |
| **Target framework** | .NET Framework 2.0 (CLR 2.0) |
| **Prebuilt artifact** | `dist/IF97Wrapper.1_1_1_0.zip` |
| **Units** | SI in, SI out (Pa, K, J/kg, kg/m³) |

Targeting .NET 2.0 is deliberate: SMath Studio runs plugins under Mono on Linux,
and Mono's plugin host there is a CLR 2.0 host. A 2.0 assembly loads on both
Windows and Linux; a 4.x assembly does not.

---

## 1. Install the prebuilt plugin

1. Copy `dist/IF97Wrapper.1_1_1_0.zip` anywhere convenient.
2. In SMath Studio: **Tools → Plugins → Install…**, select the zip, restart.
3. Or unzip it by hand into

   ```
   %APPDATA%\SMath\extensions\plugins\bdb8d1d9-11bf-4f8b-aca4-c1fa3f6e13ad\1.1.1.0\
   ```

   so that the folder contains `IF97Wrapper.dll`, `IF97Wrapper.dll.ini` and
   `metadata\`.

The 46 functions then appear in the function list under **IF97 Wrapper**.

> The zip must contain **stored** (uncompressed) entries. A deflated zip fails to
> install with *"Stream length must be non-negative and less than 2^31 - 1 -
> origin"*. `tools/pack.py` enforces this — do not repackage with a generic zip
> tool that compresses.

---

## 2. Repository layout

```
IF97_src_win/
├── src/
│   ├── Api.cs                  IF97 public API — upstream, verbatim
│   ├── Region1.cs              region 1 Gibbs function — upstream, verbatim
│   ├── Region2.cs              region 2 — upstream, verbatim
│   ├── Region3.cs              region 3 (Helmholtz) — upstream, verbatim
│   ├── Region4.cs              saturation line — upstream, verbatim
│   ├── Region5.cs              region 5 — upstream, verbatim
│   ├── RegionBase.cs           shared helpers — upstream, verbatim
│   ├── BackwardsRegion.cs      B2bc, T(p,h), T(p,s), p(h,s) — upstream, verbatim
│   ├── BackwardsRegion3.cs     region 3 backward equations — upstream, verbatim
│   ├── Constants.cs            critical point, gas constant — upstream, verbatim
│   ├── Enums.cs                region / phase enums — upstream, verbatim
│   ├── FastPow.cs              integer-power helper — upstream, verbatim
│   ├── ValueTupleShim.cs       System.ValueTuple for net20 — ours
│   ├── Functions.cs            the 46 function definitions — ours
│   ├── Units.cs                lazy base-unit expression cache — ours
│   ├── Plugin.cs               IPluginHandleEvaluation — ours
│   ├── AssemblyInfo.cs         carries the plugin GUID — ours
│   └── IF97SMath.csproj        legacy (non-SDK) project file
├── tests/
│   ├── Program.cs              console harness, 35 checks vs R7-97 tables
│   ├── App.config              lets the net20 exe run on a v4 CLR
│   └── IF97Harness.csproj
├── tools/
│   └── pack.py                 STORED-only zip writer
├── packaging/
│   ├── IF97Wrapper.dll.ini     plugin manifest (GUID, version, title)
│   └── metadata/               localised descriptions
├── patches/                    upstream diffs, if any (see patches/README.md)
├── dist/                       installable zip artifacts
├── reference/                  the original Linux/Mono build, kept for comparison
├── build.ps1                   one-shot: build + package
└── .gitignore
```

`packages/` is created by `build.ps1` and is **not** committed.
`reference/IF97Wrapper.1_0_0_1.zip` is the Mono/Linux plugin this Windows port
was matched against — it is a historical baseline, **not** an install candidate.
Use the zip in `dist/`. Two differences worth knowing: it ships the library as a
separate `IF97.dll`, whereas this build compiles the same source straight into
the wrapper (one DLL, no load-order risk); and it carries the **same GUID**, so
installing it would overwrite the Windows build.

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
`-Python` to `python`.

If your machine blocks unsigned scripts, run it as

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

### Doing it by hand

```bat
dotnet build src\IF97SMath.csproj -c Release
```

Output: `src\bin\Release\IF97Wrapper.dll`.

Any of the path properties can be overridden instead of creating `packages\`:

```bat
dotnet build src\IF97SMath.csproj -c Release ^
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

### Why `ValueTupleShim.cs`

The upstream IF97 source targets `netstandard2.0` and leans on tuple syntax for
its coefficient tables:

```csharp
static readonly ValueTuple<int, int, double>[] Region1residdata = {
    (0, -2, 0.14632971213167),
    ...
};
```

.NET 2.0 has no `System.ValueTuple`, and shipping the NuGet `System.ValueTuple`
assembly would not load on a CLR 2.0 host. `ValueTupleShim.cs` declares
`ValueTuple<…>` and the `TupleElementNamesAttribute` placeholder in our own
assembly, so the compiler is satisfied and the runtime never needs an extra DLL.
It is compiled first; do not remove it.

---

## 6. Test

```bat
dotnet build tests\IF97Harness.csproj -c Release
tests\bin\Release\IF97Harness.exe
```

The harness references the plugin **as a project**, so this one command builds the
plugin first and then links it — a clean checkout needs no prior step, and the
tests can never run against a stale DLL. It exercises `EvaluateRaw`, the
pure-`double` layer, and does **not** need SMath running. Its 35 checks cover
values against the official IAPWS R7-97 verification tables — region 1 and 2
*(T, p)* points, region 3 isochores, the saturation line, region 5, and the
backward equations — plus round trips and `NaN` containment.

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
copy src\bin\Release\IF97Wrapper.dll build\package\
python tools\pack.py build\package dist\IF97Wrapper.1_1_1_0.zip
```

`tools/pack.py` writes every entry with `ZIP_STORED` and then re-opens the archive
to assert `compress_type == 0` on each entry, so a bad artifact cannot be
produced silently.

The version in the filename comes from `Version` in
`packaging/IF97Wrapper.dll.ini`. Bump it there **and** in
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

### Why the results carry base-unit expressions, never `J` or `Pa`

When SMath combines two numbers it compares their unit ASTs for equality
(`BaseEntry.ComUnits`). A result tagged `'J/'kg` is *not* the same AST as a
native `'m^2/'s^2`, so the first subtraction raises **Error 74**. Every result
this plugin emits is therefore tagged with a base-unit product:

| Quantity | Tagged as |
|---|---|
| Temperature | `'K` |
| Pressure | `'kg/('m*'s^2)` |
| Specific energy (h, u, latent heat) | `'m^2/'s^2` |
| Specific entropy, cp, cv | `'m^2/('s^2*'K)` |
| Density | `'kg/'m^3` |
| Specific volume | `'m^3/'kg` |
| Velocity, speed of sound | `'m/'s` |
| Dynamic viscosity | `'kg/('m*'s)` |
| Thermal conductivity | `'kg*'m/('s^3*'K)` |
| Surface tension | `'kg/'s^2` |
| Prandtl number, region index | *(no tag — `Units` stays `1`)* |

SMath contracts these for display on its own: a result of `115331 'm^2/'s^2`
shows as `115.331 kJ/kg` if you ask for those units. **Do not pre-scale outputs**
in the plugin — that would double-convert.

### The conversion the plugin performs

The IF97 library works in **K / MPa / kJ/kg / kJ/(kg·K)**. SMath works in SI
base units. The plugin converts at the boundary and nowhere else:

| Boundary | SMath supplies | IF97 expects | Factor |
|---|---|---|---|
| temperature | K | K | ×1 |
| pressure | Pa | MPa | ÷1e6 |
| specific enthalpy | J/kg | kJ/kg | ÷1e3 |
| specific entropy | J/(kg·K) | kJ/(kg·K) | ÷1e3 |

Outputs are scaled back by the inverse. This lives in `ToLibraryUnits` /
`OutputScale` in `src/Functions.cs` and is covered by the harness. It is the
reason you can write `IF97_h_Tp(300*'K; 3*'MPa)` and get an answer in J/kg that
SMath will happily contract to kJ/kg.

---

## 9. Function reference

**Reading the tables.** *In* lists the arguments in the order they must be
supplied, with the unit SMath hands to the plugin. *Out* is the unit of the
returned quantity. `-` means dimensionless (a plain number, `Units = 1`).

### 9.1 Saturation curve (3)

| Function | In | Out |
|---|---|---|
| `IF97_Tsat_p` | `p` Pa | K |
| `IF97_psat_T` | `T` K | Pa |
| `IF97_sigma_T` | `T` K | N/m (kg/s²) |

### 9.2 Properties from (T, p) (11)

| Function | In | Out | Symbol |
|---|---|---|---|
| `IF97_rho_Tp` | `T` K, `p` Pa | kg/m³ | ρ |
| `IF97_v_Tp` | `T` K, `p` Pa | m³/kg | v |
| `IF97_h_Tp` | `T` K, `p` Pa | J/kg | h |
| `IF97_s_Tp` | `T` K, `p` Pa | J/(kg·K) | s |
| `IF97_u_Tp` | `T` K, `p` Pa | J/kg | u |
| `IF97_cp_Tp` | `T` K, `p` Pa | J/(kg·K) | c_p |
| `IF97_cv_Tp` | `T` K, `p` Pa | J/(kg·K) | c_v |
| `IF97_w_Tp` | `T` K, `p` Pa | m/s | w |
| `IF97_mu_Tp` | `T` K, `p` Pa | Pa·s | μ |
| `IF97_tc_Tp` | `T` K, `p` Pa | W/(m·K) | λ |
| `IF97_Pr_Tp` | `T` K, `p` Pa | - | Pr |

### 9.3 Saturated liquid, from pressure (10)

| Function | In | Out |
|---|---|---|
| `IF97_rholiq_p` | `p` Pa | kg/m³ |
| `IF97_hliq_p` | `p` Pa | J/kg |
| `IF97_sliq_p` | `p` Pa | J/(kg·K) |
| `IF97_uliq_p` | `p` Pa | J/kg |
| `IF97_cpliq_p` | `p` Pa | J/(kg·K) |
| `IF97_cvliq_p` | `p` Pa | J/(kg·K) |
| `IF97_wliq_p` | `p` Pa | m/s |
| `IF97_muliq_p` | `p` Pa | Pa·s |
| `IF97_tcliq_p` | `p` Pa | W/(m·K) |
| `IF97_Prliq_p` | `p` Pa | - |

### 9.4 Saturated vapour, from pressure (10)

| Function | In | Out |
|---|---|---|
| `IF97_rhovap_p` | `p` Pa | kg/m³ |
| `IF97_hvap_p` | `p` Pa | J/kg |
| `IF97_svap_p` | `p` Pa | J/(kg·K) |
| `IF97_uvap_p` | `p` Pa | J/kg |
| `IF97_cpvap_p` | `p` Pa | J/(kg·K) |
| `IF97_cvvap_p` | `p` Pa | J/(kg·K) |
| `IF97_wvap_p` | `p` Pa | m/s |
| `IF97_muvap_p` | `p` Pa | Pa·s |
| `IF97_tcvap_p` | `p` Pa | W/(m·K) |
| `IF97_Prvap_p` | `p` Pa | - |

### 9.5 Latent heat (2)

| Function | In | Out |
|---|---|---|
| `IF97_hfg_p` | `p` Pa | J/kg |
| `IF97_hfg_T` | `T` K | J/kg |

### 9.6 Saturated properties from temperature (2)

| Function | In | Out |
|---|---|---|
| `IF97_hliq_T` | `T` K | J/kg |
| `IF97_hvap_T` | `T` K | J/kg |

### 9.7 Backward equations (8)

| Function | In | Out |
|---|---|---|
| `IF97_T_ph` | `p` Pa, `h` J/kg | K |
| `IF97_rho_ph` | `p` Pa, `h` J/kg | kg/m³ |
| `IF97_T_ps` | `p` Pa, `s` J/(kg·K) | K |
| `IF97_rho_ps` | `p` Pa, `s` J/(kg·K) | kg/m³ |
| `IF97_p_hs` | `h` J/kg, `s` J/(kg·K) | Pa |
| `IF97_T_hs` | `h` J/kg, `s` J/(kg·K) | K |
| `IF97_Region_ph` | `p` Pa, `h` J/kg | - (1…5) |
| `IF97_Region_ps` | `p` Pa, `s` J/(kg·K) | - (1…5) |

**Total: 46 functions.**

---

## 10. Validity ranges

IF97 covers five regions. Outside them the correlations are extrapolations; well
outside, the library throws and the plugin returns `NaN` rather than an error
dialog.

| Region | Covers | Limits |
|---|---|---|
| 1 | compressed liquid | 273.15 ≤ T ≤ 623.15 K, p_sat(T) ≤ p ≤ 100 MPa |
| 2 | superheated steam | 273.15 ≤ T ≤ 623.15 K, 0 < p ≤ p_sat(T); up to 1073.15 K below 100 MPa |
| 3 | near-critical / supercritical | 623.15 ≤ T ≤ 647.096 K, p > p_sat(T) |
| 4 | saturation line | 273.15 ≤ T ≤ 647.096 K |
| 5 | high-temperature steam | 1073.15 ≤ T ≤ 2273.15 K, 0 < p ≤ 50 MPa |

`IF97_Region_ph` and `IF97_Region_ps` return the region number so a worksheet can
branch on it. The `(T, p)` functions pick the region themselves.

---

## 11. Worked examples

```text
T := 300*'K
p := 3*'MPa

h  := IF97_h_Tp(T; p)                  = 115.33 kJ/kg    (tagged m^2/s^2)
v  := IF97_v_Tp(T; p)                  = 0.001002 m³/kg
s  := IF97_s_Tp(T; p)                  = 0.3923 kJ/(kg·K)
cp := IF97_cp_Tp(T; p)                 = 4.173 kJ/(kg·K)
ρ  := IF97_rho_Tp(T; p)                = 997.9 kg/m³

Ts := IF97_Tsat_p(101325*'Pa)          = 373.12 K
hg := IF97_hvap_p(101325*'Pa)          = 2676 kJ/kg
```

Because the results carry real units, these compose directly:

```text
IF97_hvap_p(101325*'Pa) - IF97_hliq_p(101325*'Pa)   = 2257 kJ/kg   (h_fg, no Error 74)
```

And the backward equations close the loop:

```text
IF97_T_ph(3*'MPa; 115331*'J/'kg)       = 300.0 K
IF97_Region_ph(3*'MPa; 115331*'J/'kg)  = 1
```

> Note the `'J/'kg` above: a **worksheet** may write derived units freely — SMath
> reduces them to base units before the plugin sees them. The restriction on
> derived units applies only to the unit ASTs the **plugin** returns.

---

## 12. Regenerating from source

The upstream IF97 library files are vendored verbatim; only the SMath layer is
original. `patches/` exists so that the day an upstream file needs a local edit,
the edit has a home — see `patches/README.md` for the workflow.

### From this tree

```powershell
.\build.ps1 -PackagesSource ..\nuget-extracted     # → dist\IF97Wrapper.1_1_1_0.zip
```

### From upstream plus this tree

1. Download the IF97 C# library (`IF97-master.zip`) and extract.
2. Copy **only the library sources** over the vendored ones — `Api.cs`,
   `Region1.cs`…`Region5.cs`, `RegionBase.cs`, `BackwardsRegion.cs`,
   `BackwardsRegion3.cs`, `Constants.cs`, `Enums.cs`, `FastPow.cs`:
   ```bat
   copy /Y IF97-master\*.cs src\
   ```
   Keep `ValueTupleShim.cs`, `Functions.cs`, `Units.cs`, `Plugin.cs` and
   `AssemblyInfo.cs` from this repository — they are not part of upstream. If the
   upstream drop also carries its own test/demo files, delete them again; they are
   not in this project's `<Compile>` list.
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
3. Write `AssemblyInfo.cs` with `[assembly: Guid("bdb8d1d9-…")]`. **This GUID,
   not `ProjectGuid`, is the install folder name.** Reusing an existing GUID is
   how you upgrade a plugin in place instead of installing a second copy.
4. Add `ValueTupleShim.cs` before the upstream sources in the `<Compile>` list.
5. Implement `IPluginHandleEvaluation.GetTermsHandled` and return one `TermInfo`
   per function; implement `IPluginMathNumericEvaluation.NumericEvaluation` to
   evaluate it.
6. Read arguments with `args[i].obj.ToDouble()` — `TNumber.obj` is a public field
   of type `BaseEntry`; there is no `DoubleVal`.
7. Build the result as `new TNumber(value)` and assign `result.obj.Units` from a
   lazily-created `MItem`.
8. Wrap every library call in `try/catch` and return `NaN` on failure.
9. Package with `ZIP_STORED` entries only.

---

## 13. Plugin identity and versioning

* `[assembly: Guid("bdb8d1d9-11bf-4f8b-aca4-c1fa3f6e13ad")]` in
  `src/AssemblyInfo.cs` — the plugin's identity. Two zips with the same GUID are
  the same plugin; the higher version wins.
* `Version` in `packaging/IF97Wrapper.dll.ini` — the version SMath reports and the
  folder name under `extensions\plugins\{GUID}\`.
* `[assembly: AssemblyVersion]` — bump alongside, for consistency.

Keep the GUID stable forever. Changing it orphans existing installations.

---

## 14. Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| `Stream length must be non-negative…` on install | zip entries are DEFLATE | repack with `tools/pack.py` |
| `MSB3644: reference assemblies … not found` | `TargetFrameworkRootPath` wrong | must end at `…\net20.1.0.3\build\` |
| `NETSDK1004: assets file not found` | you converted to SDK-style | keep the legacy `.csproj` |
| `CS0518: ValueTuple is not defined` | shim missing or compiled last | keep `ValueTupleShim.cs` first in `<Compile>` |
| `CS0102: already contains a definition` | nested class name collides with a static field | suffix the class with `Fn` |
| Plugin loads, functions missing | `GetTermsHandled` returned nothing, or the `.ini` `Guid` ≠ assembly GUID | check both |
| Result shows a number but units are wrong | output tagged with a named unit (`'J/'kg`) | tag base units (`'m^2/'s^2`) |
| **Error 74** on first arithmetic with a result | same as above | same fix |
| Result is 1e6 too large / too small | conversion applied twice, or not at all | `ToLibraryUnits` ÷1e6 in, `OutputScale` ×1e6 out |
| Cell shows an error dialog instead of `NaN` | library exception escaped | `try/catch` → `NaN` |
| `CS0246: IF97Wrapper not found` | the project reference failed to build the plugin | build `src\IF97SMath.csproj` directly and read its error |
| `0x80131700` when starting the harness | no CLR 2 runtime | keep `App.config` with `supportedRuntime v4.0` |
| `CS0305` / `CS0104: Converter is ambiguous` | `Func<>` is .NET 3.5; `Converter` exists twice | declare your own delegate; fully qualify `SMath.Math.Symbolic.Converter` |
| Array literal arrives as garbage numbers | Roslyn `InitializeArray` RVA-blob corruption on this toolchain | fill arrays element by element; avoid `new double[]{…}` with 3+ elements and `params double[]` with 3+ args |

---

## 15. Provenance and licence

* Formulation: IAPWS R7-97 (2012 revision), *Revised Release on the IAPWS
  Industrial Formulation 1997 for the Thermodynamic Properties of Water and
  Steam*.
* C# implementation: the `IF97` library by xhydongda (MIT licence), vendored
  verbatim in `src/`.
* SMath bridge, unit layer, function table, tests and packaging: original work in
  this repository.
