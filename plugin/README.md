# plugin/ — SMath Studio plugin sources (Windows)

Source trees for six SMath Studio plugins. Each is self-contained with its own `README.md`,
`build.ps1`, `src/`, `tests/`, and packaging.

| Directory | Plugin | README |
| :--- | :--- | :--- |
| `IF97_src_win/` | IAPWS-IF97 water/steam properties | [`IF97_src_win/README.md`](IF97_src_win/README.md) |
| `PsychroLib_src_win/` | Psychrometric functions | [`PsychroLib_src_win/README.md`](PsychroLib_src_win/README.md) |
| `CoolPropWrapper_src_win/` | CoolProp wrapper (native DLL + .so) | [`CoolPropWrapper_src_win/README.md`](CoolPropWrapper_src_win/README.md) |
| `PhreeqcWrapper_src_win/` | PHREEQC geochemical modelling (native DLL + .so + databases) | [`PhreeqcWrapper_src_win/README.md`](PhreeqcWrapper_src_win/README.md) |
| `SwarmFit_src_win/` | Swarm particle-swarm fitting (managed only, no native library) | [`SwarmFit_src_win/README.md`](SwarmFit_src_win/README.md) |
| `Optim_src_win/` | OptimLib numerical optimisation and root-finding (native DLL + .so) | [`Optim_src_win/README.md`](Optim_src_win/README.md) |

`Optim_src_win/` is the newest tree; the first five came from `~/claude_ukhan/smath/plugins/`,
Optim from `~/PDF/plugins/Optim_src_win` (2026-10-24).

## Installable plugin packages — `dist/` (tracked deliberately)

Each project's `dist/` holds the **built, user-installable plugin ZIP** — import it directly into
SMath Studio; no build required. These are release artifacts and must stay in the repo:

| Package | Size | Version |
| :--- | ---: | :--- |
| `IF97_src_win/dist/IF97Wrapper.1_1_1_0.zip` | 116 KB | 1.1.1.0 |
| `PsychroLib_src_win/dist/PsychroLibWrapper.1_2_0_0.zip` | 44 KB | 1.2.0.0 |
| `CoolPropWrapper_src_win/dist/CoolPropWrapper.8_0_0_5.zip` | 37 MB | 8.0.0.5 |
| `PhreeqcWrapper_src_win/dist/PhreeqcWrapper.1_1_0_0.zip` | 20 MB | 1.1.0.0 |
| `SwarmFit_src_win/dist/SwarmFitWrapper.1_1_0_0.zip` | 40 KB | 1.1.0.0 |
| `Optim_src_win/dist/OptimWrapper.1_0_0_0.zip` | 8.0 MB | 1.0.0.0 |

Each contains the wrapper assembly (`*Wrapper.dll`), its `.ini`, `metadata/*.json` descriptors, and —
where the plugin needs one — the native engines. CoolProp, Phreeqc and Optim also ship the Linux
`.so`; Phreeqc adds its `databases/*.dat` set, Optim its `LICENSE`/`NOTICE`/`LICENSE-Eigen.txt`.

## Worked examples — `examples/`

Each `examples/` folder holds runnable SMath worksheets (`.sm`) with a rendered PDF (`.pdf`) beside
them, so the expected output is visible without opening SMath:

| Worksheet | Rendered |
| :--- | :--- |
| `IF97_src_win/examples/if97_steam_tables.sm` | `if97_steam_tables.pdf` (1.31 MB) |
| `PsychroLib_src_win/examples/psychrolib_psychrometrics.sm` | `psychrolib_psychrometrics.pdf` (2.00 MB) |
| `PsychroLib_src_win/examples/cooling_tower_water_makeup_psychrometrics.sm` | `cooling_tower_water_makeup_psychrometrics.pdf` (3.55 MB) |
| `SwarmFit_src_win/examples/swarmfit_parameter_estimation.sm` | `swarmfit_parameter_estimation.pdf` (1.90 MB) |
| `Optim_src_win/examples/optim_solvers.sm` | `optim_solvers.pdf` (0.79 MB) |
| `Optim_src_win/examples/optim_system_5x5.sm` | `optim_system_5x5.pdf` (0.49 MB) |
| `PhreeqcWrapper_src_win/examples/cooling_water_scaling_wet_utilities.sm` | *(no PDF yet)* |

To run one, install the matching plugin from `dist/` and open the `.sm` in SMath Studio. The
worksheets carry their own expected results, so a build that is wrong will disagree with them.

Notes:

- `CoolPropWrapper_src_win` has **no** `examples/` folder.
- The Phreeqc worksheet is the current one and has no PDF.
- Worked-example snippets also appear in each project's own README (`## Worked examples`), which
  describe results inline rather than shipping the sheet.

> Older builds also appear under some `reference/*.zip`; those are superseded by `dist/`.

## ⚠️ The bundled Linux `.so` files are **aarch64** — replace them for x86-64 Linux

Every Linux shared library in this repo is an **ARM64 / aarch64** build:

| File | Architecture (verified with `file`) |
| :--- | :--- |
| `CoolPropWrapper_src_win/native/libCoolProp.so` | ELF 64-bit **ARM aarch64** |
| `PhreeqcWrapper_src_win/native/libIPhreeqc.so` | ELF 64-bit **ARM aarch64** |
| `Optim_src_win/native/libOptim.so` | ELF 64-bit **ARM aarch64** |

They load on aarch64 Linux (e.g. Raspberry Pi, Orange Pi, ARM servers) **only**. On **x86-64
Linux** you must supply your own build and overwrite the file, **keeping the same name** — the
`dllmap` in each `*.dll.config` resolves the P/Invoke name (`CoolProp_x86`/`CoolProp_x64` →
`libCoolProp.so`; `IPhreeqc_x86`/`IPhreeqc_x64` → `./libIPhreeqc.so`; `Optim_x86`/`Optim_x64` →
`./libOptim.so`) relative to the assembly, so the filename is what matters, not its contents.

Two ways to get an x86-64 plugin working:

```bash
# Option 1 — rebuild and repackage from source
#   drop your x86-64 .so over native/<name>.so, then run build.ps1 again
cp /path/to/x86_64/libIPhreeqc.so native/libIPhreeqc.so     # same filename

# Option 2 — patch the already-built zip in place (no toolchain needed)
zip -j CoolPropWrapper.8_0_0_5.zip /path/to/x86_64/libCoolProp.so     # replaces the entry
```

Provenance differs per project, and **none** ships a Linux build script:

- `CoolPropWrapper_src_win` — no `native/build-native.ps1`. `libCoolProp.so` is a **prebuilt,
  unmodified** aarch64 binary taken from the upstream SMath CoolProp integration
  (`CoolpropWrapper_src/lib/arm64/`); only `CoolProp_x64.dll` is built here, from pinned
  CoolProp-8.0.0 source. See its `patches/README.md`.
- `PhreeqcWrapper_src_win` — `native/build-native.ps1` drives CMake (`-G Ninja`) for the **Windows**
  DLLs; `build.ps1 -BuildNative` states outright that on Linux you must *"build libIPhreeqc.so with
  CMake yourself and drop it in `native\`"*.
- `Optim_src_win` — `native/build-native.ps1` invokes MinGW-w64 `g++` directly (no CMake), picking
  the output name from `g++ -dumpmachine`. The shipped aarch64 `libOptim.so` was cross-built on a
  Linux x64 host with `aarch64-linux-gnu-g++` 11.4; `patches/README.md` records the exact command,
  the source pins (OptimLib 3.1.3, BaseMatrixOps, Eigen) and the expected MD5.

All three mean the same thing for an x86-64 user: obtain or build the shared object yourself, keep
the filename, and drop it in `native/`.

The Windows engines (`*_x86.dll`, `*_x64.dll`) are unaffected: the plugin picks the one matching
the host process bitness.

## Build-generated paths are not committed

Only source, configuration, documentation, and vendored native binaries are tracked. Everything
each project's own `.gitignore` excludes is **absent from this repo by design**:

```
packages/     # NuGet reference assemblies — supplied by build.ps1 -PackagesSource
build/        # build output
src/bin/      # compiler output
src/obj/      # compiler intermediates
tests/bin/    # test binary output
tests/obj/    # test intermediates
```

`PhreeqcWrapper_src_win` and `Optim_src_win` additionally exclude `native/_build_x86/` and
`native/_build_x64/` — CMake scratch directories that `build-native.ps1` **creates** (`-B $build`),
never reads.

This removes ~276 MB of restorable/duplicated build output (the same Microsoft reference
assemblies and SMath `smathstudio.*.ref` packages would otherwise appear once per project).

**To build, populate `packages/` first.** `build.ps1` does not download anything — it copies
`-PackagesSource <dir>` into `packages/` on first run and **throws if `packages/` is missing and no
source is given**. The projects are deliberately offline: the `.csproj` files reference the
assemblies by path (`$(PackagesPath)`) and never run a NuGet restore. Obtain the packages with
`nuget.exe` — or by unzipping the `.nupkg` files — as each project's README documents:

| Project | Required packages |
| :--- | :--- |
| IF97, PsychroLib, CoolProp, Phreeqc, Optim | `Microsoft.NETFramework.ReferenceAssemblies.net20` 1.0.3 + `SMathStudio.Core.Ref` 1.73.9126 + `SMathStudio.Drawing.Ref` 1.4.0 |
| SwarmFit | `Microsoft.NETFramework.ReferenceAssemblies.net20` 1.0.3 + `SMathStudio.Core.Ref` 1.73.9126 |

```powershell
nuget install Microsoft.NETFramework.ReferenceAssemblies.net20 -Version 1.0.3 -OutputDirectory <extract-dir>
nuget install SMathStudio.Core.Ref    -Version 1.73.9126 -OutputDirectory <extract-dir>
nuget install SMathStudio.Drawing.Ref -Version 1.4.0    -OutputDirectory <extract-dir>
.\build.ps1 -PackagesSource <extract-dir>
```

Requires PowerShell and the .NET SDK (6.0+) on Windows; `build.ps1` then runs
`dotnet build src/<proj>.csproj -c Release` and packages the result into `dist/`. The plugins
target **.NET Framework 2.0** so they also load under Mono on Linux.

Note: `CoolPropWrapper_src_win/native/`, `PhreeqcWrapper_src_win/native/` and
`Optim_src_win/native/` **are** tracked (vendored engine binaries); they are not regenerated by
`build.ps1` alone.

## Licensing note

Unlike the fonts in [`../font-pack/`](../font-pack/) and the patched SMath assembly in
[`../print-patch/`](../print-patch/), the plugin trees are **freely licensed** — `packages/` is the
only non-redistributable part:

- `Optim_src_win/` — **Apache-2.0** (© 2026 OptimWrapper contributors); bundles OptimLib (Apache-2.0,
  © 2016-2023 Keith O'Hara) and Eigen (MPL-2.0), recorded in `packaging/LICENSE`,
  `packaging/NOTICE` and `packaging/LICENSE-Eigen.txt`.
- `CoolPropWrapper_src_win/native/` — CoolProp engine, **MIT**.
- `PhreeqcWrapper_src_win/native/` and the `databases/*.dat` in its zip — IPhreeqc and the PHREEQC
  database files from **USGS PHREEQC**, public-domain US Government work.
- `SwarmFit_src_win/reference/LICENSE` — **MIT**, © 2026 Usman Khan.
- `packages/` (when populated) — Microsoft .NET reference assemblies and SMath Studio reference
  DLLs. **Not** freely redistributable; they are build prerequisites only.

Each vendored engine's own `patches/README.md` / `reference/` notes record how it was built.