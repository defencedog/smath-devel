/*
 * IF97 SMath Plugin
 * Exposes IAPWS-IF97 steam/water properties to SMath Studio.
 *
 * Units: T [K], p [MPa], h/u [kJ/kg], s/cp/cv [kJ/(kg*K)], rho [kg/m3],
 *        v [m3/kg], w [m/s], mu [Pa*s], tc [W/(m*K)], sigma [N/m].
 */

using System;
using System.Collections.Generic;
using SMath.Manager;
using SMath.Math.Numeric;

namespace IF97SMath
{
    public class Plugin : IPluginHandleEvaluation, IPluginMathNumericEvaluation
    {
        private TermInfo[] _termInfos;
        private AssemblyInfo[] _assemblyInfos;
        private static Dictionary<string, IF97Function> _index;

        public void Initialize()
        {
            // No extra assembly dependencies: the plugin needs only SMath Studio
            // (the host) and the BCL, both always present.
            _assemblyInfos = new AssemblyInfo[0];

            IF97Function[] functions = IF97Functions.All;
            _termInfos = new TermInfo[functions.Length];
            _index = new Dictionary<string, IF97Function>(functions.Length);

            for (int i = 0; i < functions.Length; i++)
            {
                IF97Function function = functions[i];
                _termInfos[i] = function.GetTermInfo();
                _index[function.Name] = function;
            }
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

            if (_index == null)
                return false;

            IF97Function function;
            if (!_index.TryGetValue(value.Text, out function))
                return false;

            // SMath matches on name + argument count; a mismatched arity means
            // this is a different call and must fall through to other plugins.
            if (value.ArgsCount != function.ArgsCount)
                return false;

            var values = new double[function.ArgsCount];
            for (int i = 0; i < values.Length; i++)
                values[i] = args[i].obj.ToDouble();

            result = function.Evaluate(values);
            return true;
        }

        public void Dispose()
        {
        }
    }
}
