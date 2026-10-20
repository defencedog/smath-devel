# SMath Studio (Mono) Print Fix — CUPS PDF printer + blank-page IL patch

Fixes for **SMath Studio Desktop (Mono Edition) v1.5.0.9678** on Linux
(Ubuntu 26.04 LTS / ARM64). Two independent defects, in the order they block you:

| # | Symptom | Cause | Fix |
| :-- | :--- | :--- | :--- |
| **A** | Print dialog lists **no printers**; "Print to file" + Print **exits the app** | No CUPS server or queues exist — Mono enumerates printers only via CUPS | Install CUPS + `printer-driver-cups-pdf`, create a virtual **`PDF`** queue |
| **B** | Pages print **blank white**; Print Preview errors with *"A null reference or invalid value was found (GDI+ status: InvalidParameter)"* | Custom paper (`PaperId = 0`) matches Mono's bogus **0×0 `'Custom'`** paper entry → 0×0 PostScript page | One-byte IL patch in `SMath.Document.dll` |

Fix A is system configuration (no files here). Fix B is the artifact in this folder.

---

## Part A — No printers / app exits on print

### A.1 Root cause

```
[SMath print dialog]
   └─► Mono System.Drawing.Printing
         └─► PrintingServicesUnix ──► CUPS (libcups)
               ├─ enumerate printers → cupsGetDests → (no cupsd installed) → EMPTY LIST
               └─ PrintDocument.Print()
                     └─► PrintingServicesUnix.CreateGraphicsContext()
                           └─► PageSettings.get_PaperSize()   ← needs a valid printer
                                 └─► InvalidPrinterException("No printers are installed.")
                                       └─► unhandled → Mono exits → APP CLOSES
```

`lpstat` reported *"Scheduler is not running"* and `systemctl is-enabled cups` was `not-found`
— only the `cups-client` tools were present, not the **`cups` server**. Mono 6.14.1 / libgdiplus 6.1
does have `GdipGetPostScriptGraphicsContext` (print-to-file support), but "Print to file" is **not**
independent of a printer in Mono: it still resolves page/paper size from a real printer first.

### A.2 Install and create the queue

```bash
sudo apt-get install -y printer-driver-cups-pdf
sudo lpadmin -p PDF -E -v cups-pdf:/ -m /usr/share/ppd/cups-pdf/CUPS-PDF_opt.ppd
sudo lpadmin -d PDF        # make it the default
```

`printer-driver-cups-pdf 3.0.2-4ubuntu0.1` pulls in `cups` 2.4.16 + `cups-daemon` (the server),
`cups-filters`, `cups-browsed`, `ghostscript`, `poppler-utils`.

Output lands in `${HOME}/PDF` per `/etc/cups/cups-pdf.conf` (`Out ${HOME}/PDF`); queues persist in
`/etc/cups/printers.conf`, PPD at `/etc/cups/ppd/PDF.ppd`. Admin UI: <http://localhost:631>.

### A.3 Verification

| Check | Before | After |
| :--- | :--- | :--- |
| `InstalledPrinters.Count` | `0` | `1` → `- PDF` |
| `PrinterSettings.IsValid` | `False`, name `''` | `True`, name `'PDF'` |
| `PrintDocument.Print()` (PrintToFile) | `InvalidPrinterException` → app exit | `Print() returned OK` |
| Print-to-file output | none | `printtest.ps` (13,643 bytes, `%!PS-Adobe-3.0`, cairo) |

End-to-end: `lp -d PDF /tmp/opencode/pdftest.txt` → job `PDF-1` →
`~/PDF/pdftest.txt__F-job_1.pdf` (valid 1-page PDF).

> **Mono quirk:** "Print to file" writes the `.ps` **and** also submits a copy to the default CUPS
> queue, so you get a duplicate PDF in `~/PDF/` too. Harmless.

### A.4 On-demand CUPS (memory saving)

The idle stack costs ~23.4 MB RSS (`cupsd` ~10 MB + `cups-browsed` ~14 MB), and CUPS is
socket-activated — it silently restarts on any print request. All auto-start paths were disabled and a
wrapper unit `/etc/systemd/system/cups-pdf.service` was installed (**not enabled**):

```ini
[Unit]
Description=CUPS printing stack (on-demand for SMath Studio PDF printing)
After=local-fs.target

[Service]
Type=oneshot
RemainAfterExit=yes
ExecStart=/usr/bin/systemctl start cups.service
ExecStop=/usr/bin/systemctl stop cups.service
TimeoutStartSec=60
```

Disabled: `cups.socket`, `cups.path`, `cups.service`, `cups-browsed.service`.

```bash
sudo systemctl start cups-pdf   # before printing (~12 MB, no cups-browsed)
sudo systemctl stop  cups-pdf   # when done (frees everything)
```

> **Important:** with the service stopped the print dialog is **empty again** and pressing Print
> would exit the app as before. Always `start cups-pdf` first.
> To make it permanent: `sudo systemctl enable --now cups-pdf`.

---

## Part B — Blank pages / Print Preview `InvalidParameter`

### B.1 Symptoms

1. The dialog shows `PDF`, but every PDF in `~/PDF/` is **blank white** (~1 KB).
2. Print Preview opens, then errors: *"A null reference or invalid value was found (GDI+ status: InvalidParameter)"*.
3. Print-to-file PostScript has a **0×0 page**:
   ```
   %%DocumentMedia: 0x0mm 0 0 0 () ()
   %%BoundingBox: 0 0 0 0
   ```

### B.2 Root cause

| Layer | Fact |
| :--- | :--- |
| SMath page setup | Format is **Custom** → `PaperId = 0` (= `PaperKind.Custom`); `~/.config/SMath/settings.inf` has `PageSize=0 850,1100`; documents store `<paper id="…" width="…" height="…"/>` |
| SMath printing code | `SMath.Viewer.Printing.InitPrinting()` in `SMath.Document.dll`: `if (PaperId > -1)` → scan `PrinterSettings.PaperSizes` for `(int)Kind == PaperId`, adopt on match; else fall back to `new PaperSize("Custom", PageModel.Size.Width, PageModel.Size.Height)` |
| Mono paper list (CUPS-PDF) | 66 entries; the **first is a bogus zero-sized `'Custom' kind=Custom 0x0`** (CUPS custom-size pseudo-entry — the PPD sets `*CustomPageSize True` with no fixed dimensions). Most real PPD sizes also report `kind=Custom`; only a few map to real kinds |
| **Result** | `PaperId = 0` matches the **0×0 `'Custom'`** entry → `DefaultPageSettings.PaperSize = 0×0` |
| Mono print path | `PrintingServicesUnix.CreateGraphicsContext()` sizes the PostScript surface from `PageSettings.PaperSize` → `0×0` → cairo emits `0x0mm` media → everything clipped → **blank page** |
| Preview path | Rendering a 0×0 page via Mono's `PreviewPrintController` throws `ArgumentException: … [GDI+ status: InvalidParameter]` → SMath alert popup |

On Windows the same code picks the printer's real "Custom" entry *with* dimensions, which is why
these settings work there.

### B.3 The fix — one byte in `SMath.Document.dll`

In `SMath.Viewer.Printing::InitPrinting`, the guard changes from `PaperId > -1` to `PaperId > 0`:

```
 IL_00ae:  ldc.i4.m1      →      IL_00ae:  ldc.i4.0
```

| Detail | Value |
| :--- | :--- |
| File offset | `0x22BA` |
| Byte | `0x15` (`ldc.i4.m1`) → `0x16` (`ldc.i4.0`) |
| File size | unchanged (440,664 bytes) |
| Assembly | `SMath.Document, Version=1.75.9678.0` |

Effect: **Custom paper (id 0) skips the printer-paper scan** and takes the document's own
`PageModel.Size` (850×1100) as a `"Custom"` PaperSize — exactly what the fallback branch was for.
Standard formats (Letter = 1, A4 = 9, …) keep the previous matching behavior.

### B.4 Verification

Mono probes re-implementing SMath's exact logic:

| Probe | Paper selected | PostScript result |
| :--- | :--- | :--- |
| A: first `Kind==Custom` entry (pre-patch behavior) | `'Custom' 0x0` | `%%DocumentMedia: 0x0mm 0 0 0` → **blank** (reproduces bug) |
| B: `new PaperSize("Custom", 850, 1100)` (the intended fallback) | `850×1100` | `%%DocumentMedia: Letter 612 792`, **content present** |
| C: first `Kind==Letter` entry (`'ANSI A'`) | `850×1100` | `%%DocumentMedia: Letter 612 792`, **content present** |

Preview probe (`PreviewPrintController`): page `0×0` → `InvalidParameter` (the exact popup message);
page `850×1100` → renders (`img=850x1100`).

Disassembly diff (`ikdasm`, original vs patched): **exactly one** changed IL line
(`ldc.i4.m1` → `ldc.i4.0` in `InitPrinting`) — all 96,267 IL lines otherwise identical. The patched
assembly loads under Mono 6.14.1 (`Assembly.LoadFrom`).

---

## Files in this folder

| File | sha256 |
| :--- | :--- |
| `SMath.Document.dll.patched` — ready-to-use patched assembly | `692476272fecde2b6976168b0b7276cf6cebeb3316ecf249615c9879c37ecb7f` |
| `apply-patch.py` — re-applies the patch in place | `ae7f5545b8dfb6b86050a1ae57e5daf278a85ee5642e9081608c270765622dc6` |

Reference hashes for the DLL:

| File | sha256 |
| :--- | :--- |
| original (unpatched) | `74a996895331353d626e24a1ec27c890c95e376890ed3b17936d9b3a341262df` |
| patched | `692476272fecde2b6976168b0b7276cf6cebeb3316ecf249615c9879c37ecb7f` |

## Install the patched DLL

```bash
cd ~/Binaries/SMathStudioDesktop.1_5_0_9678.Mono
cp -p SMath.Document.dll SMath.Document.dll.bak-$(date +%Y%m%d)-blankprint   # keep the original
cp /path/to/print-patch/SMath.Document.dll.patched SMath.Document.dll
```

SMath must be **restarted** to load the new assembly.

## Re-apply after an SMath update

SMath updates replace the DLL, losing the patch. `apply-patch.py` re-applies it in place
(the one-byte change), creating a dated `.bak-YYYYMMDD-blankprint` backup on first run:

```bash
python3 apply-patch.py                          # default Mono install path
python3 apply-patch.py /path/to/SMath.Document.dll
```

It locates the site by pattern (`callvirt get_PaperId; ldc.i4.m1; ble.s; …`) and **aborts** if the
pattern is missing (already patched / different version) or matches more than once — so it fails
safe rather than corrupting the file.

## Revert

```bash
cd ~/Binaries/SMathStudioDesktop.1_5_0_9678.Mono
cp -p SMath.Document.dll.bak-20261018-blankprint SMath.Document.dll
```

## Alternative without patching (per-document workaround)

Choosing **"US Letter"** paper in Page Setup avoids the 0×0 bug for that document (stores `PaperId=1`,
and Mono reports the `ANSI A` entry as `kind=Letter`, so the match succeeds). The global default can be
flipped the same way while SMath is closed:

```
~/.config/SMath/settings.inf:   PageSize=0 850,1100   →   PageSize=1 850,1100
```

Caveat: on Mono **most** PPD paper names (including "A4") report `kind=Custom`, so picking "A4" still
stores `PaperId=0` and still hits the 0×0 entry. Documents carry their own `<paper id=…>`, so the DLL
patch is the only fix that covers every document at once — which is why it was applied.

---

## Licensing note

`SMath.Document.dll` is a proprietary third-party binary (SMath Studio, © SMath LLC). The patched
copy is redistributed here as a one-byte-modified build for interoperability; it is **not** free
software and remains under its original licence. Verify the hashes above before use.

---
*Companion to the font fix; see also `../font-pack/`. Full original write-up: `SMATH_STUDIO_PRINT_FIX.md`.*