// Functional test harness for the PsychroLib SMath plugin.
//
// Targets .NET 2.0, the same framework the plugin is built for.
//
// IMPORTANT: SMath.Manager cannot construct TNumber / Term / TermInfo /
// ArgumentInfo outside a live SMath session (they dereference GlobalProfile
// state that only the host application initialises). So this harness cannot
// call NumericEvaluation directly. Instead it verifies:
//   1. all 40 function objects exist, with unique well-formed term names
//   2. every function declares the expected argument count, argument unit
//      kinds and output quantity (SMath dispatches on name + argument count,
//      and the unit kinds are what make the plugin unit-aware)
//   3. the SI conversion chain plus the psychrometric core produce correct
//      ASHRAE reference values, driving EvaluateSi (kelvin in, kelvin out)
using System;
using System.Collections.Generic;
using System.Reflection;
using PsychroLibSMath;

namespace Harness
{
    internal static class Program
    {
        private static int _fail;
        private static Dictionary<string, PsychroFunctionBase> _fns;

        private static void Check(string label, double actual, double expected, double tol)
        {
            bool ok = Math.Abs(actual - expected) <= tol;
            Console.WriteLine("  {0}  {1,-26} got {2,14:F6}   expected {3,12:F6}",
                ok ? "PASS" : "FAIL", label, actual, expected);
            if (!ok) _fail++;
        }

        /// <summary>Evaluates a function through the SI boundary (kelvin, pascal, ...).</summary>
        // NOTE: every argument array below is filled element by element on
        // purpose. Roslyn materialises multi-element primitive array
        // initialisers (and params arrays) through RuntimeHelpers.InitializeArray
        // with an RVA blob; in this net20-targeted build that blob can be read
        // with the wrong values (298.15 arriving as 302.062255859375), which
        // silently corrupts the test inputs. Explicit assignment is reliable.
        private static double Si(string name, double[] args)
        {
            return _fns[name].EvaluateSi(args);
        }

        private static double Si1(string name, double a)
        {
            double[] args = new double[1];
            args[0] = a;
            return Si(name, args);
        }

        private static double Si2(string name, double a, double b)
        {
            double[] args = new double[2];
            args[0] = a;
            args[1] = b;
            return Si(name, args);
        }

        private static double Si3(string name, double a, double b, double c)
        {
            double[] args = new double[3];
            args[0] = a;
            args[1] = b;
            args[2] = c;
            return Si(name, args);
        }

        private static void Main()
        {
            Console.WriteLine("=== PsychroLib SMath plugin functional test ===\n");

            // ---- 1. function inventory ---------------------------------------
            _fns = new Dictionary<string, PsychroFunctionBase>();
            foreach (FieldInfo f in typeof(PsychroFunctions).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                var fn = f.GetValue(null) as PsychroFunctionBase;
                if (fn == null) continue;
                if (_fns.ContainsKey(fn.Name))
                {
                    Console.WriteLine("  FAIL  duplicate function name: " + fn.Name);
                    _fail++;
                    continue;
                }
                _fns.Add(fn.Name, fn);
            }

            Console.WriteLine("Functions registered: {0} (expected 40)", _fns.Count);
            if (_fns.Count != 40) _fail++;

            int badNames = 0;
            foreach (string n in _fns.Keys)
                if (!n.StartsWith("Psy_") || n.IndexOf(' ') >= 0) { Console.WriteLine("  FAIL  bad term name: " + n); badNames++; }
            if (badNames == 0) Console.WriteLine("  PASS  all term names are Psy_* identifiers");
            _fail += badNames;

            // ---- 2. declared contract: arity, argument kinds, output quantity --
            // "T" = temperature (kelvin at the boundary, Celsius inside),
            // "P" = plain SI value passed straight through.
            var contract = new Dictionary<string, string>
            {
                {"Psy_CelsiusToKelvin",    "1|P|Temperature"},
                {"Psy_KelvinToCelsius",    "1|P|Dimensionless"},
                {"Psy_HumRatio_RH",        "3|TPP|Dimensionless"},
                {"Psy_HumRatio_TWetBulb",  "3|TTP|Dimensionless"},
                {"Psy_HumRatio_TDewPoint", "2|TP|Dimensionless"},
                {"Psy_HumRatio_VapPres",   "2|PP|Dimensionless"},
                {"Psy_HumRatio_SpecHum",   "1|P|Dimensionless"},
                {"Psy_HumRatio_Enthalpy",  "2|PT|Dimensionless"},
                {"Psy_TDewPoint_RH",       "2|TP|Temperature"},
                {"Psy_TDewPoint_TWetBulb", "3|TTP|Temperature"},
                {"Psy_TDewPoint_VapPres",  "2|TP|Temperature"},
                {"Psy_TDewPoint_HumRatio", "3|TPP|Temperature"},
                {"Psy_TWetBulb_TDewPoint", "3|TTP|Temperature"},
                {"Psy_TWetBulb_RH",        "3|TPP|Temperature"},
                {"Psy_TWetBulb_HumRatio",  "3|TPP|Temperature"},
                {"Psy_RH_TDewPoint",       "2|TT|Dimensionless"},
                {"Psy_RH_TWetBulb",        "3|TTP|Dimensionless"},
                {"Psy_RH_VapPres",         "2|TP|Dimensionless"},
                {"Psy_RH_HumRatio",        "3|TPP|Dimensionless"},
                {"Psy_VapPres_RH",         "2|TP|Pressure"},
                {"Psy_VapPres_TDewPoint",  "1|T|Pressure"},
                {"Psy_VapPres_HumRatio",   "2|PP|Pressure"},
                {"Psy_SpecHum",            "1|P|Dimensionless"},
                {"Psy_Enthalpy",           "2|TP|SpecificEnergy"},
                {"Psy_DryAirEnthalpy",     "1|T|SpecificEnergy"},
                {"Psy_TDryBulb_Enthalpy",  "2|PP|Temperature"},
                {"Psy_AirDensity",         "3|TPP|Density"},
                {"Psy_AirVolume",          "3|TPP|SpecificVolume"},
                {"Psy_DryAirDensity",      "2|TP|Density"},
                {"Psy_DryAirVolume",       "2|TP|SpecificVolume"},
                {"Psy_TDryBulb_Volume",    "3|PPP|Temperature"},
                {"Psy_SatVapPres",         "1|T|Pressure"},
                {"Psy_SatHumRatio",        "2|TP|Dimensionless"},
                {"Psy_SatEnthalpy",        "2|TP|SpecificEnergy"},
                {"Psy_VPD",                "3|TPP|Pressure"},
                {"Psy_DegSaturation",      "3|TPP|Dimensionless"},
                {"Psy_AtmPressure",        "1|P|Pressure"},
                {"Psy_AtmTemperature",     "1|P|Temperature"},
                {"Psy_SeaLevelPressure",   "3|PPT|Pressure"},
                {"Psy_StationPressure",    "3|PPT|Pressure"},
            };

            Console.WriteLine("\n-- declared contract (arity, argument kinds, output) --");
            int contractErrors = 0;
            foreach (KeyValuePair<string, string> kv in contract)
            {
                PsychroFunctionBase fn;
                if (!_fns.TryGetValue(kv.Key, out fn))
                {
                    Console.WriteLine("  FAIL  missing function: " + kv.Key);
                    contractErrors++;
                    continue;
                }

                string[] parts = kv.Value.Split('|');
                int argc = int.Parse(parts[0]);
                string kinds = parts[1];
                Quantity output = (Quantity)Enum.Parse(typeof(Quantity), parts[2], false);

                if (fn.ArgsCount != argc)
                {
                    Console.WriteLine("  FAIL  {0}: ArgsCount={1}, expected {2}", kv.Key, fn.ArgsCount, argc);
                    contractErrors++;
                    continue;
                }
                if (fn.ArgKinds.Length != kinds.Length)
                {
                    Console.WriteLine("  FAIL  {0}: {1} argument kinds for {2} args", kv.Key, fn.ArgKinds.Length, argc);
                    contractErrors++;
                    continue;
                }
                for (int i = 0; i < kinds.Length; i++)
                {
                    ArgKind want = kinds[i] == 'T' ? ArgKind.Temperature : ArgKind.Plain;
                    if (fn.ArgKinds[i] != want)
                    {
                        Console.WriteLine("  FAIL  {0}: argument {1} is {2}, expected {3}",
                            kv.Key, i + 1, fn.ArgKinds[i], want);
                        contractErrors++;
                    }
                }
                if (fn.Output != output)
                {
                    Console.WriteLine("  FAIL  {0}: output is {1}, expected {2}", kv.Key, fn.Output, output);
                    contractErrors++;
                }
            }
            if (contractErrors == 0)
                Console.WriteLine("  PASS  all 40 functions match the expected arity, argument kinds and quantity");
            _fail += contractErrors;

            int extra = 0;
            foreach (string n in _fns.Keys)
                if (!contract.ContainsKey(n)) { Console.WriteLine("  FAIL  unexpected function: " + n); extra++; }
            _fail += extra;

            // ---- 3. SI conversion chain --------------------------------------
            // The worksheet supplies kelvin; PsychroLib wants Celsius. 25 C is
            // 298.15 K, so these two must agree exactly.
            Console.WriteLine("\n-- kelvin boundary -> Celsius core --");
            Check("Enthalpy(298.15 K)", Si2("Psy_Enthalpy", 298.15, 0.00988),
                  Psy.SI.GetMoistAirEnthalpy(25.0, 0.00988), 1e-9);
            Check("HumRatio(298.15 K)", Si3("Psy_HumRatio_RH", 298.15, 0.5, 101325.0),
                  Psy.SI.GetHumRatioFromRelHum(25.0, 0.5, 101325.0), 1e-9);
            Check("AtmTemperature(0 m) K", Si1("Psy_AtmTemperature", 0.0), 288.15, 1e-9);


            // ---- 4. psychrometric core at the ASHRAE reference point ---------
            // 25 C = 298.15 K, 50 % RH, 101325 Pa. Results in SI base units.
            Console.WriteLine("\n-- ASHRAE reference point: 298.15 K (25 C), 50% RH, 101325 Pa --");
            const double tK = 298.15, rh = 0.5, p = 101325.0;
            const double dewK = 13.87 + 273.15;
            const double wetK = 17.89 + 273.15;

            double w = Si3("Psy_HumRatio_RH", tK, rh, p);
            Check("Psy_HumRatio_RH", w, 0.00988, 0.00005);
            Check("Psy_TDewPoint_RH [K]", Si2("Psy_TDewPoint_RH", tK, rh), dewK, 0.05);
            Check("Psy_TWetBulb_RH [K]", Si3("Psy_TWetBulb_RH", tK, rh, p), wetK, 0.05);
            Check("Psy_VapPres_RH [Pa]", Si2("Psy_VapPres_RH", tK, rh), 1583.7, 1.0);
            Check("Psy_Enthalpy [J/kg]", Si2("Psy_Enthalpy", tK, w), 50300.0, 150.0);
            // PsychroLib defines moist-air density as (1 + W) / v, i.e. total
            // mass of dry air + water vapour per unit volume, so the value is
            // slightly higher than the dry-air density at the same conditions.
            Check("Psy_AirDensity [kg/m3]", Si3("Psy_AirDensity", tK, w, p), 1.1770, 0.005);
            Check("Psy_SatVapPres [Pa]", Si1("Psy_SatVapPres", tK), 3169.7, 2.0);
            Check("Psy_AtmPressure [Pa]", Si1("Psy_AtmPressure", 0.0), 101325.0, 0.5);

            // ---- 5. round trips through the SI boundary ----------------------
            Console.WriteLine("\n-- round trips --");
            double dew = Si2("Psy_TDewPoint_RH", tK, rh);
            Check("Psy_RH_TDewPoint", Si2("Psy_RH_TDewPoint", tK, dew), rh, 1e-6);
            double wet = Si3("Psy_TWetBulb_RH", tK, rh, p);
            Check("Psy_RH_TWetBulb", Si3("Psy_RH_TWetBulb", tK, wet, p), rh, 1e-5);
            Check("Psy_HumRatio_TDewPoint", Si2("Psy_HumRatio_TDewPoint", dew, p), w, 1e-6);
            Check("Psy_VapPres_HumRatio", Si2("Psy_VapPres_HumRatio", w, p), 1583.7, 1.0);
            Check("Psy_SpecHum", Si1("Psy_SpecHum", w), w / (1.0 + w), 1e-12);
            Check("Psy_TDryBulb_Enthalpy [K]", Si2("Psy_TDryBulb_Enthalpy", Si2("Psy_Enthalpy", tK, w), w), tK, 0.02);
            Check("Psy_TDryBulb_Volume [K]", Si3("Psy_TDryBulb_Volume", Si3("Psy_AirVolume", tK, w, p), w, p), tK, 0.02);

            // ---- 6. legacy scalar converters ---------------------------------
            Console.WriteLine("\n-- scalar temperature converters --");
            Check("Psy_CelsiusToKelvin(0)", Si1("Psy_CelsiusToKelvin", 0.0), 273.15, 0.001);
            Check("Psy_KelvinToCelsius(300)", Si1("Psy_KelvinToCelsius", 300.0), 26.85, 0.001);

            // ---- 7. behaviour -------------------------------------------------
            Console.WriteLine("\n-- behaviour --");
            double h1 = Si2("Psy_Enthalpy", 293.15, 0.010);   // 20 C
            double h2 = Si2("Psy_Enthalpy", 303.15, 0.010);   // 30 C
            bool rising = h2 > h1;
            Console.WriteLine("  {0}  enthalpy rises 293.15 K -> 303.15 K: {1:F0} -> {2:F0} J/kg",
                rising ? "PASS" : "FAIL", h1, h2);
            if (!rising) _fail++;

            // Out-of-range input must yield NaN, never escape as an exception.
            double bad = Si1("Psy_SatVapPres", 1e6);          // far above the valid range
            bool contained = double.IsNaN(bad);
            Console.WriteLine("  {0}  Psy_SatVapPres(1e6 K) -> {1}",
                contained ? "PASS" : "FAIL", contained ? "NaN" : bad.ToString());
            if (!contained) _fail++;

            Console.WriteLine("\n=== {0} ===", _fail == 0 ? "ALL TESTS PASSED" : _fail + " FAILURE(S)");
            Environment.Exit(_fail == 0 ? 0 : 1);
        }
    }
}
