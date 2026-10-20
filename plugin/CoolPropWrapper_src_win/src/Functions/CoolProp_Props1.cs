using SMath.Manager;

namespace CoolPropWrapper.Functions
{
    /// <summary>CoolProp_Props1(FluidName, Output)</summary>
    internal class CoolProp_Props1 : IFunction
    {
        // double Props1SI(const char *FluidName, const char *Output);
        // Returns a fluid constant: a critical/reducing point, a limit, or a
        // hazard rating - anything that does not depend on a state point.

        Term inf;
        public static int[] Arguments = new[] { 2 };

        public CoolProp_Props1(int argsCount)
        {
            inf = new Term(this.GetType().Name, TermType.Function, argsCount);
        }

        Term IFunction.Info { get { return inf; } }

        TermInfo IFunction.GetTermInfo(string lang)
        {
            string funcInfo = "(FluidName, Output) Return a fluid property that does not depend on the thermodynamic state\r\n" +
                "FluidName: The fluid name, e.g. \"Water\"\r\n" +
                "Output: The output parameter, one of \"Tcrit\", \"Pcrit\", \"Dcrit\", \"Tmin\", \"Tmax\", \"Ttriple\", \"Ptriple\", \"M\", \"acentric\", \"GWP20\", \"ODP\", ...";

            var argsInfos = new[] {
                new ArgumentInfo(ArgumentSections.String),
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
            var FluidName = coolpropPlugin.GetStringParam(value.Items[0], context);
            var Output = coolpropPlugin.GetStringParam(value.Items[1], context);
            var Result = CoolPropEngine.Props1SI(FluidName, Output);
            coolpropPlugin.LogInfo("[INFO]", "FluidName = {0}, Output = {1}, Result = {2}", FluidName, Output, Result);

            // Props1SI answers the question "Output of FluidName", but CoolProp
            // also accepts the arguments the other way round, in which case the
            // *first* argument holds the property name. Try the documented
            // order first and fall back to the reversed one, so both spellings
            // get the right unit tag.
            //
            // ReferenceEquals rather than == : MItem overloads op_Equality and
            // dereferences both operands, so `x == null` throws instead of
            // returning true when x is a real MItem.
            var ResUnit = Unit.Find(Output);
            if (ReferenceEquals(ResUnit, null))
                ResUnit = Unit.Find(FluidName);

            result = coolpropPlugin.MakeDoubleResult(Result, ResUnit);

            return true;
        }
    }
}
