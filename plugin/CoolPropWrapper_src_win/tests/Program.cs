/*
 * CoolPropWrapper test harness.
 *
 * Exercises everything that can run without a live SMath session:
 *   - platform detection and the explicit engine load
 *   - all 11 P/Invoke entry points, against published CoolProp reference values
 *   - the pure-string helpers used to transport SMath arguments (UnescapeMathString)
 *   - the unit table: which base-unit expression each parameter name maps to
 *   - registration: every IFunction class is discoverable and reports the right
 *     arity and signature
 *
 * SMath types (TNumber, Entry, TermInfo) cannot be constructed outside a live
 * session - they dereference session state - so TryEvaluateExpression itself is
 * only reachable from inside SMath. Everything below it is covered here.
 */

using System;
using System.IO;
using System.Reflection;
using CoolPropWrapper;
using CoolPropWrapper.Functions;
using SMath.Manager;

namespace CoolPropHHarness
{
    internal static class Program
    {
        private static int _pass;
        private static int _fail;
        private static int _skip;

        /// <summary>
        /// Func&lt;&gt; lives in System.Core (.NET 3.5) and this assembly targets
        /// .NET 2.0, so a check that may need to be retried outside a session
        /// gets its own delegate type.
        /// </summary>
        private delegate bool BoolTest();

        private static void Check(string label, bool ok, string detail)
        {
            if (ok) { _pass++; Console.WriteLine("  PASS  " + label + (detail == null ? "" : "  " + detail)); }
            else { _fail++; Console.WriteLine("  FAIL  " + label + (detail == null ? "" : "  " + detail)); }
        }

        /// <summary>
        /// A check that may legitimately need a live SMath session. Records a
        /// SKIP instead of a FAIL when the SMath layer refuses to cooperate, so
        /// that a harness run outside SMath does not report false breakage.
        /// </summary>
        private static void CheckMaybe(string label, BoolTest test, string detail)
        {
            try { Check(label, test(), detail); }
            catch (Exception ex)
            {
                _skip++;
                Console.WriteLine("  SKIP  " + label + "  (" + ex.GetType().Name + ": " + ex.Message + ")");
            }
        }

        private static void Near(string label, double got, double expected, double tol)
        {
            bool ok = Math.Abs(got - expected) <= tol;
            Check(label, ok, string.Format("got {0,18:F6}  expected {1,14:F6}  (tol {2:G3})", got, expected, tol));
        }

        private static void Eq(string label, string got, string expected)
        {
            bool ok = got == expected;
            Check(label, ok, "got \"" + got + "\"  expected \"" + expected + "\"");
        }

        /// <summary>
        /// True if any unit expression in either table names something other
        /// than a base unit. A derived name (J, Pa, W, N) or an unreduced
        /// compound is exactly what makes a result dimensionally unequal to a
        /// native worksheet expression - SMath's Error 74.
        /// </summary>
        private static readonly string[] BaseUnitTokens = new[] { "kg", "m", "s", "mol", "K" };

        private static bool AnyDerivedName()
        {
            foreach (string expr in Unit.AllExpressions())
            {
                if (expr == null)
                    continue;

                for (int i = 0; i < expr.Length; i++)
                {
                    if (expr[i] != '\'')
                        continue;

                    int j = i + 1;
                    while (j < expr.Length && char.IsLetter(expr[j]))
                        j++;

                    if (j > i + 1)
                    {
                        string token = expr.Substring(i + 1, j - i - 1);
                        bool known = false;
                        foreach (string b in BaseUnitTokens)
                        {
                            if (b == token)
                                known = true;
                        }
                        if (!known)
                        {
                            Console.WriteLine("        derived unit token '" + token + "' in " + expr);
                            return true;
                        }
                    }
                    i = j - 1;
                }
            }
            return false;
        }

        private static int Main()
        {
            try { Console.OutputEncoding = System.Text.Encoding.UTF8; }
            catch (Exception) { /* a redirected console has no encoding to set */ }

            Console.WriteLine("=== CoolPropWrapper test: managed layer + native engine ===");
            Console.WriteLine();

            // ---- platform ----------------------------------------------------
            Console.WriteLine("-- platform detection --");
            Check("reports Windows (harness runs on Windows)", CoolPropEngine.IsWindows, null);
            Console.WriteLine("  plugin dir: " + CoolPropEngine.PluginDirectory);
            string engineName = IntPtr.Size == 8 ? "CoolProp_x64.dll" : "CoolProp_x86.dll";
            string engine = Path.Combine(CoolPropEngine.PluginDirectory, engineName);
            bool staged = File.Exists(engine);
            Check("native engine staged (" + engineName + ")", staged, engine);
            if (!staged)
            {
                Console.WriteLine();
                Console.WriteLine("No native engine available - aborting before the P/Invoke tests.");
                Console.WriteLine("Result: {0} pass, {1} fail, {2} skip", _pass, _fail, _skip);
                return 2;
            }

            CoolPropEngine.EnsureLoaded();
            Check("EnsureLoaded() completed without throwing", true, null);

            // ---- the C API, all 11 entry points -------------------------------
            Console.WriteLine();
            Console.WriteLine("-- CoolProp C API (reference values from CoolProp documentation) --");

            // Normal boiling point of water at 1 atm: 373.1242958476... K
            Near("PropsSI  T(P=101325, Q=0, Water)",
                 CoolPropEngine.PropsSI("T", "P", 101325.0, "Q", 0.0, "Water"),
                 373.124295847667, 1e-6);

            // Critical temperature of water: 647.096 K.
            Near("Props1SI  Water Tcrit",
                 CoolPropEngine.Props1SI("Water", "Tcrit"), 647.096, 1e-3);

            // Critical pressure of water: 22.064 MPa.
            Near("Props1SI  Water Pcrit",
                 CoolPropEngine.Props1SI("Water", "Pcrit"), 22064000.0, 1000.0);

            // Mass density of saturated liquid water at 1 atm: 958.35 kg/m^3.
            Near("PropsSI  D(P=101325, Q=0, Water)",
                 CoolPropEngine.PropsSI("D", "P", 101325.0, "Q", 0.0, "Water"),
                 958.345, 0.05);

            // Specific enthalpy of saturated vapour at 1 atm: 2675.5 kJ/kg.
            Near("PropsSI  H(P=101325, Q=1, Water)",
                 CoolPropEngine.PropsSI("H", "P", 101325.0, "Q", 1.0, "Water"),
                 2675470.0, 300.0);

            // Enthalpy of humid air per kg dry air at 25 degC, 1 atm, W=0.010.
            // h = 1.006*t + W*(2501 + 1.86*t) in kJ/kg, t in degC -> 50.625 kJ/kg.
            Near("HAPropsSI  H(T=298.15, P=101325, W=0.010)",
                 CoolPropEngine.HAPropsSI("H", "T", 298.15, "P", 101325.0, "W", 0.010),
                 50625.0, 20.0);

            // Saturation pressure of water at the normal boiling point.
            Near("saturation_ancillary  Water P(T=373.1242856)",
                 CoolPropEngine.saturation_ancillary("Water", "P", 0, "T", 373.124285686),
                 101325.0, 300.0);

            var phase = CoolPropEngine.MakeBuffer(null);
            int phaseRc = CoolPropEngine.PhaseSI("P", 101325.0, "Q", 0.0, "Water", phase, phase.Capacity);
            Check("PhaseSI  returns rc 1", phaseRc == 1, "rc = " + phaseRc);
            Eq("PhaseSI  recognises the two-phase region", phase.ToString().Trim(), "twophase");

            var aliases = CoolPropEngine.MakeBuffer(null);
            int aliasRc = CoolPropEngine.get_fluid_param_string("Water", "aliases", aliases, aliases.Capacity);
            Check("get_fluid_param_string  returns rc 1", aliasRc == 1, "rc = " + aliasRc);
            Console.WriteLine("        aliases -> " + aliases.ToString().Trim());

            // The buffer must be seeded with the info type; see the function file.
            var infoLong = CoolPropEngine.MakeBuffer("long");
            int infoRc = CoolPropEngine.get_parameter_information_string("P", infoLong, infoLong.Capacity);
            Check("get_parameter_information_string  returns rc 1", infoRc == 1, "rc = " + infoRc);
            Eq("get_parameter_information_string(P,\"long\")", infoLong.ToString().Trim(), "Pressure");

            var infoIo = CoolPropEngine.MakeBuffer("IO");
            int ioRc = CoolPropEngine.get_parameter_information_string("P", infoIo, infoIo.Capacity);
            Check("get_parameter_information_string(P,\"IO\") rc 1", ioRc == 1, "rc = " + ioRc);
            Console.WriteLine("        P IO class -> " + infoIo.ToString().Trim());

            Eq("get_param_index(P) == 20", CoolPropEngine.get_param_index("P").ToString(), "20");
            Eq("get_param_index(T) == 19", CoolPropEngine.get_param_index("T").ToString(), "19");

            // Setting a reference state is a global side effect; do it once and
            // reset it, so the earlier reference values stay reproducible.
            int rsRc = CoolPropEngine.set_reference_stateS("R134a", "IIR");
            Check("set_reference_stateS(R134a, IIR) rc 1", rsRc == 1, "rc = " + rsRc);
            int rdRc = CoolPropEngine.set_reference_stateD("R134a", 233.15, 5.0, 200000.0, 1000.0);
            Check("set_reference_stateD(R134a, ...) rc 1", rdRc == 1, "rc = " + rdRc);
            int resetRc = CoolPropEngine.set_reference_stateS("R134a", "RESET");
            Check("set_reference_stateS(R134a, RESET) rc 1", resetRc == 1, "rc = " + resetRc);

            // After RESET the reference values must be unchanged - this is what
            // catches a signature mismatch that silently corrupts global state.
            Near("PropsSI unchanged after reference-state round trip",
                 CoolPropEngine.PropsSI("T", "P", 101325.0, "Q", 0.0, "Water"),
                 373.124295847667, 1e-6);

            // The datum semantics themselves, pinned on a throw-away fluid:
            // a named state is applied by ADDING an offset, and "RESET" adds
            // (0, 0) - it is a no-op in CoolProp 8.0.0. "DEF" is the string that
            // disables the offset and restores the library default. A worksheet
            // that wants the default back must therefore use "DEF"; README §11
            // documents this, so a change in the engine must fail here.
            // The round trip above left R134a on a custom + IIR datum, so clear
            // it back to the library default first.
            Check("DEF clears the round-trip offset",
                  CoolPropEngine.set_reference_stateS("R134a", "DEF") == 1, null);
            Near("R134a default h at -40 C, before",
                 CoolPropEngine.PropsSI("H", "T", 233.15, "Q", 0.0, "R134a"), 148144.046, 1.0);
            Check("ASHRAE datum rc 1",
                  CoolPropEngine.set_reference_stateS("R134a", "ASHRAE") == 1, null);
            Near("R134a h at -40 C under ASHRAE is 0",
                 CoolPropEngine.PropsSI("H", "T", 233.15, "Q", 0.0, "R134a"), 0.0, 1e-6);
            Near("R134a s at -40 C under ASHRAE is 0",
                 CoolPropEngine.PropsSI("S", "T", 233.15, "Q", 0.0, "R134a"), 0.0, 1e-9);
            Check("RESET is a no-op, not a restore",
                  CoolPropEngine.set_reference_stateS("R134a", "RESET") == 1, null);
            Near("R134a still on the ASHRAE datum after RESET",
                 CoolPropEngine.PropsSI("H", "T", 233.15, "Q", 0.0, "R134a"), 0.0, 1e-6);
            Check("DEF restores the default",
                  CoolPropEngine.set_reference_stateS("R134a", "DEF") == 1, null);
            Near("R134a back on the default datum after DEF",
                 CoolPropEngine.PropsSI("H", "T", 233.15, "Q", 0.0, "R134a"), 148144.046, 1.0);

            var errBuf = CoolPropEngine.MakeBuffer(null);
            int errRc = CoolPropEngine.get_global_param_string("errstring", errBuf, errBuf.Capacity);
            Check("get_global_param_string(errstring) rc 1", errRc == 1, "rc = " + errRc + " text=\"" + errBuf.ToString().Trim() + "\"");

            // A failing call must come back as +Infinity (CoolProp reports
            // errors that way, not by returning an error code), which
            // MakeDoubleResult turns into an exception carrying errstring.
            double bogus = CoolPropEngine.PropsSI("T", "P", 101325.0, "Q", 0.0, "NotAFluid");
            Check("PropsSI reports a bad fluid as +Infinity",
                  double.IsPositiveInfinity(bogus), "got " + bogus);

            // ---- functional matrix ------------------------------------------
            Console.WriteLine();
            Console.WriteLine("-- functional matrix: the wrapper driving the engine --");

            // Everything above pins *one* value per entry point. This section
            // drives the same P/Invoke surface the plugin's function classes use,
            // the way a worksheet drives them: every output parameter of a
            // state, both phases, the metadata getters, the reference-state
            // setter, and the dimensions the engine itself reports. It is the
            // "is the wrapper actually talking to the DLL" check - a marshalling
            // fault (wrong arity, wrong string type, stack imbalance) shows up
            // here as garbage or +Infinity, not as a clean exception.

            // Compressed liquid water, 300 K, 101325 Pa. Handbook values:
            // rho 996.56, cp 4180.6, mu 853.7e-6, lambda 609.5e-3, a 1501.5,
            // Pr 5.86 - so these are physical, not just self-consistent.
            double liqD = CoolPropEngine.PropsSI("D", "T", 300.0, "P", 101325.0, "Water");
            Near("liquid D  (T,P) = 996.556935265199", liqD, 996.556935265199, 1e-7);
            Near("liquid H  (T,P) = 112654.899654621", CoolPropEngine.PropsSI("H", "T", 300.0, "P", 101325.0, "Water"), 112654.899654621, 1e-6);
            Near("liquid S  (T,P) = 393.062068440610", CoolPropEngine.PropsSI("S", "T", 300.0, "P", 101325.0, "Water"), 393.062068440610, 1e-8);
            Near("liquid U  (T,P) = 112553.224580753", CoolPropEngine.PropsSI("U", "T", 300.0, "P", 101325.0, "Water"), 112553.224580753, 1e-6);
            Near("liquid C  (T,P) = 4180.63577655604", CoolPropEngine.PropsSI("C", "T", 300.0, "P", 101325.0, "Water"), 4180.63577655604, 1e-8);
            Near("liquid O  (T,P) = 4130.17424451451", CoolPropEngine.PropsSI("O", "T", 300.0, "P", 101325.0, "Water"), 4130.17424451451, 1e-8);
            Near("liquid A  (T,P) = 1501.52264675013", CoolPropEngine.PropsSI("A", "T", 300.0, "P", 101325.0, "Water"), 1501.52264675013, 1e-8);
            Near("liquid V  (T,P) = 8.5374248628594e-4", CoolPropEngine.PropsSI("V", "T", 300.0, "P", 101325.0, "Water"), 8.5374248628594e-4, 1e-13);
            Near("liquid L  (T,P) = 0.609499858485630", CoolPropEngine.PropsSI("L", "T", 300.0, "P", 101325.0, "Water"), 0.609499858485630, 1e-12);
            Near("liquid Pr (T,P) = 5.85592651489852", CoolPropEngine.PropsSI("Prandtl", "T", 300.0, "P", 101325.0, "Water"), 5.85592651489852, 1e-10);

            // Surface tension is defined on the saturation curve only in this
            // engine: (T,Q) works, (T,P) at a compressed state is +Infinity.
            Near("surface tension (T,Q=0) = 0.0717693240525",
                 CoolPropEngine.PropsSI("I", "T", 300.0, "Q", 0.0, "Water"), 0.071769324052462, 1e-12);
            Check("surface tension off the saturation line is +Infinity",
                  double.IsPositiveInfinity(CoolPropEngine.PropsSI("I", "T", 300.0, "P", 101325.0, "Water")), null);

            // Superheated steam, 500 K, 101325 Pa: cp 1981.6 J/kg/K matches the
            // steam tables to 0.1 %.
            Near("steam D  (T,P) = 0.440929951846290", CoolPropEngine.PropsSI("D", "T", 500.0, "P", 101325.0, "Water"), 0.440929951846290, 1e-13);
            Near("steam H  (T,P) = 2928507.76671480", CoolPropEngine.PropsSI("H", "T", 500.0, "P", 101325.0, "Water"), 2928507.76671480, 1e-6);
            Near("steam S  (T,P) = 7938.58172955420", CoolPropEngine.PropsSI("S", "T", 500.0, "P", 101325.0, "Water"), 7938.58172955420, 1e-8);
            Near("steam C  (T,P) = 1981.60971649542", CoolPropEngine.PropsSI("C", "T", 500.0, "P", 101325.0, "Water"), 1981.60971649542, 1e-8);
            Near("steam A  (T,P) = 548.296753674497", CoolPropEngine.PropsSI("A", "T", 500.0, "P", 101325.0, "Water"), 548.296753674497, 1e-8);
            Near("steam V  (T,P) = 1.72987223433672e-5", CoolPropEngine.PropsSI("V", "T", 500.0, "P", 101325.0, "Water"), 1.72987223433672e-5, 1e-16);
            Near("steam L  (T,P) = 0.0360351732978627", CoolPropEngine.PropsSI("L", "T", 500.0, "P", 101325.0, "Water"), 0.0360351732978627, 1e-14);
            Near("steam Pr (T,P) = 0.951273801161542", CoolPropEngine.PropsSI("Prandtl", "T", 500.0, "P", 101325.0, "Water"), 0.951273801161542, 1e-12);

            // Round trips: the same state named three different ways must give
            // the same property. This is what proves string/number arguments
            // marshal correctly in every position, not just the first pair.
            double liqH = CoolPropEngine.PropsSI("H", "T", 300.0, "P", 101325.0, "Water");
            double liqS = CoolPropEngine.PropsSI("S", "T", 300.0, "P", 101325.0, "Water");
            Near("round trip H(D,P) == H(T,P)",
                 CoolPropEngine.PropsSI("H", "D", liqD, "P", 101325.0, "Water") - liqH, 0.0, 1e-3);
            Near("round trip H(S,P) == H(T,P)",
                 CoolPropEngine.PropsSI("H", "S", liqS, "P", 101325.0, "Water") - liqH, 0.0, 1e-3);
            Near("round trip T(D,P) == 300",
                 CoolPropEngine.PropsSI("T", "D", liqD, "P", 101325.0, "Water") - 300.0, 0.0, 1e-9);
            Near("definition U == H - p/D",
                 CoolPropEngine.PropsSI("U", "T", 300.0, "P", 101325.0, "Water") - (liqH - 101325.0 / liqD), 0.0, 1e-6);
            Near("definition G == H - T*S",
                 CoolPropEngine.PropsSI("G", "T", 500.0, "P", 101325.0, "Water")
                 - (CoolPropEngine.PropsSI("H", "T", 500.0, "P", 101325.0, "Water")
                    - 500.0 * CoolPropEngine.PropsSI("S", "T", 500.0, "P", 101325.0, "Water")), 0.0, 1e-6);

            // A mixture, bracketed and joined with '&' - the string that most
            // depends on the escape decoder being correct.
            Near("mixture H(T=250,P=101325,R32[.5]&R125[.5]) = 384080.8252",
                 CoolPropEngine.PropsSI("H", "T", 250.0, "P", 101325.0, "R32[0.5]&R125[0.5]"), 384080.8252135, 1e-6);

            // State-independent constants.
            Near("Water Tcrit", CoolPropEngine.Props1SI("Water", "Tcrit"), 647.096, 1e-3);
            Near("Water Pcrit", CoolPropEngine.Props1SI("Water", "Pcrit"), 22064000.0, 1e-3);
            Near("Water Ttriple", CoolPropEngine.Props1SI("Water", "Ttriple"), 273.16, 1e-9);
            Near("Water Tmin", CoolPropEngine.Props1SI("Water", "Tmin"), 273.16, 1e-9);
            Near("Water Tmax", CoolPropEngine.Props1SI("Water", "Tmax"), 2000.0, 1e-9);
            Near("Water acentric", CoolPropEngine.Props1SI("Water", "acentric"), 0.3442920843, 1e-10);
            Near("Water molar mass", CoolPropEngine.Props1SI("Water", "M"), 0.018015268, 1e-12);
            Near("Water rhocrit", CoolPropEngine.Props1SI("Water", "rhocrit"), 322.0, 1e-6);

            // Every phase branch, so each PhaseSI return path is covered.
            Eq("PhaseSI liquid", PhaseText("T", 300.0, "P", 101325.0, "Water"), "rc=1 liquid");
            Eq("PhaseSI gas", PhaseText("T", 500.0, "P", 101325.0, "Water"), "rc=1 gas");
            Eq("PhaseSI twophase", PhaseText("P", 101325.0, "Q", 0.0, "Water"), "rc=1 twophase");
            Eq("PhaseSI supercritical", PhaseText("T", 700.0, "P", 30000000.0, "Water"), "rc=1 supercritical");
            // A bad fluid is not rc 0: CoolProp::PhaseSI catches the init error
            // and writes "unknown: <reason>" through the buffer, still rc 1.
            string badPhase = PhaseText("P", 101325.0, "Q", 0.0, "NoSuchFluid");
            Check("PhaseSI reports a bad fluid as rc 1 + \"unknown: ...\"",
                  badPhase.StartsWith("rc=1 unknown: "), badPhase);

            // The metadata getters, with the buffer-seeding rule applied to all
            // four info kinds and the full index range used by the unit table.
            Eq("info T / IO", InfoText("T", "IO"), "rc=1 IO");
            Eq("info T / short", InfoText("T", "short"), "rc=1 T");
            Eq("info T / long", InfoText("T", "long"), "rc=1 Temperature");
            Eq("info T / units", InfoText("T", "units"), "rc=1 K");
            Eq("info D / units", InfoText("D", "units"), "rc=1 kg/m^3");
            Eq("info H / long", InfoText("H", "long"), "rc=1 Mass specific enthalpy");
            // The engine agrees with the wrapper's table on what "V" means.
            Eq("info V / long  (V is viscosity)", InfoText("V", "long"), "rc=1 Viscosity");
            Eq("info V / units", InfoText("V", "units"), "rc=1 Pa-s");
            Eq("info I / units", InfoText("I", "units"), "rc=1 N/m");
            Eq("info HMOLAR / units", InfoText("HMOLAR", "units"), "rc=1 J/mol");

            EqIndex("T", 19); EqIndex("P", 20); EqIndex("D", 40); EqIndex("H", 41);
            EqIndex("S", 42); EqIndex("U", 46); EqIndex("Q", 21); EqIndex("C", 43);
            EqIndex("O", 45); EqIndex("A", 56); EqIndex("V", 52); EqIndex("L", 53);
            EqIndex("I", 54); EqIndex("Prandtl", 55); EqIndex("HMOLAR", 26);

            Eq("fluid param Water/formula", FluidText("Water", "formula"), "rc=1 H_{2}O_{1}");
            Eq("fluid param Water/CAS", FluidText("Water", "CAS"), "rc=1 7732-18-5");
            Eq("fluid param Water/pure", FluidText("Water", "pure"), "rc=1 true");
            Eq("fluid param Air/pure", FluidText("Air", "pure"), "rc=1 false");
            Eq("fluid param Air/aliases", FluidText("Air", "aliases"), "rc=1 air,AIR,R729");

            var fluidsList = CoolPropEngine.MakeBuffer(null);
            Check("get_global_param_string(FluidsList) rc 1",
                  CoolPropEngine.get_global_param_string("FluidsList", fluidsList, fluidsList.Capacity) == 1, null);
            Check("FluidsList contains Water", fluidsList.ToString().IndexOf("Water") >= 0, null);
            Check("FluidsList contains R134a", fluidsList.ToString().IndexOf("R134a") >= 0, null);
            var paramList = CoolPropEngine.MakeBuffer(null);
            Check("get_global_param_string(parameter_list) rc 1",
                  CoolPropEngine.get_global_param_string("parameter_list", paramList, paramList.Capacity) == 1, null);
            Check("parameter_list mentions Prandtl", paramList.ToString().IndexOf("Prandtl") >= 0, null);

            // The custom-state setter, end to end: it must move h, and DEF must
            // put it back.
            Near("set_reference_stateD baseline",
                 CoolPropEngine.PropsSI("H", "T", 233.15, "Q", 0.0, "R134a"), 148144.0457993, 1e-6);
            Check("set_reference_stateD rc 1",
                  CoolPropEngine.set_reference_stateD("R134a", 233.15, 5.0, 200000.0, 1000.0) == 1, null);
            Near("set_reference_stateD moved h",
                 CoolPropEngine.PropsSI("H", "T", 233.15, "Q", 0.0, "R134a"), 1732628.3107986, 1e-6);
            Check("DEF restores the default",
                  CoolPropEngine.set_reference_stateS("R134a", "DEF") == 1, null);
            Near("default h restored",
                 CoolPropEngine.PropsSI("H", "T", 233.15, "Q", 0.0, "R134a"), 148144.0457993, 1e-6);

            // Failure signalling: the engine says no, and the wrapper has to
            // hear it. Every one of these is +Infinity, never a crash and
            // never a plausible-looking number.
            Check("bad fluid -> +Infinity",
                  double.IsPositiveInfinity(CoolPropEngine.PropsSI("T", "P", 101325.0, "Q", 0.0, "NoSuchFluid")), null);
            Check("bad output -> +Infinity",
                  double.IsPositiveInfinity(CoolPropEngine.PropsSI("NoSuchOutput", "P", 101325.0, "Q", 0.0, "Water")), null);
            Check("bad Props1SI fluid -> +Infinity",
                  double.IsPositiveInfinity(CoolPropEngine.Props1SI("NoSuchFluid", "Tcrit")), null);
            var errAfter = CoolPropEngine.MakeBuffer(null);
            CoolPropEngine.get_global_param_string("errstring", errAfter, errAfter.Capacity);
            Check("errstring carries the reason",
                  errAfter.ToString().IndexOf("valid fluid") >= 0, errAfter.ToString().Trim());

            // The dimensions the engine reports must be the dimensions the
            // wrapper tags: this is the Error 74 safety net, checked against the
            // engine's own opinion rather than against our own table.
            CheckDim("T", false, "K", "'K");
            CheckDim("P", false, "Pa", "'kg/('m*'s^2)");
            CheckDim("D", false, "kg/m^3", "'kg/'m^3");
            CheckDim("H", false, "J/kg", "'m^2/'s^2");
            CheckDim("S", false, "J/kg/K", "'m^2/'s^2/'K");
            CheckDim("A", false, "m/s", "'m/'s");
            CheckDim("V", false, "Pa-s", "'kg/('m*'s)");
            CheckDim("L", false, "W/m/K", "'kg*'m/('s^3*'K)");
            CheckDim("I", false, "N/m", "'kg/'s^2");
            CheckDim("HMOLAR", false, "J/mol", "'kg*'m^2/('mol*'s^2)");
            CheckDim("SMOLAR", false, "J/mol/K", "'kg*'m^2/('mol*'s^2*'K)");
            CheckDim("DMOLAR", false, "mol/m^3", "'mol/'m^3");
            CheckDim("M", false, "kg/mol", "'kg/'mol");
            CheckDim("H", true, "J/kg", "'m^2/'s^2");        // humid air enthalpy
            CheckDim("P_w", true, "Pa", "'kg/('m*'s^2)", engineKnows: false);    // HA-only key, no engine info entry
            Check("dimensionless Prandtl has no tag",
                  ReferenceEquals(Unit.ExpressionFor("Prandtl", false), null), null);
            Check("dimensionless Q has no tag",
                  ReferenceEquals(Unit.ExpressionFor("Q", false), null), null);
            Check("dimensionless HA W has no tag",
                  ReferenceEquals(Unit.ExpressionFor("W", true), null), null);

            // ---- pure string helpers ----------------------------------------
            Console.WriteLine();
            Console.WriteLine("-- SMath string transport --");

            // SMath escapes a character that is special to its parser as
            // \HHHH\ : four hex digits plus a trailing backslash delimiter.
            Eq("no backslash is passed through",
               coolpropPlugin.UnescapeMathString("Water"), "Water");
            Eq("\\002E\\ decodes to '.'",
               coolpropPlugin.UnescapeMathString("0\\002E\\5"), "0.5");
            Eq("\\0026\\ decodes to '&'",
               coolpropPlugin.UnescapeMathString("R32\\0026\\R125"), "R32&R125");
            Eq("\\0027\\ decodes to an apostrophe",
               coolpropPlugin.UnescapeMathString("a\\0027\\b"), "a'b");
            Eq("decoding is idempotent",
               coolpropPlugin.UnescapeMathString(coolpropPlugin.UnescapeMathString("0\\002E\\5")), "0.5");
            Eq("a backslash not followed by 4 hex digits stays literal",
               coolpropPlugin.UnescapeMathString("C:\\path\\to"), "C:\\path\\to");
            Eq("truncated escape at the end is literal",
               coolpropPlugin.UnescapeMathString("abc\\00"), "abc\\00");
            Eq("null survives", coolpropPlugin.UnescapeMathString(null), null);
            Eq("an escape with no trailing delimiter still decodes",
               coolpropPlugin.UnescapeMathString("a\\0026b"), "a&b");
            // Mixture syntax for CoolProp, bracketed with fractional
            // compositions: '[' -> \005B\, ']' -> \005D\, '.' -> \002E\,
            // '&' -> \0026\. Each segment is tested on its own so that two
            // adjacent escapes never share one backslash.
            Eq("'[' and ']' decode",
               coolpropPlugin.UnescapeMathString("\\005B\\0\\002E\\5\\005D\\"), "[0.5]");
            Eq("'&' between two fluids decodes",
               coolpropPlugin.UnescapeMathString("R410A\\0026\\R410B"), "R410A&R410B");

            // Known limitation, deliberately not "fixed": the delimiter scheme
            // is ambiguous when two escapes are adjacent, because the backslash
            // that terminates the first also looks like the introducer of the
            // second. SMath does not emit that form (its escapes are emitted
            // per character, each with its own backslash pair), and the
            // alternative reading would break a literal "\005B" text string.
            // The decoder keeps the Linux build's proven behaviour.
            Eq("adjacent escapes consume one delimiter (documented)",
               coolpropPlugin.UnescapeMathString("\\005B\\005D\\"), "[005D\\");

            // ---- unit table --------------------------------------------------
            Console.WriteLine();
            Console.WriteLine("-- unit table (base-unit expressions only) --");

            // The mapping itself is asserted directly, because parsing an MItem
            // needs a live SMath session. These are the whole safety net against
            // Error 74: a named derived unit here would make every combination
            // of that quantity fail.
            //
            // Expectations come from CoolProp's own parameter_info_list
            // (src/DataStructures.cpp), so they cannot drift from the engine.
            Eq("T  -> K", Unit.ExpressionFor("T", false), "'K");
            Eq("P  -> kg/(m*s^2)", Unit.ExpressionFor("P", false), "'kg/('m*'s^2)");
            Eq("D  -> kg/m^3", Unit.ExpressionFor("D", false), "'kg/'m^3");
            Eq("H  -> m^2/s^2 (J/kg)", Unit.ExpressionFor("H", false), "'m^2/'s^2");
            Eq("S  -> m^2/(s^2*K) (J/kg/K)", Unit.ExpressionFor("S", false), "'m^2/'s^2/'K");
            Eq("C  -> m^2/(s^2*K) (cp)", Unit.ExpressionFor("C", false), "'m^2/'s^2/'K");
            Eq("O  -> m^2/(s^2*K) (cv)", Unit.ExpressionFor("O", false), "'m^2/'s^2/'K");
            Eq("A  -> m/s", Unit.ExpressionFor("A", false), "'m/'s");
            Eq("M  -> kg/mol", Unit.ExpressionFor("M", false), "'kg/'mol");
            // In CoolProp the parameter "V" is VISCOSITY, not specific volume:
            // the parameter table installs "V" -> iviscosity as a backward-
            // compatibility alias. There is no specific-volume parameter.
            Eq("V  -> kg/(m*s) (Pa*s, viscosity)", Unit.ExpressionFor("V", false), "'kg/('m*'s)");
            Eq("L  -> kg*m/(s^3*K) (conductivity)", Unit.ExpressionFor("L", false), "'kg*'m/('s^3*'K)");
            Eq("I  -> kg/s^2 (surface tension)", Unit.ExpressionFor("I", false), "'kg/'s^2");
            Eq("CONDUCTIVITY -> kg*m/(s^3*K) (W/m/K)", Unit.ExpressionFor("CONDUCTIVITY", false), "'kg*'m/('s^3*'K)");
            Eq("SURFACE_TENSION -> kg/s^2 (N/m)", Unit.ExpressionFor("SURFACE_TENSION", false), "'kg/'s^2");
            Eq("HMOLAR -> J/mol base form", Unit.ExpressionFor("HMOLAR", false), "'kg*'m^2/('mol*'s^2)");
            Eq("SMOLAR -> J/mol/K base form", Unit.ExpressionFor("SMOLAR", false), "'kg*'m^2/('mol*'s^2*'K)");
            Eq("DMOLAR -> mol/m^3", Unit.ExpressionFor("DMOLAR", false), "'mol/'m^3");
            Eq("GAS_CONSTANT -> J/mol/K base form", Unit.ExpressionFor("gas_constant", false), "'kg*'m^2/('mol*'s^2*'K)");
            Eq("ISOBARIC_EXPANSION_COEFFICIENT -> 1/K", Unit.ExpressionFor("isobaric_expansion_coefficient", false), "1/'K");
            Eq("ISOTHERMAL_COMPRESSIBILITY -> 1/Pa base form", Unit.ExpressionFor("isothermal_compressibility", false), "'m*'s^2/'kg");
            Eq("T_critical -> K", Unit.ExpressionFor("T_critical", false), "'K");
            Eq("p_critical -> kg/(m*s^2)", Unit.ExpressionFor("p_critical", false), "'kg/('m*'s^2)");
            Eq("molar_mass -> kg/mol", Unit.ExpressionFor("molar_mass", false), "'kg/'mol");

            // CoolProp accepts both spellings of every name; the upper-case one
            // must resolve to the same unit as the mixed-case one.
            Eq("upper-case alias of T_reducing", Unit.ExpressionFor("T_REDUCING", false), "'K");
            Eq("upper-case alias of pcrit", Unit.ExpressionFor("PCRIT", false), "'kg/('m*'s^2)");
            Eq("upper-case alias of hmass", Unit.ExpressionFor("HMASS", false), "'m^2/'s^2");
            Eq("upper-case alias of viscosity", Unit.ExpressionFor("VISCOSITY", false), "'kg/('m*'s)");

            // Dimensionless and unknown names must have no expression at all.
            Check("dimensionless Q has no unit", Unit.ExpressionFor("Q", false) == null, null);
            Check("dimensionless Z has no unit", Unit.ExpressionFor("Z", false) == null, null);
            Check("dimensionless Phase has no unit", Unit.ExpressionFor("Phase", false) == null, null);
            Check("dimensionless Prandtl has no unit", Unit.ExpressionFor("Prandtl", false) == null, null);
            Check("dipole_moment (C-m) is left untagged", Unit.ExpressionFor("dipole_moment", false) == null, null);
            Check("unknown name has no unit", Unit.ExpressionFor("NoSuchParameter", false) == null, null);
            Check("null name has no unit", Unit.ExpressionFor(null, false) == null, null);
            Check("HA W (humidity ratio) has no unit", Unit.ExpressionFor("W", true) == null, null);
            Check("HA RH has no unit", Unit.ExpressionFor("RH", true) == null, null);

            // Humid-air table.
            Eq("HA H -> m^2/s^2 (J/kg dry air)", Unit.ExpressionFor("H", true), "'m^2/'s^2");
            Eq("HA Tdb -> K", Unit.ExpressionFor("Tdb", true), "'K");
            Eq("HA DewPoint -> K", Unit.ExpressionFor("DewPoint", true), "'K");
            Eq("HA P_w -> kg/(m*s^2)", Unit.ExpressionFor("P_w", true), "'kg/('m*'s^2)");
            Eq("HA Vda -> m^3/kg", Unit.ExpressionFor("Vda", true), "'m^3/'kg");
            Eq("HA mu -> kg/(m*s)", Unit.ExpressionFor("mu", true), "'kg/('m*'s)");
            Eq("HA k -> kg*m/(s^3*K)", Unit.ExpressionFor("k", true), "'kg*'m/('s^3*'K)");
            Check("HA virial coefficient Bvir is untagged", Unit.ExpressionFor("Bvir", true) == null, null);

            // None of the emitted expressions may name a derived unit: that is
            // the Error 74 trap. A scan over every table entry is the cheapest
            // way to make sure a future addition does not reintroduce it.
            Check("no expression names a derived unit (J, Pa, W, N, mol)",
                  !AnyDerivedName(), "scanned both tables");

            // Find() must never throw outside a session; it degrades to "no tag".
            bool threw = false;
            try { Unit.Find("P"); }
            catch (Exception) { threw = true; }
            Check("Find() degrades gracefully without a session", !threw, null);

            bool threwHa = false;
            try { Unit.FindHA("H"); }
            catch (Exception) { threwHa = true; }
            Check("FindHA() degrades gracefully without a session", !threwHa, null);

            // MItem overloads == and dereferences both operands, so null tests
            // here go through ReferenceEquals.
            Check("unknown parameter returns no unit", ReferenceEquals(Unit.Find("NoSuchParameter"), null), null);
            Check("null parameter returns no unit", ReferenceEquals(Unit.Find(null), null), null);
            Check("dimensionless parameter returns no unit", ReferenceEquals(Unit.Find("Q"), null), null);
            Check("dimensionless HA parameter returns no unit", ReferenceEquals(Unit.FindHA("W"), null), null);
            Check("unitless is null by design", ReferenceEquals(Unit.unitless, null), null);

            // ---- registration ------------------------------------------------
            Console.WriteLine();
            Console.WriteLine("-- reflection registration --");

            // The plugin registers by reflecting over IFunction types that
            // declare `public static int[] Arguments`. Reproduce that walk to
            // prove every function class is discoverable.
            Assembly plugin = typeof(coolpropPlugin).Assembly;
            Type[] types;
            try
            {
                types = plugin.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types;
                Console.WriteLine("  (some types failed to load: " + ex.Message + ")");
            }

            int registered = 0;
            foreach (Type type in types)
            {
                if (type == null || !type.IsClass)
                    continue;
                if (!typeof(IFunction).IsAssignableFrom(type))
                    continue;

                FieldInfo arguments = type.GetField("Arguments",
                    BindingFlags.GetField | BindingFlags.Static | BindingFlags.Public);
                if (arguments == null)
                {
                    Check("Arguments field is public and static", false, type.Name);
                    continue;
                }

                int[] args = arguments.GetValue(null) as int[];
                if (args == null)
                {
                    Check("Arguments field is an int[]", false, type.Name);
                    continue;
                }

                foreach (int arity in args)
                {
                    registered++;
                    Console.WriteLine("        " + type.Name + "  arity " + arity);
                }
            }

            Check("all 11 functions discovered", registered == 11, "found " + registered);

            // Constructing a function needs a live session: new Term(...)
            // dereferences session state, so it throws here. That is expected
            // and is why the instantiation itself is only reachable from SMath.
            bool constructionThrew = false;
            foreach (Type type in types)
            {
                if (type == null || !type.IsClass || !typeof(IFunction).IsAssignableFrom(type))
                    continue;
                try
                {
                    Activator.CreateInstance(type, new object[] { 1 });
                }
                catch (Exception)
                {
                    constructionThrew = true;
                }
            }
            Console.WriteLine("  note: constructing a function outside SMath throws: " +
                              constructionThrew + " (expected - new Term needs a session)");

            // Class names become the term names SMath shows, so they have to be
            // unique; two classes with one name would fight over one term.
            CheckMaybe("function class names are unique",
                       () =>
                       {
                           var seen = new System.Collections.Specialized.StringCollection();
                           foreach (Type type in plugin.GetTypes())
                           {
                               if (type == null || !type.IsClass || !typeof(IFunction).IsAssignableFrom(type))
                                   continue;
                               if (seen.Contains(type.Name))
                                   return false;
                               seen.Add(type.Name);
                           }
                           return true;
                       }, "11 distinct names");

            Console.WriteLine();
            Console.WriteLine("Result: {0} pass, {1} fail, {2} skip", _pass, _fail, _skip);
            Console.WriteLine("\n=== {0} ===", _fail == 0 ? "ALL TESTS PASSED" : _fail + " FAILURE(S)");
            return _fail == 0 ? 0 : 1;
        }

        /// <summary>
        /// "rc=1 liquid" - the phase string with its return code, for Eq().
        /// </summary>
        private static string PhaseText(string n1, double v1, string n2, double v2, string fluid)
        {
            var sb = CoolPropEngine.MakeBuffer(null);
            int rc = CoolPropEngine.PhaseSI(n1, v1, n2, v2, fluid, sb, sb.Capacity);
            return "rc=" + rc + " " + sb.ToString().Trim();
        }

        /// <summary>
        /// "rc=1 Temperature" - a metadata string with its return code. The
        /// buffer must be seeded with the info kind; the same buffer is both the
        /// lookup key and the write destination in the engine.
        /// </summary>
        private static string InfoText(string key, string kind)
        {
            var sb = CoolPropEngine.MakeBuffer(kind);
            int rc = CoolPropEngine.get_parameter_information_string(key, sb, sb.Capacity);
            return "rc=" + rc + " " + sb.ToString().Trim();
        }

        private static string FluidText(string fluid, string param)
        {
            var sb = CoolPropEngine.MakeBuffer(null);
            int rc = CoolPropEngine.get_fluid_param_string(fluid, param, sb, sb.Capacity);
            return "rc=" + rc + " " + sb.ToString().Trim();
        }

        private static void EqIndex(string name, int expected)
        {
            Eq("get_param_index(" + name + ")", CoolPropEngine.get_param_index(name).ToString(), expected.ToString());
        }

        /// <summary>
        /// The engine's own unit string for a parameter, next to the base-unit
        /// expression this wrapper would tag the result with. A mismatch means
        /// either the wrapper's table is wrong or the Error 74 trap is back.
        /// HA-only keys ("P_w") are not in the AbstractState parameter list,
        /// so get_parameter_information_string cannot confirm them: rc 0 is
        /// then the expected answer and only the wrapper's own tag is checked.
        /// </summary>
        private static void CheckDim(string param, bool humidAir, string engineUnits, string wrapperExpression, bool engineKnows = true)
        {
            var sb = CoolPropEngine.MakeBuffer("units");
            int rc = CoolPropEngine.get_parameter_information_string(param, sb, sb.Capacity);
            string engine = sb.ToString().Trim();
            string tag = Unit.ExpressionFor(param, humidAir);
            if (engineKnows)
            {
                Check("dimension " + param + " (" + (humidAir ? "HA" : "pure") + ")"
                      + " engine says \"" + engine + "\" rc=" + rc,
                      rc == 1 && engine == engineUnits && ReferenceEquals(tag, null) == false,
                      "wrapper tags " + (ReferenceEquals(tag, null) ? "(none)" : tag));
            }
            else
            {
                Check("dimension " + param + " (HA-only, engine has no info)"
                      + " engine says \"" + engine + "\" rc=" + rc,
                      rc == 0 && ReferenceEquals(tag, null) == false,
                      "wrapper tags " + (ReferenceEquals(tag, null) ? "(none)" : tag));
            }
            if (!ReferenceEquals(tag, null))
            {
                Eq("   tag is the base-unit form", tag, wrapperExpression);
            }
        }

        /// <summary>
        /// Reports how a parameter maps to a unit, for the console. Parsing is
        /// impossible outside a live session, so only the mapping is shown.
        /// </summary>
        private static string UnitText(string name, bool humidAir)
        {
            try
            {
                string text = Unit.ExpressionFor(name, humidAir);
                return ReferenceEquals(text, null) ? "(no unit)" : "\"" + text + "\"";
            }
            catch (Exception)
            {
                return "(unavailable)";
            }
        }
    }
}
