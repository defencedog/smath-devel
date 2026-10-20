using SMath.Manager;

namespace CoolPropWrapper.Functions
{
    /// <summary>CoolProp_get_global_param_string(ParamName)</summary>
    internal class CoolProp_get_global_param_string : IFunction
    {
        // long get_global_param_string(const char* param, char* Output, int n);

        Term inf;
        public static int[] Arguments = new[] { 1 };

        public CoolProp_get_global_param_string(int argsCount)
        {
            inf = new Term(this.GetType().Name, TermType.Function, argsCount);
        }

        Term IFunction.Info { get { return inf; } }

        TermInfo IFunction.GetTermInfo(string lang)
        {
            string funcInfo = "(ParamName) Get a globally-defined string\r\n" +
                "ParamName: One of \"version\", \"errstring\", \"warnstring\", \"gitrevision\", \"FluidsList\", \"fluids_list\", \"parameter_list\", \"predefined_mixtures\"";

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

        /// <summary>
        /// Reads a global string out of the native library. Exposed statically
        /// because the plugin itself needs "errstring" to build error messages
        /// after a failing call, which cannot go through an Entry.
        /// </summary>
        internal static bool ReadGlobalString(string param, out string resultStr)
        {
            var output = CoolPropEngine.MakeBuffer(null);
            int rc = CoolPropEngine.get_global_param_string(param, output, output.Capacity);
            resultStr = output.ToString();
            coolpropPlugin.LogInfo("[INFO]", "param = {0}, output = {1}, rc = {2}", param, resultStr, rc);
            return rc == 1;
        }

        public bool TryEvaluateExpression(Entry value, SMath.Math.Store context, out Entry result)
        {
            var param = coolpropPlugin.GetStringParam(value.Items[0], context);
            string resultStr;
            if (!ReadGlobalString(param, out resultStr))
                coolpropPlugin.CoolPropError();
            result = coolpropPlugin.MakeStringResult(resultStr);
            return true;
        }
    }
}
