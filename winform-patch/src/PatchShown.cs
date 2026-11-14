using System;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

// PatchShown.cs
// Patches Mono 6.14.1 System.Windows.Forms.dll:
//   1. Fixes the MenuTracker.HideSubPopups NullReferenceException.
//   2. Opens file dialogs in Details view (after the dialog is shown).
//
// Build:  mcs -r:/usr/lib/mono/gac/Mono.Cecil/0.11.1.0__0738eb9f132ed756/Mono.Cecil.dll \
//              -out:PatchShown.exe PatchShown.cs
// Run:    mono PatchShown.exe <original System.Windows.Forms.dll> <output System.Windows.Forms.dll>
class PatchShown
{
    static MethodReference FindMenuItemGetItem(AssemblyDefinition asm)
    {
        foreach (var t in asm.MainModule.GetTypes())
            if (t.Name == "MenuItemCollection")
                foreach (var m in t.Methods)
                    if (m.Name == "get_Item" && m.Parameters.Count == 1)
                        return m;
        return null;
    }

    static int Main(string[] args)
    {
        if (args.Length != 2) {
            Console.Error.WriteLine("usage: PatchShown.exe <input.dll> <output.dll>");
            return 2;
        }
        var asm = AssemblyDefinition.ReadAssembly(args[0]);
        var mod = asm.MainModule;

        // 1. Fix MenuTracker.HideSubPopups NRE
        var tracker = mod.GetType("System.Windows.Forms.MenuTracker");
        var hide = tracker.Methods.First(m => m.Name == "HideSubPopups");
        var hil = hide.Body.GetILProcessor();
        var hinstr = hide.Body.Instructions;
        var hanchor = hinstr.First(i => i.Offset == 0x75);
        var htarget = hinstr.First(i => i.Offset == 0x97);
        hil.InsertBefore(hanchor, hil.Create(OpCodes.Ldloc_3));
        hil.InsertBefore(hanchor, hil.Create(OpCodes.Brfalse_S, htarget));

        var fileDialog = mod.GetType("System.Windows.Forms.FileDialog");
        var listViewType = mod.GetType("System.Windows.Forms.ListView");
        var formType = mod.GetType("System.Windows.Forms.Form");
        var menuItemType = mod.GetType("System.Windows.Forms.MenuItem");
        var menuType = mod.GetType("System.Windows.Forms.Menu");
        var eventArgsType = mod.ImportReference(typeof(EventArgs));
        var eventHandlerCtor = mod.ImportReference(typeof(EventHandler).GetConstructor(new[] { typeof(object), typeof(IntPtr) }));

        var getView = listViewType.Methods.First(m => m.Name == "get_View" && m.Parameters.Count == 0);
        var addShown = formType.Methods.First(m => m.Name == "add_Shown" && m.Parameters.Count == 1);
        var getMenuItems = menuType.Methods.First(m => m.Name == "get_MenuItems" && m.Parameters.Count == 0);
        var getItem = FindMenuItemGetItem(asm);
        var performClick = menuItemType.Methods.First(m => m.Name == "PerformClick" && m.Parameters.Count == 0);
        if (getItem == null) { Console.Error.WriteLine("MenuItemCollection.get_Item not found"); return 1; }

        var fForm = mod.GetType("System.Windows.Forms.CommonDialog").Fields.First(f => f.Name == "form");
        var fView = fileDialog.Fields.First(f => f.Name == "mwfFileView");
        var fMenu = fileDialog.Fields.First(f => f.Name == "menueToolBarButtonContextMenu");

        // 2. Add FileDialog.OnDialogShown(object, EventArgs)
        var handler = new MethodDefinition("OnDialogShown",
            MethodAttributes.Private | MethodAttributes.HideBySig,
            mod.TypeSystem.Void);
        handler.Parameters.Add(new ParameterDefinition("sender", ParameterAttributes.None, mod.TypeSystem.Object));
        handler.Parameters.Add(new ParameterDefinition("e", ParameterAttributes.None, eventArgsType));
        handler.Body.InitLocals = false;
        var hil2 = handler.Body.GetILProcessor();
        var end = hil2.Create(OpCodes.Ret);
        hil2.Append(hil2.Create(OpCodes.Ldarg_0));
        hil2.Append(hil2.Create(OpCodes.Ldfld, fView));
        hil2.Append(hil2.Create(OpCodes.Callvirt, getView));
        hil2.Append(hil2.Create(OpCodes.Ldc_I4_1)); // Details
        hil2.Append(hil2.Create(OpCodes.Beq_S, end));
        hil2.Append(hil2.Create(OpCodes.Ldarg_0));
        hil2.Append(hil2.Create(OpCodes.Ldfld, fMenu));
        hil2.Append(hil2.Create(OpCodes.Callvirt, getMenuItems));
        hil2.Append(hil2.Create(OpCodes.Ldc_I4_4)); // Details menu item
        hil2.Append(hil2.Create(OpCodes.Callvirt, getItem));
        hil2.Append(hil2.Create(OpCodes.Callvirt, performClick));
        hil2.Append(end);
        fileDialog.Methods.Add(handler);

        // 3. Subscribe form.Shown += OnDialogShown in the FileDialog constructor
        var ctor = fileDialog.Methods.First(m => m.IsConstructor && m.Parameters.Count == 0);
        var cil = ctor.Body.GetILProcessor();
        var cret = ctor.Body.Instructions.Last(i => i.OpCode == OpCodes.Ret);
        cil.InsertBefore(cret, cil.Create(OpCodes.Ldarg_0));
        cil.InsertBefore(cret, cil.Create(OpCodes.Ldfld, fForm));
        cil.InsertBefore(cret, cil.Create(OpCodes.Ldarg_0));
        cil.InsertBefore(cret, cil.Create(OpCodes.Ldftn, handler));
        cil.InsertBefore(cret, cil.Create(OpCodes.Newobj, eventHandlerCtor));
        cil.InsertBefore(cret, cil.Create(OpCodes.Callvirt, addShown));

        asm.Write(args[1]);
        Console.WriteLine("patched -> " + args[1]);
        return 0;
    }
}
