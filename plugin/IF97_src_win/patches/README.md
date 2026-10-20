# patches/

Diffs against the upstream IF97 source, if and when any are needed.

## Current state: empty (intentionally)

Every file the plugin vendors from upstream is **verbatim** — `Api.cs`,
`Region1.cs` … `Region5.cs`, `RegionBase.cs`, `BackwardsRegion.cs`,
`BackwardsRegion3.cs`, `Constants.cs`, `Enums.cs`, `FastPow.cs` are unmodified.
`ValueTupleShim.cs`, `Functions.cs`, `Units.cs`, `Plugin.cs` and `AssemblyInfo.cs`
are original code from this repository, not modifications of upstream. So there
is nothing to patch, and this folder stays empty apart from this file.

Note that `ValueTupleShim.cs` is *not* a patch even though it exists to make
upstream compile under .NET 2.0: it adds types in our own assembly rather than
editing upstream files, so upstream can be re-vendored without touching it.

## When a patch belongs here

Only for changes to a **third-party file** that we must carry ourselves — for
example if a newer upstream release changes a coefficient table layout, or a
boundary equation needs a fix that upstream has not released yet.

## How to make one

Start from a clean upstream copy and let git write the diff:

```bash
# 1. vendor the pristine upstream files into git first
cp IF97-master/*.cs src/
git add src/ && git commit -m "vendor IF97 upstream"

# 2. edit the upstream file, then capture the change
git diff -- src/Region3.cs > patches/0001-region3-some-fix.patch

# 3. revert the working tree so the patch is the only record
git checkout -- src/Region3.cs
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
