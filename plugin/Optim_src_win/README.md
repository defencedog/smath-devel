# OptimWrapper for SMath Studio

OptimWrapper exposes [OptimLib](https://github.com/kthohr/optim) to SMath
Studio: **13 optimisation algorithms** applied to **user-defined worksheet
expressions and functions**. There are no built-in test problems - you define
every objective, constraint and root system in SMath CAS. All values are unitless.

| | |
|---|---|
| Assembly | `OptimWrapper.dll` |
| Plugin GUID | `3c9f7e0a-c2b1-4eae-ab68-9ea2c518c510` |
| Version | 1.0.0.0 |
| Target | .NET Framework 2.0 (CLR 2.0) |
| Native ABI | 101 |
| Engines | `Optim_x86.dll`, `Optim_x64.dll`, `libOptim.so` (Linux aarch64) |
| Worksheet functions | 16 |
| Harness | 87 checks |

## Install

1. In SMath Studio, open **Tools > Plugins > Install...** and select
   `dist/OptimWrapper.1_0_0_0.zip`.
2. Restart SMath Studio.
3. The functions appear under **Optim Wrapper** in the function list.

Manual install: extract the zip into

```text
%APPDATA%\SMath\extensions\plugins\3c9f7e0a-c2b1-4eae-ab68-9ea2c518c510\1.0.0.0\
```

All package entries must remain **uncompressed (ZIP_STORED)**. SMath rejects a
deflated plugin zip; `tools/pack.py` enforces this.

## Quick start

```text
optim_bfgs(100*(v - u^2)^2 + (1 - u)^2, [u; v], [-1.2; 1])   // Rosenbrock -> [1; 1; ~0; iters; 1]
optim_lbfgs(100*(v - u^2)^2 + (1 - u)^2, [u; v], [0; 0])     // L-BFGS
optim_sumt([(u - 5)^2 + (v - 4)^2; -2*u - v + 14], [u; v], [0; 0])
optim_broyden([u^2 + v^2 - 1; u - v], [u; v], [0.7; 0.7])
optim_de(20 + (u^2 - 10*cos(2*π*u)) + (v^2 - 10*cos(2*π*v)), [u; v], [3; 3], [-5.12; -5.12], [5.12; 5.12])

optim_bfgs(x^2 + 4*y^2, [x; y], [1; 2])
optim_bfgs(x^2 + 4*y^2, [1; 2])                     // variables inferred
optim_solve_expr(0, x^2 + 4*y^2, [x; y], [1; 2])    // algorithm by id
optim_sumt([(x-5)^2 + (y-4)^2; -2*x - y + 14], [x; y], [1; 1])
optim_broyden([x^2 + y^2 - 1; x - y], [x; y], [1; 1])
optim_eval_expr(x^2 + 4*y^2, [x; y], [1; 2])        // 17
```

A worksheet function can be passed by name — bare is preferred:

```text
f1(u) := u^3 - u + 1
optim_broyden(f1, [0.5])
```

Both `optim_broyden(f1, x0)` and `optim_broyden("f1", x0)` resolve to the same
worksheet function and give identical results (the wrapper strips optional
surrounding quotes). Quoting is **not** required; it was only historically
recommended for plotted sheets, because older X-Y plot sheets scanned a bare
function name as a free variable. With inline plot series (Chart2D series-inline
law) that no longer applies.

## Result layout

Every solver returns a column vector with `n + 3` rows:

```text
[x1; x2; ...; xn; f; iterations; ok]
```

| Field | Meaning |
|---|---|
| `x1..xn` | Best point found, also when `ok = 0`. |
| `f` | Objective value; for `optim_sumt`, the objective; for root systems, the largest absolute residual. |
| `iterations` | Iterations reported by the algorithm (generations for DE/PSO). |
| `ok` | `1` = algorithm convergence test met, `0` = best guess. |

For stochastic methods `ok = 0` is common; inspect `f` and `x`, not `ok` alone.

## Functions

| Function | Arguments | Returns |
|---|---|---|
| `optim_eval_expr` | `expr, x` or `expr, vars, x` | Scalar or vector value at `x`. |
| `optim_algo_count` | - | Number of algorithms (13). |
| `optim_solve_expr` | `algo, expr, x0 [, max_iter]` or `algo, expr, vars, x0 [, max_iter]` | Generic expression solver (deterministic/SUMT/Broyden). |
| `optim_bfgs` | `expr \| fn, x0 [, max_iter]` or `expr \| fn, vars, x0 [, max_iter]` | BFGS quasi-Newton. |
| `optim_lbfgs` | same as `optim_bfgs` | L-BFGS. |
| `optim_cg` | same | Conjugate gradient. |
| `optim_gd` | same | Gradient descent. |
| `optim_newton` | same | Newton with numerical Hessian. |
| `optim_nm` | same | Nelder-Mead simplex. |
| `optim_de` | `expr \| fn, vars, x0, p_min, p_max [, max_iter]` | Differential evolution (bounded). |
| `optim_de_prmm` | same | DE with pro-active mutation (bounded). |
| `optim_pso` | same | Particle swarm (bounded). |
| `optim_pso_dv` | same | Particle swarm with differential variation (bounded). |
| `optim_sumt` | `expr \| fn, x0 [, max_iter]` or `expr \| fn, vars, x0 [, max_iter]` | SUMT constrained solver. |
| `optim_broyden` | `expr \| fn, x0 [, max_iter]` or `expr \| fn, vars, x0 [, max_iter]` | Broyden root finding. |
| `optim_broyden_df` | same as `optim_broyden` | Broyden dual-form root finding. |

**There are no built-in test problems.** Every objective, constraint and root
system is a user-defined SMath CAS expression or worksheet function.

`max_iter` defaults to `2000`. For DE/PSO it is the number of generations; the
population remains OptimLib's default of 200.

## Expression form

The first argument selects the call form:

- a **worksheet function name** (bare or quoted) uses that function's body and
  formal parameters, e.g. `optim_broyden(f1, x0)` (or `optim_broyden("f1", x0)`);
- anything else is a **worksheet expression**, e.g.
  `optim_bfgs(x^2 + 4*y^2, [x; y], x0)`.

For three-argument calls, a **matrix third argument** is `x0`, while a **scalar
integer third argument** is `max_iter`:

```text
optim_bfgs(expr, [x; y], [1; 1])    // explicit variables
optim_bfgs(expr, [1; 1], 500)       // x0 and max_iter
```

Use the four-argument form when the call is ambiguous:

```text
optim_bfgs(expr, [x; y], [1; 1], 500)
```

The expression shape must match the algorithm kind:

| Kind | Expression must return | Example |
|---|---|---|
| Objective (`bfgs`, `lbfgs`, `cg`, `gd`, `newton`, `nm`) | One scalar | `x^2 + 4*y^2` |
| Constrained (`sumt`) | Column `[f; c1; ...; cm]`, `m >= 1` | `[(x-5)^2 + (y-4)^2; -2*x - y + 14]` |
| Root (`broyden`, `broyden_df`) | Column `[r1; ...; rm]`, normally `m = n` | `[x^2 + y^2 - 1; x - y]` |

For `optim_sumt`, constraints use `ci(x) <= 0`. Root systems must be square
(`m = n`); rectangular systems are rejected. A matrix argument may contain any
number of rows.

### Variables

The variable vector is an ordinary SMath column such as `[x; y]`. If it is
omitted, free operands are collected in order of first appearance. Defined
worksheet names, constants, units and strings are not free variables. Explicit
variables are recommended because an already-defined name is otherwise treated
as a constant.

Expressions are evaluated by SMath's numeric engine after substituting the
current point. Worksheet functions, constants and previously defined names work
as usual. Values must be unitless and numeric. Optimisers call the expression
repeatedly, so cheap formulas are preferable for large problems.

### Limitations

- Expression-form DE/PSO are not exposed: the native API supports bounds, but
  the worksheet API does not accept a bounds argument. Use a deterministic
  algorithm for expressions, or a built-in function id for DE/PSO.
- `sumt` uses finite differences for constraints.
- Root systems are dynamically sized but must be square. Memory grows as
  `O(n^2)` for the Broyden inverse Jacobian.
- For plotted sheets, keep optimizer variables distinct from the plot's `x`/`y`
  and write the plotted series inline (Chart2D series-inline law); pass worksheet
  functions by bare name (quoting is optional).

## Algorithms

| Id | Name | Kind | Notes |
|---|---|---|---|
| 0 | `bfgs` | objective | Quasi-Newton with line search; robust default. |
| 1 | `lbfgs` | objective | Limited-memory BFGS. |
| 2 | `cg` | objective | Conjugate gradient. |
| 3 | `gd` | objective | Gradient descent; fixed step can diverge. |
| 4 | `newton` | objective | Numerical Hessian. |
| 5 | `nm` | objective | Derivative-free simplex. |
| 6 | `de` | objective | Global; population 200; bounds required. |
| 7 | `de_prmm` | objective | DE with pro-active mutation; bounds required. |
| 8 | `pso` | objective | Global; population 200; bounds required. |
| 9 | `pso_dv` | objective | PSO with differential variation; bounds required. |
| 10 | `sumt` | constrained | Constraint problems only. |
| 11 | `broyden` | root | Root systems only. |
| 12 | `broyden_df` | root | Root systems only. |

Mixing kinds is an error: `optim_sumt` on an objective, or `optim_bfgs` on a
constrained/root problem, raises an evaluation error. The global stochastic
algorithms (de, de_prmm, pso, pso_dv) take explicit box bounds as arguments.

## User-defined problems

Every problem is defined in SMath CAS - the plugin ships **no built-in test
problems** (the old id-based surface was removed):

| Kind | Expression must return | Example |
|---|---|---|
| Objective (`bfgs`, `lbfgs`, `cg`, `gd`, `newton`, `nm`) | one scalar | `100*(v-u^2)^2 + (1-u)^2` |
| Global objective (`de`, `de_prmm`, `pso`, `pso_dv`) | one scalar + `p_min`/`p_max` | Rastrigin bounded to [-5.12, 5.12] |
| Constrained (`sumt`) | column `[f; c1; ...; cm]`, `ci <= 0` | `mat((u-5)^2+(v-4)^2, -2*u-v+14, 2, 1)` |
| Root (`broyden`, `broyden_df`) | column `[r1; ...; rm]`, `m = n` | `mat(u^2+v^2-1, u-v, 2, 1)` |

Root solvers return the largest absolute residual in the `f` slot.

## Build and test

Prerequisites: .NET SDK, Python 3, and the extracted net20/SMath reference
assemblies. Native rebuilds additionally need OptimLib, Eigen, and MinGW-w64
(i686 and x86-64) or an aarch64 Linux cross toolchain.

```powershell
# managed plugin + 81-check harness + repackage (engines already present)
.\build.ps1 -PackagesSource ..\nuget-extracted -Dotnet ..\dotnet\dotnet.exe

# also rebuild both Windows engines
.\build.ps1 -BuildNative -OptimSource ..\optim-master `
            -EigenPath ..\_odeps\eigen-3.4.0 `
            -Dotnet ..\dotnet\dotnet.exe
```

The harness exercises the real engines for both bitnesses. Expected final line:

```text
=== ALL TESTS PASSED (81/81) ===
```

`build.ps1` rebuilds the managed DLL as AnyCPU after the x86 test run and
verifies the COR flags before packaging, so the plugin loads in 64-bit SMath.
Native source pins and rebuild recipes are in `patches/README.md`.

## Package

`build.ps1` stages the plugin and engines, then writes
`dist/OptimWrapper.1_0_0_0.zip` with version taken from
`packaging/OptimWrapper.dll.ini`.

| | |
|---|---|
| Package | `OptimWrapper.1_0_0_0.zip` |
| Size | 8,440,440 bytes |
| MD5 | `413B75AC368803A34BFBE65C4E10DA86` |
| SHA-256 | `FE7E45325963DB0B5C06CBF02ED9AA7D382AB9A3F57A2A04A48EC78C847E9352` |
| Entries | 11, all STORED |

| Entry | Purpose |
|---|---|
| `OptimWrapper.dll` | Managed plugin. |
| `OptimWrapper.dll.config` | Mono `<dllmap>` entries. |
| `OptimWrapper.dll.ini` | Plugin manifest. |
| `Optim_x86.dll`, `Optim_x64.dll` | Windows engines. |
| `libOptim.so` | Linux aarch64 engine. |
| `metadata\OptimWrapper.en.json`, `.ru.json` | Localised descriptions. |
| `LICENSE`, `NOTICE`, `LICENSE-Eigen.txt` | Licence notices. |

All entries are STORED. The package is self-contained except for SMath's host
assemblies.

## Troubleshooting

| Symptom | Cause / fix |
|---|---|
| Functions do not appear | Ensure `config.1_5_0_9678.ini` is one level above `1.0.0.0\` and lists `OptimWrapper.dll`; restart SMath. |
| `DllNotFoundException: Native engine not found` | The package is missing an engine or it is not next to `OptimWrapper.dll`. |
| `BadImageFormatException` / `0x8007000B` | Engine bitness does not match the host, or the managed DLL is not AnyCPU. Re-run `build.ps1`. |
| `Unsupported Optim engine ABI ... expects 101` | Managed DLL and engines are from different builds. Replace all together. |
| `Expression has N free variable(s)` | Inference found the wrong count. Pass the variable vector explicitly. |
| Objective/root shape error | Use one scalar for objectives, `[f; constraints]` for SUMT, and square residuals for Broyden. |
| X-Y plot hangs or reports `Pair (f, v) must have an explicit form.` | Series-Inline Law violation: do not pass a bare named variable (e.g. `s1_pt := mat(...)`) referencing solver outputs 2+ levels deep. Write the marker inline: `mat(el(res, 1), y, 1, 2)`. Ensure `plotmethod="Lines"` (`Points` is invalid). Keep optimizer variables distinct from plot sweep variable `x`. |
| `ArgumentException: Expected a number or a vector, got '-'` in `optim_eval_expr` | Inline matrix literal context bypass trap: passing `mat(...)` directly into a plugin call bypasses SMath CAS evaluation. Define a named variable first: `x_solv := mat(...)`, then pass `optim_eval_expr(..., x_solv)`. |
| `ok = 0` | Usually a stochastic best guess. Inspect `f` and `x`; increase `max_iter`. |
| Linux/Mono load error | `libOptim.so` is aarch64. Build a matching library for other Linux architectures. |

### Visualization with Chart2DLib (`<xyplot>`): Marker & Landscape Pitfalls

When visualizing optimization landscapes, residual basins, or solved optimum markers using `Chart2DLib` (`<xyplot>`, GUID `c12231ec-4873-43c1-a7d0-a167ebd17066`), observe these three mandatory rules:

1. **The Series-Inline Law (Evaluation-Store Depth Trap):**
   `XYPlotRegion.dll` evaluates each `sys(...)` trace against a shallow local store context that only resolves operands written directly in the plot `<input>` plus ~one level of dependencies.
   - If a marker is declared as a bare intermediate variable, e.g. `s1_pt := mat(v_opt, 20, 1, 2)` where `v_opt := el(res_bfgs, 1)` and `res_bfgs := optim_bfgs(f_obj, s_v, x0_v)`:
     The shallow store context fails to resolve `res_bfgs`, falling back to analytical evaluation. It encounters the unresolved solver symbols `f_obj` and `v`, throwing `lastError="Pair (f_obj, v) must have an explicit form."`.
   - **Fix:** Always write computed markers **inline** directly inside the plot input:
     `mat(el(res_bfgs, 1), 20.0, 1, 2)` or `augment(...)`.
2. **Valid `PlotMethod` Enum (`"Points"` is Invalid):**
   `LineStyle.PlotMethod` in `Chart2DLib` is strictly an enum of four values: `Lines`, `Splines`, `Labels`, `Shapes`.
   - `"Points"` is **not** a valid enum member and is rejected or ignored.
   - To draw point markers or scatter points, use `methods=["Lines"]` with `symbols=["Circle"]` (or `"Diamond"`, `"Square"`). A single-point matrix (`mat(x, y, 1, 2)`) drawn with `Lines` cannot form line segments, so `Chart2DLib` renders **exclusively the symbol marker**.
3. **Solver Variable vs Plot Sweep Variable (`x`):**
   `Chart2DLib` reserves variable `x` as its independent sweep axis for 1-variable analytical functions (`f(x)`).
   - Ensure the solver variable (`v`, `u`, `w`) and any worksheet data arrays (e.g. mole fractions `x_mol`) do not use `x`. Leaving `x` unbound allows `Chart2DLib` to evaluate the 1-var landscape `f_obj(x)` continuously.

## Native engines

| File | Target | Size | MD5 |
|---|---|---|---|
| `Optim_x86.dll` | i686-w64-mingw32, GCC 12.4 | 3,083,637 B | `57D842333A8969DF12C0EC4A90425A4C` |
| `Optim_x64.dll` | x86_64-w64-mingw32, GCC 16.2 | 3,355,751 B | `E0E82A1B70495FDD33A7334ECC4FAC22` |
| `libOptim.so` | aarch64-linux-gnu, GCC 11.4 | 1,931,768 B | `DABC319AFD5446C466C07B9458A3081D` |

The engines export the same 16 plain `optim_*` symbols (ABI 101): the 15 v1
functions plus `optim_solve_custom`. DE/PSO are seeded with 42 for reproducible
runs. The Windows DLLs import only KERNEL32 and the CRT; the `.so` depends only
on libm, libc and the aarch64 loader.

The custom callback ABI is:

```c
typedef int (*optim_eval_cb)(const double* x, int n,
                             double* out, int out_len, void* user);
```

`optim_solve_custom` returns `0` success, `1` algorithm failure with a best
point, `2` invalid arguments, and `3` callback/native exception. `out_len` is
`1` for an objective, `m + 1` for `[f; constraints]`, and `m` for a residual
system.

## Licence

OptimWrapper follows Apache License 2.0. OptimLib and BaseMatrixOps are
Copyright (C) 2016-2023 Keith O'Hara, Apache License 2.0. Eigen is primarily
MPL2; see `LICENSE-Eigen.txt` and `NOTICE`.
