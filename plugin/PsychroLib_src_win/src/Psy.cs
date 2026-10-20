/*
 * Psy — bridge between the SMath plugin layer and PsychroLib.
 *
 * PsychroLib's Psychrometrics class exposes instance methods whose behaviour
 * depends on the UnitSystem set on the instance, so the plugin keeps one
 * shared SI instance and calls through it.
 */

using SMath.Math.Numeric;

namespace PsychroLibSMath
{
    internal static class Psy
    {
        /// <summary>Shared PsychroLib instance configured for SI units.</summary>
        public static readonly global::PsychroLib.Psychrometrics SI =
            new global::PsychroLib.Psychrometrics(global::PsychroLib.UnitSystem.SI);

        /// <summary>
        /// Extract a plain double from an SMath numeric argument.
        /// TNumber.obj holds a BaseEntry (TDouble, TFraction, ...), and
        /// BaseEntry.ToDouble() handles every numeric representation.
        /// </summary>
        public static double D(TNumber number)
        {
            return number.obj.ToDouble();
        }
    }
}
