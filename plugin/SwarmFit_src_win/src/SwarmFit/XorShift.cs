// Vendored from SwarmFit (https://github.com/swharden/SwarmFit), BSD-3-Clause.
// See patches/README.md for the list of changes made for .NET Framework 2.0.

namespace SwarmFit
{
    /// <summary>
    /// Xorshift PRNG. Deterministic and very fast, which matters because the
    /// swarm draws several numbers per particle per iteration.
    /// </summary>
    /// <remarks>
    /// 2463534242 in binary is 10010010110101101000110010100010, a seed with a
    /// good balance of ones and zeros. The fixed default seed means a given fit
    /// is reproducible run to run, which is what the harness relies on.
    /// </remarks>
    public class XorShift : IRandomNumberGenerator
    {
        public const uint DefaultSeed = 2463534242u;

        private uint _rand;

        public XorShift() : this(DefaultSeed)
        {
        }

        public XorShift(uint seed)
        {
            _rand = seed;
        }

        public uint Next()
        {
            _rand ^= _rand << 13;
            _rand ^= _rand >> 17;
            _rand ^= _rand << 5;
            return _rand;
        }

        public double NextDouble()
        {
            return (double)Next() / uint.MaxValue;
        }
    }
}
