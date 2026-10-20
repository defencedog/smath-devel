/*
 * CoolPropEngine - the P/Invoke surface against the native CoolProp C API.
 *
 * Three native engines ship at the package root, so one zip serves every host:
 *
 *  1. CoolProp_x86.dll - the 32-bit Windows engine (i686 stdcall). Built by
 *     upstream CMake with COOLPROP_STDCALL_LIBRARY=ON; it exports every symbol
 *     decorated: _PropsSI@32, _PhaseSI@36, ... (71 exports, 0 undecorated
 *     names - verified by parsing the export table).
 *
 *  2. CoolProp_x64.dll - the 64-bit Windows engine, built from the same
 *     upstream source with the ordinary shared-library options; the Win64 ABI
 *     has no name decoration, so it exports the same 71 names plain and the
 *     exact-name probe below matches it directly.
 *
 *  3. libCoolProp.so - the Linux engine; exports the same functions
 *     undecorated, cdecl.
 *
 * The two Windows engines cannot share a file name - they sit in the same
 * folder and the plugin installer objects to duplicate paths / subfolders -
 * and the CLR binds a DllImport by the module's base name, so every entry
 * point is declared twice: once against "CoolProp_x86", once against
 * "CoolProp_x64". A one-line dispatcher per function picks the pair by
 * IntPtr.Size. (The legacy Mono plugin used the same x86/x64 split.)
 *
 * The CLR's stdcall P/Invoke resolver probes, in order: the exact name, then
 * "<name>@<n>" and "_<name>@<n>", where <n> is derived from the *managed*
 * signature. Therefore one declaration with a plain EntryPoint and
 * CallingConvention.StdCall binds on all three engines - as long as the
 * managed signature mirrors the native one exactly, so the CLR's computed <n>
 * matches the DLL's decoration. A wrong arity or a `ref` where the native side
 * takes a value does not throw EntryPointNotFoundException; it throws nothing
 * at all - the name simply never matches and the call fails with "Unable to
 * find an entry point". That is why every signature below is a byte-for-byte
 * transcription of CoolPropLib.h. Mono ignores the calling convention on ELF
 * and matches by name.
 *
 * MSVC `long` is 32 bits while LP64 `long` (Mono) is 64. All CoolProp C API
 * functions that return `long` return small codes/indices (< 2^31), so every
 * one of them is declared `int` here: correct on Windows, and correct on Linux
 * because the upper half of RAX is just the sign extension of the small value.
 *
 * Windows additionally needs the engine loaded by absolute path from the plugin
 * folder: .NET Framework does not probe a plugin's own directory for native
 * dependencies (it searches the host exe's directory, System32 and PATH), so a
 * bare DllImport would never find the engine next to us. EnsureLoaded() loads
 * CoolProp_x64.dll or CoolProp_x86.dll (by the process bitness) by absolute
 * path and fails with a readable message when the engine is missing or cannot
 * be loaded. On Mono the two <dllmap> entries in
 * packaging/CoolPropWrapper.dll.config redirect both names to libCoolProp.so,
 * and that platform resolves relative to the assembly, so no explicit load is
 * needed.
 */

using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace CoolPropWrapper
{
    /// <summary>P/Invoke surface of the native CoolProp C library.</summary>
    internal static class CoolPropEngine
    {
        private static bool _loadAttempted;

        // ------------------------------------------------------------------
        // Windows only: pull the engine out of the plugin folder.
        // ------------------------------------------------------------------

        [DllImport("kernel32.dll", EntryPoint = "LoadLibraryA", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern IntPtr Win32LoadLibrary(string lpFileName);

        /// <summary>True when this process will use the x64 engines.</summary>
        internal static bool IsX64
        {
            get { return IntPtr.Size == 8; }
        }

        /// <summary>
        /// Loads CoolProp_x64.dll or CoolProp_x86.dll (by the process bitness)
        /// by absolute path. No-op on Mono (the dllmap covers it). Throws with
        /// a readable message when the engine is missing or cannot be loaded -
        /// a silent return here is exactly how a bitness mismatch used to
        /// surface as a bare BadImageFormatException (HRESULT 0x8007000B) from
        /// the first P/Invoke instead.
        /// </summary>
        internal static void EnsureLoaded()
        {
            if (_loadAttempted)
                return;

            if (!IsWindows)
                return;

            string name = IsX64 ? "CoolProp_x64.dll" : "CoolProp_x86.dll";
            string path = Path.Combine(PluginDirectory, name);

            if (!File.Exists(path))
            {
                throw new FileNotFoundException(
                    "CoolProp native engine missing: " + path
                    + " (this plugin ships x86 and x64 engines; the installation looks incomplete).",
                    path);
            }

            try
            {
                if (Win32LoadLibrary(path) == IntPtr.Zero)
                {
                    int code = Marshal.GetLastWin32Error();
                    throw new System.ComponentModel.Win32Exception(
                        code, "Failed to load the CoolProp native engine (" + name + "): " + path);
                }
            }
            catch
            {
                // Leave _loadAttempted false so a later evaluation retries and
                // reports the same clear failure rather than a stale state.
                _loadAttempted = false;
                throw;
            }

            _loadAttempted = true;
        }

        /// <summary>True when running under Windows .NET, false under Mono.</summary>
        internal static bool IsWindows
        {
            get
            {
                PlatformID id = Environment.OSVersion.Platform;
                // 4 = Unix, 6 = MacOSX, 128 = older Mono. Anything else is a
                // Windows PlatformID (Win32Windows / Win32NT / WinCE ...).
                if (id == PlatformID.Unix || id == PlatformID.MacOSX)
                    return false;
                int raw = (int)id;
                return raw != 4 && raw != 6 && raw != 128;
            }
        }

        /// <summary>Directory holding this assembly (the plugin folder).</summary>
        internal static string PluginDirectory
        {
            get
            {
                try
                {
                    // CodeBase (not Location) so this also works while the
                    // assembly is shadow-copied by the host.
                    string path = new Uri(Assembly.GetExecutingAssembly().CodeBase).LocalPath;
                    string dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir))
                        return dir;
                }
                catch (Exception)
                {
                }
                return AppDomain.CurrentDomain.BaseDirectory;
            }
        }

        // ------------------------------------------------------------------
        // The C API, transcribed from include/CoolProp/CoolPropLib.h.
        // One imported set per engine; only the DllImport name (and therefore
        // the module bound at runtime) differs between the two.
        // ------------------------------------------------------------------

        private static class X86
        {
            private const string Library = "CoolProp_x86";

            // double PropsSI(const char *Output, const char *Name1, double Prop1, const char *Name2, double Prop2, const char *FluidName);
            [DllImport(Library, EntryPoint = "PropsSI", CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
            internal static extern double PropsSI(string Output, string Name1, double Prop1, string Name2, double Prop2, string FluidName);

            // double Props1SI(const char *FluidName, const char *Output);
            [DllImport(Library, EntryPoint = "Props1SI", CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
            internal static extern double Props1SI(string FluidName, string Output);

            // double HAPropsSI(const char *Output, const char *Name1, double Prop1, const char *Name2, double Prop2, const char *Name3, double Prop3);
            [DllImport(Library, EntryPoint = "HAPropsSI", CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
            internal static extern double HAPropsSI(string Output, string Name1, double Prop1, string Name2, double Prop2, string Name3, double Prop3);

            // long PhaseSI(const char* Name1, double Prop1, const char* Name2, double Prop2, const char* FluidName, char* phase, int n);
            [DllImport(Library, EntryPoint = "PhaseSI", CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
            internal static extern int PhaseSI(string Name1, double Prop1, string Name2, double Prop2, string FluidName, StringBuilder phase, int n);

            // double saturation_ancillary(const char* fluid_name, const char* output, int Q, const char* input, double value);
            [DllImport(Library, EntryPoint = "saturation_ancillary", CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
            internal static extern double saturation_ancillary(string fluid_name, string output, int Q, string input, double value);

            // long get_global_param_string(const char* param, char* Output, int n);
            [DllImport(Library, EntryPoint = "get_global_param_string", CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
            internal static extern int get_global_param_string(string param, StringBuilder Output, int n);

            // long get_parameter_information_string(const char* param, char* Output, int n);
            [DllImport(Library, EntryPoint = "get_parameter_information_string", CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
            internal static extern int get_parameter_information_string(string param, StringBuilder Output, int n);

            // long get_fluid_param_string(const char* fluid, const char* param, char* Output, int n);
            [DllImport(Library, EntryPoint = "get_fluid_param_string", CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
            internal static extern int get_fluid_param_string(string fluid, string param, StringBuilder Output, int n);

            // long get_param_index(const char* param);
            [DllImport(Library, EntryPoint = "get_param_index", CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
            internal static extern int get_param_index(string param);

            // int set_reference_stateS(const char* Ref, const char* reference_state);
            [DllImport(Library, EntryPoint = "set_reference_stateS", CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
            internal static extern int set_reference_stateS(string Ref, string reference_state);

            // int set_reference_stateD(const char* Ref, double T, double rhomolar, double hmolar0, double smolar0);
            [DllImport(Library, EntryPoint = "set_reference_stateD", CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
            internal static extern int set_reference_stateD(string Ref, double T, double rhomolar, double hmolar0, double smolar0);
        }

        private static class X64
        {
            private const string Library = "CoolProp_x64";

            // double PropsSI(const char *Output, const char *Name1, double Prop1, const char *Name2, double Prop2, const char *FluidName);
            [DllImport(Library, EntryPoint = "PropsSI", CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
            internal static extern double PropsSI(string Output, string Name1, double Prop1, string Name2, double Prop2, string FluidName);

            // double Props1SI(const char *FluidName, const char *Output);
            [DllImport(Library, EntryPoint = "Props1SI", CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
            internal static extern double Props1SI(string FluidName, string Output);

            // double HAPropsSI(const char *Output, const char *Name1, double Prop1, const char *Name2, double Prop2, const char *Name3, double Prop3);
            [DllImport(Library, EntryPoint = "HAPropsSI", CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
            internal static extern double HAPropsSI(string Output, string Name1, double Prop1, string Name2, double Prop2, string Name3, double Prop3);

            // long PhaseSI(const char* Name1, double Prop1, const char* Name2, double Prop2, const char* FluidName, char* phase, int n);
            [DllImport(Library, EntryPoint = "PhaseSI", CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
            internal static extern int PhaseSI(string Name1, double Prop1, string Name2, double Prop2, string FluidName, StringBuilder phase, int n);

            // double saturation_ancillary(const char* fluid_name, const char* output, int Q, const char* input, double value);
            [DllImport(Library, EntryPoint = "saturation_ancillary", CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
            internal static extern double saturation_ancillary(string fluid_name, string output, int Q, string input, double value);

            // long get_global_param_string(const char* param, char* Output, int n);
            [DllImport(Library, EntryPoint = "get_global_param_string", CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
            internal static extern int get_global_param_string(string param, StringBuilder Output, int n);

            // long get_parameter_information_string(const char* param, char* Output, int n);
            [DllImport(Library, EntryPoint = "get_parameter_information_string", CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
            internal static extern int get_parameter_information_string(string param, StringBuilder Output, int n);

            // long get_fluid_param_string(const char* fluid, const char* param, char* Output, int n);
            [DllImport(Library, EntryPoint = "get_fluid_param_string", CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
            internal static extern int get_fluid_param_string(string fluid, string param, StringBuilder Output, int n);

            // long get_param_index(const char* param);
            [DllImport(Library, EntryPoint = "get_param_index", CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
            internal static extern int get_param_index(string param);

            // int set_reference_stateS(const char* Ref, const char* reference_state);
            [DllImport(Library, EntryPoint = "set_reference_stateS", CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
            internal static extern int set_reference_stateS(string Ref, string reference_state);

            // int set_reference_stateD(const char* Ref, double T, double rhomolar, double hmolar0, double smolar0);
            [DllImport(Library, EntryPoint = "set_reference_stateD", CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
            internal static extern int set_reference_stateD(string Ref, double T, double rhomolar, double hmolar0, double smolar0);
        }

        // ------------------------------------------------------------------
        // Dispatchers - the names the rest of the assembly calls. The C
        // prototypes repeat here; each dispatcher forwards to the engine
        // matching the process bitness.
        // ------------------------------------------------------------------

        internal static double PropsSI(string Output, string Name1, double Prop1, string Name2, double Prop2, string FluidName)
        {
            return IsX64
                ? X64.PropsSI(Output, Name1, Prop1, Name2, Prop2, FluidName)
                : X86.PropsSI(Output, Name1, Prop1, Name2, Prop2, FluidName);
        }

        internal static double Props1SI(string FluidName, string Output)
        {
            return IsX64
                ? X64.Props1SI(FluidName, Output)
                : X86.Props1SI(FluidName, Output);
        }

        internal static double HAPropsSI(string Output, string Name1, double Prop1, string Name2, double Prop2, string Name3, double Prop3)
        {
            return IsX64
                ? X64.HAPropsSI(Output, Name1, Prop1, Name2, Prop2, Name3, Prop3)
                : X86.HAPropsSI(Output, Name1, Prop1, Name2, Prop2, Name3, Prop3);
        }

        internal static int PhaseSI(string Name1, double Prop1, string Name2, double Prop2, string FluidName, StringBuilder phase, int n)
        {
            return IsX64
                ? X64.PhaseSI(Name1, Prop1, Name2, Prop2, FluidName, phase, n)
                : X86.PhaseSI(Name1, Prop1, Name2, Prop2, FluidName, phase, n);
        }

        internal static double saturation_ancillary(string fluid_name, string output, int Q, string input, double value)
        {
            return IsX64
                ? X64.saturation_ancillary(fluid_name, output, Q, input, value)
                : X86.saturation_ancillary(fluid_name, output, Q, input, value);
        }

        internal static int get_global_param_string(string param, StringBuilder Output, int n)
        {
            return IsX64
                ? X64.get_global_param_string(param, Output, n)
                : X86.get_global_param_string(param, Output, n);
        }

        internal static int get_parameter_information_string(string param, StringBuilder Output, int n)
        {
            return IsX64
                ? X64.get_parameter_information_string(param, Output, n)
                : X86.get_parameter_information_string(param, Output, n);
        }

        internal static int get_fluid_param_string(string fluid, string param, StringBuilder Output, int n)
        {
            return IsX64
                ? X64.get_fluid_param_string(fluid, param, Output, n)
                : X86.get_fluid_param_string(fluid, param, Output, n);
        }

        internal static int get_param_index(string param)
        {
            return IsX64
                ? X64.get_param_index(param)
                : X86.get_param_index(param);
        }

        internal static int set_reference_stateS(string Ref, string reference_state)
        {
            return IsX64
                ? X64.set_reference_stateS(Ref, reference_state)
                : X86.set_reference_stateS(Ref, reference_state);
        }

        internal static int set_reference_stateD(string Ref, double T, double rhomolar, double hmolar0, double smolar0)
        {
            return IsX64
                ? X64.set_reference_stateD(Ref, T, rhomolar, hmolar0, smolar0)
                : X86.set_reference_stateD(Ref, T, rhomolar, hmolar0, smolar0);
        }

        // ------------------------------------------------------------------
        // Buffer helper shared by the four string-returning functions.
        // ------------------------------------------------------------------

        internal const int BufferCapacity = 10000;

        /// <summary>
        /// Builds a StringBuilder for CoolProp's (param, buffer, n) functions.
        /// get_parameter_information_string needs the buffer seeded with the
        /// info-type word ("IO", "short", "long", "units") because CoolProp uses
        /// that same buffer both as the info key and as the output destination.
        /// </summary>
        internal static StringBuilder MakeBuffer(string seed)
        {
            return seed == null ? new StringBuilder(BufferCapacity) : new StringBuilder(seed, BufferCapacity);
        }
    }
}
