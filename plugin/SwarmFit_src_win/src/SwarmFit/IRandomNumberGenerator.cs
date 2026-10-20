// Vendored from SwarmFit (https://github.com/swharden/SwarmFit), BSD-3-Clause.
// See patches/README.md for the list of changes made for .NET Framework 2.0.

namespace SwarmFit
{
    public interface IRandomNumberGenerator
    {
        /// <summary>
        /// Return a random double between 0 and 1.
        /// </summary>
        double NextDouble();
    }
}
