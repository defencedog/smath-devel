# SwarmFitWrapper — Particle Swarm Optimization Curve Fitting Plugin for SMath Studio

**SwarmFitWrapper** brings robust, derivative-free global non-linear curve fitting to
SMath Studio worksheets via **Particle Swarm Optimization (PSO)**, wrapping the
standalone C# library **SwarmFit** (by Scott W. Harden, BSD-3-Clause).

Like the PhreeqcWrapper, this plugin is **unitless**: every input (`xs`, `ys`,
parameter bounds) and every output (parameter vectors, predictions, R², diagnostics)
is a plain scalar or vector of real numbers. The plugin performs **no unit tagging** —
pass unitless data, or strip units first with `sv(x) := x * (UnitsOf(x)^(-1))`.

| | |
|---|---|
| **Engine** | SwarmFit (C#, vendored & inline-compiled) |
| **Platforms** | Windows (.NET Framework 2.0) and Linux (Mono) — same DLL |
| **Target framework** | .NET Framework 2.0 / CLR 2.0 |
| **Functions registered** | 10 functions across **17 signatures** |
| **Built-in models** | 22 name aliases |
| **Assembly version** | 1.1.0.0 |
| **Install GUID** | `b0a439e5-20d5-46a3-bcb5-596cbd1d9934` (same as the Linux/Mono build) |
| **License** | MIT (wrapper) · BSD-3-Clause (SwarmFit engine) |

---

## 1. Complete Function Reference

| Function | Arity | Arguments | Returns | Description |
| :--- | :---: | :--- | :--- | :--- |
| `swarmfit` | 5 | `xs, ys, model, p_min, p_max` | Vector (P×1) | Fit parameters with default swarm (20 particles, 5000 iters). |
| `swarmfit` | 7 | `xs, ys, model, p_min, p_max, particles, max_iter` | Vector (P×1) | Fit with explicit particle count and iteration budget. |
| `swarmfit_predict` | 3 | `xs, model, p` | Vector (N×1) | Evaluate ŷᵢ = f(xᵢ, **p**). |
| `swarmfit_details` | 5 | `xs, ys, model, p_min, p_max` | Matrix (5×1) | `[SSE; RMSE; MaxResidual; Iterations; Improvements]`. |
| `swarmfit_details` | 7 | `xs, ys, model, p_min, p_max, particles, max_iter` | Matrix (5×1) | Same diagnostics with custom swarm settings. |
| `swarmfit_r2` | 2 | `ys, y_pred` | Scalar | Coefficient of determination R² = 1 − SSE/SST. |
| `swarmfit_linear` | 2 | `xs, ys` | Vector (2×1) | Convenience fit of y = a·x + b → `[a; b]`. |
| `swarmfit_exp` | 4, 6 | `xs, ys, p_min, p_max [, particles, max_iter]` | Vector (3×1) | y = a + b·exp(c·x) → `[a; b; c]`. |
| `swarmfit_expdecay` | 4, 6 | `xs, ys, p_min, p_max [, particles, max_iter]` | Vector (2×1) | y = a·exp(−x/b) → `[a; b]`. |
| `swarmfit_power` | 4, 6 | `xs, ys, p_min, p_max [, particles, max_iter]` | Vector (2×1) | y = a·xᵇ → `[a; b]`. |
| `swarmfit_gauss` | 4, 6 | `xs, ys, p_min, p_max [, particles, max_iter]` | Vector (3×1) | y = a·exp(−(x−b)²/(2c²)) → `[a; b; c]`. |
| `swarmfit_poly` | 5, 7 | `xs, ys, degree, p_min, p_max [, particles, max_iter]` | Vector ((d+1)×1) | Polynomial of degree d (1 ≤ d ≤ 6) → `[p0; p1; …; pd]`. |

`model` is either a **quoted built-in name** (e.g. `"mm"`, `"haldane"`) or a
**user-defined SMath function**. The 5- and 7-argument forms of `swarmfit`/
`swarmfit_details` and the 4- and 6-argument forms of the convenience wrappers differ
only by the trailing `particles` and `max_iter` arguments.

### Example

```smath
xs := [1; 2; 3; 4; 5]
ys := [2.5; 4.5; 6.5; 8.5; 10.5]        // y = 2x + 0.5 + noise
p  := swarmfit(xs, ys, "linear", [0; -10], [10; 10])
p                                   // ~ [2.0; 0.5]
swarmfit_predict(xs, "linear", p)    // fitted curve
swarmfit_r2(ys, swarmfit_predict(xs, "linear", p))   // ~ 1.0
```

---

## 2. Built-in Standard Model Catalog

When `model` is a string, SwarmFit evaluates the curve natively in C# (no CAS
overhead). Every alias resolves to a model with a fixed parameter count:

| Model name | Formula | Params | Parameters |
| :--- | :--- | :---: | :--- |
| `"linear"` / `"line"` | y = p₁·x + p₂ | 2 | p₁ slope, p₂ intercept |
| `"exp"` / `"expdec1"` | y = p₁ + p₂·exp(p₃·x) | 3 | baseline, amplitude, rate |
| `"expdecay"` / `"expdecay1"` | y = p₁·exp(−x/p₂) | 2 | initial value, time constant τ |
| `"power"` | y = p₁·x^p₂ | 2 | prefactor, exponent |
| `"poly2"` | y = p₁·x² + p₂·x + p₃ | 3 | quadratic coefficients |
| `"poly3"` | y = p₁·x³ + p₂·x² + p₃·x + p₄ | 4 | cubic coefficients |
| `"gauss"` / `"gaussian"` | y = p₁·exp(−(x−p₂)²/(2p₃²)) | 3 | height, centre μ, width σ |
| `"hill"` / `"sigmoid"` | y = p₁ + (p₂−p₁)/(1+(p₃/x)^p₄) | 4 | y₀, y_max, K, n |
| `"mm"` / `"michaelis_menten"` | y = p₁·x/(p₂+x) | 2 | V_max, K_m |
| `"log"` / `"logarithmic"` | y = p₁ + p₂·ln(x) | 2 | offset, slope (safe for x ≤ 0 via 1e-12 floor) |
| `"fourier1"` | y = p₁ + p₂·cos(p₃·x) + p₄·sin(p₃·x) | 4 | offset, a, ω, b |
| `"arrhenius"` | y = p₁·exp(−p₂/x) | 2 | A, Eₐ/R |
| `"antoine"` | y = 10^(p₁ − p₂/(x+p₃)) | 3 | Antoine A, B, C |
| `"haldane"` / `"andrews"` | y = p₁·x/(p₂+x+x²/p₃) | 3 | V_max, K_m, K_i |

### User-defined worksheet functions

Any function defined in the worksheet can be fitted, in either vector or scalar
parameter form:

```smath
f_haldane(x, p) := (el(p,1) * x) / (el(p,2) + x + x^2 / el(p,3))
p := swarmfit(S_data, v_data, f_haldane, [10; 1; 10], [40; 10; 100], 12, 60)
```

### Bound Inversion Auto-Normalization

In practical worksheets, parameter bounds may occasionally be passed in reversed order (e.g. `p_max` before `p_min` or individual components with `p_min[i] > p_max[i]`). The wrapper automatically detects and normalizes inverted bounds per component (`min := Math.Min(p_min[i], p_max[i])`, `max := Math.Max(p_min[i], p_max[i])`), preventing evaluation errors without requiring manual argument swapping.

---

## 3. Diagnostics & Goodness of Fit

- **`swarmfit_details(...)`** → a 5×1 column vector:
  1. **SSE** — sum of squared errors Σ(yᵢ − ŷᵢ)²
  2. **RMSE** — √(SSE/N)
  3. **MaxResidual** — max |yᵢ − ŷᵢ|
  4. **Iterations** — swarm iterations performed
  5. **Improvements** — global-best improvements found
- **`swarmfit_r2(ys, y_pred)`** → R² = 1 − Σ(yᵢ−ŷᵢ)² / Σ(yᵢ−ȳ)² (1.0 = perfect).

> The swarm **minimises the L1 residual** (Σ|y−ŷ|) during the search; the reported
> statistics are the L2 quantities the worksheet expects.

---

## 4. Architecture & the net20 Port

This is a **pure C#** plugin (no native library). Two design decisions keep it working
on .NET Framework 2.0 / Mono:

### Vendored engine, not a referenced DLL

Upstream `SwarmFit` is `netstandard2.0`, which would pull in `System.Memory`,
`System.Buffers`, `System.Numerics.Vectors` and `System.Runtime.CompilerServices.Unsafe`
— none of which load on CLR 2.0. The needed parts are therefore **vendored into
`src/SwarmFit/` and compiled inline**. Modern-C# constructs that don't exist on the
2.0 BCL were replaced: `Func<,>` → a `FitFunction` delegate, LINQ → explicit loops,
`Span<T>` → `Array.Copy`, primary constructors / `readonly struct` → plain struct, and
`string.Trim(char)` → a hand-written `StripQuotes` scanner. Full diff is in
`patches/README.md`.

### A pure, headless-testable core

SMath numeric types (`TNumber`, `TMatrix`, `TermInfo`, `ArgumentInfo`, `Entry`) depend
on the host's `GlobalProfile` and throw `NullReferenceException` when constructed
outside a running SMath session — so logic in the SMath adapter cannot be unit tested.
The maths is therefore extracted into **`src/SwarmFitCore.cs`**, which touches **no**
SMath type and holds `StandardModel`, `Fit`, `Predict`, `R2`, `Details` and
`LinearBounds`. `SwarmFitEngine` / `SwarmFitPlugin` are the SMath-only adapter and
delegate into `SwarmFitCore`. The harness (`tests/Program.cs`) exercises `SwarmFitCore`
directly — **55 checks, all passing**.

The `XorShift` PRNG keeps SwarmFit's **fixed default seed**, so a given fit is
bit-identical run to run (and across the Windows and Linux builds).

---

## 5. Installation

### Option A — Extension Manager (recommended)

1. In SMath Studio: **Tools → Plugins…** (or `Ctrl+F10`).
2. Click **Install from file…** and select `dist/SwarmFitWrapper.1_1_0_0.zip`.
3. Restart SMath Studio.

### Option B — Manual (Windows)

```cmd
set PLUGIN_DIR=%APPDATA%\SMath\extensions\plugins\b0a439e5-20d5-46a3-bcb5-596cbd1d9934\1.1.0.0
mkdir "%PLUGIN_DIR%"
copy dist\SwarmFitWrapper.1_1_0_0.zip "%TEMP%\sf.zip"
tar -xf "%TEMP%\sf.zip" -C "%PLUGIN_DIR%"
```

### Option C — Manual (Linux / Mono)

```bash
PLUGIN_DIR="$HOME/.config/SMath/extensions/plugins/b0a439e5-20d5-46a3-bcb5-596cbd1d9934/1.1.0.0"
mkdir -p "$PLUGIN_DIR"
cd "$PLUGIN_DIR"
unzip /path/to/SwarmFitWrapper.1_1_0_0.zip
```

Because the **GUID is unchanged** from the earlier Linux/Mono build, installing this
`1.1.0.0` package is a drop-in upgrade — it lands in the same plugin folder.

---

## 6. Building from Source

No Visual Studio required. You need the .NET 8 SDK and the reference packages
(`microsoft.netframework.referenceassemblies.net20` and `smathstudio.core.ref`).

```powershell
# First time only: point -PackagesSource at the extracted NuGet packages
.\build.ps1 -PackagesSource ..\nuget-extracted
# Later builds (packages\ already populated):
.\build.ps1
```

`build.ps1` builds `src/SwarmFitWrapper.csproj`, stages the DLL + manifest + metadata
and zips them with **`ZIP_STORED`** into `dist/SwarmFitWrapper.1_1_0_0.zip`. The
version is read from `packaging/SwarmFitWrapper.dll.ini` — bump it there to re-version.

### Running the tests

```powershell
& (dotnet) build tests/SwarmFitHarness.csproj -c Release
.\tests\bin\Release\SwarmFitHarness.exe
# expect: "ALL TESTS PASSED (55/55)"
```

The harness references the plugin as a **project reference**, so it always builds the
current source first.

---

## 7. Repository Structure

```
SwarmFit_src_win/
├── build.ps1                 # builds DLL, stages manifests, writes the STORED zip
├── README.md                 # this document
├── tools/pack.py             # ZIP_STORED packer (asserts no deflated entries)
├── packaging/                # source for the manifests (copied into the zip)
│   ├── SwarmFitWrapper.dll.ini
│   └── metadata/SwarmFitWrapper.en.json
│   └── metadata/SwarmFitWrapper.ru.json
├── src/
│   ├── SwarmFitWrapper.cs    # SMath plugin: engine adapter + IPlugin interfaces
│   ├── SwarmFitCore.cs       # PURE maths (no SMath types) — the testable layer
│   ├── SwarmFitWrapper.csproj
│   └── SwarmFit/             # vendored SwarmFit engine (inline-compiled)
│       ├── SwarmFitter.cs  Particle.cs  XorShift.cs
│       ├── ParameterLimits.cs  FitSolution.cs  IRandomNumberGenerator.cs  FitFunction.cs
├── tests/
│   ├── Program.cs            # 55-check headless harness for SwarmFitCore
│   ├── SwarmFitHarness.csproj
│   └── App.config            # rolls the net20 EXE forward to CLR 4
├── patches/README.md         # exact net20 port changes vs upstream SwarmFit
├── reference/                # the original Linux/Mono build, kept as a baseline
└── dist/SwarmFitWrapper.1_1_0_0.zip   # the installable artifact
```

---

## 8. Changelog & Engine Hardening (v1.1.0.0)

### 8.1 SMath Case-Sensitivity Collision Fix (Critical Bug Fix)
* **Root Cause & Technical Diagnosis:**
  In SMath Studio, variable names and identifier symbols are **strictly case-sensitive**:
  - `t_dim` represents the dimensionless time coordinate vector: $[0, 2, 4, \dots, 60]\ \text{s}$.
  - `T_dim` represents the receiver gas temperature vector: $[298.15, 320.52, \dots, 330.71]\ \text{K}$.
  
  Inside `SwarmFitWrapper.cs`, the internal evaluation caches were originally instantiated with `StringComparer.OrdinalIgnoreCase`:
  ```csharp
  private static readonly Dictionary<string, double[]> _matrixByName =
      new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase);
  private static readonly Dictionary<string, double> _scalarByName =
      new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
  ```
* **Failure Mode & Exact Mechanism:**
  When a worksheet evaluated `t_dim` before `T_dim`:
  1. `t_dim` was evaluated, and its array `[0, 2, ..., 60]` was cached under key `"t_dim"`.
  2. Next, when `T_dim` was passed to `swarmfit_exp(t_dim, T_dim, T.LB, T.UB)`:
     ```csharp
     if (entry.Type == TermType.Operand && !string.IsNullOrEmpty(entry.Text))
     {
         double[] cached;
         if (_matrixByName.TryGetValue(entry.Text, out cached) && cached != null && cached.Length > 0)
             return cached;
     }
     ```
     `_matrixByName.TryGetValue("T_dim", ...)` case-insensitively matched the existing entry `"t_dim"`.
  3. Consequently, `ExtractDoubleArray` returned the **time coordinates** `[0..60]` as the dependent $y$-vector to `swarmfit_exp`!
  4. The PSO optimizer was commanded to fit the exponential relaxation model $y = a + b \cdot e^{c \cdot t}$ (with physical bounds $a \in [300, 360]$, $b \in [-80, -10]$, $c \in [-2, -0.01]$) to **$y = [0, 2, ..., 60]$**.
  5. Naturally, minimizing $\sum |y - \hat{y}|$ forced the parameters to their absolute boundary extremes:
     $$a = 300, \quad b = -80, \quad c = -0.01 \implies T_{\text{pred}}(t) \in [220.0, 256.1]\ \text{K}$$
  6. When evaluated via `swarmfit_r2(T_dim, T_pred)`, `T_dim` again returned `[0..60]`, comparing $[0..60]$ against $[220..256]$ and resulting in the exact anomalous metric:
     $$R^2 = -136.6898$$
* **Fix Applied:**
  Changed both `_matrixByName` and `_scalarByName` to use `StringComparer.Ordinal`. In SMath worksheets, variables differing only by capitalization (e.g. $t$ vs $T$, $p$ vs $P$, $v$ vs $V$, $h$ vs $H$) are now guaranteed distinct, independent entries in the cache.

### 8.2 Worksheet Store-First Evaluation Priority & Context Propagation
* **Cross-Worksheet Cache Contamination Bug:**
  - **Symptom:** When a user calculated one worksheet (e.g. `air_compressor_capacity_simplified_v2.sm`) that assigned `P_pred := swarmfit_predict(...)` with absolute pressure coordinates ($1.16\dots 6.98\ \text{bar abs}$), and subsequently calculated another worksheet (e.g. `air_compressor_capacity_pump_up_test.sm`) in the same SMath Studio session that ALSO defined a variable named `P_pred` (with gauge pressure $0\dots 5.99\ \text{barg}$), the second worksheet evaluated with $R^2 = 0.6619$ instead of $R^2 > 0.999$.
  - **Root Cause:** In `ExtractDoubleArray` and `ExtractScalar`, step 1 queried the static process cache `_matrixByName` *before* inspecting the current worksheet `Store context`. Because `P_pred` was already present in `_matrixByName` from the first worksheet, `ExtractDoubleArray` returned the stale absolute pressure array, completely bypassing the second worksheet's definition.
  - **Fix Applied:** Inverted the resolution hierarchy to **Store-First**:
    1. If `context != null`, the variable name is queried directly from the current worksheet's `Store` via `LastWhere(context, d => d.Name == entry.Text)`.
    2. If a matching `Definition def` exists in the active worksheet, its `def.Result` is evaluated in the current context (triggering a fresh `swarmfit_predict` evaluation if it is a function call).
    3. The fresh result updates `_matrixByName[entry.Text]` and returns immediately.
    4. Static dictionaries (`_matrixByName`, `_matrixBySig`) are now only queried as fallbacks if `context == null` or if no definition exists in the active Store.
* **Recursive Context Preservation:**
  - Propagated `Store context` across all recursive branches (`ExtractDoubleArray(fromDefinition, context)` and `ExtractScalar(def.Result, context)`), eliminating `null` context drops during column slicing (`col(M, j)`), transpositions, and nested CAS expressions.
* **Empty Array Cache Guard:** Added validation `cached != null && cached.Length > 0` before returning cached arrays to eliminate stale, empty-vector returns.

### 8.3 Parameter Bounds Inversion Auto-Normalization
* **Root Cause:** In physical modeling (e.g., negative exponential decay rates $c \in [-2.0, -0.01]$ or descending temperature gradients), engineers often specify bounds in natural physical order, which may invert the numeric bounds ($p_{\min}[i] > p_{\max}[i]$). Upstream SwarmFit threw a fatal `ArgumentException`.
* **Fix Applied:** In `SwarmFitWrapper.cs` (`RunSwarmFit`), parameter bounds are automatically normalized per dimension before invoking the optimizer:
  ```csharp
  if (p_min[i] > p_max[i])
  {
      double tmp = p_min[i];
      p_min[i] = p_max[i];
      p_max[i] = tmp;
  }
  ```

### 8.4 Logarithmic Model Singularity Guard
* **Root Cause:** Evaluating `"log"` or `"logarithmic"` models on physical datasets starting at $t = 0\ \text{s}$ caused $\ln(0) = -\infty$, resulting in NaN evaluations and runtime crash.
* **Fix Applied:** In `SwarmFitCore.cs`, inputs $x \le 0$ are automatically clamped to $10^{-12}$:
  ```csharp
  double safeX = x > 1e-12 ? x : 1e-12;
  return p[0] + p[1] * Math.Log(safeX);
  ```

### 8.5 Multi-Version Plugin Directory Synchronization & Release Packaging
* **Linux/Mono Plugin Loader Behavior:**
  Under Linux/Mono, SMath Studio maintains plugin versions under `~/.config/SMath/extensions/plugins/b0a439e5-20d5-46a3-bcb5-596cbd1d9934/`. If multiple version directories exist (`1.0.0.0/`, `1.0.0.1/`, `1.1.0.0/`), the runtime can load an earlier version directory depending on installed manifest metadata.
* **Deployment Sync:** The build and deployment scripts now verify and synchronize identical binaries across all three version directories (`1.0.0.0`, `1.0.0.1`, and `1.1.0.0`) with matching MD5 checksums.
* **Release Artifact:** The installable distribution zip `dist/SwarmFitWrapper.1_1_0_0.zip` is assembled via `tools/pack.py` using `ZIP_STORED` (uncompressed), satisfying SMath Studio's package decompression requirement without CRC corruption.

### 8.6 Automated Headless Verification Suite
* **Unit Test Coverage:** All 55 headless checks in `tests/SwarmFitHarness.exe` pass (100% test pass rate covering all 22 built-in models, convenience wrappers, error handling, PRNG determinism, and R² calculation).
* **Global Regression Gate:** Full repository regression audit via `_scratch/t1a_verify.py` confirms that all 89 worksheets pass two-pass normalized determinism with 0 diffs (`RESULT: ALL PASS`).

---

## 9. License & Acknowledgements

- **SwarmFit engine:** © Scott W. Harden — [BSD-3-Clause](https://github.com/swharden/SwarmFit).
- **PSO:** based on Kennedy & Eberhart (1995) and the McCaffrey PSO formulation.
- **SwarmFitWrapper (this plugin):** © Usman Khan — MIT License.
- Original Linux/Mono build at `SwarmFit_src/` (GUID `b0a439e5-…`, version 1.0.0.1);
  this Windows port preserves the GUID and raises the version to 1.1.0.0.
