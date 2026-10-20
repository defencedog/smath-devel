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
 *   specific energy / enthalpy / latent heat   m^2/s^2
 *   specific entropy / heat capacity           m^2/(s^2*K)
 *   pressure                                   kg/(m*s^2)
 *   density                                    kg/m^3
 *   specific volume                            m^3/kg
 *   velocity / speed of sound                  m/s
 *   dynamic viscosity                          kg/(m*s)
 *   thermal conductivity                       kg*m/(s^3*K)
 *   surface tension                            kg/s^2
 *
 * The MItem instances are built lazily on first evaluation: SMath.Manager's
 * parser needs live session state, so they cannot be created in a static
 * initializer that might run too early.
 */

using System;
using SMath.Math.Symbolic;

namespace IF97SMath
{
    internal static class UnitExpressions
    {
        /// <summary>Set if the parser ever rejects a unit string; stops retries.</summary>
        private static bool _disabled;

        private static MItem _temperature;
        private static MItem _pressure;
        private static MItem _density;
        private static MItem _specificVolume;
        private static MItem _specificEnergy;
        private static MItem _specificEntropy;
        private static MItem _velocity;
        private static MItem _dynamicViscosity;
        private static MItem _thermalConductivity;
        private static MItem _surfaceTension;

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
                    // stop retrying. (Never seen in practice - the strings are
                    // plain base-unit products - but cheap insurance.)
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
                case Quantity.Density:
                    return Get(ref _density, "'kg/'m^3");
                case Quantity.SpecificVolume:
                    return Get(ref _specificVolume, "'m^3/'kg");
                case Quantity.SpecificEnergy:
                    return Get(ref _specificEnergy, "'m^2/'s^2");
                case Quantity.SpecificEntropy:
                    return Get(ref _specificEntropy, "'m^2/('s^2*'K)");
                case Quantity.Velocity:
                    return Get(ref _velocity, "'m/'s");
                case Quantity.DynamicViscosity:
                    return Get(ref _dynamicViscosity, "'kg/('m*'s)");
                case Quantity.ThermalConductivity:
                    return Get(ref _thermalConductivity, "'kg*'m/('s^3*'K)");
                case Quantity.SurfaceTension:
                    return Get(ref _surfaceTension, "'kg/'s^2");
                default:
                    return null;   // dimensionless: Prandtl number, region index
            }
        }
    }
}
