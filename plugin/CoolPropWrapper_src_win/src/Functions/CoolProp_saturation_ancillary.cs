using SMath.Manager;

namespace CoolPropWrapper.Functions
{
    /// <summary>CoolProp_saturation_ancillary(FluidName, output, Q, input, value)</summary>
    internal class CoolProp_saturation_ancillary : IFunction
    {
        // double saturation_ancillary(const char* fluid_name, const char* output, int Q, const char* input, double value);
        // Reads a value out of a fluid's saturation ancillary equations - the
        // fits CoolProp uses for the saturation line itself. HelmholtzEOS-backed
        // fluids only.

        Term inf;
        public static int[] Arguments = new[] { 5 };

        public CoolProp_saturation_ancillary(int argsCount)
        {
            inf = new Term(this.GetType().Name, TermType.Function, argsCount);
        }

        Term IFunction.Info { get { return inf; } }

        TermInfo IFunction.GetTermInfo(string lang)
        {
            string funcInfo = "(FluidName, output, Q, input, value) Extract a value from the saturation ancillary\r\n" +
                "FluidName: The name of the fluid to be used - HelmholtzEOS backend only\r\n" +
                "output: The desired output variable, e.g. \"P\" for saturation pressure\"\r\n" +
                "Q: The mass vapour quality, 0 (saturated liquid) or 1 (saturated vapour)\r\n" +
                "input: The input variable name, one of \"T\", \"P\", etc...\r\n" +
                "value: The input value, in SI units";

            var argsInfos = new[] {
                new ArgumentInfo(ArgumentSections.String),
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
            var fluid_name = coolpropPlugin.GetStringParam(value.Items[0], context);
            var output = coolpropPlugin.GetStringParam(value.Items[1], context);
            var Q = (int)coolpropPlugin.GetNumberParam(value.Items[2], context).obj.ToDouble();
            var inputVar = coolpropPlugin.GetStringParam(value.Items[3], context);
            var inputVal = coolpropPlugin.GetNumberParam(value.Items[4], context).obj.ToDouble();

            var Result = CoolPropEngine.saturation_ancillary(fluid_name, output, Q, inputVar, inputVal);
            coolpropPlugin.LogInfo("[INFO]",
                                   "fluid_name = {0}, output = {1}, Q = {2}, input = {3}, value = {4}, Result = {5}",
                                   fluid_name, output, Q, inputVar, inputVal, Result);

            result = coolpropPlugin.MakeDoubleResult(Result, Unit.Find(output));

            return true;
        }
    }
}
