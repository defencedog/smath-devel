# plugin/ — SMath Studio plugin sources (Windows)

Source trees for five SMath Studio plugins, copied from `~/claude_ukhan/smath/plugins/`.
Each is self-contained with its own `README.md`, `build.ps1`, `src/`, `tests/`, and packaging.

| Directory | Plugin | README |
| :--- | :--- | :--- |
| `IF97_src_win/` | IAPWS-IF97 water/steam properties | [`IF97_src_win/README.md`](IF97_src_win/README.md) |
| `PsychroLib_src_win/` | Psychrometric functions | [`PsychroLib_src_win/README.md`](PsychroLib_src_win/README.md) |
| `CoolPropWrapper_src_win/` | CoolProp wrapper (native DLL + .so) | [`CoolPropWrapper_src_win/README.md`](CoolPropWrapper_src_win/README.md) |
| `PhreeqcWrapper_src_win/` | PHREEQC geochemical modelling (native DLL + .so + databases) | [`PhreeqcWrapper_src_win/README.md`](PhreeqcWrapper_src_win/README.md) |
| `SwarmFit_src_win/` | Swarm particle-swarm fitting (managed only, no native library) | [`SwarmFit_src_win/README.md`](SwarmFit_src_win/README.md) |

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

Each contains the wrapper assembly (`*Wrapper.dll`), its `.ini`, `metadata/*.json` descriptors, and —
where the plugin needs one — the native engines. CoolProp and Phreeqc also ship the Linux `.so` and
(Phreeqc) its `databases/*.dat` set.

> Older builds also appear under some `reference/*.zip`; those are superseded by `dist/`.

## ⚠️ The bundled Linux `.so` files are **aarch64** — replace them for x86-64 Linux

Every Linux shared library in this repo is an **ARM64 / aarch64** build:

| File | Architecture (verified with `file`) |
| :--- | :--- |
| `CoolPropWrapper_src_win/native/libCoolProp.so` | ELF 64-bit **ARM aarch64** |
| `PhreeqcWrapper_src_win/native/libIPhreeqc.so` | ELF 64-bit **ARM aarch64** |

They load on aarch64 Linux (e.g. Raspberry Pi, Orange Pi, ARM servers) **only**. On **x86-64
Linux** you must supply your own build and overwrite the file, **keeping the same name** — the
`dllmap` in each `*.dll.config` resolves the P/Invoke name (`CoolProp_x86`/`CoolProp_x64` →
`libCoolProp.so`; `IPhreeqc_x86`/`IPhreeqc_x64` → `./libIPhreeqc.so`) relative to the assembly, so
the filename is what matters, not its contents.

Two ways to get an x86-64 plugin working:

```bash
# Option 1 — rebuild and repackage from source
#   drop your x86-64 .so over native/<name>.so, then run build.ps1 again
cp /path/to/x86_64/libIPhreeqc.so native/libIPhreeqc.so     # same filename

# Option 2 — patch the already-built zip in place (no toolchain needed)
zip -j CoolPropWrapper.8_0_0_5.zip /path/to/x86_64/libCoolProp.so     # replaces the entry
```

`PhreeqcWrapper_src_win/native/build-native.ps1` drives CMake for the **Windows** DLLs and refuses
to cross-build the `.so` on Linux — build `libIPhreeqc.so` yourself with CMake and drop it in
`native/` (the script says so explicitly).

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

`PhreeqcWrapper_src_win` additionally excludes `native/_build_x86/` and `native/_build_x64/` —
CMake scratch directories that `build-native.ps1` **creates** (`-B $build`), never reads.

This removes ~233 MB of restorable/duplicated build output (the same Microsoft reference
assemblies and SMath `smathstudio.*.ref` packages would otherwise appear once per project).

**To build, populate `packages/` first.** `build.ps1` does not download anything — it copies
`-PackagesSource <dir>` into `packages/` on first run and **throws if `packages/` is missing and no
source is given**. The projects are deliberately offline: the `.csproj` files reference the
assemblies by path (`$(PackagesPath)`) and never run a NuGet restore. Obtain the packages with
`nuget.exe` — or by unzipping the `.nupkg` files — as each project's README documents:

| Project | Required packages |
| :--- | :--- |
| IF97, PsychroLib, CoolProp, Phreeqc | `Microsoft.NETFramework.ReferenceAssemblies.net20` 1.0.3 + `SMathStudio.Core.Ref` 1.73.9126 + `SMathStudio.Drawing.Ref` 1.4.0 |
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

Note: `CoolPropWrapper_src_win/native/` and `PhreeqcWrapper_src_win/native/` **are** tracked
(vendored engine binaries); they are not regenerated by `build.ps1`.

## Licensing note

Third-party binaries are present here as build prerequisites for interoperability, not as
free software:

- `packages/` (when populated) — Microsoft .NET reference assemblies and SMath Studio reference
  DLLs. **Not** freely redistributable.
- `PhreeqcWrapper_src_win/native/` and the `databases/*.dat` shipped in its zip — IPhreeqc and the
  PHREEQC database files from **USGS PHREEQC**. Public-domain US Government work.
- `CoolPropWrapper_src_win/native/` — CoolProp engine, **MIT**.
- `SwarmFit_src_win/reference/LICENSE` — **MIT**, © 2026 Usman Khan.

Each vendored engine's own `patches/README.md` / `reference/` notes record how it was built.