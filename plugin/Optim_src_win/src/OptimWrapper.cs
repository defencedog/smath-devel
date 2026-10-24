/*
 * OptimWrapper - SMath Studio plugin for OptimLib (kthohr/optim).
 *
 * OptimLib is a C++ optimisation library. This plugin wraps its collection of
 * built-in test functions in a native C ABI engine (Optim_x86.dll /
 * Optim_x64.dll, built by native\build-native.ps1) and evaluates them at the
 * expression level so that a worksheet can solve them without compiling
 * anything.
 *
 * Design notes:
 *
 *   1. Two Windows engines ship flat at the plugin root under distinct names -
 *      Optim_x86.dll and Optim_x64.dll (SMath's installer rejects engine
 *      subfolders whose files share a basename, and the CLR binds a DllImport
 *      by module base name, so the files cannot share "Optim.dll"). Every
 *      entry point is declared once per engine name and a one-line dispatcher
 *      picks the pair by IntPtr.Size. On Mono both names are <dllmap>'d to
 *      libOptim.so in OptimWrapper.dll.config.
 *   2. The native library is loaded explicitly from the plugin folder by
 *      absolute path. .NET Framework does not probe a plugin's own directory
 *      for native dependencies - it searches the *host* executable's
 *      directory, System32 and PATH - so a plain DllImport would never find it
 *      next to us. The preload also turns a bitness mismatch into a readable
 *      error instead of a bare BadImageFormatException (0x8007000B).
 *   3. Functions are evaluated through IPluginLowLevelEvaluationFast, because
 *      vectors and matrices arrive as expressions. Only built-in test
 *      functions are supported - there is no expression parser.
 *   4. Targets .NET Framework 2.0 (CLR 2.0) for Mono/Linux parity, like the
 *      other plugins in this collection.
 *
 * Result layout: every solver returns a column vector [x1; ...; xn; f; iter;
 * ok], where f is the objective value (or the largest absolute residual for
 * root systems), iter is the number of iterations/generations, and ok is 1
 * when the algorithm reports convergence, 0 when it returned a best guess.
 */

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SMath.Manager;
using SMath.Math;
using SMath.Math.Numeric;
using SMath.Math.Symbolic;

[assembly: AssemblyTitle("OptimWrapper")]
[assembly: AssemblyDescription("SMath Studio plugin wrapping OptimLib test functions and optimisation algorithms")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
[assembly: Guid("3c9f7e0a-c2b1-4eae-ab68-9ea2c518c510")]
[assembly: ComVisible(false)]
[assembly: InternalsVisibleTo("OptimHarness")]

namespace OptimWrapper
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

        public static bool IsWindows
        {
            get { return Path.DirectorySeparatorChar == '\\'; }
        }

        public static bool IsX64
        {
            get { return IntPtr.Size == 8; }
        }

        /// <summary>
        /// The native file name for this platform and process bitness. On
        /// Windows that is the engine matching the host process - SMath on
        /// 64-bit Windows is a 64-bit process, so Optim_x64.dll is the name
        /// production actually loads there. Under Mono both Windows names are
        /// <dllmap>'d to libOptim.so by OptimWrapper.dll.config.
        /// </summary>
        public static string NativeFileName
        {
            get
            {
                if (!IsWindows)
                    return "libOptim.so";
                return IsX64 ? "Optim_x64.dll" : "Optim_x86.dll";
            }
        }

        public static void EnsureLoaded()
        {
            if (_loaded) return;

            lock (_gate)
            {
                if (_loaded) return;

                string dir = OptimEngine.PluginDir;
                string full = Path.Combine(dir, NativeFileName);

                if (!File.Exists(full))
                    throw new DllNotFoundException(
                        "Native engine not found: " + full + Environment.NewLine +
                        "The plugin package ships Optim_x86.dll and Optim_x64.dll " +
                        "(and libOptim.so for Mono) at the package root next to " +
                        "OptimWrapper.dll - the installation looks incomplete.");

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
                                ? " The DLL bitness does not match the host process - this plugin ships Optim_x86.dll and Optim_x64.dll and loads the one matching the host."
                                : ""));
                    }
                }
                else
                {
                    try { DlOpen(full, RTLD_NOW | RTLD_GLOBAL); }
                    catch (DllNotFoundException) { }
                    catch (EntryPointNotFoundException) { }
                }

                _loaded = true;
            }
        }
    }

    // =========================================================================
    // 2. Native Optim engine P/Invoke declarations
    //
    // One imported set per engine name; the public dispatchers below pick the
    // pair by process bitness.
    // =========================================================================
    internal static class NativeMethods
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate int CustomEvalCallback(
            IntPtr x, int n, IntPtr outValues, int outLen, IntPtr user);

        private static bool IsX64
        {
            get { return IntPtr.Size == 8; }
        }

        private static class X86
        {
            private const string LibName = "Optim_x86";

            [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
            internal static extern int optim_api_version();

            [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
            internal static extern IntPtr optim_library_name();

            [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
            internal static extern IntPtr optim_last_error();

            [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
            internal static extern int optim_algo_count();

            [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
            internal static extern int optim_algo_kind(int id);

            [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
            internal static extern IntPtr optim_algo_name(int id);

            [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
            internal static extern int optim_testfn_count();

            [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
            internal static extern int optim_testfn_kind(int id);

            [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
            internal static extern int optim_testfn_dim(int id);

            [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
            internal static extern int optim_testfn_bounded(int id);

            [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
            internal static extern double optim_testfn_lower(int id, int index);

            [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
            internal static extern double optim_testfn_upper(int id, int index);

            [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
            internal static extern IntPtr optim_testfn_name(int id);

            [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
            internal static extern double optim_testfn_value(int id, int n, double[] x);

            [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
            internal static extern int optim_solve(
                int algo, int fn, int n, double[] x0, double[] xOut,
                out double f, out int iter, out double err, int maxIter, int seed);

            [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
            internal static extern int optim_solve_custom(
                int algo, int kind, int n, int m,
                double[] x0, double[] xOut,
                out double f, out int iter, out double err,
                int maxIter, int seed,
                double[] lb, double[] ub, int bounded,
                CustomEvalCallback eval, IntPtr user);
        }

        private static class X64
        {
            private const string LibName = "Optim_x64";

            [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
            internal static extern int optim_api_version();

            [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
            internal static extern IntPtr optim_library_name();

            [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
            internal static extern IntPtr optim_last_error();

            [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
            internal static extern int optim_algo_count();

            [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
            internal static extern int optim_algo_kind(int id);

            [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
            internal static extern IntPtr optim_algo_name(int id);

            [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
            internal static extern int optim_testfn_count();

            [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
            internal static extern int optim_testfn_kind(int id);

            [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
            internal static extern int optim_testfn_dim(int id);

            [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
            internal static extern int optim_testfn_bounded(int id);

            [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
            internal static extern double optim_testfn_lower(int id, int index);

            [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
            internal static extern double optim_testfn_upper(int id, int index);

            [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
            internal static extern IntPtr optim_testfn_name(int id);

            [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
            internal static extern double optim_testfn_value(int id, int n, double[] x);

            [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
            internal static extern int optim_solve(
                int algo, int fn, int n, double[] x0, double[] xOut,
                out double f, out int iter, out double err, int maxIter, int seed);

            [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
            internal static extern int optim_solve_custom(
                int algo, int kind, int n, int m,
                double[] x0, double[] xOut,
                out double f, out int iter, out double err,
                int maxIter, int seed,
                double[] lb, double[] ub, int bounded,
                CustomEvalCallback eval, IntPtr user);
        }

        internal static int ApiVersion()
        {
            return IsX64 ? X64.optim_api_version() : X86.optim_api_version();
        }

        internal static string LibraryName()
        {
            IntPtr p = IsX64 ? X64.optim_library_name() : X86.optim_library_name();
            return Marshal.PtrToStringAnsi(p);
        }

        internal static string LastError()
        {
            IntPtr p = IsX64 ? X64.optim_last_error() : X86.optim_last_error();
            return Marshal.PtrToStringAnsi(p);
        }

        internal static int AlgoCount()
        {
            return IsX64 ? X64.optim_algo_count() : X86.optim_algo_count();
        }

        internal static int AlgoKind(int id)
        {
            return IsX64 ? X64.optim_algo_kind(id) : X86.optim_algo_kind(id);
        }

        internal static string AlgoName(int id)
        {
            IntPtr p = IsX64 ? X64.optim_algo_name(id) : X86.optim_algo_name(id);
            return Marshal.PtrToStringAnsi(p);
        }

        internal static int TestFnCount()
        {
            return IsX64 ? X64.optim_testfn_count() : X86.optim_testfn_count();
        }

        internal static int TestFnKind(int id)
        {
            return IsX64 ? X64.optim_testfn_kind(id) : X86.optim_testfn_kind(id);
        }

        internal static int TestFnDim(int id)
        {
            return IsX64 ? X64.optim_testfn_dim(id) : X86.optim_testfn_dim(id);
        }

        internal static int TestFnBounded(int id)
        {
            return IsX64 ? X64.optim_testfn_bounded(id) : X86.optim_testfn_bounded(id);
        }

        internal static double TestFnLower(int id, int index)
        {
            return IsX64 ? X64.optim_testfn_lower(id, index) : X86.optim_testfn_lower(id, index);
        }

        internal static double TestFnUpper(int id, int index)
        {
            return IsX64 ? X64.optim_testfn_upper(id, index) : X86.optim_testfn_upper(id, index);
        }

        internal static string TestFnName(int id)
        {
            IntPtr p = IsX64 ? X64.optim_testfn_name(id) : X86.optim_testfn_name(id);
            return Marshal.PtrToStringAnsi(p);
        }

        internal static double TestFnValue(int id, int n, double[] x)
        {
            return IsX64 ? X64.optim_testfn_value(id, n, x) : X86.optim_testfn_value(id, n, x);
        }

        internal static int Solve(int algo, int fn, int n, double[] x0, double[] xOut,
            out double f, out int iter, out double err, int maxIter, int seed)
        {
            if (IsX64)
                return X64.optim_solve(algo, fn, n, x0, xOut, out f, out iter, out err, maxIter, seed);
            return X86.optim_solve(algo, fn, n, x0, xOut, out f, out iter, out err, maxIter, seed);
        }

        internal static int SolveCustom(
            int algo, int kind, int n, int m,
            double[] x0, double[] xOut,
            out double f, out int iter, out double err,
            int maxIter, int seed,
            double[] lb, double[] ub, int bounded,
            CustomEvalCallback eval)
        {
            if (IsX64)
                return X64.optim_solve_custom(algo, kind, n, m, x0, xOut,
                    out f, out iter, out err, maxIter, seed, lb, ub, bounded, eval, IntPtr.Zero);
            return X86.optim_solve_custom(algo, kind, n, m, x0, xOut,
                out f, out iter, out err, maxIter, seed, lb, ub, bounded, eval, IntPtr.Zero);
        }
    }

    // =========================================================================
    // 3. Managed engine facade
    // =========================================================================
    public sealed class OptimResult
    {
        public double[] X;
        public double F;
        public int Iterations;
        public double Error;
        public bool Success;
        public string ErrorMessage;

        public double[] ToVector()
        {
            int n = X == null ? 0 : X.Length;
            double[] packed = new double[n + 3];
            for (int i = 0; i < n; i++) packed[i] = X[i];
            packed[n] = F;
            packed[n + 1] = Iterations;
            packed[n + 2] = Success ? 1.0 : 0.0;
            return packed;
        }
    }

    public static class OptimEngine
    {
        private const int SeedValue = 42;

        private static readonly object _syncLock = new object();
        private static string _cachedPluginDir;
        private static int _apiVersion = -1;

        /// <summary>Folder holding OptimWrapper.dll - where the native engines live.</summary>
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

        public static int ApiVersion
        {
            get
            {
                EnsureInitialized();
                return _apiVersion;
            }
        }

        public static int AlgorithmCount
        {
            get
            {
                EnsureInitialized();
                return NativeMethods.AlgoCount();
            }
        }

        public static int FunctionCount
        {
            get
            {
                EnsureInitialized();
                return NativeMethods.TestFnCount();
            }
        }

        private static void EnsureInitialized()
        {
            if (_apiVersion >= 0) return;

            lock (_syncLock)
            {
                if (_apiVersion >= 0) return;

                NativeLibraryLoader.EnsureLoaded();
                int version = NativeMethods.ApiVersion();
                if (version != 101)
                    throw new InvalidOperationException(
                        "Unsupported Optim engine ABI " + version + " (this wrapper expects 101).");
                _apiVersion = version;
            }
        }

        public static int FunctionKind(int fn)
        {
            EnsureInitialized();
            if (fn < 0 || fn >= NativeMethods.TestFnCount())
                throw new ArgumentOutOfRangeException("fn", "Test-function id is out of range.");
            return NativeMethods.TestFnKind(fn);
        }

        public static int FunctionDim(int fn)
        {
            EnsureInitialized();
            if (fn < 0 || fn >= NativeMethods.TestFnCount())
                throw new ArgumentOutOfRangeException("fn", "Test-function id is out of range.");
            return NativeMethods.TestFnDim(fn);
        }

        public static double Evaluate(int fn, double[] x)
        {
            EnsureInitialized();
            if (fn < 0 || fn >= NativeMethods.TestFnCount())
                throw new ArgumentOutOfRangeException("fn", "Test-function id is out of range.");
            if (x == null || x.Length == 0)
                throw new ArgumentException("x must contain at least one value.");

            if (NativeMethods.TestFnKind(fn) == 2)
                throw new ArgumentException(
                    "Test function " + fn + " is a root system and has no scalar objective.");

            int dim = NativeMethods.TestFnDim(fn);
            if (dim != 0 && dim != x.Length)
                throw new ArgumentException(
                    "Test function " + fn + " expects " + dim + " variables, got " + x.Length + ".");

            return NativeMethods.TestFnValue(fn, x.Length, x);
        }

        public static OptimResult SolveCustom(
            int algo, Entry expr, string[] names, double[] x0,
            double[] lower, double[] upper, int maxIter, Store context)
        {
            EnsureInitialized();
            int algoCount = NativeMethods.AlgoCount();
            if (algo < 0 || algo >= algoCount)
                throw new ArgumentOutOfRangeException("algo", "Algorithm id is out of range.");
            if (expr == null)
                throw new ArgumentException("The expression is empty.");
            if (names == null || names.Length == 0)
                throw new ArgumentException("The variable vector is empty.");
            if (x0 == null || x0.Length == 0)
                throw new ArgumentException("x0 must contain at least one value.");
            if (names.Length != x0.Length)
                throw new ArgumentException(
                    "The variable vector has " + names.Length +
                    " name(s) but x0 has " + x0.Length + " value(s).");

            int kind = NativeMethods.AlgoKind(algo);
            if (kind < 0 || kind > 2)
                throw new ArgumentException("Unknown algorithm kind.");

            bool bounded = lower != null && upper != null;
            if (bounded)
            {
                if (lower.Length != x0.Length || upper.Length != x0.Length)
                    throw new ArgumentException("The bound vectors must have the same length as x0.");
                for (int i = 0; i < x0.Length; i++)
                    if (lower[i] > upper[i])
                        throw new ArgumentException(
                            "Lower bound exceeds upper bound for parameter " + (i + 1) + ".");
            }

            double[] probe = OptimExpression.Evaluate(expr, names, x0, context);
            int m = 0;
            if (kind == 0)
            {
                if (probe.Length != 1)
                    throw new ArgumentException(
                        "An objective expression must return one value; it returned " +
                        probe.Length + ". Use optim_broyden for residual systems or " +
                        "optim_sumt for [f; constraints].");
            }
            else if (kind == 1)
            {
                if (probe.Length < 2)
                    throw new ArgumentException(
                        "optim_sumt expects an expression [f; c1; ...; cm] with at least " +
                        "one constraint; it returned " + probe.Length + " value(s).");
                m = probe.Length - 1;
            }
            else
            {
                if (probe.Length < 1)
                    throw new ArgumentException("The residual system returned no values.");
                m = probe.Length;
                if (m != x0.Length)
                    throw new ArgumentException(
                        "A root system must be square: it has " + m + " equation(s) but " +
                        x0.Length + " variable(s). Add or remove residuals so m = n.");
            }

            CustomEvalContext callback = new CustomEvalContext(expr, names, context, kind, m);
            double[] xOut = new double[x0.Length];
            double f;
            int iter;
            double err;

            int rc = NativeMethods.SolveCustom(algo, kind, x0.Length, m, x0, xOut,
                out f, out iter, out err, maxIter, SeedValue,
                bounded ? lower : null, bounded ? upper : null, bounded ? 1 : 0, callback.Evaluate);

            if (callback.Error != null)
                throw new InvalidOperationException(
                    "Custom expression evaluation failed: " + callback.Error.Message,
                    callback.Error);

            OptimResult result = new OptimResult();
            result.X = xOut;
            result.F = f;
            result.Iterations = iter;
            result.Error = err;
            result.Success = (rc == 0);
            result.ErrorMessage = (rc == 0) ? null : NativeMethods.LastError();

            if (rc == 2 || rc == 3)
                throw new InvalidOperationException(
                    string.IsNullOrEmpty(result.ErrorMessage)
                        ? "Optim engine rejected the request."
                        : result.ErrorMessage);

            return result;
        }

        public static OptimResult Solve(int algo, int fn, double[] x0, int maxIter)
        {
            EnsureInitialized();
            int algoCount = NativeMethods.AlgoCount();
            int fnCount = NativeMethods.TestFnCount();

            if (algo < 0 || algo >= algoCount)
                throw new ArgumentOutOfRangeException("algo", "Algorithm id is out of range.");
            if (fn < 0 || fn >= fnCount)
                throw new ArgumentOutOfRangeException("fn", "Test-function id is out of range.");
            if (x0 == null || x0.Length == 0)
                throw new ArgumentException("x0 must contain at least one value.");

            int rc;
            double f;
            int iter;
            double err;
            double[] xOut = new double[x0.Length];

            rc = NativeMethods.Solve(algo, fn, x0.Length, x0, xOut,
                out f, out iter, out err, maxIter, SeedValue);

            OptimResult result = new OptimResult();
            result.X = xOut;
            result.F = f;
            result.Iterations = iter;
            result.Error = err;
            result.Success = (rc == 0);
            result.ErrorMessage = (rc == 0) ? null : NativeMethods.LastError();

            if (rc == 2 || rc == 3)
                throw new InvalidOperationException(
                    string.IsNullOrEmpty(result.ErrorMessage)
                        ? "Optim engine rejected the request."
                        : result.ErrorMessage);

            return result;
        }

        public static string AlgorithmName(int algo)
        {
            EnsureInitialized();
            return NativeMethods.AlgoName(algo);
        }

        public static string FunctionName(int fn)
        {
            EnsureInitialized();
            return NativeMethods.TestFnName(fn);
        }

        // ------------------------------------------------------------- results

        public static Entry MakeMatrixResult(double[] values)
        {
            int rows = values.Length;
            TNumber[,] cells = new TNumber[rows, 1];
            for (int i = 0; i < rows; i++)
                cells[i, 0] = new TNumber(values[i]);

            return Entry.Create(new TMatrix(cells).ToTerms());
        }

        public static Entry MakeDoubleResult(double value)
        {
            return Entry.Create(new TDouble(value).ToTerms());
        }

        // ------------------------------------------------------------ arguments

        public static double ExtractScalar(Entry entry, Store context)
        {
            double[] values = ExtractDoubleArray(entry, context);
            return (values != null && values.Length > 0) ? values[0] : 0.0;
        }

        public static int ExtractInt(Entry entry, Store context)
        {
            double value = ExtractScalar(entry, context);
            double rounded = Math.Round(value);
            if (Math.Abs(value - rounded) > 1e-9)
                throw new ArgumentException("Expected an integer, got " + value.ToString(CultureInfo.InvariantCulture) + ".");
            return (int)rounded;
        }

        /// <summary>
        /// Coerce an expression to a flat array of doubles. A column matrix, a row
        /// matrix and a vector all arrive here as the same flat array.
        /// </summary>
        public static double[] ExtractDoubleArray(Entry entry, Store context)
        {
            if (entry == null) return new double[0];

            // 1. Matrix literal in the tree: [v0, ..., rows, cols]
            if (entry.Items != null && entry.Items.Length >= 2)
            {
                int count = entry.Items.Length;
                bool isMatrixLiteral = entry.Text == "mat" || entry.Text == "matrix" ||
                                       string.IsNullOrEmpty(entry.Text);

                if (isMatrixLiteral)
                {
                    int rows = (int)ExtractSoleScalar(entry.Items[count - 2], context);
                    int cols = (int)ExtractSoleScalar(entry.Items[count - 1], context);
                    if (rows > 0 && cols > 0 && rows * cols == count - 2)
                    {
                        double[] values = new double[rows * cols];
                        for (int i = 0; i < values.Length; i++)
                            values[i] = ExtractSoleScalar(entry.Items[i], context);
                        return values;
                    }
                }
            }

            // 2. Let the CAS evaluate it (handles literals, variables and vectors)
            if (context != null)
            {
                TNumber number = null;
                try { number = Computation.NumericCalculation(entry, context); }
                catch { }

                if (number != null)
                {
                    TMatrix matrix = number.obj as TMatrix;
                    if (matrix != null)
                    {
                        int rows = matrix.unit.GetLength(0);
                        int cols = matrix.unit.GetLength(1);
                        double[] values = new double[rows * cols];
                        int index = 0;
                        for (int r = 0; r < rows; r++)
                            for (int c = 0; c < cols; c++)
                                values[index++] = matrix[r, c].obj.ToDouble();
                        return values;
                    }

                    TDouble asDouble = number.obj as TDouble;
                    if (asDouble != null) return new double[] { asDouble.ToDouble() };

                    TFraction asFraction = number.obj as TFraction;
                    if (asFraction != null) return new double[] { asFraction.ToDouble() };
                }
            }

            // 3. Plain scalar literal
            double literal;
            if (double.TryParse(entry.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out literal))
                return new double[] { literal };

            throw new ArgumentException(
                "Expected a number or a vector, got '" + (entry.Text ?? "?") + "'.");
        }

        private static double ExtractSoleScalar(Entry entry, Store context)
        {
            double[] values = ExtractDoubleArray(entry, context);
            if (values == null || values.Length == 0)
                throw new ArgumentException("Expected a number.");
            return values[0];
        }
    }

    // =========================================================================
    // 4. SMath plugin implementation
    // =========================================================================
    public class OptimPlugin : IPlugin, IPluginHandleEvaluation, IPluginLowLevelEvaluationFast
    {
        private static TermInfo[] _termsHandled;

        private static readonly string[] AlgorithmFunctions =
        {
            "optim_bfgs", "optim_lbfgs", "optim_cg", "optim_gd", "optim_newton",
            "optim_nm", "optim_de", "optim_de_prmm", "optim_pso", "optim_pso_dv",
            "optim_sumt", "optim_broyden", "optim_broyden_df"
        };

        private static readonly string[] AlgorithmDescriptions =
        {
            "BFGS quasi-Newton minimisation.",
            "L-BFGS limited-memory quasi-Newton minimisation.",
            "Conjugate-gradient minimisation.",
            "Gradient-descent minimisation.",
            "Newton minimisation (numerical Hessian).",
            "Nelder-Mead simplex minimisation.",
            "Differential evolution global minimisation.",
            "Differential evolution with pro-active mutation (DE-PRMM).",
            "Particle-swarm global minimisation.",
            "Particle-swarm with differential variation (PSO-DV).",
            "Sequential unconstrained minimisation (SUMT) for constrained problems.",
            "Broyden root finding for systems of equations.",
            "Broyden (dual form) root finding for systems of equations."
        };

        public void Initialize()
        {
            if (_termsHandled != null) return;

            List<TermInfo> list = new List<TermInfo>();

            list.Add(new TermInfo("optim_eval_expr",
                TermType.Function,
                "Evaluates a worksheet expression at x. Variables are inferred from the expression. Returns a number or a vector.",
                FunctionSections.Unknown, true,
                new ArgumentInfo[] { new ArgumentInfo("expr"), new ArgumentInfo("x") }));

            list.Add(new TermInfo("optim_eval_expr",
                TermType.Function,
                "Evaluates a worksheet expression at x using the given variable vector. Returns a number or a vector.",
                FunctionSections.Unknown, true,
                new ArgumentInfo[]
                {
                    new ArgumentInfo("expr"), new ArgumentInfo("vars"), new ArgumentInfo("x")
                }));

            list.Add(new TermInfo("optim_algo_count",
                TermType.Function,
                "Returns the number of available optimisation algorithms.",
                FunctionSections.Unknown, true,
                new ArgumentInfo[0]));

            AddSolverTerms(list);

            for (int i = 0; i < AlgorithmFunctions.Length; i++)
            {
                string name = AlgorithmFunctions[i];
                string description = AlgorithmDescriptions[i];

                bool globalOnly = name == "optim_de" || name == "optim_de_prmm" ||
                                  name == "optim_pso" || name == "optim_pso_dv";
                if (globalOnly)
                {
                    list.Add(new TermInfo(name,
                        TermType.Function,
                        description + " Minimises a worksheet expression over explicit box bounds: (expr, vars, x0, p_min, p_max). Returns [x; f; iterations; ok].",
                        FunctionSections.Unknown, true,
                        new ArgumentInfo[]
                        {
                            new ArgumentInfo("expr"), new ArgumentInfo("vars"),
                            new ArgumentInfo("x0"), new ArgumentInfo("p_min"), new ArgumentInfo("p_max")
                        }));

                    list.Add(new TermInfo(name,
                        TermType.Function,
                        description + " Bounded expression form with a generation budget: (expr, vars, x0, p_min, p_max, max_iter). Returns [x; f; iterations; ok].",
                        FunctionSections.Unknown, true,
                        new ArgumentInfo[]
                        {
                            new ArgumentInfo("expr"), new ArgumentInfo("vars"),
                            new ArgumentInfo("x0"), new ArgumentInfo("p_min"), new ArgumentInfo("p_max"),
                            new ArgumentInfo("max_iter")
                        }));
                    continue;
                }

                string wording = " Accepts a worksheet expression or a worksheet function; returns [x; f; iterations; ok].";
                string middle = " Accepts (expr, vars, x0) or (expr, x0, max_iter); returns [x; f; iterations; ok].";
                if (name == "optim_sumt")
                {
                    wording = " Accepts a constrained expression [f; c1; ...; cm] with ci <= 0; returns [x; f; iterations; ok].";
                    middle = " Accepts (expr, vars, x0) or (expr, x0, max_iter) with [f; c1; ...; cm], ci <= 0.";
                }
                else if (name == "optim_broyden" || name == "optim_broyden_df")
                {
                    wording = " Accepts a residual expression [r1; ...; rm]; returns [x; f; iterations; ok].";
                    middle = " Accepts (expr, vars, x0) or (expr, x0, max_iter) with residuals [r1; ...; rm].";
                }

                list.Add(new TermInfo(name,
                    TermType.Function,
                    description + wording,
                    FunctionSections.Unknown, true,
                    new ArgumentInfo[] { new ArgumentInfo("expr"), new ArgumentInfo("x0") }));

                list.Add(new TermInfo(name,
                    TermType.Function,
                    description + middle,
                    FunctionSections.Unknown, true,
                    new ArgumentInfo[]
                    {
                        new ArgumentInfo("expr"), new ArgumentInfo("x0/vars"),
                        new ArgumentInfo("max_iter/x0")
                    }));

                list.Add(new TermInfo(name,
                    TermType.Function,
                    description + " Accepts (expr, vars, x0, max_iter); returns [x; f; iterations; ok].",
                    FunctionSections.Unknown, true,
                    new ArgumentInfo[]
                    {
                        new ArgumentInfo("expr"), new ArgumentInfo("vars"),
                        new ArgumentInfo("x0"), new ArgumentInfo("max_iter")
                    }));
            }

            _termsHandled = list.ToArray();
        }

        private static void AddSolverTerms(List<TermInfo> list)
        {
            list.Add(new TermInfo("optim_solve_expr",
                TermType.Function,
                "Solves a worksheet expression with algorithm algo; free variables are inferred. Deterministic/SUMT/Broyden algorithms only (use the DE/PSO wrappers for bounds). Returns [x; f; iterations; ok].",
                FunctionSections.Unknown, true,
                new ArgumentInfo[]
                {
                    new ArgumentInfo("algo"), new ArgumentInfo("expr"), new ArgumentInfo("x0")
                }));

            list.Add(new TermInfo("optim_solve_expr",
                TermType.Function,
                "Solves a worksheet expression; use (algo, expr, x0, max_iter) or (algo, expr, vars, x0). Deterministic/SUMT/Broyden algorithms only. Returns [x; f; iterations; ok].",
                FunctionSections.Unknown, true,
                new ArgumentInfo[]
                {
                    new ArgumentInfo("algo"), new ArgumentInfo("expr"),
                    new ArgumentInfo("x0/vars"), new ArgumentInfo("max_iter/x0")
                }));

            list.Add(new TermInfo("optim_solve_expr",
                TermType.Function,
                "Solves a worksheet expression with explicit variables and iteration budget. Deterministic/SUMT/Broyden algorithms only. Returns [x; f; iterations; ok].",
                FunctionSections.Unknown, true,
                new ArgumentInfo[]
                {
                    new ArgumentInfo("algo"), new ArgumentInfo("expr"),
                    new ArgumentInfo("vars"), new ArgumentInfo("x0"),
                    new ArgumentInfo("max_iter")
                }));
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
                    case "optim_eval_expr":
                    {
                        if (argc != 2 && argc != 3) return false;

                        Entry expr = args[0];
                        double[] x = OptimEngine.ExtractDoubleArray(args[argc - 1], context);
                        if (x.Length == 0)
                            throw new ArgumentException("x must contain at least one value.");

                        Entry functionBody;
                        string[] functionNames;
                        string[] names;
                        if (OptimExpression.TryResolveFunction(expr, context, out functionBody, out functionNames))
                        {
                            expr = functionBody;
                            names = functionNames;
                        }
                        else
                        {
                            names = argc == 3
                                ? OptimExpression.ParseVariableNames(args[1], context)
                                : OptimExpression.InferVariableNames(expr, context, x.Length);
                        }

                        if (names.Length != x.Length)
                            throw new ArgumentException(
                                "The variable vector has " + names.Length +
                                " name(s) but x has " + x.Length + " value(s).");

                        double[] values = OptimExpression.Evaluate(expr, names, x, context);
                        result = values.Length == 1
                            ? OptimEngine.MakeDoubleResult(values[0])
                            : OptimEngine.MakeMatrixResult(values);
                        return true;
                    }

                    case "optim_algo_count":
                        if (argc != 0) return false;
                        result = OptimEngine.MakeDoubleResult(OptimEngine.AlgorithmCount);
                        return true;

                    case "optim_solve_expr":
                    {
                        if (argc < 3 || argc > 5) return false;
                        int algo = OptimEngine.ExtractInt(args[0], context);
                        Entry expr = args[1];
                        Entry varsArg = null;
                        Entry xArg;
                        int maxIter = 0;

                        if (argc == 3)
                        {
                            xArg = args[2];
                        }
                        else if (argc == 4)
                        {
                            if (!LooksLikeVariableMatrix(args[2]) &&
                                CanExtractNumeric(args[2], context))
                            {
                                xArg = args[2];
                                maxIter = OptimEngine.ExtractInt(args[3], context);
                            }
                            else
                            {
                                varsArg = args[2];
                                xArg = args[3];
                            }
                        }
                        else
                        {
                            varsArg = args[2];
                            xArg = args[3];
                            maxIter = OptimEngine.ExtractInt(args[4], context);
                        }

                        result = SolveAndPackCustom(algo, expr, varsArg, xArg, maxIter, context);
                        return true;
                    }

                    default:
                    {
                        int algo = AlgorithmIndex(fnName);
                        if (algo < 0) return false;

                        // Global stochastic solvers (de, de_prmm, pso, pso_dv) need
                        // explicit box bounds: (expr, vars, x0, p_min, p_max [, max_iter]).
                        bool globalOnly = algo >= 6 && algo <= 9;
                        if (globalOnly)
                        {
                            if (argc != 5 && argc != 6) return false;
                            Entry gexpr = args[0];
                            string[] gnames = OptimExpression.ParseVariableNames(args[1], context);
                            double[] gx0 = OptimEngine.ExtractDoubleArray(args[2], context);
                            double[] glo = OptimEngine.ExtractDoubleArray(args[3], context);
                            double[] ghi = OptimEngine.ExtractDoubleArray(args[4], context);
                            int gmax = argc == 6 ? OptimEngine.ExtractInt(args[5], context) : 0;
                            result = SolveAndPackCustomBounded(algo, gexpr, gnames, gx0, glo, ghi, gmax, context);
                            return true;
                        }

                        if (argc < 2 || argc > 4) return false;

                        Entry expr = args[0];
                        Entry varsArg = null;
                        Entry xArg;
                        int maxIter = 0;

                        if (argc == 2)
                        {
                            xArg = args[1];
                        }
                        else if (argc == 3)
                        {
                            bool looksVars = LooksLikeVariableMatrix(args[1]);
                            bool arg2IsMatrix = IsMatrixEntry(args[2]);
                            bool numeric = !looksVars && !arg2IsMatrix &&
                                           CanExtractNumeric(args[1], context) &&
                                           IsIntegerScalarEntry(args[2], context);
                            if (numeric)
                            {
                                xArg = args[1];
                                maxIter = OptimEngine.ExtractInt(args[2], context);
                            }
                            else
                            {
                                varsArg = args[1];
                                xArg = args[2];
                            }
                        }
                        else
                        {
                            varsArg = args[1];
                            xArg = args[2];
                            maxIter = OptimEngine.ExtractInt(args[3], context);
                        }

                        result = SolveAndPackCustom(algo, expr, varsArg, xArg, maxIter, context);
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception("Optim evaluation error in " + fnName + ": " + ex.Message, ex);
            }
        }

        private static int AlgorithmIndex(string fnName)
        {
            for (int i = 0; i < AlgorithmFunctions.Length; i++)
                if (string.Equals(AlgorithmFunctions[i], fnName, StringComparison.OrdinalIgnoreCase))
                    return i;
            return -1;
        }

        private static Entry SolveAndPackCustomBounded(
            int algo, Entry expr, string[] names, double[] x0,
            double[] lower, double[] upper, int maxIter, Store context)
        {
            if (x0 == null || x0.Length == 0)
                throw new ArgumentException("x0 must contain at least one value.");

            Entry functionBody;
            string[] functionNames;
            if (OptimExpression.TryResolveFunction(expr, context, out functionBody, out functionNames))
            {
                expr = functionBody;
                names = functionNames;
            }

            if (names.Length != x0.Length)
                throw new ArgumentException(
                    "The variable vector has " + names.Length +
                    " name(s) but x0 has " + x0.Length + " value(s).");

            OptimResult result = OptimEngine.SolveCustom(algo, expr, names, x0, lower, upper, maxIter, context);
            return OptimEngine.MakeMatrixResult(result.ToVector());
        }

        private static Entry SolveAndPackCustom(
            int algo, Entry expr, Entry varsArg, Entry xArg, int maxIter, Store context)
        {
            double[] x0 = OptimEngine.ExtractDoubleArray(xArg, context);
            if (x0.Length == 0)
                throw new ArgumentException("x0 must contain at least one value.");

            string[] names;
            Entry functionBody;
            string[] functionNames;
            if (OptimExpression.TryResolveFunction(expr, context, out functionBody, out functionNames))
            {
                names = functionNames;
                expr = functionBody;
            }
            else if (varsArg == null)
            {
                names = OptimExpression.InferVariableNames(expr, context, x0.Length);
            }
            else
            {
                try
                {
                    names = OptimExpression.ParseVariableNames(varsArg, context);
                }
                catch (ArgumentException)
                {
                    // A plot evaluation may have substituted x/y before this
                    // plugin was called, leaving a numeric matrix where the
                    // variable names used to be. Recover them from the still
                    // symbolic objective when possible.
                    if (!CanExtractNumeric(varsArg, context))
                        throw;
                    names = OptimExpression.InferVariableNames(expr, context, x0.Length);
                }
            }

            if (names.Length != x0.Length)
                throw new ArgumentException(
                    "The variable vector has " + names.Length +
                    " name(s) but x0 has " + x0.Length + " value(s).");

            OptimResult result = OptimEngine.SolveCustom(algo, expr, names, x0, null, null, maxIter, context);
            return OptimEngine.MakeMatrixResult(result.ToVector());
        }

        private static bool IsNumericScalar(Entry entry, Store context)
        {
            double value;
            return TryGetNumericScalar(entry, context, out value);
        }

        private static bool TryGetNumericScalar(Entry entry, Store context, out double value)
        {
            value = 0.0;
            try
            {
                double[] values = OptimEngine.ExtractDoubleArray(entry, context);
                if (values.Length != 1)
                    return false;
                value = values[0];
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsWholeNumber(double value)
        {
            return Math.Abs(value - Math.Round(value)) <= 1e-9;
        }

        private static bool CanExtractNumeric(Entry entry, Store context)
        {
            try
            {
                OptimEngine.ExtractDoubleArray(entry, context);
                return true;
            }
            catch
            {
                return false;
            }
        }

        // A variable vector such as mat(x, y, 2, 1) must not be evaluated
        // numerically while deciding which overload was used: doing so asks
        // SMath for x before the optimiser has supplied a value and can leave
        // the region reporting "x - not defined". Recognise the symbolic
        // matrix structurally instead.
        private static bool LooksLikeVariableMatrix(Entry entry)
        {
            if (entry == null || entry.Items == null || entry.Items.Length < 3 ||
                !OptimExpression.IsMatrixLiteralEntry(entry))
                return false;

            int count = entry.Items.Length;
            int rows, cols;
            if (!TryParseIntEntry(entry.Items[count - 2], out rows) ||
                !TryParseIntEntry(entry.Items[count - 1], out cols) ||
                rows <= 0 || cols <= 0 || rows * cols != count - 2)
                return false;

            for (int i = 0; i < rows * cols; i++)
                if (ContainsSymbolicOperand(entry.Items[i]))
                    return true;

            return false;
        }

        // SMath may substitute defined names (x, y, ...) before a plugin sees
        // an argument. A 3-arg call whose third argument is a matrix is always
        // the (expr, vars, x0) form: the matrix cannot be max_iter. This keeps
        // plot evaluation, where x/y may already be bound, from misreading the
        // call as (expr, x0, max_iter).
        private static bool IsMatrixEntry(Entry entry)
        {
            if (entry == null || entry.Items == null || entry.Items.Length < 3 ||
                !OptimExpression.IsMatrixLiteralEntry(entry))
                return false;

            int count = entry.Items.Length;
            int rows, cols;
            return TryParseIntEntry(entry.Items[count - 2], out rows) &&
                   TryParseIntEntry(entry.Items[count - 1], out cols) &&
                   rows > 0 && cols > 0 && rows * cols == count - 2;
        }

        private static bool IsIntegerScalarEntry(Entry entry, Store context)
        {
            if (entry == null || IsMatrixEntry(entry))
                return false;

            try
            {
                OptimEngine.ExtractInt(entry, context);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool ContainsSymbolicOperand(Entry entry)
        {
            if (entry == null)
                return false;

            if (entry.Type == TermType.Operand &&
                (entry.Items == null || entry.Items.Length == 0))
            {
                string text = entry.Text;
                if (string.IsNullOrEmpty(text))
                    return false;
                if (text[0] == '\'' || text[0] == '"')
                    return false;

                double dummy;
                if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out dummy))
                    return false;
                if (OptimExpression.IsReservedName(text))
                    return false;

                return true;
            }

            if (entry.Items != null)
                for (int i = 0; i < entry.Items.Length; i++)
                    if (ContainsSymbolicOperand(entry.Items[i]))
                        return true;

            return false;
        }

        private static bool TryParseIntEntry(Entry entry, out int value)
        {
            value = 0;
            if (entry == null || entry.Text == null)
                return false;

            double parsed;
            if (!double.TryParse(entry.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
                return false;

            int rounded = (int)Math.Round(parsed);
            if (Math.Abs(parsed - rounded) > 1e-9)
                return false;

            value = rounded;
            return true;
        }

        public bool NumericEvaluation(Term value, TNumber[] args, ref TNumber result)
        {
            // These functions take vectors, so they are handled at the
            // expression level by TryEvaluateExpression, not here.
            return false;
        }

        public void Dispose()
        {
        }
    }
}
