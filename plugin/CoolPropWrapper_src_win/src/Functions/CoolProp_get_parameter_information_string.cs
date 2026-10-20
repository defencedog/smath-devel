using SMath.Manager;

namespace CoolPropWrapper.Functions
{
    /// <summary>CoolProp_get_parameter_information_string(Key, Kind)</summary>
    internal class CoolProp_get_parameter_information_string : IFunction
    {
        // long get_parameter_information_string(const char* param, char* Output, int n);

        Term inf;
        public static int[] Arguments = new[] { 2 };

        public CoolProp_get_parameter_information_string(int argsCount)
        {
            inf = new Term(this.GetType().Name, TermType.Function, argsCount);
        }

        Term IFunction.Info { get { return inf; } }

        TermInfo IFunction.GetTermInfo(string lang)
        {
            string funcInfo = "(Key, Kind) Get a description of a CoolProp parameter\r\n" +
                "Key: The parameter name, one of \"T\", \"P\", \"D\", \"H\", ...\r\n" +
                "Kind: Which piece of information to return, one of \"IO\", \"short\", \"long\", \"units\"";

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
            var key = coolpropPlugin.GetStringParam(value.Items[0], context);
            var kind = coolpropPlugin.GetStringParam(value.Items[1], context);

            // The buffer has to be SEEDED with the information kind: CoolProp
            // uses the same buffer both as the key it looks up ("IO", "short",
            // "long", "units") and as the destination it writes the answer into,
            // so it must already contain the desired kind on entry. Passing an
            // empty buffer returns an empty string - a silent wrong answer
            // rather than an error.
            var output = CoolPropEngine.MakeBuffer(kind);
            int rc = CoolPropEngine.get_parameter_information_string(key, output, output.Capacity);
            coolpropPlugin.LogInfo("[INFO]",
                                   "key = {0}, kind = {1}, output = {2}, rc = {3}",
                                   key, kind, output.ToString(), rc);

            if (rc != 1)
                coolpropPlugin.CoolPropError();

            result = coolpropPlugin.MakeStringResult(output.ToString());

            return true;
        }
    }
}
