# calculations/ — finished engineering calculations

Completed SMath Studio calculations, published as-is. Each calculation is one topic and appears as
**three artifacts sharing the same base name** — no subfolders:

| Extension | Artifact | Purpose |
| :--- | :--- | :--- |
| `.sm` | SMath Studio worksheet | The working calculation — open it in SMath to inspect or re-run |
| `.md` | Companion write-up | Derivation, method, standards basis, and verification tables |
| `.pdf` | Rendered output | The evaluated worksheet, so results are readable without SMath |

Both the `.md` and the `.pdf` are the author's own furnished output, reproduced here unchanged.

## Index

| Calculation | Standards basis | Pages | Companion |
| :--- | :--- | ---: | :--- |
| `Air_Compressor_Capacity_Isothermal_Trap` | ISO 1217 Annex C / CAGI Compressed Air Challenge | 9 | [`.md`](Air_Compressor_Capacity_Isothermal_Trap.md) · [`.pdf`](Air_Compressor_Capacity_Isothermal_Trap.pdf) · [`.sm`](Air_Compressor_Capacity_Isothermal_Trap.sm) |

### Air_Compressor_Capacity_Isothermal_Trap

Estimates the delivered capacity of an industrial air compressor from a closed-volume receiver
pump-up test — the pressure/temperature rise recorded while charging a known receiver.

The write-up derives the problem from first principles (ideal gas law and a control volume), then
works it two independent ways and compares them:

- **Approach A — discrete numerical calculus.** Plant DCS historian vectors are differentiated and
  integrated numerically: finite-difference derivatives, composite trapezoidal quadrature
  (`trapz_vec`) and SMath's native 4-argument summation operator, with a procedural RK4 solver as a
  forward physical check. Verified against a 15-metric diagnostic vector.
- **Approach B — continuous analytical calculus.** Smooth curves are fitted to the telemetry and the
  thermodynamics is evaluated with exact symbolic calculus, giving a closed-form result. Verified
  against a 9-metric diagnostic vector.

The two approaches agree to machine precision. The write-up also quantifies the error introduced by
the common isothermal shortcut: assuming a constant temperature during pump-up inflates the
estimated capacity, and §4.5 puts a number on that overestimation.

To view it: open the `.pdf` (or the `.md`) directly. The `.sm` needs SMath Studio — install one of
the official builds from [`../setup/`](../setup/), and note that the PDFs in this repo were produced
on Mono/Linux, so [`../font-pack/`](../font-pack/) and [`../print-patch/`](../print-patch/) are what
make the symbols and printing come out right there.

## Adding a calculation

Drop the three artifacts in this folder using **one identical base name**, so the triple stays
together:

```
calculations/
├── README.md
└── <Calculation_Name>.sm
    <Calculation_Name>.md
    <Calculation_Name>.pdf
```

No subfolders — the flat layout is deliberate, so a calculation is one name to search for and its
three files sort together.
