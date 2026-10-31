# Porting notes: SwarmFit → SMath Studio (.NET Framework 2.0 / CLR 2.0)

The upstream **SwarmFit** library (https://github.com/swharden/SwarmFit, by Scott
W. Harden, BSD-3-Clause) is the numerical engine behind this plugin. It targets
`netstandard2.0;net8.0` and is written in modern C# (C# 12). SMath Studio plugins
must target **.NET Framework 2.0** so the same DLL also loads under Mono on Linux,
which is far below what the upstream source needs. This document records every
change required to make the engine compile and run on CLR 2.0.

## Why we vendor and inline-compile instead of referencing SwarmFit.dll

The upstream `SwarmFit.dll` is a `netstandard2.0` assembly. On .NET Framework 2.0 /
Mono that drags in a chain of runtime-support packages that **do not exist on CLR 2.0**:

- `System.Memory` (for `Span<T>` / `AsSpan()`)
- `System.Buffers`
- `System.Numerics.Vectors`
- `System.Runtime.CompilerServices.Unsafe`

Referencing the DLL would therefore fail to load inside SMath Studio. Instead the
needed parts of SwarmFit are **vendored into `src/SwarmFit/` and compiled inline**
alongside the plugin (see `SwarmFitFitter.cs`, `Particle.cs`, `XorShift.cs`,
`ParameterLimits.cs`, `FitSolution.cs`, `IRandomNumberGenerator.cs`,
`FitFunction.cs`).

## Specific language/API changes

| Upstream (modern C# / netstandard2.0) | Ported form (net20) | Reason |
| --- | --- | --- |
| `Func<double, double[], double>` | `SwarmFit.FitFunction` delegate | `Func<,>` lives in `System.Core` (.NET 3.5); not on CLR 2.0. |
| `list.Select(...).ToArray()` (LINQ) | explicit `for` loops | `System.Linq` absent on net20. |
| `span.AsSpan().CopyTo(...)` | `Array.Copy(...)` | `Span<T>` requires `System.Memory`. |
| `readonly struct` + primary constructor (`ParameterLimits(double min, double max)`) | plain `struct` with explicit `Min`/`Max`/... fields and a normal ctor | primary constructors & `readonly struct` are C# 7.2+/8.0. |
| collection expressions `[.. parameters]` | explicit `Array.Copy` | C# 12. |
| `List<T>.LastOrDefault / Any / Min / Max` | hand-written loops | LINQ / newer `List<T>` members. |
| `string.Trim(char)` | hand-written `StripQuotes` index scanner | `String.Trim(Char)` only exists from .NET Core 2.1 / netstandard2.1; absent from the .NET Framework 4.x `mscorlib` the plugin runs on. Calling it throws `MissingMethodException` on Windows. |
| default `double.Parse` | `double.Parse(s, CultureInfo.InvariantCulture)` | guarantees `.` decimal separator regardless of host locale. |

## Architecture split (so the maths is testable)

SMath numeric/term types (`TNumber`, `TDouble`, `TMatrix`, `TermInfo`,
`ArgumentInfo`, `Entry`, `Store`) **dereference the host's `GlobalProfile`** and
throw `NullReferenceException` when constructed outside a running SMath Studio
session. That means any logic living in the SMath-facing adapter cannot be unit
tested from a plain console program.

To keep the numerical contract verifiable, the pure maths was extracted into
**`src/SwarmFitCore.cs`**:

- `StandardModel(name, out parameterCount)` — the 22 model catalogue
- `Fit(...)` — runs the swarm, computes SSE / RMSE / MaxResidual / R²
- `Predict`, `R2`, `Details`, `LinearBounds`

`SwarmFitCore` touches **no SMath type**. `SwarmFitEngine` / `SwarmFitPlugin` are
the SMath-only adapter and simply delegate into `SwarmFitCore`. The test harness
(`tests/Program.cs`) exercises `SwarmFitCore` directly, so the whole fit contract
is covered headlessly (see the test output: 55 checks, all passing).

## Behaviour preserved from upstream

- PSO weights unchanged: inertia `0.729`, local weight `1.49445`, global weight
  `1.49445`, particle-death probability `0.01`.
- The swarm **minimises the L1 residual** (`Σ|ŷ−y|`) during search, exactly as
  upstream; `swarmfit_details` then reports the L2 statistics (SSE, RMSE, R²) the
  user expects.
- `XorShift` keeps the upstream **fixed default seed** (`2463534242`), so a given
  fit is reproducible run-to-run (and between the Windows and Linux builds).
- Early-stagnation termination: the batch solver stops once relative improvement
  stays below `1e-5` for three batches, so good fits finish in milliseconds while
  hard ones still get the full iteration budget.

## What was deliberately dropped

- **All GUI code** in the upstream repository is ignored. SMath plugins are
  headless; the worksheet supplies the data and receives the result vectors.
- No native companion library — SwarmFit is pure managed code, so the package
  contains only `SwarmFitWrapper.dll` + manifests + metadata.

---

## SMath CAS Integration Fixes (v1.1.0.0)

1. **`StringComparer.Ordinal` for Variable Caching:**
   Changed the variable name cache dictionaries (`_matrixByName`, `_scalarByName`) from
   `StringComparer.OrdinalIgnoreCase` to `StringComparer.Ordinal`. In mathematical sheets,
   differently-cased variables (e.g. `t_dim` for time vs `T_dim` for temperature) must
   never alias.
2. **Context Preservation:**
   Propagated `Store context` across all recursive entry/definition evaluations in
   `ExtractDoubleArray` and `ExtractScalar`.
3. **Bounds Swapping:**
   Normalized $p_{\min}$ and $p_{\max}$ vectors automatically if inverted, avoiding solver
   crashes when modeling inverse gradients.
4. **Logarithmic Singularity Guard:**
   Protected `"log"` and `"logarithmic"` models by clamping non-positive $x \le 0$ to
   $10^{-12}$ to avoid $-\infty$ or NaN blowups at $t = 0$.
5. **Store-First Evaluation Priority:**
   Prioritized active worksheet `Store` definitions over static process-level caches in
   `ExtractDoubleArray` and `ExtractScalar`, preventing cross-worksheet variable pollution
   (e.g., shared variable names like `P_pred` between consecutive sheets).
