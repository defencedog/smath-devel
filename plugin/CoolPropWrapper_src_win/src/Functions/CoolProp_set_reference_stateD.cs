using SMath.Manager;

namespace CoolPropWrapper.Functions
{
    /// <summary>CoolProp_set_reference_stateD(FluidName, T, RhoMolar, h0, s0)</summary>
    internal class CoolProp_set_reference_stateD : IFunction
    {
        // int set_reference_stateD(const char* Ref, double T, double rhomolar, double hmolar0, double smolar0);

        Term inf;
        public static int[] Arguments = new[] { 5 };

        public CoolProp_set_reference_stateD(int argsCount)
        {
            inf = new Term(this.GetType().Name, TermType.Function, argsCount);
        }

        Term IFunction.Info { get { return inf; } }

        TermInfo IFunction.GetTermInfo(string lang)
        {
            string funcInfo = "(FluidName, T, RhoMolar, h0, s0) Set the reference state from a thermodynamic state point\r\n" +
                "FluidName: The name of the fluid\r\n" +
                "T: Temperature at the reference state [K]\r\n" +
                "RhoMolar: Molar density at the reference state [mol/m^3]\r\n" +
                "h0: Molar enthalpy at the reference state [J/mol]\r\n" +
                "s0: Molar entropy at the reference state [J/mol/K]";

            var argsInfos = new[] {
                new ArgumentInfo(ArgumentSections.String),
                new ArgumentInfo(ArgumentSections.RealNumber),
                new ArgumentInfo(ArgumentSections.RealNumber),
                new ArgumentInfo(ArgumentSections.RealNumber),
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
            var Ref = coolpropPlugin.GetStringParam(value.Items[0], context);
            var T = coolpropPlugin.GetNumberParam(value.Items[1], context).obj.ToDouble();
            var rho = coolpropPlugin.GetNumberParam(value.Items[2], context).obj.ToDouble();
            var h0 = coolpropPlugin.GetNumberParam(value.Items[3], context).obj.ToDouble();
            var s0 = coolpropPlugin.GetNumberParam(value.Items[4], context).obj.ToDouble();

            var Result = CoolPropEngine.set_reference_stateD(Ref, T, rho, h0, s0);
            coolpropPlugin.LogInfo("[INFO]",
                                   "Ref = {0}, T = {1}, rho = {2}, h0 = {3}, s0 = {4}, rc = {5}",
                                   Ref, T, rho, h0, s0, Result);

            result = coolpropPlugin.MakeDoubleResult(Result, Unit.unitless);

            return true;
        }
    }
}
