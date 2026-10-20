/*
 * PsychroLib SMath Plugin — Function Definitions
 *
 * The plugin is unit-aware and works entirely in SI base units, matching how
 * SMath itself behaves:
 *
 *   INPUTS  (what the worksheet supplies)
 *     temperature      K        -> converted to degrees Celsius for PsychroLib
 *     pressure         Pa       -> passed straight through
 *     enthalpy         J/kg     -> passed straight through
 *     humidity ratio   kg/kg    -> passed straight through (dimensionless)
 *     altitude         m        -> passed straight through
 *
 *   OUTPUTS (value in base units + matching unit expression attached)
 *     temperature      K        -> tagged 'K
 *     pressure         Pa       -> tagged 'kg/('m*'s^2)
 *     enthalpy         J/kg     -> tagged 'm^2/'s^2
 *     density          kg/m3    -> tagged 'kg/'m^3
 *     specific volume  m3/kg    -> tagged 'm^3/'kg
 *     ratios           -        -> dimensionless, Units left at 1
 *
 * PsychroLib's SI layer takes Celsius while SMath works in kelvin, which is the
 * only conversion this plugin performs. Callers therefore write
 *
 *     Psy_Enthalpy(25*'°C, 0.00988)      or      Psy_Enthalpy(298.15*'K, 0.00988)
 *
 * and get a real quantity back that SMath can contract to kJ/kg for display.
 */

using System;
using SMath.Manager;
using SMath.Math;
using SMath.Math.Numeric;

namespace PsychroLibSMath
{
    /// <summary>Unit kind of one input argument, in SI base units.</summary>
    internal enum ArgKind
    {
        /// <summary>Already SI: Pa, kg/kg, J/kg, m, m3/kg.</summary>
        Plain,

        /// <summary>Kelvin at the boundary; degrees Celsius inside PsychroLib.</summary>
        Temperature
    }

    /// <summary>
    /// Base class for all PsychroLib SMath functions.
    ///
    /// A subclass supplies only its identity, its argument kinds, its output
    /// quantity and the psychrometric call itself. The unit conversion, the
    /// NaN containment and the unit tagging all happen here, once.
    /// </summary>
    internal abstract class PsychroFunctionBase
    {
        /// <summary>Offset between kelvin and degree Celsius.</summary>
        public const double ZeroCelsius = 273.15;

        private readonly ArgKind[] _argKinds;
        private readonly Quantity _output;
        private ArgumentInfo[] _args;

        protected PsychroFunctionBase(ArgKind[] argKinds, Quantity output)
        {
            _argKinds = argKinds;
            _output = output;
        }

        public abstract string Name { get; }
        public abstract string Description { get; }

        public ArgKind[] ArgKinds { get { return _argKinds; } }
        public Quantity Output { get { return _output; } }
        public int ArgsCount { get { return _argKinds.Length; } }

        public ArgumentInfo[] Args
        {
            get
            {
                if (_args == null)
                {
                    ArgumentInfo[] a = new ArgumentInfo[_argKinds.Length];
                    for (int i = 0; i < a.Length; i++)
                        a[i] = Arg.Real();
                    _args = a;
                }
                return _args;
            }
        }

        public TermInfo GetTermInfo()
        {
            return new TermInfo(Name, ArgsCount == 0 ? TermType.Operand : TermType.Function,
                                Description, FunctionSections.Unknown, true, Args);
        }

        public bool IsSupport(Term value)
        {
            return value.Text.Equals(Name) && value.ArgsCount == ArgsCount;
        }

        /// <summary>
        /// The psychrometric call itself, in the units PsychroLib expects:
        /// degrees Celsius, pascal, J/kg, m3/kg, dimensionless ratios.
        /// </summary>
        protected abstract double Compute(double[] a);

        /// <summary>
        /// SI base units in, SI base units out. Uses no SMath types, so the whole
        /// conversion chain is testable outside a live session.
        /// </summary>
        public double EvaluateSi(double[] siArgs)
        {
            double[] a = new double[siArgs.Length];
            for (int i = 0; i < siArgs.Length; i++)
                a[i] = _argKinds[i] == ArgKind.Temperature ? siArgs[i] - ZeroCelsius : siArgs[i];

            double value;
            try
            {
                value = Compute(a);
            }
            catch (Exception)
            {
                // PsychroLib throws outside its valid ranges; a worksheet cell
                // should show NaN, not an error dialog.
                return double.NaN;
            }

            return _output == Quantity.Temperature ? value + ZeroCelsius : value;
        }

        public bool NumericEvaluation(Term value, TNumber[] args, ref TNumber result)
        {
            if (args == null || args.Length != ArgsCount)
                return false;

            double[] si = new double[args.Length];
            for (int i = 0; i < si.Length; i++)
                si[i] = Psy.D(args[i]);

            result = new TNumber(EvaluateSi(si));
            UnitExpressions.Attach(result, _output);
            return true;
        }
    }

    internal static class Arg
    {
        public static ArgumentInfo Real() { return new ArgumentInfo(ArgumentSections.RealNumber); }
    }

    /// <summary>
    /// All PsychroLib SMath functions. SI base units at the boundary.
    /// </summary>
    internal static class PsychroFunctions
    {
        private const ArgKind T = ArgKind.Temperature;   // kelvin at the boundary
        private const ArgKind P = ArgKind.Plain;         // already SI

        private static ArgKind[] A(params ArgKind[] kinds) { return kinds; }

        // ===== Temperature conversions =====
        //
        // Kept for compatibility with earlier versions of this plugin. They are
        // plain-number helpers: SMath converts units natively, so 25*'°C already
        // is 298.15 K and needs no function call.

        internal sealed class CelsiusToKelvinFn : PsychroFunctionBase
        {
            public CelsiusToKelvinFn() : base(A(P), Quantity.Temperature) { }
            public override string Name { get { return "Psy_CelsiusToKelvin"; } }
            public override string Description { get { return "Convert a plain Celsius reading to absolute temperature. Psy_CelsiusToKelvin(T_C) -> K. In a worksheet prefer 25*'C: SMath converts temperature units natively."; } }
            // The argument is a Celsius reading and the base class converts a
            // Celsius result to kelvin, so passing the value through unchanged
            // is exactly the conversion. Do not call GetTKelvinFromTCelsius
            // here: that would add the offset twice.
            protected override double Compute(double[] a) { return a[0]; }
        }

        internal sealed class KelvinToCelsiusFn : PsychroFunctionBase
        {
            public KelvinToCelsiusFn() : base(A(P), Quantity.Dimensionless) { }
            public override string Name { get { return "Psy_KelvinToCelsius"; } }
            public override string Description { get { return "Convert an absolute temperature to a Celsius reading. Psy_KelvinToCelsius(T_K) -> C"; } }
            protected override double Compute(double[] a) { return Psy.SI.GetTCelsiusFromTKelvin(a[0]); }
        }

        // ===== Humidity ratio =====

        internal sealed class HumRatioFromRelHumFn : PsychroFunctionBase
        {
            public HumRatioFromRelHumFn() : base(A(T, P, P), Quantity.Dimensionless) { }
            public override string Name { get { return "Psy_HumRatio_RH"; } }
            public override string Description { get { return "Humidity ratio from dry-bulb temperature, relative humidity and pressure. Psy_HumRatio_RH(T_dry_K, RH, P_Pa) -> kg/kg"; } }
            protected override double Compute(double[] a) { return Psy.SI.GetHumRatioFromRelHum(a[0], a[1], a[2]); }
        }

        internal sealed class HumRatioFromTWetBulbFn : PsychroFunctionBase
        {
            public HumRatioFromTWetBulbFn() : base(A(T, T, P), Quantity.Dimensionless) { }
            public override string Name { get { return "Psy_HumRatio_TWetBulb"; } }
            public override string Description { get { return "Humidity ratio from dry-bulb and wet-bulb temperature and pressure. Psy_HumRatio_TWetBulb(T_dry_K, T_wet_K, P_Pa) -> kg/kg"; } }
            protected override double Compute(double[] a) { return Psy.SI.GetHumRatioFromTWetBulb(a[0], a[1], a[2]); }
        }

        internal sealed class HumRatioFromTDewPointFn : PsychroFunctionBase
        {
            public HumRatioFromTDewPointFn() : base(A(T, P), Quantity.Dimensionless) { }
            public override string Name { get { return "Psy_HumRatio_TDewPoint"; } }
            public override string Description { get { return "Humidity ratio from dew-point temperature and pressure. Psy_HumRatio_TDewPoint(T_dew_K, P_Pa) -> kg/kg"; } }
            protected override double Compute(double[] a) { return Psy.SI.GetHumRatioFromTDewPoint(a[0], a[1]); }
        }

        internal sealed class HumRatioFromVapPresFn : PsychroFunctionBase
        {
            public HumRatioFromVapPresFn() : base(A(P, P), Quantity.Dimensionless) { }
            public override string Name { get { return "Psy_HumRatio_VapPres"; } }
            public override string Description { get { return "Humidity ratio from vapour pressure and total pressure. Psy_HumRatio_VapPres(P_vap_Pa, P_Pa) -> kg/kg"; } }
            protected override double Compute(double[] a) { return Psy.SI.GetHumRatioFromVapPres(a[0], a[1]); }
        }

        internal sealed class HumRatioFromSpecificHumFn : PsychroFunctionBase
        {
            public HumRatioFromSpecificHumFn() : base(A(P), Quantity.Dimensionless) { }
            public override string Name { get { return "Psy_HumRatio_SpecHum"; } }
            public override string Description { get { return "Humidity ratio from specific humidity. Psy_HumRatio_SpecHum(specific_humidity) -> kg/kg"; } }
            protected override double Compute(double[] a) { return Psy.SI.GetHumRatioFromSpecificHum(a[0]); }
        }

        internal sealed class HumRatioFromEnthalpyFn : PsychroFunctionBase
        {
            public HumRatioFromEnthalpyFn() : base(A(P, T), Quantity.Dimensionless) { }
            public override string Name { get { return "Psy_HumRatio_Enthalpy"; } }
            public override string Description { get { return "Humidity ratio from moist air enthalpy and dry-bulb temperature. Psy_HumRatio_Enthalpy(h_J_kg, T_dry_K) -> kg/kg"; } }
            protected override double Compute(double[] a) { return Psy.SI.GetHumRatioFromEnthalpyAndTDryBulb(a[0], a[1]); }
        }

        // ===== Dew point =====

        internal sealed class TDewPointFromRelHumFn : PsychroFunctionBase
        {
            public TDewPointFromRelHumFn() : base(A(T, P), Quantity.Temperature) { }
            public override string Name { get { return "Psy_TDewPoint_RH"; } }
            public override string Description { get { return "Dew-point temperature from dry-bulb temperature and relative humidity. Psy_TDewPoint_RH(T_dry_K, RH) -> K"; } }
            protected override double Compute(double[] a) { return Psy.SI.GetTDewPointFromRelHum(a[0], a[1]); }
        }

        internal sealed class TDewPointFromTWetBulbFn : PsychroFunctionBase
        {
            public TDewPointFromTWetBulbFn() : base(A(T, T, P), Quantity.Temperature) { }
            public override string Name { get { return "Psy_TDewPoint_TWetBulb"; } }
            public override string Description { get { return "Dew-point temperature from dry-bulb and wet-bulb temperature and pressure. Psy_TDewPoint_TWetBulb(T_dry_K, T_wet_K, P_Pa) -> K"; } }
            protected override double Compute(double[] a) { return Psy.SI.GetTDewPointFromTWetBulb(a[0], a[1], a[2]); }
        }

        internal sealed class TDewPointFromVapPresFn : PsychroFunctionBase
        {
            public TDewPointFromVapPresFn() : base(A(T, P), Quantity.Temperature) { }
            public override string Name { get { return "Psy_TDewPoint_VapPres"; } }
            public override string Description { get { return "Dew-point temperature from dry-bulb temperature and vapour pressure. Psy_TDewPoint_VapPres(T_dry_K, P_vap_Pa) -> K"; } }
            protected override double Compute(double[] a) { return Psy.SI.GetTDewPointFromVapPres(a[0], a[1]); }
        }

        internal sealed class TDewPointFromHumRatioFn : PsychroFunctionBase
        {
            public TDewPointFromHumRatioFn() : base(A(T, P, P), Quantity.Temperature) { }
            public override string Name { get { return "Psy_TDewPoint_HumRatio"; } }
            public override string Description { get { return "Dew-point temperature from dry-bulb temperature, humidity ratio and pressure. Psy_TDewPoint_HumRatio(T_dry_K, HumRatio, P_Pa) -> K"; } }
            protected override double Compute(double[] a) { return Psy.SI.GetTDewPointFromHumRatio(a[0], a[1], a[2]); }
        }

        // ===== Wet bulb =====

        internal sealed class TWetBulbFromTDewPointFn : PsychroFunctionBase
        {
            public TWetBulbFromTDewPointFn() : base(A(T, T, P), Quantity.Temperature) { }
            public override string Name { get { return "Psy_TWetBulb_TDewPoint"; } }
            public override string Description { get { return "Wet-bulb temperature from dry-bulb and dew-point temperature and pressure. Psy_TWetBulb_TDewPoint(T_dry_K, T_dew_K, P_Pa) -> K"; } }
            protected override double Compute(double[] a) { return Psy.SI.GetTWetBulbFromTDewPoint(a[0], a[1], a[2]); }
        }

        internal sealed class TWetBulbFromRelHumFn : PsychroFunctionBase
        {
            public TWetBulbFromRelHumFn() : base(A(T, P, P), Quantity.Temperature) { }
            public override string Name { get { return "Psy_TWetBulb_RH"; } }
            public override string Description { get { return "Wet-bulb temperature from dry-bulb temperature, relative humidity and pressure. Psy_TWetBulb_RH(T_dry_K, RH, P_Pa) -> K"; } }
            protected override double Compute(double[] a) { return Psy.SI.GetTWetBulbFromRelHum(a[0], a[1], a[2]); }
        }

        internal sealed class TWetBulbFromHumRatioFn : PsychroFunctionBase
        {
            public TWetBulbFromHumRatioFn() : base(A(T, P, P), Quantity.Temperature) { }
            public override string Name { get { return "Psy_TWetBulb_HumRatio"; } }
            public override string Description { get { return "Wet-bulb temperature from dry-bulb temperature, humidity ratio and pressure. Psy_TWetBulb_HumRatio(T_dry_K, HumRatio, P_Pa) -> K"; } }
            protected override double Compute(double[] a) { return Psy.SI.GetTWetBulbFromHumRatio(a[0], a[1], a[2]); }
        }

        // ===== Relative humidity =====

        internal sealed class RelHumFromTDewPointFn : PsychroFunctionBase
        {
            public RelHumFromTDewPointFn() : base(A(T, T), Quantity.Dimensionless) { }
            public override string Name { get { return "Psy_RH_TDewPoint"; } }
            public override string Description { get { return "Relative humidity from dry-bulb and dew-point temperature. Psy_RH_TDewPoint(T_dry_K, T_dew_K) -> 0..1"; } }
            protected override double Compute(double[] a) { return Psy.SI.GetRelHumFromTDewPoint(a[0], a[1]); }
        }

        internal sealed class RelHumFromTWetBulbFn : PsychroFunctionBase
        {
            public RelHumFromTWetBulbFn() : base(A(T, T, P), Quantity.Dimensionless) { }
            public override string Name { get { return "Psy_RH_TWetBulb"; } }
            public override string Description { get { return "Relative humidity from dry-bulb and wet-bulb temperature and pressure. Psy_RH_TWetBulb(T_dry_K, T_wet_K, P_Pa) -> 0..1"; } }
            protected override double Compute(double[] a) { return Psy.SI.GetRelHumFromTWetBulb(a[0], a[1], a[2]); }
        }

        internal sealed class RelHumFromVapPresFn : PsychroFunctionBase
        {
            public RelHumFromVapPresFn() : base(A(T, P), Quantity.Dimensionless) { }
            public override string Name { get { return "Psy_RH_VapPres"; } }
            public override string Description { get { return "Relative humidity from dry-bulb temperature and vapour pressure. Psy_RH_VapPres(T_dry_K, P_vap_Pa) -> 0..1"; } }
            protected override double Compute(double[] a) { return Psy.SI.GetRelHumFromVapPres(a[0], a[1]); }
        }

        internal sealed class RelHumFromHumRatioFn : PsychroFunctionBase
        {
            public RelHumFromHumRatioFn() : base(A(T, P, P), Quantity.Dimensionless) { }
            public override string Name { get { return "Psy_RH_HumRatio"; } }
            public override string Description { get { return "Relative humidity from dry-bulb temperature, humidity ratio and pressure. Psy_RH_HumRatio(T_dry_K, HumRatio, P_Pa) -> 0..1"; } }
            protected override double Compute(double[] a) { return Psy.SI.GetRelHumFromHumRatio(a[0], a[1], a[2]); }
        }

        // ===== Vapour pressure =====

        internal sealed class VapPresFromRelHumFn : PsychroFunctionBase
        {
            public VapPresFromRelHumFn() : base(A(T, P), Quantity.Pressure) { }
            public override string Name { get { return "Psy_VapPres_RH"; } }
            public override string Description { get { return "Vapour pressure from dry-bulb temperature and relative humidity. Psy_VapPres_RH(T_dry_K, RH) -> Pa"; } }
            protected override double Compute(double[] a) { return Psy.SI.GetVapPresFromRelHum(a[0], a[1]); }
        }

        internal sealed class VapPresFromTDewPointFn : PsychroFunctionBase
        {
            public VapPresFromTDewPointFn() : base(A(T), Quantity.Pressure) { }
            public override string Name { get { return "Psy_VapPres_TDewPoint"; } }
            public override string Description { get { return "Vapour pressure from dew-point temperature. Psy_VapPres_TDewPoint(T_dew_K) -> Pa"; } }
            protected override double Compute(double[] a) { return Psy.SI.GetVapPresFromTDewPoint(a[0]); }
        }

        internal sealed class VapPresFromHumRatioFn : PsychroFunctionBase
        {
            public VapPresFromHumRatioFn() : base(A(P, P), Quantity.Pressure) { }
            public override string Name { get { return "Psy_VapPres_HumRatio"; } }
            public override string Description { get { return "Vapour pressure from humidity ratio and total pressure. Psy_VapPres_HumRatio(HumRatio, P_Pa) -> Pa"; } }
            protected override double Compute(double[] a) { return Psy.SI.GetVapPresFromHumRatio(a[0], a[1]); }
        }

        // ===== Specific humidity =====

        internal sealed class SpecificHumFromHumRatioFn : PsychroFunctionBase
        {
            public SpecificHumFromHumRatioFn() : base(A(P), Quantity.Dimensionless) { }
            public override string Name { get { return "Psy_SpecHum"; } }
            public override string Description { get { return "Specific humidity from humidity ratio. Psy_SpecHum(HumRatio) -> kg/kg"; } }
            protected override double Compute(double[] a) { return Psy.SI.GetSpecificHumFromHumRatio(a[0]); }
        }

        // ===== Enthalpy =====

        internal sealed class MoistAirEnthalpyFn : PsychroFunctionBase
        {
            public MoistAirEnthalpyFn() : base(A(T, P), Quantity.SpecificEnergy) { }
            public override string Name { get { return "Psy_Enthalpy"; } }
            public override string Description { get { return "Moist air enthalpy from dry-bulb temperature and humidity ratio. Psy_Enthalpy(T_dry_K, HumRatio) -> J/kg"; } }
            protected override double Compute(double[] a) { return Psy.SI.GetMoistAirEnthalpy(a[0], a[1]); }
        }

        internal sealed class DryAirEnthalpyFn : PsychroFunctionBase
        {
            public DryAirEnthalpyFn() : base(A(T), Quantity.SpecificEnergy) { }
            public override string Name { get { return "Psy_DryAirEnthalpy"; } }
            public override string Description { get { return "Dry air enthalpy from dry-bulb temperature. Psy_DryAirEnthalpy(T_dry_K) -> J/kg"; } }
            protected override double Compute(double[] a) { return Psy.SI.GetDryAirEnthalpy(a[0]); }
        }

        internal sealed class TDryBulbFromEnthalpyFn : PsychroFunctionBase
        {
            public TDryBulbFromEnthalpyFn() : base(A(P, P), Quantity.Temperature) { }
            public override string Name { get { return "Psy_TDryBulb_Enthalpy"; } }
            public override string Description { get { return "Dry-bulb temperature from moist air enthalpy and humidity ratio. Psy_TDryBulb_Enthalpy(h_J_kg, HumRatio) -> K"; } }
            protected override double Compute(double[] a) { return Psy.SI.GetTDryBulbFromEnthalpyAndHumRatio(a[0], a[1]); }
        }

        // ===== Density and volume =====

        internal sealed class MoistAirDensityFn : PsychroFunctionBase
        {
            public MoistAirDensityFn() : base(A(T, P, P), Quantity.Density) { }
            public override string Name { get { return "Psy_AirDensity"; } }
            public override string Description { get { return "Moist air density from dry-bulb temperature, humidity ratio and pressure. Psy_AirDensity(T_dry_K, HumRatio, P_Pa) -> kg/m3"; } }
            protected override double Compute(double[] a) { return Psy.SI.GetMoistAirDensity(a[0], a[1], a[2]); }
        }

        internal sealed class MoistAirVolumeFn : PsychroFunctionBase
        {
            public MoistAirVolumeFn() : base(A(T, P, P), Quantity.SpecificVolume) { }
            public override string Name { get { return "Psy_AirVolume"; } }
            public override string Description { get { return "Moist air specific volume from dry-bulb temperature, humidity ratio and pressure. Psy_AirVolume(T_dry_K, HumRatio, P_Pa) -> m3/kg"; } }
            protected override double Compute(double[] a) { return Psy.SI.GetMoistAirVolume(a[0], a[1], a[2]); }
        }

        internal sealed class DryAirDensityFn : PsychroFunctionBase
        {
            public DryAirDensityFn() : base(A(T, P), Quantity.Density) { }
            public override string Name { get { return "Psy_DryAirDensity"; } }
            public override string Description { get { return "Dry air density from dry-bulb temperature and pressure. Psy_DryAirDensity(T_dry_K, P_Pa) -> kg/m3"; } }
            protected override double Compute(double[] a) { return Psy.SI.GetDryAirDensity(a[0], a[1]); }
        }

        internal sealed class DryAirVolumeFn : PsychroFunctionBase
        {
            public DryAirVolumeFn() : base(A(T, P), Quantity.SpecificVolume) { }
            public override string Name { get { return "Psy_DryAirVolume"; } }
            public override string Description { get { return "Dry air specific volume from dry-bulb temperature and pressure. Psy_DryAirVolume(T_dry_K, P_Pa) -> m3/kg"; } }
            protected override double Compute(double[] a) { return Psy.SI.GetDryAirVolume(a[0], a[1]); }
        }

        internal sealed class TDryBulbFromVolumeFn : PsychroFunctionBase
        {
            public TDryBulbFromVolumeFn() : base(A(P, P, P), Quantity.Temperature) { }
            public override string Name { get { return "Psy_TDryBulb_Volume"; } }
            public override string Description { get { return "Dry-bulb temperature from moist air specific volume, humidity ratio and pressure. Psy_TDryBulb_Volume(v_m3kg, HumRatio, P_Pa) -> K"; } }
            protected override double Compute(double[] a) { return Psy.SI.GetTDryBulbFromMoistAirVolumeAndHumRatio(a[0], a[1], a[2]); }
        }

        // ===== Saturation =====

        internal sealed class SatVapPresFn : PsychroFunctionBase
        {
            public SatVapPresFn() : base(A(T), Quantity.Pressure) { }
            public override string Name { get { return "Psy_SatVapPres"; } }
            public override string Description { get { return "Saturation vapour pressure from dry-bulb temperature. Psy_SatVapPres(T_dry_K) -> Pa"; } }
            protected override double Compute(double[] a) { return Psy.SI.GetSatVapPres(a[0]); }
        }

        internal sealed class SatHumRatioFn : PsychroFunctionBase
        {
            public SatHumRatioFn() : base(A(T, P), Quantity.Dimensionless) { }
            public override string Name { get { return "Psy_SatHumRatio"; } }
            public override string Description { get { return "Saturation humidity ratio from dry-bulb temperature and pressure. Psy_SatHumRatio(T_dry_K, P_Pa) -> kg/kg"; } }
            protected override double Compute(double[] a) { return Psy.SI.GetSatHumRatio(a[0], a[1]); }
        }

        internal sealed class SatAirEnthalpyFn : PsychroFunctionBase
        {
            public SatAirEnthalpyFn() : base(A(T, P), Quantity.SpecificEnergy) { }
            public override string Name { get { return "Psy_SatEnthalpy"; } }
            public override string Description { get { return "Saturated air enthalpy from dry-bulb temperature and pressure. Psy_SatEnthalpy(T_dry_K, P_Pa) -> J/kg"; } }
            protected override double Compute(double[] a) { return Psy.SI.GetSatAirEnthalpy(a[0], a[1]); }
        }

        // ===== Other =====

        internal sealed class VaporPressureDeficitFn : PsychroFunctionBase
        {
            public VaporPressureDeficitFn() : base(A(T, P, P), Quantity.Pressure) { }
            public override string Name { get { return "Psy_VPD"; } }
            public override string Description { get { return "Vapour pressure deficit from dry-bulb temperature, humidity ratio and pressure. Psy_VPD(T_dry_K, HumRatio, P_Pa) -> Pa"; } }
            protected override double Compute(double[] a) { return Psy.SI.GetVaporPressureDeficit(a[0], a[1], a[2]); }
        }

        internal sealed class DegreeOfSaturationFn : PsychroFunctionBase
        {
            public DegreeOfSaturationFn() : base(A(T, P, P), Quantity.Dimensionless) { }
            public override string Name { get { return "Psy_DegSaturation"; } }
            public override string Description { get { return "Degree of saturation from dry-bulb temperature, humidity ratio and pressure. Psy_DegSaturation(T_dry_K, HumRatio, P_Pa) -> 0..1"; } }
            protected override double Compute(double[] a) { return Psy.SI.GetDegreeOfSaturation(a[0], a[1], a[2]); }
        }

        // ===== Pressure =====

        internal sealed class StandardAtmPressureFn : PsychroFunctionBase
        {
            public StandardAtmPressureFn() : base(A(P), Quantity.Pressure) { }
            public override string Name { get { return "Psy_AtmPressure"; } }
            public override string Description { get { return "Standard atmospheric pressure from altitude. Psy_AtmPressure(altitude_m) -> Pa"; } }
            protected override double Compute(double[] a) { return Psy.SI.GetStandardAtmPressure(a[0]); }
        }

        internal sealed class StandardAtmTemperatureFn : PsychroFunctionBase
        {
            public StandardAtmTemperatureFn() : base(A(P), Quantity.Temperature) { }
            public override string Name { get { return "Psy_AtmTemperature"; } }
            public override string Description { get { return "Standard atmospheric temperature from altitude. Psy_AtmTemperature(altitude_m) -> K"; } }
            protected override double Compute(double[] a) { return Psy.SI.GetStandardAtmTemperature(a[0]); }
        }

        internal sealed class SeaLevelPressureFn : PsychroFunctionBase
        {
            public SeaLevelPressureFn() : base(A(P, P, T), Quantity.Pressure) { }
            public override string Name { get { return "Psy_SeaLevelPressure"; } }
            public override string Description { get { return "Sea-level pressure from station pressure, altitude and dry-bulb temperature. Psy_SeaLevelPressure(P_stn_Pa, altitude_m, T_dry_K) -> Pa"; } }
            protected override double Compute(double[] a) { return Psy.SI.GetSeaLevelPressure(a[0], a[1], a[2]); }
        }

        internal sealed class StationPressureFn : PsychroFunctionBase
        {
            public StationPressureFn() : base(A(P, P, T), Quantity.Pressure) { }
            public override string Name { get { return "Psy_StationPressure"; } }
            public override string Description { get { return "Station pressure from sea-level pressure, altitude and dry-bulb temperature. Psy_StationPressure(P_sea_Pa, altitude_m, T_dry_K) -> Pa"; } }
            protected override double Compute(double[] a) { return Psy.SI.GetStationPressure(a[0], a[1], a[2]); }
        }

        // ===== Static instances =====

        public static readonly CelsiusToKelvinFn CelsiusToKelvin = new CelsiusToKelvinFn();
        public static readonly KelvinToCelsiusFn KelvinToCelsius = new KelvinToCelsiusFn();
        public static readonly HumRatioFromRelHumFn HumRatioFromRelHum = new HumRatioFromRelHumFn();
        public static readonly HumRatioFromTWetBulbFn HumRatioFromTWetBulb = new HumRatioFromTWetBulbFn();
        public static readonly HumRatioFromTDewPointFn HumRatioFromTDewPoint = new HumRatioFromTDewPointFn();
        public static readonly HumRatioFromVapPresFn HumRatioFromVapPres = new HumRatioFromVapPresFn();
        public static readonly HumRatioFromSpecificHumFn HumRatioFromSpecificHum = new HumRatioFromSpecificHumFn();
        public static readonly HumRatioFromEnthalpyFn HumRatioFromEnthalpy = new HumRatioFromEnthalpyFn();
        public static readonly TDewPointFromRelHumFn TDewPointFromRelHum = new TDewPointFromRelHumFn();
        public static readonly TDewPointFromTWetBulbFn TDewPointFromTWetBulb = new TDewPointFromTWetBulbFn();
        public static readonly TDewPointFromVapPresFn TDewPointFromVapPres = new TDewPointFromVapPresFn();
        public static readonly TDewPointFromHumRatioFn TDewPointFromHumRatio = new TDewPointFromHumRatioFn();
        public static readonly TWetBulbFromTDewPointFn TWetBulbFromTDewPoint = new TWetBulbFromTDewPointFn();
        public static readonly TWetBulbFromRelHumFn TWetBulbFromRelHum = new TWetBulbFromRelHumFn();
        public static readonly TWetBulbFromHumRatioFn TWetBulbFromHumRatio = new TWetBulbFromHumRatioFn();
        public static readonly RelHumFromTDewPointFn RelHumFromTDewPoint = new RelHumFromTDewPointFn();
        public static readonly RelHumFromTWetBulbFn RelHumFromTWetBulb = new RelHumFromTWetBulbFn();
        public static readonly RelHumFromVapPresFn RelHumFromVapPres = new RelHumFromVapPresFn();
        public static readonly RelHumFromHumRatioFn RelHumFromHumRatio = new RelHumFromHumRatioFn();
        public static readonly VapPresFromRelHumFn VapPresFromRelHum = new VapPresFromRelHumFn();
        public static readonly VapPresFromTDewPointFn VapPresFromTDewPoint = new VapPresFromTDewPointFn();
        public static readonly VapPresFromHumRatioFn VapPresFromHumRatio = new VapPresFromHumRatioFn();
        public static readonly SpecificHumFromHumRatioFn SpecificHumFromHumRatio = new SpecificHumFromHumRatioFn();
        public static readonly MoistAirEnthalpyFn MoistAirEnthalpy = new MoistAirEnthalpyFn();
        public static readonly DryAirEnthalpyFn DryAirEnthalpy = new DryAirEnthalpyFn();
        public static readonly TDryBulbFromEnthalpyFn TDryBulbFromEnthalpy = new TDryBulbFromEnthalpyFn();
        public static readonly MoistAirDensityFn MoistAirDensity = new MoistAirDensityFn();
        public static readonly MoistAirVolumeFn MoistAirVolume = new MoistAirVolumeFn();
        public static readonly DryAirDensityFn DryAirDensity = new DryAirDensityFn();
        public static readonly DryAirVolumeFn DryAirVolume = new DryAirVolumeFn();
        public static readonly TDryBulbFromVolumeFn TDryBulbFromVolume = new TDryBulbFromVolumeFn();
        public static readonly SatVapPresFn SatVapPres = new SatVapPresFn();
        public static readonly SatHumRatioFn SatHumRatio = new SatHumRatioFn();
        public static readonly SatAirEnthalpyFn SatAirEnthalpy = new SatAirEnthalpyFn();
        public static readonly VaporPressureDeficitFn VaporPressureDeficit = new VaporPressureDeficitFn();
        public static readonly DegreeOfSaturationFn DegreeOfSaturation = new DegreeOfSaturationFn();
        public static readonly StandardAtmPressureFn StandardAtmPressure = new StandardAtmPressureFn();
        public static readonly StandardAtmTemperatureFn StandardAtmTemperature = new StandardAtmTemperatureFn();
        public static readonly SeaLevelPressureFn SeaLevelPressure = new SeaLevelPressureFn();
        public static readonly StationPressureFn StationPressure = new StationPressureFn();
    }
}
