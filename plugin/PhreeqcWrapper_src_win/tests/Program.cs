/*
 * PhreeqcWrapper test harness.
 *
 * Exercises everything that can run without a live SMath session:
 *   - native library loading and the P/Invoke chain
 *   - database resolution by bare name
 *   - script sanitisation (SMath rational fractions)
 *   - SMath string escaping
 *   - real PHREEQC results against known values
 *
 * SMath types (Term, TNumber, ArgumentInfo) cannot be constructed outside a
 * live session — they dereference GlobalProfile — so TryEvaluateExpression
 * itself is only reachable from inside SMath. Everything below it is covered
 * here.
 */

using System;
using System.IO;
using PhreeqcWrapper;

namespace PhreeqcHarness
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

        private static void Main()
        {
            Console.WriteLine("=== PhreeqcWrapper test: managed layer + native engine ===");
            Console.WriteLine();

            Console.WriteLine("-- platform detection --");
            Check("reports Windows", NativeLibraryLoader.IsWindows,
                  "native file = " + NativeLibraryLoader.NativeFileName);
            Check("native file name matches platform and bitness",
                  NativeLibraryLoader.IsWindows
                      ? NativeLibraryLoader.NativeFileName == (IntPtr.Size == 8 ? "IPhreeqc_x64.dll" : "IPhreeqc_x86.dll")
                      : NativeLibraryLoader.NativeFileName == "libIPhreeqc.so",
                  NativeLibraryLoader.NativeFileName);
            string enginePath = Path.Combine(PhreeqcEngine.PluginDir, NativeLibraryLoader.NativeFileName);
            Check("engine staged at harness root", File.Exists(enginePath), enginePath);

            Console.WriteLine();
            Console.WriteLine("-- database resolution --");
            string pluginDir = PhreeqcEngine.PluginDir;
            Console.WriteLine("  plugin dir: " + pluginDir);

            string dbDefault = PhreeqcEngine.ResolveDatabasePath(null);
            Check("default resolves to phreeqc.dat", dbDefault.EndsWith("phreeqc.dat"), dbDefault);
            Check("default file exists", File.Exists(dbDefault), null);

            string dbPitzer = PhreeqcEngine.ResolveDatabasePath("pitzer.dat");
            Check("bare name resolves inside plugin dir",
                  File.Exists(dbPitzer) && dbPitzer.StartsWith(pluginDir, StringComparison.OrdinalIgnoreCase),
                  dbPitzer);

            bool threw = false;
            try { PhreeqcEngine.ResolveDatabasePath("no_such_database.dat"); }
            catch (FileNotFoundException) { threw = true; }
            Check("missing database throws FileNotFoundException", threw, null);

            Console.WriteLine();
            Console.WriteLine("-- script sanitisation --");
            string frac = PhreeqcEngine.SanitizeScript("SOLUTION 1\n    temp 358293780948959/100000000000000\nEND");
            Check("rational fraction collapsed",
                  frac.IndexOf('/') < 0 && frac.IndexOf("3.5829378") > 0,
                  frac.Replace("\n", " | "));
            string keepUnits = PhreeqcEngine.SanitizeScript("    units     mol/kgw");
            Check("mol/kgw keyword untouched", keepUnits.IndexOf("mol/kgw") > 0, keepUnits.Trim());

            Console.WriteLine();
            Console.WriteLine("-- SMath string escaping --");
            Check("\\0041 unescaped to A", PhreeqcEngine.UnescapeMathString("\\0041") == "A",
                  PhreeqcEngine.UnescapeMathString("\\0041"));
            Check("\\n unescaped to newline", PhreeqcEngine.UnescapeMathString("a\\nb") == "a\nb", null);

            Console.WriteLine();
            Console.WriteLine("-- live engine: Ca-Na-Cl-HCO3 solution, 25 C, pH 7 --");

            const string script =
                "SOLUTION 1\n" +
                "    temp      25\n" +
                "    pH        7.0\n" +
                "    pe        4.0\n" +
                "    units     mmol/kgw\n" +
                "    Ca        1.0\n" +
                "    C         1.0 as HCO3\n" +
                "    Na        1.0\n" +
                "    Cl        1.0\n" +
                "END\n";

            double ph = PhreeqcEngine.RunPhreeqcQuery(script, null, delegate(int id, int sol) { return NativeMethods.GetPH(id, sol); });
            Near("pH", ph, 7.0, 1e-9);

            double sc = PhreeqcEngine.RunPhreeqcQuery(script, null, delegate(int id, int sol) { return NativeMethods.GetSC(id, sol); });
            Near("SC [uS/cm]", sc, 267.5391, 0.01);

            double rho = PhreeqcEngine.RunPhreeqcQuery(script, null, delegate(int id, int sol) { return NativeMethods.GetDensity(id, sol); });
            Near("density [kg/L]", rho, 0.997174, 1e-6);

            double mu = PhreeqcEngine.RunPhreeqcQuery(script, null, delegate(int id, int sol) { return NativeMethods.GetMu(id, sol); });
            Near("ionic strength [mol/kgw]", mu, 0.003407, 1e-6);

            double temp = PhreeqcEngine.RunPhreeqcQuery(script, null, delegate(int id, int sol) { return NativeMethods.GetTemperature(id, sol); });
            Near("temperature [C]", temp, 25.0, 1e-9);

            double mass = PhreeqcEngine.RunPhreeqcQuery(script, null, delegate(int id, int sol) { return NativeMethods.GetMass(id, sol); });
            Near("water mass [kgw]", mass, 1.0, 1e-9);

            double si = PhreeqcEngine.RunPhreeqcQuery(script, null, "Calcite",
                delegate(int id, int sol, string phase) { return NativeMethods.GetSI(id, sol, phase); });
            Near("SI Calcite", si, -1.070219, 1e-5);

            double mol = PhreeqcEngine.RunPhreeqcQuery(script, null, "Ca+2",
                delegate(int id, int sol, string sp) { return NativeMethods.GetMolality(id, sol, sp); });
            Near("molality Ca+2 [mol/kgw]", mol, 0.000997062, 1e-8);

            double act = PhreeqcEngine.RunPhreeqcQuery(script, null, "Ca+2",
                delegate(int id, int sol, string sp) { return NativeMethods.GetActivity(id, sol, sp); });
            Near("activity Ca+2", act, 0.000777354, 1e-8);

            double tot = PhreeqcEngine.RunPhreeqcQuery(script, null, "Ca",
                delegate(int id, int sol, string el) { return NativeMethods.GetTotal(id, sol, el); });
            Near("total Ca [mol/kgw]", tot, 0.001, 1e-9);

            // Activity coefficient should be ~0.78 for a divalent ion at I=0.0034.
            Near("gamma Ca+2 = a/m", act / mol, 0.7796, 2e-3);

            Console.WriteLine();
            Console.WriteLine("-- saturation ratio is 10^SI --");
            double sr = Math.Pow(10.0, si);
            Near("SR Calcite", sr, 0.08505, 1e-4);

            Console.WriteLine();
            Console.WriteLine("-- explicit database argument --");
            double phPitzer = PhreeqcEngine.RunPhreeqcQuery(script, "pitzer.dat",
                delegate(int id, int sol) { return NativeMethods.GetPH(id, sol); });
            Near("pH with pitzer.dat", phPitzer, 7.0, 1e-9);

            Console.WriteLine();
            Console.WriteLine("-- last SOLUTION block wins --");
            const string twoSolutions =
                "SOLUTION 1\n    temp 25\n    pH 7\n    units mmol/kgw\n    Ca 1\n    Cl 2\nEND\n" +
                "SOLUTION 2\n    temp 25\n    pH 4\n    units mmol/kgw\n    Ca 1\n    Cl 2\nEND\n";
            double ph2 = PhreeqcEngine.RunPhreeqcQuery(twoSolutions, null,
                delegate(int id, int sol) { return NativeMethods.GetPH(id, sol); });
            Near("pH of solution 2", ph2, 4.0, 1e-9);

            Console.WriteLine();
            Console.WriteLine("-- repeated calls stay isolated --");
            for (int i = 0; i < 3; i++)
            {
                double again = PhreeqcEngine.RunPhreeqcQuery(script, null,
                    delegate(int id, int sol) { return NativeMethods.GetPH(id, sol); });
                Near("repeat " + (i + 1) + " pH", again, 7.0, 1e-9);
            }

            Console.WriteLine();
            Console.WriteLine("=== {0} ===", _fail == 0 ? "ALL TESTS PASSED" : _fail + " FAILURE(S)");
            Environment.ExitCode = _fail == 0 ? 0 : 1;
        }
    }
}
