namespace CoolPropWrapper
{
    /// <summary>
    /// One CoolProp entry point exposed to SMath. Each class holds the P/Invoke
    /// declaration, the TermInfo metadata (signature, argument kinds) and the
    /// evaluation itself, and is discovered by the plugin's reflection pass
    /// because it declares `public static int[] Arguments`.
    /// </summary>
    internal interface IFunction
    {
        /// <summary>The function term, e.g. "CoolProp_Props" with 6 arguments.</summary>
        SMath.Manager.Term Info { get; }

        /// <summary>Signature shown in SMath's function browser.</summary>
        SMath.Manager.TermInfo GetTermInfo(string lang);

        bool TryEvaluateExpression(
            SMath.Manager.Entry value,
            SMath.Math.Store context,
            out SMath.Manager.Entry result);
    }
}
