// Vendored from SwarmFit (https://github.com/swharden/SwarmFit), BSD-3-Clause.
// See patches/README.md for the list of changes made for .NET Framework 2.0.

namespace SwarmFit
{
    /// <summary>
    /// The curve model: given an X and a parameter vector, return Y.
    /// </summary>
    /// <remarks>
    /// Upstream declares this as <c>Func&lt;double, double[], double&gt;</c>, which
    /// lives in System.Core.dll (.NET Framework 3.5). A plugin targeting .NET 2.0
    /// cannot reference System.Core, so the delegate is declared here instead.
    /// Anonymous methods and lambdas bind to it exactly as they did to Func.
    /// </remarks>
    public delegate double FitFunction(double x, double[] parameters);
}
