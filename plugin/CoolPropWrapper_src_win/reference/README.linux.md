# CoolPropWrapper — Native SMath Studio Thermodynamic Plugin (v8.0.0.5)

**CoolPropWrapper** is the native **SMath Studio** plugin for **CoolProp** (www.coolprop.org), providing high-accuracy thermodynamic and transport properties for pure fluids, pseudo-pure fluids, multi-component mixtures, and humid air (psychrometrics).

This repository contains the complete, production-tested source code, project files, native library artifacts, and distribution package updated for **SMath Studio 1.75+ under Linux (Mono aarch64/x86_64) and Windows (.NET Framework 4.5+)**.

---

## 1. Overview & Capabilities

- **Pure and Pseudo-Pure Fluids:** Access state properties for over 120 fluids (Water, Nitrogen, Argon, Oxygen, $\text{CO}_2$, $\text{Cl}_2$, Hydrocarbons, Ammonia, Siloxanes, and modern ASHRAE fluorinated refrigerants R134a, R410A, R1234yf, etc.) via fundamental Helmholtz-energy equations of state.
- **Multi-Component Mixtures:** Rigorous thermodynamic equilibrium and property calculations for fluid mixtures (e.g. natural gas blends, refrigerant mixtures) using the GERG-2008 and Peng-Robinson formulations with cubic and multiparameter mixing rules.
- **Humid Air (Psychrometrics):** Complete implementation of ASHRAE RP-1485 formulation for moist air properties (enthalpy, wet-bulb temperature, relative humidity, dew-point temperature, partial pressure).
- **Saturation Ancillaries:** High-speed, closed-form ancillary equations for saturation pressure, saturation temperature, and saturated liquid/vapor densities.
- **Reference State Customization:** User-configurable reference enthalpy and entropy states (IIR, ASHRAE, NBP, DEF, or custom $[T_0, \rho_0, h_0, s_0]$ datum points).

---

## 2. Upstream Bug Fixes & Linux Mono Port (The 8.0.0.5 Story)

The official legacy plugin distributed via the SMath Extension Gallery (v6.4.8214.13502) failed on non-Windows operating systems. Version **8.0.0.5** resolves all architectural barriers:

### Bug #1: The Mono Kill-Switch & Embedded Windows Binaries
- **Legacy Issue:** The 6.4 plugin embedded gzip-compressed Windows PE binaries (`CoolProp_x64.dll.gz`) and contained an explicit check for `"Mono.Runtime"` in `Plugin::Initialize` (`ikdasm` analysis: `ldstr "Mono.Runtime"` $\to$ `call Plugin::''()` $\to$ `brtrue IL_0328` jumping over the entire registration loop). On Linux Mono, the plugin loaded silently, registered zero functions, and every `CoolProp_*` call produced `"function is not defined"`.
- **Resolution:** Recompiled from the CoolProp v8 open-source wrapper without Mono gates, decoupling managed MSIL execution from native platform binaries. Dynamic calls link cleanly via standard dynamic linker P/Invoke (`dlopen`).

### Bug #2: SMath 1.75 `Unit` Static Initializer Crash
- **Legacy Issue:** `Unit` constructed its 160-entry unit mapping dictionary in its static constructor (`.cctor`), calling `UnitsManager.GetCurrentUnitName(Profile, ...)` where `Profile` was resolved via `GlobalParams.CurrentSessionProfile`. In SMath 1.75, `CurrentSessionProfile` became non-public, returning `null` and throwing an unhandled `NullReferenceException` inside `UnitsManager`. In .NET/Mono, a failed static initializer permanently poisons the class for the entire process lifetime, causing subsequent calls to fail with *"The type initializer for 'coolprop_wrapper.Unit' threw an exception"*.
- **Resolution:** Implemented explicit lazy binding (`Unit.Bind(sessionProfile)`) invoked from `IPluginHandleEvaluation.GetTermsHandled` using the live session profile passed by the SMath kernel, backed by reflection fallback (`BindingFlags.NonPublic`) and a never-throw fallback path.

### Bug #3: The `\NNNN\` Mixture String Transport Corruption
- **Legacy Issue:** Under Mono, when strings were evaluated via `TDouble.ToString()`, SMath's AST internal serialization escapes reached native C++ verbatim (e.g., `.` $\to$ `\002E\`, `&` $\to$ `\0026\`). For single fluids (`Water`, `Chlorine`), strings contained no punctuation and worked fine. However, multi-component mixture strings (e.g., `ETHANE[0.5]&METHANE[0.3]&PROPANE[0.2]`) reached native `PropsSI` as:
  ```text
  ETHANE[0\002E\5]\0026\METHANE[0\002E\3]\0026\PROPANE[0\002E\2]
  ```
  CoolProp's native `strsplit('&')` failed to find `&`, generating:
  - `Could not break [...] into name/fraction`
  - `Neither input to Props1SI [...] is a valid fluid`
- **Resolution:** Prefer the raw `TDouble.Text` field and implement `UnescapeMathString` in `CoolPropPlugin.cs` to decode residual `\NNNN\` escapes into clean UTF-8 before calling native P/Invoke.

---

## 3. Function Reference (All 11 Functions)

### 3.1 Thermodynamic State & Property Evaluation

| Function | Arity | Description | Typical Call |
|:---|:---:|:---|:---|
| `CoolProp_Props` | 6 | Evaluates state property for pure fluid or mixture given two independent properties | `Props("D", "T", 300, "P", 101325, "Water")` |
| `CoolProp_Props1` | 2 | Evaluates trivial fluid-constant property (1-argument) | `Props1("Water", "Tcrit")`<br>`Props1("Methane", "M")` |
| `CoolProp_Phase` | 5 | Determines physical phase state string (`liquid`, `gas`, `twophase`, `supercritical`) | `Phase("T", 300, "P", 101325, "Water")` |
| `CoolProp_saturation_ancillary` | 5 | High-speed saturation ancillary equations (liquid/vapor boundary) | `saturation_ancillary("Water", "P", 0, "T", 373.15)` |

### 3.2 Humid Air Properties (Psychrometrics)

| Function | Arity | Description | Typical Call |
|:---|:---:|:---|:---:|
| `CoolProp_HAProps` | 7 | Calculates thermodynamic properties of moist air (ASHRAE RP-1485) | `HAProps("H", "T", 300, "P", 101325, "R", 0.5)` |

*Note on Humid Air:* Relative humidity must be specified using the key `"R"` (not `"RH"`). Values range between $0.0$ and $1.0$.

### 3.3 Information & Configuration

| Function | Arity | Description | Typical Call |
|:---|:---:|:---|:---:|
| `CoolProp_get_global_param_string` | 1 | Queries global CoolProp library parameters (`version`, `errstring`, `fluids_list`) | `get_global_param_string("version")` |
| `CoolProp_get_fluid_param_string` | 2 | Queries fluid metadata (`CAS`, `formula`, `aliases`, `ASHRAE34`) | `get_fluid_param_string("Water", "CAS")` |
| `CoolProp_get_param_index` | 1 | Returns internal numeric index for a parameter name | `get_param_index("P")` |
| `CoolProp_get_parameter_information_string` | 2 | Returns parameter documentation (`long`, `units`, `IO`) | `get_parameter_information_string("T", "units")` |
| `CoolProp_set_reference_stateS` | 2 | Sets reference state to standard preset (`IIR`, `ASHRAE`, `NBP`, `DEF`) | `set_reference_stateS("Water", "ASHRAE")` |
| `CoolProp_set_reference_stateD` | 5 | Sets custom reference datum point $[T_0, \rho_0, h_0, s_0]$ | `set_reference_stateD(Fluid, T, rho, h0, s0)` |

---

## 4. C-API Conventions & Native Calling Footguns

When interacting with native CoolProp through P/Invoke, adhere to these critical implementation behaviors:

1. **Success Return Code Convention (`rc == 1`):**
   - For `PhaseSI`, `get_fluid_param_string`, `get_parameter_information_string`, and `set_reference_stateS/D`, a return code of **`1` indicates success**.
   - A return code of `0` is NOT success (it indicates failure or missing data), and return values `< 0` signal fatal errors.
2. **`get_parameter_information_string` StringBuilder Seeding:**
   - The third argument is an `[In, Out] StringBuilder` that **must be pre-seeded** with the desired query type (e.g. `new StringBuilder("long", 1024)`).
   - Valid seed modes: `"long"`, `"units"`, `"short"`, `"IO"`. Passing an unseeded or empty buffer returns uninitialized garbage memory.
3. **Internal Parameter Indices (`get_param_index`):**
   - Enum values differ across major CoolProp releases (e.g., `get_param_index("P")` returns index `20` in CoolProp 8.x). Never hardcode integer indices into wrapper logic.
4. **Reference State Shifts:**
   - Setting a reference state via `set_reference_stateS(fluid, "ASHRAE" | "NBP")` shifts **enthalpy ($h$) and entropy ($s$) only**.
   - Density ($\rho$) and temperature ($T$) states are strictly physical and invariant under reference datum transformations.

---

## 5. SMath Studio Worksheet Usage Rules

1. **Numeric Arguments Must Ride BARE:**  
   Inside CoolProp call expressions, numeric arguments must be pure numbers. **Never** juxtapose SMath unit operands into call arguments (e.g., write `Props("D", "T", 300, "P", 101325, "Water")`, NOT `Props("D", "T", 300*[K], ...)`). To use unit-bearing variables, strip them first via `val := T / [K]` or `val := T * UnitsOf(T)^(-1)`.
2. **String Arguments:**  
   Enter string literals as standard text in double quotes (e.g. `"Water"`, `"D"`, `"P"`).
3. **Mixture Format:**  
   Specify multi-component mixtures using mole fractions: `"COMPONENT1[x1]&COMPONENT2[x2]"`. Example: `"METHANE[0.9]&ETHANE[0.1]"`.
4. **Identifier Naming Law:**  
   Variable names in SMath Studio must not begin with a digit (e.g. `M_1` is valid; `1_M` is invalid).

---

## 6. Linux Mono Architecture & Native P/Invoke (`dllmap`)

### 6.1 Call Chain
```
SMath (Mono) 
  → ~/.config/SMath/extensions/plugins/ca92ef03-.../config.1_5_0_9678.ini
  → 8.0.0.5/CoolPropWrapper.dll
  → Initialize / GetTermsHandled (registers 11 terms)
  → Evaluation dispatch
  → P/Invoke DllImport("CoolProp_x64")
  → Mono dllmap (~/.mono/config or CoolPropWrapper.dll.config)
  → dlopen of ~/.config/SMath/extensions/plugins/ca92ef03-.../libCoolProp.so
  → Native CoolProp 8.0.0 C/C++ engine
```

### 6.2 DllMap Configuration
The native library `libCoolProp.so` resides directly inside the plugin folder:
```text
~/.config/SMath/extensions/plugins/ca92ef03-c7da-4888-98ad-528482733e2f/libCoolProp.so
```
Mono resolves the unmanaged P/Invoke library names (`CoolProp_x64` and `CoolProp_x86`) via `CoolPropWrapper.dll.config` or global `~/.mono/config`:

```xml
<configuration>
  <dllmap dll="CoolProp_x64.dll" target="/home/ukhan/.config/SMath/extensions/plugins/ca92ef03-c7da-4888-98ad-528482733e2f/libCoolProp.so" os="!windows"/>
  <dllmap dll="CoolProp_x64"     target="/home/ukhan/.config/SMath/extensions/plugins/ca92ef03-c7da-4888-98ad-528482733e2f/libCoolProp.so" os="!windows"/>
  <dllmap dll="CoolProp_x86.dll" target="/home/ukhan/.config/SMath/extensions/plugins/ca92ef03-c7da-4888-98ad-528482733e2f/libCoolProp.so" os="!windows"/>
  <dllmap dll="CoolProp_x86"     target="/home/ukhan/.config/SMath/extensions/plugins/ca92ef03-c7da-4888-98ad-528482733e2f/libCoolProp.so" os="!windows"/>
</configuration>
```

---

## 7. Forensic Diagnostic Principles for SMath Plugin Authors

During the Linux Mono bring-up, several key testing axioms were established:

1. **In-Process Witness Beats Out-of-Process Reproduction:**
   External `ctypes` or Mono P/Invoke test scripts fed clean strings and could not reproduce string corruption occurring *inside* SMath's `GetStringParam`. Instrumenting the plugin wrapper with `mcs -d:DEBUG` to write a pre-call `log.txt` directly before native P/Invoke immediately captured the escaped transport strings.
2. **Display-Layer Unescaping Trap:**
   SMath's graphical canvas and `.sm` result caches un-escape `\NNNN\` hex codes on render. Consequently, string variables displayed on-screen or echoed through `errstring` appeared completely clean, concealing the fact that the underlying native C++ engine received escaped strings. Only logs written directly from C# variables before P/Invoke are reliable witnesses.
3. **Cross-Platform A/B Bisection:**
   Confirming that the exact same `.sm` worksheet evaluated cleanly on Windows SMath isolated the issue to the Mono/Linux transport boundary within minutes.
4. **Punctuation Testing:**
   Strings without punctuation (`Water`, `Nitrogen`) never expose escape transport bugs. Always include tests with `.`, `,`, `&`, `[`, `]`, and Unicode characters.

---

## 8. Installation & Deployment

### Method A: Install via SMath Studio Extension Manager (.zip Package)
1. Copy **`CoolPropWrapper.8_0_0_5.zip`** to your system.
2. Open SMath Studio.
3. Go to **Tools > Extensions Manager...**
4. Click **Install from file...**
5. Select `CoolPropWrapper.8_0_0_5.zip`. SMath extracts the package into `~/.config/SMath/extensions/plugins/ca92ef03-c7da-4888-98ad-528482733e2f/8.0.0.5/` and configures `config.1_5_0_9678.ini` automatically.

### Method B: Manual File Deployment
```bash
TARGET="$HOME/.config/SMath/extensions/plugins/ca92ef03-c7da-4888-98ad-528482733e2f/8.0.0.5"
mkdir -p "$TARGET"
cp CoolPropWrapper.dll CoolPropWrapper.dll.ini CoolPropWrapper.config CoolPropWrapper.dll.config libCoolProp.so "$TARGET/"

cat << 'EOF' > "$HOME/.config/SMath/extensions/plugins/ca92ef03-c7da-4888-98ad-528482733e2f/config.1_5_0_9678.ini"
[General]
version=8.0.0.5
state=0
file=CoolPropWrapper.dll
EOF
```

---

## 9. Compilation Guide

### Build Managed DLL (`build.sh`)
```bash
./build.sh
```
Compiles `bin/CoolPropWrapper.dll` using Mono `mcs` targeting SMath Desktop assemblies.

### Build Native Library (`libCoolProp.so`) from Upstream Source
```bash
git clone --branch v8.0.0 --depth 1 https://github.com/CoolProp/CoolProp.git
cd CoolProp
mkdir build && cd build
cmake .. -DCOOLPROP_SHARED_LIBRARY=ON -DCMAKE_BUILD_TYPE=Release
make -j2 CoolProp
# Result: build/libCoolProp.so (12.5 MB)
```

### Package Distribution Archive (`pack.sh`)
```bash
./pack.sh
```
Produces `CoolPropWrapper.8_0_0_5.zip` with the primary assembly entry comment set to GUID `ca92ef03-c7da-4888-98ad-528482733e2f`.

---

## 10. Verification Worksheets & Proven Artifacts

- 📁 **[`examples/coolprop_probe.sm`](examples/coolprop_probe.sm)**: Verifies pure fluid properties (`Water`, `R134a`, `Nitrogen`), saturation ancillaries, reference state shifts, and library metadata.
- 📁 **[`examples/coolprop_mix_string_probe.sm`](examples/coolprop_mix_string_probe.sm)**: Verifies multi-component hydrocarbon mixtures (`Ethane + Methane + Propane`) through the string unescaping pipeline.

### Verified Binary Specifications
- **Managed Assembly:** `CoolPropWrapper.dll` (v8.0.0.5, 35,840 bytes, SHA256 `4fa9d8f12abee784d619f55ae10b75e3a78477993a723de68a8770b36e9df06d`).
- **Native Shared Object:** `libCoolProp.so` (v8.0.0 Linux aarch64, 12,559,840 bytes, SHA256 `f9a33ad9f8a5e4767e694103bc834ddad678b5b094b1e29b33e4f2071208dab8`).
- **Distribution Archive:** `coolprop-smath-linux-aarch64.7z` (SHA256 `00fbad30...`) and `CoolPropWrapper.8_0_0_5.zip`.

---

## 11. Metadata & Plugin Identity

- **Assembly Name:** `CoolPropWrapper`
- **Assembly Title:** `CoolProp Wrapper`
- **GUID:** `ca92ef03-c7da-4888-98ad-528482733e2f`
- **Active Version:** `8.0.0.5`
- **Target Framework:** `.NET Framework 4.5 / Mono 2.0+`
- **License:** Open Source / MIT & CoolProp BSD License
