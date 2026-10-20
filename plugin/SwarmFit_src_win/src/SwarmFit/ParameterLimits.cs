// Vendored from SwarmFit (https://github.com/swharden/SwarmFit), BSD-3-Clause.
// See patches/README.md for the list of changes made for .NET Framework 2.0.

using System;

namespace SwarmFit
{
    /// <summary>
    /// The closed interval [Min, Max] a single parameter is allowed to occupy.
    /// </summary>
    /// <remarks>
    /// Upstream is a <c>readonly struct</c> with a primary constructor. Both are
    /// C# 12 / C# 7.2 constructs; this is a plain struct with explicit members so
    /// it compiles against the .NET 2.0 reference assemblies.
    /// </remarks>
    public struct ParameterLimits
    {
        private readonly double _min;
        private readonly double _max;

        public ParameterLimits(double min, double max)
        {
            // Upstream normalises the pair, so a reversed bound is not an error.
            _min = Math.Min(min, max);
            _max = Math.Max(min, max);
        }

        public double Min { get { return _min; } }
        public double Max { get { return _max; } }
        public double Mid { get { return (_min + _max) / 2.0; } }
        public double Span { get { return _max - _min; } }

        public double Random(IRandomNumberGenerator rand)
        {
            return Span * rand.NextDouble() + _min;
        }

        public double Random(Random rand)
        {
            return Span * rand.NextDouble() + _min;
        }

        /// <summary>
        /// Constrain a value to this interval.
        /// </summary>
        public double Clamp(double value)
        {
            if (value < _min) return _min;
            if (value > _max) return _max;
            return value;
        }
    }
}
