# smath-devel

Working repository for getting **SMath Studio Desktop (Mono Edition)** fully usable on Linux, plus
the engineering work built on top of it.

Everything here was developed and verified against **SMath Studio 1.5.0.9678** on Linux
(Ubuntu 26.04 LTS / ARM64, aarch64), running under **Mono 6.14** with `libgdiplus` and WinForms.

> **Start here:** [`setup/`](setup/) has the exact two official installers this repo was tested
> against. Everything below is a fix, tool, plugin or calculation that sits on top of that baseline.

---

## What's in here

| Folder | What it's for |
| :--- | :--- |
| [`setup/`](setup/) | The two official **SMath Studio 1.5.0.9678** installers (Windows `.msi` and Mono `.tar.gz`) — the proven baseline every patch here was derived from |
| [`font-pack/`](font-pack/) | Makes maths and chemistry **render**: fixes missing symbols (reaction arrows, subscripts, `≤` `Δ` `°`) that Mono draws as empty `.notdef` boxes. Installer, fontconfig rules and verification script |
| [`print-patch/`](print-patch/) | Makes **printing work**: a CUPS virtual `PDF` printer plus a one-byte `SMath.Document.dll` IL patch that stops blank pages and the Print Preview `InvalidParameter` error |
| [`winform-patch/`](winform-patch/) | Stops the **file dialog crashing**: a patched Mono `System.Windows.Forms.dll` for a `NullReferenceException` when the view is switched List → Details, which also opens the dialog in Details by default |
| [`plugin/`](plugin/) | Source trees and ready-to-import packages for **six SMath plugins** — IF97, PsychroLib, CoolPropWrapper, PhreeqcWrapper, SwarmFit, Optim |
| [`smath-cleanup/`](smath-cleanup/) | **PDF post-processing** for SMath's own export: watermark removal (PyMuPDF backend plus a cpdf-only fallback) and a tall-image → A4 splitter |
| [`calculations/`](calculations/) | **Finished engineering calculations**, each as three same-named artifacts — `.sm` worksheet, `.md` write-up, `.pdf` render |

Every folder has its own `README.md` with the detail: root cause, install steps, verification
tables, and caveats.

---

## The three fixes, in one paragraph each

**Fonts** — SMath defaults to `Arial`, and the Linux `msttcorefonts` Arial is the 1996 build with
only 1,184 glyphs: no equilibrium arrows, no subscript numerals. Windows papers over this with GDI+
registry font linking; Mono's `libgdiplus` has no multi-font fallback, so a missing codepoint becomes
glyph 0 — the hollow box. [`font-pack/`](font-pack/) installs the ClearType and symbol suites, adds
Fontconfig aliases that map `Arial` to `DejaVu Sans` (63/63 symbol coverage), and points SMath at
`DejaVu Sans` directly.

**Printing** — two independent defects. Mono enumerates printers *only* through CUPS, and no CUPS
server was installed, so the print dialog was empty and Print exited the app. Then, with a printer
present, custom paper (`PaperId = 0`) matched Mono's bogus zero-sized `Custom` entry and produced a
0×0 PostScript page — blank output. [`print-patch/`](print-patch/) installs CUPS + `cups-pdf` and
patches one byte in `SMath.Viewer.Printing::InitPrinting` so custom paper keeps its real dimensions.

**File dialog** — this one is Mono's bug, not SMath's. `MenuTracker.HideSubPopups` null-checks
`menu.parent_menu` but not `menuTracker` itself, so switching the dialog from List to Details throws
a `NullReferenceException` out of the X11 mouse-up handler and takes the dialog and the main window
with it. [`winform-patch/`](winform-patch/) ships a patched `System.Windows.Forms.dll` that adds the
missing guard and defaults the dialog to Details view.

Each fix is independent — you can apply any subset.

---

## Reproducing a working install

```bash
# 1. Baseline — the exact installers this repo was tested against
tar xzf setup/SMathStudioDesktop.1_5_0_9678.Mono.tar.gz -C ~/Binaries/SMathStudioDesktop.1_5_0_9678.Mono

# 2. Symbols render instead of boxes
cd font-pack && sudo ./install.sh && ./verify/verify_fonts.sh

# 3. Printing produces real pages
cd ../print-patch && python3 apply-patch.py

# 4. The file dialog opens (and opens in Details)
cd ../winform-patch && ./src/apply.sh ~/Binaries/SMathStudioDesktop.1_5_0_9678.Mono

# 5. Run it
~/Binaries/SMathStudioDesktop.1_5_0_9678.Mono/smathstudio_desktop_mono
```

Run each folder's `README.md` for the manual route, verification steps and how to revert. Each patch
preserves the original: `print-patch/apply-patch.py` writes a dated
`.bak-YYYYMMDD-blankprint` before editing and **aborts** if the IL pattern is missing or ambiguous,
and `winform-patch/src/apply.sh` copies the untouched GAC assembly to `<target>/backup/` on first
run. Neither will silently corrupt the install.

**After any SMath update, re-run `print-patch/apply-patch.py`** — updating replaces the assembly, and
the patch is lost.

---

## Notes

- **Platform.** The fixes target Linux/Mono. `font-pack/` and `print-patch/` are Linux-only by
  nature. In [`smath-cleanup/`](smath-cleanup/) only `remove_wm.py` runs on native Windows; the three
  `.sh` tools need a POSIX shell. The plugins build on Windows (PowerShell + .NET SDK) and ship
  Windows DLLs, with a Linux `.so` alongside.
- **`build.ps1` has not been run here.** The plugin trees are verified statically — link targets,
  `<Compile Include>` resolution, package contents, hashes — not by compiling. Treat them as
  source-complete rather than build-tested on this machine.
- **Licensing.** Third-party material is tracked deliberately and documented per folder:
  `font-pack/fonts/` holds Microsoft ClearType fonts (not freely redistributable),
  `print-patch/` a modified proprietary SMath assembly (© SMath LLC),
  `winform-patch/` a modified Mono assembly (MIT, `LICENSE-Mono.md` included), and `setup/` the
  vendor's own unmodified installers. The plugin trees are freely licensed (Apache-2.0 / MIT / USGS
  public domain) and five of the six `dist/` zips ship a `LICENSE` — Phreeqc needs none, its USGS
  data being public domain. Original work — scripts, fontconfig rules, config templates,
  documentation, calculations — is the author's.