# SMath Studio (Mono) Linux Font Pack & Symbol Rendering Fix

A complete, self-contained font pack and configuration fix for **SMath Studio Desktop (Mono Edition)** on Linux (x86_64 and ARM64 / aarch64).

---

## The Problem

When running SMath Studio on Linux under Mono, mathematical equations, chemical reactions, and engineering notations frequently display **blank rectangles ($\square$)** instead of proper characters:

- **Chemical reaction arrows**: `→` (U+2192), `⇌` (U+21CC, reversible equilibrium), `⇄` (U+21C4), `⇒` (U+21D2).
- **Subscript numerals**: `₀ ₁ ₂ ₃ ₄ ₅ ₆ ₇ ₈ ₉` (e.g., in $\text{H}_2\text{O}$ or $\text{H}_2 + \text{Cl}_2 \rightarrow 2\,\text{HCl}$).
- **Relational and engineering operators**: `≤` (U+2264), `≥` (U+2265), `≡` (U+2261), `≈` (U+2248), `Δ` (U+0394), `°` (U+00B0).

### Why Does This Happen?
1. **Incomplete Linux "Arial"**: SMath Studio defaults to `Arial`. On Linux, the standard `msttcorefonts` package installs the vintage 1996 Windows core fonts (`Arial.ttf` v2.82). This font contains only 1,184 basic characters and **completely lacks** chemical equilibrium arrows, double arrows, and subscript numerals (only 44% symbol coverage).
2. **No Font Linking in Mono's `libgdiplus`**: On Windows, Windows GDI+ automatically borrows missing glyphs from *Segoe UI Symbol* or *Cambria Math* via registry font linking. On Linux, Mono's `libgdiplus` renders directly through Cairo / FreeType without multi-font fallback: whenever a character is not in the active font file, it renders glyph `0` (the hollow `.notdef` rectangle).
3. **Missing Windows ClearType & Math Fonts**: Worksheets authored on Windows frequently specify `Segoe UI`, `Segoe UI Symbol`, `Cambria`, `Cambria Math`, `Calibri`, or `Consolas`, which are not included with standard Linux installations.

---

## Quick Start (Automated Install)

Run the installer with `sudo` (recommended for system-wide availability):

```bash
chmod +x install.sh
sudo ./install.sh
```

*(If you do not have sudo/root privileges, simply run `./install.sh` to install fonts and configs into your user home directory).*

### Verify Installation
Run the included verification script:
```bash
./verify/verify_fonts.sh
```

---

## Manual Installation Instructions

If you prefer to install manually or need to integrate into custom system images, follow these steps:

### Step 1: Install System Prerequisites (Debian / Ubuntu / Kubuntu)
```bash
sudo apt-get update
sudo apt-get install -y fonts-symbola fonts-freefont-ttf fonts-dejavu fonts-noto-extra fontconfig
```

### Step 2: Copy Font Files
Copy all font files from the `fonts/` directory to the system font directory:

```bash
sudo mkdir -p /usr/local/share/fonts/ms-cleartype
sudo cp fonts/* /usr/local/share/fonts/ms-cleartype/
sudo chmod 644 /usr/local/share/fonts/ms-cleartype/*
```

*(Or for single-user install without root)*:
```bash
mkdir -p ~/.local/share/fonts/ms-cleartype
cp fonts/* ~/.local/share/fonts/ms-cleartype/
chmod 644 ~/.local/share/fonts/ms-cleartype/*
```

### Step 3: Configure Fontconfig Aliasing & Fallbacks
Fontconfig must map requests for `Arial` and other symbol-deficient fonts to `DejaVu Sans` (which has 100% symbol coverage).

**System-wide**:
```bash
sudo cp fontconfig/99-smath-mono-fonts.conf /etc/fonts/local.conf
sudo chmod 644 /etc/fonts/local.conf
```

**User-level** (guarantees per-user priority):
```bash
mkdir -p ~/.config/fontconfig
cp fontconfig/fonts.conf ~/.config/fontconfig/fonts.conf
chmod 644 ~/.config/fontconfig/fonts.conf
```

### Step 4: Configure SMath Studio Preferences
Ensure SMath Studio natively uses `DejaVu Sans`:

1. **Edit `~/.config/SMath/settings.inf`**:
   Make sure `FontName` is set to `DejaVu Sans` and `FontSize` is set to `9`:
   ```ini
   FontName=DejaVu Sans
   FontSize=10
   ```
   *(Or via command line)*:
   ```bash
   sed -i 's/^FontName=.*/FontName=DejaVu Sans/' ~/.config/SMath/settings.inf
   sed -i 's/^FontSize=.*/FontSize=10/' ~/.config/SMath/settings.inf
   ```

2. **Update Default Text Styles in `~/.config/SMath/extensions/WriterManager/styles.xml`**:
   ```bash
   cp smath-configs/styles.xml ~/.config/SMath/extensions/WriterManager/styles.xml
   ```

### Step 5: Refresh the Font Cache
```bash
fc-cache -fv
```

---

## Package Contents & File Inventory

```
smath-linux-font-pack/
├── README.md                      # This documentation
├── install.sh                     # Automated 1-step installer
├── uninstall.sh                   # Reverts changes and restores backups
├── fonts/                         # 42 TrueType/OpenType font files (46 MB total)
│   ├── CAMBRIA.TTC                # Microsoft Cambria & Cambria Math (full math tables)
│   ├── CAMBRIAB.TTF, ...          # Cambria Bold, Italic, Bold Italic
│   ├── CALIBRI.TTF, ...           # Microsoft Calibri family (Regular, Bold, Italic, Bold Italic)
│   ├── CONSOLA.TTF, ...           # Microsoft Consolas family (monospace)
│   ├── CANDARA.TTF, ...           # Microsoft Candara family
│   ├── CONSTAN.TTF, ...           # Microsoft Constantia family
│   ├── CORBEL.TTF, ...            # Microsoft Corbel family
│   ├── MEIRYO.TTC, MEIRYOB.TTC    # Microsoft Meiryo / Asian & symbols suite
│   ├── tahoma.ttf, tahomabd.ttf   # Microsoft Tahoma & Tahoma Bold
│   ├── seguisym.ttf               # Microsoft Segoe UI Symbol (7,362 technical & math symbols)
│   ├── segoeui*.ttf, segui*.ttf   # Microsoft Segoe UI full family (13 weights/variants)
│   └── Symbola.ttf                # Symbola Unicode symbol font
├── fontconfig/
│   ├── 99-smath-mono-fonts.conf   # System configuration (for /etc/fonts/local.conf)
│   └── fonts.conf                 # User configuration (for ~/.config/fontconfig/fonts.conf)
├── smath-configs/
│   ├── styles.xml                 # SMath WriterManager default styles configured for DejaVu Sans
│   └── settings.inf.snippet       # SMath settings.inf reference snippet
└── verify/
    └── verify_fonts.sh            # Programmatic font test & verification script
```

---

## Verification & Test Results

The included `verify/verify_fonts.sh` programmatically renders chemical formulas and arrows using Mono's `System.Drawing.Graphics.DrawString`:

$$\text{H}_2(\text{g}) + \text{Cl}_2(\text{g}) \rightarrow 2\,\text{HCl}(\text{g})$$
$$\text{N}_2(\text{g}) + 3\,\text{H}_2(\text{g}) \rightleftharpoons 2\,\text{NH}_3(\text{g})$$
$$\text{A} + \text{B} \rightleftarrows \text{C} + \text{D} \quad (\Delta H^\circ \le 0)$$

### Before vs. After Benchmark

| Character / Symbol | Before Fix (`Arial.ttf`) | After Fix (Font Pack) | Visual Result |
| :--- | :---: | :---: | :--- |
| `→` (U+2192, right arrow) | 68 px | **84 px** | Rendered arrow |
| `⇌` (U+21CC, equilibrium) | **57 px** ($\square$ box) | **148 px** | Authentic double harpoon |
| `⇄` (U+21C4, reversible) | **57 px** ($\square$ box) | **176 px** | Authentic double arrow |
| `₂` (U+2082, subscript 2) | **57 px** ($\square$ box) | **49 px** | Authentic subscript numeral |
| `≤` (U+2264, less than / equal) | 108 px | **140 px** | Rendered operator |
| `Δ` (U+0394, Greek Delta) | 152 px | **164 px** | Rendered glyph |

*(Note: In the unpatched state, `⇌`, `⇄`, and `₂` all generated exactly 57 ink pixels in Arial, which is the exact ink footprint of the `.notdef` empty box).*

---

## Uninstallation

To remove the installed fonts, revert the Fontconfig rules, and restore your previous SMath configuration backups:

```bash
sudo ./uninstall.sh
```
