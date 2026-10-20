using SMath.Manager;

namespace CoolPropWrapper.Functions
{
    /// <summary>CoolProp_Phase(Name1, Prop1, Name2, Prop2, FluidName)</summary>
    internal class CoolProp_Phase : IFunction
    {
        // long PhaseSI(const char* Name1, double Prop1, const char* Name2, double Prop2, const char* FluidName, char* phase, int n);
        // Returns a phrase string: "liquid", "gas", "twophase", "supercritical",
        // "supercritical_gas", "supercritical_liquid", "solid", "sublidation"...

        Term inf;
        public static int[] Arguments = new[] { 5 };

        public CoolProp_Phase(int argsCount)
        {
            inf = new Term(this.GetType().Name, TermType.Function, argsCount);
        }

        Term IFunction.Info { get { return inf; } }

        TermInfo IFunction.GetTermInfo(string lang)
        {
            string funcInfo = "(Name1, Prop1, Name2, Prop2, FluidName) Return a string describing the phase at the given state\r\n" +
                "Name1: The first state variable name\r\n" +
                "Prop1: The first state variable value, in SI units\r\n" +
                "Name2: The second state variable name\r\n" +
                "Prop2: The second state variable value, in SI units\r\n" +
                "FluidName: The fluid name, e.g. \"Water\"";

            var argsInfos = new[] {
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
            var Name1 = coolpropPlugin.GetStringParam(value.Items[0], context);
            var Prop1 = coolpropPlugin.GetNumberParam(value.Items[1], context).obj.ToDouble();
            var Name2 = coolpropPlugin.GetStringParam(value.Items[2], context);
            var Prop2 = coolpropPlugin.GetNumberParam(value.Items[3], context).obj.ToDouble();
            var FluidName = coolpropPlugin.GetStringParam(value.Items[4], context);

            var phase = CoolPropEngine.MakeBuffer(null);
            var rc = CoolPropEngine.PhaseSI(Name1, Prop1, Name2, Prop2, FluidName, phase, phase.Capacity);
            coolpropPlugin.LogInfo("[INFO]",
                                   "Name1 = {0}, Prop1 = {1}, Name2 = {2}, Prop2 = {3}, FluidName = {4}, phase = {5}, rc = {6}",
                                   Name1, Prop1, Name2, Prop2, FluidName, phase.ToString(), rc);

            // rc == 1 is CoolProp's success code.
            if (rc != 1)
                coolpropPlugin.CoolPropError();

            result = coolpropPlugin.MakeStringResult(phase.ToString());

            return true;
        }
    }
}
