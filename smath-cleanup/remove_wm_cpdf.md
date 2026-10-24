# SMath Studio Watermark Removal — cpdf backend

`remove_wm_cpdf.sh` strips the SMath Studio free-version watermarks using
**cpdf only** (no Python). It is deliberately conservative: cpdf can delete
whole images but cannot inspect their pixels or position, so the script **refuses
to guess** when a strip could be the running header or rasterised worksheet
content. For a guaranteed lossless clean of merged watermark/content bitmaps use
the PyMuPDF backend (`remove_wm.py`); see [Limits](#limits) below.

## The watermarks

SMath Studio's free version stamps **two thin, page-wide raster images on every
page**:

| Position | Image text |
|----------|-----------|
| **Top**    | `Created using a free version of SMath Studio` |
| **Bottom** | `Not for commercial use` |

They are **images**, not text — `pdftotext` / `cpdf -text` never show them.

## Why cpdf needs care

- `cpdf -draft` removes **every** image in the file, illustrations included.
- `cpdf -draft-remove-only "/Name"` removes **one named image entirely**. There
  is no pixel editing and no partial crop.
- `cpdf -list-images` exposes only name, pixel size, filter and the pages an
  image appears on. It does **not** expose placement, colour or content.
- SMath 2026 exports emit a **fresh watermark image per page**, so “appears on
  every page” does not describe the watermark — it describes the **running
  header**.
- Where worksheet content overlaps the watermark band, SMath **merges the
  watermark and content into one bitmap**. Deleting that image deletes content;
  keeping it keeps the watermark.

Because of the last two points, a size/recurrence heuristic alone is unsafe:
an earlier revision of this script selected **the running header and none of the
watermarks** on `sample2.pdf`. The current version refuses rather than repeats
that.

## Safety model

| Rule | Behaviour |
|------|-----------|
| **Rule-like strips** | Images ≤ 8 px tall, aspect ≥ 6, height ≤ 120 px are removed automatically. These are the watermark hairlines. |
| **Text-height strips** | Anything 9–120 px tall is **never removed automatically**: cpdf cannot distinguish watermark wording from the running header or rasterised content. |
| **Recurring-header protection** | An image ≥ 9 px tall that appears on every page is assumed to be the running header and is kept. |
| **Refusal** | If any text-height strip exists and neither `--names` nor `--allow-text-strips` is given, the script reports and **writes nothing** (exit 2). |
| **Explicit names** | `--names "/R22 /R17 …"` removes exactly those images — the only reliable way to remove text strips. |
| **Extra protection** | `--protect "/R15"` guarantees a name is never removed. |

Auto mode therefore cleans **nothing** on a normal SMath export; that is
intentional. It tells you why and how to proceed:

```
NOTE: 5 strip(s) are text-height, so cpdf cannot tell watermark wording
      from the running header or rasterised worksheet content.

      * Pass --names "/Rxx /Ryy ..." for the strips you have inspected
        (cpdf -list-images shows the names).
      * Or --allow-text-strips after checking the file yourself.
      * If SMath merged the watermark into content bitmaps (such as sample.pdf),
        no cpdf selection is safe -- use remove_wm.py, which inpaints the
        watermark pixels and keeps the content.

REFUSING to write: removing only the hairline rules would leave the watermark
wording in place, and guessing on the text strips could delete the header or
rasterised content. Nothing was modified (exit 2).
```

Exit status: `0` success / dry-run, `1` usage or tool error, `2` refused because
an explicit selection is required.

## Usage

```bash
./remove_wm_cpdf.sh [options] [input.pdf]

# See what it finds (never writes):
./remove_wm_cpdf.sh --dry-run sample2.pdf

# Full clean after inspecting names (recommended):
./remove_wm_cpdf.sh --names "/R22 /R18 /R17 /R16 /R21 /R23 \
                              /R41 /R38 /R37 /R36 /R39 /R40" \
                    -o sample2-clean.pdf sample2.pdf
```

Default input `~/PDF/smath-demo.pdf`; default output `<input>-clean.pdf`.

Get the names and sizes with:

```bash
cpdf -list-images sample2.pdf
```

### Options

| Option | Meaning | Default |
|--------|---------|---------|
| `-o, --output FILE` | output PDF | `<input>-clean.pdf` |
| `--dry-run` | classify + report only | off |
| `--min-aspect N` | geometry: min Width/Height | `6` (0 = off) |
| `--max-height N` | geometry: max pixel height | `120` (0 = off) |
| `--min-pages-ratio R` | recurrence guard: min fraction of pages | `0` (off) |
| `--filter F` | only remove images with this Filter (e.g. `/FlateDecode`) | any |
| `--names "A B"` | remove exactly these XObject names (bypasses guards) | — |
| `--protect "A B"` | never remove these names | — |
| `--allow-text-strips` | let auto mode remove 9–120 px strips too **after you have checked the file** | off |
| `--boxes` | draw crossed boxes where images were removed | off |
| `-n, --no-op-guard` | still write an output when 0 images matched | off |
| `-v, --verbose` | per-image progress | off |

## Worked example: `sample2.pdf`

`--dry-run` shows the running header protected, the wording strips held back,
and only the hairline rules as candidates:

```
NAME           SIZE ASPECT    FILTER            PAGES  DECISION REASON
/R22        1485x70 21.2      /FlateDecode       1/2    KEEP   text strip - verify and use --names
/R15        1722x59 29.2      /FlateDecode       2/2    KEEP   recurs on every page (running header?) - use --names
/R18         2943x4 735.8     /FlateDecode       1/2    REMOVE thin strip
/R17        2943x89 33.1      /FlateDecode       1/2    KEEP   text strip - verify and use --names
/R14        985x535 1.8       /FlateDecode       1/2    KEEP   not-thin(aspect,height)
...
candidates to remove: 8   /R18 /R23 /R16 /R21 /R41 /R38 /R39 /R36  (withheld)
```

Passing the twelve watermark names explicitly gives the same result as the
Python cleaner:

```
Removed 12 watermark image(s). Image instances: 14 -> 2.
```

Remaining images: the running header and the tank illustration. Verified by
rendering: watermark wording gone from both bands, page numbers `1/2` / `2/2`
intact, and the illustration’s pixel hash unchanged (`991f89f96176`).

`--allow-text-strips` also cleans this file in one command, because its
watermarks do not recur across pages and the header does; the flag is opt-in
precisely because that reasoning does not hold for every document.

## Limits

### `sample.pdf` cannot be cleaned by cpdf

Pages 2–6 contain ten bitmaps in which the watermark is **flattened together
with worksheet content** (formula-box borders, the tops of glyphs). cpdf can
only delete whole images:

| Action | Result |
|--------|--------|
| remove `/R32` | watermark wording still remains (its upper part is `/R31`), **and** the box border plus letter tops are erased |
| remove `/R32 /R33 /R34` | the whole “Verify mole fraction unity closure” box disappears |
| keep them | watermark remains |

There is no cpdf switch that edits content streams or pixel data, and
`-list-images` has no placement information, so a “margins only” rule cannot be
expressed either. Use `remove_wm.py`, which removes pure watermark images and
inpaints the watermark pixels out of the merged bitmaps.

This is also why **`--allow-text-strips` is dangerous** on such a file: the
merged bitmaps are 28–73 px tall and would be deleted as if they were
watermarks, taking content with them.

### Heuristics are still heuristics

The header protection keys off “appears on every page”. In `sample.pdf` the
header is emitted as different XObjects (`/R14`, `/R29`, …), so only the
text-strip refusal keeps it safe there. When in doubt, use `--protect` and
`--names` rather than `--allow-text-strips`.

## Tooling notes

- Requires **cpdf** and **jq**, plus `pdfinfo` (poppler) for the page count
  (falls back to `cpdf -list-images-json` if unavailable).
- **cpdf gotcha:** only the **last** `-draft-remove-only` in one invocation takes
  effect, so the script chains one cpdf run per selected name.
- `-draft-remove-only` matches the XObject **name** (`/R16`); the leading slash
  is required.
- `--boxes` fills removed regions with a crossed box; omit it for a clean result.

## Verification

```bash
# Images left after cleaning (expect only the header/illustration):
cpdf -list-images sample2-clean.pdf

# Body text and page numbers intact:
pdftotext sample2-clean.pdf - | grep -E '1/2|2/2'

# Watermark wording gone (render + OCR):
~/venv_engg/bin/python - <<'PY'
import pymupdf, subprocess
d = pymupdf.open("sample2-clean.pdf")
for i, p in enumerate(d):
    r = p.rect
    for name, box in (("top", pymupdf.Rect(0, 0, r.width, 60)),
                      ("bot", pymupdf.Rect(0, r.height - 50, r.width, r.height))):
        p.get_pixmap(dpi=300, clip=box).save(f"/tmp/{name}{i+1}.png")
        print(i + 1, name, " ".join(subprocess.run(
            ["tesseract", f"/tmp/{name}{i+1}.png", "-", "--psm", "6"],
            capture_output=True, text=True).stdout.split())[:60])
PY
```

For a one-command check with the same guarantees as the Python backend, use
`./remove_wm.sh --self-test`, which verifies palette, OCR, text layer, headers
and untouched body images.
