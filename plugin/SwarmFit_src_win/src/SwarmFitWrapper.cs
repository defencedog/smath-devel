// SwarmFitWrapper — SMath Studio plugin wrapping SwarmFit (particle swarm optimization).
//
// Ported from the Mono/Linux build to .NET Framework 2.0 so that the same assembly
// loads on Windows and under Mono. The differences from the Linux source are
// mechanical and are listed in patches/README.md; the evaluation logic is unchanged.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using SMath.Manager;
using SMath.Math;
using SMath.Math.Numeric;
using SMath.Math.Symbolic;
using SwarmFit;

[assembly: System.Reflection.AssemblyTitle("SwarmFitWrapper")]
[assembly: System.Reflection.AssemblyDescription("Particle Swarm Optimization curve fitting plugin for SMath Studio, wrapping SwarmFit")]
[assembly: System.Reflection.AssemblyVersion("1.1.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.1.0.0")]
[assembly: System.Runtime.InteropServices.Guid("b0a439e5-20d5-46a3-bcb5-596cbd1d9934")]
[assembly: System.Runtime.InteropServices.ComVisible(false)]

namespace SwarmFitWrapper
{
    /// <summary>
    /// Everything a completed fit produced, before it is flattened into a
    /// result matrix for the worksheet.
    /// </summary>
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

    /// <summary>
    /// The evaluation engine. Everything here is static and re-entrant-safe: the
    /// plugin instance is shared by the whole worksheet, and SMath may evaluate
    /// several regions in any order.
    /// </summary>
    public static class SwarmFitEngine
    {
        private static readonly object _syncLock = new object();

        // SMath hands a plugin the *unevaluated* expression tree, and the value a
        // user assigned earlier lives in the worksheet's Store, not in the tree.
        // These four caches bridge that gap: two keyed by the variable name the
        // user wrote, two keyed by the expression signature. Name keys are the
        // fast path; signature keys survive a value being passed inline.
        private static readonly Dictionary<string, double[]> _matrixByName =
            new Dictionary<string, double[]>(StringComparer.Ordinal);
        private static readonly Dictionary<string, double> _scalarByName =
            new Dictionary<string, double>(StringComparer.Ordinal);
        private static readonly Dictionary<string, double[]> _matrixBySig =
            new Dictionary<string, double[]>();
        private static readonly Dictionary<string, double> _scalarBySig =
            new Dictionary<string, double>();

        private static void LogDebug(string msg)
        {
            try
            {
                System.IO.File.AppendAllText("/tmp/swarmfit_debug.log", DateTime.Now.ToString("HH:mm:ss.fff") + " " + msg + Environment.NewLine);
            }
            catch {}
        }

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

        // ---------------------------------------------------------------- helpers

        // A predicate over worksheet definitions. Declared explicitly because
        // System.Func is System.Core (.NET 3.5) and unavailable on this target.
        private delegate bool DefinitionMatch(Definition definition);

        /// <summary>Last matching definition, or null. Mirrors Enumerable.LastOrDefault.</summary>
        private static Definition LastWhere(Store context, DefinitionMatch match)
        {
            if (context == null) return null;

            Definition found = null;
            foreach (Definition d in context)
            {
                if (d != null && match(d)) found = d;
            }
            return found;
        }

        /// <summary>True if any definition matches. Mirrors Enumerable.Any.</summary>
        private static bool AnyWhere(Store context, DefinitionMatch match)
        {
            if (context == null) return false;

            foreach (Definition d in context)
            {
                if (d != null && match(d)) return true;
            }
            return false;
        }

        // ------------------------------------------------------------- signatures

        /// <summary>
        /// A stable textual fingerprint of an expression tree, used as a cache key.
        /// </summary>
        public static string GetEntrySignature(Entry entry)
        {
            if (entry == null) return string.Empty;
            if (entry.Items == null || entry.Items.Length == 0)
                return entry.Type + ":" + entry.Text;

            StringBuilder sb = new StringBuilder();
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

        // ---------------------------------------------------------- store bridging

        /// <summary>
        /// Find the worksheet definition that produced a given value, so its Result
        /// can be replaced with the computed one and the CAS stops re-evaluating it.
        /// </summary>
        public static Definition FindDefiningDefinition(Entry value, Store context)
        {
            if (context == null || value == null) return null;

            try
            {
                // 1. Same object identity
                Definition def = LastWhere(context,
                    d => d.Result != null && ReferenceEquals(d.Result, value));
                if (def != null) return def;

                // 2. Same expression signature
                string valueSignature = GetEntrySignature(value);
                def = LastWhere(context,
                    d => d.Result != null && GetEntrySignature(d.Result) == valueSignature);
                if (def != null) return def;

                // 3. Same function name and arity
                if (value.Type == TermType.Function)
                {
                    int argc = value.Items != null ? value.Items.Length : 0;
                    def = LastWhere(context, d =>
                        d.Result != null &&
                        d.Result.Type == TermType.Function &&
                        string.Equals(d.Result.Text, value.Text, StringComparison.OrdinalIgnoreCase) &&
                        (d.Result.Items != null ? d.Result.Items.Length : 0) == argc);
                    if (def != null) return def;
                }
            }
            catch
            {
                // A malformed Store must never take the plugin down.
            }

            return null;
        }

        /// <summary>
        /// Publish a computed result back into the Store and the caches, keyed both
        /// by expression signature and by the variable name the user assigned to.
        /// </summary>
        public static void RecordEvaluationResult(Entry value, Store context, Entry result,
                                                  double[] arrayResult, double? scalarResult)
        {
            if (value == null) return;

            string signature = GetEntrySignature(value);

            lock (_syncLock)
            {
                if (arrayResult != null && !string.IsNullOrEmpty(signature))
                    _matrixBySig[signature] = arrayResult;

                if (scalarResult.HasValue && !string.IsNullOrEmpty(signature))
                    _scalarBySig[signature] = scalarResult.Value;
            }

            if (context == null) return;

            Definition def = FindDefiningDefinition(value, context);
            if (def == null) return;

            if (result != null)
            {
                def.Result = result;
                def.IsCalculated = true;
            }

            if (string.IsNullOrEmpty(def.Name)) return;

            lock (_syncLock)
            {
                if (arrayResult != null)
                    _matrixByName[def.Name] = arrayResult;

                if (scalarResult.HasValue)
                    _scalarByName[def.Name] = scalarResult.Value;
            }
        }

        // ------------------------------------------------------------ extraction

        /// <summary>
        /// Coerce an expression to a single number, using the caches and the Store
        /// where the tree alone is not enough.
        /// </summary>
        public static double ExtractScalar(Entry entry, Store context)
        {
            if (entry == null) return 0.0;

            // 1. Look the variable up in the worksheet Store first (if context available)
            if (context != null && entry.Type == TermType.Operand && !string.IsNullOrEmpty(entry.Text))
            {
                Definition def = LastWhere(context, d => d.Name == entry.Text);
                if (def != null && def.Result != null)
                {
                    // A definition whose result is still an unevaluated swarmfit call
                    if (def.Result.Type == TermType.Function && IsSwarmFitFunction(def.Result.Text))
                    {
                        Entry computed;
                        double[] computedArray;
                        double? computedScalar;

                        if (TryEvaluateSwarmFit(def.Result, context, out computed, out computedArray, out computedScalar))
                        {
                            def.Result = computed;
                            def.IsCalculated = true;

                            if (computedScalar.HasValue)
                            {
                                lock (_syncLock) { _scalarByName[entry.Text] = computedScalar.Value; }
                                return computedScalar.Value;
                            }

                            if (computedArray != null && computedArray.Length > 0)
                            {
                                lock (_syncLock) { _matrixByName[entry.Text] = computedArray; }
                                return computedArray[0];
                            }
                        }
                    }

                    double fromStore = ExtractScalar(def.Result, context);
                    if (!double.IsNaN(fromStore) && fromStore != 0.0)
                    {
                        lock (_syncLock) { _scalarByName[entry.Text] = fromStore; }
                        return fromStore;
                    }
                }
            }

            // 2. Variable already seen in cache (fallback)
            if (entry.Type == TermType.Operand && !string.IsNullOrEmpty(entry.Text))
            {
                lock (_syncLock)
                {
                    double cachedScalar;
                    if (_scalarByName.TryGetValue(entry.Text, out cachedScalar))
                        return cachedScalar;

                    double[] cachedArray;
                    if (_matrixByName.TryGetValue(entry.Text, out cachedArray) && cachedArray.Length > 0)
                        return cachedArray[0];
                }
            }

            // 3. Expression already seen

            // 4. The entry is itself a swarmfit call
            if (entry.Type == TermType.Function && IsSwarmFitFunction(entry.Text))
            {
                Entry computed;
                double[] computedArray;
                double? computedScalar;

                if (TryEvaluateSwarmFit(entry, context, out computed, out computedArray, out computedScalar))
                {
                    if (computedScalar.HasValue) return computedScalar.Value;
                    if (computedArray != null && computedArray.Length > 0) return computedArray[0];
                }
            }

            // 5. Let the CAS evaluate it
            if (context != null)
            {
                try
                {
                    TNumber number = Computation.NumericCalculation(entry, context);
                    if (number != null)
                    {
                        TDouble asDouble = number.obj as TDouble;
                        if (asDouble != null) return asDouble.ToDouble();

                        TFraction asFraction = number.obj as TFraction;
                        if (asFraction != null) return asFraction.ToDouble();

                        TMatrix asMatrix = number.obj as TMatrix;
                        if (asMatrix != null)
                        {
                            TNumber first = asMatrix[0, 0];
                            if (first != null) return first.obj.ToDouble();
                        }
                    }
                }
                catch
                {
                }
            }

            // 6. Arithmetic over operands the CAS could not fold
            if (entry.Text == "-" && entry.Items != null && entry.Items.Length == 1)
                return -ExtractScalar(entry.Items[0], context);

            if (entry.Items != null && entry.Items.Length == 2)
            {
                double left;
                double right;

                switch (entry.Text)
                {
                    case "*":
                        return ExtractScalar(entry.Items[0], context) * ExtractScalar(entry.Items[1], context);
                    case "/":
                        return ExtractScalar(entry.Items[0], context) / ExtractScalar(entry.Items[1], context);
                    case "+":
                        return ExtractScalar(entry.Items[0], context) + ExtractScalar(entry.Items[1], context);
                    case "-":
                        return ExtractScalar(entry.Items[0], context) - ExtractScalar(entry.Items[1], context);
                    case "^":
                        left = ExtractScalar(entry.Items[0], context);
                        right = ExtractScalar(entry.Items[1], context);
                        return Math.Pow(left, right);
                }
            }

            double literal;
            if (double.TryParse(entry.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out literal))
                return literal;

            return 0.0;
        }

        /// <summary>
        /// Coerce an expression to a flat array of doubles. A column matrix, a row
        /// matrix and a vector all arrive here as the same thing.
        /// </summary>
        public static double[] ExtractDoubleArray(Entry entry, Store context)
        {
            if (entry == null) return new double[0];

            // 1. Look the variable up in the worksheet Store first (if context available)
            if (context != null && entry.Type == TermType.Operand && !string.IsNullOrEmpty(entry.Text))
            {
                Definition def = LastWhere(context, d => d.Name == entry.Text);
                if (def != null && def.Result != null)
                {
                    // 1a. The stored result is an unevaluated swarmfit call
                    if (def.Result.Type == TermType.Function && IsSwarmFitFunction(def.Result.Text))
                    {
                        Entry computed;
                        double[] computedArray;
                        double? computedScalar;

                        if (TryEvaluateSwarmFit(def.Result, context, out computed, out computedArray, out computedScalar))
                        {
                            def.Result = computed;
                            def.IsCalculated = true;

                            if (computedArray != null && computedArray.Length > 0)
                            {
                                lock (_syncLock) { _matrixByName[entry.Text] = computedArray; }
                                return computedArray;
                            }
                        }
                    }

                    // 1b. The stored result is already a matrix
                    double[] fromStore = ExtractDoubleArray(def.Result, context);
                    if (fromStore != null && fromStore.Length > 0)
                    {
                        lock (_syncLock) { _matrixByName[entry.Text] = fromStore; }
                        return fromStore;
                    }
                }
            }

            // 2. Variable already seen in cache (fallback)
            if (entry.Type == TermType.Operand && !string.IsNullOrEmpty(entry.Text))
            {
                lock (_syncLock)
                {
                    double[] cached;
                    if (_matrixByName.TryGetValue(entry.Text, out cached) && cached != null && cached.Length > 0)
                        return cached;
                }
            }

            // 3. Expression already seen
            string signature = GetEntrySignature(entry);
            if (!string.IsNullOrEmpty(signature))
            {
                lock (_syncLock)
                {
                    double[] cachedBySignature;
                    if (_matrixBySig.TryGetValue(signature, out cachedBySignature) && cachedBySignature != null && cachedBySignature.Length > 0)
                        return cachedBySignature;
                }
            }

            // 4. A matrix literal in the tree
            if (entry.Items != null && entry.Items.Length >= 2)
            {
                int count = entry.Items.Length;
                bool isMatrixLiteral = entry.Text == "mat" || entry.Text == "matrix" ||
                                       string.IsNullOrEmpty(entry.Text);

                if (isMatrixLiteral)
                {
                    // Layout emitted by TMatrix.ToTerms(): [v0, v1, ..., rows, cols]
                    int rows = (int)ExtractScalar(entry.Items[count - 2], context);
                    int cols = (int)ExtractScalar(entry.Items[count - 1], context);
                    if (rows > 0 && cols > 0 && rows * cols == count - 2)
                    {
                        double[] values = new double[rows * cols];
                        for (int i = 0; i < values.Length; i++)
                            values[i] = ExtractScalar(entry.Items[i], context);
                        return values;
                    }

                    // Legacy layout: [rows, cols, v0, v1, ...]
                    rows = (int)ExtractScalar(entry.Items[0], context);
                    cols = (int)ExtractScalar(entry.Items[1], context);
                    if (rows > 0 && cols > 0 && rows * cols == count - 2)
                    {
                        double[] values = new double[rows * cols];
                        for (int i = 0; i < values.Length; i++)
                            values[i] = ExtractScalar(entry.Items[2 + i], context);
                        return values;
                    }
                }
            }

            // 5. The entry is itself a swarmfit call
            if (entry.Type == TermType.Function && IsSwarmFitFunction(entry.Text))
            {
                Entry computed;
                double[] computedArray;
                double? computedScalar;

                if (TryEvaluateSwarmFit(entry, context, out computed, out computedArray, out computedScalar))
                {
                    if (computedArray != null && computedArray.Length > 0)
                        return computedArray;
                }
            }

            // 6. Let the CAS evaluate it
            if (context != null)
            {
                try
                {
                    TNumber number = Computation.NumericCalculation(entry, context);
                    if (number != null)
                    {
                        TMatrix matrix = number.obj as TMatrix;
                        if (matrix != null)
                        {
                            int rows = matrix.unit.GetLength(0);
                            int cols = matrix.unit.GetLength(1);
                            double[] values = new double[rows * cols];
                            int index = 0;
                            for (int r = 0; r < rows; r++)
                            {
                                for (int c = 0; c < cols; c++)
                                {
                                    values[index++] = matrix[r, c].obj.ToDouble();
                                }
                            }

                            if (entry.Type == TermType.Operand && !string.IsNullOrEmpty(entry.Text))
                            {
                                lock (_syncLock) { _matrixByName[entry.Text] = values; }
                            }
                            return values;
                        }

                        TDouble asDouble = number.obj as TDouble;
                        if (asDouble != null) return new double[] { asDouble.ToDouble() };

                        TFraction asFraction = number.obj as TFraction;
                        if (asFraction != null) return new double[] { asFraction.ToDouble() };
                    }
                }
                catch
                {
                }
            }

            // 7. Last resort: ask the definition to render itself as an entry
            if (context != null && entry.Type == TermType.Operand && !string.IsNullOrEmpty(entry.Text))
            {
                Definition def = LastWhere(context, d => d.Name == entry.Text);
                if (def != null)
                {
                    try
                    {
                        Entry fromDefinition = def.ToEntry();
                        if (fromDefinition != null && fromDefinition != entry)
                        {
                            double[] values = ExtractDoubleArray(fromDefinition, context);
                            if (values != null && values.Length > 0) return values;
                        }
                    }
                    catch
                    {
                    }
                }
            }

            // 8. Unary minus
            if (entry.Text == "-" && entry.Items != null && entry.Items.Length == 1)
                return new double[] { -ExtractScalar(entry.Items[0], context) };

            // 9. A bare number is a one-element vector
            double scalar;
            if (double.TryParse(entry.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out scalar))
                return new double[] { scalar };

            return new double[0];
        }

        // --------------------------------------------------------------- results

        /// <summary>Wrap a column vector as an SMath matrix.</summary>
        public static Entry MakeMatrixResult(double[] values)
        {
            int rows = values.Length;
            TNumber[,] cells = new TNumber[rows, 1];
            for (int i = 0; i < rows; i++)
                cells[i, 0] = new TNumber(values[i]);

            return Entry.Create(new TMatrix(cells).ToTerms());
        }

        /// <summary>Wrap a 2-D array as an SMath matrix.</summary>
        public static Entry MakeMatrix2DResult(double[,] values)
        {
            int rows = values.GetLength(0);
            int cols = values.GetLength(1);
            TNumber[,] cells = new TNumber[rows, cols];
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                    cells[r, c] = new TNumber(values[r, c]);

            return Entry.Create(new TMatrix(cells).ToTerms());
        }

        public static Entry MakeDoubleResult(double value)
        {
            return Entry.Create(new TDouble(value).ToTerms());
        }

        // ---------------------------------------------------------------- models

        /// <summary>
        /// Remove surrounding quotes and whitespace from a model name. Implemented
        /// by hand rather than with String.Trim(char), which does not exist on
        /// .NET Framework 4.x and throws MissingMethodException at run time.
        /// </summary>
        internal static string StripQuotes(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;

            int start = 0;
            int end = value.Length;

            while (start < end && (value[start] == '"' || value[start] == '\'' || char.IsWhiteSpace(value[start])))
                start++;

            while (end > start && (value[end - 1] == '"' || value[end - 1] == '\'' || char.IsWhiteSpace(value[end - 1])))
                end--;

            return (start >= end) ? string.Empty : value.Substring(start, end - start);
        }

        /// <summary>
        /// Turn the third argument of <c>swarmfit</c> into a callable model. Either a
        /// quoted standard name, or the name of a function defined in the worksheet.
        /// </summary>
        public static FitFunction ResolveModel(Entry modelArg, Store context, out int parameterCount)
        {
            parameterCount = -1;

            if (modelArg == null)
                throw new ArgumentNullException("modelArg");

            string modelName = null;
            string raw = modelArg.Text ?? string.Empty;

            // 1. A quoted literal: "mm", "haldane", ...
            if (raw.Length >= 2 && raw.StartsWith("\"") && raw.EndsWith("\""))
            {
                modelName = StripQuotes(raw).ToLowerInvariant();
            }
            else
            {
                // 2. A bare identifier that happens to name a standard model
                string candidate = StripQuotes(raw).ToLowerInvariant();
                if (IsKnownModelName(candidate))
                {
                    modelName = candidate;
                }
                else if (context != null)
                {
                    // 3. A variable holding the model name as a string
                    Definition variable = LastWhere(context, d => d.Name == raw && !d.IsFunction);
                    if (variable != null && variable.Result != null)
                    {
                        string stored = variable.Result.Text ?? string.Empty;
                        if (stored.Length >= 2 && stored.StartsWith("\"") && stored.EndsWith("\""))
                            modelName = StripQuotes(stored).ToLowerInvariant();
                    }

                    // 4. Let the CAS resolve it, if it is not a function name
                    if (string.IsNullOrEmpty(modelName) &&
                        (variable != null || !AnyWhere(context, d => d.Name == raw && d.IsFunction)))
                    {
                        try
                        {
                            TNumber number = Computation.NumericCalculation(modelArg, context);
                            TDouble text = number != null ? number.obj as TDouble : null;
                            if (text != null && text.isText)
                            {
                                string resolved = text.Text ?? text.ToString();
                                if (!string.IsNullOrEmpty(resolved))
                                    modelName = StripQuotes(resolved).ToLowerInvariant();
                            }
                        }
                        catch
                        {
                        }
                    }
                }
            }

            if (!string.IsNullOrEmpty(modelName))
            {
                FitFunction standard = StandardModel(modelName, out parameterCount);
                if (standard != null) return standard;
            }

            // 5. A user-defined SMath function
            Definition definition = null;
            if (context != null && !string.IsNullOrEmpty(modelArg.Text))
                definition = LastWhere(context, d => d.Name == modelArg.Text);

            Entry template = definition != null ? definition.ToEntry() : modelArg;
            if (template == null || template.Type != TermType.Function)
            {
                throw new InvalidOperationException(
                    "Model '" + modelArg.Text + "' is neither a known standard model name nor a defined SMath function.");
            }

            int arity = template.Items != null ? template.Items.Length : 0;
            if (arity < 2)
            {
                throw new InvalidOperationException(
                    "Function '" + template.Text + "' has arity " + arity +
                    ", but a curve model must accept at least 2 arguments: (x, p) or (x, p0, p1, ...).");
            }

            if (arity == 2)
            {
                // f(x, p) with p as a column vector
                return delegate(double x, double[] p)
                {
                    try
                    {
                        Entry call = new Entry(template.Text, TermType.Function, new Entry[2]);
                        call.Items[0] = MakeDoubleResult(x);

                        TNumber[,] cells = new TNumber[p.Length, 1];
                        for (int k = 0; k < p.Length; k++) cells[k, 0] = new TNumber(p[k]);
                        call.Items[1] = Entry.Create(new TMatrix(cells).ToTerms());

                        TNumber result = Computation.NumericCalculation(call, context);
                        if (result == null || result.obj == null) return 1e12;

                        double value = result.obj.ToDouble();
                        return (double.IsNaN(value) || double.IsInfinity(value)) ? 1e12 : value;
                    }
                    catch
                    {
                        // A model that throws for some parameter vector must look
                        // like a very bad fit, not like a worksheet error.
                        return 1e12;
                    }
                };
            }

            // f(x, p0, p1, ...) with one scalar per parameter
            parameterCount = arity - 1;
            return delegate(double x, double[] p)
            {
                try
                {
                    Entry call = new Entry(template.Text, TermType.Function, new Entry[arity]);
                    call.Items[0] = MakeDoubleResult(x);
                    for (int k = 0; k < p.Length && k < arity - 1; k++)
                        call.Items[1 + k] = MakeDoubleResult(p[k]);

                    TNumber result = Computation.NumericCalculation(call, context);
                    if (result == null || result.obj == null) return 1e12;

                    double value = result.obj.ToDouble();
                    return (double.IsNaN(value) || double.IsInfinity(value)) ? 1e12 : value;
                }
                catch
                {
                    return 1e12;
                }
            };
        }

        /// <summary>
        /// The built-in model catalogue, delegated to the pure core so the model
        /// formulas can be verified outside SMath. Returns null for an unknown name.
        /// </summary>
        private static FitFunction StandardModel(string name, out int parameterCount)
        {
            return SwarmFitCore.StandardModel(name, out parameterCount);
        }

        // ------------------------------------------------------------------ solve

        /// <summary>
        /// Run the swarm and compute the fit statistics. The arithmetic lives in
        /// SwarmFitCore so it is unit-testable; this wrapper only adds the
        /// re-entrancy lock the plugin needs.
        /// </summary>
        public static FitResult RunSwarmFit(double[] xs, double[] ys, FitFunction model,
                                            double[] parameterMin, double[] parameterMax,
                                            int particleCount, int maxIterations)
        {
            lock (_syncLock)
            {
                if (parameterMin != null && parameterMax != null && parameterMin.Length == parameterMax.Length)
                {
                    for (int i = 0; i < parameterMin.Length; i++)
                    {
                        if (parameterMin[i] > parameterMax[i])
                        {
                            double tmp = parameterMin[i];
                            parameterMin[i] = parameterMax[i];
                            parameterMax[i] = tmp;
                        }
                    }
                }
                return SwarmFitCore.Fit(xs, ys, model, parameterMin, parameterMax,
                                        particleCount, maxIterations);
            }
        }

        /// <summary>
        /// Convenience overloads read the swarm budget from two optional trailing
        /// arguments. A missing argument keeps the default.
        /// </summary>
        private static void ReadSwarmBudget(Entry[] args, int firstOptionalIndex, Store context,
                                            ref int particleCount, ref int maxIterations)
        {
            if (args.Length <= firstOptionalIndex) return;

            double[] particles = ExtractDoubleArray(args[firstOptionalIndex], context);
            if (particles.Length > 0) particleCount = (int)Math.Round(particles[0]);

            if (args.Length <= firstOptionalIndex + 1) return;

            double[] iterations = ExtractDoubleArray(args[firstOptionalIndex + 1], context);
            if (iterations.Length > 0) maxIterations = (int)Math.Round(iterations[0]);
        }

        /// <summary>
        /// Dispatch one swarmfit-family call. Returns false when the arity does not
        /// match, so SMath can try another handler instead of erroring.
        /// </summary>
        public static bool TryEvaluateSwarmFit(Entry value, Store context, out Entry result,
                                               out double[] arrayResult, out double? scalarResult)
        {
            result = null;
            arrayResult = null;
            scalarResult = null;

            if (value == null || value.Type != TermType.Function) return false;

            string functionName = (value.Text ?? string.Empty).ToLowerInvariant();
            Entry[] args = value.Items ?? new Entry[0];
            LogDebug("Call: " + functionName + " (args=" + args.Length + ")");

            try
            {
                switch (functionName)
                {
                    case "swarmfit":
                    {
                        if (args.Length != 5 && args.Length != 7) return false;

                        double[] xs = ExtractDoubleArray(args[0], context);
                        double[] ys = ExtractDoubleArray(args[1], context);

                        int parameterCount;
                        FitFunction model = ResolveModel(args[2], context, out parameterCount);

                        double[] parameterMin = ExtractDoubleArray(args[3], context);
                        double[] parameterMax = ExtractDoubleArray(args[4], context);

                        int particles = 20;
                        int iterations = 5000;
                        ReadSwarmBudget(args, 5, context, ref particles, ref iterations);

                        FitResult fit = RunSwarmFit(xs, ys, model, parameterMin, parameterMax,
                                                    particles, iterations);

                        result = MakeMatrixResult(fit.Parameters);
                        arrayResult = fit.Parameters;
                        RecordEvaluationResult(value, context, result, arrayResult, null);
                        return true;
                    }

                    case "swarmfit_predict":
                    {
                        if (args.Length != 3) return false;

                        double[] xs = ExtractDoubleArray(args[0], context);

                        int parameterCount;
                        FitFunction model = ResolveModel(args[1], context, out parameterCount);

                        double[] parameters = ExtractDoubleArray(args[2], context);
                        double[] predicted = SwarmFitCore.Predict(xs, model, parameters);

                        result = MakeMatrixResult(predicted);
                        arrayResult = predicted;
                        RecordEvaluationResult(value, context, result, arrayResult, null);
                        return true;
                    }

                    case "swarmfit_details":
                    {
                        if (args.Length != 5 && args.Length != 7) return false;

                        double[] xs = ExtractDoubleArray(args[0], context);
                        double[] ys = ExtractDoubleArray(args[1], context);

                        int parameterCount;
                        FitFunction model = ResolveModel(args[2], context, out parameterCount);

                        double[] parameterMin = ExtractDoubleArray(args[3], context);
                        double[] parameterMax = ExtractDoubleArray(args[4], context);

                        int particles = 20;
                        int iterations = 5000;
                        ReadSwarmBudget(args, 5, context, ref particles, ref iterations);

                        FitResult fit = RunSwarmFit(xs, ys, model, parameterMin, parameterMax,
                                                    particles, iterations);

                        double[] details = SwarmFitCore.Details(fit);

                        result = MakeMatrixResult(details);
                        arrayResult = details;
                        RecordEvaluationResult(value, context, result, arrayResult, null);
                        return true;
                    }

                    case "swarmfit_r2":
                    {
                        if (args.Length != 2) return false;

                        double[] ys = ExtractDoubleArray(args[0], context);
                        double[] predicted = ExtractDoubleArray(args[1], context);
                        double r2 = SwarmFitCore.R2(ys, predicted);

                        LogDebug(string.Format("swarmfit_r2: ys.Len={0}, pred.Len={1}, r2={2}", ys.Length, predicted.Length, r2));
                        if (ys.Length > 0) LogDebug(string.Format("  ys[0]={0}, ys[{1}]={2}", ys[0], ys.Length - 1, ys[ys.Length - 1]));
                        if (predicted.Length > 0) LogDebug(string.Format("  pred[0]={0}, pred[{1}]={2}", predicted[0], predicted.Length - 1, predicted[predicted.Length - 1]));

                        result = MakeDoubleResult(r2);
                        scalarResult = r2;
                        RecordEvaluationResult(value, context, result, null, scalarResult);
                        return true;
                    }

                    case "swarmfit_linear":
                    {
                        if (args.Length != 2) return false;

                        double[] xs = ExtractDoubleArray(args[0], context);
                        double[] ys = ExtractDoubleArray(args[1], context);

                        if (xs.Length != ys.Length || xs.Length == 0)
                        {
                            throw new ArgumentException("xs (length " + xs.Length + ") and ys (length " +
                                                        ys.Length +
                                                        ") must have identical non-zero lengths");
                        }

                        // The search box is derived from the data range so the caller
                        // does not have to supply bounds for the convenience form.
                        double[] parameterMin;
                        double[] parameterMax;
                        SwarmFitCore.LinearBounds(xs, ys, out parameterMin, out parameterMax);

                        FitFunction line = delegate(double x, double[] p) { return p[0] * x + p[1]; };

                        FitResult fit = RunSwarmFit(xs, ys, line, parameterMin, parameterMax, 20, 5000);

                        result = MakeMatrixResult(fit.Parameters);
                        arrayResult = fit.Parameters;
                        RecordEvaluationResult(value, context, result, arrayResult, null);
                        return true;
                    }

                    case "swarmfit_exp":
                    {
                        if (args.Length != 4 && args.Length != 6) return false;

                        double[] xs = ExtractDoubleArray(args[0], context);
                        double[] ys = ExtractDoubleArray(args[1], context);
                        double[] parameterMin = ExtractDoubleArray(args[2], context);
                        double[] parameterMax = ExtractDoubleArray(args[3], context);

                        LogDebug(string.Format("swarmfit_exp: xs.Len={0}, ys.Len={1}, pMin.Len={2}, pMax.Len={3}", xs.Length, ys.Length, parameterMin.Length, parameterMax.Length));
                        if (xs.Length > 0) LogDebug(string.Format("  xs[0]={0}, xs[{1}]={2}", xs[0], xs.Length - 1, xs[xs.Length - 1]));
                        if (ys.Length > 0) LogDebug(string.Format("  ys[0]={0}, ys[{1}]={2}", ys[0], ys.Length - 1, ys[ys.Length - 1]));
                        if (parameterMin.Length > 0) LogDebug("  pMin=" + string.Join(",", Array.ConvertAll(parameterMin, delegate(double d) { return d.ToString(); })));
                        if (parameterMax.Length > 0) LogDebug("  pMax=" + string.Join(",", Array.ConvertAll(parameterMax, delegate(double d) { return d.ToString(); })));

                        int particles = 20;
                        int iterations = 5000;
                        ReadSwarmBudget(args, 4, context, ref particles, ref iterations);

                        FitFunction model = delegate(double x, double[] p)
                        {
                            return p[0] + p[1] * Math.Exp(p[2] * x);
                        };

                        FitResult fit = RunSwarmFit(xs, ys, model, parameterMin, parameterMax,
                                                    particles, iterations);

                        LogDebug("  fit result: " + string.Join(",", Array.ConvertAll(fit.Parameters, delegate(double d) { return d.ToString(); })) + " R2=" + fit.R2 + " iters=" + fit.Iterations);

                        result = MakeMatrixResult(fit.Parameters);
                        arrayResult = fit.Parameters;
                        RecordEvaluationResult(value, context, result, arrayResult, null);
                        return true;
                    }

                    case "swarmfit_expdecay":
                    {
                        if (args.Length != 4 && args.Length != 6) return false;

                        double[] xs = ExtractDoubleArray(args[0], context);
                        double[] ys = ExtractDoubleArray(args[1], context);
                        double[] parameterMin = ExtractDoubleArray(args[2], context);
                        double[] parameterMax = ExtractDoubleArray(args[3], context);

                        int particles = 20;
                        int iterations = 5000;
                        ReadSwarmBudget(args, 4, context, ref particles, ref iterations);

                        FitFunction model = delegate(double x, double[] p)
                        {
                            return p[0] * Math.Exp(-x / p[1]);
                        };

                        FitResult fit = RunSwarmFit(xs, ys, model, parameterMin, parameterMax,
                                                    particles, iterations);

                        result = MakeMatrixResult(fit.Parameters);
                        arrayResult = fit.Parameters;
                        RecordEvaluationResult(value, context, result, arrayResult, null);
                        return true;
                    }

                    case "swarmfit_power":
                    {
                        if (args.Length != 4 && args.Length != 6) return false;

                        double[] xs = ExtractDoubleArray(args[0], context);
                        double[] ys = ExtractDoubleArray(args[1], context);
                        double[] parameterMin = ExtractDoubleArray(args[2], context);
                        double[] parameterMax = ExtractDoubleArray(args[3], context);

                        int particles = 20;
                        int iterations = 5000;
                        ReadSwarmBudget(args, 4, context, ref particles, ref iterations);

                        FitFunction model = delegate(double x, double[] p)
                        {
                            return p[0] * Math.Pow(x, p[1]);
                        };

                        FitResult fit = RunSwarmFit(xs, ys, model, parameterMin, parameterMax,
                                                    particles, iterations);

                        result = MakeMatrixResult(fit.Parameters);
                        arrayResult = fit.Parameters;
                        RecordEvaluationResult(value, context, result, arrayResult, null);
                        return true;
                    }

                    case "swarmfit_gauss":
                    {
                        if (args.Length != 4 && args.Length != 6) return false;

                        double[] xs = ExtractDoubleArray(args[0], context);
                        double[] ys = ExtractDoubleArray(args[1], context);
                        double[] parameterMin = ExtractDoubleArray(args[2], context);
                        double[] parameterMax = ExtractDoubleArray(args[3], context);

                        int particles = 20;
                        int iterations = 5000;
                        ReadSwarmBudget(args, 4, context, ref particles, ref iterations);

                        FitFunction model = delegate(double x, double[] p)
                        {
                            return p[0] * Math.Exp(-Math.Pow(x - p[1], 2) / (2.0 * p[2] * p[2]));
                        };

                        FitResult fit = RunSwarmFit(xs, ys, model, parameterMin, parameterMax,
                                                    particles, iterations);

                        result = MakeMatrixResult(fit.Parameters);
                        arrayResult = fit.Parameters;
                        RecordEvaluationResult(value, context, result, arrayResult, null);
                        return true;
                    }

                    case "swarmfit_poly":
                    {
                        if (args.Length != 5 && args.Length != 7) return false;

                        double[] xs = ExtractDoubleArray(args[0], context);
                        double[] ys = ExtractDoubleArray(args[1], context);

                        double[] degreeArgument = ExtractDoubleArray(args[2], context);
                        int degree = degreeArgument.Length > 0 ? (int)Math.Round(degreeArgument[0]) : 2;

                        double[] parameterMin = ExtractDoubleArray(args[3], context);
                        double[] parameterMax = ExtractDoubleArray(args[4], context);

                        int particles = 20;
                        int iterations = 5000;
                        ReadSwarmBudget(args, 5, context, ref particles, ref iterations);

                        // Y = p0*X^degree + p1*X^(degree-1) + ... + p_degree
                        FitFunction model = delegate(double x, double[] p)
                        {
                            double sum = 0;
                            for (int d = 0; d <= degree && d < p.Length; d++)
                                sum += p[d] * Math.Pow(x, degree - d);
                            return sum;
                        };

                        FitResult fit = RunSwarmFit(xs, ys, model, parameterMin, parameterMax,
                                                    particles, iterations);

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
                throw new Exception("SwarmFit evaluation error in " + functionName + ": " + ex.Message, ex);
            }
        }
    }

    /// <summary>
    /// The plugin entry point. Registers 10 functions under 17 signatures.
    /// </summary>
    public class SwarmFitPlugin : IPlugin, IPluginHandleEvaluation, IPluginLowLevelEvaluationFast
    {
        private TermInfo[] _termsHandled;

        public void Initialize()
        {
            if (_termsHandled != null) return;

            List<TermInfo> list = new List<TermInfo>();

            // swarmfit(xs, ys, model, p_min, p_max [, particles, max_iterations])
            list.Add(new TermInfo("swarmfit", TermType.Function,
                "Fits model parameters to X/Y data by particle swarm optimization. Returns the parameter vector.",
                FunctionSections.Unknown, true,
                new ArgumentInfo[]
                {
                    new ArgumentInfo("xs"), new ArgumentInfo("ys"), new ArgumentInfo("model"),
                    new ArgumentInfo("p_min"), new ArgumentInfo("p_max")
                }));

            list.Add(new TermInfo("swarmfit", TermType.Function,
                "Fits model parameters by particle swarm optimization with a custom swarm budget.",
                FunctionSections.Unknown, true,
                new ArgumentInfo[]
                {
                    new ArgumentInfo("xs"), new ArgumentInfo("ys"), new ArgumentInfo("model"),
                    new ArgumentInfo("p_min"), new ArgumentInfo("p_max"),
                    new ArgumentInfo("particles"), new ArgumentInfo("max_iterations")
                }));

            // swarmfit_predict(xs, model, p)
            list.Add(new TermInfo("swarmfit_predict", TermType.Function,
                "Evaluates Y_hat = f(X, p) over an X vector using already fitted parameters.",
                FunctionSections.Unknown, true,
                new ArgumentInfo[]
                {
                    new ArgumentInfo("xs"), new ArgumentInfo("model"), new ArgumentInfo("p")
                }));

            // swarmfit_details(xs, ys, model, p_min, p_max [, particles, max_iterations])
            list.Add(new TermInfo("swarmfit_details", TermType.Function,
                "Returns fit diagnostics as [SSE; RMSE; MaxResidual; Iterations; Improvements].",
                FunctionSections.Unknown, true,
                new ArgumentInfo[]
                {
                    new ArgumentInfo("xs"), new ArgumentInfo("ys"), new ArgumentInfo("model"),
                    new ArgumentInfo("p_min"), new ArgumentInfo("p_max")
                }));

            list.Add(new TermInfo("swarmfit_details", TermType.Function,
                "Returns fit diagnostics with a custom swarm budget.",
                FunctionSections.Unknown, true,
                new ArgumentInfo[]
                {
                    new ArgumentInfo("xs"), new ArgumentInfo("ys"), new ArgumentInfo("model"),
                    new ArgumentInfo("p_min"), new ArgumentInfo("p_max"),
                    new ArgumentInfo("particles"), new ArgumentInfo("max_iterations")
                }));

            // swarmfit_r2(ys, y_pred)
            list.Add(new TermInfo("swarmfit_r2", TermType.Function,
                "Coefficient of determination R^2 between observed Y and predicted Y.",
                FunctionSections.Unknown, true,
                new ArgumentInfo[]
                {
                    new ArgumentInfo("ys"), new ArgumentInfo("y_pred")
                }));

            // swarmfit_linear(xs, ys)
            list.Add(new TermInfo("swarmfit_linear", TermType.Function,
                "Fits Y = a*X + b and returns [a; b].",
                FunctionSections.Unknown, true,
                new ArgumentInfo[]
                {
                    new ArgumentInfo("xs"), new ArgumentInfo("ys")
                }));

            // swarmfit_exp(xs, ys, p_min, p_max [, particles, max_iterations])
            list.Add(new TermInfo("swarmfit_exp", TermType.Function,
                "Fits Y = a + b*exp(c*X) and returns [a; b; c].",
                FunctionSections.Unknown, true,
                new ArgumentInfo[]
                {
                    new ArgumentInfo("xs"), new ArgumentInfo("ys"),
                    new ArgumentInfo("p_min"), new ArgumentInfo("p_max")
                }));

            list.Add(new TermInfo("swarmfit_exp", TermType.Function,
                "Fits Y = a + b*exp(c*X) with a custom swarm budget.",
                FunctionSections.Unknown, true,
                new ArgumentInfo[]
                {
                    new ArgumentInfo("xs"), new ArgumentInfo("ys"),
                    new ArgumentInfo("p_min"), new ArgumentInfo("p_max"),
                    new ArgumentInfo("particles"), new ArgumentInfo("max_iterations")
                }));

            // swarmfit_expdecay(xs, ys, p_min, p_max [, particles, max_iterations])
            list.Add(new TermInfo("swarmfit_expdecay", TermType.Function,
                "Fits Y = a*exp(-X/b) and returns [a; b].",
                FunctionSections.Unknown, true,
                new ArgumentInfo[]
                {
                    new ArgumentInfo("xs"), new ArgumentInfo("ys"),
                    new ArgumentInfo("p_min"), new ArgumentInfo("p_max")
                }));

            list.Add(new TermInfo("swarmfit_expdecay", TermType.Function,
                "Fits Y = a*exp(-X/b) with a custom swarm budget.",
                FunctionSections.Unknown, true,
                new ArgumentInfo[]
                {
                    new ArgumentInfo("xs"), new ArgumentInfo("ys"),
                    new ArgumentInfo("p_min"), new ArgumentInfo("p_max"),
                    new ArgumentInfo("particles"), new ArgumentInfo("max_iterations")
                }));

            // swarmfit_power(xs, ys, p_min, p_max [, particles, max_iterations])
            list.Add(new TermInfo("swarmfit_power", TermType.Function,
                "Fits Y = a*X^b and returns [a; b].",
                FunctionSections.Unknown, true,
                new ArgumentInfo[]
                {
                    new ArgumentInfo("xs"), new ArgumentInfo("ys"),
                    new ArgumentInfo("p_min"), new ArgumentInfo("p_max")
                }));

            list.Add(new TermInfo("swarmfit_power", TermType.Function,
                "Fits Y = a*X^b with a custom swarm budget.",
                FunctionSections.Unknown, true,
                new ArgumentInfo[]
                {
                    new ArgumentInfo("xs"), new ArgumentInfo("ys"),
                    new ArgumentInfo("p_min"), new ArgumentInfo("p_max"),
                    new ArgumentInfo("particles"), new ArgumentInfo("max_iterations")
                }));

            // swarmfit_gauss(xs, ys, p_min, p_max [, particles, max_iterations])
            list.Add(new TermInfo("swarmfit_gauss", TermType.Function,
                "Fits Y = a*exp(-(X-b)^2/(2c^2)) and returns [a; b; c].",
                FunctionSections.Unknown, true,
                new ArgumentInfo[]
                {
                    new ArgumentInfo("xs"), new ArgumentInfo("ys"),
                    new ArgumentInfo("p_min"), new ArgumentInfo("p_max")
                }));

            list.Add(new TermInfo("swarmfit_gauss", TermType.Function,
                "Fits a Gaussian peak with a custom swarm budget.",
                FunctionSections.Unknown, true,
                new ArgumentInfo[]
                {
                    new ArgumentInfo("xs"), new ArgumentInfo("ys"),
                    new ArgumentInfo("p_min"), new ArgumentInfo("p_max"),
                    new ArgumentInfo("particles"), new ArgumentInfo("max_iterations")
                }));

            // swarmfit_poly(xs, ys, degree, p_min, p_max [, particles, max_iterations])
            list.Add(new TermInfo("swarmfit_poly", TermType.Function,
                "Fits a polynomial of the given degree and returns its coefficients.",
                FunctionSections.Unknown, true,
                new ArgumentInfo[]
                {
                    new ArgumentInfo("xs"), new ArgumentInfo("ys"), new ArgumentInfo("degree"),
                    new ArgumentInfo("p_min"), new ArgumentInfo("p_max")
                }));

            list.Add(new TermInfo("swarmfit_poly", TermType.Function,
                "Fits a polynomial of the given degree with a custom swarm budget.",
                FunctionSections.Unknown, true,
                new ArgumentInfo[]
                {
                    new ArgumentInfo("xs"), new ArgumentInfo("ys"), new ArgumentInfo("degree"),
                    new ArgumentInfo("p_min"), new ArgumentInfo("p_max"),
                    new ArgumentInfo("particles"), new ArgumentInfo("max_iterations")
                }));

            _termsHandled = list.ToArray();
        }

        public TermInfo[] GetTermsHandled(SessionProfile sessionProfile)
        {
            if (_termsHandled == null) Initialize();
            return _termsHandled;
        }

        public bool TryEvaluateExpression(Entry value, Store context, out Entry result)
        {
            double[] arrayResult;
            double? scalarResult;
            return SwarmFitEngine.TryEvaluateSwarmFit(value, context, out result, out arrayResult, out scalarResult);
        }

        /// <summary>
        /// Not part of the current interface set; kept so older hosts that still
        /// probe for it get a defined answer instead of a missing-method error.
        /// </summary>
        public bool NumericEvaluation(Term value, TNumber[] args, ref TNumber result)
        {
            return false;
        }

        public void Dispose()
        {
        }
    }
}
