# smath-cleanup/ — PDF post-processing helpers for SMath Studio output

Small, self-contained tools for the two chores SMath Studio's PDF export forces on you afterwards:
**strip the free-version watermarks**, and **fold a very tall sheet export onto A4**.

Each tool has its own `.md` with the full design notes, options and worked examples. This file is the
index: what each tool does, what it needs installed, and which platform it runs on.

## The tools

| Tool | What it does | Runtime |
| :--- | :--- | :--- |
| [`remove_wm.py`](remove_wm.py) | Removes the watermarks; inpaints the bitmaps SMath merged watermark+content into. Includes a `--verify` self-test. | Python 3 |
| [`remove_wm.sh`](remove_wm.sh) | Wrapper around `remove_wm.py` — runs it with `python3` from `PATH`, adds `--self-test` | bash → `remove_wm.py` |
| [`remove_wm_cpdf.sh`](remove_wm_cpdf.sh) | Same goal, **cpdf only** (no Python). Conservative: refuses rather than guesses | bash |
| [`longimg2a4pdf.sh`](longimg2a4pdf.sh) | Splits one very tall image into A4 pages with equal 25.4 mm margins, one multi-page PDF | bash |

Reference docs: [`remove_wm.md`](remove_wm.md) · [`remove_wm_cpdf.md`](remove_wm_cpdf.md) ·
[`longimg2a4pdf.md`](longimg2a4pdf.md)

## Requirements

Nothing is vendored here — install what you need. Everything except the three Python modules is
available from the standard Debian/Ubuntu repositories.

### Python backend (`remove_wm.py`)

Needs **PyMuPDF**, **NumPy** and **SciPy**. Either install them system-wide:

```bash
# Debian / Ubuntu — no virtualenv needed
sudo apt install python3-pymupdf python3-numpy python3-scipy
```

…or into a virtual environment (any platform, keeps system Python untouched):

```bash
python3 -m venv ~/.venv-smath
. ~/.venv-smath/bin/activate
pip install pymupdf numpy scipy
```

`remove_wm.sh` uses `python3` from `PATH`. To point it at a different interpreter, set `PYTHON`:

```bash
PYTHON=~/.venv-smath/bin/python ./remove_wm.sh input.pdf output.pdf
```

### System tools

| Tool needed | Provides | Debian / Ubuntu | Notes |
| :--- | :--- | :--- | :--- |
| `cpdf` | `remove_wm_cpdf.sh`; the exact-A4 media-box pass in `longimg2a4pdf.sh` | `sudo apt install cpdf` | Coherent PDF CLI. Optional for `longimg2a4pdf.sh`, **required** by `remove_wm_cpdf.sh` |
| `jq` | `remove_wm_cpdf.sh` (parses `cpdf -list-images-json`) | `sudo apt install jq` | required by `remove_wm_cpdf.sh` |
| `pdfinfo`, `pdftotext`, `pdftoppm` | page counts, text checks, page rendering | `sudo apt install poppler-utils` | `pdfinfo` is optional (the script falls back to `cpdf`) |
| `magick` / `convert`, `identify` | `longimg2a4pdf.sh` | `sudo apt install imagemagick` | ImageMagick 7 (`magick`) or 6 (`convert`); the script picks whichever exists |
| `tesseract` | OCR half of `remove_wm.py --verify` | `sudo apt install tesseract-ocr` | **optional** — without it that one check reports as skipped |
| `awk` | `longimg2a4pdf.sh`, `remove_wm_cpdf.sh` | preinstalled (`mawk` or `gawk`) | the `awk` programs are POSIX-only, so either works |

One-liner for the lot:

```bash
sudo apt install python3-pymupdf python3-numpy python3-scipy \
                 cpdf jq poppler-utils imagemagick tesseract-ocr
```

## Platform support

| File | Linux / macOS / WSL / Git-Bash | Native Windows |
| :--- | :---: | :---: |
| `remove_wm.py` | yes | **yes** — plain Python 3, no platform-specific code |
| `remove_wm.sh` | yes | no — needs a POSIX shell (`bash`) |
| `remove_wm_cpdf.sh` | yes | no — needs a POSIX shell |
| `longimg2a4pdf.sh` | yes | no — needs a POSIX shell |

The three `.sh` files assume a POSIX shell and Unix tooling (`awk`, `mktemp`, `identify`). On native
Windows, call the Python directly:

```powershell
python remove_wm.py input.pdf output.pdf
```

## Why two watermark removers

Because the reliable approach needs pixel access, and cpdf has none:

- **Both watermarks are raster images**, not text — `pdftotext` never shows them. They are drawn as
  image XObjects in a tiny grey palette (`#D3D3D3` fill, `#2F4F4F` outline, `#696969` shading).
- **`remove_wm.py`** deletes the watermark draw blocks from the content stream (no white-out boxes)
  and inpaints only the watermark-coloured pixels out of bitmaps where SMath flattened the watermark
  *together with* real content. That last case is why cpdf cannot do it: `cpdf -draft-remove-only`
  deletes a whole image, so it would take the formula-box border with it.
- **`remove_wm_cpdf.sh`** is the no-Python fallback. It removes thin "hairline rule" images
  automatically and **refuses to write** when text-height strips exist, because cpdf cannot tell
  watermark wording from the running header or rasterised content. On a normal export its auto mode
  cleans nothing on purpose; you pass `--names "/Rxx ..."` after inspecting `cpdf -list-images`.
  For a guaranteed lossless result, use the Python backend.

The running header (`<date> - <file>.sm`) is also a grey raster image sitting just above the top
watermark and must survive. Both tools protect it: the Python side by palette **and** density (the
header is `#A9A9A9` only), the cpdf side by the refusal rule.

Only text is stored here — no PDFs, no binaries, no sample documents.

## Quick start

```bash
# Watermark removal, with the built-in self-test:
./remove_wm.sh --self-test input.pdf output.pdf

# cpdf-only path: see what it would do, then name the strips explicitly:
./remove_wm_cpdf.sh --dry-run input.pdf
./remove_wm_cpdf.sh --names "/R22 /R18 /R17" -o input-clean.pdf input.pdf

# Long sheet export -> A4:
./longimg2a4pdf.sh -p 3 sheet.png sheet-3up.pdf
```

With no arguments, `remove_wm.py` / `remove_wm.sh` read `sample.pdf` and write `sample_clean.pdf`;
`remove_wm_cpdf.sh` reads `sample.pdf` and writes `sample-clean.pdf`. Substitute your own paths.

`remove_wm.sh --self-test` verifies page count, the unchanged text layer, watermark pixels gone from
the margins, OCR of the bands, the running header on every page, and untouched body images — and
exits non-zero if any check fails, which makes it usable as a regression test.
