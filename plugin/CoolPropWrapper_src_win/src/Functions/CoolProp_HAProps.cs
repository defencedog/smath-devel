using SMath.Manager;

namespace CoolPropWrapper.Functions
{
    /// <summary>CoolProp_HAProps(Output, Name1, Prop1, Name2, Prop2, Name3, Prop3)</summary>
    internal class CoolProp_HAProps : IFunction
    {
        // double HAPropsSI(const char *Output, const char *Name1, double Prop1, const char *Name2, double Prop2, const char *Name3, double Prop3);
        // Humid air, evaluated by CoolProp's HumidAirProp backend. All inputs are
        // SI base units: K for temperatures, Pa for pressures, J/kg for
        // enthalpies, J/kg/K for entropies, kg/kg for the humidity ratio.

        Term inf;
        public static int[] Arguments = new[] { 7 };

        public CoolProp_HAProps(int argsCount)
        {
            inf = new Term(this.GetType().Name, TermType.Function, argsCount);
        }

        Term IFunction.Info { get { return inf; } }

        TermInfo IFunction.GetTermInfo(string lang)
        {
            string funcInfo = "(Output, Name1, Prop1, Name2, Prop2, Name3, Prop3) Return a humid-air property\r\n" +
                "Output: The output parameter, e.g. \"H\", \"S\", \"V\", \"mu\", \"k\", \"Tdb\", \"Tdp\", \"Twb\", \"RH\", \"W\", \"Y\"\r\n" +
                "Name1/Prop1, Name2/Prop2, Name3/Prop3: three name/value pairs fixing the state.\r\n" +
                "Accepted names: \"T\" (dry-bulb), \"D\" (dew-point), \"B\" (wet-bulb), \"P\" (pressure), \"W\" (humidity ratio), \"R\" (relative humidity), \"H\" (enthalpy), \"S\" (entropy), \"V\" (volume), \"mu\" (viscosity), \"k\" (conductivity)";

            var argsInfos = new[] {
                new ArgumentInfo(ArgumentSections.String),
                new ArgumentInfo(ArgumentSections.String),
                new ArgumentInfo(ArgumentSections.RealNumber),
                new ArgumentInfo(ArgumentSections.String),
                new ArgumentInfo(ArgumentSections.RealNumber),
                new ArgumentInfo(ArgumentSections.String),
                new ArgumentInfo(ArgumentSections.RealNumber)
            };

            return new TermInfo(inf.Text,
                                inf.Type,
                                funcInfo,
                                FunctionSections.Unknown,
                                true,
                                argsInfos);
        }

        public bool TryEvaluateExpression(Entry value, SMath.Math.Store context, out Entry result)
        {
            var Output = coolpropPlugin.GetStringParam(value.Items[0], context);
            var Name1 = coolpropPlugin.GetStringParam(value.Items[1], context);
            var Prop1 = coolpropPlugin.GetNumberParam(value.Items[2], context).obj.ToDouble();
            var Name2 = coolpropPlugin.GetStringParam(value.Items[3], context);
            var Prop2 = coolpropPlugin.GetNumberParam(value.Items[4], context).obj.ToDouble();
            var Name3 = coolpropPlugin.GetStringParam(value.Items[5], context);
            var Prop3 = coolpropPlugin.GetNumberParam(value.Items[6], context).obj.ToDouble();

            var Result = CoolPropEngine.HAPropsSI(Output, Name1, Prop1, Name2, Prop2, Name3, Prop3);
            coolpropPlugin.LogInfo("[INFO]",
                                   "Output = {0}, Name1 = {1}, Prop1 = {2}, Name2 = {3}, Prop2 = {4}, Name3 = {5}, Prop3 = {6}, Result = {7}",
                                   Output, Name1, Prop1, Name2, Prop2, Name3, Prop3, Result);

            result = coolpropPlugin.MakeDoubleResult(Result, Unit.FindHA(Output));

            return true;
        }
    }
}
