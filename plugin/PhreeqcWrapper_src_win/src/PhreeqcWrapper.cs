/*
 * PhreeqcWrapper — SMath Studio plugin, Windows port.
 *
 * A thin managed shell over the native VIPhreeqc C ABI. All the chemistry runs
 * in the native engine; this file only marshals strings in and doubles out.
 *
 * Differences from the Mono/Linux build this was ported from:
 *
 *   1. Two Windows engines ship flat at the plugin root under distinct names -
 *      IPhreeqc_x86.dll and IPhreeqc_x64.dll (SMath's installer rejects engine
 *      subfolders whose files share a basename, and the CLR binds a DllImport
 *      by module base name, so the files cannot share "IPhreeqc.dll"). Every
 *      entry point is therefore declared once per engine name and a one-line
 *      dispatcher picks the pair by IntPtr.Size. On Mono both names are
 *      <dllmap>'d to libIPhreeqc.so, so the same code binds there.
 *   2. The native library is loaded explicitly from the plugin folder by
 *      absolute path. .NET Framework does not probe a plugin's own directory
 *      for native dependencies — it searches the *host* executable's directory,
 *      System32 and PATH — so a plain DllImport would never find it next to us.
 *      The preload also turns a bitness mismatch into a readable error instead
 *      of a bare BadImageFormatException (0x8007000B) from the first P/Invoke.
 *   3. Func<int,int,double> is replaced by a delegate. Func<> lives in
 *      System.Core (.NET 3.5) and this assembly targets .NET 2.0.
 *   4. The hard-coded /home/... fallback paths are gone; databases resolve
 *      relative to the plugin folder.
 *
 * Targets .NET Framework 2.0 (CLR 2.0) for Mono/Linux parity, like the other
 * plugins in this collection.
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Globalization;
using SMath.Manager;
using SMath.Controls;
using SMath.Math;
using SMath.Math.Numeric;
using SMath.Math.Symbolic;

[assembly: AssemblyTitle("PhreeqcWrapper")]
[assembly: AssemblyDescription("SMath Studio Native Geochemical Speciation Plugin wrapping IPhreeqc / VIPhreeqc")]
[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]
[assembly: Guid("8ead8647-c30a-4f13-8cc7-5f8ac4f3c066")]
[assembly: ComVisible(false)]
[assembly: InternalsVisibleTo("PhreeqcHarness")]

namespace PhreeqcWrapper
{
    // =========================================================================
    // 1. Native library loader (Windows .dll / Linux .so)
    // =========================================================================
    internal static class NativeLibraryLoader
    {
        private static readonly object _gate = new object();
        private static bool _loaded;

        private const int RTLD_NOW = 2;
        private const int RTLD_GLOBAL = 0x100;

        [DllImport("kernel32", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern IntPtr LoadLibraryA(string fileName);

        [DllImport("kernel32", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern IntPtr SetDllDirectoryA(string path);

        [DllImport("libdl.so.2", EntryPoint = "dlopen")]
        private static extern IntPtr DlOpen(string fileName, int flags);

        /// <summary>True on Windows, false on Mono/Linux. Avoids Environment.OSVersion guessing.</summary>
        public static bool IsWindows
        {
            get { return Path.DirectorySeparatorChar == '\\'; }
        }

        /// <summary>True when this process will use the x64 engine.</summary>
        public static bool IsX64
        {
            get { return IntPtr.Size == 8; }
        }

        /// <summary>
        /// The native file name for this platform and process bitness. On
        /// Windows that is the engine matching the host process — SMath on
        /// 64-bit Windows is a 64-bit process, so IPhreeqc_x64.dll is the name
        /// production actually loads there. Under Mono both Windows names are
        /// <dllmap>'d to libIPhreeqc.so by the .dll.config.
        /// </summary>
        public static string NativeFileName
        {
            get
            {
                if (!IsWindows)
                    return "libIPhreeqc.so";
                return IsX64 ? "IPhreeqc_x64.dll" : "IPhreeqc_x86.dll";
            }
        }

        /// <summary>
        /// Loads the native engine for this platform and process bitness from
        /// the plugin folder, once per process. The preloaded module carries the
        /// engine's file base name (IPhreeqc_x86 / IPhreeqc_x64), which is the
        /// exact name the later DllImports bind to.
        ///
        /// The explicit preload matters on Windows: .NET Framework does not probe
        /// a plugin's own directory for native dependencies, it searches the
        /// *host* executable's folder, System32 and PATH. On Mono the preload is
        /// belt-and-braces — Mono's own probing would find libIPhreeqc.so too.
        /// </summary>
        public static void EnsureLoaded()
        {
            if (_loaded) return;

            lock (_gate)
            {
                if (_loaded) return;

                string dir = PhreeqcEngine.PluginDir;
                string full = Path.Combine(dir, NativeFileName);

                if (!File.Exists(full))
                    throw new DllNotFoundException(
                        "Native engine not found: " + full + Environment.NewLine +
                        "The plugin package ships IPhreeqc_x86.dll and IPhreeqc_x64.dll " +
                        "(and libIPhreeqc.so for Mono) at the package root next to " +
                        "PhreeqcWrapper.dll - the installation looks incomplete.");

                if (IsWindows)
                {
                    try { SetDllDirectoryA(dir); } catch { }

                    IntPtr handle = LoadLibraryA(full);
                    if (handle == IntPtr.Zero)
                    {
                        int code = Marshal.GetLastWin32Error();
                        throw new BadImageFormatException(
                            "LoadLibrary failed for " + full + " (Win32 error " + code + ")." +
                            (code == 193
                                ? " The DLL bitness does not match the host process - this plugin ships IPhreeqc_x86.dll and IPhreeqc_x64.dll and loads the one matching the host."
                                : ""));
                    }
                }
                else
                {
                    // Mono maps DllImport("IPhreeqc_x86"/"IPhreeqc_x64") to
                    // libIPhreeqc.so through the <dllmap>s in the .dll.config.
                    // dlopen here is only to fail early with a clear message.
                    try { DlOpen(full, RTLD_NOW | RTLD_GLOBAL); }
                    catch (DllNotFoundException) { /* fall back to Mono's probing */ }
                    catch (EntryPointNotFoundException) { /* ditto */ }
                }

                _loaded = true;
            }
        }
    }

    // =========================================================================
    // 2. Native IPhreeqc / VIPhreeqc P/Invoke declarations
    //
    // One imported set per engine name: IPhreeqc_x86.dll and IPhreeqc_x64.dll
    // sit side by side at the package root (see the file header) and the CLR
    // binds a DllImport by the module's base name. Both engines export the C
    // API plain — MinGW cdecl decoration is empty on i686 and the Win64 ABI
    // never decorates — so Cdecl plus the exact name binds in both. Each
    // public dispatcher below picks the engine pair by process bitness; its
    // signature is what callers compile against.
    // =========================================================================
    internal static class NativeMethods
    {
        private static bool IsX64
        {
            get { return IntPtr.Size == 8; }
        }

        private static class X86
        {
            private const string LibName = "IPhreeqc_x86";

            [DllImport(LibName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
            internal static extern int CreateIPhreeqc();

            [DllImport(LibName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
            internal static extern int DestroyIPhreeqc(int id);

            [DllImport(LibName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
            internal static extern int LoadDatabase(int id, string filename);

            [DllImport(LibName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
            internal static extern int RunString(int id, string input);

            [DllImport(LibName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
            internal static extern IntPtr GetErrorString(int id);

            [DllImport(LibName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
            internal static extern IntPtr GetSolutionList(int id);

            [DllImport(LibName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
            internal static extern double GetPH(int id, int solution);

            [DllImport(LibName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
            internal static extern double GetSC(int id, int solution);

            [DllImport(LibName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
            internal static extern double GetDensity(int id, int solution);

            [DllImport(LibName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
            internal static extern double GetMu(int id, int solution);

            [DllImport(LibName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
            internal static extern double GetPe(int id, int solution);

            [DllImport(LibName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
            internal static extern double GetTemperature(int id, int solution);

            [DllImport(LibName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
            internal static extern double GetMass(int id, int solution);

            [DllImport(LibName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
            internal static extern double GetSI(int id, int solution, string phase);

            [DllImport(LibName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
            internal static extern double GetMolality(int id, int solution, string species);

            [DllImport(LibName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
            internal static extern double GetActivity(int id, int solution, string species);

            [DllImport(LibName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
            internal static extern double GetTotal(int id, int solution, string element);
        }

        private static class X64
        {
            private const string LibName = "IPhreeqc_x64";

            [DllImport(LibName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
            internal static extern int CreateIPhreeqc();

            [DllImport(LibName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
            internal static extern int DestroyIPhreeqc(int id);

            [DllImport(LibName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
            internal static extern int LoadDatabase(int id, string filename);

            [DllImport(LibName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
            internal static extern int RunString(int id, string input);

            [DllImport(LibName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
            internal static extern IntPtr GetErrorString(int id);

            [DllImport(LibName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
            internal static extern IntPtr GetSolutionList(int id);

            [DllImport(LibName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
            internal static extern double GetPH(int id, int solution);

            [DllImport(LibName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
            internal static extern double GetSC(int id, int solution);

            [DllImport(LibName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
            internal static extern double GetDensity(int id, int solution);

            [DllImport(LibName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
            internal static extern double GetMu(int id, int solution);

            [DllImport(LibName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
            internal static extern double GetPe(int id, int solution);

            [DllImport(LibName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
            internal static extern double GetTemperature(int id, int solution);

            [DllImport(LibName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
            internal static extern double GetMass(int id, int solution);

            [DllImport(LibName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
            internal static extern double GetSI(int id, int solution, string phase);

            [DllImport(LibName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
            internal static extern double GetMolality(int id, int solution, string species);

            [DllImport(LibName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
            internal static extern double GetActivity(int id, int solution, string species);

            [DllImport(LibName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
            internal static extern double GetTotal(int id, int solution, string element);
        }

        public static int CreateIPhreeqc()
        {
            return IsX64 ? X64.CreateIPhreeqc() : X86.CreateIPhreeqc();
        }

        public static int DestroyIPhreeqc(int id)
        {
            return IsX64 ? X64.DestroyIPhreeqc(id) : X86.DestroyIPhreeqc(id);
        }

        public static int LoadDatabase(int id, string filename)
        {
            return IsX64 ? X64.LoadDatabase(id, filename) : X86.LoadDatabase(id, filename);
        }

        public static int RunString(int id, string input)
        {
            return IsX64 ? X64.RunString(id, input) : X86.RunString(id, input);
        }

        public static IntPtr GetErrorString(int id)
        {
            return IsX64 ? X64.GetErrorString(id) : X86.GetErrorString(id);
        }

        public static IntPtr GetSolutionList(int id)
        {
            return IsX64 ? X64.GetSolutionList(id) : X86.GetSolutionList(id);
        }

        public static double GetPH(int id, int solution)
        {
            return IsX64 ? X64.GetPH(id, solution) : X86.GetPH(id, solution);
        }

        public static double GetSC(int id, int solution)
        {
            return IsX64 ? X64.GetSC(id, solution) : X86.GetSC(id, solution);
        }

        public static double GetDensity(int id, int solution)
        {
            return IsX64 ? X64.GetDensity(id, solution) : X86.GetDensity(id, solution);
        }

        public static double GetMu(int id, int solution)
        {
            return IsX64 ? X64.GetMu(id, solution) : X86.GetMu(id, solution);
        }

        public static double GetPe(int id, int solution)
        {
            return IsX64 ? X64.GetPe(id, solution) : X86.GetPe(id, solution);
        }

        public static double GetTemperature(int id, int solution)
        {
            return IsX64 ? X64.GetTemperature(id, solution) : X86.GetTemperature(id, solution);
        }

        public static double GetMass(int id, int solution)
        {
            return IsX64 ? X64.GetMass(id, solution) : X86.GetMass(id, solution);
        }

        public static double GetSI(int id, int solution, string phase)
        {
            return IsX64 ? X64.GetSI(id, solution, phase) : X86.GetSI(id, solution, phase);
        }

        public static double GetMolality(int id, int solution, string species)
        {
            return IsX64 ? X64.GetMolality(id, solution, species) : X86.GetMolality(id, solution, species);
        }

        public static double GetActivity(int id, int solution, string species)
        {
            return IsX64 ? X64.GetActivity(id, solution, species) : X86.GetActivity(id, solution, species);
        }

        public static double GetTotal(int id, int solution, string element)
        {
            return IsX64 ? X64.GetTotal(id, solution, element) : X86.GetTotal(id, solution, element);
        }
    }

    /// <summary>Extracts one value from a solved IPhreeqc instance.</summary>
    internal delegate double SolutionExtractor(int id, int solution);

    /// <summary>Extracts a value that depends on a named phase, species or element.</summary>
    internal delegate double TargetExtractor(int id, int solution, string target);

    // =========================================================================
    // 3. Phreeqc engine manager & evaluation helpers
    // =========================================================================
    public static class PhreeqcEngine
    {
        private static readonly object _syncLock = new object();
        private static string _cachedPluginDir;

        /// <summary>Folder holding PhreeqcWrapper.dll - where the native engines and the databases live.</summary>
        public static string PluginDir
        {
            get
            {
                if (_cachedPluginDir == null)
                {
                    try
                    {
                        string loc = Assembly.GetExecutingAssembly().Location;
                        if (!string.IsNullOrEmpty(loc))
                            _cachedPluginDir = Path.GetDirectoryName(loc);
                    }
                    catch { }

                    if (string.IsNullOrEmpty(_cachedPluginDir))
                        _cachedPluginDir = AppDomain.CurrentDomain.BaseDirectory;
                }
                return _cachedPluginDir;
            }
        }

        /// <summary>
        /// Resolves a database argument to a file on disk.
        ///
        /// A bare name such as "phreeqc.dat" is looked up in, in order:
        ///   1. the current working directory (an explicit relative path wins)
        ///   2. &lt;plugin&gt;\databases\  - where the package installs them
        ///   3. &lt;plugin&gt;\            - the older flat layout
        ///
        /// So a worksheet can just write phreeqc_pH(script) for the default, or
        /// phreeqc_pH(script; "pitzer.dat") to pick another bundled database.
        /// An absolute path is always honoured.
        /// </summary>
        public static string ResolveDatabasePath(string database)
        {
            if (string.IsNullOrEmpty(database))
                database = "phreeqc.dat";

            if (File.Exists(database))
                return Path.GetFullPath(database);

            string dir = PluginDir;

            string inDatabases = Path.Combine(Path.Combine(dir, "databases"), database);
            if (File.Exists(inDatabases))
                return inDatabases;

            string inRoot = Path.Combine(dir, database);
            if (File.Exists(inRoot))
                return inRoot;

            throw new FileNotFoundException(
                "PHREEQC database not found: '" + database + "'." + Environment.NewLine +
                "Searched:" + Environment.NewLine +
                "  " + Path.GetFullPath(database) + Environment.NewLine +
                "  " + inDatabases + Environment.NewLine +
                "  " + inRoot + Environment.NewLine +
                "Pass a bundled name (e.g. \"pitzer.dat\") or an absolute path.",
                database);
        }

        public static string GetStringParam(Entry arg, Store context)
        {
            if (arg == null) return string.Empty;
            try
            {
                TNumber num = Computation.NumericCalculation(arg, context);
                if (num != null && num.obj is TDouble && ((TDouble)num.obj).isText)
                {
                    TDouble td = (TDouble)num.obj;
                    string text = td.Text;
                    if (string.IsNullOrEmpty(text))
                        text = td.ToString();
                    text = text.Trim(new char[] { '"', '\'' });
                    return UnescapeMathString(text);
                }
            }
            catch { }

            string s = arg.Text ?? string.Empty;
            if (s.StartsWith("\"") && s.EndsWith("\"") && s.Length >= 2)
                return UnescapeMathString(s.Substring(1, s.Length - 2));

            return s;
        }

        public static string UnescapeMathString(string s)
        {
            if (string.IsNullOrEmpty(s) || s.IndexOf('\\') < 0) return s;
            System.Text.StringBuilder sb = new System.Text.StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '\\' && i + 1 < s.Length)
                {
                    char c = s[i + 1];
                    if (c == 'n') { sb.Append('\n'); i++; continue; }
                    if (c == 'r') { sb.Append('\r'); i++; continue; }
                    if (c == 't') { sb.Append('\t'); i++; continue; }
                    if (c == '\\') { sb.Append('\\'); i++; continue; }
                    if (c == '"') { sb.Append('"'); i++; continue; }

                    // SMath encodes non-ASCII as \XXXX (4 hex digits).
                    if (i + 4 < s.Length &&
                        Uri.IsHexDigit(s[i + 1]) && Uri.IsHexDigit(s[i + 2]) &&
                        Uri.IsHexDigit(s[i + 3]) && Uri.IsHexDigit(s[i + 4]))
                    {
                        string hex = s.Substring(i + 1, 4);
                        int code = int.Parse(hex, NumberStyles.HexNumber);
                        sb.Append((char)code);
                        i += 4;
                        if (i + 1 < s.Length && s[i + 1] == '\\') i++;
                        continue;
                    }
                }
                sb.Append(s[i]);
            }
            return sb.ToString();
        }

        public static Entry MakeDoubleResult(double val)
        {
            TDouble td = new TDouble(val);
            return Entry.Create(td.ToTerms());
        }

        /// <summary>
        /// SMath's num2str() renders division as an exact rational fraction
        /// (e.g. 358293780948959/100000000000000). PHREEQC's parser cannot read
        /// that, so collapse such fractions to plain decimals first.
        /// </summary>
        public static string SanitizeScript(string script)
        {
            if (string.IsNullOrEmpty(script)) return script;
            return Regex.Replace(script, @"(?<=\s|^|=|-|\+)(\d+(?:\.\d+)?)\s*/\s*(\d+(?:\.\d+)?)(?=\s|;|$)", delegate(Match match)
            {
                double num, den;
                if (double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out num) &&
                    double.TryParse(match.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out den) &&
                    den != 0)
                {
                    return (num / den).ToString("G8", CultureInfo.InvariantCulture);
                }
                return match.Value;
            });
        }

        internal static double RunPhreeqcQuery(string script, string database, SolutionExtractor extractor)
        {
            NativeLibraryLoader.EnsureLoaded();

            lock (_syncLock)
            {
                int id = NativeMethods.CreateIPhreeqc();
                if (id < 0) throw new InvalidOperationException("Failed to allocate an IPhreeqc instance");

                try
                {
                    string dbPath = ResolveDatabasePath(database);
                    int dbRes = NativeMethods.LoadDatabase(id, dbPath);
                    if (dbRes != 0)
                        throw new InvalidOperationException(
                            "Phreeqc LoadDatabase failed for '" + dbPath + "': " + LastError(id));

                    script = SanitizeScript(script);
                    int runRes = NativeMethods.RunString(id, script);
                    if (runRes != 0)
                        throw new InvalidOperationException("Phreeqc RunString failed: " + LastError(id));

                    return extractor(id, LastSolutionNumber(id));
                }
                finally
                {
                    NativeMethods.DestroyIPhreeqc(id);
                }
            }
        }

        /// <summary>
        /// Same as above for accessors that need a phase / species / element name.
        /// Binds the name into a plain two-argument extractor so the run loop
        /// exists only once.
        /// </summary>
        internal static double RunPhreeqcQuery(string script, string database, string target,
                                             TargetExtractor extractor)
        {
            return RunPhreeqcQuery(script, database,
                delegate(int id, int solution) { return extractor(id, solution, target); });
        }

        private static string LastError(int id)
        {
            IntPtr ptr = NativeMethods.GetErrorString(id);
            return ptr != IntPtr.Zero ? Marshal.PtrToStringAnsi(ptr) : string.Empty;
        }

        /// <summary>
        /// The script may define several SOLUTION blocks; report the last one.
        /// GetSolutionList returns a space-separated list of solution numbers.
        /// </summary>
        private static int LastSolutionNumber(int id)
        {
            int solNum = 1;
            IntPtr ptr = NativeMethods.GetSolutionList(id);
            if (ptr == IntPtr.Zero) return solNum;

            string list = Marshal.PtrToStringAnsi(ptr);
            if (string.IsNullOrEmpty(list)) return solNum;

            string[] parts = list.Split(new char[] { ' ', '\t', '\n', '\r', ',' },
                                        StringSplitOptions.RemoveEmptyEntries);
            List<int> valid = new List<int>();
            foreach (string p in parts)
            {
                int parsed;
                if (int.TryParse(p, out parsed) && parsed >= 0)
                    valid.Add(parsed);
            }
            if (valid.Count > 0)
                solNum = valid[valid.Count - 1];
            else if (parts.Length > 0 && int.TryParse(parts[parts.Length - 1], out solNum))
                solNum = 1;   // keep the previous default if the tail is unparseable

            return solNum;
        }
    }

    // =========================================================================
    // 4. SMath plugin implementation
    // =========================================================================
    public class PhreeqcPlugin : IPlugin, IPluginHandleEvaluation, IPluginLowLevelEvaluationFast,
                                 IPluginMathNumericEvaluation, IDisposable
    {
        private static TermInfo[] _termsHandled;

        public void Initialize()
        {
            if (_termsHandled != null) return;

            List<TermInfo> list = new List<TermInfo>();

            // --- arity (1, 2): (script [, database]) -------------------------
            AddPair(list, "phreeqc_pH",
                "Calculates equilibrium solution pH (-)",
                "script");
            AddPair(list, "phreeqc_sc",
                "Calculates solution specific conductance (uS/cm)",
                "script");
            AddPair(list, "phreeqc_density",
                "Calculates solution density at equilibrium (kg/L)",
                "script");
            AddPair(list, "phreeqc_mu",
                "Calculates ionic strength mu (mol/kgw)",
                "script");
            AddPair(list, "phreeqc_pe",
                "Calculates solution redox potential pe = -log(a_e-)",
                "script");
            AddPair(list, "phreeqc_temp",
                "Returns equilibrium solution temperature (degC)",
                "script");
            AddPair(list, "phreeqc_water_mass",
                "Calculates mass of solvent water (kg)",
                "script");

            // --- arity (2, 3): (target, script [, database]) ------------------
            AddTriple(list, "phreeqc_si",
                "Calculates Saturation Index SI = log10(IAP / Ksp) for a mineral or gas phase",
                "phase");
            AddTriple(list, "phreeqc_sr",
                "Calculates Saturation Ratio SR = 10^SI = IAP / Ksp",
                "phase");
            AddTriple(list, "phreeqc_molality",
                "Calculates molality of an aqueous species (mol/kgw)",
                "species");
            AddTriple(list, "phreeqc_activity",
                "Calculates thermodynamic activity of an aqueous species (mol/kgw)",
                "species");
            AddTriple(list, "phreeqc_total",
                "Calculates total dissolved elemental concentration (mol/kgw)",
                "element");

            _termsHandled = list.ToArray();
        }

        private static void AddPair(List<TermInfo> list, string name, string description, string firstArg)
        {
            list.Add(new TermInfo(name, TermType.Function, description + " [default database phreeqc.dat]",
                FunctionSections.Unknown, true,
                new ArgumentInfo[] { new ArgumentInfo(firstArg) }));
            list.Add(new TermInfo(name, TermType.Function, description + " with a custom database",
                FunctionSections.Unknown, true,
                new ArgumentInfo[] { new ArgumentInfo(firstArg), new ArgumentInfo("database") }));
        }

        private static void AddTriple(List<TermInfo> list, string name, string description, string firstArg)
        {
            list.Add(new TermInfo(name, TermType.Function, description + " [default database phreeqc.dat]",
                FunctionSections.Unknown, true,
                new ArgumentInfo[] { new ArgumentInfo(firstArg), new ArgumentInfo("script") }));
            list.Add(new TermInfo(name, TermType.Function, description + " with a custom database",
                FunctionSections.Unknown, true,
                new ArgumentInfo[] { new ArgumentInfo(firstArg), new ArgumentInfo("script"), new ArgumentInfo("database") }));
        }

        public TermInfo[] GetTermsHandled(SessionProfile sessionProfile)
        {
            if (_termsHandled == null)
                Initialize();
            return _termsHandled;
        }

        public bool TryEvaluateExpression(Entry value, Store context, out Entry result)
        {
            result = null;
            if (value == null || value.Type != TermType.Function)
                return false;

            string fnName = value.Text ?? string.Empty;
            Entry[] args = value.Items ?? new Entry[0];
            int argc = args.Length;

            try
            {
                switch (fnName.ToLowerInvariant())
                {
                    // ---- (script [, database]) --------------------------------
                    case "phreeqc_ph":
                        return OneArg(args, argc, context, out result, NativeMethods.GetPH);
                    case "phreeqc_sc":
                        return OneArg(args, argc, context, out result, NativeMethods.GetSC);
                    case "phreeqc_density":
                        return OneArg(args, argc, context, out result, NativeMethods.GetDensity);
                    case "phreeqc_mu":
                        return OneArg(args, argc, context, out result, NativeMethods.GetMu);
                    case "phreeqc_pe":
                        return OneArg(args, argc, context, out result, NativeMethods.GetPe);
                    case "phreeqc_temp":
                        return OneArg(args, argc, context, out result, NativeMethods.GetTemperature);
                    case "phreeqc_water_mass":
                        return OneArg(args, argc, context, out result, NativeMethods.GetMass);

                    // ---- (target, script [, database]) ------------------------
                    case "phreeqc_si":
                        return TwoArg(args, argc, context, out result, NativeMethods.GetSI);
                    case "phreeqc_sr":
                    {
                        if (argc < 2 || argc > 3) return false;
                        string phase = PhreeqcEngine.GetStringParam(args[0], context);
                        string srScript = PhreeqcEngine.GetStringParam(args[1], context);
                        string srDb = argc == 3 ? PhreeqcEngine.GetStringParam(args[2], context) : null;
                        double si = PhreeqcEngine.RunPhreeqcQuery(srScript, srDb, phase, NativeMethods.GetSI);
                        result = PhreeqcEngine.MakeDoubleResult(Math.Pow(10.0, si));
                        return true;
                    }
                    case "phreeqc_molality":
                        return TwoArg(args, argc, context, out result, NativeMethods.GetMolality);
                    case "phreeqc_activity":
                        return TwoArg(args, argc, context, out result, NativeMethods.GetActivity);
                    case "phreeqc_total":
                        return TwoArg(args, argc, context, out result, NativeMethods.GetTotal);

                    default:
                        return false;
                }
            }
            catch (Exception ex)
            {
                throw new Exception("Phreeqc evaluation error in " + fnName + ": " + ex.Message, ex);
            }
        }

        /// <summary>Shared body for the (script [, database]) functions.</summary>
        private static bool OneArg(Entry[] args, int argc, Store context, out Entry result,
                                   SolutionExtractor extractor)
        {
            result = null;
            if (argc < 1 || argc > 2) return false;

            string script = PhreeqcEngine.GetStringParam(args[0], context);
            string db = argc == 2 ? PhreeqcEngine.GetStringParam(args[1], context) : null;

            result = PhreeqcEngine.MakeDoubleResult(PhreeqcEngine.RunPhreeqcQuery(script, db, extractor));
            return true;
        }

        /// <summary>Shared body for the (target, script [, database]) functions.</summary>
        private static bool TwoArg(Entry[] args, int argc, Store context, out Entry result,
                                   TargetExtractor extractor)
        {
            result = null;
            if (argc < 2 || argc > 3) return false;

            string target = PhreeqcEngine.GetStringParam(args[0], context);
            string script = PhreeqcEngine.GetStringParam(args[1], context);
            string db = argc == 3 ? PhreeqcEngine.GetStringParam(args[2], context) : null;

            result = PhreeqcEngine.MakeDoubleResult(PhreeqcEngine.RunPhreeqcQuery(script, db, target, extractor));
            return true;
        }

        public bool NumericEvaluation(Term value, TNumber[] args, ref TNumber result)
        {
            // These functions take strings, so they are handled at the
            // expression level by TryEvaluateExpression, not here.
            return false;
        }

        public void Dispose()
        {
        }
    }
}
