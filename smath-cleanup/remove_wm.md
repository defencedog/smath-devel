# SMath Studio Watermark Removal

Tools to strip the **SMath Studio free-version watermarks** from a PDF, leaving
all real content — text, formula boxes and illustrations — untouched.

## The watermarks

SMath Studio's free version stamps **two raster image artifacts on every page**:

| Position | Image artifact text | Notes |
|----------|--------------------|-------|
| **Top**     | `Created using a free version of SMath Studio` | spans most of the page width |
| **Bottom**  | `Not for commercial use` | shorter strip |

They are **images**, not selectable text. That is why a plain text extract
(`pdftotext`) shows the running header and page numbers but *not* the watermark
wording — the watermark only appears when the page is rendered to pixels.

They are drawn as image XObjects in a tiny grey palette (note the values can be
off by a level or two, depending on the exporting colour space):

| Role | Colour |
|------|--------|
| glyph fill | `#D3D3D3` (light grey) |
| glyph outline | `#2F4F4F` (dark slate grey) |
| shading | `#696969` (dim grey) |
| antialias fringe | `#A9A9A9` (dark grey) |

Two complications shape the design:

1. **The running header is also a grey raster image**, sitting directly above
   the top watermark (`<date> - <file>.sm`). It is not a watermark and must
   survive. It is drawn in `#A9A9A9` only, and the few dim-grey antialias
   pixels it contains are well under 0.1 % of its area — the watermark's greys
   cover several percent — so palette plus density separate the two cleanly.
2. **On some pages content overlaps the watermark band.** SMath then rasterises
   the overlap into a single bitmap containing the watermark *and* real content
   (a formula-box border, the tops of glyphs). Deleting those draws would erase
   content, so their watermark pixels are inpainted instead.

## Files

| File | Role |
|------|------|
| `remove_wm.py` | All of the logic: cleaning plus the `--verify` self-test. |
| `remove_wm.sh` | Thin wrapper that runs `remove_wm.py` with `python3` from `PATH` (override with `PYTHON=…`); adds `--self-test`. |

## Requirements

The Python backend needs **PyMuPDF**, **NumPy** and **SciPy**. Install them either system-wide
(Debian/Ubuntu — no venv needed) or into a virtual environment:

```bash
# Debian / Ubuntu (recommended)
sudo apt install python3-pymupdf python3-numpy python3-scipy

# or any platform, via pip (into a venv to avoid touching system Python)
python3 -m venv ~/.venv-smath && . ~/.venv-smath/bin/activate
pip install pymupdf numpy scipy
```

`tesseract` is **optional** — only the OCR half of `--self-test` uses it; without it that single
check is reported as skipped.

## Usage

```bash
# Wrapper (uses python3 from PATH; PYTHON=/path/to/python to override):
./remove_wm.sh [--self-test] [input.pdf] [output.pdf]

# Direct:
python3 remove_wm.py [input.pdf] [output.pdf] [-v]
```

**Defaults**

- input  = `sample.pdf`
- output = `<input>_clean.pdf` (e.g. `sample_clean.pdf`)

**Examples**

```bash
./remove_wm.sh                                  # sample.pdf -> sample_clean.pdf
./remove_wm.sh sample2.pdf sample2_clean.pdf    # explicit paths
./remove_wm.sh -v                               # report every image touched
./remove_wm.sh --self-test                      # clean, then verify the result
```

## Self-test

`--self-test` cleans as usual and then checks the result, printing one
`[PASS]`/`[FAIL]` line per check and exiting non-zero if anything is wrong:

| Check | What it proves |
|-------|----------------|
| page count | nothing was dropped from the document |
| searchable text unchanged | text layer byte-identical to the source |
| watermark pixels gone from top/bottom bands | no grey watermark pixel survives in the watermark bands (near-flat bitmaps only, so charts reaching into the margin are not misjudged) |
| OCR of watermark bands | neither `Created using a free version…` nor `Not for commercial use` is readable in the margins |
| running header present on every page | the grey header bitmap was not swept away with the watermark |
| body images preserved | every image outside the watermark bands is unchanged (rect + SHA-1 of its pixels) — this is what protects page-body illustrations |

```
$ ./remove_wm.sh --self-test
sample.pdf -> sample_clean.pdf: removed 43 watermark image(s), cleaned 10 flattened bitmap(s)
self-test: sample.pdf -> sample_clean.pdf
  [PASS] page count                                  (8 -> 8)
  [PASS] searchable text unchanged
  [PASS] watermark pixels gone from top/bottom bands (0 signature pixel(s) left)
  [PASS] OCR of watermark bands                      (16 band(s) read)
  [PASS] running header present on every page
  [PASS] body images preserved
PASS: output looks clean
```

Pointing it at an *uncleaned* PDF deliberately fails, which makes it usable as
a regression test:

```
$ python3 remove_wm.py --verify sample.pdf sample.pdf
  [FAIL] watermark pixels gone from top/bottom bands (782360 signature pixel(s) left)
  [FAIL] OCR of watermark bands                      (16 band(s) read)
FAIL: output is not clean            # exit status 1
```

The OCR check needs `tesseract`. Without it the self-test still runs and reports
that check as skipped; the palette check is deterministic and needs no OCR.

## How it works

1. **Open** the source PDF with PyMuPDF.
2. For each page, collect every drawn image with its placement rectangle and
   pixel data.
3. **Find the watermark bands.** Bands are seeded from images in the top/bottom
   margins that consist of nothing but the watermark greys (matched with a
   tolerance, since exports shift the exact values). A band is then grown to
   cover adjacent bitmaps that the watermark was flattened into — those still
   carry watermark-coloured pixels, but also real content.
4. **Remove pure watermark draws.** Each watermark is a self-contained
   `q … cm /Rxx Do Q` block, so the block is deleted from the content stream:
   nothing is painted. No white-out rectangles, no hidden ink.
5. **Inpaint flattened bitmaps.** Where a bitmap mixes the watermark with
   content, only the watermark-coloured pixels are replaced. Each one is filled
   from the nearest unmasked pixel along eight ray directions: if both sides of
   a cross-section agree on a colour (a box border is black to the left and
   right, open background is white/yellow), that colour wins; remaining pixels
   take a distance-weighted vote. This keeps box borders continuous instead of
   punching notches into them. Content pixels are never modified.
6. **Save** with `garbage=3`, which collects the now-unreferenced watermark
   image XObjects and deflate-compresses the output.

Because detection is palette- and position-based rather than page-specific, body
illustrations (which the sample2 document has one of, mid-page) are untouched.

### What is preserved

- The running header on every page
- Page-number footers (`1/8`, `2/8`, …)
- All worksheet body content, and body illustrations pixel-for-pixel
- The searchable text layer (it is not touched at all)

## Verification

Run `./remove_wm.sh --self-test` first — it automates the checks below. To do it
by hand (or to understand what the self-test does), note that the watermarks are
images, so verify by **rendering to pixels** and OCR-ing the top and bottom
bands:

```bash
python3 - <<'PY'
import os, subprocess, tempfile
import pymupdf
doc = pymupdf.open("sample_clean.pdf")
for i, page in enumerate(doc):
    r = page.rect
    for band, box in (("top", pymupdf.Rect(0, 0, r.width, 55)),
                      ("bot", pymupdf.Rect(0, r.height - 45, r.width, r.height))):
        png = os.path.join(tempfile.gettempdir(), f"{band}{i+1}.png")
        page.get_pixmap(dpi=300, clip=box).save(png)
        text = subprocess.run(["tesseract", png, "-", "--psm", "6"],
                              capture_output=True, text=True).stdout
        print(i + 1, band, " ".join(text.split())[:70])
PY
```

Before: the bottom band reads `Not for commercial use`, the top band contains
`Created using a free version of SMath Studio`.
After: the bottom band reads the page number (`1/8` …) and the top band has
nothing but the running header.

Quick checks that real content survived:

```bash
# Searchable text must be identical to the source:
pdftotext sample.pdf -       > a.txt
pdftotext sample_clean.pdf - > b.txt
diff a.txt b.txt                  # expect no output

# The running header is a bitmap, so check it is still placed on each page:
python3 -c "import pymupdf; d=pymupdf.open('sample_clean.pdf'); \
print([len([r for x in p.get_images(full=True) for r in p.get_image_rects(x[0]) if r.y0<26]) for p in d])"
```

## Tooling notes

- **cpdf cannot do this.** It has no content-stream operator editor; white-out
  rectangles would only hide the ink and risk covering real content.
- **PyMuPDF, NumPy and SciPy are required** — see [Requirements](#requirements)
  above for the `apt` and `pip` install forms.
- **tesseract is optional**, only used by `--self-test` for the OCR half of the
  verification. Without it, that one check is reported as skipped.

## Known results

`sample.pdf` (8 pages, A4/Letter mix of text, formulas and four plots):

```
sample.pdf -> sample_clean.pdf: removed 43 watermark image(s), cleaned 10 flattened bitmap(s)
```

`sample2.pdf` (2 pages, A4, one mid-page illustration):

```
sample2.pdf -> sample2_clean.pdf: removed 12 watermark image(s), cleaned 0 flattened bitmap(s)
```

Both pass `--self-test`:

```
  [PASS] page count
  [PASS] searchable text unchanged
  [PASS] watermark pixels gone from top/bottom bands (0 signature pixel(s) left)
  [PASS] OCR of watermark bands
  [PASS] running header present on every page
  [PASS] body images preserved
PASS: output looks clean
```

Independently confirmed by hand: both watermarks gone on every page; running
header present on every page; page numbers intact; text layer byte-identical;
the illustration in `sample2.pdf` unchanged (same pixel hash and placement).
