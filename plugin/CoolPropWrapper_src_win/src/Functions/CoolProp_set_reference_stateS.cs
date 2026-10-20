using SMath.Manager;

namespace CoolPropWrapper.Functions
{
    /// <summary>CoolProp_set_reference_stateS(FluidName, ReferenceState)</summary>
    internal class CoolProp_set_reference_stateS : IFunction
    {
        // int set_reference_stateS(const char* Ref, const char* reference_state);
        // Sets the reference state from which a fluid's enthalpy and entropy are
        // measured. This changes every subsequent H, S and U result for that
        // fluid, so it is a global side effect.

        Term inf;
        public static int[] Arguments = new[] { 2 };

        public CoolProp_set_reference_stateS(int argsCount)
        {
            inf = new Term(this.GetType().Name, TermType.Function, argsCount);
        }

        Term IFunction.Info { get { return inf; } }

        TermInfo IFunction.GetTermInfo(string lang)
        {
            string funcInfo = "(FluidName, ReferenceState) Set the reference state from a name\r\n" +
                "FluidName: The name of the fluid\r\n" +
                "ReferenceState: One of \"IIR\" (h=200 kJ/kg, s=1 kJ/kg/K at saturated liquid, 0 degC), \"ASHRAE\" (h=0, s=0 at saturated liquid, -40 degC), \"NBP\" (h=0, s=0 at the normal boiling point), \"DEF\" (reset to the default), \"RESET\" (clear any custom state)";

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
            var Ref = coolpropPlugin.GetStringParam(value.Items[0], context);
            var reference_state = coolpropPlugin.GetStringParam(value.Items[1], context);
            var Result = CoolPropEngine.set_reference_stateS(Ref, reference_state);
            coolpropPlugin.LogInfo("[INFO]", "Ref = {0}, reference_state = {1}, rc = {2}", Ref, reference_state, Result);
            result = coolpropPlugin.MakeDoubleResult(Result, Unit.unitless);
            return true;
        }
    }
}
