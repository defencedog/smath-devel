# SMath Studio / Mono WinForms file dialog crash and default Details view

## Abstract

This repository documents a reproducible **Mono 6.14.1 WinForms** defect that
crashes SMath Studio's file dialog when the user changes the file view from
**List** to **Details**, and provides a small binary patch that:

1. fixes the crash, and
2. makes SMath file dialogs open in **Details** view by default.

It contains the patched `System.Windows.Forms.dll`, the Mono.Cecil patcher
source, C# source-level diffs, build/apply scripts, checksums, and verification
screenshots.

> **Short version:** `File > Open` starts in List. Clicking the view dropdown
> and selecting Details throws a `NullReferenceException` inside Mono's
> `MenuTracker.HideSubPopups`, which tears down the dialog and the SMath
> window. The app is a .NET Framework 4.x WinForms application, so it must run
> on Mono; `dotnet`/CoreCLR cannot run it.

---

## Tested environment

| Item | Value |
|---|---|
| OS | Ubuntu 26.04, arm64 |
| Runtime | Mono JIT 6.14.1 |
| WinForms assembly | `System.Windows.Forms, Version=4.0.0.0, PublicKeyToken=b77a5c561934e089` |
| Application | SMath Studio Desktop 1.5.0.9678 (Mono package) |
| Display | X11; automated tests used Xvfb (`:99`, 1400x900x24) |
| .NET SDK | 8.0.408 (not used for this fix) |

The application is a .NET Framework 4.x WinForms app. On Linux it requires
Mono. `dotnet` cannot run it: .NET 8 has no Linux WinForms implementation and
`System.Drawing.Common` is Windows-only.

---

## Symptoms

1. `File > Open` opens the file selector in **List** view.
   See `screenshots/01-before-list.png`.
2. The toolbar dropdown contains:
   `Small Icon`, `Tiles`, `Large Icon`, `List`, `Details`.
   See `screenshots/02-view-menu.png`.
3. Selecting **Details** causes the file dialog and the SMath main window to
   disappear. The process may remain alive, but the UI is gone.
   See `screenshots/03-crash.png`.
4. stderr contains:

```text
System.NullReferenceException: Object reference not set to an instance of an object
  at System.Windows.Forms.MenuTracker.HideSubPopups (System.Windows.Forms.Menu menu, System.Windows.Forms.Menu topmenu) [0x00075]
  at System.Windows.Forms.PopUpWindow.HideWindow () [0x00020]
  at (wrapper remoting-invoke-with-check) System.Windows.Forms.PopUpWindow.HideWindow()
  at System.Windows.Forms.MenuTracker.Deactivate () [0x00088]
  at System.Windows.Forms.MenuTracker.OnMouseUp (System.Windows.Forms.MouseEventArgs args) [0x00088]
  at System.Windows.Forms.Control.ProcessActiveTracker (System.Windows.Forms.Message& m) [0x000a9]
  at System.Windows.Forms.Control.WmLButtonUp (System.Windows.Forms.Message& m) [0x0001b]
  at System.Windows.Forms.Control.WndProc (System.Windows.Forms.Message& m) [0x0016f]
  ...
```

The exception escapes the X11 mouse-up handler. The file dialog and the SMath
main window disappear; the process may remain alive, but the UI is unusable.

---

## Root cause

Mono's `MenuTracker.HideSubPopups` contains:

```csharp
MenuTracker menuTracker = menu.parent_menu?.Tracker;
if (menuTracker.HideSubPopupEvent != null)
{
    menuTracker.HideSubPopupEvent(menu.parent_menu, new SubPopupEventArgs(menu));
}
```

The null-conditional operator only guards `menu.parent_menu`. `menuTracker`
itself can still be `null`; this is exactly what happens for the toolbar
context menu that hosts the file dialog's View items. Dereferencing
`menuTracker.HideSubPopupEvent` throws the `NullReferenceException`.

This is a **Mono WinForms defect**, not an SMath defect. It is unrelated to the
`dotnet` runtime.

### Why the dialog always starts in List view

`MWFFileView`, the internal ListView used by Mono's `FileDialog`, hardcodes:

```csharp
base.View = (old_view = View.List);
```

There is no persisted setting. Every new `OpenFileDialog` creates a new
`MWFFileView` and resets the view to List.

---

## Fix

### 1. Crash fix (`MenuTracker.HideSubPopups`)

Add a null check:

```csharp
MenuTracker menuTracker = menu.parent_menu?.Tracker;
if (menuTracker != null && menuTracker.HideSubPopupEvent != null)
{
    menuTracker.HideSubPopupEvent(menu.parent_menu, new SubPopupEventArgs(menu));
}
```

At the IL level the patch inserts, after `stloc.3`:

```text
IL_0075: ldloc.3
IL_0076: brfalse.s IL_009a
```

The original `IL_0097` target shifts by three bytes to `IL_009a`. The method
body grows from `0xE2` to `0xE5` bytes.

### 2. Default Details view (`FileDialog`)

Hook the dialog form's `Shown` event. When the dialog is actually visible
(fonts/layout initialized and file list populated), invoke the existing
Details menu item:

```csharp
private void OnDialogShown(object sender, EventArgs e)
{
    if (mwfFileView.View != View.Details)
    {
        // 0=Small Icon, 1=Tiles, 2=Large Icon, 3=List, 4=Details
        menueToolBarButtonContextMenu.MenuItems[4].PerformClick();
    }
}
```

and subscribe in the `FileDialog` constructor:

```csharp
form.Shown += OnDialogShown;
```

This calls the normal `OnClickViewMenuSubItem` path, which sets
`View.Details` and adds the four column headers. Running it from `Shown`
ensures the control has a valid font and the folder items are already
populated.

#### Design notes / failed approaches

Several earlier attempts were tested and rejected:

| Attempt | Result |
|---|---|
| Set `View.Details` and add columns in `MWFFileView` constructor | File list remained blank until the user toggled views |
| Add columns at the end of `RealFileViewUpdate` | Same blank list |
| Call `PerformClick` in `FileDialog.RunDialog` | `ArgumentNullException: font` (form not shown yet) |
| Use a fresh `ViewMenuItems` clone in `OnVisibleChanged` | `ArgumentOutOfRangeException` because the clone is unattached and `Index == -1` |
| Use the attached menu item in `form.Shown` | **Works** |

The important detail is to use the menu item that is already attached to
`menueToolBarButtonContextMenu` (index 4 is Details). A fresh clone produced by
`ViewMenuItems` before attachment has `Index == -1`, which breaks
`UpdateMenuItems`.

---

## Repository layout

```text
WinForm_bug/
├── README.md
├── .gitignore
├── patched/
│   └── System.Windows.Forms.dll
├── src/
│   ├── PatchShown.cs
│   ├── build.sh
│   ├── apply.sh
│   └── verify.sh
├── source-changes/
│   ├── README.md
│   ├── MenuTracker.HideSubPopups.diff
│   └── FileDialog.OnDialogShown.diff
├── screenshots/
│   ├── README.md
│   ├── 01-before-list.png
│   ├── 02-view-menu.png
│   ├── 03-crash.png
│   ├── 04-after-default-details.png
│   ├── 05-after-switch-list.png
│   └── 06-after-switch-details.png
├── checksums.txt
└── LICENSE-Mono.md
```

---

## Installation

### Where to place the patched DLL

| Item | Path |
|---|---|
| Patched file in this repository | `patched/System.Windows.Forms.dll` |
| Destination | `<SMath directory>/System.Windows.Forms.dll` |
| Tested SMath directory | `/home/ukhan/Binaries/SMathStudioDesktop.1_5_0_9678.Mono` |
| Do **not** replace | `/usr/lib/mono/gac/System.Windows.Forms/4.0.0.0__b77a5c561934e089/System.Windows.Forms.dll` |

The DLL must sit **next to `Solver.exe`**, not in the GAC. Mono probes the
application directory first, so the patch applies only to SMath in that
directory. The system-wide Mono installation remains untouched.

### Quick apply

Close SMath first, then:

```bash
cd /path/to/WinForm_bug
./src/apply.sh /home/ukhan/Binaries/SMathStudioDesktop.1_5_0_9678.Mono
```

The script:

1. backs up the original GAC assembly to
   `backup/System.Windows.Forms.dll.orig-4.0.0.0` inside the SMath directory
   (only if the backup does not already exist), and
2. copies `patched/System.Windows.Forms.dll` into the SMath directory as
   `System.Windows.Forms.dll`.

Restart SMath via `smathstudio_desktop_mono`.

### Manual apply

```bash
cd /home/ukhan/Binaries/SMathStudioDesktop.1_5_0_9678.Mono
mkdir -p backup

cp -p /usr/lib/mono/gac/System.Windows.Forms/4.0.0.0__b77a5c561934e089/System.Windows.Forms.dll \
      backup/System.Windows.Forms.dll.orig-4.0.0.0

cp /path/to/WinForm_bug/patched/System.Windows.Forms.dll ./System.Windows.Forms.dll
```

Mono probes the application directory before the GAC, so this app-local DLL is
loaded only for SMath in that directory. The system-wide GAC assembly is not
modified.

### Revert

```bash
rm /home/ukhan/Binaries/SMathStudioDesktop.1_5_0_9678.Mono/System.Windows.Forms.dll
```

Mono will fall back to the original GAC assembly. The backup is kept only for
reference; restoring it app-locally is optional:

```bash
cd /home/ukhan/Binaries/SMathStudioDesktop.1_5_0_9678.Mono
cp backup/System.Windows.Forms.dll.orig-4.0.0.0 ./System.Windows.Forms.dll
```

---

## Building the patch from source

Prerequisites: `mcs`, Mono.Cecil, `monodis` (for verification). On
Debian/Ubuntu, `sudo apt install mono-devel` provides `mcs`, `mono`, and
`monodis`; `Mono.Cecil.dll` is provided by `mono-libraries` and lives in the
Mono GAC.

```bash
cd WinForm_bug/src
./build.sh
```

Or manually:

```bash
mcs -r:/usr/lib/mono/gac/Mono.Cecil/0.11.1.0__0738eb9f132ed756/Mono.Cecil.dll \
    -out:PatchShown.exe PatchShown.cs

mono PatchShown.exe \
  /usr/lib/mono/gac/System.Windows.Forms/4.0.0.0__b77a5c561934e089/System.Windows.Forms.dll \
  ../patched/System.Windows.Forms.dll
```

`build.sh` accepts the input and output paths as optional arguments; see the
script header. It uses `MCS`, `CECIL`, `ORIG`, and `OUT` environment variables
when set.

The patcher uses Mono.Cecil to:

1. load the Mono `System.Windows.Forms.dll`,
2. locate `MenuTracker.HideSubPopups` by IL offset,
3. inject `ldloc.3; brfalse.s` after `stloc.3`,
4. add a new private `FileDialog.OnDialogShown` method,
5. inject `form.Shown += OnDialogShown` into the `FileDialog` constructor,
6. write the modified assembly.

The generated binary is deterministic for this Mono build: rebuilding from
`src/PatchShown.cs` produced the same SHA-256 as `patched/System.Windows.Forms.dll`.

### Verifying the installed patch

```bash
cd WinForm_bug
./src/verify.sh /home/ukhan/Binaries/SMathStudioDesktop.1_5_0_9678.Mono
```

The script checks the SHA-256, the `OnDialogShown` method, and the injected
null-check branch.

---

## Verification results

Automated test under Xvfb:

```bash
Xvfb :99 -screen 0 1400x900x24 &
DISPLAY=:99 mono Solver.exe
```

Then, via `xdotool`:

1. `Ctrl+O` — file dialog opens.
2. Default view is **Details** with Name / Size / Type / Last Access columns.
3. Dropdown -> **List** — switches correctly.
4. Dropdown -> **Details** — switches back correctly, no crash.
5. Select a file and click **Open** — the worksheet loads.

Screenshots:

- `screenshots/04-after-default-details.png` — default Details view.
- `screenshots/05-after-switch-list.png` — List after switching.
- `screenshots/06-after-switch-details.png` — Details after switching back.

The dialog screenshots show local test file names. Review or redact them before
publishing this directory if those names are sensitive.

No `NullReferenceException` or other exception appeared in stderr during the
patched tests.

---

## Checksums

```text
Original Mono GAC assembly:
  62439ae638d994b7983d3a033fdd4a4b2d91934528038e3c3d2b51f86bfa8460

Patched assembly (patched/System.Windows.Forms.dll):
  10bc8f08b661c6574809d8065b1c2c15bf497e14e98d0436a2b7f12816908cf8
```

The installed app-local file in the tested SMath directory matched the patched
checksum.

---

## Caveats

- **Strong naming:** the Cecil rewrite strips/zeroes the assembly's strong-name
  public key, so the original signature is invalid and the patched DLL is not
  re-signed with Mono's private key (unavailable). Mono's default configuration
  does not enforce strong-name verification, so the app-local assembly loads
  and runs. A runtime or configuration that enforces strong names may reject
  it.
- **Version-specific:** the IL offsets and field/method layout are specific to
  the Mono 6.14.1 `System.Windows.Forms.dll` used here. Rebuild against the
  exact assembly you intend to patch. Do not assume the same binary works with
  another Mono version.
- **Local workaround:** the correct long-term fix belongs upstream in Mono's
  `MenuTracker.HideSubPopups`; a configurable/persisted file-dialog view would
  also be a proper upstream feature.
- **Not a .NET Core fix:** this patched assembly targets Mono/.NET Framework.
  It is not intended for CoreCLR or modern .NET WinForms.

---

## License

The patch source, scripts, and documentation in `src/` and this README are
provided as-is.

`patched/System.Windows.Forms.dll` is a modified Mono class library. Mono is
primarily licensed under the MIT license; see `LICENSE-Mono.md` and
<https://github.com/mono/mono/blob/main/LICENSE>. SMath Studio itself is not
included in this repository.
