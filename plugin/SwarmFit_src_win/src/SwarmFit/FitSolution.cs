// Vendored from SwarmFit (https://github.com/swharden/SwarmFit), BSD-3-Clause.
// See patches/README.md for the list of changes made for .NET Framework 2.0.

using System;

namespace SwarmFit
{
    /// <summary>
    /// A snapshot of the best fit found so far.
    /// </summary>
    /// <remarks>
    /// Upstream uses a primary constructor and a collection expression
    /// (<c>[.. parameters]</c>) to defensively copy the array. Both are newer
    /// than this target, so the copy is explicit.
    /// </remarks>
    public class FitSolution
    {
        private readonly double[] _parameters;

        public FitSolution(double[] parameters, double error, TimeSpan elapsed, int iterations, int improvements)
        {
            _parameters = new double[parameters.Length];
            Array.Copy(parameters, _parameters, parameters.Length);

            Error = error;
            Elapsed = elapsed;
            Iterations = iterations;
            ImprovementCount = improvements;
        }

        public double[] Parameters { get { return _parameters; } }
        public double Error { get; private set; }
        public TimeSpan Elapsed { get; private set; }
        public int Iterations { get; private set; }
        public int ImprovementCount { get; private set; }
    }
}
