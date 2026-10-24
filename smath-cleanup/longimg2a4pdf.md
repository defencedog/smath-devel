# Long Image → A4 PDF Splitter

`longimg2a4pdf.sh` takes one very tall raster image — a Mathcad / SMath Studio
sheet export, a long screenshot, a scanned strip — scales it to the width of the
A4 text area, cuts it vertically into pages, and combines everything into a
**single multi-page A4 PDF** with **equal (“normal”) margins on all four sides**.

It is the sibling of the `remove_wm*` tools in this directory. Pure bash +
ImageMagick; no Python required.

## Behaviour at a glance

| Property | Value |
|----------|-------|
| Page size | A4 210 × 297 mm (exactly, see *Tooling notes*) |
| Margins | `25.4 mm` (1 inch, Word “Normal”) on every side — `-m` to change |
| Scale | image fitted to the A4 **text width** (159.2 mm with default margins) |
| Cut direction | vertical only (tall image assumption) |
| Page count | automatic (as few as fit) or forced with `-p N` (balanced) |
| Cut placement | snapped to a blank row near the nominal cut (`-s` to disable) |
| Assembly | lossless FlateDecode; optional `cpdf` pass for an exact A4 media box |

## Usage

```bash
./longimg2a4pdf.sh [options] INPUT_IMAGE [OUTPUT_PDF]

# Default: automatic page count, 25.4 mm margins, 300 dpi
./longimg2a4pdf.sh ../FlushWater_final_solve_units.png

# Exactly 3 pages instead of 2, with the content balanced
./longimg2a4pdf.sh -p 3 sheet.png sheet-3up.pdf

# 2 cm margins, 150 dpi (smaller file), no snapping
./longimg2a4pdf.sh -m 20 -d 150 -s tall.png
```

Without `OUTPUT_PDF` the output is `<input basename>.pdf` next to the input.

### Options

| Option | Meaning | Default |
|--------|---------|---------|
| `-p, --pages N` | use exactly `N` pages, balanced heights (`--cuts N` alias) | automatic |
| `-m, --margin MM` | margin on each side, in mm | `25.4` |
| `-d, --dpi N` | page raster resolution | `300` |
| `-b, --background C` | background / padding colour | `white` |
| `-s, --no-smart-cut` | cut at exact page-height multiples | off |
| `-w, --cut-window P` | smart-cut search window, % of the content height | `15` |
| `-t, --blank-level N` | row mean brightness (0–255) counted as blank | `254` |
| `-k, --keep-temp` | keep the generated per-page PNGs | off |
| `-h, --help` | show help | — |

## How it works (code construction)

1. **Arguments & validation.** A hand-rolled parser (no getopt) accepts the
   options above plus positional `INPUT [OUTPUT]`. Numbers are validated
   (margin/dpi/window/blank-level/page-count), the input must exist, and the
   output must not clobber the input. Default output replaces the *basename’s*
   extension (`../sheet.png → ../sheet.pdf`, hidden files keep theirs).

2. **Geometry.** All measurements are computed in pixels at the chosen DPI with
   `awk`:
   `PW×PH = round(210/25.4·DPI) × round(297/25.4·DPI)`,
   margin `OFF = round(MM/25.4·DPI)`,
   content area `CW×CH = (PW−2·OFF) × (PH−2·OFF)`.
   Smart-cut parameters: window `WIN = 15 % of CH`, minimum blank run
   `MIN_RUN = DPI/30` (≈ 0.85 mm of white).

3. **Base bitmap.** The input’s first frame is auto-oriented, its alpha channel
   is flattened onto the background colour, and it is resized to `CW` pixels
   wide (fit-to-width) — rendered **once** into a `mktemp -d` directory. Every
   later crop comes from this one bitmap, so cut coordinates are all in one
   coordinate system (`BW×BH`, e.g. 1880 × 4873 for the example sheet).

4. **Pagination.** `MIN_PAGES = ceil(BH/CH)` — the fewest pages that fit.
   *Default (fill) mode:* boundaries at `k·CH`; the remainder lands on the last
   page. If that remainder would be a sliver (`< CH/3`), the **last two pages
   are rebalanced to equal heights** instead of leaving a nearly empty page.
   *Forced mode (`-p N`):* `N ≥ MIN_PAGES` is enforced (clear error otherwise),
   boundaries at `k·BH/N` so all pages get nearly equal heights.

5. **Smart cut.** For every boundary the script probes row brightness:
   * crop a full-width strip around the nominal cut,
   * reduce it to a 1-pixel-wide column with `-scale "1xH!"` (box average per
     row at `-depth 8`),
   * parse the resulting `txt:` enumeration in a POSIX (mawk-safe) `awk`
     script, treat rows with mean ≥ `--blank-level` as **blank**, group
     consecutive blank rows into **runs**, and choose:
     1. the run of length ≥ `MIN_RUN` whose center is closest to the nominal
        cut, else
     2. the closest blank run of any length, else
     3. the nominal cut itself.
   The chosen cut is clamped to `[top+1, top+CH]` (page can never overflow);
   for the last boundary it is additionally floored at `BH−CH` so the final
   page can’t overflow either.

   *Why 254 as the blank level?* The sheet background is pure white (mean 255).
   The pale-yellow note boxes are ≈ `#F7E0A1` (gray ≈ 225), so a row crossing a
   box stays well below 254 — a cut never slices through a highlighted box.

6. **Page composition.** Each slice is padded to the full content height
   (bottom padding via `-extent`; this only matters for the last page) and
   composited onto a white `PW×PH` canvas at offset `(+OFF, +OFF)`. That is
   what makes the margins identical on all four sides, and why short pages are
   top-aligned with white space below.

7. **PDF assembly.** `magick page-*.png -units PixelsPerInch -density DPI
   -compress Zip out.pdf` — one image per page, page size = pixels ÷ density,
   Flate (lossless). If `cpdf` is installed, one more pass
   (`cpdf -mediabox "0 0 595.276 841.89"`) normalises every media box to
   exactly 210.000 × 297.000 mm.

8. **Report & verify.** The script prints the cut plan (with per-page row
   ranges and which cuts were snapped), the output size, and — via `pdfinfo` —
   the verified page count and page size. Temporary files are removed unless
   `-k` is given.

## Worked example — the FlushWater sheet

`../FlushWater_final_solve_units.png` is 738 × 1913 px. Default run:

```
Input : ../FlushWater_final_solve_units.png  (738 x 1913 px)
Page  : A4 210 x 297 mm @ 300 dpi -> 2480 x 3508 px
        margin 25.4 mm on all four sides
Scaled: fitted to text width -> 1880 x 4873 px  (x2.547)
Pages : 2
  page 1: rows      0 .. 2859    (height  2859 px)   (snapped to a blank line)
  page 2: rows   2859 .. 4873    (height  2014 px)

Wrote : ../FlushWater_final_solve_units.pdf  (1.8M)
        2 page(s), A4 210 x 297 mm, 25.4 mm margins
Verify: pages=2  page size=595.2 x 841.89 pts (A4)
```

The nominal cut at row 2908 would have ended mid-equation; the smart cut moved
it 49 px up into the big white gap, so:

* **page 1** ends cleanly after `Enthalpy_D := Enthalpy_C`
  (title, tank block, diagram, temperature + enthalpy sections),
* **page 2** starts with the “Make up water requirement” note box and contains
  the solve loop, the `Flush_B` matrix, the chart and the `augment[...]` lines.

Rendered at 110 dpi for inspection: content sits inside a uniform 25.4 mm white
border on both pages; no row of the source is missing (slices are contiguous by
construction). PyMuPDF reports both pages as `210.000 × 297.000 mm` (see
*Tooling notes* for why `pdfinfo` prints `595.2`).

With `-p 3` the same sheet becomes three balanced pages
(1650 / 1768 / 1455 px tall after snapping); with `-p 2` the output is
identical to the default above.

## Verified cases

| Input / command | Result |
|-----------------|--------|
| FlushWater default | 2 pages, cut snapped 2908 → 2859 in a blank gap |
| `-p 2` | identical to default (min pages ⇒ proven fill cut) |
| `-p 3` | 3 balanced pages, each cut snapped to a blank line |
| `-p 1` | clean error: “minimum is 2 page(s)” |
| `-p 0` | clean error: “invalid page count” |
| `-s` (no smart cut) | exact cuts 0 / 2908 / 4873 → heights 2908 / 1965 |
| `-m 20 -d 150` | 1240 × 1754 px pages, content 1004 px wide, 2 pages |
| 738 × 5739 px (3× stacked) | 6 pages default (sliver rule exercised); `-p 8` → 8 balanced pages |
| image shorter than one page | single page, top-aligned |
| JPEG input | same plan as the PNG original |
| `-m 120` | clean error: margin too large for A4 |

Handy verification snippets:

```bash
pdfinfo out.pdf | grep -E 'Pages|Page size'
pdftoppm -png -r 110 out.pdf check        # then look at check-1.png, check-2.png
~/venv_engg/bin/python -c 'import pymupdf,sys; \
  [print(p.number, p.mediabox) for p in pymupdf.open(sys.argv[1])]' out.pdf
```

## Tuning notes

| Knob | Effect |
|------|--------|
| `-m 20` | switch from Word “Normal” (1 in) to the 2 cm margins common in Europe |
| `-d 150` | smaller file (~⅓), still fine on screen; `300` is print-safe |
| `-p N` | force page count; pages get shorter, nothing is cropped, no content is lost |
| `-s` | purely arithmetic cuts (no blank-row search); useful for debugging |
| `-w 25` | larger snap window: easier to find a blank row, page heights vary more |
| `-t 250` | loosen “blank” in case a document has an off-white background |
| `-b '#f5f5f5'` | tinted padding if you dislike pure white |

The sheet is scaled to the text width, so printed text size is fixed by that
fit (≈ 6–7 pt equivalent for this 738-px-wide sheet). Raising `-d` makes the
print smoother, not the text bigger — bigger text would need a wider page.

## Tooling notes

* Tested with ImageMagick 7.1.2 (Q16). IM6 is supported via the `convert`
  fallback; the script picks whichever of `magick` / `convert` exists.
* The row-brightness probe is `-colorspace gray -depth 8 -scale "1xH!"` plus
  a `txt:` parse — no temporary files, one pipeline per boundary.
* The `awk` program is POSIX-only (mawk): no gawk features (e.g. no 3-argument
  `match()`), so it runs on every Debian/Ubuntu default.
* Construction gotcha: `read -r W H < <(identify -format '%w %h' …)` fails
  under `set -e` because `read` returns 1 when the output has no trailing
  newline — the script’s format strings always end with `\n` (and the reads
  are additionally guarded).
* IM’s PDF page size is pixels ÷ density: a 2480-px-wide page at 300 dpi is
  595.2 × 841.92 pt (0.03 mm off A4). The optional `cpdf -mediabox` pass makes
  it exactly 595.276 × 841.89 pt. `pdfinfo` *displays* `595.2 x 841.89 pts
  (A4)` due to rounding, but the media box really is 210.000 × 297.000 mm.
* PDF pages are FlateDecode (lossless) — no JPEG artifacts, safe for thin
  equation strokes and small fonts.
* No Python / venv dependency; `cpdf` and `pdfinfo` are optional (the script
  degrades gracefully when they are missing).
