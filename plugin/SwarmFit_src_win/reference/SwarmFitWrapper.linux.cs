using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using SMath.Manager;
using SMath.Controls;
using SMath.Math;
using SMath.Math.Numeric;
using SMath.Math.Symbolic;
using SwarmFit;

[assembly: AssemblyTitle("SwarmFitWrapper")]
[assembly: AssemblyDescription("Particle Swarm Optimization Curve Fitting Plugin for SMath Studio wrapping SwarmFit")]
[assembly: AssemblyVersion("1.0.0.1")]
[assembly: AssemblyFileVersion("1.0.0.1")]
[assembly: Guid("b0a439e5-20d5-46a3-bcb5-596cbd1d9934")]
[assembly: ComVisible(false)]

namespace SwarmFitWrapper
{
    public class FitResult
    {
        public double[] Parameters;
        public double SSE;
        public double RMSE;
        public double MaxResidual;
        public int Iterations;
        public int Improvements;
        public double R2;
    }

    public static class SwarmFitEngine
    {
        private static readonly object _syncLock = new object();
        private static readonly Dictionary<string, double[]> _matrixByName = new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, double> _scalarByName = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, double[]> _matrixBySig = new Dictionary<string, double[]>();
        private static readonly Dictionary<string, double> _scalarBySig = new Dictionary<string, double>();

        public static void ClearCache()
        {
            lock (_syncLock)
            {
                _matrixByName.Clear();
                _scalarByName.Clear();
                _matrixBySig.Clear();
                _scalarBySig.Clear();
            }
        }

        public static string GetEntrySignature(Entry entry)
        {
            if (entry == null) return string.Empty;
            if (entry.Items == null || entry.Items.Length == 0) return $"{entry.Type}:{entry.Text}";
            var sb = new System.Text.StringBuilder();
            sb.Append(entry.Text).Append('(');
            for (int i = 0; i < entry.Items.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(GetEntrySignature(entry.Items[i]));
            }
            sb.Append(')');
            return sb.ToString();
        }

        public static bool IsSwarmFitFunction(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            switch (name.ToLowerInvariant())
            {
                case "swarmfit":
                case "swarmfit_predict":
                case "swarmfit_details":
                case "swarmfit_r2":
                case "swarmfit_linear":
                case "swarmfit_exp":
                case "swarmfit_expdecay":
                case "swarmfit_power":
                case "swarmfit_gauss":
                case "swarmfit_poly":
                    return true;
                default:
                    return false;
            }
        }

        public static bool IsKnownModelName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            switch (name.ToLowerInvariant())
            {
                case "linear":
                case "line":
                case "exp":
                case "expdec1":
                case "expdecay":
                case "expdecay1":
                case "power":
                case "poly2":
                case "poly3":
                case "gauss":
                case "gaussian":
                case "hill":
                case "sigmoid":
                case "mm":
                case "michaelis_menten":
                case "log":
                case "logarithmic":
                case "fourier1":
                case "arrhenius":
                case "antoine":
                case "haldane":
                case "andrews":
                    return true;
                default:
                    return false;
            }
        }

        public static Definition FindDefiningDefinition(Entry value, Store context)
        {
            if (context == null || value == null) return null;
            try
            {
                // 1. Direct object identity match on Result
                var def = context.LastOrDefault(d => d != null && d.Result != null && ReferenceEquals(d.Result, value));
                if (def != null) return def;

                // 2. Exact match on signature (Text and argument texts)
                string valSig = GetEntrySignature(value);
                def = context.LastOrDefault(d => d != null && d.Result != null && GetEntrySignature(d.Result) == valSig);
                if (def != null) return def;

                // 3. Match by function name and argument count
                if (value.Type == TermType.Function)
                {
                    int argc = value.Items != null ? value.Items.Length : 0;
                    def = context.LastOrDefault(d => d != null && d.Result != null &&
                        d.Result.Type == TermType.Function &&
                        string.Equals(d.Result.Text, value.Text, StringComparison.OrdinalIgnoreCase) &&
                        (d.Result.Items != null ? d.Result.Items.Length : 0) == argc);
                    if (def != null) return def;
                }
            }
            catch { }
            return null;
        }

        public static void RecordEvaluationResult(Entry value, Store context, Entry result, double[] arrayResult, double? scalarResult)
        {
            if (value == null) return;

            string sig = GetEntrySignature(value);
            lock (_syncLock)
            {
                if (arrayResult != null && !string.IsNullOrEmpty(sig))
                    _matrixBySig[sig] = arrayResult;
                if (scalarResult.HasValue && !string.IsNullOrEmpty(sig))
                    _scalarBySig[sig] = scalarResult.Value;
            }

            if (context != null)
            {
                var def = FindDefiningDefinition(value, context);
                if (def != null)
                {
                    if (result != null)
                    {
                        def.Result = result;
                        def.IsCalculated = true;
                    }
                    if (!string.IsNullOrEmpty(def.Name))
                    {
                        lock (_syncLock)
                        {
                            if (arrayResult != null)
                                _matrixByName[def.Name] = arrayResult;
                            if (scalarResult.HasValue)
                                _scalarByName[def.Name] = scalarResult.Value;
                        }
                    }
                }
            }
        }

        public static double ExtractScalar(Entry entry, Store context)
        {
            if (entry == null) return 0.0;

            // 1. Check direct name cache
            if (entry.Type == TermType.Operand && !string.IsNullOrEmpty(entry.Text))
            {
                lock (_syncLock)
                {
                    if (_scalarByName.TryGetValue(entry.Text, out double cachedScalar))
                        return cachedScalar;
                    if (_matrixByName.TryGetValue(entry.Text, out double[] cachedArr) && cachedArr.Length > 0)
                        return cachedArr[0];
                }
            }

            // 2. Check direct signature cache
            string sig = GetEntrySignature(entry);
            if (!string.IsNullOrEmpty(sig))
            {
                lock (_syncLock)
                {
                    if (_scalarBySig.TryGetValue(sig, out double cachedSig))
                        return cachedSig;
                }
            }

            // 3. Check context definitions
            if (context != null && entry.Type == TermType.Operand && !string.IsNullOrEmpty(entry.Text))
            {
                var def = context.LastOrDefault(d => d.Name == entry.Text);
                if (def != null && def.Result != null)
                {
                    double s = ExtractScalar(def.Result, null);
                    if (!double.IsNaN(s) && s != 0.0)
                    {
                        lock (_syncLock) { _scalarByName[entry.Text] = s; }
                        return s;
                    }

                    if (def.Result.Type == TermType.Function && IsSwarmFitFunction(def.Result.Text))
                    {
                        if (TryEvaluateSwarmFit(def.Result, context, out Entry calcRes, out double[] arrRes, out double? scRes))
                        {
                            def.Result = calcRes;
                            def.IsCalculated = true;
                            if (scRes.HasValue)
                            {
                                lock (_syncLock) { _scalarByName[entry.Text] = scRes.Value; }
                                return scRes.Value;
                            }
                            if (arrRes != null && arrRes.Length > 0)
                            {
                                lock (_syncLock) { _matrixByName[entry.Text] = arrRes; }
                                return arrRes[0];
                            }
                        }
                    }
                }
            }

            // 4. If entry is a SwarmFit function call
            if (entry.Type == TermType.Function && IsSwarmFitFunction(entry.Text))
            {
                if (TryEvaluateSwarmFit(entry, context, out Entry calcRes, out double[] arrRes, out double? scRes))
                {
                    if (scRes.HasValue) return scRes.Value;
                    if (arrRes != null && arrRes.Length > 0) return arrRes[0];
                }
            }

            // 5. Try NumericCalculation if context available
            if (context != null)
            {
                try
                {
                    TNumber num = Computation.NumericCalculation(entry, context);
                    if (num != null)
                    {
                        if (num.obj is TDouble td) return td.ToDouble();
                        if (num.obj is TFraction tf) return tf.ToDouble();
                        if (num.obj is TMatrix tm && tm.unit.GetLength(0) > 0 && tm.unit.GetLength(1) > 0)
                            return tm.unit[0, 0].obj.ToDouble();
                    }
                }
                catch { }
            }

            // 6. Operators: -, +, *, /, ^
            if (entry.Text == "-" && entry.Items != null && entry.Items.Length == 1)
            {
                return -ExtractScalar(entry.Items[0], context);
            }

            if (entry.Items != null && entry.Items.Length == 2)
            {
                if (entry.Text == "*")
                    return ExtractScalar(entry.Items[0], context) * ExtractScalar(entry.Items[1], context);
                if (entry.Text == "/")
                    return ExtractScalar(entry.Items[0], context) / ExtractScalar(entry.Items[1], context);
                if (entry.Text == "+")
                    return ExtractScalar(entry.Items[0], context) + ExtractScalar(entry.Items[1], context);
                if (entry.Text == "-")
                    return ExtractScalar(entry.Items[0], context) - ExtractScalar(entry.Items[1], context);
                if (entry.Text == "^")
                    return Math.Pow(ExtractScalar(entry.Items[0], context), ExtractScalar(entry.Items[1], context));
            }

            if (double.TryParse(entry.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double val))
                return val;

            return 0.0;
        }

        public static double[] ExtractDoubleArray(Entry entry, Store context)
        {
            if (entry == null) return new double[0];

            // 1. Direct variable lookup from cache
            if (entry.Type == TermType.Operand && !string.IsNullOrEmpty(entry.Text))
            {
                lock (_syncLock)
                {
                    if (_matrixByName.TryGetValue(entry.Text, out double[] cached))
                        return cached;
                }
            }

            // 2. Direct signature lookup from cache
            string sig = GetEntrySignature(entry);
            if (!string.IsNullOrEmpty(sig))
            {
                lock (_syncLock)
                {
                    if (_matrixBySig.TryGetValue(sig, out double[] cachedSig))
                        return cachedSig;
                }
            }

            // 3. Direct context definition lookup
            if (context != null && entry.Type == TermType.Operand && !string.IsNullOrEmpty(entry.Text))
            {
                var def = context.LastOrDefault(d => d.Name == entry.Text);
                if (def != null && def.Result != null)
                {
                    // 3a. If def.Result is already a matrix AST
                    double[] arr = ExtractDoubleArray(def.Result, null);
                    if (arr != null && arr.Length > 0)
                    {
                        lock (_syncLock) { _matrixByName[entry.Text] = arr; }
                        return arr;
                    }

                    // 3b. If def.Result is a SwarmFit function call
                    if (def.Result.Type == TermType.Function && IsSwarmFitFunction(def.Result.Text))
                    {
                        if (TryEvaluateSwarmFit(def.Result, context, out Entry calculatedResult, out double[] evalArr, out double? sc))
                        {
                            def.Result = calculatedResult;
                            def.IsCalculated = true;
                            if (evalArr != null && evalArr.Length > 0)
                            {
                                lock (_syncLock) { _matrixByName[entry.Text] = evalArr; }
                                return evalArr;
                            }
                        }
                    }
                }
            }

            // 4. Direct AST matrix inspection (mat, matrix, "")
            if (entry.Items != null && entry.Items.Length >= 2)
            {
                int n = entry.Items.Length;

                // SMath standard matrix: Text == "mat", "matrix", or "" (from TMatrix.ToTerms())
                // Arguments layout: [val0, val1, ..., val(k-1), rows, cols]
                if (entry.Text == "mat" || entry.Text == "matrix" || string.IsNullOrEmpty(entry.Text))
                {
                    int rows = (int)ExtractScalar(entry.Items[n - 2], null);
                    int cols = (int)ExtractScalar(entry.Items[n - 1], null);
                    if (rows > 0 && cols > 0 && rows * cols == n - 2)
                    {
                        double[] res = new double[rows * cols];
                        for (int i = 0; i < res.Length; i++)
                        {
                            res[i] = ExtractScalar(entry.Items[i], null);
                        }
                        return res;
                    }
                }

                // Legacy synthetic format: [rows, cols, val0, val1, ...]
                if (entry.Text == "mat" || entry.Text == "matrix")
                {
                    int rows = (int)ExtractScalar(entry.Items[0], null);
                    int cols = (int)ExtractScalar(entry.Items[1], null);
                    if (rows > 0 && cols > 0 && rows * cols == n - 2)
                    {
                        double[] res = new double[rows * cols];
                        for (int i = 0; i < res.Length; i++)
                        {
                            res[i] = ExtractScalar(entry.Items[2 + i], null);
                        }
                        return res;
                    }
                }
            }

            // 5. Direct SwarmFit function call evaluation
            if (entry.Type == TermType.Function && IsSwarmFitFunction(entry.Text))
            {
                if (TryEvaluateSwarmFit(entry, context, out Entry calculatedResult, out double[] evalArr, out double? sc))
                {
                    if (evalArr != null && evalArr.Length > 0)
                        return evalArr;
                }
            }

            // 6. Try NumericCalculation if context available
            if (context != null)
            {
                try
                {
                    TNumber num = Computation.NumericCalculation(entry, context);
                    if (num != null)
                    {
                        if (num.obj is TMatrix mat)
                        {
                            int rows = mat.unit.GetLength(0);
                            int cols = mat.unit.GetLength(1);
                            double[] res = new double[rows * cols];
                            int idx = 0;
                            for (int r = 0; r < rows; r++)
                            {
                                for (int c = 0; c < cols; c++)
                                {
                                    res[idx++] = mat.unit[r, c].obj.ToDouble();
                                }
                            }
                            if (entry.Type == TermType.Operand && !string.IsNullOrEmpty(entry.Text))
                            {
                                lock (_syncLock) { _matrixByName[entry.Text] = res; }
                            }
                            return res;
                        }
                        if (num.obj is TDouble td)
                        {
                            return new double[] { td.ToDouble() };
                        }
                        if (num.obj is TFraction tf)
                        {
                            return new double[] { tf.ToDouble() };
                        }
                    }
                }
                catch { }
            }

            // 7. Secondary fallback: check def.ToEntry() if entry was an operand and NumericCalculation failed
            if (context != null && entry.Type == TermType.Operand && !string.IsNullOrEmpty(entry.Text))
            {
                var def = context.LastOrDefault(d => d.Name == entry.Text);
                if (def != null)
                {
                    try
                    {
                        Entry eDef = def.ToEntry();
                        if (eDef != null && eDef != entry)
                        {
                            double[] arr = ExtractDoubleArray(eDef, null);
                            if (arr != null && arr.Length > 0) return arr;
                        }
                    }
                    catch { }
                }
            }

            // 8. Unary minus
            if (entry.Text == "-" && entry.Items != null && entry.Items.Length == 1)
            {
                return new double[] { -ExtractScalar(entry.Items[0], context) };
            }

            // 9. Scalar parse
            if (double.TryParse(entry.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double scalarVal))
                return new double[] { scalarVal };

            return new double[0];
        }

        public static Entry MakeMatrixResult(double[] vals)
        {
            int rows = vals.Length;
            TNumber[,] units = new TNumber[rows, 1];
            for (int i = 0; i < rows; i++)
            {
                units[i, 0] = new TNumber(new TDouble(vals[i]));
            }
            TMatrix mat = new TMatrix(units);
            return Entry.Create(mat.ToTerms());
        }

        public static Entry MakeMatrix2DResult(double[,] vals)
        {
            int rows = vals.GetLength(0);
            int cols = vals.GetLength(1);
            TNumber[,] units = new TNumber[rows, cols];
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    units[r, c] = new TNumber(new TDouble(vals[r, c]));
                }
            }
            TMatrix mat = new TMatrix(units);
            return Entry.Create(mat.ToTerms());
        }

        public static Entry MakeDoubleResult(double val)
        {
            var td = new TDouble(val);
            return Entry.Create(td.ToTerms());
        }

        internal static string StripQuotes(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            int start = 0;
            int end = s.Length;
            while (start < end && (s[start] == '"' || s[start] == '\'' || char.IsWhiteSpace(s[start])))
                start++;
            while (end > start && (s[end - 1] == '"' || s[end - 1] == '\'' || char.IsWhiteSpace(s[end - 1])))
                end--;
            return (start >= end) ? string.Empty : s.Substring(start, end - start);
        }

        public static Func<double, double[], double> ResolveModel(Entry modelArg, Store context, out int paramCount)
        {
            paramCount = -1;
            if (modelArg == null)
                throw new ArgumentNullException(nameof(modelArg));

            string modelStr = null;

            // 1. Quoted string literal: "mm", "haldane", etc.
            string raw = modelArg.Text ?? string.Empty;
            if (raw.StartsWith("\"") && raw.EndsWith("\"") && raw.Length >= 2)
            {
                modelStr = StripQuotes(raw).ToLowerInvariant();
            }
            else
            {
                // 2. Check if identifier directly matches known model name
                string candidate = StripQuotes(raw).ToLowerInvariant();
                if (IsKnownModelName(candidate))
                {
                    modelStr = candidate;
                }
                else if (context != null)
                {
                    // 3. Check if it's a variable in context holding a string model name
                    var defVar = context.LastOrDefault(d => d.Name == raw && !d.IsFunction);
                    if (defVar != null && defVar.Result != null)
                    {
                        string defRaw = defVar.Result.Text ?? string.Empty;
                        if (defRaw.StartsWith("\"") && defRaw.EndsWith("\"") && defRaw.Length >= 2)
                            modelStr = StripQuotes(defRaw).ToLowerInvariant();
                    }

                    // Only call NumericCalculation if not already determined and definitely not a function definition
                    if (string.IsNullOrEmpty(modelStr) && (defVar != null || !context.Any(d => d.Name == raw && d.IsFunction)))
                    {
                        try
                        {
                            TNumber num = Computation.NumericCalculation(modelArg, context);
                            if (num != null && num.obj is TDouble td && td.isText)
                            {
                                modelStr = td.Text ?? td.ToString();
                                if (!string.IsNullOrEmpty(modelStr))
                                    modelStr = StripQuotes(modelStr).ToLowerInvariant();
                            }
                        }
                        catch { }
                    }
                }
            }

            if (!string.IsNullOrEmpty(modelStr))
            {
                switch (modelStr)
                {
                    case "linear":
                    case "line":
                        paramCount = 2;
                        return (x, p) => p[0] * x + p[1];

                    case "exp":
                    case "expdec1":
                        paramCount = 3;
                        return (x, p) => p[0] + p[1] * Math.Exp(p[2] * x);

                    case "expdecay":
                    case "expdecay1":
                        paramCount = 2;
                        return (x, p) => p[0] * Math.Exp(-x / p[1]);

                    case "power":
                        paramCount = 2;
                        return (x, p) => p[0] * Math.Pow(x, p[1]);

                    case "poly2":
                        paramCount = 3;
                        return (x, p) => p[0] * x * x + p[1] * x + p[2];

                    case "poly3":
                        paramCount = 4;
                        return (x, p) => p[0] * x * x * x + p[1] * x * x + p[2] * x + p[3];

                    case "gauss":
                    case "gaussian":
                        paramCount = 3;
                        return (x, p) => p[0] * Math.Exp(-Math.Pow(x - p[1], 2) / (2.0 * p[2] * p[2]));

                    case "hill":
                    case "sigmoid":
                        paramCount = 4;
                        return (x, p) => p[0] + (p[1] - p[0]) / (1.0 + Math.Pow(p[2] / x, p[3]));

                    case "mm":
                    case "michaelis_menten":
                        paramCount = 2;
                        return (x, p) => (p[0] * x) / (p[1] + x);

                    case "log":
                    case "logarithmic":
                        paramCount = 2;
                        return (x, p) => p[0] + p[1] * Math.Log(x);

                    case "fourier1":
                        paramCount = 4;
                        return (x, p) => p[0] + p[1] * Math.Cos(p[2] * x) + p[3] * Math.Sin(p[2] * x);

                    case "arrhenius":
                        paramCount = 2;
                        return (x, p) => p[0] * Math.Exp(-p[1] / x);

                    case "antoine":
                        paramCount = 3;
                        return (x, p) => Math.Pow(10.0, p[0] - p[1] / (x + p[2]));

                    case "haldane":
                    case "andrews":
                        paramCount = 3;
                        return (x, p) => (p[0] * x) / (p[1] + x + (x * x) / p[2]);
                }
            }

            Definition def = null;
            if (context != null && !string.IsNullOrEmpty(modelArg.Text))
            {
                def = context.LastOrDefault(d => d.Name == modelArg.Text);
            }

            Entry templateCall = def != null ? def.ToEntry() : modelArg;
            if (templateCall == null || templateCall.Type != TermType.Function)
            {
                throw new InvalidOperationException($"Model '{modelArg.Text}' is neither a known standard model name nor a defined SMath function.");
            }

            int arity = templateCall.Items != null ? templateCall.Items.Length : 0;
            if (arity == 2)
            {
                // f(x, p) where p is a column vector
                return (x, p) =>
                {
                    try
                    {
                        Entry call = new Entry(templateCall.Text, TermType.Function, new Entry[2]);
                        call.Items[0] = Entry.Create(new TDouble(x).ToTerms());
                        TNumber[,] pMat = new TNumber[p.Length, 1];
                        for (int k = 0; k < p.Length; k++) pMat[k, 0] = new TNumber(new TDouble(p[k]));
                        call.Items[1] = Entry.Create(new TMatrix(pMat).ToTerms());
                        TNumber res = Computation.NumericCalculation(call, context);
                        if (res == null || res.obj == null) return 1e12;
                        double val = res.obj.ToDouble();
                        return (double.IsNaN(val) || double.IsInfinity(val)) ? 1e12 : val;
                    }
                    catch
                    {
                        return 1e12;
                    }
                };
            }
            else if (arity > 2)
            {
                // f(x, p0, p1, ..., pK) where each parameter is a scalar
                paramCount = arity - 1;
                return (x, p) =>
                {
                    try
                    {
                        Entry call = new Entry(templateCall.Text, TermType.Function, new Entry[arity]);
                        call.Items[0] = Entry.Create(new TDouble(x).ToTerms());
                        for (int k = 0; k < p.Length && k < arity - 1; k++)
                        {
                            call.Items[1 + k] = Entry.Create(new TDouble(p[k]).ToTerms());
                        }
                        TNumber res = Computation.NumericCalculation(call, context);
                        if (res == null || res.obj == null) return 1e12;
                        double val = res.obj.ToDouble();
                        return (double.IsNaN(val) || double.IsInfinity(val)) ? 1e12 : val;
                    }
                    catch
                    {
                        return 1e12;
                    }
                };
            }
            else
            {
                throw new InvalidOperationException($"Function '{templateCall.Text}' has arity {arity}, but curve models must accept at least 2 arguments: (x, p) or (x, p0, p1, ...).");
            }
        }

        public static FitResult RunSwarmFit(
            double[] xs, double[] ys,
            Func<double, double[], double> model,
            double[] pMin, double[] pMax,
            int numParticles, int maxIterations)
        {
            lock (_syncLock)
            {
                if (xs.Length != ys.Length)
                    throw new ArgumentException($"xs length ({xs.Length}) does not match ys length ({ys.Length})");
                if (pMin.Length != pMax.Length)
                    throw new ArgumentException($"p_min length ({pMin.Length}) does not match p_max length ({pMax.Length})");
                if (xs.Length < pMin.Length)
                    throw new ArgumentException($"Number of data points ({xs.Length}) must be >= number of parameters ({pMin.Length})");

                ParameterLimits[] limits = new ParameterLimits[pMin.Length];
                for (int i = 0; i < pMin.Length; i++)
                {
                    if (pMin[i] > pMax[i])
                        throw new ArgumentException($"p_min[{i}] ({pMin[i]}) cannot be greater than p_max[{i}] ({pMax[i]})");
                    limits[i] = new ParameterLimits(pMin[i], pMax[i]);
                }

                if (numParticles <= 0) numParticles = 20;
                if (maxIterations <= 0) maxIterations = 5000;

                SwarmFitter fitter = new SwarmFitter(xs, ys, model, limits, numParticles);

                int batchSize = Math.Min(25, Math.Max(10, maxIterations / 10));
                double prevBestError = double.MaxValue;
                int stagnantBatches = 0;
                double tol = 1e-5;

                while (fitter.IterationCount < maxIterations)
                {
                    int remaining = maxIterations - fitter.IterationCount;
                    int curBatch = Math.Min(batchSize, remaining);
                    fitter.Solve(maxIterations: curBatch);

                    double currentError = fitter.BestSolution.Error;
                    if (currentError < 1e-12)
                        break;

                    double relImpr = (prevBestError - currentError) / Math.Max(1.0, currentError);
                    if (relImpr < tol)
                    {
                        stagnantBatches++;
                        if (stagnantBatches >= 3 && fitter.IterationCount >= 60)
                        {
                            break;
                        }
                    }
                    else
                    {
                        stagnantBatches = 0;
                    }

                    prevBestError = currentError;
                }

                FitSolution best = fitter.BestSolution;

                double sse = 0;
                double maxRes = 0;
                double ssTot = 0, yMean = 0;
                for (int i = 0; i < ys.Length; i++) yMean += ys[i];
                yMean /= ys.Length;

                for (int i = 0; i < xs.Length; i++)
                {
                    double pred = model(xs[i], best.Parameters);
                    double diff = ys[i] - pred;
                    sse += diff * diff;
                    maxRes = Math.Max(maxRes, Math.Abs(diff));
                    ssTot += (ys[i] - yMean) * (ys[i] - yMean);
                }
                double rmse = Math.Sqrt(sse / xs.Length);
                double r2 = ssTot > 0 ? 1.0 - (sse / ssTot) : 1.0;

                return new FitResult
                {
                    Parameters = best.Parameters,
                    SSE = sse,
                    RMSE = rmse,
                    MaxResidual = maxRes,
                    Iterations = best.Iterations,
                    Improvements = best.ImprovementCount,
                    R2 = r2
                };
            }
        }

        public static bool TryEvaluateSwarmFit(Entry value, Store context, out Entry result, out double[] arrayResult, out double? scalarResult)
        {
            result = null;
            arrayResult = null;
            scalarResult = null;

            if (value == null || value.Type != TermType.Function)
                return false;

            string fnName = (value.Text ?? string.Empty).ToLowerInvariant();
            Entry[] args = value.Items ?? new Entry[0];
            int argc = args.Length;

            try
            {
                switch (fnName)
                {
                    case "swarmfit":
                    {
                        if (argc != 5 && argc != 7) return false;
                        double[] xs = ExtractDoubleArray(args[0], context);
                        double[] ys = ExtractDoubleArray(args[1], context);
                        int pCount;
                        var model = ResolveModel(args[2], context, out pCount);
                        double[] pMin = ExtractDoubleArray(args[3], context);
                        double[] pMax = ExtractDoubleArray(args[4], context);

                        int particles = 20;
                        int maxIter = 5000;
                        if (argc == 7)
                        {
                            double[] pArg = ExtractDoubleArray(args[5], context);
                            double[] iArg = ExtractDoubleArray(args[6], context);
                            if (pArg.Length > 0) particles = (int)Math.Round(pArg[0]);
                            if (iArg.Length > 0) maxIter = (int)Math.Round(iArg[0]);
                        }

                        FitResult res = RunSwarmFit(xs, ys, model, pMin, pMax, particles, maxIter);
                        result = MakeMatrixResult(res.Parameters);
                        arrayResult = res.Parameters;
                        RecordEvaluationResult(value, context, result, arrayResult, null);
                        return true;
                    }

                    case "swarmfit_predict":
                    {
                        if (argc != 3) return false;
                        double[] xs = ExtractDoubleArray(args[0], context);
                        int pCount;
                        var model = ResolveModel(args[1], context, out pCount);
                        double[] p = ExtractDoubleArray(args[2], context);

                        if (xs.Length == 0)
                            throw new ArgumentException("xs must be a non-empty vector");
                        if (p.Length == 0)
                            throw new ArgumentException("p must be a non-empty parameter vector");

                        double[] yPred = new double[xs.Length];
                        for (int i = 0; i < xs.Length; i++)
                        {
                            yPred[i] = model(xs[i], p);
                        }
                        result = MakeMatrixResult(yPred);
                        arrayResult = yPred;
                        RecordEvaluationResult(value, context, result, arrayResult, null);
                        return true;
                    }

                    case "swarmfit_details":
                    {
                        if (argc != 5 && argc != 7) return false;
                        double[] xs = ExtractDoubleArray(args[0], context);
                        double[] ys = ExtractDoubleArray(args[1], context);
                        int pCount;
                        var model = ResolveModel(args[2], context, out pCount);
                        double[] pMin = ExtractDoubleArray(args[3], context);
                        double[] pMax = ExtractDoubleArray(args[4], context);

                        int particles = 20;
                        int maxIter = 5000;
                        if (argc == 7)
                        {
                            double[] pArg = ExtractDoubleArray(args[5], context);
                            double[] iArg = ExtractDoubleArray(args[6], context);
                            if (pArg.Length > 0) particles = (int)Math.Round(pArg[0]);
                            if (iArg.Length > 0) maxIter = (int)Math.Round(iArg[0]);
                        }

                        FitResult fit = RunSwarmFit(xs, ys, model, pMin, pMax, particles, maxIter);
                        double[] details = new double[] {
                            fit.SSE,
                            fit.RMSE,
                            fit.MaxResidual,
                            fit.Iterations,
                            fit.Improvements
                        };
                        result = MakeMatrixResult(details);
                        arrayResult = details;
                        RecordEvaluationResult(value, context, result, arrayResult, null);
                        return true;
                    }

                    case "swarmfit_r2":
                    {
                        if (argc != 2) return false;
                        double[] ys = ExtractDoubleArray(args[0], context);
                        double[] yPred = ExtractDoubleArray(args[1], context);
                        if (ys.Length != yPred.Length || ys.Length == 0)
                            throw new ArgumentException($"ys (length {ys.Length}) and y_pred (length {yPred.Length}) must have identical non-zero lengths");

                        double yMean = 0;
                        for (int i = 0; i < ys.Length; i++) yMean += ys[i];
                        yMean /= ys.Length;

                        double ssTot = 0, ssRes = 0;
                        for (int i = 0; i < ys.Length; i++)
                        {
                            ssTot += Math.Pow(ys[i] - yMean, 2);
                            ssRes += Math.Pow(ys[i] - yPred[i], 2);
                        }
                        double r2 = ssTot > 0 ? 1.0 - (ssRes / ssTot) : 1.0;
                        result = MakeDoubleResult(r2);
                        scalarResult = r2;
                        RecordEvaluationResult(value, context, result, null, scalarResult);
                        return true;
                    }

                    case "swarmfit_linear":
                    {
                        if (argc != 2) return false;
                        double[] xs = ExtractDoubleArray(args[0], context);
                        double[] ys = ExtractDoubleArray(args[1], context);
                        if (xs.Length != ys.Length || xs.Length == 0)
                            throw new ArgumentException($"xs (length {xs.Length}) and ys (length {ys.Length}) must have identical non-zero lengths");
                        double xMin = xs.Min(), xMax = xs.Max();
                        double yMin = ys.Min(), yMax = ys.Max();
                        double dx = xMax - xMin;
                        if (Math.Abs(dx) < 1e-12) dx = 1.0;
                        double dy = yMax - yMin;
                        double approxSlope = dy / dx;

                        double[] pMin = new double[] { approxSlope - 10 * Math.Abs(approxSlope) - 100, yMin - 10 * Math.Abs(yMin) - 100 };
                        double[] pMax = new double[] { approxSlope + 10 * Math.Abs(approxSlope) + 100, yMax + 10 * Math.Abs(yMax) + 100 };

                        Func<double, double[], double> lineModel = (x, p) => p[0] * x + p[1];
                        FitResult fit = RunSwarmFit(xs, ys, lineModel, pMin, pMax, 20, 5000);
                        result = MakeMatrixResult(fit.Parameters);
                        arrayResult = fit.Parameters;
                        RecordEvaluationResult(value, context, result, arrayResult, null);
                        return true;
                    }

                    case "swarmfit_exp":
                    {
                        if (argc != 4 && argc != 6) return false;
                        double[] xs = ExtractDoubleArray(args[0], context);
                        double[] ys = ExtractDoubleArray(args[1], context);
                        double[] pMin = ExtractDoubleArray(args[2], context);
                        double[] pMax = ExtractDoubleArray(args[3], context);
                        int particles = 20, maxIter = 5000;
                        if (argc == 6)
                        {
                            double[] pArg = ExtractDoubleArray(args[4], context);
                            double[] iArg = ExtractDoubleArray(args[5], context);
                            if (pArg.Length > 0) particles = (int)Math.Round(pArg[0]);
                            if (iArg.Length > 0) maxIter = (int)Math.Round(iArg[0]);
                        }
                        Func<double, double[], double> expModel = (x, p) => p[0] + p[1] * Math.Exp(p[2] * x);
                        FitResult fit = RunSwarmFit(xs, ys, expModel, pMin, pMax, particles, maxIter);
                        result = MakeMatrixResult(fit.Parameters);
                        arrayResult = fit.Parameters;
                        RecordEvaluationResult(value, context, result, arrayResult, null);
                        return true;
                    }

                    case "swarmfit_expdecay":
                    {
                        if (argc != 4 && argc != 6) return false;
                        double[] xs = ExtractDoubleArray(args[0], context);
                        double[] ys = ExtractDoubleArray(args[1], context);
                        double[] pMin = ExtractDoubleArray(args[2], context);
                        double[] pMax = ExtractDoubleArray(args[3], context);
                        int particles = 20, maxIter = 5000;
                        if (argc == 6)
                        {
                            double[] pArg = ExtractDoubleArray(args[4], context);
                            double[] iArg = ExtractDoubleArray(args[5], context);
                            if (pArg.Length > 0) particles = (int)Math.Round(pArg[0]);
                            if (iArg.Length > 0) maxIter = (int)Math.Round(iArg[0]);
                        }
                        Func<double, double[], double> decayModel = (x, p) => p[0] * Math.Exp(-x / p[1]);
                        FitResult fit = RunSwarmFit(xs, ys, decayModel, pMin, pMax, particles, maxIter);
                        result = MakeMatrixResult(fit.Parameters);
                        arrayResult = fit.Parameters;
                        RecordEvaluationResult(value, context, result, arrayResult, null);
                        return true;
                    }

                    case "swarmfit_power":
                    {
                        if (argc != 4 && argc != 6) return false;
                        double[] xs = ExtractDoubleArray(args[0], context);
                        double[] ys = ExtractDoubleArray(args[1], context);
                        double[] pMin = ExtractDoubleArray(args[2], context);
                        double[] pMax = ExtractDoubleArray(args[3], context);
                        int particles = 20, maxIter = 5000;
                        if (argc == 6)
                        {
                            double[] pArg = ExtractDoubleArray(args[4], context);
                            double[] iArg = ExtractDoubleArray(args[5], context);
                            if (pArg.Length > 0) particles = (int)Math.Round(pArg[0]);
                            if (iArg.Length > 0) maxIter = (int)Math.Round(iArg[0]);
                        }
                        Func<double, double[], double> pwrModel = (x, p) => p[0] * Math.Pow(x, p[1]);
                        FitResult fit = RunSwarmFit(xs, ys, pwrModel, pMin, pMax, particles, maxIter);
                        result = MakeMatrixResult(fit.Parameters);
                        arrayResult = fit.Parameters;
                        RecordEvaluationResult(value, context, result, arrayResult, null);
                        return true;
                    }

                    case "swarmfit_gauss":
                    {
                        if (argc != 4 && argc != 6) return false;
                        double[] xs = ExtractDoubleArray(args[0], context);
                        double[] ys = ExtractDoubleArray(args[1], context);
                        double[] pMin = ExtractDoubleArray(args[2], context);
                        double[] pMax = ExtractDoubleArray(args[3], context);
                        int particles = 20, maxIter = 5000;
                        if (argc == 6)
                        {
                            double[] pArg = ExtractDoubleArray(args[4], context);
                            double[] iArg = ExtractDoubleArray(args[5], context);
                            if (pArg.Length > 0) particles = (int)Math.Round(pArg[0]);
                            if (iArg.Length > 0) maxIter = (int)Math.Round(iArg[0]);
                        }
                        Func<double, double[], double> gaussModel = (x, p) => p[0] * Math.Exp(-Math.Pow(x - p[1], 2) / (2.0 * p[2] * p[2]));
                        FitResult fit = RunSwarmFit(xs, ys, gaussModel, pMin, pMax, particles, maxIter);
                        result = MakeMatrixResult(fit.Parameters);
                        arrayResult = fit.Parameters;
                        RecordEvaluationResult(value, context, result, arrayResult, null);
                        return true;
                    }

                    case "swarmfit_poly":
                    {
                        if (argc != 5 && argc != 7) return false;
                        double[] xs = ExtractDoubleArray(args[0], context);
                        double[] ys = ExtractDoubleArray(args[1], context);
                        double[] degArr = ExtractDoubleArray(args[2], context);
                        int degree = degArr.Length > 0 ? (int)Math.Round(degArr[0]) : 2;
                        double[] pMin = ExtractDoubleArray(args[3], context);
                        double[] pMax = ExtractDoubleArray(args[4], context);
                        int particles = 20, maxIter = 5000;
                        if (argc == 7)
                        {
                            double[] pArg = ExtractDoubleArray(args[5], context);
                            double[] iArg = ExtractDoubleArray(args[6], context);
                            if (pArg.Length > 0) particles = (int)Math.Round(pArg[0]);
                            if (iArg.Length > 0) maxIter = (int)Math.Round(iArg[0]);
                        }
                        Func<double, double[], double> polyModel = (x, p) =>
                        {
                            double sum = 0;
                            for (int d = 0; d <= degree && d < p.Length; d++)
                            {
                                sum += p[d] * Math.Pow(x, degree - d);
                            }
                            return sum;
                        };
                        FitResult fit = RunSwarmFit(xs, ys, polyModel, pMin, pMax, particles, maxIter);
                        result = MakeMatrixResult(fit.Parameters);
                        arrayResult = fit.Parameters;
                        RecordEvaluationResult(value, context, result, arrayResult, null);
                        return true;
                    }

                    default:
                        return false;
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"SwarmFit evaluation error in {fnName}: {ex.Message}", ex);
            }
        }
    }

    public class SwarmFitPlugin : IPlugin, IPluginHandleEvaluation, IPluginLowLevelEvaluationFast
    {
        private TermInfo[] _termsHandled;

        public void Initialize()
        {
            if (_termsHandled == null)
            {
                var list = new List<TermInfo>();

                // 1. swarmfit (5 & 7 args)
                list.Add(new TermInfo("swarmfit", TermType.Function, "Fits parameters to X/Y data using Particle Swarm Optimization (PSO). Returns optimal parameter vector.", FunctionSections.Unknown, true,
                    new ArgumentInfo[] {
                        new ArgumentInfo("xs"),
                        new ArgumentInfo("ys"),
                        new ArgumentInfo("model"),
                        new ArgumentInfo("p_min"),
                        new ArgumentInfo("p_max")
                    }));
                list.Add(new TermInfo("swarmfit", TermType.Function, "Fits parameters to X/Y data using PSO with custom particle count and max iterations.", FunctionSections.Unknown, true,
                    new ArgumentInfo[] {
                        new ArgumentInfo("xs"),
                        new ArgumentInfo("ys"),
                        new ArgumentInfo("model"),
                        new ArgumentInfo("p_min"),
                        new ArgumentInfo("p_max"),
                        new ArgumentInfo("particles"),
                        new ArgumentInfo("max_iterations")
                    }));

                // 2. swarmfit_predict (3 args)
                list.Add(new TermInfo("swarmfit_predict", TermType.Function, "Evaluates model predictions Y_hat = f(X, p) across an X vector using fitted parameters.", FunctionSections.Unknown, true,
                    new ArgumentInfo[] {
                        new ArgumentInfo("xs"),
                        new ArgumentInfo("model"),
                        new ArgumentInfo("p")
                    }));

                // 3. swarmfit_details (5 & 7 args)
                list.Add(new TermInfo("swarmfit_details", TermType.Function, "Returns convergence and error metrics matrix: [SSE; RMSE; MaxResidual; Iterations; Improvements].", FunctionSections.Unknown, true,
                    new ArgumentInfo[] {
                        new ArgumentInfo("xs"),
                        new ArgumentInfo("ys"),
                        new ArgumentInfo("model"),
                        new ArgumentInfo("p_min"),
                        new ArgumentInfo("p_max")
                    }));
                list.Add(new TermInfo("swarmfit_details", TermType.Function, "Returns convergence and error metrics with custom swarm size and max iterations.", FunctionSections.Unknown, true,
                    new ArgumentInfo[] {
                        new ArgumentInfo("xs"),
                        new ArgumentInfo("ys"),
                        new ArgumentInfo("model"),
                        new ArgumentInfo("p_min"),
                        new ArgumentInfo("p_max"),
                        new ArgumentInfo("particles"),
                        new ArgumentInfo("max_iterations")
                    }));

                // 4. swarmfit_r2 (2 args)
                list.Add(new TermInfo("swarmfit_r2", TermType.Function, "Calculates coefficient of determination R^2 between observed Y and model predictions Y_pred.", FunctionSections.Unknown, true,
                    new ArgumentInfo[] {
                        new ArgumentInfo("ys"),
                        new ArgumentInfo("y_pred")
                    }));

                // 5. swarmfit_linear (2 args)
                list.Add(new TermInfo("swarmfit_linear", TermType.Function, "Fits linear model Y = a * X + b using PSO. Returns [a; b].", FunctionSections.Unknown, true,
                    new ArgumentInfo[] {
                        new ArgumentInfo("xs"),
                        new ArgumentInfo("ys")
                    }));

                // 6. swarmfit_exp (4 & 6 args)
                list.Add(new TermInfo("swarmfit_exp", TermType.Function, "Fits exponential model Y = a + b * exp(c * X). Returns [a; b; c].", FunctionSections.Unknown, true,
                    new ArgumentInfo[] {
                        new ArgumentInfo("xs"),
                        new ArgumentInfo("ys"),
                        new ArgumentInfo("p_min"),
                        new ArgumentInfo("p_max")
                    }));
                list.Add(new TermInfo("swarmfit_exp", TermType.Function, "Fits exponential model with custom particle count and max iterations.", FunctionSections.Unknown, true,
                    new ArgumentInfo[] {
                        new ArgumentInfo("xs"),
                        new ArgumentInfo("ys"),
                        new ArgumentInfo("p_min"),
                        new ArgumentInfo("p_max"),
                        new ArgumentInfo("particles"),
                        new ArgumentInfo("max_iterations")
                    }));

                // 7. swarmfit_expdecay (4 & 6 args)
                list.Add(new TermInfo("swarmfit_expdecay", TermType.Function, "Fits single exponential decay Y = a * exp(-X / b). Returns [a; b].", FunctionSections.Unknown, true,
                    new ArgumentInfo[] {
                        new ArgumentInfo("xs"),
                        new ArgumentInfo("ys"),
                        new ArgumentInfo("p_min"),
                        new ArgumentInfo("p_max")
                    }));
                list.Add(new TermInfo("swarmfit_expdecay", TermType.Function, "Fits single exponential decay with custom swarm settings.", FunctionSections.Unknown, true,
                    new ArgumentInfo[] {
                        new ArgumentInfo("xs"),
                        new ArgumentInfo("ys"),
                        new ArgumentInfo("p_min"),
                        new ArgumentInfo("p_max"),
                        new ArgumentInfo("particles"),
                        new ArgumentInfo("max_iterations")
                    }));

                // 8. swarmfit_power (4 & 6 args)
                list.Add(new TermInfo("swarmfit_power", TermType.Function, "Fits power law model Y = a * X^b. Returns [a; b].", FunctionSections.Unknown, true,
                    new ArgumentInfo[] {
                        new ArgumentInfo("xs"),
                        new ArgumentInfo("ys"),
                        new ArgumentInfo("p_min"),
                        new ArgumentInfo("p_max")
                    }));
                list.Add(new TermInfo("swarmfit_power", TermType.Function, "Fits power law model with custom swarm settings.", FunctionSections.Unknown, true,
                    new ArgumentInfo[] {
                        new ArgumentInfo("xs"),
                        new ArgumentInfo("ys"),
                        new ArgumentInfo("p_min"),
                        new ArgumentInfo("p_max"),
                        new ArgumentInfo("particles"),
                        new ArgumentInfo("max_iterations")
                    }));

                // 9. swarmfit_gauss (4 & 6 args)
                list.Add(new TermInfo("swarmfit_gauss", TermType.Function, "Fits Gaussian peak Y = a * exp(-(X - b)^2 / (2 * c^2)). Returns [a; b; c].", FunctionSections.Unknown, true,
                    new ArgumentInfo[] {
                        new ArgumentInfo("xs"),
                        new ArgumentInfo("ys"),
                        new ArgumentInfo("p_min"),
                        new ArgumentInfo("p_max")
                    }));
                list.Add(new TermInfo("swarmfit_gauss", TermType.Function, "Fits Gaussian peak with custom swarm settings.", FunctionSections.Unknown, true,
                    new ArgumentInfo[] {
                        new ArgumentInfo("xs"),
                        new ArgumentInfo("ys"),
                        new ArgumentInfo("p_min"),
                        new ArgumentInfo("p_max"),
                        new ArgumentInfo("particles"),
                        new ArgumentInfo("max_iterations")
                    }));

                // 10. swarmfit_poly (5 & 7 args)
                list.Add(new TermInfo("swarmfit_poly", TermType.Function, "Fits polynomial of degree d: Y = p0*X^d + ... + pd. Returns parameter vector.", FunctionSections.Unknown, true,
                    new ArgumentInfo[] {
                        new ArgumentInfo("xs"),
                        new ArgumentInfo("ys"),
                        new ArgumentInfo("degree"),
                        new ArgumentInfo("p_min"),
                        new ArgumentInfo("p_max")
                    }));
                list.Add(new TermInfo("swarmfit_poly", TermType.Function, "Fits polynomial with custom swarm settings.", FunctionSections.Unknown, true,
                    new ArgumentInfo[] {
                        new ArgumentInfo("xs"),
                        new ArgumentInfo("ys"),
                        new ArgumentInfo("degree"),
                        new ArgumentInfo("p_min"),
                        new ArgumentInfo("p_max"),
                        new ArgumentInfo("particles"),
                        new ArgumentInfo("max_iterations")
                    }));

                _termsHandled = list.ToArray();
            }
        }

        public TermInfo[] GetTermsHandled(SessionProfile sessionProfile)
        {
            if (_termsHandled == null) Initialize();
            return _termsHandled;
        }

        public bool TryEvaluateExpression(Entry value, Store context, out Entry result)
        {
            double[] arr;
            double? sc;
            return SwarmFitEngine.TryEvaluateSwarmFit(value, context, out result, out arr, out sc);
        }

        public bool NumericEvaluation(Term value, TNumber[] args, ref TNumber result)
        {
            result = null;
            return false;
        }

        public void Dispose()
        {
        }
    }
}
