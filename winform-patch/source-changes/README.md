# Source-level changes

These diffs show the equivalent C# changes for Mono's WinForms sources. They
were generated against the decompiled Mono 6.14.1 sources used for this
analysis, so upstream line numbers may differ. The shipped
`patched/System.Windows.Forms.dll` was produced by applying the same changes at
the IL level with Mono.Cecil (`src/PatchShown.cs`), because rebuilding all of
Mono just for this fix is unnecessary and slower.

## 1. `MenuTracker.HideSubPopups`

File: `mcs/class/System.Windows.Forms/System.Windows.Forms/MenuTracker.cs`

The null-conditional operator only checks `menu.parent_menu`. The result
(`menuTracker`) can still be `null`, especially for the toolbar context menu
used by the file dialog. Add an explicit null check.

Diff: `MenuTracker.HideSubPopups.diff`

IL equivalent:

```text
Before:
  IL_0074: stloc.3
  IL_0075: ldloc.3
  IL_0076: ldfld MenuTracker::HideSubPopupEvent
  IL_007b: brfalse IL_0097

After:
  IL_0074: stloc.3
  IL_0075: ldloc.3
  IL_0076: brfalse.s IL_009a
  IL_0078: ldloc.3
  IL_0079: ldfld MenuTracker::HideSubPopupEvent
  IL_007e: brfalse IL_009a
```

The method body grows from 226 (`0xE2`) to 229 (`0xE5`) bytes.

## 2. Default Details view in `FileDialog`

File: `mcs/class/System.Windows.Forms/System.Windows.Forms/FileDialog.cs`

`MWFFileView` hardcodes `View.List` in its constructor. Instead of changing
that constructor (which produced a blank file list in testing), subscribe to
the dialog form's `Shown` event and invoke the already-attached **Details**
menu item. At `Shown` time the font/layout are initialized, the folder has
been populated, and the normal `OnClickViewMenuSubItem` path can set
`View.Details` and add the columns.

Diff: `FileDialog.OnDialogShown.diff`

IL equivalent of `OnDialogShown`:

```text
ldarg.0
ldfld       FileDialog::mwfFileView
callvirt    ListView::get_View()
ldc.i4.1                         // View.Details
beq.s       ret
ldarg.0
ldfld       FileDialog::menueToolBarButtonContextMenu
callvirt    Menu::get_MenuItems()
ldc.i4.4                         // Details menu item
callvirt    Menu/MenuItemCollection::get_Item(int32)
callvirt    MenuItem::PerformClick()
ret
```

Constructor injection:

```text
ldarg.0
ldfld       CommonDialog::form
ldarg.0
ldftn       FileDialog::OnDialogShown
newobj      EventHandler::.ctor(object, native int)
callvirt    Form::add_Shown(EventHandler)
```

## Why not modify `MWFFileView` directly?

Attempts that changed `MWFFileView`'s constructor or added columns during
`RealFileViewUpdate` left the initial Details list blank until the user
manually switched List -> Details. Calling `PerformClick` during `RunDialog`
failed with `ArgumentNullException: font`. Using an unattached menu-item clone
failed with `ArgumentOutOfRangeException` because `MenuItem.Index` was `-1`.
The `Shown` event with the attached toolbar menu item is the first point where
all of these are valid.
