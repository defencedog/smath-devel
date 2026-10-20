/*
 * Unit expressions emitted by the plugin.
 *
 * SMath's numeric layer keeps a value and a unit AST per component
 * (BaseEntry.Value + BaseEntry.Units) and converts to base units through
 * UnitInfo.Factor, so a plugin both receives and must return SI base units.
 * BaseEntry.ComUnits compares the two unit ASTs for equality when combining
 * components, which is why a named derived unit (J, Pa, W) or an unreduced
 * compound ('J/'kg) makes the result dimensionally unequal to a native
 * worksheet expression and surfaces as Error 74. So every quantity is emitted
 * as base units only.
 *
 *   temperature                        K
 *   pressure / vapour pressure         kg/(m*s^2)
 *   specific energy / enthalpy         m^2/s^2
 *   density                            kg/m^3
 *   specific volume                    m^3/kg
 *
 * The MItem instances are built lazily on first evaluation: SMath.Manager's
 * parser needs live session state, so they cannot be created in a static
 * initializer that might run too early.
 */

using System;
using SMath.Math.Numeric;
using SMath.Math.Symbolic;

namespace PsychroLibSMath
{
    /// <summary>Physical quantity produced by a function.</summary>
    internal enum Quantity
    {
        Dimensionless,      // humidity ratio, RH, degree of saturation, specific humidity
        Temperature,
        Pressure,
        SpecificEnergy,     // enthalpy, J/kg
        Density,
        SpecificVolume
    }

    internal static class UnitExpressions
    {
        /// <summary>Set if the parser ever rejects a unit string; stops retries.</summary>
        private static bool _disabled;

        private static MItem _temperature;
        private static MItem _pressure;
        private static MItem _specificEnergy;
        private static MItem _density;
        private static MItem _specificVolume;

        /// <summary>Builds (once) and returns the unit expression for a quantity.</summary>
        private static MItem Get(ref MItem slot, string expression)
        {
            if (slot == null && !_disabled)
            {
                try
                {
                    slot = Converter.ToMItem(expression);
                }
                catch (Exception)
                {
                    // A unit string the parser rejects must never turn into a
                    // plugin error dialog: fall back to unit-less results and
                    // stop retrying.
                    _disabled = true;
                    return null;
                }
            }
            return slot;
        }

        public static MItem For(Quantity quantity)
        {
            switch (quantity)
            {
                case Quantity.Temperature:
                    return Get(ref _temperature, "'K");
                case Quantity.Pressure:
                    return Get(ref _pressure, "'kg/('m*'s^2)");
                case Quantity.SpecificEnergy:
                    return Get(ref _specificEnergy, "'m^2/'s^2");
                case Quantity.Density:
                    return Get(ref _density, "'kg/'m^3");
                case Quantity.SpecificVolume:
                    return Get(ref _specificVolume, "'m^3/'kg");
                default:
                    return null;   // dimensionless: leave Units at SMath's default of 1
            }
        }

        /// <summary>Tags a result with its unit expression, if it has one.</summary>
        public static void Attach(TNumber number, Quantity quantity)
        {
            MItem units = For(quantity);
            if (units != null)
                number.obj.Units = units;
        }
    }
}
