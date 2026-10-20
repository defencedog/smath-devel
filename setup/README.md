# setup/ — SMath Studio 1.5.0.9678 installers

The two official SMath Studio installers that everything else in this repository was **built and
tested against**. Kept here so a reader can reproduce the exact environment rather than guess at
version drift.

| File | Size | SHA-256 |
| :--- | ---: | :--- |
| `SMathStudioDesktop.1_5_0_9678.Setup.msi` | 3,551,744 B (3.39 MiB) | `611e8baea730373d4249553c7553f77df51e8de52dc30d7d37a407781405f2ba` |
| `SMathStudioDesktop.1_5_0_9678.Mono.tar.gz` | 2,309,472 B (2.20 MiB) | `8cc386d9d5b66befb102b3aca77c23543939bebdce82b137e1cba91df0d005a2` |

Downloaded 2026-10-21 from the official SMath site:

- `https://smath.com/en-US/files/Download/g5NWh/SMathStudioDesktop.1_5_0_9678.Setup.msi`
- `https://smath.com/en-US/files/Download/NKnL7/SMathStudioDesktop.1_5_0_9678.Mono.tar.gz`

## Verification

| Check | Result |
| :--- | :--- |
| MSI magic | `d0cf 11e0 a1b1 1ae1` — Composite Document / MSI Installer |
| MSI metadata | self-identifies as *SMath Studio*, revision `1.5.0-9678`, Advanced Installer 12.7 |
| MSI opens with `7z` | yes — `disk1.cab` extracts, 20 assemblies recovered |
| Tarball magic | `1f 8b 08 08` — gzip; `gzip -t` passes; 158 entries |
| Both | downloaded at exactly the advertised `Content-Length` |

## What each installer targets

- **`.Setup.msi`** — Windows installer. Its `disk1.cab` holds the Windows assemblies
  (`SMath.Document.dll`, `SMath.UI.*`, `PlotRegion.dll`, `TextRegion.dll`, …). Use this to test the
  Windows side of the plugins in [`../plugin/`](../plugin/).
- **`.Mono.tar.gz`** — Mono/Linux tarball. Top level is the portable assembly set plus `book/` (the
  built-in help corpus). Extract it and run `smathstudio_desktop_mono` to get the host that
  [`../print-patch/`](../print-patch/) and [`../font-pack/`](../font-pack/) were developed against.

## This is the exact baseline `print-patch/` patches

Both installers ship an **unpatched** `SMath.Document.dll` — and it is byte-identical in both:

```
sha256  74a996895331353d626e24a1ec27c890c95e376890ed3b17936d9b3a341262df
```

That is precisely the **original** hash recorded in
[`../print-patch/README.md`](../print-patch/README.md). Proven end-to-end: extracting
`SMath.Document.dll` from this tarball and running the shipped patcher against it reproduces the
shipped patched assembly exactly —

```bash
tar xzf SMathStudioDesktop.1_5_0_9678.Mono.tar.gz --wildcards '*SMath.Document.dll' -O > /tmp/SMath.Document.dll
python3 ../print-patch/apply-patch.py /tmp/SMath.Document.dll
# -> patched at file offset: 0x22ba
# -> sha256: 692476272fecde2b6976168b0b7276cf6cebeb3316ecf249615c9879c37ecb7f
```

which matches `../print-patch/SMath.Document.dll.patched` byte for byte. So these files are the
correct starting point, not an approximation.

> The blank-page patch targets **Mono/Linux**. On Windows the printer reports a real `Custom` paper
> entry *with* dimensions, so the same code path behaves correctly and the patch is not needed.

## Recreating the patched install (Linux / Mono)

```bash
mkdir -p ~/Binaries/SMathStudioDesktop.1_5_0_9678.Mono
tar xzf setup/SMathStudioDesktop.1_5_0_9678.Mono.tar.gz \
    -C ~/Binaries/SMathStudioDesktop.1_5_0_9678.Mono
python3 print-patch/apply-patch.py     # patches in place, keeps a dated .bak
```

Then apply the font configuration from [`../font-pack/`](../font-pack/) so chemistry and maths
symbols render as glyphs rather than `.notdef` boxes.

## Licensing note

SMath Studio is freeware, © SMath LLC — these installers are redistributed here unmodified, as
published by the vendor, solely so the patched/configured state described elsewhere in this
repository is reproducible. They are not covered by any free-software licence and remain subject to
the vendor's terms. Verify the SHA-256 values above before use.