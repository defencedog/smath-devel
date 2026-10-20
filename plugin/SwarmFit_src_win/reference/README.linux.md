# SwarmFitWrapper — Particle Swarm Optimization Curve Fitting Plugin for SMath Studio

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![SMath Studio](https://img.shields.io/badge/SMath%20Studio-1.5%2B-orange.svg)](https://en.smath.com)
[![Platform](https://img.shields.io/badge/Platform-Linux%20Mono%20%7C%20Windows%20.NET-brightgreen.svg)](https://en.smath.com)
[![Engine](https://img.shields.io/badge/Core-SwarmFit%20(Scott%20Harden)-lightgrey.svg)](https://github.com/swharden/SwarmFit)
[![Dependencies](https://img.shields.io/badge/Dependencies-Zero%20Runtime%20Deps-success.svg)](#)

**SwarmFitWrapper** is a high-performance native plugin for **SMath Studio** (supporting Linux Mono ARM64/x86_64 and Windows .NET) that brings robust, derivative-free global non-linear curve fitting to SMath Studio worksheets via **Particle Swarm Optimization (PSO)**.

It wraps the standalone C# library **SwarmFit** (`SwarmFit.dll`, .NET Standard 2.0 with bundled companion assemblies for Windows .NET Framework 4.8 / Linux Mono 6.14.1), providing seamless parameter estimation for both standard built-in kinetic/adsorption models and user-defined SMath functions.

---

## Table of Contents

1. [Overview & Capabilities](#1-overview--capabilities)
2. [Mathematical Formulation & PSO Mechanics](#2-mathematical-formulation--pso-mechanics)
3. [Architecture & Integration Flow](#3-architecture--integration-flow)
4. [Complete Function Reference](#4-complete-function-reference)
5. [Built-in Standard Model Catalog](#5-built-in-standard-model-catalog)
6. [User-Defined Custom SMath Functions](#6-user-defined-custom-smath-functions)
   - [Vector Parameter Form: `f(x, p)`](#vector-parameter-form-fx-p)
   - [Scalar Parameter Form: `f(x, p0, p1, ...)`](#scalar-parameter-form-fx-p0-p1-)
   - [Sizing the Swarm Budget](#sizing-the-swarm-budget-built-in-models-vs-custom-smath-cas-functions)
7. [Diagnostics & Goodness of Fit](#7-diagnostics--goodness-of-fit)
8. [Example Benchmark Worksheets & Plots](#8-example-benchmark-worksheets--plots)
9. [Installation & Deployment](#9-installation--deployment)
   - [Option A: SMath Studio Extension Manager](#option-a-smath-studio-extension-manager-recommended)
   - [Option B: Manual Installation (Linux)](#option-b-manual-installation-linux)
   - [Option C: Manual Installation (Windows)](#option-c-manual-installation-windows)
   - [Cross-Platform Engineering: Zero-Trim Law & .NET Standard 2.0](#91-cross-platform-engineering-zero-trim-law--net-standard-20)
10. [Building from Source](#10-building-from-source)
11. [Automated Headless Verification Test Suite](#11-automated-headless-verification-test-suite)
12. [Repository Structure](#12-repository-structure)
13. [License & Acknowledgements](#13-license--acknowledgements)

---

## 1. Overview & Capabilities

Standard gradient-based non-linear regression solvers (e.g., Levenberg-Marquardt, Gauss-Newton, Nelder-Mead) frequently fail when confronted with:
- Poor, inaccurate, or distant initial parameter guesses.
- Non-smooth, piecewise, or highly non-linear response surfaces.
- Discontinuous derivatives or multi-modal parameter spaces with deep local minima.

**SwarmFitWrapper** overcomes these limitations by deploying stochastic Particle Swarm Optimization (PSO):
- **Derivative-Free Global Search:** Uses a swarm of candidate particles moving through bounded parameter space, driven by personal best positions and swarm global intelligence.
- **Built-in Standard Engineering Models:** High-speed native C# evaluation for common engineering forms:
  - Michaelis-Menten enzyme kinetics (`"mm"`)
  - Substrate inhibition / Haldane kinetics (`"haldane"`, `"andrews"`)
  - Exponential decay / first-order relaxation (`"expdecay"`)
  - Exponential growth / baseline (`"exp"`)
  - Power-law / Freundlich adsorption isotherms (`"power"`)
  - Polynomials of degree 1 to 6 (`"poly2"`, `"poly3"`, `swarmfit_poly`)
  - Gaussian peaks and spectral distributions (`"gauss"`)
  - Hill equation / sigmoidal kinetics (`"hill"`)
  - Logarithmic response curves (`"log"`)
  - Arrhenius temperature dependency (`"arrhenius"`)
  - Antoine vapor pressure equation (`"antoine"`)
- **Direct SMath User-Defined Function Support:** Fit any custom CAS function `f(x, p)` (vector parameters) or `f(x, p0, p1, ...)` (scalar parameters) defined directly in the worksheet.
- **Dedicated Convenience Wrappers:** Fast, single-call functions for common curves (`swarmfit_linear`, `swarmfit_expdecay`, `swarmfit_power`, `swarmfit_gauss`, etc.).
- **Comprehensive Quality-of-Fit Metrics:** Calculate $R^2$ (`swarmfit_r2`), model predictions (`swarmfit_predict`), and complete optimization diagnostics (`swarmfit_details`: SSE, RMSE, MaxResidual, Iterations, Improvements).
- **Zero Runtime Dependencies:** Pure managed assembly (`SwarmFit.dll`) verified under Mono 6.14.1 on Linux without external shared C libraries.

---

## 2. Mathematical Formulation & PSO Mechanics

### The Optimization Problem
Given $N$ experimental data points $(x_i, y_i)_{i=1}^N$, find the optimal parameter vector $\mathbf{p}^* = [p_1, p_2, \dots, p_P]^T \in [\mathbf{p}_{\min}, \mathbf{p}_{\max}]$ that minimizes the objective residual function:
$$\min_{\mathbf{p}} \; E(\mathbf{p}) = \sum_{i=1}^N |y_i - f(x_i, \mathbf{p})|$$

### Swarm Kinematics
For each particle $k \in \{1, \dots, M\}$ with position $\mathbf{x}_k$ and velocity $\mathbf{v}_k$:
1. **Velocity Update:**
   $$\mathbf{v}_k^{(t+1)} = w \cdot \mathbf{v}_k^{(t)} + c_1 r_1 (\mathbf{p}_{k,\text{best}} - \mathbf{x}_k^{(t)}) + c_2 r_2 (\mathbf{g}_{\text{best}} - \mathbf{x}_k^{(t)})$$
   where:
   - $w = 0.729$ (Inertia weight balancing exploration and exploitation)
   - $c_1 = 1.49445$ (Cognitive / local weight pulling toward individual best)
   - $c_2 = 1.49445$ (Social / global weight pulling toward swarm global best)
   - $r_1, r_2 \sim U(0, 1)$ (Uniform pseudo-random numbers via fast XorShift RNG)
2. **Position Update & Boundary Clamping:**
   $$\mathbf{x}_k^{(t+1)} = \text{clamp}\left(\mathbf{x}_k^{(t)} + \mathbf{v}_k^{(t+1)}, \; \mathbf{p}_{\min}, \; \mathbf{p}_{\max}\right)$$
3. **Particle Re-seeding (Death Probability):**
   To prevent premature convergence in local attractors, non-best particles undergo random re-initialization with probability $P_{\text{death}} = 0.01$ per iteration.

---

## 3. Architecture & Integration Flow

```
┌────────────────────────────────────────────────────────────────────────┐
│                        SMath Studio Worksheet                          │
│         p := swarmfit(xs, ys, "mm", [1; 0.1], [30; 10])               │
└───────────────────────────────────┬────────────────────────────────────┘
                                    │ LowLevel AST Evaluation
                                    ▼
┌────────────────────────────────────────────────────────────────────────┐
│                        SwarmFitWrapper.dll                             │
│   (Implements IPlugin, IPluginHandleEvaluation, IPluginLowLevelFast)   │
│                                                                        │
│  1. Extracts X/Y data vectors & parameter bounds (AST / CAS Store)     │
│  2. Resolves model target:                                             │
│     - Built-in string ("mm", "expdecay", "power", etc.) -> C# delegate │
│     - User-defined SMath CAS function f(x, p) -> Computation Callback  │
│  3. Instantiates SwarmFitter with bounds and particle population       │
│  4. Runs PSO solve loop until convergence / maximum iterations         │
│  5. Synchronizes AST results to prevent CAS re-evaluation stalls       │
│  6. Formats parameter vector or error matrix as SMath TMatrix          │
└───────────────────────────────────┬────────────────────────────────────┘
                                    │ Managed Reference
                                    ▼
┌────────────────────────────────────────────────────────────────────────┐
│                          SwarmFit.dll                                  │
│         (Pure C# netstandard2.0 / Mono / .NET Framework 4.8)           │
│         - SwarmFitter, QuickFit, FitSolution, ParameterLimits          │
│         - Fast XorShift Random Number Generator                        │
└────────────────────────────────────────────────────────────────────────┘
```

---

## 4. Complete Function Reference

> [!IMPORTANT]
> **Physical Units Limitation & Mandatory `sv()` Stripping:**
> `SwarmFitWrapper` operates purely on dimensionless numerical `double` values and column matrices of doubles. It **does NOT support SMath physical units** on input vectors (`xs`, `ys`), parameter bound vectors (`p_min`, `p_max`), or user-defined model functions.
> If unit-bearing matrices or vectors are passed directly to `swarmfit` or `swarmfit_predict`, SMath's low-level evaluation fails with `InvalidCastException` or Error 74 ("Units don't match.").
> **Mandatory Usage Pattern:** All unit-bearing variables and data vectors must have their units stripped using the universal unit-stripping function `sv()`:
> ```smath
> sv(x) := x * (UnitsOf(x)^(-1))
> 
> // Fitting with stripped unit inputs:
> p_fit := swarmfit(sv(S_data), sv(v_data), "mm", sv(p_min), sv(p_max))
> v_pred := swarmfit_predict(sv(S_data), "mm", p_fit)
> ```

The plugin registers **10 functions** across **17 signatures** into the SMath Studio function catalog:

| Function | Arity | Arguments | Return | Description |
| :--- | :---: | :--- | :--- | :--- |
| `swarmfit` | 5 | `xs, ys, model, p_min, p_max` | Vector ($P \times 1$) | Fits parameters using default swarm settings (20 particles, 5000 iters). |
| `swarmfit` | 7 | `xs, ys, model, p_min, p_max, particles, max_iter` | Vector ($P \times 1$) | Fits parameters with custom particle count and max iteration budget. |
| `swarmfit_predict` | 3 | `xs, model, p` | Vector ($N \times 1$) | Evaluates predicted values $\hat{y}_i = f(x_i, \mathbf{p})$ at given coordinates. |
| `swarmfit_details` | 5 | `xs, ys, model, p_min, p_max` | Matrix ($5 \times 1$) | Returns `[SSE; RMSE; MaxResidual; Iterations; Improvements]`. |
| `swarmfit_details` | 7 | `xs, ys, model, p_min, p_max, particles, max_iter` | Matrix ($5 \times 1$) | Returns convergence and error metrics with custom swarm settings. |
| `swarmfit_r2` | 2 | `ys, y_pred` | Scalar | Computes coefficient of determination $R^2 = 1 - \frac{\text{SSE}}{\text{SST}}$. |
| `swarmfit_linear` | 2 | `xs, ys` | Vector ($2 \times 1$) | Fits linear model $y = a \cdot x + b$. Returns `[a; b]`. |
| `swarmfit_exp` | 4, 6 | `xs, ys, p_min, p_max [, particles, max_iter]` | Vector ($3 \times 1$) | Fits exponential model $y = a + b \cdot \exp(c \cdot x)$. Returns `[a; b; c]`. |
| `swarmfit_expdecay` | 4, 6 | `xs, ys, p_min, p_max [, particles, max_iter]` | Vector ($2 \times 1$) | Fits decay model $y = a \cdot \exp(-x / b)$. Returns `[a; b]`. |
| `swarmfit_power` | 4, 6 | `xs, ys, p_min, p_max [, particles, max_iter]` | Vector ($2 \times 1$) | Fits power law $y = a \cdot x^b$. Returns `[a; b]`. |
| `swarmfit_gauss` | 4, 6 | `xs, ys, p_min, p_max [, particles, max_iter]` | Vector ($3 \times 1$) | Fits Gaussian peak $y = a \cdot \exp\left(-\frac{(x - b)^2}{2 c^2}\right)$. Returns `[a; b; c]`. |
| `swarmfit_poly` | 5, 7 | `xs, ys, degree, p_min, p_max [, particles, max_iter]` | Vector ($(d+1) \times 1$) | Fits polynomial of degree $d$ ($1 \le d \le 6$). Returns `[p0; p1; ...; pd]`. |

---

## 5. Built-in Standard Model Catalog

When specifying the `model` argument as a string, SwarmFit evaluates the curve natively in C# for maximum performance:

| Model Name | Formula | Parameters | Parameter Description |
| :--- | :--- | :---: | :--- |
| `"linear"` / `"line"` | $y = p_1 \cdot x + p_2$ | 2 | $p_1$: Slope $a$, $p_2$: Intercept $b$ |
| `"exp"` | $y = p_1 + p_2 \cdot \exp(p_3 \cdot x)$ | 3 | $p_1$: Baseline offset, $p_2$: Amplitude, $p_3$: Rate constant |
| `"expdecay"` | $y = p_1 \cdot \exp(-x / p_2)$ | 2 | $p_1$: Initial value $C_0$, $p_2$: Time constant $\tau$ |
| `"power"` | $y = p_1 \cdot x^{p_2}$ | 2 | $p_1$: Pre-factor $a$ ($K_F$), $p_2$: Exponent $b$ ($1/n$) |
| `"poly2"` | $y = p_1 \cdot x^2 + p_2 \cdot x + p_3$ | 3 | Quadratic coefficients ($a, b, c$) |
| `"poly3"` | $y = p_1 \cdot x^3 + p_2 \cdot x^2 + p_3 \cdot x + p_4$ | 4 | Cubic polynomial coefficients |
| `"gauss"` / `"gaussian"` | $y = p_1 \cdot \exp\left(-\frac{(x - p_2)^2}{2 p_3^2}\right)$ | 3 | $p_1$: Peak height, $p_2$: Center $\mu$, $p_3$: Width $\sigma$ |
| `"mm"` | $y = \frac{p_1 \cdot x}{p_2 + x}$ | 2 | $p_1$: Maximum velocity $V_{\max}$, $p_2$: Affinity $K_m$ |
| `"haldane"` / `"andrews"` | $y = \frac{p_1 \cdot x}{p_2 + x + x^2 / p_3}$ | 3 | $p_1$: $V_{\max}$, $p_2$: $K_m$, $p_3$: Inhibition constant $K_i$ |
| `"hill"` | $y = \frac{p_1 \cdot x^{p_3}}{p_2^{p_3} + x^{p_3}}$ | 3 | $p_1$: $V_{\max}$, $p_2$: $K_{0.5}$, $p_3$: Hill coefficient $n$ |
| `"log"` | $y = p_1 + p_2 \cdot \ln(x)$ | 2 | $p_1$: Offset, $p_2$: Slope |
| `"arrhenius"` | $y = p_1 \cdot \exp\left(-\frac{p_2}{x}\right)$ | 2 | $p_1$: Frequency factor $A$, $p_2$: Activation energy ratio $E_a / R$ |
| `"antoine"` | $y = 10^{p_1 - \frac{p_2}{x + p_3}}$ | 3 | Antoine vapor pressure coefficients ($A, B, C$) |

---

## 6. User-Defined Custom SMath Functions

SwarmFit seamlessly supports user-defined SMath functions defined directly in the worksheet:

### Vector Parameter Form: `f(x, p)`
```smath
f_haldane(x, p) := (el(p, 1) * x) / (el(p, 2) + x + (x^2) / el(p, 3))
p_fit := swarmfit(S_data, v_data, f_haldane, [10; 1; 10], [40; 10; 100], 12, 60)
```

### Scalar Parameter Form: `f(x, p0, p1, ...)`
```smath
f_custom(x, a, b, c) := a * sin(b * x) + c
p_fit := swarmfit(xs, ys, f_custom, [0; 0; -5], [10; 10; 5], 15, 80)
```

### Sizing the Swarm Budget: Built-in Models vs Custom SMath CAS Functions

Understanding the performance difference between model types is essential for optimal worksheet sizing:

| Model Type | Evaluation Engine | Speed per Evaluation | Recommended Swarm Settings | Typical Solve Time |
| :--- | :--- | :--- | :--- | :--- |
| **Built-in Models** (`"mm"`, `"haldane"`, `"expdecay"`, etc.) | Native C# compiled delegates | Nanoseconds | 20–30 particles, 2,000–5,000 iterations | $< 0.05\text{ seconds}$ |
| **User-Defined SMath Functions** (`f(x, p)`, `f(x, a, b)`) | SMath CAS symbolic AST engine | ~0.1–0.3 ms | 10–15 particles, 50–100 iterations | $0.8–1.5\text{ seconds}$ |

> [!TIP]
> **Automatic Early Stagnation Termination:**
> SwarmFitWrapper includes automated stagnation detection. If the relative improvement in the best solution error drops below $10^{-5}$ across successive iteration batches, the solver terminates safely early to conserve computational resources.
>
> **Best Practice for Custom CAS Functions:**
> For non-linear models with 2–4 parameters, PSO converges tightly to $> 4$ significant figures within 50–80 iterations. Setting `particles = 12, max_iterations = 60` provides sub-second evaluation in SMath Studio with $R^2 > 0.998$. If you require thousands of iterations, consider using the built-in string models (`"mm"`, `"haldane"`, `"power"`, etc.).

---

## 7. Diagnostics & Goodness of Fit

### `swarmfit_details(xs, ys, model, p_min, p_max)`
Returns a $5 \times 1$ column vector containing:
1. `el(details, 1)`: **SSE** — Sum of Squared Errors: $\sum_{i=1}^N (y_i - \hat{y}_i)^2$
2. `el(details, 2)`: **RMSE** — Root Mean Squared Error: $\sqrt{\text{SSE} / N}$
3. `el(details, 3)`: **MaxResidual** — Maximum absolute deviation: $\max_i |y_i - \hat{y}_i|$
4. `el(details, 4)`: **Iterations** — Total particle swarm iterations performed
5. `el(details, 5)`: **Improvements** — Number of global best improvements identified

### `swarmfit_r2(ys, y_pred)`
Calculates the coefficient of determination:
$$R^2 = 1 - \frac{\sum_{i=1}^N (y_i - \hat{y}_i)^2}{\sum_{i=1}^N (y_i - \bar{y})^2}$$
where $R^2 = 1.0$ indicates a perfect fit.

---

## 8. Example Benchmark Worksheets & Plots

The companion verification worksheet [`examples/swarmfit_parameter_estimation.sm`](examples/swarmfit_parameter_estimation.sm) exercises all plugin capabilities across **7 distinct chemical engineering benchmark problems**, each featuring dedicated **Chart2DLib X-Y Plot** regions visually comparing measured experimental points against fitted model curves:

1. **Michaelis-Menten Biochemical Enzyme Kinetics:**
   $v = \frac{V_{\max} S}{K_m + S}$ (`"mm"` model). Fits $V_{\max} \approx 20.19$, $K_m \approx 2.45$, $R^2 \approx 0.9996$ with $(S, v)$ overlay plot.
2. **First-Order Reaction Relaxation & CSTR Tracer Washout:**
   $C(t) = C_0 \exp(-t / \tau)$ (`swarmfit_expdecay`). Fits $C_0 \approx 99.8$, $\tau \approx 4.97$, $R^2 \approx 0.9998$ with $(t, C)$ overlay plot.
3. **Freundlich Adsorption Isotherm (Heterogeneous Carbon Uptake):**
   $q_e = K_F C_e^{1/n}$ (`swarmfit_power`). Fits $K_F \approx 1.84$, $1/n \approx 0.44$, $R^2 \approx 0.998$ with $(C_e, q_e)$ overlay plot.
4. **Substrate Inhibition Kinetics in Fermentation (User-Defined CAS Function with Cached `=` Result):**
   $f_{\text{haldane}}(x, \mathbf{p}) = \frac{p_1 x}{p_2 + x + x^2 / p_3}$. Fits $V_{\max} \approx 20.3$, $K_m \approx 4.1$, $K_i \approx 26.6$, $S_{\text{opt}} \approx 10.4$, $R^2 \approx 0.997$ with non-monotonic $(S, v)$ overlay plot.
5. **Langmuir Monolayer Wastewater Adsorption (User-Defined CAS Function with Cached `=` Result):**
   $f_{\text{langmuir}}(x, \mathbf{p}) = \frac{p_1 p_2 x}{1 + p_2 x}$. Fits $q_m \approx 49.6$, $K_L \approx 0.098$, Hall separation factor $R_L \approx 0.169$ (favorable adsorption), $R^2 \approx 0.999$ with $(C_e, q_e)$ overlay plot.
6. **Consecutive Batch Reaction Kinetics $A \xrightarrow{k_1} B \xrightarrow{k_2} C$ (User-Defined CAS Function with Cached `=` Result):**
   $f_{\text{consec}}(t, \mathbf{p}) = \frac{k_1}{k_2 - k_1}(\exp(-k_1 t) - \exp(-k_2 t))$. Fits $k_1 \approx 0.601$, $k_2 \approx 0.150$, $t_{\text{opt}} \approx 3.08\text{ h}$, $C_{B,\max} \approx 0.53$, $R^2 \approx 0.999$ with intermediate yield overlay plot. Demonstrates PSO convergence where Gauss-Newton solvers face ill-conditioned singularities.
7. **Analytical Instrumentation Baseline Calibration:**
   $Y = a X + b$ (`swarmfit_linear`). Fits $a \approx 2.49$, $b \approx 1.05$, $R^2 \approx 0.9998$ with $(X, Y)$ overlay plot.

For complete formulas, mathematical derivations, parameter bounds, and variable catalogs, see [examples/README.md](examples/README.md).

---

## 9. Installation & Deployment

### Option A: SMath Studio Extension Manager (Recommended)
1. In SMath Studio, go to **Tools** > **Plugins...** (or press `Ctrl+F10`).
2. Search for `SwarmFit` or click **Install from file...** and select `SwarmFitWrapper.1_0_0_1.zip`.
3. Restart SMath Studio.

### Option B: Manual Installation (Linux)
Create the plugin version directory and deploy the compiled assemblies and configuration:
```bash
PLUGIN_DIR="$HOME/.config/SMath/extensions/plugins/b0a439e5-20d5-46a3-bcb5-596cbd1d9934/1.0.0.1"
mkdir -p "${PLUGIN_DIR}"

cp bin/SwarmFitWrapper.dll "${PLUGIN_DIR}/"
cp lib/netstandard2.0/SwarmFit.dll "${PLUGIN_DIR}/"
cp lib/netstandard2.0/System.Memory.dll "${PLUGIN_DIR}/"
cp lib/netstandard2.0/System.Buffers.dll "${PLUGIN_DIR}/"
cp lib/netstandard2.0/System.Numerics.Vectors.dll "${PLUGIN_DIR}/"
cp lib/netstandard2.0/System.Runtime.CompilerServices.Unsafe.dll "${PLUGIN_DIR}/"
cp config/SwarmFitWrapper.dll.ini "${PLUGIN_DIR}/"
cp config/config.1_5_0_9678.ini "$(dirname "${PLUGIN_DIR}")/"
```

### Option C: Manual Installation (Windows)
Create the target directory and copy assemblies:
```cmd
set PLUGIN_DIR=%APPDATA%\SMath\extensions\plugins\b0a439e5-20d5-46a3-bcb5-596cbd1d9934\1.0.0.1
mkdir "%PLUGIN_DIR%"

copy bin\SwarmFitWrapper.dll "%PLUGIN_DIR%\"
copy lib\netstandard2.0\SwarmFit.dll "%PLUGIN_DIR%\"
copy lib\netstandard2.0\System.Memory.dll "%PLUGIN_DIR%\"
copy lib\netstandard2.0\System.Buffers.dll "%PLUGIN_DIR%\"
copy lib\netstandard2.0\System.Numerics.Vectors.dll "%PLUGIN_DIR%\"
copy lib\netstandard2.0\System.Runtime.CompilerServices.Unsafe.dll "%PLUGIN_DIR%\"
copy config\SwarmFitWrapper.dll.ini "%PLUGIN_DIR%\"
copy config\config.1_5_0_9678.ini "%APPDATA%\SMath\extensions\plugins\b0a439e5-20d5-46a3-bcb5-596cbd1d9934\"
```

### 9.1 Cross-Platform Engineering: Zero-Trim Law & .NET Standard 2.0

#### 1. The Zero-Trim Law (Windows CLR / .NET Framework 4.8)
Under Windows 11 running SMath Studio on Microsoft .NET Framework 4.8, invoking `System.String.Trim(char)` throws a fatal runtime exception:
```
System.MissingMethodException: Method not found: 'System.String System.String.Trim(Char)'
```
This occurs because single-character `Trim(char)` was introduced in .NET Core 2.1 / .NET Standard 2.1 and does not exist in .NET Framework 4.8's `mscorlib.dll` (which only provides `Trim(params char[])`).

In `SwarmFitWrapper.cs`, string model parsing is handled exclusively via an index-based scanner:
```csharp
private static string StripQuotes(string s)
{
    if (string.IsNullOrEmpty(s)) return s;
    int start = 0;
    int end = s.Length;
    while (start < end && (s[start] == '"' || s[start] == '\'' || char.IsWhiteSpace(s[start])))
        start++;
    while (end > start && (s[end - 1] == '"' || s[end - 1] == '\'' || char.IsWhiteSpace(s[end - 1])))
        end--;
    return (start == 0 && end == s.Length) ? s : s.Substring(start, end - start);
}
```
This guarantees **0 calls to `System.String.Trim` in the compiled IL binary**, eliminating the `MissingMethodException` under Windows.

#### 2. .NET Standard 2.0 & Companion Assemblies
Microsoft .NET Framework 4.8 only supports up to **.NET Standard 2.0** (it does not support .NET Standard 2.1). The upstream `SwarmFit.dll` is compiled targeting `.NET Standard 2.0` and distributed alongside companion assemblies providing `Span<T>` and high-performance buffer support:
- `System.Memory.dll` (v4.0.1.2)
- `System.Buffers.dll` (v4.0.3.0)
- `System.Numerics.Vectors.dll` (v4.0.3.0)
- `System.Runtime.CompilerServices.Unsafe.dll` (v4.0.4.1)

#### 3. Version Bump to `1.0.0.1`
To avoid Windows DLL memory-mapped locks (`ERROR_SHARING_VIOLATION`) when replacing existing assemblies in running SMath instances and to prevent Extension Manager cache collisions, the assembly and package are pinned to version `1.0.0.1`.

---

## 10. Building from Source

### Prerequisites
- **Linux:** Mono runtime & compiler (`mono-complete`, `mcs`, Mono 6.14.1+) and SMath Studio assemblies.
- **Windows:** .NET Framework 4.5+ Developer Pack or Visual Studio 2013+.

### Linux Build Script
```bash
# Set path to SMath Studio install if not in standard default location
export SMATH_DIR="$HOME/Binaries/SMathStudioDesktop.1_5_0_9678.Mono"

# Build SwarmFitWrapper.dll into bin/
bash build.sh

# Package into distribution zip and deploy locally
bash pack.sh
```

### Building via MSBuild / Visual Studio
```bash
# On Linux
xbuild /p:Configuration=Release SwarmFitWrapper.sln
# or
msbuild /p:Configuration=Release SwarmFitWrapper.sln

# On Windows
msbuild /p:Configuration=Release SwarmFitWrapper.sln
```

---

## 11. Automated Headless Verification Test Suite

A standalone test suite is included in `tests/` to verify plugin operation without launching the SMath GUI:

```bash
bash tests/run_tests.sh
```

### Expected Output
```
Compiling test harness: .../bin/test_verify.exe...
Executing test harness...
================================================================================
SwarmFitWrapper Standalone Verification Test Suite
Loading plugin assembly from: .../bin/SwarmFitWrapper.dll
================================================================================
[PASS] Plugin initialized successfully.
[PASS] Registered TermsHandled count: 17

--- Test 1: swarmfit linear string model ---
[PASS] Fitted linear: a=2.0000, b=0.5000

--- Test 2: swarmfit_predict ---
[PASS] Predictions count: 5, First: 2.5000, Last: 10.5000

--- Test 3: swarmfit_r2 ---
[PASS] R^2: 1.000000

--- Test 4: swarmfit_details ---
[PASS] Details: SSE=6.1534E-015, RMSE=3.5081E-008, MaxRes=5.5970E-008, Iters=225, Impr=78

--- Test 5: swarmfit_linear convenience ---
[PASS] swarmfit_linear: a=2.0000, b=0.5000

--- Test 6: Michaelis-Menten kinetics ('mm') ---
[PASS] Michaelis-Menten: Vmax=8.5000 (expected 8.5), Km=2.4000 (expected 2.4)

--- Test 7: swarmfit_expdecay convenience ---
[PASS] swarmfit_expdecay: a=12.0000 (expected 12.0), tau=2.5000 (expected 2.5)

--- Test 8: Substrate Inhibition kinetics ('haldane') ---
[PASS] Haldane: Vmax=21.131, Km=4.668, Ki=26.980

--- Test 9: Substrate Inhibition prediction and R2 ---
[PASS] Haldane predictions: [3.70, 6.20, 9.97, 11.50, 10.70, 8.13, 6.40, 5.25]
[PASS] Haldane R^2 = 0.998956

--- Test 10: swarmfit_power convenience ---
[PASS] swarmfit_power: a=2.0000 (expected 2.0), b=0.5000 (expected 0.5)

================================================================================
>>> ALL SWARMFIT TESTS PASSED SUCCESSFULLY (10/10) <<<
================================================================================
Cleaning up temporary test binary...
Test execution complete.
```

---

## 12. Repository Structure

```
SwarmFit_src/
├── .gitignore                  # Git ignore rules for build artifacts & IDE files
├── LICENSE                     # MIT License & SwarmFit BSD-3 Clause acknowledgment
├── README.md                   # Complete documentation & usage guide
├── SwarmFitWrapper.1_0_0_1.zip # Distribution package for SMath Extension Manager
├── SwarmFitWrapper.csproj      # Visual Studio / MonoDevelop C# project file
├── SwarmFitWrapper.sln         # Visual Studio Solution file
├── bin/                        # Compiled build outputs
│   ├── SwarmFit.dll            # SwarmFit core managed library (.NET Standard 2.0)
│   ├── SwarmFitWrapper.dll     # Compiled native SMath Studio plugin (v1.0.0.1)
│   ├── SwarmFitWrapper.dll.ini # Assembly loader hints
│   ├── System.Buffers.dll      # Companion buffer dependency
│   ├── System.Memory.dll       # Companion Span<T> memory dependency
│   ├── System.Numerics.Vectors.dll # Companion SIMD vector dependency
│   └── System.Runtime.CompilerServices.Unsafe.dll # Companion low-level dependency
├── build.sh                    # Linux Mono compilation script (mcs)
├── config/                     # Extension manager configuration files
│   ├── config.1_5_0_9678.ini   # Extension manager version pinning (v1.0.0.1)
│   └── SwarmFitWrapper.dll.ini # Assembly load hints
├── examples/                   # Demonstration & validation worksheets
│   ├── README.md               # Detailed guide for example problems & plots
│   └── swarmfit_parameter_estimation.sm # 5 engineering models + Chart2DLib plots
├── lib/                        # Pre-compiled core dependencies
│   └── netstandard2.0/         # .NET Standard 2.0 assemblies & companions
│       ├── SwarmFit.dll        # Upstream SwarmFit library (.NET Standard 2.0)
│       ├── System.Buffers.dll
│       ├── System.Memory.dll
│       ├── System.Numerics.Vectors.dll
│       └── System.Runtime.CompilerServices.Unsafe.dll
├── metadata/                   # SMath Studio catalog definitions
│   ├── SwarmFitWrapper.en.json # English metadata catalog (v1.0.0.1)
│   └── SwarmFitWrapper.ru.json # Russian metadata catalog (v1.0.0.1)
├── pack.sh                     # ZIP packaging & local deployment script
├── src/                        # Plugin C# source code
│   └── SwarmFitWrapper.cs      # Complete SMath plugin implementation (Zero-Trim)
└── tests/                      # Standalone automated headless verification
    ├── run_tests.sh            # Automated bash runner
    └── test_verify.cs          # 10-point test harness
```

---

## 13. License & Acknowledgements

- **SwarmFit Library:** Developed by Scott W. Harden ([BSD-3-Clause License](LICENSE)).
- **Particle Swarm Optimization:** Based on foundational algorithms published by Kennedy & Eberhart (1995) and standard PSO implementations in scientific literature.
- **SwarmFitWrapper for SMath Studio:** Developed by Usman Khan ([MIT License](LICENSE)).
