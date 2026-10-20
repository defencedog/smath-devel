# patches/

Everything this repository had to change relative to its two inputs: the
**Mono/Linux CoolPropWrapper** (`CoolpropWrapper_src/`, the upstream SMath plugin
that ships with SMath's own CoolProp integration) and the **CoolProp C library**
(`CoolProp-8.0.0/`, referenced by path only — not vendored here).

## 1. Native libraries — two pinned, one built from pinned source

| file | source | architecture | role |
|---|---|---|---|
| `native/CoolProp_x86.dll` | `CoolProp_PREBUILT/CoolProp.dll` | **i686** | 32-bit Windows hosts |
| `native/CoolProp_x64.dll` | built here from `CoolProp-8.0.0/` (recipe below) | **x86-64** | 64-bit Windows hosts |
| `native/libCoolProp.so` | `CoolpropWrapper_src/lib/arm64/libCoolProp.so` | **aarch64** | Linux |

Why all three:

* The SMath host is **AnyCPU**: `SMathStudio_Desktop.exe`, `Solver.exe` and every
  SMath assembly are PE32 with `corflags = ILONLY`, so on 64-bit Windows the
  host runs as a **64-bit process**. (An earlier draft of this repository
  claimed the host was i686 based on the PE machine field alone — that field is
  meaningless for ILONLY assemblies, and a live worksheet disproved the claim
  with `BadImageFormatException` / HRESULT `0x8007000B` when the i686 engine
  failed to load.) The engine production actually loads is therefore the
  **x86-64** one; the i686 engine is kept for genuinely 32-bit hosts and is
  exercised by the x86 harness run.
* The engine files are named `CoolProp_x86.dll` / `CoolProp_x64.dll` and ship
  **flat at the package root**. SMath's Extensions installer rejects a zip whose
  engines sit in `win32\`/`win64\` subfolders — two `CoolProp.dll` entries
  collide by basename and the install fails with *"plugin installation failed"*
  — so each engine's file name is also its `DllImport` name in
  `src/CoolPropEngine.cs`.
* `CoolProp_PREBUILT/CoolProp.dll` is built from `build_stdc` — CoolProp's
  `COOLPROP_STDCALL_LIBRARY=ON` configuration, the 32-bit variant CMake builds
  for exactly this purpose (see `CMakeLists.txt` line 1242,
  `COOLPROP_WINDOWS_PACKAGE_32B_DIR_STDCALL`). It exports all 71 C-API symbols,
  decorated (`_PropsSI@32`, …).
* **No official 64-bit Windows DLL of the C library is published anywhere.**
  The PyPI wheel statically links the engine into `CoolProp.pyd`; the GUI
  installer's `coolprop-gui.exe` is statically linked too; conda-forge's
  `win-64` channel stops at 7.1.0. The x86-64 engine was therefore **built from
  the pinned `CoolProp-8.0.0/` source** — recipe and hashes below.
* The Linux `.so` is aarch64 because the Linux host is ARM64. The x86-64
  `libCoolProp.so` in `CoolProp_PREBUILT/` is unused.

### The x86-64 engine build recipe (reproducible)

Toolchain: [w64devkit](https://github.com/skeeto/w64devkit) 2.10.1
(GCC 16.2.0, `x86_64-w64-mingw32`), CMake 3.29.5 + Ninja. The nine CPM
dependencies (Eigen, fmt, nlohmann_json, IF97, REFPROP-headers, msgpack-c,
valijson, boost-headers, multicomplex) are fetched once and supplied **offline**
(`-DCPM_<name>_SOURCE=<dir>` plus `-DFETCHCONTENT_FULLY_DISCONNECTED=ON`).

```bat
set MSYSTEM=MINGW64   :: satisfies CoolProp's MSYS2 shell guard
cmake -S CoolProp-8.0.0 -B CoolProp-8.0.0\_win64build -G Ninja ^
  -DCMAKE_MAKE_PROGRAM=<cmake-dir>\ninja.exe ^
  -DCMAKE_C_COMPILER=<w64devkit>\bin\gcc.exe ^
  -DCMAKE_CXX_COMPILER=<w64devkit>\bin\g++.exe ^
  -DCMAKE_RC_COMPILER=<w64devkit>\bin\x86_64-w64-mingw32-windres.exe ^
  -DCMAKE_BUILD_TYPE=Release ^
  -DCOOLPROP_SHARED_LIBRARY=ON -DCOOLPROP_STATIC_LIBRARY=OFF ^
  -DCOOLPROP_CATCH_MODULE=OFF -DCOOLPROP_NO_EXAMPLES=ON ^
  -DCPM_Eigen_SOURCE=<deps>\eigen    ... (all nine)
cmake --build CoolProp-8.0.0\_win64build --target CoolProp
```

Gotchas that cost time:

* `MSYSTEM` must be set or `CMakeLists.txt:264` stops with a FATAL_ERROR
  ("To compile with MSYS2 UCRT64…"). With it, the MSYS2 branch also drops the
  `lib` prefix and statically links libgcc/libstdc++/winpthread — the resulting
  DLL then needs nothing but system libraries.
* The RC compiler must be the **prefixed** `x86_64-w64-mingw32-windres.exe`:
  plain `windres.exe` preprocesses with whichever `gcc` is first on `PATH` and
  produced an **i386** `CoolProp.rc.obj`, failing the link with "i386
  architecture of input file … is incompatible with i386:x86-64 output". The
  prefixed windres needs `x86_64-w64-mingw32-gcc` on `PATH` — put the
  toolchain's `bin` first.
* The FetchContent sub-builds need `ninja` discoverable via `PATH` (otherwise
  the sub-build dies with "unable to find a build program corresponding to
  Ninja").

Result: `Release\CoolProp.dll` (copied to `native\CoolProp_x64.dll`) — 17,227,360 bytes, SHA-256
`64604EA8445E13C92A5681004086CDFAE2A3E89759AF7370B63FF8DE62E56950`, MD5
`27C721D678D41DCA28E6A7AB56C31319`; imports only `KERNEL32.dll` and
`msvcrt.dll`; 71 undecorated exports (no `@<n>` names).

Pinned hashes of the other two: `native/CoolProp_x86.dll` MD5
`27F65817735E0D566FF751364A800DB5`; `native/libCoolProp.so` MD5
`E88C271408F13D41560DED5E0F08C329`. `tests/` runs against the real binaries, so
a bad pin fails the suite rather than reaching a user.

## 2. What the Windows port changed against the Mono/Linux plugin

The Mono plugin is the design baseline and is kept whole in `reference/` (the
`*.linux.*` files). The deliberate differences:

### 2.1 Unit tagging — `src/Unit.cs` (rewritten)

The Mono plugin tagged results with **named derived units**: `'Pa`, `'J/'mol`,
`'W/'m/'K`, `'N/'m`. It then had to carry a "SS-2414 hack" (`FreshStore` +
`SymbolicCalculation` on every lookup) to force those into base form before
comparing them to a worksheet expression.

That approach is wrong on Windows: `BaseEntry.ComUnits` compares the two unit
ASTs for **equality** when combining components, so a result tagged `'Pa` is
dimensionally unequal to a native worksheet expression and the first subtraction
fails with **Error 74**. `PsychroLib_src_win/` and `IF97_src_win/` both document
this and tag **base-unit expressions only**.

`src/Unit.cs` therefore emits only base units — `'K`, `'kg/('m*'s^2)` (Pa),
`'m^2/'s^2` (J/kg), `'m^2/'s^2/'K` (J/kg/K), `'kg*'m^2/('mol*'s^2)` (J/mol),
`'mol/'m^3`, `'m^3/'kg`, `'kg/'mol`, `'kg*'m/('s^3*'K)` (W/m/K), `'kg/'s^2`
(N/m), `'kg/('m*'s)` (Pa·s), `'m/'s`, `1/'K`, `'m*'s^2/'kg` (1/Pa) — with the
parser call wrapped in try/catch and latched off on the first failure. No
`FreshStore`, no per-lookup `SymbolicCalculation`, no SS-2414.

The parameter-name table is rebuilt from CoolProp's own `parameter_info_list`
(`src/DataStructures.cpp` in the CoolProp source) rather than copied, which
fixed three defects in the Mono version:

| Mono version | CoolProp source | consequence |
|---|---|---|
| `"V"` → specific volume | `"V"` → `iviscosity` (a backward-compat alias) | **viscosity results were tagged m^3/kg** |
| `"GMOLAR"` filed under J/mol **with a `[J/mol/K]` comment** | `Gmolar` is J/mol, `gas_constant` is J/mol/K | comment was wrong; the mapping was right |
| `dipole_moment` unmatched → dimensionless | `C-m` | a unit-ful value was returned unit-less |

Names absent from the table (humid-air virial coefficients `Bvir`/`Cvir`,
`dipole_moment`, the higher derivatives) deliberately get **no tag**: an untagged
result still works, a wrong tag does not. `tests/` scans every expression in
both tables and fails if any token other than `kg`, `m`, `s`, `mol`, `K` appears.

Also dropped entirely: the Mono version's input-unit *validation* (`Unit.match`
/ `matchHA`, which threw `Errors.UnitsDontMatch`). Because `BaseEntry.Value` is
always SI base units, `3*'MPa` and `3000000` arrive identically as `3e6`; the
check added no safety and could reject a correct input. `PsychroLib` and `IF97`
do not validate either.

### 2.2 Native binding — `src/CoolPropEngine.cs` (new file)

The Mono plugin declared every entry point **twice** (`DllImport("CoolProp_x86")`
and `DllImport("CoolProp_x64")`) and switched on `IntPtr.Size`. This port keeps
that shape, for a new reason: the two Windows engines ship side by side **at the
package root** (file names are the only way to tell them apart, and SMath's
installer rejects `win32\`/`win64\` subfolder layouts — see §1), and the CLR
binds a `DllImport` by module base name. Each entry point is therefore declared
once per engine name, behind a one-line dispatcher that picks the pair by
`IntPtr.Size`; the engine itself is loaded by absolute path before the first
call.

Each declaration still uses a plain `EntryPoint` + `CallingConvention.StdCall`,
which binds because of how the engines name things:

* The i686 engine exports **only decorated names** — `_PropsSI@32`,
  `_PhaseSI@36`, … — as parsed from its export directory: 71 exports, 0
  undecorated. (A raw byte search for `PropsSI` finds only 3 of the 11 symbols,
  which is misleading; the export table is authoritative.)
* The x86-64 engine exports the same 71 names **plain** — the Win64 ABI has no
  name decoration — and the Linux engine exports undecorated cdecl names.
* The CLR's stdcall resolver probes the exact name, then `name@<n>`, then
  `_name@<n>`, where `<n>` is derived from the **managed** signature. Mono
  ignores the calling convention on ELF and matches by name. (On x64 the exact
  name matches immediately; the `@<n>` forms only ever matter for the i686
  engine.)

So each declaration serves its engine — **provided the managed signature
matches the native one exactly**, so the CLR's computed `<n>` equals the i686
DLL's decoration. A wrong arity does not raise `EntryPointNotFoundException`
with a helpful message; it simply never matches. Four of the eleven functions
were initially wrong in this port and all four failed exactly that way.

All signatures are transcribed from `include/CoolProp/CoolPropLib.h`, and the
five functions that return `long` are declared **`int`** — MSVC `long` is 32
bits while LP64 `long` is 64, and the upper half of the return register is
undefined on x86. `tests/` asserts `get_param_index("P") == 20` and
`get_param_index("T") == 19` precisely to catch that.

Windows also needs the engine loaded explicitly: .NET Framework does not probe a
plugin's own directory for native dependencies, so a bare `DllImport` would
never find the engine inside the plugin folder. `CoolPropEngine.EnsureLoaded()`
picks `CoolProp_x64.dll` or `CoolProp_x86.dll` by `IntPtr.Size` and calls
`LoadLibraryA` on it from `GetTermsHandled` and from `Initialize`. It **throws a
readable exception** when the engine is missing or cannot be loaded — the
original silent return is what turned a bitness mismatch into a bare
`BadImageFormatException` (0x8007000B) from the first P/Invoke with no hint of
the cause. Under Mono, `packaging/CoolPropWrapper.dll.config` carries a
`<dllmap>` per engine name (`CoolProp_x86`, `CoolProp_x64`), both to
`libCoolProp.so`, resolved relative to the assembly, so no explicit load is
needed there.

### 2.3 API level — `src/CoolPropPlugin.cs`

`SMath.Math.Decision` is now marked **obsolete** in `SMath.Manager` 1.73.9126,
pointing at `SMath.Math.Computation` — which exists in the same assembly with
the identical `NumericCalculation` / `SymbolicCalculation` / `Preprocessing`
signatures. The plugin uses the non-obsolete `Computation`.

The Mono plugin's `SessionProfile` fallback — reflecting a `CurrentSessionProfile`
property out of `GlobalParams` — is gone: that property does not exist in
1.73.9126's `GlobalParams` at all. The live profile now arrives as the argument
of `GetTermsHandled(SessionProfile)` and is stored there, which is also the only
non-obsolete route to the UI language (the obsolete `GlobalParams.CurLang3Letter`
is used nowhere).

### 2.4 `MItem == null` — a real crash, fixed in two places

`MItem` overloads `op_Equality` and dereferences both operands. The Mono plugin
compares an `MItem` with `null` in `Unit.matchInternal` and in
`CoolProp_Props1` (`if (ResUnit == Unit.unitless)`), which throws
`NullReferenceException` rather than returning true. This port uses
`ReferenceEquals` in every `MItem` null check, and the test suite pins the rule
("unitless is null by design", "unknown parameter returns no unit") — those are
the tests that caught it here.

### 2.5 String transport

`UnescapeMathString` is kept verbatim from the Mono plugin (it decodes SMath's
`\NNNN\` character escapes, which the parser emits for `.` and `&` inside text
operands — without it, a mixture string like `R410A[0.5]&R410B[0.5]` reaches
CoolProp with the `&` mangled and no separator to split on). One documented
limitation is added: two *adjacent* escapes share a single delimiter backslash,
because the `\` that ends one escape also looks like the introducer of the next.
SMath emits each escape with its own pair, so the form does not occur in
practice, and the alternative reading would break a literal `\005B` text string.
`tests/` pins the behaviour rather than guessing at a "fix".

Also added: `GetStringParam` now checks the cast result with `ReferenceEquals`
before touching `isText`, so a matrix passed where a string is expected raises
`Errors.ArgumentMustBeString` instead of crashing.

### 2.6 `rc == 1` is success

Every CoolProp C-API function that writes into a buffer returns a code where
**1 means success** (0 means failure, and `CoolProp::set_error_string` carries
the message). `PhaseSI`, `get_fluid_param_string`,
`get_parameter_information_string` and `get_global_param_string` all check it and
throw `coolpropPlugin.CoolPropError()` with the retrieved message otherwise.

`get_parameter_information_string` additionally needs its output buffer **seeded
with the info type** ("IO", "short", "long", "units") — the same buffer is both
the lookup key and the write destination (`src/CoolPropLib.cpp` line 369). An
unseeded buffer returns an empty string with `rc == 1`, i.e. a silent wrong
answer. `tests/` asserts `("P", "long") == "Pressure"` to lock this in.

The double-returning functions instead report failure by returning `+Infinity`
(`_HUGE`), which `MakeDoubleResult` converts into an exception carrying the
native `errstring`.

### 2.7 `"RESET"` is a no-op — use `"DEF"` to restore the default

Worth knowing because it is the opposite of what the string's name suggests,
and because both engines behave the same way. `set_reference_stateS` applies a
datum by **adding** an offset to whatever is currently active:
`IdealHelmholtzEnthalpyEntropyOffset::set` (in `include/CoolProp/fluids/
Helmholtz.h`) only zeroes the offset when the ref name is exactly `"DEF"`;
every other name, including `"RESET"`, *increments*. Since `"RESET"` passes
`(0, 0)`, it leaves the active datum exactly where it was — it does not clear it.

Verified against the engine (`tests/` pins this): after `"ASHRAE"` puts h = s = 0
at saturated liquid −40 °C, `"RESET"` keeps those zeros and `"DEF"` restores the
library default (148 144.046 J/kg at that state for R134a). The plugin passes
both strings through unchanged — matching the upstream C API is the contract —
and README §11 tells a worksheet which one to use.

## 3. When a patch belongs here

Only for changes to a **third-party file** we must carry ourselves. This
repository contains none: `native/CoolProp_x86.dll` and `native/libCoolProp.so`
are byte-identical to their sources, `native/CoolProp_x64.dll` is built
unmodified from the pinned upstream source (recipe in §1 — a build, not a
patch), and everything under `src/` is either this repository's own code or a
transcription of a public C header.

To record one:

```bash
# 1. vendor the pristine file first
cp CoolProp-8.0.0/src/SomeFile.cpp .../native-src/
git add .../native-src/ && git commit -m "vendor CoolProp upstream"

# 2. edit, then capture the change
git diff -- .../native-src/SomeFile.cpp > patches/0001-some-fix.patch
```
