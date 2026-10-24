# smath-cleanup/ — PDF post-processing helpers for SMath Studio output

Small, self-contained tools for the two things SMath Studio's PDF export forces you to do
afterwards: **strip the free-version watermarks**, and **fold a very tall sheet export onto A4**.

Each tool has its own `.md` with the full design notes, options and worked examples. This file is
the index, plus the platform split.

## The tools

| Tool | What it does | Backend |
| :--- | :--- | :--- |
| [`remove_wm.py`](remove_wm.py) | Removes the watermarks; inpaints the bitmaps SMath merged watermark+content into | PyMuPDF + NumPy + SciPy |
| [`remove_wm.sh`](remove_wm.sh) | Thin wrapper around `remove_wm.py` — runs it in `~/venv_engg`, adds `--self-test` | bash → `remove_wm.py` |
| [`remove_wm_cpdf.sh`](remove_wm_cpdf.sh) | Same goal, **cpdf only** (no Python). Conservative: refuses rather than guesses | cpdf + jq + pdfinfo |
| [`longimg2a4pdf.sh`](longimg2a4pdf.sh) | Splits one very tall image into A4 pages with equal 25.4 mm margins, one multi-page PDF | bash + ImageMagick |

Reference docs: [`remove_wm.md`](remove_wm.md) · [`remove_wm_cpdf.md`](remove_wm_cpdf.md) ·
[`longimg2a4pdf.md`](longimg2a4pdf.md)

## ⚠️ Platform: only `remove_wm.py` runs on Windows

| File | Linux / macOS / WSL / Git-Bash | Native Windows |
| :--- | :---: | :---: |
| `remove_wm.py` | yes | **yes** — plain Python 3 |
| `remove_wm.sh` | yes | no — `#!/usr/bin/env bash`, hard-codes `~/venv_engg/bin/python` |
| `remove_wm_cpdf.sh` | yes | no — bash |
| `longimg2a4pdf.sh` | yes | no — bash |

The three `.sh` files need a POSIX shell and assume Unix tooling (`awk`, `mktemp`, `identify`);
`remove_wm.sh` additionally hard-codes a Linux venv path. On Windows, call the Python directly:

```powershell
python remove_wm.py input.pdf output.pdf
```

One caveat in the `.py`: the cleaning path is entirely portable, but the **self-test's** OCR helper
writes to a hard-coded POSIX temp path (`/tmp/smath_cleanup_selftest.png`, in `_ocr_band_text`), so
`--verify` on Windows needs `C:\tmp` to exist — or that one line pointed at `tempfile`. Cleaning
itself has no platform-specific code.

## Why two watermark removers

Because the reliable approach needs pixel access and cpdf does not have any:

- **Both watermarks are raster images**, not text — `pdftotext` never shows them. They are drawn as
  image XObjects in a tiny grey palette (`#D3D3D3` fill, `#2F4F4F` outline, `#696969` shading).
- **`remove_wm.py`** deletes the watermark draw blocks from the content stream (no white-out boxes)
  and inpaints only the watermark-coloured pixels out of bitmaps where SMath flattened the
  watermark *together with* real content. That last case is the reason cpdf cannot do it: `cpdf
  -draft-remove-only` deletes a whole image, so it would take the formula-box border with it.
- **`remove_wm_cpdf.sh`** is the no-Python fallback. It removes thin "hairline rule" images
  automatically and **refuses to write** when text-height strips exist, because it cannot tell
  watermark wording from the running header or rasterised content. On a normal export its auto mode
  cleans nothing on purpose; you pass `--names "/Rxx ..."` after inspecting `cpdf -list-images`.
  For a guaranteed lossless result, use the Python backend.

The running header (`<date> - <file>.sm`) is also a grey raster image sitting just above the top
watermark and must survive. Both tools protect it: the Python side by palette **and** density
(the header is `#A9A9A9` only), the cpdf side by the refusal rule.

## Dependencies

| Tool | Needs |
| :--- | :--- |
| `remove_wm.py` | `pip install pymupdf numpy scipy`; `tesseract` optional (OCR half of `--verify` only) |
| `remove_wm.sh` | the above, via `~/venv_engg` — edit `PY=` if your venv lives elsewhere |
| `remove_wm_cpdf.sh` | `cpdf`, `jq`, `pdfinfo` (poppler) |
| `longimg2a4pdf.sh` | ImageMagick 7 (`magick`, IM6 via `convert`); `cpdf` + `pdfinfo` optional, degrades gracefully |

Only text is stored here — no PDFs, no binaries.

## Quick start

```bash
# Watermark removal, with the built-in self-test:
./remove_wm.sh --self-test sample.pdf sample_clean.pdf

# cpdf-only path: see what it would do, then name the strips explicitly:
./remove_wm_cpdf.sh --dry-run sample2.pdf
./remove_wm_cpdf.sh --names "/R22 /R18 /R17" -o sample2-clean.pdf sample2.pdf

# Long sheet export -> A4:
./longimg2a4pdf.sh -p 3 sheet.png sheet-3up.pdf
```

`remove_wm.sh --self-test` verifies page count, the unchanged text layer, watermark pixels gone
from the margins, OCR of the bands, the running header on every page, and untouched body images —
and exits non-zero if any check fails, which makes it usable as a regression test.
