# smath-devel — SMath Studio on Linux: Font Rendering & Print Fixes

Development home for fixes that make **SMath Studio Desktop (Mono Edition)** usable on Linux
(verified on Ubuntu 26.04 LTS / ARM64, aarch64).

| Folder | Purpose |
| :--- | :--- |
| [`font-pack/`](font-pack/) | Self-contained font pack + config that fixes missing math/chemistry symbols (installer, fontconfig rules, SMath settings, verification script) |
| [`print-patch/`](print-patch/) | Print fix: CUPS virtual `PDF` printer setup, plus a one-byte `SMath.Document.dll` IL patch that stops blank pages and the Print Preview `InvalidParameter` error |

The sections below summarise the **font** fix, adapted from the original write-up
`SMATH_STUDIO_FONT_FIX.md` (root-cause analysis, files installed, before/after measurements).
The **print** fix — a different root cause — is documented in
[`print-patch/README.md`](print-patch/README.md), from `SMATH_STUDIO_PRINT_FIX.md`.

---

## 1. Problem

On Linux under Mono, chemical reactions, engineering notations, and formulas rendered as
**empty hollow rectangles** (`□`, the `.notdef` fallback glyph) instead of real characters:

- **Chemical reaction arrows** — `→` (U+2192), `⇌` (U+21CC, equilibrium), `⇄` (U+21C4, reversible).
- **Subscript numerals in formulas** — `2`, `3`, … (e.g. `H₂O`, `H₂ + Cl₂ → 2 HCl`).
- **Relational / engineering symbols** — `≤` (U+2264), `≥` (U+2265), `≡` (U+2261), `Δ` (U+0394), `°` (U+00B0).

---

## 2. Root cause

An architectural gap between **Windows GDI+** and **Mono's Linux `libgdiplus`** in font glyph lookup:

```
[SMath Studio on Windows]
  Text ("Arial") ──► GDI+ / DirectWrite
                       ├── in Arial.ttf? ──► render glyph
                       └── missing?       ──► Uniscribe font linking (registry)
                                              └── fallback to Segoe UI Symbol / Cambria Math (seamless)

[SMath Studio on Linux (Mono)]
  Text ("Arial") ──► Mono System.Drawing ──► libgdiplus.so
                       ├── Fontconfig resolves "Arial" ──► single face: Arial.ttf (msttcorefonts)
                       └── FreeType FT_Get_Char_Index(face, codepoint)
                             ├── missing in Arial.ttf!
                             └── NO multi-font fallback ──► glyph 0 (.notdef rectangle)
```

### Technical breakdown

1. **Default font.** SMath defaults to `FontName=Arial`, in `~/.config/SMath/settings.inf` and
   `~/.config/SMath/extensions/WriterManager/styles.xml`.
2. **Severely incomplete legacy fonts.** The standard `ttf-mscorefonts-installer` provides the **1996**
   Core Fonts (`Arial.ttf` v2.82), covering only 1,184 basic Latin/Cyrillic glyphs — **0 %** coverage of
   reaction arrows (`⇌ ⇋ ⇄ ⇆ ⇒ ⇐`), subscript numerals (U+2080–U+2089), or advanced operators
   (`≡ ≈ ∈ ∉`). Empirically, Arial covered only **28 of 63 (44.4 %)** common engineering/chemistry symbols.
3. **No font fallback in Mono `libgdiplus`.** It implements `System.Drawing` via **Cairo + FreeType**
   (`src/text-cairo.c`). It builds one `FT_Face` from the single file Fontconfig returns for "Arial";
   when `FT_Get_Char_Index` returns `0`, Cairo draws the `.notdef` box. Fontconfig is never queried
   per character for a fallback.
4. **Missing Windows ClearType / math fonts.** Worksheets from Windows often name `Segoe UI`,
   `Segoe UI Symbol`, `Cambria`, `Cambria Math`, `Calibri`, `Consolas` — none present on a stock Linux host.

---

## 3. Solution

A three-pronged fix that does **not** break existing worksheets:

1. Install the missing Windows ClearType, math, and Unicode symbol font suites.
2. Add **Fontconfig alias rules** so requests for symbol-deficient fonts (`Arial`, `Helvetica`,
   `Times New Roman`, …) map to fonts with full Unicode symbol coverage (`DejaVu Sans` / `DejaVu Serif`).
3. Point SMath's own preferences at `DejaVu Sans` natively.

### Installed packages (APT)

```bash
sudo apt-get install -y fonts-symbola fonts-freefont-ttf fonts-dejavu fonts-noto-extra fonts-noto-color-emoji
```

### Font sources (official redistribution points, not third-party mirrors)

- **Microsoft PowerPoint Viewer 2007 Redistributable** — legal extraction source for the ClearType
  family (Cambria/Cambria Math, Calibri, Consolas, Candara, Constantia, Corbel, Meiryo):
  `https://web.archive.org/web/20171225132744if_/https://download.microsoft.com/download/E/6/7/E675FFFC-2A6D-4AB0-B3EB-27C9F8C8F696/PowerPointViewer.exe`
- **IE International Language Pack** — Tahoma / Tahoma Bold: `https://downloads.sourceforge.net/corefonts/OldFiles/IELPKTH.CAB`
- **Segoe UI + Segoe UI Symbol suite** — `segoeui*.ttf`, `segui*.ttf`, `seguisym.ttf` (7,362 symbols).

Installed to `/usr/local/share/fonts/ms-cleartype/` (`root:staff 0644`).

### Configuration

- **`/etc/fonts/local.conf`** (system) and **`~/.config/fontconfig/fonts.conf`** (user, to guarantee
  per-user load priority) carry the alias rules — `Arial`→`DejaVu Sans`, `Helvetica`→`DejaVu Sans`,
  `Times New Roman`→`DejaVu Serif`, `Courier New`→`DejaVu Sans Mono`, `MS Sans Serif`→`Segoe UI`.
- **SMath settings** (`settings.inf` line 26 → `FontName=DejaVu Sans`; `styles.xml` line 4 →
  `font-family="DejaVu Sans"`), each with a `.bak` backup.

Both configuration files ship in [`font-pack/fontconfig/`](font-pack/fontconfig/), and the SMath
templates in [`font-pack/smath-configs/`](font-pack/smath-configs/).

---

## 4. Verification

Font coverage over 63 common engineering/chemistry symbols:

| Font | Coverage |
| :--- | :--- |
| Arial (`msttcorefonts`) | 28 / 63 — **44.4 %** (no equilibrium arrows or subscripts) |
| Liberation Sans | 45 / 63 — 71.4 % (missing equilibrium arrows) |
| **DejaVu Sans** | **63 / 63 — 100 %** |
| **Segoe UI Symbol** | **63 / 63 — 100 %** (7,362 glyphs) |

Mono `System.Drawing` pixel-rendering benchmark (`Graphics.DrawString` onto an off-screen bitmap):

| Glyph | Before (`Arial.ttf`) | After (fix) | Result |
| :--- | :---: | :---: | :--- |
| `→` (U+2192) | 68 px | **84 px** | rendered glyph |
| `⇌` (U+21CC) | **57 px** (box) | **148 px** | authentic equilibrium arrows |
| `⇄` (U+21C4) | **57 px** (box) | **176 px** | authentic double arrows |
| `2` (U+2082) | **57 px** (box) | **49 px** | authentic subscript numeral |
| `≤` (U+2264) | 108 px | **140 px** | rendered glyph |
| `Δ` (U+0394) | 152 px | **164 px** | rendered glyph |

Before the fix, `⇌`, `⇄`, and `2` all measured exactly **57 ink pixels** — the exact footprint of the
`.notdef` empty box.

---

## 5. Install

```bash
cd font-pack
chmod +x install.sh
sudo ./install.sh        # or ./install.sh for a user-local install
./verify/verify_fonts.sh # confirm
```

See [`font-pack/README.md`](font-pack/README.md) for manual steps and `uninstall.sh` to revert.

### Launch

```bash
~/Binaries/SMathStudioDesktop.1_5_0_9678.Mono/smathstudio_desktop_mono
```

---

## Licensing note

The `font-pack/fonts/` directory holds **Microsoft ClearType fonts** (Segoe UI, Calibri, Cambria,
Consolas, Tahoma, Meiryo …). They are redistributable only under the terms of the Microsoft products
they ship with; treat them as **not** freely redistributable.

`print-patch/SMath.Document.dll.patched` is a **modified proprietary binary** (SMath Studio,
© SMath LLC). It is redistributed here as a one-byte-modified build for interoperability and
remains under its original licence.

Everything else (scripts, fontconfig rules, SMath config templates, the patch script, and this
documentation) is original work.