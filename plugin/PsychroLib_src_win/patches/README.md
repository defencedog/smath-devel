# patches/

Diffs against the upstream source, if and when any are needed.

## Current state: empty (intentionally)

`src/psychrolib.cs` is **upstream PsychroLib 2.5.0 verbatim** — not one byte
changed. Everything else in `src/` is original code from this repository, not a
modification of upstream. So there is nothing to patch, and this folder stays
empty apart from this file.

## When a patch belongs here

Only for changes to a **third-party file** that we must carry ourselves — for
example if PsychroLib 2.6.0 renames `GetMoistAirEnthalpy`, or if a correlation
needs a fix that upstream has not released yet. Our own `Psy.cs`, `Functions.cs`,
`Units.cs` and `Plugin.cs` are never patches; they are the project.

## How to make one

Start from a clean upstream copy and let git write the diff:

```bash
# 1. vendor the pristine upstream file into git first
cp psychrolib-2.5.0/c_sharp/psychrolib.cs src/psychrolib.cs
git add src/psychrolib.cs && git commit -m "vendor PsychroLib 2.5.0"

# 2. edit src/psychrolib.cs, then capture the change
git diff -- src/psychrolib.cs > patches/0001-psychrolib-some-fix.patch

# 3. revert the working tree so the patch is the only record
git checkout -- src/psychrolib.cs
```

## How to apply

```bat
git apply --verbose patches\*.patch
```

`build.ps1` does **not** apply patches automatically. That is deliberate: the
repository is expected to build from the committed `src/` alone, and a patch that
silently fails to apply is worse than no patch. If you re-vendor upstream, apply
the patches by hand and commit the result.

## Naming

`NNNN-short-description.patch`, numbered in application order. Note the upstream
version the patch was written against in the patch header, so a future re-vendor
knows whether it still applies.
