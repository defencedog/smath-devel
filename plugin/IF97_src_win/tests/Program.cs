// Functional test harness for the IF97 SMath plugin.
//
// The plugin is unit-aware: it accepts SI base units (Pa, J/kg, K) and returns
// SI base-unit values. This harness validates that conversion chain against the
// official IAPWS-IF97 verification tables.
//
// EvaluateRaw() is tested rather than Evaluate(), because attaching a unit
// expression needs a live SMath session (MItem construction dereferences
// GlobalProfile). EvaluateRaw exercises everything except the MItem itself.
using System;
using System.Collections.Generic;
using IF97SMath;

namespace IF97Harness
{
    internal static class Program
    {
        private static int _fail;
        private static Dictionary<string, IF97Function> _fns;

        private static double Call(string name, params double[] args)
        {
            IF97Function f;
            if (!_fns.TryGetValue(name, out f))
            {
                Console.WriteLine("  FAIL  missing function: " + name);
                _fail++;
                return double.NaN;
            }
            if (f.ArgsCount != args.Length)
            {
                Console.WriteLine("  FAIL  {0}: ArgsCount={1}, called with {2}", name, f.ArgsCount, args.Length);
                _fail++;
                return double.NaN;
            }
            return f.EvaluateRaw(args);
        }

        private static void Check(string label, double actual, double expected, double relTol)
        {
            double scale = Math.Max(Math.Abs(expected), 1e-12);
            bool ok = !double.IsNaN(actual) && Math.Abs(actual - expected) <= relTol * scale;
            Console.WriteLine("  {0}  {1,-20} got {2,18:G10}   ref {3,18:G10}",
                ok ? "PASS" : "FAIL", label, actual, expected);
            if (!ok) _fail++;
        }

        private static void CheckAbs(string label, double actual, double expected, double absTol)
        {
            bool ok = !double.IsNaN(actual) && Math.Abs(actual - expected) <= absTol;
            Console.WriteLine("  {0}  {1,-20} got {2,18:G10}   ref {3,18:G10}  (tol {4})",
                ok ? "PASS" : "FAIL", label, actual, expected, absTol);
            if (!ok) _fail++;
        }

        private static void Main()
        {
            Console.WriteLine("=== IF97 plugin test: SI in / SI out ===\n");

            _fns = new Dictionary<string, IF97Function>();
            foreach (IF97Function f in IF97Functions.All)
            {
                if (_fns.ContainsKey(f.Name)) { Console.WriteLine("  FAIL  duplicate: " + f.Name); _fail++; continue; }
                _fns.Add(f.Name, f);
            }

            // ---- signature / unit table --------------------------------------
            string[] expected =
            {
                "IF97_Tsat_p|1|Temperature", "IF97_psat_T|1|Pressure", "IF97_sigma_T|1|SurfaceTension",
                "IF97_rho_Tp|2|Density", "IF97_v_Tp|2|SpecificVolume", "IF97_h_Tp|2|SpecificEnergy",
                "IF97_s_Tp|2|SpecificEntropy", "IF97_u_Tp|2|SpecificEnergy", "IF97_cp_Tp|2|SpecificEntropy",
                "IF97_cv_Tp|2|SpecificEntropy", "IF97_w_Tp|2|Velocity", "IF97_mu_Tp|2|DynamicViscosity",
                "IF97_tc_Tp|2|ThermalConductivity", "IF97_Pr_Tp|2|Dimensionless",
                "IF97_rholiq_p|1|Density", "IF97_hliq_p|1|SpecificEnergy", "IF97_sliq_p|1|SpecificEntropy",
                "IF97_uliq_p|1|SpecificEnergy", "IF97_cpliq_p|1|SpecificEntropy", "IF97_cvliq_p|1|SpecificEntropy",
                "IF97_wliq_p|1|Velocity", "IF97_muliq_p|1|DynamicViscosity", "IF97_tcliq_p|1|ThermalConductivity",
                "IF97_Prliq_p|1|Dimensionless",
                "IF97_rhovap_p|1|Density", "IF97_hvap_p|1|SpecificEnergy", "IF97_svap_p|1|SpecificEntropy",
                "IF97_uvap_p|1|SpecificEnergy", "IF97_cpvap_p|1|SpecificEntropy", "IF97_cvvap_p|1|SpecificEntropy",
                "IF97_wvap_p|1|Velocity", "IF97_muvap_p|1|DynamicViscosity", "IF97_tcvap_p|1|ThermalConductivity",
                "IF97_Prvap_p|1|Dimensionless",
                "IF97_hfg_p|1|SpecificEnergy", "IF97_hfg_T|1|SpecificEnergy",
                "IF97_hliq_T|1|SpecificEnergy", "IF97_hvap_T|1|SpecificEnergy",
                "IF97_T_ph|2|Temperature", "IF97_rho_ph|2|Density", "IF97_T_ps|2|Temperature",
                "IF97_rho_ps|2|Density", "IF97_p_hs|2|Pressure", "IF97_T_hs|2|Temperature",
                "IF97_Region_ph|2|Dimensionless", "IF97_Region_ps|2|Dimensionless",
            };

            Console.WriteLine("Functions registered: {0} (expected {1})", _fns.Count, expected.Length);
            if (_fns.Count != expected.Length) _fail++;

            Console.WriteLine("\n-- declared arity and output quantity --");
            int sigErrors = 0;
            foreach (string row in expected)
            {
                string[] parts = row.Split('|');
                string name = parts[0];
                int argc = int.Parse(parts[1]);
                Quantity qty = (Quantity)Enum.Parse(typeof(Quantity), parts[2]);

                IF97Function f;
                if (!_fns.TryGetValue(name, out f))
                {
                    Console.WriteLine("  FAIL  missing function: " + name);
                    sigErrors++;
                    continue;
                }
                if (f.ArgsCount != argc || f.Output != qty)
                {
                    Console.WriteLine("  FAIL  {0}: argc={1}/{2}  qty={3}/{4}", name, f.ArgsCount, argc, f.Output, qty);
                    sigErrors++;
                }
            }
            if (sigErrors == 0) Console.WriteLine("  PASS  all {0} functions match the expected arity and quantity", expected.Length);
            _fail += sigErrors;

            const double TOL = 1e-7;

            // ---- Region 1: T = 300 K, p = 3 MPa = 3e6 Pa --------------------
            Console.WriteLine("\n-- Region 1: T=300 K, p=3 MPa (3e6 Pa) --");
            Check("v [m3/kg]", Call("IF97_v_Tp", 300.0, 3e6), 0.100215168e-2, TOL);
            Check("h [J/kg]", Call("IF97_h_Tp", 300.0, 3e6), 0.115331273e6, TOL);
            Check("u [J/kg]", Call("IF97_u_Tp", 300.0, 3e6), 0.112324818e6, TOL);
            Check("s [J/kgK]", Call("IF97_s_Tp", 300.0, 3e6), 0.392294792e3, TOL);
            Check("cp [J/kgK]", Call("IF97_cp_Tp", 300.0, 3e6), 0.417301218e4, TOL);
            Check("w [m/s]", Call("IF97_w_Tp", 300.0, 3e6), 0.150773921e4, TOL);

            // ---- Region 2: T = 700 K, p = 30 MPa = 3e7 Pa -------------------
            Console.WriteLine("\n-- Region 2: T=700 K, p=30 MPa (3e7 Pa) --");
            Check("v [m3/kg]", Call("IF97_v_Tp", 700.0, 3e7), 0.542946619e-2, TOL);
            Check("h [J/kg]", Call("IF97_h_Tp", 700.0, 3e7), 0.263149474e7, TOL);
            Check("u [J/kg]", Call("IF97_u_Tp", 700.0, 3e7), 0.246861076e7, TOL);
            Check("s [J/kgK]", Call("IF97_s_Tp", 700.0, 3e7), 0.517540298e4, TOL);
            Check("cp [J/kgK]", Call("IF97_cp_Tp", 700.0, 3e7), 0.103505092e5, TOL);
            Check("w [m/s]", Call("IF97_w_Tp", 700.0, 3e7), 0.480386523e3, TOL);

            // ---- Region 5: T = 1500 K, p = 0.5 MPa = 5e5 Pa -----------------
            Console.WriteLine("\n-- Region 5: T=1500 K, p=0.5 MPa (5e5 Pa) --");
            Check("v [m3/kg]", Call("IF97_v_Tp", 1500.0, 5e5), 0.138455090e1, TOL);
            Check("h [J/kg]", Call("IF97_h_Tp", 1500.0, 5e5), 0.521976855e7, TOL);
            Check("u [J/kg]", Call("IF97_u_Tp", 1500.0, 5e5), 0.452749310e7, TOL);
            Check("s [J/kgK]", Call("IF97_s_Tp", 1500.0, 5e5), 0.965408875e4, TOL);
            Check("cp [J/kgK]", Call("IF97_cp_Tp", 1500.0, 5e5), 0.261609445e4, TOL);
            Check("w [m/s]", Call("IF97_w_Tp", 1500.0, 5e5), 0.917068690e3, TOL);

            // ---- transport properties: the easy place to lose a 1e-6 --------
            // The library returns Pa*s, W/(m*K) and N/m already, so these also
            // prove the plugin applies no spurious scaling to them.
            Console.WriteLine("\n-- transport properties at 300 K, 3 MPa --");
            double mu = Call("IF97_mu_Tp", 300.0, 3e6);
            double tc = Call("IF97_tc_Tp", 300.0, 3e6);
            double cp = Call("IF97_cp_Tp", 300.0, 3e6);
            double pr = Call("IF97_Pr_Tp", 300.0, 3e6);
            Check("mu [Pa*s] (not uPa*s)", mu, 8.536e-4, 0.01);     // IAPWS R12-08
            Check("tc [W/(m*K)]", tc, 0.6114, 0.01);                // IAPWS R15-11
            Check("sigma(300 K) [N/m]", Call("IF97_sigma_T", 300.0), 0.0716, 0.01);
            Check("Pr == mu*cp/tc", pr, mu * cp / tc, 1e-9);        // dimensionless

            // ---- Region 4: saturation curve, pressure returned in Pa ---------
            Console.WriteLine("\n-- Region 4: saturation curve (Pa) --");
            Check("psat(300 K) [Pa]", Call("IF97_psat_T", 300.0), 0.353658941e4, TOL);
            Check("psat(500 K) [Pa]", Call("IF97_psat_T", 500.0), 0.263889776e7, TOL);
            Check("psat(600 K) [Pa]", Call("IF97_psat_T", 600.0), 0.123443146e8, TOL);
            Check("Tsat(1 bar) [K]", Call("IF97_Tsat_p", 1e5), 0.372755919e3, TOL);
            Check("Tsat(1 MPa) [K]", Call("IF97_Tsat_p", 1e6), 0.453035632e3, TOL);
            Check("Tsat(10 MPa) [K]", Call("IF97_Tsat_p", 1e7), 0.584149488e3, TOL);

            Console.WriteLine("\n-- round trip T -> psat -> Tsat --");
            double ps = Call("IF97_psat_T", 450.0);
            Check("Tsat(psat(450 K)) [K]", Call("IF97_Tsat_p", ps), 450.0, 1e-6);

            // ---- backward equations (spec-allowed 25 mK) ---------------------
            Console.WriteLine("\n-- backward equations --");
            CheckAbs("T_ph(3 MPa, 115.331 kJ/kg)", Call("IF97_T_ph", 3e6, 115331.273), 300.0, 0.025);
            CheckAbs("T_ps(3 MPa, 0.392294792)", Call("IF97_T_ps", 3e6, 392.294792), 300.0, 0.025);

            // ---- behaviour ---------------------------------------------------
            Console.WriteLine("\n-- behaviour --");
            double hfg300 = Call("IF97_hfg_T", 300.0);
            double hfg600 = Call("IF97_hfg_T", 600.0);
            bool ok = hfg300 > 0 && hfg600 > 0 && hfg600 < hfg300;
            Console.WriteLine("  {0}  hfg(300K)={1:F0}  hfg(600K)={2:F0} J/kg", ok ? "PASS" : "FAIL", hfg300, hfg600);
            if (!ok) _fail++;

            double bad = Call("IF97_Tsat_p", 50e6);   // 50 MPa > Pcrit
            bool nan = double.IsNaN(bad);
            Console.WriteLine("  {0}  Tsat(50 MPa) -> {1}", nan ? "PASS" : "FAIL", bad);
            if (!nan) _fail++;

            Console.WriteLine("\n=== {0} ===", _fail == 0 ? "ALL TESTS PASSED" : _fail + " FAILURE(S)");
            Environment.Exit(_fail == 0 ? 0 : 1);
        }
    }
}
