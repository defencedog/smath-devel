/*
 * IF97 SMath Plugin — function table.
 *
 * The plugin is unit-aware and works entirely in SI base units, matching how
 * SMath itself behaves:
 *
 *   INPUTS  (what the worksheet supplies)
 *     temperature   K                 -> passed straight to IF97
 *     pressure      Pa                -> converted to MPa for IF97
 *     enthalpy      J/kg              -> converted to kJ/kg for IF97
 *     entropy       J/(kg*K)          -> converted to kJ/(kg*K) for IF97
 *
 *   OUTPUTS (value in base units + matching unit expression attached)
 *     see UnitExpressions for the exact base-unit forms.
 *
 * The underlying IF97 library works in K / MPa / kJ/kg, which is why the
 * conversions exist. Callers therefore write IF97_h_Tp(300*'K, 3*'MPa) and get
 * a real quantity back that SMath can contract to kJ/kg for display.
 */

using System;
using SMath.Manager;
using SMath.Math.Numeric;
using SMath.Math.Symbolic;
using IF97;

namespace IF97SMath
{
    /// <summary>.NET 2.0 has no Func&lt;&gt;, so the plugin declares its own delegate.</summary>
    internal delegate double Compute(double[] a);

    /// <summary>Unit kind of one input argument, in SI base units.</summary>
    internal enum ArgKind
    {
        Temperature,   // K
        Pressure,      // Pa
        Enthalpy,      // J/kg
        Entropy        // J/(kg*K)
    }

    /// <summary>Physical quantity produced by a function.</summary>
    internal enum Quantity
    {
        Temperature,
        Pressure,
        Density,
        SpecificVolume,
        SpecificEnergy,
        SpecificEntropy,      // same dimension as specific heat capacity
        Velocity,
        DynamicViscosity,
        ThermalConductivity,
        SurfaceTension,
        Dimensionless
    }

    /// <summary>One SMath function backed by an IF97 call.</summary>
    internal sealed class IF97Function
    {
        public readonly string Name;
        public readonly string Description;
        public readonly ArgKind[] ArgKinds;
        public readonly Quantity Output;
        private readonly Compute _compute;

        public IF97Function(string name, string description,
                            ArgKind[] argKinds, Quantity output, Compute compute)
        {
            Name = name;
            Description = description;
            ArgKinds = argKinds;
            Output = output;
            _compute = compute;
        }

        public int ArgsCount
        {
            get { return ArgKinds.Length; }
        }

        public TermInfo GetTermInfo()
        {
            var arguments = new ArgumentInfo[ArgsCount];
            for (int i = 0; i < ArgsCount; i++)
                arguments[i] = new ArgumentInfo(ArgumentSections.RealNumber);

            return new TermInfo(Name, TermType.Function, Description,
                                FunctionSections.Unknown, true, arguments);
        }

        /// <summary>SI base-unit input -> the unit the IF97 library expects.</summary>
        private static double ToLibraryUnits(double value, ArgKind kind)
        {
            switch (kind)
            {
                case ArgKind.Pressure:
                    return value / 1e6;        // Pa  -> MPa
                case ArgKind.Enthalpy:
                    return value / 1e3;        // J/kg -> kJ/kg
                case ArgKind.Entropy:
                    return value / 1e3;        // J/(kg*K) -> kJ/(kg*K)
                default:
                    return value;              // K stays K
            }
        }

        /// <summary>IF97 library unit -> SI base-unit multiplier for the output.</summary>
        private static double OutputScale(Quantity quantity)
        {
            switch (quantity)
            {
                case Quantity.Pressure:
                    return 1e6;                // MPa -> Pa
                case Quantity.SpecificEnergy:
                    return 1e3;                // kJ/kg -> J/kg (m^2/s^2)
                case Quantity.SpecificEntropy:
                    return 1e3;                // kJ/(kg*K) -> J/(kg*K)
                default:
                    return 1.0;
            }
        }

        /// <summary>
        /// Evaluates the function and returns the result in SI base units,
        /// without attaching a unit expression. Kept separate from
        /// <see cref="Evaluate"/> so the conversion chain is testable without a
        /// live SMath session (MItem construction needs one).
        /// </summary>
        public double EvaluateRaw(double[] siArgs)
        {
            var args = new double[siArgs.Length];
            for (int i = 0; i < siArgs.Length; i++)
                args[i] = ToLibraryUnits(siArgs[i], ArgKinds[i]);

            double value;
            try
            {
                value = _compute(args);
            }
            catch (Exception)
            {
                value = double.NaN;
            }

            return value * OutputScale(Output);
        }

        /// <summary>
        /// Evaluates the function and returns a unit-bearing TNumber, so SMath
        /// treats the result as a real physical quantity.
        /// </summary>
        public TNumber Evaluate(double[] siArgs)
        {
            TNumber result = new TNumber(EvaluateRaw(siArgs));
            MItem units = UnitExpressions.For(Output);
            if (units != null)
                result.obj.Units = units;
            return result;
        }
    }

    internal static class IF97Functions
    {
        private static readonly ArgKind T = ArgKind.Temperature;
        private static readonly ArgKind P = ArgKind.Pressure;
        private static readonly ArgKind H = ArgKind.Enthalpy;
        private static readonly ArgKind S = ArgKind.Entropy;

        private static ArgKind[] A(params ArgKind[] kinds)
        {
            return kinds;
        }

        public static readonly IF97Function[] All = new IF97Function[]
        {
            // ---- saturation curve -----------------------------------------
            new IF97Function("IF97_Tsat_p",
                "Saturation temperature from pressure. IF97_Tsat_p(p_Pa) -> T [K]",
                A(P), Quantity.Temperature,
                delegate(double[] a) { return Api.Tsat97(a[0]); }),

            new IF97Function("IF97_psat_T",
                "Saturation pressure from temperature. IF97_psat_T(T_K) -> p [Pa]",
                A(T), Quantity.Pressure,
                delegate(double[] a) { return Api.psat97(a[0]); }),

            new IF97Function("IF97_sigma_T",
                "Surface tension from temperature. IF97_sigma_T(T_K) -> sigma [N/m]",
                A(T), Quantity.SurfaceTension,
                delegate(double[] a) { return Api.sigma97(a[0]); }),

            // ---- properties from (T, p) -----------------------------------
            new IF97Function("IF97_rho_Tp",
                "Density from temperature and pressure. IF97_rho_Tp(T_K, p_Pa) -> rho [kg/m3]",
                A(T, P), Quantity.Density,
                delegate(double[] a) { return Api.rhomass_Tp(a[0], a[1]); }),

            new IF97Function("IF97_v_Tp",
                "Specific volume from temperature and pressure. IF97_v_Tp(T_K, p_Pa) -> v [m3/kg]",
                A(T, P), Quantity.SpecificVolume,
                delegate(double[] a) { return Api.vmass_Tp(a[0], a[1]); }),

            new IF97Function("IF97_h_Tp",
                "Specific enthalpy from temperature and pressure. IF97_h_Tp(T_K, p_Pa) -> h [J/kg]",
                A(T, P), Quantity.SpecificEnergy,
                delegate(double[] a) { return Api.hmass_Tp(a[0], a[1]); }),

            new IF97Function("IF97_s_Tp",
                "Specific entropy from temperature and pressure. IF97_s_Tp(T_K, p_Pa) -> s [J/(kg*K)]",
                A(T, P), Quantity.SpecificEntropy,
                delegate(double[] a) { return Api.smass_Tp(a[0], a[1]); }),

            new IF97Function("IF97_u_Tp",
                "Specific internal energy from temperature and pressure. IF97_u_Tp(T_K, p_Pa) -> u [J/kg]",
                A(T, P), Quantity.SpecificEnergy,
                delegate(double[] a) { return Api.umass_Tp(a[0], a[1]); }),

            new IF97Function("IF97_cp_Tp",
                "Isobaric heat capacity from temperature and pressure. IF97_cp_Tp(T_K, p_Pa) -> cp [J/(kg*K)]",
                A(T, P), Quantity.SpecificEntropy,
                delegate(double[] a) { return Api.cpmass_Tp(a[0], a[1]); }),

            new IF97Function("IF97_cv_Tp",
                "Isochoric heat capacity from temperature and pressure. IF97_cv_Tp(T_K, p_Pa) -> cv [J/(kg*K)]",
                A(T, P), Quantity.SpecificEntropy,
                delegate(double[] a) { return Api.cvmass_Tp(a[0], a[1]); }),

            new IF97Function("IF97_w_Tp",
                "Speed of sound from temperature and pressure. IF97_w_Tp(T_K, p_Pa) -> w [m/s]",
                A(T, P), Quantity.Velocity,
                delegate(double[] a) { return Api.speed_sound_Tp(a[0], a[1]); }),

            new IF97Function("IF97_mu_Tp",
                "Dynamic viscosity from temperature and pressure. IF97_mu_Tp(T_K, p_Pa) -> mu [Pa*s]",
                A(T, P), Quantity.DynamicViscosity,
                delegate(double[] a) { return Api.visc_Tp(a[0], a[1]); }),

            new IF97Function("IF97_tc_Tp",
                "Thermal conductivity from temperature and pressure. IF97_tc_Tp(T_K, p_Pa) -> tc [W/(m*K)]",
                A(T, P), Quantity.ThermalConductivity,
                delegate(double[] a) { return Api.tcond_Tp(a[0], a[1]); }),

            new IF97Function("IF97_Pr_Tp",
                "Prandtl number from temperature and pressure. IF97_Pr_Tp(T_K, p_Pa) -> Pr [-]",
                A(T, P), Quantity.Dimensionless,
                delegate(double[] a) { return Api.prandtl_Tp(a[0], a[1]); }),

            // ---- saturated liquid, from pressure --------------------------
            new IF97Function("IF97_rholiq_p",
                "Saturated liquid density. IF97_rholiq_p(p_Pa) -> rho [kg/m3]",
                A(P), Quantity.Density,
                delegate(double[] a) { return Api.rholiq_p(a[0]); }),

            new IF97Function("IF97_hliq_p",
                "Saturated liquid enthalpy. IF97_hliq_p(p_Pa) -> h [J/kg]",
                A(P), Quantity.SpecificEnergy,
                delegate(double[] a) { return Api.hliq_p(a[0]); }),

            new IF97Function("IF97_sliq_p",
                "Saturated liquid entropy. IF97_sliq_p(p_Pa) -> s [J/(kg*K)]",
                A(P), Quantity.SpecificEntropy,
                delegate(double[] a) { return Api.sliq_p(a[0]); }),

            new IF97Function("IF97_uliq_p",
                "Saturated liquid internal energy. IF97_uliq_p(p_Pa) -> u [J/kg]",
                A(P), Quantity.SpecificEnergy,
                delegate(double[] a) { return Api.uliq_p(a[0]); }),

            new IF97Function("IF97_cpliq_p",
                "Saturated liquid isobaric heat capacity. IF97_cpliq_p(p_Pa) -> cp [J/(kg*K)]",
                A(P), Quantity.SpecificEntropy,
                delegate(double[] a) { return Api.cpliq_p(a[0]); }),

            new IF97Function("IF97_cvliq_p",
                "Saturated liquid isochoric heat capacity. IF97_cvliq_p(p_Pa) -> cv [J/(kg*K)]",
                A(P), Quantity.SpecificEntropy,
                delegate(double[] a) { return Api.cvliq_p(a[0]); }),

            new IF97Function("IF97_wliq_p",
                "Saturated liquid speed of sound. IF97_wliq_p(p_Pa) -> w [m/s]",
                A(P), Quantity.Velocity,
                delegate(double[] a) { return Api.speed_soundliq_p(a[0]); }),

            new IF97Function("IF97_muliq_p",
                "Saturated liquid dynamic viscosity. IF97_muliq_p(p_Pa) -> mu [Pa*s]",
                A(P), Quantity.DynamicViscosity,
                delegate(double[] a) { return Api.viscliq_p(a[0]); }),

            new IF97Function("IF97_tcliq_p",
                "Saturated liquid thermal conductivity. IF97_tcliq_p(p_Pa) -> tc [W/(m*K)]",
                A(P), Quantity.ThermalConductivity,
                delegate(double[] a) { return Api.tcondliq_p(a[0]); }),

            new IF97Function("IF97_Prliq_p",
                "Saturated liquid Prandtl number. IF97_Prliq_p(p_Pa) -> Pr [-]",
                A(P), Quantity.Dimensionless,
                delegate(double[] a) { return Api.prandtlliq_p(a[0]); }),

            // ---- saturated vapour, from pressure --------------------------
            new IF97Function("IF97_rhovap_p",
                "Saturated vapour density. IF97_rhovap_p(p_Pa) -> rho [kg/m3]",
                A(P), Quantity.Density,
                delegate(double[] a) { return Api.rhovap_p(a[0]); }),

            new IF97Function("IF97_hvap_p",
                "Saturated vapour enthalpy. IF97_hvap_p(p_Pa) -> h [J/kg]",
                A(P), Quantity.SpecificEnergy,
                delegate(double[] a) { return Api.hvap_p(a[0]); }),

            new IF97Function("IF97_svap_p",
                "Saturated vapour entropy. IF97_svap_p(p_Pa) -> s [J/(kg*K)]",
                A(P), Quantity.SpecificEntropy,
                delegate(double[] a) { return Api.svap_p(a[0]); }),

            new IF97Function("IF97_uvap_p",
                "Saturated vapour internal energy. IF97_uvap_p(p_Pa) -> u [J/kg]",
                A(P), Quantity.SpecificEnergy,
                delegate(double[] a) { return Api.uvap_p(a[0]); }),

            new IF97Function("IF97_cpvap_p",
                "Saturated vapour isobaric heat capacity. IF97_cpvap_p(p_Pa) -> cp [J/(kg*K)]",
                A(P), Quantity.SpecificEntropy,
                delegate(double[] a) { return Api.cpvap_p(a[0]); }),

            new IF97Function("IF97_cvvap_p",
                "Saturated vapour isochoric heat capacity. IF97_cvvap_p(p_Pa) -> cv [J/(kg*K)]",
                A(P), Quantity.SpecificEntropy,
                delegate(double[] a) { return Api.cvvap_p(a[0]); }),

            new IF97Function("IF97_wvap_p",
                "Saturated vapour speed of sound. IF97_wvap_p(p_Pa) -> w [m/s]",
                A(P), Quantity.Velocity,
                delegate(double[] a) { return Api.speed_soundvap_p(a[0]); }),

            new IF97Function("IF97_muvap_p",
                "Saturated vapour dynamic viscosity. IF97_muvap_p(p_Pa) -> mu [Pa*s]",
                A(P), Quantity.DynamicViscosity,
                delegate(double[] a) { return Api.viscvap_p(a[0]); }),

            new IF97Function("IF97_tcvap_p",
                "Saturated vapour thermal conductivity. IF97_tcvap_p(p_Pa) -> tc [W/(m*K)]",
                A(P), Quantity.ThermalConductivity,
                delegate(double[] a) { return Api.tcondvap_p(a[0]); }),

            new IF97Function("IF97_Prvap_p",
                "Saturated vapour Prandtl number. IF97_Prvap_p(p_Pa) -> Pr [-]",
                A(P), Quantity.Dimensionless,
                delegate(double[] a) { return Api.prandtlvap_p(a[0]); }),

            // ---- latent heat ----------------------------------------------
            new IF97Function("IF97_hfg_p",
                "Latent heat of vaporisation from pressure. IF97_hfg_p(p_Pa) -> h_fg [J/kg]",
                A(P), Quantity.SpecificEnergy,
                delegate(double[] a) { return Api.latentheat_p(a[0]); }),

            new IF97Function("IF97_hfg_T",
                "Latent heat of vaporisation from temperature. IF97_hfg_T(T_K) -> h_fg [J/kg]",
                A(T), Quantity.SpecificEnergy,
                delegate(double[] a) { return Api.latentheat_T(a[0]); }),

            // ---- saturated properties from temperature --------------------
            new IF97Function("IF97_hliq_T",
                "Saturated liquid enthalpy from temperature. IF97_hliq_T(T_K) -> h [J/kg]",
                A(T), Quantity.SpecificEnergy,
                delegate(double[] a) { return Api.hliq_T(a[0]); }),

            new IF97Function("IF97_hvap_T",
                "Saturated vapour enthalpy from temperature. IF97_hvap_T(T_K) -> h [J/kg]",
                A(T), Quantity.SpecificEnergy,
                delegate(double[] a) { return Api.hvap_T(a[0]); }),

            // ---- backward equations ---------------------------------------
            new IF97Function("IF97_T_ph",
                "Temperature from pressure and enthalpy. IF97_T_ph(p_Pa, h_Jkg) -> T [K]",
                A(P, H), Quantity.Temperature,
                delegate(double[] a) { return Api.T_phmass(a[0], a[1]); }),

            new IF97Function("IF97_rho_ph",
                "Density from pressure and enthalpy. IF97_rho_ph(p_Pa, h_Jkg) -> rho [kg/m3]",
                A(P, H), Quantity.Density,
                delegate(double[] a) { return Api.rhomass_phmass(a[0], a[1]); }),

            new IF97Function("IF97_T_ps",
                "Temperature from pressure and entropy. IF97_T_ps(p_Pa, s_JkgK) -> T [K]",
                A(P, S), Quantity.Temperature,
                delegate(double[] a) { return Api.T_psmass(a[0], a[1]); }),

            new IF97Function("IF97_rho_ps",
                "Density from pressure and entropy. IF97_rho_ps(p_Pa, s_JkgK) -> rho [kg/m3]",
                A(P, S), Quantity.Density,
                delegate(double[] a) { return Api.rhomass_psmass(a[0], a[1]); }),

            new IF97Function("IF97_p_hs",
                "Pressure from enthalpy and entropy. IF97_p_hs(h_Jkg, s_JkgK) -> p [Pa]",
                A(H, S), Quantity.Pressure,
                delegate(double[] a) { return Api.p_hsmass(a[0], a[1]); }),

            new IF97Function("IF97_T_hs",
                "Temperature from enthalpy and entropy. IF97_T_hs(h_Jkg, s_JkgK) -> T [K]",
                A(H, S), Quantity.Temperature,
                delegate(double[] a) { return Api.T_hsmass(a[0], a[1]); }),

            new IF97Function("IF97_Region_ph",
                "IF97 region number from pressure and enthalpy. IF97_Region_ph(p_Pa, h_Jkg) -> 1..5",
                A(P, H), Quantity.Dimensionless,
                delegate(double[] a) { return Api.Region_ph(a[0], a[1]); }),

            new IF97Function("IF97_Region_ps",
                "IF97 region number from pressure and entropy. IF97_Region_ps(p_Pa, s_JkgK) -> 1..5",
                A(P, S), Quantity.Dimensionless,
                delegate(double[] a) { return Api.Region_ps(a[0], a[1]); }),
        };
    }
}
