# patches/

Diffs against the upstream VIPhreeqc source, if and when any are needed.

## Current state: empty (intentionally)

`native/build-native.ps1` compiles `VIPhreeqc-master/` **without modifying a
single line** — for both bitnesses: GCC 12.4 (i686, `C:\mingw32`) produces
`IPhreeqc_x86.dll` and GCC 16.2 (x86_64, w64devkit) produces `IPhreeqc_x64.dll`
with the same flags. Getting a clean GCC build came down to a configuration
choice rather than a source edit:

| Symptom | Cause | Resolution |
|---|---|---|
| `invalid conversion from 'double (*)(…, char*, size_t)' to 'double (*)(…, const char*, size_t)'` at `IPhreeqc.cpp:936` | `IPHREEQC_NO_FORTRAN_MODULE` selects an overload that passes `char*` where `const char*` is expected. MSVC allows it silently; GCC rejects it. | Leave `IPHREEQC_ENABLE_MODULE=ON` (the default), which selects the `const char*` overload at `IPhreeqc.cpp:939`. No source change. |
| CMake configure fails downloading GoogleTest | `BUILD_TESTING` defaults ON and `FetchContent` pulls googletest over the network | `-DBUILD_TESTING=OFF` |
| DLL depends on `libstdc++-6.dll`, `libgcc_s_dw2-1.dll`, `libwinpthread-1.dll` | default dynamic runtime | `-DCMAKE_SHARED_LINKER_FLAGS="-static -static-libgcc -static-libstdc++"` |

## When a patch belongs here

Only for changes to an **upstream file** that we must carry ourselves — for
example if a future VIPhreeqc release renames an accessor, or if a compiler
upgrade rejects something that cannot be fixed with a flag.

`src/PhreeqcWrapper.cs` is **not** a patch. It is our own code, a rewrite of the
Mono/Linux wrapper to be platform-aware.

## How to make one

```bash
# 1. vendor the pristine upstream file into git first
cp VIPhreeqc-master/src/IPhreeqc.cpp native/upstream/IPhreeqc.cpp
git add native/upstream && git commit -m "vendor VIPhreeqc 3.8.7"

# 2. edit the upstream file, then capture the change
git diff -- native/upstream/IPhreeqc.cpp > patches/0001-iphreeqc-some-fix.patch

# 3. revert the working tree so the patch is the only record
git checkout -- native/upstream/IPhreeqc.cpp
```

## How to apply

```bat
git apply --verbose patches\*.patch
```

`build-native.ps1` does **not** apply patches automatically. The build is
expected to work from the pristine upstream checkout plus the CMake flags above;
a patch that silently fails to apply is worse than no patch.
