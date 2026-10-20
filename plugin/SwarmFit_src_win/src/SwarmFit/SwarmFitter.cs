// Vendored from SwarmFit (https://github.com/swharden/SwarmFit), BSD-3-Clause.
// See patches/README.md for the list of changes made for .NET Framework 2.0.
//
// Adapted from "Particle Swarm Optimization Using C#" by James McCaffrey,
// published in Visual Studio Magazine on 11/25/2013 and other texts:
// https://visualstudiomagazine.com/Articles/2013/11/01/Particle-Swarm-Optimization.aspx
// https://ieeexplore.ieee.org/document/9376550
// https://www.mathworks.com/matlabcentral/fileexchange/58895-ppso

using System;
using System.Diagnostics;

namespace SwarmFit
{
    /// <summary>
    /// Minimises the sum of absolute residuals of <see cref="Function"/> over the
    /// supplied X/Y data by moving a swarm of particles through the bounded
    /// parameter space. No derivative is required.
    /// </summary>
    public class SwarmFitter
    {
        /// <summary>X values the parameters are fitted to.</summary>
        public double[] Xs { get; private set; }

        /// <summary>Y values the parameters are fitted to.</summary>
        public double[] Ys { get; private set; }

        /// <summary>The model being fitted.</summary>
        public FitFunction Function { get; private set; }

        /// <summary>Bounds for each parameter, also the swarm's search box.</summary>
        private ParameterLimits[] Limits { get; set; }

        /// <summary>
        /// Number of parameters being optimised. Must agree with the length of
        /// the limits array and the parameter array the model consumes.
        /// </summary>
        public int ParameterCount { get { return Limits.Length; } }

        /// <summary>Source of randomness for placement and inertial angle.</summary>
        public IRandomNumberGenerator Rand { get; set; }

        public double InertiaWeight { get; set; }
        public double LocalWeight { get; set; }
        public double GlobalWeight { get; set; }

        /// <summary>
        /// Chance per iteration that a non-best particle is killed and respawned,
        /// which is what stops the swarm collapsing into a local minimum.
        /// </summary>
        public double ParticleDeathProbability;

        public int ParticleCount { get { return Particles.Length; } }

        private Particle[] Particles { get; set; }
        private double[] BestParameters { get; set; }
        private double BestError;

        private readonly Stopwatch _stopwatch;

        /// <summary>Total time the solver has been running.</summary>
        public TimeSpan CalculationTime { get { return _stopwatch.Elapsed; } }

        /// <summary>Number of global-best improvements seen so far.</summary>
        public int ImprovementCount { get; private set; }

        /// <summary>Number of times the whole swarm has been stepped forward.</summary>
        public int IterationCount { get; private set; }

        public FitSolution BestSolution
        {
            get
            {
                return new FitSolution(BestParameters, BestError, CalculationTime,
                                       IterationCount, ImprovementCount);
            }
        }

        public SwarmFitter(double[] xs, double[] ys, FitFunction func,
                           ParameterLimits[] limits, int numParticles)
        {
            if (xs.Length != ys.Length)
                throw new ArgumentException("xs and ys must have equal length");

            if (limits == null || limits.Length == 0)
                throw new ArgumentException("at least one parameter limit is required");

            if (numParticles < 1)
                throw new ArgumentException("at least one particle is required");

            Xs = xs;
            Ys = ys;
            Function = func;
            Limits = limits;
            Particles = new Particle[numParticles];
            BestParameters = new double[ParameterCount];

            BestError = double.MaxValue;
            Rand = new XorShift();
            InertiaWeight = 0.729;
            LocalWeight = 1.49445;
            GlobalWeight = 1.49445;
            ParticleDeathProbability = 0.01;

            _stopwatch = new Stopwatch();
        }

        /// <summary>
        /// Sum of absolute residuals. This is the objective the swarm minimises;
        /// note it is L1, not the L2 that <c>swarmfit_details</c> reports as SSE.
        /// </summary>
        private double GetError(double[] parameters)
        {
            double error = 0;
            double[] xs = Xs;
            double[] ys = Ys;

            for (int i = 0; i < xs.Length; i++)
            {
                double predictedY = Function(xs[i], parameters);
                double actualY = ys[i];
                double diff = Math.Abs(predictedY - actualY);
                error += diff;
            }

            return error;
        }

        /// <summary>
        /// Advance the swarm. May be called repeatedly; particles are only
        /// randomised on the first call, so successive calls refine the same run.
        /// </summary>
        /// <param name="maxIterations">Stop after this many swarm steps.</param>
        /// <param name="maxImprovements">Stop after this many improvements.</param>
        /// <param name="errorThreshold">Stop once the best error drops to this.</param>
        public FitSolution Solve(int maxIterations, int maxImprovements, double errorThreshold)
        {
            _stopwatch.Start();

            if (IterationCount == 0)
            {
                RandomizeAll();
            }

            int improvementLimit = maxImprovements == int.MaxValue
                ? maxImprovements
                : ImprovementCount + maxImprovements;

            for (int i = 0; i < maxIterations; i++)
            {
                IterateAllParticles();
                if (ImprovementCount >= improvementLimit || BestError <= errorThreshold)
                    break;
            }

            _stopwatch.Stop();

            return BestSolution;
        }

        public FitSolution Solve()
        {
            return Solve(1000, int.MaxValue, 0);
        }

        public FitSolution Solve(int maxIterations)
        {
            return Solve(maxIterations, int.MaxValue, 0);
        }

        private void RandomizeAll()
        {
            const double initialVelocityRandomness = 0.01;

            for (int i = 0; i < Particles.Length; i++)
            {
                // Upstream builds these with ParameterLimits.Select(...).ToArray().
                // The draw order is preserved exactly so a seeded run still
                // reproduces the same trajectory.
                double[] randomPositions = new double[Limits.Length];
                for (int j = 0; j < randomPositions.Length; j++)
                {
                    randomPositions[j] = Limits[j].Random(Rand);
                }

                double error = GetError(randomPositions);

                double[] randomVelocities = new double[Limits.Length];
                for (int j = 0; j < randomVelocities.Length; j++)
                {
                    randomVelocities[j] = Limits[j].Random(Rand) * Limits[j].Span
                                        * initialVelocityRandomness;
                }

                Particles[i] = new Particle(randomPositions, error, randomVelocities,
                                            randomPositions, error);

                if (Particles[i].Error < BestError)
                {
                    BestError = Particles[i].Error;
                    Array.Copy(Particles[i].Positions, BestParameters, BestParameters.Length);
                }
            }
        }

        private void IterateAllParticles()
        {
            double originalBestError = BestError;

            for (int i = 0; i < Particles.Length; i++)
            {
                IterateParticle(Particles[i]);
            }

            IterationCount++;
            if (BestError != originalBestError)
            {
                ImprovementCount++;
            }
        }

        private void IterateParticle(Particle particle)
        {
            double[] positions = particle.Positions;
            double[] bestPositions = particle.BestPositions;
            double[] velocities = particle.Velocities;

            // v = w*v + c1*r1*(p_best - x) + c2*r2*(g_best - x)
            for (int j = 0; j < velocities.Length; j++)
            {
                double inertia = InertiaWeight * velocities[j];
                double local = LocalWeight * Rand.NextDouble() * (bestPositions[j] - positions[j]);
                double global = GlobalWeight * Rand.NextDouble() * (BestParameters[j] - positions[j]);
                velocities[j] = inertia + local + global;
            }

            // x = clamp(x + v)
            for (int j = 0; j < positions.Length; j++)
            {
                positions[j] = positions[j] + velocities[j];
                positions[j] = Limits[j].Clamp(positions[j]);
            }

            particle.Error = GetError(positions);

            if (particle.Error < particle.BestError)
            {
                Array.Copy(positions, bestPositions, positions.Length);
                particle.BestError = particle.Error;
            }

            if (particle.Error < BestError)
            {
                Array.Copy(positions, BestParameters, positions.Length);
                BestError = particle.BestError;
            }

            bool isBestParticle = particle.BestError == BestError;

            if (!isBestParticle && (Rand.NextDouble() < ParticleDeathProbability))
            {
                particle.RandomizePositions(Rand, Limits);
                particle.Error = GetError(particle.Positions);
                particle.BestError = particle.Error;

                if (particle.Error < BestError)
                {
                    BestError = particle.Error;
                    Array.Copy(positions, BestParameters, positions.Length);
                }
            }
        }
    }
}
