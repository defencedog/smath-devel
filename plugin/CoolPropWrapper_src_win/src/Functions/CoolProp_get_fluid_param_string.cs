using SMath.Manager;

namespace CoolPropWrapper.Functions
{
    /// <summary>CoolProp_get_fluid_param_string(FluidName, ParamName)</summary>
    internal class CoolProp_get_fluid_param_string : IFunction
    {
        // long get_fluid_param_string(const char* fluid, const char* param, char* Output, int n);

        Term inf;
        public static int[] Arguments = new[] { 2 };

        public CoolProp_get_fluid_param_string(int argsCount)
        {
            inf = new Term(this.GetType().Name, TermType.Function, argsCount);
        }

        Term IFunction.Info { get { return inf; } }

        TermInfo IFunction.GetTermInfo(string lang)
        {
            string funcInfo = "(FluidName, ParamName) Get a fluid-specific string\r\n" +
                "FluidName: The fluid name, e.g. \"Water\"\r\n" +
                "ParamName: One of \"aliases\", \"CAS\", \"ASN\", \"ASHRAE34\", \"REFPROPName\", \"INCHIName\", \"formula\", \"BibTeX-CON\", \"BibTeX-EOS\", \"BibTeX-CP0\", \"BibTeX-ECS\", \"BibTeX-VIS\", \"BibTeX-TC0\", \"BibTeX-STN\", \"BibTeX-SUR\", \"BibTeX-XX\"\r\n" +
                "or a predefined-mixture name such as \"Air\" or \"R410A\"";

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
            var fluid = coolpropPlugin.GetStringParam(value.Items[0], context);
            var param = coolpropPlugin.GetStringParam(value.Items[1], context);

            var output = CoolPropEngine.MakeBuffer(null);
            int rc = CoolPropEngine.get_fluid_param_string(fluid, param, output, output.Capacity);
            coolpropPlugin.LogInfo("[INFO]",
                                   "fluid = {0}, param = {1}, output = {2}, rc = {3}",
                                   fluid, param, output.ToString(), rc);

            if (rc != 1)
                coolpropPlugin.CoolPropError();

            result = coolpropPlugin.MakeStringResult(output.ToString());

            return true;
        }
    }
}
