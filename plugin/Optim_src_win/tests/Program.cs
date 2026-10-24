/*
 * OptimWrapper test harness.
 *
 * Exercises everything that can run without a live SMath session:
 *   - native library loading and the P/Invoke chain
 *   - test-function metadata and evaluation
 *   - every deterministic algorithm on a problem it can actually solve
 *   - the stochastic algorithms on problems where their answer is checked
 *     by value rather than by the convergence flag
 *   - argument validation and bitness staging
 *
 * SMath types (Term, TNumber, ArgumentInfo) cannot be constructed outside a
 * live session - they dereference GlobalProfile - so TryEvaluateExpression
 * itself is only reachable from inside SMath. Everything below it is covered
 * here.
 */

using System;
using System.IO;
using System.Runtime.InteropServices;
using OptimWrapper;

namespace OptimHarness
{
    internal static class Program
    {
        private static int _pass;
        private static int _fail;

        private static void Check(string label, bool ok, string detail)
        {
            if (ok) { _pass++; Console.WriteLine("  PASS  " + label + (detail == null ? "" : "  " + detail)); }
            else { _fail++; Console.WriteLine("  FAIL  " + label + (detail == null ? "" : "  " + detail)); }
        }

        private static void Near(string label, double got, double expected, double tol)
        {
            bool ok = Math.Abs(got - expected) <= tol;
            Check(label, ok, string.Format("got {0,16:F9}  expected {1,16:F9}  (tol {2:G3})", got, expected, tol));
        }

        private static void NearX(string label, OptimResult result, double[] expected, double tol)
        {
            bool ok = result.X != null && result.X.Length == expected.Length;
            string detail = "got ";
            if (ok)
            {
                for (int i = 0; i < expected.Length; i++)
                {
                    if (Math.Abs(result.X[i] - expected[i]) > tol) ok = false;
                    if (i > 0) detail += ", ";
                    detail += result.X[i].ToString("F9");
                }
                detail += "  expected ";
                for (int i = 0; i < expected.Length; i++)
                {
                    if (i > 0) detail += ", ";
                    detail += expected[i].ToString("F9");
                }
                detail += string.Format("  (tol {0:G3}, f={1:G6}, iters={2}, ok={3})",
                    tol, result.F, result.Iterations, result.Success);
            }
            Check(label, ok, detail);
        }

        private static OptimResult Run(int algo, int fn, double[] x0, int maxIter)
        {
            return OptimEngine.Solve(algo, fn, x0, maxIter);
        }

        // --- custom-callback ABI probes (no SMath types involved) -----------

        private static double[] CopyPoint(IntPtr xPtr, int n)
        {
            double[] x = new double[n];
            Marshal.Copy(xPtr, x, 0, n);
            return x;
        }

        private static int CustomObjective(IntPtr xPtr, int n, IntPtr outPtr, int outLen, IntPtr user)
        {
            if (n != 2 || outLen != 1) return 1;
            double[] x = CopyPoint(xPtr, n);
            double[] value = new double[] { x[0] * x[0] + 4.0 * x[1] * x[1] };
            Marshal.Copy(value, 0, outPtr, 1);
            return 0;
        }

        private static int CustomConstrained(IntPtr xPtr, int n, IntPtr outPtr, int outLen, IntPtr user)
        {
            if (n != 2 || outLen < 1 || outLen > 2) return 1;
            double[] x = CopyPoint(xPtr, n);
            double[] all = new double[]
            {
                (x[0] - 5.0) * (x[0] - 5.0) + (x[1] - 4.0) * (x[1] - 4.0),
                -2.0 * x[0] - x[1] + 14.0
            };
            double[] value = new double[outLen];
            Array.Copy(all, value, outLen);
            Marshal.Copy(value, 0, outPtr, outLen);
            return 0;
        }

        private static int CustomRoot(IntPtr xPtr, int n, IntPtr outPtr, int outLen, IntPtr user)
        {
            if (n != 2 || outLen != 2) return 1;
            double[] x = CopyPoint(xPtr, n);
            double[] value = new double[]
            {
                x[0] * x[0] + x[1] * x[1] - 1.0,
                x[0] - x[1]
            };
            Marshal.Copy(value, 0, outPtr, 2);
            return 0;
        }

        private static int CustomSumt2(IntPtr xPtr, int n, IntPtr outPtr, int outLen, IntPtr user)
        {
            if (n != 2 || outLen < 1 || outLen > 2) return 1;
            double[] x = CopyPoint(xPtr, n);
            double[] all = new double[]
            {
                x[0] * x[0] + x[1] * x[1],
                1.0 - x[0] - x[1]
            };
            double[] value = new double[outLen];
            Array.Copy(all, value, outLen);
            Marshal.Copy(value, 0, outPtr, outLen);
            return 0;
        }

        private static int CustomRoot4(IntPtr xPtr, int n, IntPtr outPtr, int outLen, IntPtr user)
        {
            if (n != 4 || outLen != 4) return 1;
            double[] x = CopyPoint(xPtr, n);
            double[] value = new double[]
            {
                x[0] * x[0] + x[1] - 2.0,
                x[1] * x[1] + x[2] - 2.0,
                x[2] * x[2] + x[3] - 2.0,
                x[3] * x[3] + x[0] - 2.0
            };
            Marshal.Copy(value, 0, outPtr, 4);
            return 0;
        }

        private static int CustomFailure(IntPtr xPtr, int n, IntPtr outPtr, int outLen, IntPtr user)
        {
            return 1;
        }

        private static int CustomRastrigin(IntPtr xPtr, int n, IntPtr outPtr, int outLen, IntPtr user)
        {
            if (n != 2 || outLen != 1) return 1;
            double[] x = CopyPoint(xPtr, n);
            double value = 20.0 + (x[0] * x[0] - 10.0 * Math.Cos(2.0 * Math.PI * x[0]))
                                + (x[1] * x[1] - 10.0 * Math.Cos(2.0 * Math.PI * x[1]));
            Marshal.Copy(new double[] { value }, 0, outPtr, 1);
            return 0;
        }

        private static void Main()
        {
            Console.WriteLine("=== OptimWrapper test: managed layer + native engine ===");
            Console.WriteLine();

            Console.WriteLine("-- platform detection --");
            Check("reports Windows", NativeLibraryLoader.IsWindows,
                  "native file = " + NativeLibraryLoader.NativeFileName);
            Check("native file name matches platform and bitness",
                  NativeLibraryLoader.IsWindows
                      ? NativeLibraryLoader.NativeFileName == (IntPtr.Size == 8 ? "Optim_x64.dll" : "Optim_x86.dll")
                      : NativeLibraryLoader.NativeFileName == "libOptim.so",
                  NativeLibraryLoader.NativeFileName);
            string enginePath = Path.Combine(OptimEngine.PluginDir, NativeLibraryLoader.NativeFileName);
            Check("engine staged at harness root", File.Exists(enginePath), enginePath);

            Console.WriteLine();
            Console.WriteLine("-- engine metadata --");
            Check("ABI version is 101", OptimEngine.ApiVersion == 101,
                  OptimEngine.ApiVersion.ToString());
            Check("15 test functions", OptimEngine.FunctionCount == 15,
                  OptimEngine.FunctionCount.ToString());
            Check("13 algorithms", OptimEngine.AlgorithmCount == 13,
                  OptimEngine.AlgorithmCount.ToString());

            Check("algorithm 0 is bfgs", OptimEngine.AlgorithmName(0) == "bfgs",
                  OptimEngine.AlgorithmName(0));
            Check("algorithm 12 is broyden_df", OptimEngine.AlgorithmName(12) == "broyden_df",
                  OptimEngine.AlgorithmName(12));
            Check("test function 8 is bukin", OptimEngine.FunctionName(8) == "bukin",
                  OptimEngine.FunctionName(8));
            Check("test function 9 is table", OptimEngine.FunctionName(9) == "table",
                  OptimEngine.FunctionName(9));

            Console.WriteLine();
            Console.WriteLine("-- test-function metadata --");
            Check("quadratic is 2-D", OptimEngine.FunctionDim(0) == 2, null);
            Check("sphere is n-D (dim 0)", OptimEngine.FunctionDim(2) == 0, null);
            Check("quadratic kind is objective", OptimEngine.FunctionKind(0) == 0, null);
            Check("constr_1 kind is constrained", OptimEngine.FunctionKind(10) == 1, null);
            Check("zeros_1 kind is root", OptimEngine.FunctionKind(13) == 2, null);
            Check("bukin is bounded", NativeMethods.TestFnBounded(8) == 1, null);
            Near("bukin lower bound x1", NativeMethods.TestFnLower(8, 0), -15.0, 1e-12);
            Near("bukin upper bound x2", NativeMethods.TestFnUpper(8, 1), 3.0, 1e-12);
            Near("beale lower bound x1", NativeMethods.TestFnLower(3, 0), -4.5, 1e-12);

            Console.WriteLine();
            Console.WriteLine("-- test-function values --");
            Near("quadratic at its minimum", OptimEngine.Evaluate(0, new double[] { 2.25, -4.75 }), -16.375, 1e-12);
            Near("beale at (3, 0.5)", OptimEngine.Evaluate(3, new double[] { 3.0, 0.5 }), 0.0, 1e-12);
            Near("ackley at (0, 0)", OptimEngine.Evaluate(6, new double[] { 0.0, 0.0 }), 0.0, 1e-12);
            Near("levi at (1, 1)", OptimEngine.Evaluate(7, new double[] { 1.0, 1.0 }), 0.0, 1e-12);
            Near("sphere at (1, 1, 1, 1)", OptimEngine.Evaluate(2, new double[] { 1.0, 1.0, 1.0, 1.0 }), 4.0, 1e-12);

            Console.WriteLine();
            Console.WriteLine("-- deterministic algorithms --");

            OptimResult r;

            r = Run(0, 1, new double[] { -1.2, 1.0 }, 2000);
            Check("bfgs reports success", r.Success, null);
            NearX("bfgs solves rosenbrock", r, new double[] { 1.0, 1.0 }, 1e-4);

            r = Run(1, 1, new double[] { 0.0, 0.0 }, 2000);
            Check("lbfgs reports success", r.Success, null);
            NearX("lbfgs solves rosenbrock", r, new double[] { 1.0, 1.0 }, 1e-4);

            r = Run(2, 1, new double[] { -1.2, 1.0 }, 2000);
            Check("cg reports success", r.Success, null);
            NearX("cg solves rosenbrock", r, new double[] { 1.0, 1.0 }, 1e-4);

            r = Run(3, 0, new double[] { 0.0, 0.0 }, 50000);
            Check("gd reports success", r.Success, null);
            NearX("gd solves quadratic", r, new double[] { 2.25, -4.75 }, 1e-4);

            r = Run(4, 0, new double[] { 0.0, 0.0 }, 100);
            Check("newton reports success", r.Success, null);
            NearX("newton solves quadratic", r, new double[] { 2.25, -4.75 }, 1e-6);

            r = Run(5, 0, new double[] { 0.0, 0.0 }, 4000);
            Check("nm reports success", r.Success, null);
            NearX("nm solves quadratic", r, new double[] { 2.25, -4.75 }, 1e-3);

            r = Run(0, 2, new double[] { 1.0, 1.0, 1.0, 1.0 }, 2000);
            Check("bfgs reports success on 4-D sphere", r.Success, null);
            NearX("bfgs solves 4-D sphere", r, new double[] { 0.0, 0.0, 0.0, 0.0 }, 1e-6);

            r = Run(10, 10, new double[] { 1.0, 1.0 }, 2000);
            Check("sumt reports success (constr_1)", r.Success, null);
            NearX("sumt solves constr_1", r, new double[] { 5.0, 4.0 }, 1e-3);

            r = Run(10, 12, new double[] { 1.2, 1.2 }, 2000);
            Check("sumt reports success (constr_3)", r.Success, null);
            NearX("sumt solves constr_3", r, new double[] { 1.0, 1.0 }, 1e-3);

            r = Run(11, 13, new double[] { 0.0, 0.0 }, 2000);
            Check("broyden reports success", r.Success, null);
            NearX("broyden solves zeros_1", r, new double[] { 0.3532, 0.6061 }, 1e-3);
            Near("broyden residual", r.F, 0.0, 1e-9);

            r = Run(12, 14, new double[] { 0.0, 0.0 }, 2000);
            Check("broyden_df reports success", r.Success, null);
            Near("broyden_df residual", r.F, 0.0, 1e-9);

            Console.WriteLine();
            Console.WriteLine("-- stochastic algorithms (answer checked by value) --");

            r = Run(6, 5, new double[] { 0.5, 0.5 }, 2000);
            NearX("de solves rastrigin", r, new double[] { 0.0, 0.0 }, 1e-2);

            r = Run(7, 6, new double[] { 1.0, 1.0 }, 2000);
            NearX("de_prmm solves ackley", r, new double[] { 0.0, 0.0 }, 1e-2);

            r = Run(8, 9, new double[] { 8.0, 9.0 }, 3000);
            Near("pso reaches the table minimum", r.F, -19.2085, 1e-3);

            r = Run(9, 9, new double[] { 8.0, 9.0 }, 3000);
            Near("pso_dv reaches the table minimum", r.F, -19.2085, 1e-2);

            Console.WriteLine();
            Console.WriteLine("-- custom callback ABI --");

            double[] customX = new double[2];
            double customF;
            int customIter;
            double customErr;

            int customRc = NativeMethods.SolveCustom(0, 0, 2, 0,
                new double[] { 1.0, 1.0 }, customX,
                out customF, out customIter, out customErr,
                2000, 42, null, null, 0, CustomObjective);
            Check("custom bfgs returns 0 or 1", customRc == 0 || customRc == 1,
                  "rc=" + customRc + " err=" + NativeMethods.LastError());
            Near("custom bfgs x1", customX[0], 0.0, 1e-4);
            Near("custom bfgs x2", customX[1], 0.0, 1e-4);
            Near("custom bfgs f", customF, 0.0, 1e-9);

            customX = new double[2];
            customRc = NativeMethods.SolveCustom(10, 1, 2, 1,
                new double[] { 1.0, 1.0 }, customX,
                out customF, out customIter, out customErr,
                2000, 42, null, null, 0, CustomConstrained);
            Check("custom sumt returns 0 or 1", customRc == 0 || customRc == 1,
                  "rc=" + customRc + " err=" + NativeMethods.LastError());
            Near("custom sumt x1", customX[0], 5.0, 1e-3);
            Near("custom sumt x2", customX[1], 4.0, 1e-3);

            customX = new double[2];
            customRc = NativeMethods.SolveCustom(10, 1, 2, 1,
                new double[] { 1.0, 1.0 }, customX,
                out customF, out customIter, out customErr,
                2000, 42, null, null, 0, CustomSumt2);
            Check("custom sumt 2x2 returns 0 or 1", customRc == 0 || customRc == 1,
                  "rc=" + customRc + " err=" + NativeMethods.LastError() +
                  " iters=" + customIter);
            Near("custom sumt 2x2 x1", customX[0], 0.5, 1e-3);
            Near("custom sumt 2x2 x2", customX[1], 0.5, 1e-3);
            Near("custom sumt 2x2 f", customF, 0.5, 1e-3);

            customX = new double[2];
            customRc = NativeMethods.SolveCustom(11, 2, 2, 2,
                new double[] { 1.0, 1.0 }, customX,
                out customF, out customIter, out customErr,
                2000, 42, null, null, 0, CustomRoot);
            Check("custom broyden returns 0 or 1", customRc == 0 || customRc == 1,
                  "rc=" + customRc + " err=" + NativeMethods.LastError());
            Near("custom broyden x1", customX[0], 1.0 / Math.Sqrt(2.0), 1e-4);
            Near("custom broyden x2", customX[1], 1.0 / Math.Sqrt(2.0), 1e-4);
            Near("custom broyden residual", customF, 0.0, 1e-8);

            customX = new double[4];
            customRc = NativeMethods.SolveCustom(11, 2, 4, 4,
                new double[] { 0.7, 0.7, 0.7, 0.7 }, customX,
                out customF, out customIter, out customErr,
                2000, 42, null, null, 0, CustomRoot4);
            Check("custom broyden 4x4 returns 0 or 1", customRc == 0 || customRc == 1,
                  "rc=" + customRc + " err=" + NativeMethods.LastError());
            Near("custom broyden 4x4 x1", customX[0], 1.0, 1e-4);
            Near("custom broyden 4x4 x2", customX[1], 1.0, 1e-4);
            Near("custom broyden 4x4 x3", customX[2], 1.0, 1e-4);
            Near("custom broyden 4x4 x4", customX[3], 1.0, 1e-4);
            Near("custom broyden 4x4 residual", customF, 0.0, 1e-8);

            customRc = NativeMethods.SolveCustom(11, 2, 2, 1,
                new double[] { 1.0, 1.0 }, new double[2],
                out customF, out customIter, out customErr,
                10, 42, null, null, 0, CustomRoot);
            Check("custom rectangular root returns 2", customRc == 2,
                  "rc=" + customRc + " err=" + NativeMethods.LastError());

            customRc = NativeMethods.SolveCustom(0, 0, 2, 0,
                new double[] { 1.0, 1.0 }, new double[2],
                out customF, out customIter, out customErr,
                10, 42, null, null, 0, CustomFailure);
            Check("custom callback failure returns 3", customRc == 3,
                  "rc=" + customRc + " err=" + NativeMethods.LastError());

            customRc = NativeMethods.SolveCustom(0, 2, 2, 2,
                new double[] { 1.0, 1.0 }, new double[2],
                out customF, out customIter, out customErr,
                10, 42, null, null, 0, CustomRoot);
            Check("custom kind mismatch returns 2", customRc == 2,
                  "rc=" + customRc + " err=" + NativeMethods.LastError());

            // bounded DE/PSO on a user callback (the expression-form capability)
            customX = new double[2];
            customRc = NativeMethods.SolveCustom(6, 0, 2, 0,
                new double[] { 3.0, 3.0 }, customX,
                out customF, out customIter, out customErr,
                300, 42, new double[] { -5.12, -5.12 }, new double[] { 5.12, 5.12 }, 1, CustomRastrigin);
            Check("custom de bounded returns 0 or 1", customRc == 0 || customRc == 1,
                  "rc=" + customRc + " err=" + NativeMethods.LastError());
            Near("custom de bounded x1", customX[0], 0.0, 1e-1);
            Near("custom de bounded x2", customX[1], 0.0, 1e-1);

            customX = new double[2];
            customRc = NativeMethods.SolveCustom(8, 0, 2, 0,
                new double[] { 3.0, 3.0 }, customX,
                out customF, out customIter, out customErr,
                300, 42, new double[] { -5.12, -5.12 }, new double[] { 5.12, 5.12 }, 1, CustomRastrigin);
            Check("custom pso bounded returns 0 or 1", customRc == 0 || customRc == 1,
                  "rc=" + customRc + " err=" + NativeMethods.LastError());
            Near("custom pso bounded x1", customX[0], 0.0, 1e-1);
            Near("custom pso bounded x2", customX[1], 0.0, 1e-1);

            Console.WriteLine();
            Console.WriteLine("-- error handling --");

            bool threw = false;
            try { OptimEngine.Evaluate(13, new double[] { 0.0, 0.0 }); }
            catch (ArgumentException) { threw = true; }
            Check("evaluating a root system throws ArgumentException", threw, null);

            threw = false;
            try { OptimEngine.Evaluate(0, new double[] { 0.0, 0.0, 0.0 }); }
            catch (ArgumentException) { threw = true; }
            Check("wrong dimension for evaluate throws", threw, null);

            threw = false;
            try { Run(0, 0, new double[] { 0.0 }, 10); }
            catch (InvalidOperationException) { threw = true; }
            Check("wrong x0 length throws InvalidOperationException", threw, null);

            threw = false;
            try { Run(10, 1, new double[] { 0.0, 0.0 }, 10); }
            catch (InvalidOperationException) { threw = true; }
            Check("sumt on an objective function throws", threw, null);

            threw = false;
            try { Run(11, 1, new double[] { 0.0, 0.0 }, 10); }
            catch (InvalidOperationException) { threw = true; }
            Check("broyden on an objective function throws", threw, null);

            threw = false;
            try { Run(0, 0, new double[0], 10); }
            catch (ArgumentException) { threw = true; }
            Check("empty x0 throws ArgumentException", threw, null);

            Console.WriteLine();
            if (_fail == 0)
                Console.WriteLine("=== ALL TESTS PASSED (" + _pass + "/" + _pass + ") ===");
            else
                Console.WriteLine("=== TESTS FAILED (" + _fail + " of " + (_pass + _fail) + ") ===");
            Environment.Exit(_fail == 0 ? 0 : 1);
        }
    }
}
