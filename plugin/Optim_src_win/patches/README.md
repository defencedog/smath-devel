# Native engines - provenance, builds, shim notes

The package ships three prebuilt engines at its root:

| file | triple | built with | size | MD5 |
|---|---|---|---|---|
| `native/Optim_x86.dll` | i686-w64-mingw32 | GCC 12.4 (`C:\mingw32`) | 3,083,637 B | `57D842333A8969DF12C0EC4A90425A4C` |
| `native/Optim_x64.dll` | x86_64-w64-mingw32 | w64devkit GCC 16.2 | 3,355,751 B | `E0E82A1B70495FDD33A7334ECC4FAC22` |
| `native/libOptim.so` | aarch64-linux-gnu | GCC 11.4 cross (Ubuntu 22.04) | 1,931,768 B | `DABC319AFD5446C466C07B9458A3081D` |

Sizes and hashes change whenever `optim_shim.cpp` or the OptimLib patch changes -
rebuild the affected engines and update this table. All three engines include the
`m == n` root-system guard in `optim_shim.cpp`; the aarch64 guard was verified
under qemu (rectangular input returns rc 2).

## Source pins

These are workspace inputs, not checked into this tree:

- **OptimLib 3.1.3** - local build source `..\optim-master\` (kthohr/optim
  archive, Apache-2.0, Copyright (C) 2016-2023 Keith O'Hara).
  Upstream: https://github.com/kthohr/optim
  The aarch64 build cloned `master` at commit
  `5453f48aca695e6fef123677f35cb4d22e356e73` (2024-04-28), which is also 3.1.3.
- **BaseMatrixOps** - `..\_odeps\bmo\BaseMatrixOps-main\`, extracted from
  `..\_odeps\BaseMatrixOps-master.zip` (60,466 B, sha256
  `5A8A005FDEB28B92D0AEFB1C5BC86248FB8C3FCD33451B9CAA7FBF1C167EAC9B`).
  Upstream: https://github.com/kthohr/BaseMatrixOps (Apache-2.0).
  It must be visible as
  `optim-master\include\BaseMatrixOps\include\BaseMatrixOps.hpp`; copy the
  extracted `BaseMatrixOps-main` content into `optim-master\include\BaseMatrixOps\`.
  The aarch64 build cloned commit
  `9e5beec15d803f0429dadae5e9218ef907271383` (2023-04-29) into
  `optim-src/include/BaseMatrixOps/`.
- **Eigen 3.4.0** - `..\_odeps\eigen-3.4.0\`, from
  `..\_odeps\eigen-3.4.0.zip` (3,704,940 B, sha256
  `EBA3F3D414D2F8CBA2919C78EC6DAAB08FC71BA2BA4AE502B7E5D4D99FC02CDA`).
  Upstream: https://gitlab.com/libeigen/eigen (MPL2).

## Upstream patch: pso_dv stagnation reset

`src/unconstrained/pso_dv.cpp`, in the stagnation handler, mixes a column vector
with a row vector and aborts on an Eigen assertion whenever a particle stagnates
(so `pso_dv` crashed on every bounded problem):

```diff
-                P.row(i) = P_min + BMO_MATOPS_HADAMARD_PROD(rand_vec, (P_max - P_min));
+                P.row(i) = P_min + BMO_MATOPS_HADAMARD_PROD(BMO_MATOPS_TRANSPOSE(rand_vec), (P_max - P_min));
```

`rand_vec` is `ColVec_t(n_vals)`; `P_max - P_min` is `RowVec_t`. The transpose
makes both operands `1 x n`. This is the only source edit to OptimLib; re-apply
it after unpacking a fresh archive.

## Windows build (x86 + x64)

`native/build-native.ps1` generates one translation unit per engine and compiles
it once:

```
_build_x64/optim_all.cpp
    #include "optim-master/src/bfgs.cpp"
    #include "optim-master/src/lbfgs.cpp"
    ... every src/**/*.cpp ...
```

```powershell
g++ -std=c++17 -O2 -shared `
    -DOPTIM_ENABLE_EIGEN_WRAPPERS -DOPTIM_DONT_USE_OPENMP `
    -I <optim>\include -I <eigen> `
    native/optim_shim.cpp _build_x64/optim_all.cpp `
    -o Optim_x64.dll `
    -static -static-libgcc -static-libstdc++ -Wl,--no-undefined
```

- **Single TU is required in practice**: Eigen + OptimLib templates recompile in
  every `.cpp`, so a per-file strategy multiplies build time by the number of
  sources. One TU takes a few minutes per engine.
- `OPTIM_DONT_USE_OPENMP` keeps the engine single-threaded (no `libgomp`
  dependency, deterministic inside the SMath host).
- Static libgcc/libstdc++ mean the DLLs import only KERNEL32 + msvcrt/ucrt.
- All 16 exports are plain, undecorated names on every architecture (verified
  with `objdump -p` / `readelf --dyn-syms`), hence `CallingConvention.Cdecl` in
  the wrapper.
- `build-native.ps1` picks the output name from `g++ -dumpmachine`; check the
  `^x86_64` branch first, because `x86_64-w64-mingw32` also ends in `mingw32`.

Commands used for this tree:

```powershell
# x64
.\native\build-native.ps1 -OptimSource ..\optim-master `
    -EigenPath ..\_odeps\eigen-3.4.0 `
    -Toolchain ..\_toolchains\w64devkit -OutputDir .\native

# x86
.\native\build-native.ps1 -OptimSource ..\optim-master `
    -EigenPath ..\_odeps\eigen-3.4.0 `
    -Toolchain C:\mingw32 -OutputDir .\native
```

## Linux build (aarch64)

`native/libOptim.so` is the same shim cross-built on a Linux x64 host with
`aarch64-linux-gnu-g++` 11.4:

```bash
mkdir -p ~/smath/Optim && cd ~/smath/Optim
git clone --depth 1 https://github.com/kthohr/optim.git optim-src
git clone --depth 1 https://github.com/kthohr/BaseMatrixOps.git bmo-src
cp -r bmo-src/. optim-src/include/BaseMatrixOps/
unzip -q eigen-3.4.0.zip
# apply the pso_dv transpose patch, copy in optim_shim.cpp, and generate the
# single-TU list:
find optim-src/src -name '*.cpp' | sort | \
    sed 's|^|#include "|; s|$|"|' > optim-src/optim_all.cpp
cd optim-src
aarch64-linux-gnu-g++ -std=c++17 -O2 -fPIC -shared \
    -DOPTIM_ENABLE_EIGEN_WRAPPERS -DOPTIM_DONT_USE_OPENMP \
    -I include -I ../eigen-3.4.0 \
    optim_shim.cpp optim_all.cpp -o ../libOptim.so \
    -static-libgcc -static-libstdc++ -pthread -Wl,--no-undefined
aarch64-linux-gnu-strip --strip-unneeded ../libOptim.so
```

`readelf -d` shows only `libm.so.6`, `libc.so.6` and the aarch64 loader (no
libgomp/libstdc++ dependency); `readelf --dyn-syms` shows the same 16 plain
`optim_*` exports as the DLLs, so Mono's `<dllmap>` from `Optim_x86`/`Optim_x64`
to `./libOptim.so` binds by exact name. Only this aarch64 `.so` is shipped; an
x86-64 Linux engine must be built on such a host with the same command.

Runtime check on the x64 host via qemu-user (the test binary is not shipped).
Copy `tests/smoke_linux.cpp` into the build directory first:

```bash
aarch64-linux-gnu-g++ -std=c++11 -O1 smoke_linux.cpp -L. -lOptim \
    -Wl,-rpath,'$ORIGIN' -o smoke_linux
LD_LIBRARY_PATH=$PWD qemu-aarch64-static -L /usr/aarch64-linux-gnu ./smoke_linux
```

`tests/smoke_linux.cpp` exercises the metadata, BFGS on Rosenbrock, SUMT on the
constrained quadratic, Broyden on root system 1 and PSO on Rastrigin; the last
line is `=== ALL TESTS PASSED ===`.

## Shim ABI

ABI version is **101**.

`native/optim_shim.cpp` exports 16 plain C functions: `optim_api_version`,
`optim_library_name`, `optim_last_error`, `optim_algo_count`, `optim_algo_kind`,
`optim_algo_name`, `optim_testfn_count`, `optim_testfn_kind`, `optim_testfn_dim`,
`optim_testfn_bounded`, `optim_testfn_lower`, `optim_testfn_upper`,
`optim_testfn_name`, `optim_testfn_value`, `optim_solve` and
`optim_solve_custom`.

`optim_solve` takes `(algo, fn, n, x0, x_out, f_out, iter_out, err_out,
max_iter, seed)` and returns `0` on success, `1` when the algorithm reports
failure but still produced a best point, `2` for invalid arguments and `3` for a
native exception. `seed` defaults to 42 in the wrapper.

`optim_solve_custom` takes
`(algo, kind, n, m, x0, x_out, f_out, iter_out, err_out, max_iter, seed,
lb, ub, bounded, eval, user)` and uses the same return codes. `kind` is `0`
objective, `1` constrained (`sumt`) or `2` root system. The managed bridge
passes `m = 0` for objectives, `m = constraints` for kind 1 and `m = residuals`
for kind 2. The algorithm kind must match `kind`; kind 2 additionally requires
`m == n` (Broyden has no least-squares fallback) and is rejected otherwise.

The callback is:

```c
typedef int (*optim_eval_cb)(const double* x, int n,
                             double* out, int out_len, void* user);
```

`out_len` is `1` when the native code wants only the objective, `m + 1` for a
`[f; c1; ...; cm]` constrained payload, and `m` for a residual system. Return
non-zero to abort; the solve then returns `3` and reports the failure through
`optim_last_error`. Gradients, constraint Jacobians and Hessians are computed by
the shim with central differences, so the callback only supplies values.

Bounds are only meaningful for objective problems solved by DE/PSO
(`algo` 6-9); the shim rejects bounds for SUMT/root problems and rejects
unbounded DE/PSO. The SMath expression API does not expose a bounds argument,
so custom DE/PSO are rejected by the managed facade; the native entry point is
still available to C/C++ callers.

## Shim behaviour worth remembering

- **Test functions are reimplemented in `optim_shim.cpp`** (analytic values and,
  where available, analytic gradients); OptimLib's `tests/test_fns/*.hpp` are
  not compiled in. Ids 0-4 and 10-12 have analytic gradients; ids 5-9 use
  central differences.
- **Bounds**: only DE/PSO use a bounded search (`vals_bound` + lower/upper). The
  initial population is a 25 %-of-box window around `x0`, and the stochastic
  objective-change tolerance is `1e-6` so a settled run reports success.
- **Generations**: OptimLib's DE/PSO ignore `iter_max`; the shim maps the API's
  `max_iter` to `de_settings.n_gen` / `pso_settings.n_gen` (`n_pop` stays 200).
- **Newton** uses a numerical Hessian (central differences; the diagonal uses a
  second difference because the four-point cross formula cancels when i == j).
- **Newton's early return** (gradient already below tolerance) skips
  `error_reporting`, leaving `settings.opt_iter/opt_error_value` uninitialised;
  the shim zeroes those fields after constructing `algo_settings_t`.
- Seed is fixed at 42, so `de`/`pso` runs are reproducible across hosts.
- Root solvers return the largest absolute residual in `f_out`; there is no
  scalar objective for kind-2 functions.
