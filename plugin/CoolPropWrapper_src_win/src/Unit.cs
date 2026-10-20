/*
 * Unit tagging for CoolProp results.
 *
 * SMath's numeric layer keeps a value and a unit AST per component
 * (BaseEntry.Value + BaseEntry.Units) and converts to base units through
 * UnitInfo.Factor, so a plugin both receives and must return SI base units:
 * PropsSI("T","P",101325,"Q",0,"Water") is handed 101325, not 1 atm, and the
 * result comes back in K.
 *
 * BaseEntry.ComUnits compares the two unit ASTs for EQUALITY when combining
 * components, so a named derived unit (J, Pa, W) or an unreduced compound
 * ('J/'kg) makes the result dimensionally unequal to a native worksheet
 * expression and surfaces as Error 74 on the first subtraction. That is exactly
 * the trap the Mono/Linux build of this plugin fell into: its Unit.cs tagged
 * results with 'Pa, 'J/'mol, 'W/'m/'K and friends.
 *
 * This file therefore tags every quantity with its base-unit expression only:
 *
 *   temperature                       K
 *   pressure, vapour pressure         kg/(m*s^2)
 *   specific energy (mass)            m^2/s^2                 (= J/kg)
 *   molar energy                      (kg*m^2)/(mol*s^2)      (= J/mol)
 *   specific entropy (mass)           m^2/(s^2*K)             (= J/kg/K)
 *   molar entropy                     (kg*m^2)/(mol*s^2*K)    (= J/mol/K)
 *   density                           kg/m^3
 *   molar density                     mol/m^3
 *   specific volume                   m^3/kg
 *   molar mass                        kg/mol
 *   speed of sound                    m/s
 *   thermal conductivity             (kg*m)/(s^3*K)          (= W/m/K)
 *   surface tension                   kg/s^2                  (= N/m)
 *   viscosity                         kg/(m*s)                (= Pa*s)
 *   isobaric expans. / compress.      1/K and (m*s^2)/kg
 *
 * A name that is not in the table gets NO tag at all (Units is left at SMath's
 * default of 1). Not tagging is always safe - a wrong tag is not.
 *
 * The MItem instances are built lazily on first use: SMath's parser needs live
 * session state, so they cannot be created in a static initializer that might
 * run too early. A static initializer that throws also poisons the type for the
 * lifetime of the process, which was the 8.0.0.1 crash on the Mono build - so
 * there is deliberately no static state in this class.
 */

using System.Collections.Generic;
using SMath.Math.Symbolic;

namespace CoolPropWrapper
{
    internal static class Unit
    {
        /// <summary>
        /// Set if the parser ever rejects a unit string; stops retrying and
        /// makes every lookup return null (no tag).
        /// </summary>
        private static bool _disabled;

        /// <summary>Pure-fluid parameter name -> unit expression, built lazily.</summary>
        private static Dictionary<string, string> _pure;

        /// <summary>Humid-air parameter name -> unit expression, built lazily.</summary>
        private static Dictionary<string, string> _ha;

        /// <summary>
        /// "No unit". Returning null rather than an MItem for 1 keeps the
        /// dimensionless case identical to leaving Units untouched.
        /// </summary>
        public static MItem unitless
        {
            get { return null; }
        }

        // ------------------------------------------------------------------
        // Table construction
        // ------------------------------------------------------------------

        /// <summary>
        /// Adds a name and its upper-cased spelling. CoolProp's index_map_insert
        /// does exactly that, so "T_reducing" and "T_REDUCING" are both valid in
        /// a worksheet and both must resolve to the same unit.
        /// </summary>
        private static void Add(Dictionary<string, string> table, string expression, params string[] names)
        {
            foreach (string name in names)
            {
                if (string.IsNullOrEmpty(name))
                    continue;
                if (!table.ContainsKey(name))
                    table[name] = expression;
                string upper = name.ToUpperInvariant();
                if (!table.ContainsKey(upper))
                    table[upper] = expression;
            }
        }

        /// <summary>
        /// Builds the pure-fluid parameter table. The name list mirrors
        /// CoolProp's parameter_info_list (src/DataStructures.cpp) plus the
        /// backward-compatibility aliases it installs (D, H, S, U, G, C, O, V,
        /// L, A, I, M). A name that is absent gets no tag, which is always safe;
        /// a name mapped to the WRONG unit is not.
        /// </summary>
        private static Dictionary<string, string> BuildPure()
        {
            var t = new Dictionary<string, string>(260);

            // Dimensionless.
            Add(t, null,
                "Delta",                                          // Reduced density (rho/rhoc)
                "Tau",                                            // Reciprocal reduced temperature
                "acentric",                                       // Acentric factor
                "Q",                                              // Molar vapour quality [mol/mol]
                "Qmass",                                          // Mass-basis vapour quality [kg/kg]
                "Bvirial", "Cvirial",                             // Virial coefficients
                "dBvirial_dT", "dCvirial_dT",
                "alpha0", "alphar",
                "dalpha0_ddelta_consttau", "dalpha0_dtau_constdelta",
                "dalphar_ddelta_consttau", "dalphar_dtau_constdelta",
                "d2alpha0_ddelta2_consttau", "d3alpha0_ddelta3_consttau",
                "FH", "HH", "PH", "GWP20", "GWP100", "GWP500", "ODP",
                "PIP",                                            // Phase identification parameter
                "Prandtl", "Z",
                "fundamental_derivative_of_gas_dynamics",
                "isentropic_expansion_coefficient",
                "fraction_min", "fraction_max",
                "DELTA", "TAU",                                   // upper-case aliases of Delta/Tau
                "Phase");                                         // Phase index as a float

            // Temperature [K].
            Add(t, "'K",
                "T",
                "T_critical", "T_reducing", "T_triple",
                "T_min", "T_max", "T_freeze",
                "Tcrit", "Ttriple", "Tmin", "Tmax");        // backward-compat aliases

            // Pressure [Pa] = kg/(m*s^2).
            Add(t, "'kg/('m*'s^2)",
                "P", "p", "p_critical", "p_reducing", "p_triple",
                "P_min", "P_max",
                "pcrit", "Pcrit", "ptriple", "pmax", "pmin");

            // Mass density [kg/m^3].
            Add(t, "'kg/'m^3",
                "D", "Dmass", "rhomass_critical", "rhomass_reducing", "rhocrit");

            // Molar density [mol/m^3].
            Add(t, "'mol/'m^3",
                "Dmolar", "rhomolar_critical", "rhomolar_reducing");

            // Mass specific enthalpy / internal energy / Gibbs / Helmholtz [J/kg].
            Add(t, "'m^2/'s^2",
                "H", "Hmass", "U", "Umass", "G", "Gmass",
                "Hmass_residual", "Hmass_idealgas",
                "Umass_idealgas", "Helmholtzmass");

            // Mass specific entropy / heat capacities [J/kg/K].
            Add(t, "'m^2/'s^2/'K",
                "S", "Smass", "C", "Cpmass", "O", "Cvmass",
                "Cp0mass", "Smass_residual", "Smass_idealgas");

            // Molar enthalpy / internal energy / Gibbs / Helmholtz [J/mol].
            Add(t, "'kg*'m^2/('mol*'s^2)",
                "Hmolar", "Umolar", "Gmolar", "Helmholtzmolar",
                "Hmolar_residual", "Hmolar_idealgas", "Umolar_idealgas");

            // Molar entropy / heat capacities [J/mol/K].
            Add(t, "'kg*'m^2/('mol*'s^2*'K)",
                "Smolar", "Cpmolar", "Cvmolar", "Cp0molar",
                "Smolar_residual", "Smolar_idealgas",
                "gas_constant");                                  // universal gas constant

            // Molar mass [kg/mol].
            Add(t, "'kg/'mol",
                "M", "molar_mass", "molarmass", "molemass");
            // Speed of sound [m/s].
            Add(t, "'m/'s",
                "A", "speed_of_sound");

            // Thermal conductivity [W/m/K] = (kg*m)/(s^3*K).
            Add(t, "'kg*'m/('s^3*'K)",
                "L", "conductivity");

            // Isobaric expansion coefficient [1/K].
            Add(t, "1/'K",
                "isobaric_expansion_coefficient");

            // Isothermal compressibility [1/Pa] = (m*s^2)/kg.
            Add(t, "'m*'s^2/'kg",
                "isothermal_compressibility");

            // Surface tension [N/m] = kg/s^2.
            Add(t, "'kg/'s^2",
                "I", "surface_tension");

            // Dynamic viscosity [Pa*s] = kg/(m*s). NOTE: in CoolProp the
            // parameter "V" is viscosity, not specific volume - there is no
            // specific-volume parameter, use 1/D for that.
            Add(t, "'kg/('m*'s)",
                "V", "viscosity");

            return t;
        }

        /// <summary>
        /// Builds the humid-air parameter table (CoolProp HumidAirProp). Names
        /// that are not listed here - the humid-air virial coefficients
        /// Bvir/Cvir and the higher derivatives in particular, whose units are
        /// not obvious from the source - deliberately get no tag.
        /// </summary>
        private static Dictionary<string, string> BuildHumidAir()
        {
            var t = new Dictionary<string, string>(60);

            // Dimensionless.
            Add(t, null,
                "Omega", "HumRat", "W",        // humidity ratio [kg water / kg dry air]
                "RH", "RelHum", "R",           // relative humidity
                "psi_w", "Y",                  // mole fraction of water
                "Z");                          // compressibility factor

            // Temperature [K].
            Add(t, "'K",
                "Tdb", "T_db", "T",                 // dry-bulb
                "Tdp", "T_dp", "DewPoint", "D",     // dew-point
                "Twb", "T_wb", "WetBulb", "B");     // wet-bulb

            // Specific enthalpy [J/kg dry air] and per kg humid air for the *ha forms.
            Add(t, "'m^2/'s^2",
                "Enthalpy", "H", "Hda",             // per kg dry air
                "Hha");                             // per kg humid air

            // Specific entropy [J/kg/K].
            Add(t, "'m^2/'s^2/'K",
                "Entropy", "S", "Sda",
                "Sha",
                "C", "cp", "Cha", "cp_ha");

            // Pressure [Pa].
            Add(t, "'kg/('m*'s^2)",
                "P",
                "P_w");                             // water partial pressure

            // Specific volume [m^3/kg dry air].
            Add(t, "'m^3/'kg",
                "V", "Vda",
                "Vha");                             // per kg humid air

            // Dynamic viscosity [Pa*s].
            Add(t, "'kg/('m*'s)",
                "mu", "Visc", "M");

            // Thermal conductivity [W/m/K].
            Add(t, "'kg*'m/('s^3*'K)",
                "k", "Conductivity", "K");

            return t;
        }

        // ------------------------------------------------------------------
        // Lookup
        // ------------------------------------------------------------------

        private static Dictionary<string, string> Table(ref Dictionary<string, string> slot, bool humidAir)
        {
            if (_disabled)
                return null;
            if (slot == null)
            {
                try
                {
                    slot = humidAir ? BuildHumidAir() : BuildPure();
                }
                catch (System.Exception)
                {
                    // Dictionary construction itself cannot realistically fail;
                    // if it somehow does, degrade to "no tags".
                    _disabled = true;
                    return null;
                }
            }
            return slot;
        }

        /// <summary>
        /// Parses a unit expression once. Returns null if SMath's parser
        /// rejects it, which disables tagging for the rest of the session.
        /// </summary>
        private static MItem Parse(string expression)
        {
            if (expression == null)
                return null;
            try
            {
                return Converter.ToMItem(expression);
            }
            catch (System.Exception)
            {
                _disabled = true;
                return null;
            }
        }

        /// <summary>
        /// The base-unit expression a parameter name maps to, without parsing
        /// it. Returns null when the name is unknown or dimensionless. This is
        /// what the tests assert against: parsing an MItem needs a live SMath
        /// session, but the mapping itself does not.
        /// </summary>
        internal static string ExpressionFor(string param, bool humidAir)
        {
            if (param == null)
                return null;

            Dictionary<string, string> table = humidAir ? BuildHumidAir() : BuildPure();
            string expression;
            if (table == null || !table.TryGetValue(param, out expression))
                return null;

            return expression;
        }

        /// <summary>
        /// Every expression either table contains. Used by the tests to assert
        /// that no derived unit name has crept back in.
        /// </summary>
        internal static System.Collections.Generic.IEnumerable<string> AllExpressions()
        {
            foreach (var table in new[] { BuildPure(), BuildHumidAir() })
            {
                if (table == null)
                    continue;
                foreach (var pair in table)
                {
                    if (pair.Value != null)
                        yield return pair.Value;
                }
            }
        }

        /// <summary>
        /// Unit for a pure-fluid CoolProp parameter name. Returns null for a
        /// dimensionless or unknown parameter, meaning "do not tag".
        /// </summary>
        public static MItem Find(string param)
        {
            if (param == null)
                return null;

            Dictionary<string, string> table = Table(ref _pure, false);
            string expression;
            if (table == null || !table.TryGetValue(param, out expression))
                return null;

            return Parse(expression);
        }

        /// <summary>
        /// Unit for a humid-air CoolProp parameter name. Returns null for a
        /// dimensionless or unknown parameter, meaning "do not tag".
        /// </summary>
        public static MItem FindHA(string param)
        {
            if (param == null)
                return null;

            Dictionary<string, string> table = Table(ref _ha, true);
            string expression;
            if (table == null || !table.TryGetValue(param, out expression))
                return null;

            return Parse(expression);
        }
    }
}
