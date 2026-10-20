using SMath.Manager;

namespace CoolPropWrapper.Functions
{
    /// <summary>CoolProp_Props(Output, Name1, Prop1, Name2, Prop2, FluidName)</summary>
    internal class CoolProp_Props : IFunction
    {
        // double PropsSI(const char *Output, const char *Name1, double Prop1, const char *Name2, double Prop2, const char *FluidName);
        // All of the P/Invoke declarations live in CoolPropEngine.cs, which also
        // documents why one plain EntryPoint binds on both Windows stdcall and
        // Linux cdecl engines.

        Term inf;
        public static int[] Arguments = new[] { 6 };

        public CoolProp_Props(int argsCount)
        {
            inf = new Term(this.GetType().Name, TermType.Function, argsCount);
        }

        Term IFunction.Info { get { return inf; } }

        TermInfo IFunction.GetTermInfo(string lang)
        {
            string funcInfo = "(Output, Name1, Prop1, Name2, Prop2, FluidName) Return a thermodynamic or transport property of a pure fluid\r\n" +
                "Output: The output parameter, one of \"T\", \"D\", \"H\", \"S\", \"U\", \"G\", \"A\", \"V\", \"C\", \"O\", \"L\", \"I\", \"M\", ...\r\n" +
                "Name1: The first state variable name, one of \"T\", \"P\", \"D\", \"H\", ...\r\n" +
                "Prop1: The first state variable value, in SI units (K, Pa, kg/m^3, J/kg, ...)\r\n" +
                "Name2: The second state variable name\r\n" +
                "Prop2: The second state variable value, in SI units\r\n" +
                "FluidName: The fluid name, e.g. \"Water\". A mixture uses \"HEOS::Fluid[0.5]&Fluid[0.5]\"";

            var argsInfos = new[] {
                new ArgumentInfo(ArgumentSections.String),
                new ArgumentInfo(ArgumentSections.String),
                new ArgumentInfo(ArgumentSections.RealNumber),
                new ArgumentInfo(ArgumentSections.String),
                new ArgumentInfo(ArgumentSections.RealNumber),
                new ArgumentInfo(ArgumentSections.String)
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
            var FluidName = coolpropPlugin.GetStringParam(value.Items[5], context);

            var Result = CoolPropEngine.PropsSI(Output, Name1, Prop1, Name2, Prop2, FluidName);
            coolpropPlugin.LogInfo("[INFO]",
                                   "Output = {0}, Name1 = {1}, Prop1 = {2}, Name2 = {3}, Prop2 = {4}, FluidName = {5}, Result = {6}",
                                   Output, Name1, Prop1, Name2, Prop2, FluidName, Result);

            result = coolpropPlugin.MakeDoubleResult(Result, Unit.Find(Output));

            return true;
        }
    }
}
