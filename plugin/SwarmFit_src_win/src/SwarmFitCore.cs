// The numeric core of the plugin: everything that can run without SMath.
//
// This layer deliberately touches no SMath type. That is not just tidiness --
// TNumber, TermInfo and ArgumentInfo dereference the host's GlobalProfile and
// throw NullReferenceException when constructed outside a running SMath Studio
// session, so any logic that lives in SwarmFitEngine cannot be unit tested.
// Keeping the maths here means the whole numerical contract is verifiable from
// a plain console harness (see tests/Program.cs).

using System;
using SwarmFit;

namespace SwarmFitWrapper
{
    public static class SwarmFitCore
    {
        /// <summary>
        /// The built-in model catalogue. Returns null when the name is unknown.
        /// </summary>
        public static FitFunction StandardModel(string name, out int parameterCount)
        {
            switch (name)
            {
                case "linear":
                case "line":
                    parameterCount = 2;
                    return delegate(double x, double[] p) { return p[0] * x + p[1]; };

                case "exp":
                case "expdec1":
                    parameterCount = 3;
                    return delegate(double x, double[] p) { return p[0] + p[1] * Math.Exp(p[2] * x); };

                case "expdecay":
                case "expdecay1":
                    parameterCount = 2;
                    return delegate(double x, double[] p) { return p[0] * Math.Exp(-x / p[1]); };

                case "power":
                    parameterCount = 2;
                    return delegate(double x, double[] p) { return p[0] * Math.Pow(x, p[1]); };

                case "poly2":
                    parameterCount = 3;
                    return delegate(double x, double[] p) { return p[0] * x * x + p[1] * x + p[2]; };

                case "poly3":
                    parameterCount = 4;
                    return delegate(double x, double[] p)
                    {
                        return p[0] * x * x * x + p[1] * x * x + p[2] * x + p[3];
                    };

                case "gauss":
                case "gaussian":
                    parameterCount = 3;
                    return delegate(double x, double[] p)
                    {
                        return p[0] * Math.Exp(-Math.Pow(x - p[1], 2) / (2.0 * p[2] * p[2]));
                    };

                case "hill":
                case "sigmoid":
                    parameterCount = 4;
                    return delegate(double x, double[] p)
                    {
                        return p[0] + (p[1] - p[0]) / (1.0 + Math.Pow(p[2] / x, p[3]));
                    };

                case "mm":
                case "michaelis_menten":
                    parameterCount = 2;
                    return delegate(double x, double[] p) { return (p[0] * x) / (p[1] + x); };

                case "log":
                case "logarithmic":
                    parameterCount = 2;
                    return delegate(double x, double[] p)
                    {
                        double safeX = x > 0.0 ? x : 1e-12;
                        return p[0] + p[1] * Math.Log(safeX);
                    };

                case "fourier1":
                    parameterCount = 4;
                    return delegate(double x, double[] p)
                    {
                        return p[0] + p[1] * Math.Cos(p[2] * x) + p[3] * Math.Sin(p[2] * x);
                    };

                case "arrhenius":
                    parameterCount = 2;
                    return delegate(double x, double[] p) { return p[0] * Math.Exp(-p[1] / x); };

                case "antoine":
                    parameterCount = 3;
                    return delegate(double x, double[] p)
                    {
                        return Math.Pow(10.0, p[0] - p[1] / (x + p[2]));
                    };

                case "haldane":
                case "andrews":
                    parameterCount = 3;
                    return delegate(double x, double[] p)
                    {
                        return (p[0] * x) / (p[1] + x + (x * x) / p[2]);
                    };
            }

            parameterCount = -1;
            return null;
        }

        /// <summary>
        /// Run the swarm, then compute the goodness-of-fit statistics the worksheet
        /// reports. The fit is run in batches so a stagnating search can stop early
        /// instead of burning the whole iteration budget.
        /// </summary>
        public static FitResult Fit(double[] xs, double[] ys, FitFunction model,
                                    double[] parameterMin, double[] parameterMax,
                                    int particleCount, int maxIterations)
        {
            if (xs == null || ys == null)
                throw new ArgumentNullException("xs", "xs and ys are required");

            if (model == null)
                throw new ArgumentNullException("model", "a model function is required");

            if (xs.Length != ys.Length)
            {
                throw new ArgumentException("xs length (" + xs.Length + ") does not match ys length (" +
                                            ys.Length + ")");
            }

            if (xs.Length == 0)
                throw new ArgumentException("at least one data point is required");

            if (parameterMin == null || parameterMax == null)
                throw new ArgumentNullException("parameterMin", "p_min and p_max are required");

            if (parameterMin.Length != parameterMax.Length)
            {
                throw new ArgumentException("p_min length (" + parameterMin.Length +
                                            ") does not match p_max length (" + parameterMax.Length + ")");
            }

            if (parameterMin.Length == 0)
                throw new ArgumentException("at least one parameter is required");

            if (xs.Length < parameterMin.Length)
            {
                throw new ArgumentException("Number of data points (" + xs.Length +
                                            ") must be >= number of parameters (" + parameterMin.Length + ")");
            }

            ParameterLimits[] limits = new ParameterLimits[parameterMin.Length];
            for (int i = 0; i < parameterMin.Length; i++)
            {
                if (parameterMin[i] > parameterMax[i])
                {
                    throw new ArgumentException("p_min[" + i + "] (" + parameterMin[i] +
                                                ") cannot be greater than p_max[" + i + "] (" +
                                                parameterMax[i] + ")");
                }
                limits[i] = new ParameterLimits(parameterMin[i], parameterMax[i]);
            }

            if (particleCount <= 0) particleCount = 20;
            if (maxIterations <= 0) maxIterations = 5000;

            SwarmFitter fitter = new SwarmFitter(xs, ys, model, limits, particleCount);

            int batchSize = Math.Min(25, Math.Max(10, maxIterations / 10));
            double previousBest = double.MaxValue;
            int stagnantBatches = 0;
            const double tolerance = 1e-5;

            while (fitter.IterationCount < maxIterations)
            {
                int remaining = maxIterations - fitter.IterationCount;
                int currentBatch = Math.Min(batchSize, remaining);
                fitter.Solve(currentBatch);

                double currentError = fitter.BestSolution.Error;
                if (currentError < 1e-12) break;

                double relativeImprovement = (previousBest - currentError) / Math.Max(1.0, currentError);
                if (relativeImprovement < tolerance)
                {
                    stagnantBatches++;
                    if (stagnantBatches >= 3 && fitter.IterationCount >= 60) break;
                }
                else
                {
                    stagnantBatches = 0;
                }

                previousBest = currentError;
            }

            FitSolution best = fitter.BestSolution;

            double sumOfSquares = 0;
            double maxResidual = 0;
            double totalSumOfSquares = 0;

            double meanY = 0;
            for (int i = 0; i < ys.Length; i++) meanY += ys[i];
            meanY /= ys.Length;

            for (int i = 0; i < xs.Length; i++)
            {
                double predicted = model(xs[i], best.Parameters);
                double difference = ys[i] - predicted;

                sumOfSquares += difference * difference;

                double absolute = Math.Abs(difference);
                if (absolute > maxResidual) maxResidual = absolute;

                totalSumOfSquares += (ys[i] - meanY) * (ys[i] - meanY);
            }

            FitResult result = new FitResult();
            result.Parameters = best.Parameters;
            result.SSE = sumOfSquares;
            result.RMSE = Math.Sqrt(sumOfSquares / xs.Length);
            result.MaxResidual = maxResidual;
            result.Iterations = best.Iterations;
            result.Improvements = best.ImprovementCount;
            result.R2 = totalSumOfSquares > 0 ? 1.0 - (sumOfSquares / totalSumOfSquares) : 1.0;
            return result;
        }

        /// <summary>Evaluate a model over a vector of X.</summary>
        public static double[] Predict(double[] xs, FitFunction model, double[] parameters)
        {
            if (xs == null || xs.Length == 0)
                throw new ArgumentException("xs must be a non-empty vector");

            if (parameters == null || parameters.Length == 0)
                throw new ArgumentException("p must be a non-empty parameter vector");

            double[] predicted = new double[xs.Length];
            for (int i = 0; i < xs.Length; i++)
                predicted[i] = model(xs[i], parameters);
            return predicted;
        }

        /// <summary>Coefficient of determination R^2 = 1 - SSE/SST.</summary>
        public static double R2(double[] observed, double[] predicted)
        {
            if (observed == null || predicted == null)
                throw new ArgumentNullException("observed", "ys and y_pred are required");

            if (observed.Length != predicted.Length || observed.Length == 0)
            {
                throw new ArgumentException("ys (length " + observed.Length + ") and y_pred (length " +
                                            predicted.Length +
                                            ") must have identical non-zero lengths");
            }

            double mean = 0;
            for (int i = 0; i < observed.Length; i++) mean += observed[i];
            mean /= observed.Length;

            double totalSumOfSquares = 0;
            double residualSumOfSquares = 0;
            for (int i = 0; i < observed.Length; i++)
            {
                totalSumOfSquares += (observed[i] - mean) * (observed[i] - mean);
                residualSumOfSquares += (observed[i] - predicted[i]) * (observed[i] - predicted[i]);
            }

            return totalSumOfSquares > 0 ? 1.0 - (residualSumOfSquares / totalSumOfSquares) : 1.0;
        }

        /// <summary>
        /// Flatten a fit into the row order <c>swarmfit_details</c> documents:
        /// [SSE; RMSE; MaxResidual; Iterations; Improvements].
        /// </summary>
        public static double[] Details(FitResult fit)
        {
            double[] details = new double[5];
            details[0] = fit.SSE;
            details[1] = fit.RMSE;
            details[2] = fit.MaxResidual;
            details[3] = fit.Iterations;
            details[4] = fit.Improvements;
            return details;
        }

        /// <summary>
        /// Search box for the convenience linear fit, derived from the data range
        /// so the caller does not have to supply bounds.
        /// </summary>
        public static void LinearBounds(double[] xs, double[] ys,
                                        out double[] parameterMin, out double[] parameterMax)
        {
            if (xs == null || ys == null || xs.Length == 0 || ys.Length == 0)
                throw new ArgumentException("xs and ys must be non-empty vectors");

            double xMin = xs[0], xMax = xs[0];
            for (int i = 1; i < xs.Length; i++)
            {
                if (xs[i] < xMin) xMin = xs[i];
                if (xs[i] > xMax) xMax = xs[i];
            }

            double yMin = ys[0], yMax = ys[0];
            for (int i = 1; i < ys.Length; i++)
            {
                if (ys[i] < yMin) yMin = ys[i];
                if (ys[i] > yMax) yMax = ys[i];
            }

            double dx = xMax - xMin;
            if (Math.Abs(dx) < 1e-12) dx = 1.0;
            double approximateSlope = (yMax - yMin) / dx;

            parameterMin = new double[2];
            parameterMin[0] = approximateSlope - 10 * Math.Abs(approximateSlope) - 100;
            parameterMin[1] = yMin - 10 * Math.Abs(yMin) - 100;

            parameterMax = new double[2];
            parameterMax[0] = approximateSlope + 10 * Math.Abs(approximateSlope) + 100;
            parameterMax[1] = yMax + 10 * Math.Abs(yMax) + 100;
        }
    }
}
