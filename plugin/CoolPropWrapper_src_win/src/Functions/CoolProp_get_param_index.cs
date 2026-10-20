using SMath.Manager;

namespace CoolPropWrapper.Functions
{
    /// <summary>CoolProp_get_param_index(Name)</summary>
    internal class CoolProp_get_param_index : IFunction
    {
        // long get_param_index(const char* param);

        Term inf;
        public static int[] Arguments = new[] { 1 };

        public CoolProp_get_param_index(int argsCount)
        {
            inf = new Term(this.GetType().Name, TermType.Function, argsCount);
        }

        Term IFunction.Info { get { return inf; } }

        TermInfo IFunction.GetTermInfo(string lang)
        {
            string funcInfo = "(Name) Returns the numeric index of a parameter\r\n" +
                "Name: The parameter name, one of \"T\", \"P\", \"D\", \"H\", \"S\", ...";

            var argsInfos = new[] {
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
            var param = coolpropPlugin.GetStringParam(value.Items[0], context);
            var Result = CoolPropEngine.get_param_index(param);
            coolpropPlugin.LogInfo("[INFO]", "param = {0}, Result = {1}", param, Result);
            result = coolpropPlugin.MakeDoubleResult(Result, Unit.unitless);
            return true;
        }
    }
}
