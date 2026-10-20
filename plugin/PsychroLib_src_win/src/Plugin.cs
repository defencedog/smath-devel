/*
 * PsychroLib SMath Plugin
 * Exposes psychrometric functions from PsychroLib to SMath Studio.
 * SI base units at the boundary: K, Pa, kg/kg, J/kg, m3/kg, kg/m3.
 * Results carry their unit expression, so they contract natively in SMath.
 */

using System;
using SMath.Manager;
using SMath.Math;
using SMath.Math.Numeric;

namespace PsychroLibSMath
{
    public class Plugin : IPluginHandleEvaluation, IPluginMathNumericEvaluation
    {
        private TermInfo[] _termInfos;
        private AssemblyInfo[] _assemblyInfos;

        public void Initialize()
        {
            // No extra assembly dependencies: the plugin only needs SMath Studio
            // itself (the host) and the BCL, both of which are always present.
            // Keeping this empty matches the packaging of the reference plugin
            // and avoids AssemblyInfo's dependency constructor.
            _assemblyInfos = new AssemblyInfo[0];

            _termInfos = new TermInfo[]
            {
                // Temperature conversions
                PsychroFunctions.CelsiusToKelvin.GetTermInfo(),
                PsychroFunctions.KelvinToCelsius.GetTermInfo(),

                // Humidity ratio
                PsychroFunctions.HumRatioFromRelHum.GetTermInfo(),
                PsychroFunctions.HumRatioFromTWetBulb.GetTermInfo(),
                PsychroFunctions.HumRatioFromTDewPoint.GetTermInfo(),
                PsychroFunctions.HumRatioFromVapPres.GetTermInfo(),
                PsychroFunctions.HumRatioFromSpecificHum.GetTermInfo(),
                PsychroFunctions.HumRatioFromEnthalpy.GetTermInfo(),

                // Dew point
                PsychroFunctions.TDewPointFromRelHum.GetTermInfo(),
                PsychroFunctions.TDewPointFromTWetBulb.GetTermInfo(),
                PsychroFunctions.TDewPointFromVapPres.GetTermInfo(),
                PsychroFunctions.TDewPointFromHumRatio.GetTermInfo(),

                // Wet bulb
                PsychroFunctions.TWetBulbFromTDewPoint.GetTermInfo(),
                PsychroFunctions.TWetBulbFromRelHum.GetTermInfo(),
                PsychroFunctions.TWetBulbFromHumRatio.GetTermInfo(),

                // Relative humidity
                PsychroFunctions.RelHumFromTDewPoint.GetTermInfo(),
                PsychroFunctions.RelHumFromTWetBulb.GetTermInfo(),
                PsychroFunctions.RelHumFromVapPres.GetTermInfo(),
                PsychroFunctions.RelHumFromHumRatio.GetTermInfo(),

                // Vapour pressure
                PsychroFunctions.VapPresFromRelHum.GetTermInfo(),
                PsychroFunctions.VapPresFromTDewPoint.GetTermInfo(),
                PsychroFunctions.VapPresFromHumRatio.GetTermInfo(),

                // Specific humidity
                PsychroFunctions.SpecificHumFromHumRatio.GetTermInfo(),

                // Enthalpy
                PsychroFunctions.MoistAirEnthalpy.GetTermInfo(),
                PsychroFunctions.DryAirEnthalpy.GetTermInfo(),
                PsychroFunctions.TDryBulbFromEnthalpy.GetTermInfo(),

                // Density and volume
                PsychroFunctions.MoistAirDensity.GetTermInfo(),
                PsychroFunctions.MoistAirVolume.GetTermInfo(),
                PsychroFunctions.DryAirDensity.GetTermInfo(),
                PsychroFunctions.DryAirVolume.GetTermInfo(),
                PsychroFunctions.TDryBulbFromVolume.GetTermInfo(),

                // Saturation
                PsychroFunctions.SatVapPres.GetTermInfo(),
                PsychroFunctions.SatHumRatio.GetTermInfo(),
                PsychroFunctions.SatAirEnthalpy.GetTermInfo(),

                // Other
                PsychroFunctions.VaporPressureDeficit.GetTermInfo(),
                PsychroFunctions.DegreeOfSaturation.GetTermInfo(),

                // Pressure
                PsychroFunctions.StandardAtmPressure.GetTermInfo(),
                PsychroFunctions.StandardAtmTemperature.GetTermInfo(),
                PsychroFunctions.SeaLevelPressure.GetTermInfo(),
                PsychroFunctions.StationPressure.GetTermInfo(),
            };
        }

        public AssemblyInfo[] Dependences
        {
            get { return _assemblyInfos; }
        }

        public TermInfo[] GetTermsHandled(SessionProfile sessionProfile)
        {
            return _termInfos;
        }

        public bool NumericEvaluation(Term value, TNumber[] args, ref TNumber result)
        {
            if (value.Type != TermType.Function)
                return false;

            // Temperature conversions
            if (PsychroFunctions.CelsiusToKelvin.IsSupport(value))
                return PsychroFunctions.CelsiusToKelvin.NumericEvaluation(value, args, ref result);
            if (PsychroFunctions.KelvinToCelsius.IsSupport(value))
                return PsychroFunctions.KelvinToCelsius.NumericEvaluation(value, args, ref result);

            // Humidity ratio
            if (PsychroFunctions.HumRatioFromRelHum.IsSupport(value))
                return PsychroFunctions.HumRatioFromRelHum.NumericEvaluation(value, args, ref result);
            if (PsychroFunctions.HumRatioFromTWetBulb.IsSupport(value))
                return PsychroFunctions.HumRatioFromTWetBulb.NumericEvaluation(value, args, ref result);
            if (PsychroFunctions.HumRatioFromTDewPoint.IsSupport(value))
                return PsychroFunctions.HumRatioFromTDewPoint.NumericEvaluation(value, args, ref result);
            if (PsychroFunctions.HumRatioFromVapPres.IsSupport(value))
                return PsychroFunctions.HumRatioFromVapPres.NumericEvaluation(value, args, ref result);
            if (PsychroFunctions.HumRatioFromSpecificHum.IsSupport(value))
                return PsychroFunctions.HumRatioFromSpecificHum.NumericEvaluation(value, args, ref result);
            if (PsychroFunctions.HumRatioFromEnthalpy.IsSupport(value))
                return PsychroFunctions.HumRatioFromEnthalpy.NumericEvaluation(value, args, ref result);

            // Dew point
            if (PsychroFunctions.TDewPointFromRelHum.IsSupport(value))
                return PsychroFunctions.TDewPointFromRelHum.NumericEvaluation(value, args, ref result);
            if (PsychroFunctions.TDewPointFromTWetBulb.IsSupport(value))
                return PsychroFunctions.TDewPointFromTWetBulb.NumericEvaluation(value, args, ref result);
            if (PsychroFunctions.TDewPointFromVapPres.IsSupport(value))
                return PsychroFunctions.TDewPointFromVapPres.NumericEvaluation(value, args, ref result);
            if (PsychroFunctions.TDewPointFromHumRatio.IsSupport(value))
                return PsychroFunctions.TDewPointFromHumRatio.NumericEvaluation(value, args, ref result);

            // Wet bulb
            if (PsychroFunctions.TWetBulbFromTDewPoint.IsSupport(value))
                return PsychroFunctions.TWetBulbFromTDewPoint.NumericEvaluation(value, args, ref result);
            if (PsychroFunctions.TWetBulbFromRelHum.IsSupport(value))
                return PsychroFunctions.TWetBulbFromRelHum.NumericEvaluation(value, args, ref result);
            if (PsychroFunctions.TWetBulbFromHumRatio.IsSupport(value))
                return PsychroFunctions.TWetBulbFromHumRatio.NumericEvaluation(value, args, ref result);

            // Relative humidity
            if (PsychroFunctions.RelHumFromTDewPoint.IsSupport(value))
                return PsychroFunctions.RelHumFromTDewPoint.NumericEvaluation(value, args, ref result);
            if (PsychroFunctions.RelHumFromTWetBulb.IsSupport(value))
                return PsychroFunctions.RelHumFromTWetBulb.NumericEvaluation(value, args, ref result);
            if (PsychroFunctions.RelHumFromVapPres.IsSupport(value))
                return PsychroFunctions.RelHumFromVapPres.NumericEvaluation(value, args, ref result);
            if (PsychroFunctions.RelHumFromHumRatio.IsSupport(value))
                return PsychroFunctions.RelHumFromHumRatio.NumericEvaluation(value, args, ref result);

            // Vapour pressure
            if (PsychroFunctions.VapPresFromRelHum.IsSupport(value))
                return PsychroFunctions.VapPresFromRelHum.NumericEvaluation(value, args, ref result);
            if (PsychroFunctions.VapPresFromTDewPoint.IsSupport(value))
                return PsychroFunctions.VapPresFromTDewPoint.NumericEvaluation(value, args, ref result);
            if (PsychroFunctions.VapPresFromHumRatio.IsSupport(value))
                return PsychroFunctions.VapPresFromHumRatio.NumericEvaluation(value, args, ref result);

            // Specific humidity
            if (PsychroFunctions.SpecificHumFromHumRatio.IsSupport(value))
                return PsychroFunctions.SpecificHumFromHumRatio.NumericEvaluation(value, args, ref result);

            // Enthalpy
            if (PsychroFunctions.MoistAirEnthalpy.IsSupport(value))
                return PsychroFunctions.MoistAirEnthalpy.NumericEvaluation(value, args, ref result);
            if (PsychroFunctions.DryAirEnthalpy.IsSupport(value))
                return PsychroFunctions.DryAirEnthalpy.NumericEvaluation(value, args, ref result);
            if (PsychroFunctions.TDryBulbFromEnthalpy.IsSupport(value))
                return PsychroFunctions.TDryBulbFromEnthalpy.NumericEvaluation(value, args, ref result);

            // Density and volume
            if (PsychroFunctions.MoistAirDensity.IsSupport(value))
                return PsychroFunctions.MoistAirDensity.NumericEvaluation(value, args, ref result);
            if (PsychroFunctions.MoistAirVolume.IsSupport(value))
                return PsychroFunctions.MoistAirVolume.NumericEvaluation(value, args, ref result);
            if (PsychroFunctions.DryAirDensity.IsSupport(value))
                return PsychroFunctions.DryAirDensity.NumericEvaluation(value, args, ref result);
            if (PsychroFunctions.DryAirVolume.IsSupport(value))
                return PsychroFunctions.DryAirVolume.NumericEvaluation(value, args, ref result);
            if (PsychroFunctions.TDryBulbFromVolume.IsSupport(value))
                return PsychroFunctions.TDryBulbFromVolume.NumericEvaluation(value, args, ref result);

            // Saturation
            if (PsychroFunctions.SatVapPres.IsSupport(value))
                return PsychroFunctions.SatVapPres.NumericEvaluation(value, args, ref result);
            if (PsychroFunctions.SatHumRatio.IsSupport(value))
                return PsychroFunctions.SatHumRatio.NumericEvaluation(value, args, ref result);
            if (PsychroFunctions.SatAirEnthalpy.IsSupport(value))
                return PsychroFunctions.SatAirEnthalpy.NumericEvaluation(value, args, ref result);

            // Other
            if (PsychroFunctions.VaporPressureDeficit.IsSupport(value))
                return PsychroFunctions.VaporPressureDeficit.NumericEvaluation(value, args, ref result);
            if (PsychroFunctions.DegreeOfSaturation.IsSupport(value))
                return PsychroFunctions.DegreeOfSaturation.NumericEvaluation(value, args, ref result);

            // Pressure
            if (PsychroFunctions.StandardAtmPressure.IsSupport(value))
                return PsychroFunctions.StandardAtmPressure.NumericEvaluation(value, args, ref result);
            if (PsychroFunctions.StandardAtmTemperature.IsSupport(value))
                return PsychroFunctions.StandardAtmTemperature.NumericEvaluation(value, args, ref result);
            if (PsychroFunctions.SeaLevelPressure.IsSupport(value))
                return PsychroFunctions.SeaLevelPressure.NumericEvaluation(value, args, ref result);
            if (PsychroFunctions.StationPressure.IsSupport(value))
                return PsychroFunctions.StationPressure.NumericEvaluation(value, args, ref result);

            return false;
        }

        public void Dispose()
        {
        }
    }
}
