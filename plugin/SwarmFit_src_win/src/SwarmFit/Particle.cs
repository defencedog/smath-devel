// Vendored from SwarmFit (https://github.com/swharden/SwarmFit), BSD-3-Clause.
// See patches/README.md for the list of changes made for .NET Framework 2.0.

using System;

namespace SwarmFit
{
    /// <summary>
    /// One member of the swarm: its current position, the velocity that carried
    /// it there, and the best position it has personally visited.
    /// </summary>
    public class Particle
    {
        public double[] Positions;
        public double[] BestPositions;
        public double[] Velocities;

        public double Error;
        public double BestError;

        public Particle(double[] pos, double err, double[] vel, double[] bestPos, double bestErr)
        {
            Positions = new double[pos.Length];
            pos.CopyTo(Positions, 0);

            Error = err;

            Velocities = new double[vel.Length];
            vel.CopyTo(Velocities, 0);

            BestPositions = new double[bestPos.Length];
            bestPos.CopyTo(BestPositions, 0);

            BestError = bestErr;
        }

        /// <summary>
        /// Teleport the particle to a random position inside the parameter box,
        /// and accept that position as its personal best.
        /// </summary>
        public void RandomizePositions(IRandomNumberGenerator rand, ParameterLimits[] limits)
        {
            double[] positions = Positions;
            for (int i = 0; i < positions.Length; i++)
            {
                positions[i] = limits[i].Random(rand);
            }

            // Upstream: positions.AsSpan().CopyTo(BestPositions). AsSpan is
            // System.Memory, so this is the equivalent BCL call.
            Array.Copy(positions, BestPositions, positions.Length);
        }
    }
}
