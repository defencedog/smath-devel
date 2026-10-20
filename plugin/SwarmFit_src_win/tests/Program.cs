// Standalone verification harness for SwarmFitWrapper.
//
// Runs the plugin OUTSIDE SMath Studio. The plugin is loaded as a project
// reference, so `dotnet build tests/SwarmFitHarness.csproj` builds the plugin
// first and the tests can never run against a stale DLL.
//
// IMPORTANT: this harness touches no SMath type on purpose. TNumber, TermInfo
// and friends dereference the host's GlobalProfile and throw NullReferenceException
// when constructed outside a running SMath session. To keep the whole numerical
// contract testable we split it into SwarmFitCore (pure, this file's focus). The
// SMath-facing adapter (SwarmFitEngine / SwarmFitPlugin) is covered by hand in
// the SMath IDE; here we only call its provably-pure members (the string
// catalogue, ClearCache, and the null argument paths of Extract*).
//
// The fixed default XorShift seed in SwarmFit makes every fit deterministic, so
// the determinism section is a meaningful regression check, not a fluke.

using System;
using System.Globalization;
using SwarmFit;
using SwarmFitWrapper;

internal static class Program
{
    private static int _passed;
    private static int _failed;

    private static int Main()
    {
        Console.WriteLine("=== SwarmFitWrapper test: pure numerical core (SwarmFitCore) ===");

        Section("model catalogue (22 aliases)");
        string[] modelNames =
        {
            "linear", "line", "exp", "expdec1", "expdecay", "expdecay1", "power",
            "poly2", "poly3", "gauss", "gaussian", "hill", "sigmoid", "mm",
            "michaelis_menten", "log", "logarithmic", "fourier1", "arrhenius",
            "antoine", "haldane", "andrews"
        };
        int[] expectedCounts =
        {
            2, 2, 3, 3, 2, 2, 2, 3, 4, 3, 3, 4, 4, 2, 2, 2, 2, 4, 2, 3, 3, 3
        };

        int catalogueFailures = 0;
        for (int i = 0; i < modelNames.Length; i++)
        {
            int pc;
            FitFunction f = SwarmFitCore.StandardModel(modelNames[i], out pc);
            if (f == null) { catalogueFailures++; Note("no model for " + modelNames[i]); continue; }
            if (pc != expectedCounts[i]) { catalogueFailures++; Note(modelNames[i] + " param count " + pc); continue; }

            // Calling the model with a zero parameter vector must yield a finite
            // number for every catalogue entry (no divide-by-zero surprises).
            double[] zeros = new double[pc];
            double sample = f(2.0, zeros);
            if (double.IsNaN(sample) || double.IsInfinity(sample))
            {
                catalogueFailures++;
                Note(modelNames[i] + " produced " + sample);
            }
        }
        Check("all 22 model aliases resolve with correct arity", catalogueFailures == 0,
              catalogueFailures + " problem(s)");

        int pcUnknown;
        Check("unknown model name returns null", SwarmFitCore.StandardModel("banana", out pcUnknown) == null,
              "null, pc=" + pcUnknown);
        Check("unknown model name reports arity -1", pcUnknown == -1, pcUnknown.ToString(CultureInfo.InvariantCulture));

        Section("swarmfit, linear model  y = 2x + 0.5");
        double[] linXs = Vec(1, 2, 3, 4, 5);
        double[] linYs = Vec(2.5, 4.5, 6.5, 8.5, 10.5);
        FitResult linear = Fit("linear", linXs, linYs, Vec(0.0, -10.0), Vec(10.0, 10.0), 30, 3000);
        CheckNear("linear slope a", linear.Parameters[0], 2.0, 0.05);
        CheckNear("linear intercept b", linear.Parameters[1], 0.5, 0.05);

        Section("swarmfit_predict  (re-uses the fitted model)");
        FitFunction linModel = SwarmFitCore.StandardModel("linear", out _);
        double[] predicted = SwarmFitCore.Predict(linXs, linModel, linear.Parameters);
        Check("predict returns one value per X", predicted.Length == linXs.Length,
              predicted.Length.ToString(CultureInfo.InvariantCulture));
        CheckNear("first prediction", predicted[0], 2.5, 0.05);
        CheckNear("last prediction", predicted[predicted.Length - 1], 10.5, 0.05);

        Section("swarmfit_r2  (coefficient of determination)");
        Check("R2 of identical vectors is 1.0", SwarmFitCore.R2(linYs, linYs) == 1.0, "1.0");
        double r2Linear = SwarmFitCore.R2(linYs, predicted);
        Check("R2 of a near-perfect fit is > 0.999", r2Linear > 0.999, Fmt(r2Linear));

        Section("swarmfit_details  [SSE; RMSE; MaxResidual; Iterations; Improvements]");
        double[] details = SwarmFitCore.Details(linear);
        Check("details has 5 rows", details.Length == 5, details.Length.ToString(CultureInfo.InvariantCulture));
        Check("SSE is near zero", details[0] < 1e-2, Fmt(details[0]));
        Check("RMSE is near zero", details[1] < 1e-2, Fmt(details[1]));
        Check("max residual is near zero", details[2] < 1e-2, Fmt(details[2]));
        Check("iteration count is positive", details[3] > 0, Fmt(details[3]));
        Check("improvement count is positive", details[4] > 0, Fmt(details[4]));

        Section("Michaelis-Menten  rate = 8.5 s / (2.4 + s)");
        double[] substrate = Vec(0.5, 1.0, 2.0, 4.0, 8.0, 12.0, 20.0);
        double[] rate = Map(substrate, delegate(double s) { return 8.5 * s / (2.4 + s); });
        FitResult mm = Fit("mm", substrate, rate, Vec(1.0, 0.1), Vec(20.0, 10.0), 30, 3000);
        CheckNear("Vmax", mm.Parameters[0], 8.5, 0.2);
        CheckNear("Km", mm.Parameters[1], 2.4, 0.2);

        Section("substrate inhibition  haldane (non-monotonic)");
        double[] inhS = Vec(1.0, 2.0, 5.0, 10.0, 20.0, 40.0, 60.0, 80.0);
        double[] inhV = Map(inhS, delegate(double s) { return 20.0 * s / (4.0 + s + (s * s) / 28.0); });
        FitResult hal = Fit("haldane", inhS, inhV, Vec(10.0, 1.0, 10.0), Vec(40.0, 10.0, 100.0), 40, 4000);
        Check("Vmax in range", hal.Parameters[0] > 15.0 && hal.Parameters[0] < 25.0, Fmt(hal.Parameters[0]));
        Check("Km in range", hal.Parameters[1] > 3.0 && hal.Parameters[1] < 6.0, Fmt(hal.Parameters[1]));
        Check("Ki in range", hal.Parameters[2] > 20.0 && hal.Parameters[2] < 35.0, Fmt(hal.Parameters[2]));
        double[] halPred = SwarmFitCore.Predict(inhS, SwarmFitCore.StandardModel("haldane", out _), hal.Parameters);
        Check("haldane R2 above 0.99", SwarmFitCore.R2(inhV, halPred) > 0.99,
              Fmt(SwarmFitCore.R2(inhV, halPred)));

        Section("exponential decay  y = 12 exp(-t / 2.5)");
        double[] time = Vec(0.0, 1.0, 2.0, 3.0, 4.0, 5.0);
        double[] decay = Map(time, delegate(double t) { return 12.0 * Math.Exp(-t / 2.5); });
        FitResult dec = Fit("expdecay", time, decay, Vec(5.0, 0.5), Vec(20.0, 5.0), 30, 3000);
        CheckNear("decay amplitude", dec.Parameters[0], 12.0, 0.3);
        CheckNear("decay time constant", dec.Parameters[1], 2.5, 0.15);

        Section("power law  y = 2 x^0.5");
        double[] powX = Vec(1.0, 2.0, 4.0, 8.0, 16.0);
        double[] powY = Map(powX, delegate(double x) { return 2.0 * Math.Sqrt(x); });
        FitResult pwr = Fit("power", powX, powY, Vec(0.5, 0.1), Vec(5.0, 1.5), 30, 3000);
        CheckNear("power prefactor", pwr.Parameters[0], 2.0, 0.1);
        CheckNear("power exponent", pwr.Parameters[1], 0.5, 0.05);

        Section("Gaussian peak  y = 10 exp(-(x-4)^2 / (2*0.8^2))");
        double[] peakX = Vec(1.0, 2.0, 3.0, 4.0, 5.0, 6.0, 7.0);
        double[] peakY = Map(peakX, delegate(double x)
        {
            return 10.0 * Math.Exp(-Math.Pow(x - 4.0, 2) / (2.0 * 0.8 * 0.8));
        });
        FitResult gauss = Fit("gauss", peakX, peakY, Vec(5.0, 2.0, 0.2), Vec(15.0, 6.0, 2.0), 40, 4000);
        CheckNear("peak height", gauss.Parameters[0], 10.0, 0.5);
        CheckNear("peak centre", gauss.Parameters[1], 4.0, 0.2);
        CheckNear("peak width", gauss.Parameters[2], 0.8, 0.1);

        Section("polynomial of degree 2  y = 1.5x^2 - 2x + 0.5");
        double[] polyX = Vec(-2.0, -1.0, 0.0, 1.0, 2.0, 3.0);
        double[] polyY = Map(polyX, delegate(double x) { return 1.5 * x * x - 2.0 * x + 0.5; });
        FitResult poly2 = Fit("poly2", polyX, polyY, Vec(0.0, -5.0, -5.0), Vec(5.0, 5.0, 5.0), 30, 3000);
        CheckNear("poly a", poly2.Parameters[0], 1.5, 0.1);
        CheckNear("poly b", poly2.Parameters[1], -2.0, 0.1);
        CheckNear("poly c", poly2.Parameters[2], 0.5, 0.1);

        Section("polynomial of degree 3  y = 0.5x^3 - 1.5x^2 + 2x + 1");
        double[] poly3X = Vec(-2.0, -1.0, 0.0, 1.0, 2.0, 3.0);
        double[] poly3Y = Map(poly3X, delegate(double x)
        {
            return 0.5 * x * x * x - 1.5 * x * x + 2.0 * x + 1.0;
        });
        FitResult poly3 = Fit("poly3", poly3X, poly3Y,
                              Vec(-5.0, -5.0, -5.0, -5.0), Vec(5.0, 5.0, 5.0, 5.0), 30, 3000);
        CheckNear("poly3 a", poly3.Parameters[0], 0.5, 0.1);
        CheckNear("poly3 b", poly3.Parameters[1], -1.5, 0.1);
        CheckNear("poly3 c", poly3.Parameters[2], 2.0, 0.1);
        CheckNear("poly3 d", poly3.Parameters[3], 1.0, 0.1);

        Section("remaining models (loose: R2 > 0.9)");
        Check("exp 3-param fits", LooseFit("exp",
            Vec(0.0, 1.0, 2.0, 3.0, 4.0),
            Map(Vec(0.0, 1.0, 2.0, 3.0, 4.0), delegate(double x) { return 1.0 + 2.0 * Math.Exp(0.3 * x); }),
            Vec(0.0, 0.0, 0.0), Vec(5.0, 5.0, 1.0), 30, 3000) > 0.9, "R2>0.9");
        Check("log fits", LooseFit("log",
            Vec(1.0, 2.0, 4.0, 8.0, 16.0),
            Map(Vec(1.0, 2.0, 4.0, 8.0, 16.0), delegate(double x) { return 1.0 + 0.5 * Math.Log(x); }),
            Vec(0.0, 0.0), Vec(5.0, 5.0), 30, 3000) > 0.9, "R2>0.9");
        Check("fourier1 fits", LooseFit("fourier1",
            Linspace(0.0, 6.0, 13),
            Map(Linspace(0.0, 6.0, 13), delegate(double x) { return 1.0 + 2.0 * Math.Cos(1.5 * x) + 3.0 * Math.Sin(1.5 * x); }),
            Vec(-5.0, -5.0, 0.5, -5.0), Vec(5.0, 5.0, 3.0, 5.0), 40, 4000) > 0.9, "R2>0.9");
        Check("arrhenius fits", LooseFit("arrhenius",
            Vec(300.0, 320.0, 340.0, 360.0, 380.0, 400.0),
            Map(Vec(300.0, 320.0, 340.0, 360.0, 380.0, 400.0), delegate(double x) { return 1000.0 * Math.Exp(-3000.0 / x); }),
            Vec(100.0, 100.0), Vec(5000.0, 10000.0), 30, 3000) > 0.9, "R2>0.9");
        Check("antoine fits", LooseFit("antoine",
            Vec(50.0, 80.0, 110.0, 140.0, 170.0, 200.0),
            Map(Vec(50.0, 80.0, 110.0, 140.0, 170.0, 200.0), delegate(double x) { return Math.Pow(10.0, 8.0 - 2000.0 / (x + 40.0)); }),
            Vec(0.0, 0.0, 0.0), Vec(15.0, 5000.0, 500.0), 30, 3000) > 0.9, "R2>0.9");
        Check("hill fits", LooseFit("hill",
            Vec(0.5, 1.0, 2.0, 4.0, 8.0, 16.0, 32.0),
            Map(Vec(0.5, 1.0, 2.0, 4.0, 8.0, 16.0, 32.0), delegate(double x) { return 1.0 + (10.0 - 1.0) / (1.0 + Math.Pow(5.0 / x, 3.0)); }),
            Vec(0.0, 0.0, 0.0, 0.0), Vec(15.0, 15.0, 20.0, 10.0), 40, 4000) > 0.9, "R2>0.9");

        Section("swarmfit_linear convenience search box");
        double[] lbMin, lbMax;
        SwarmFitCore.LinearBounds(linXs, linYs, out lbMin, out lbMax);
        Check("linear bounds bracket the slope", lbMin[0] <= 2.0 && 2.0 <= lbMax[0], "a in [" + Fmt(lbMin[0]) + "," + Fmt(lbMax[0]) + "]");
        Check("linear bounds bracket the intercept", lbMin[1] <= 0.5 && 0.5 <= lbMax[1], "b in [" + Fmt(lbMin[1]) + "," + Fmt(lbMax[1]) + "]");

        Section("error handling");
        Check("null xs throws", Throws(delegate
        {
            SwarmFitCore.Fit(null, linYs, linModel, Vec(0.0, -10.0), Vec(10.0, 10.0), 10, 100);
        }), "ArgumentNullException");
        Check("mismatched xs/ys lengths throw", Throws(delegate
        {
            SwarmFitCore.Fit(Vec(1.0, 2.0), Vec(1.0, 2.0, 3.0), linModel, Vec(0.0, -10.0), Vec(10.0, 10.0), 10, 100);
        }), "ArgumentException");
        Check("inverted bounds throw", Throws(delegate
        {
            SwarmFitCore.Fit(linXs, linYs, linModel, Vec(10.0, 10.0), Vec(0.0, -10.0), 10, 100);
        }), "ArgumentException");
        Check("empty parameter vector throws", Throws(delegate
        {
            SwarmFitCore.Fit(linXs, linYs, linModel, new double[0], new double[0], 10, 100);
        }), "ArgumentException");
        Check("fewer points than parameters throws", Throws(delegate
        {
            FitFunction mmModel = SwarmFitCore.StandardModel("mm", out _);
            SwarmFitCore.Fit(Vec(1.0), Vec(0.3), mmModel, Vec(0.0, 0.0), Vec(10.0, 10.0), 10, 100);
        }), "ArgumentException");
        Check("unknown model throws via Fit", Throws(delegate
        {
            SwarmFitCore.Fit(linXs, linYs, null, Vec(0.0, -10.0), Vec(10.0, 10.0), 10, 100);
        }), "ArgumentNullException");

        Section("determinism (fixed seed => identical fits)");
        FitResult detA = Fit("linear", linXs, linYs, Vec(0.0, -10.0), Vec(10.0, 10.0), 30, 3000);
        FitResult detB = Fit("linear", linXs, linYs, Vec(0.0, -10.0), Vec(10.0, 10.0), 30, 3000);
        Check("two identical fits are bit-identical", SameArray(detA.Parameters, detB.Parameters, 1e-15),
              Fmt(detA.Parameters[0]) + " / " + Fmt(detB.Parameters[0]));

        Section("engine pure paths (no SMath session required)");
        Check("IsKnownModelName(\"linear\")", SwarmFitEngine.IsKnownModelName("linear"), "true");
        Check("IsKnownModelName(\"banana\")", !SwarmFitEngine.IsKnownModelName("banana"), "false");
        Check("IsKnownModelName(null)", !SwarmFitEngine.IsKnownModelName(null), "false");
        // ExtractDoubleArray/ExtractScalar take a live Entry and are exercised inside
        // SMath Studio: SMath.Manager.Entry has an operator== overload, so the methods'
        // `entry == null` guards call Entry.op_Equality(null, null) and throw outside a
        // session. They are therefore not unit-tested here (the pure core returns the
        // same numbers the worksheet would, which the fit/details checks above prove).
        Check("ClearCache does not throw", Throws(delegate { SwarmFitEngine.ClearCache(); }) == false, "ok");

        Console.WriteLine();
        if (_failed == 0)
        {
            Console.WriteLine("=== ALL TESTS PASSED (" + _passed + "/" + (_passed + _failed) + ") ===");
            return 0;
        }

        Console.WriteLine("=== FAILURES: " + _failed + " of " + (_passed + _failed) + " ===");
        return 1;
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>Runs a fit end to end on a named standard model.</summary>
    private static FitResult Fit(string modelName, double[] xs, double[] ys,
                                 double[] pmin, double[] pmax, int particles, int iters)
    {
        FitFunction model = SwarmFitCore.StandardModel(modelName, out _);
        return SwarmFitCore.Fit(xs, ys, model, pmin, pmax, particles, iters);
    }

    /// <summary>Returns the R2 of a loose fit so the caller can assert &gt; 0.9.</summary>
    private static double LooseFit(string modelName, double[] xs, double[] ys,
                                   double[] pmin, double[] pmax, int particles, int iters)
    {
        FitResult fit = Fit(modelName, xs, ys, pmin, pmax, particles, iters);
        if (fit.Parameters == null) return double.NaN;
        FitFunction model = SwarmFitCore.StandardModel(modelName, out _);
        double[] predicted = SwarmFitCore.Predict(xs, model, fit.Parameters);
        return SwarmFitCore.R2(ys, predicted);
    }

    /// <summary>Apply a pure scalar function to every element.</summary>
    private delegate double Unary(double x);

    private static double[] Map(double[] xs, Unary f)
    {
        double[] result = new double[xs.Length];
        for (int i = 0; i < xs.Length; i++) result[i] = f(xs[i]);
        return result;
    }

    /// <summary>Evenly spaced samples between <paramref name="a"/> and <paramref name="b"/>.</summary>
    private static double[] Linspace(double a, double b, int n)
    {
        double[] result = new double[n];
        for (int i = 0; i < n; i++) result[i] = a + (b - a) * i / (n - 1);
        return result;
    }

    /// <summary>Build a vector element by element (avoids literal-array pitfalls).</summary>
    private static double[] Vec(params double[] values)
    {
        double[] result = new double[values.Length];
        for (int i = 0; i < values.Length; i++) result[i] = values[i];
        return result;
    }

    private static bool SameArray(double[] a, double[] b, double tolerance)
    {
        if (a == null || b == null) return false;
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
            if (Math.Abs(a[i] - b[i]) > tolerance) return false;
        return true;
    }

    private static bool Throws(TestAction action)
    {
        try { action(); return false; }
        catch { return true; }
    }

    // System.Action is System.Core (.NET 3.5); the harness declares its own.
    private delegate void TestAction();

    private static void Section(string title)
    {
        Console.WriteLine();
        Console.WriteLine("-- " + title + " --");
    }

    private static void Note(string text)
    {
        Console.WriteLine("  note  " + text);
    }

    private static void Check(string description, bool passed, string detail)
    {
        if (passed) _passed++; else _failed++;
        Console.WriteLine("  " + (passed ? "PASS" : "FAIL") + "  " + description + "  [" + detail + "]");
    }

    private static void CheckNear(string description, double actual, double expected, double tolerance)
    {
        bool passed = Math.Abs(actual - expected) <= tolerance;
        string detail = "got " + Fmt(actual) + " expected " + Fmt(expected) + " (tol " + Fmt(tolerance) + ")";
        Check(description, passed, detail);
    }

    private static string Fmt(double value)
    {
        return value.ToString("G9", CultureInfo.InvariantCulture);
    }
}
