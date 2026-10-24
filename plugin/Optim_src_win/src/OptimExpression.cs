/*
 * OptimExpression - evaluate a worksheet expression at a numeric point.
 *
 * This is the bridge that lets the optimiser solve user-defined problems
 * instead of only the built-in benchmark functions. The expression is kept as
 * an Entry tree; before each evaluation the free variables are replaced by
 * numeric literals and the result is evaluated by SMath's own numeric engine.
 * No expression parser is needed and worksheet functions, constants and units
 * keep working exactly as they do in the rest of the document.
 *
 * Variable order:
 *   - when the caller supplies a variable vector, names are taken in that
 *     order;
 *   - otherwise free operands are collected in order of first appearance.
 * Explicit variables are recommended for multi-variable problems because the
 * order must match x0.
 */

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using SMath.Manager;
using SMath.Math;
using SMath.Math.Numeric;

namespace OptimWrapper
{
    internal static class OptimExpression
    {
        private static readonly string[] ReservedNames =
        {
            "pi", "e", "i", "inf", "infinity", "true", "false",
            "nan", "undef", "#", "I?"
        };

        // ------------------------------------------------------------ variables

        /// <summary>
        /// Read variable names from a raw variable vector, e.g. [x; y] or
        /// mat(x,y,2,1). Names are returned in the order they appear.
        /// </summary>
        public static string[] ParseVariableNames(Entry varsArg, Store context)
        {
            varsArg = ResolveArrayExpression(varsArg, context);

            List<string> names = new List<string>();
            CollectDeclaredNames(varsArg, names);

            if (names.Count == 0)
                throw new ArgumentException("The variable vector is empty.");

            return names.ToArray();
        }

        /// <summary>
        /// Collect free operands in order of first appearance. Defined
        /// variables, constants, units and strings are not free variables.
        /// </summary>
        public static string[] InferVariableNames(Entry expr, Store context, int expected)
        {
            expr = ResolveArrayExpression(expr, context);

            List<string> names = new List<string>();
            CollectFreeNames(expr, context, names);

            if (names.Count != expected)
            {
                string found = names.Count == 0
                    ? "none"
                    : string.Join(", ", names.ToArray());
                throw new ArgumentException(
                    "Expression has " + names.Count + " free variable(s) (" + found +
                    ") but x0 has " + expected + " value(s). Pass the variable vector " +
                    "explicitly, e.g. optim_eval_expr(expr, [x; y], [1; 2]).");
            }

            return names.ToArray();
        }

        public static bool TryResolveFunction(
            Entry expr, Store context, out Entry body, out string[] names)
        {
            body = null;
            names = null;

            if (expr == null || context == null)
                return false;
            if (expr.Type != TermType.Operand ||
                (expr.Items != null && expr.Items.Length != 0))
                return false;

            string text = expr.Text;
            if (string.IsNullOrEmpty(text))
                return false;
            if (text.Length >= 2 && text[0] == '"' && text[text.Length - 1] == '"')
                text = text.Substring(1, text.Length - 2);

            try
            {
                for (int i = 0; i < context.Count; i++)
                {
                    Definition def = context[i];
                    if (def == null || !def.IsFunction || def.Result == null ||
                        def.Variables == null || def.Variables.Length == 0 ||
                        !string.Equals(def.Name, text, StringComparison.Ordinal))
                        continue;

                    string[] variableNames = new string[def.Variables.Length];
                    bool valid = true;
                    for (int j = 0; j < def.Variables.Length; j++)
                    {
                        Term variable = def.Variables[j];
                        if (variable == null || string.IsNullOrEmpty(variable.Text))
                        {
                            valid = false;
                            break;
                        }
                        variableNames[j] = variable.Text;
                    }

                    if (!valid)
                        return false;

                    body = def.Result;
                    names = variableNames;
                    return true;
                }
            }
            catch
            {
                // A store without a full evaluation scope can reject the
                // lookup; fall back to the ordinary expression path.
            }

            return false;
        }

        private static void CollectDeclaredNames(Entry entry, List<string> names)
        {
            if (entry == null || entry.Items == null || entry.Items.Length == 0)
            {
                AddDeclaredName(entry, names);
                return;
            }

            int rows, cols;
            if (IsMatrixLiteral(entry) && TryGetMatrixShape(entry, out rows, out cols))
            {
                int count = rows * cols;
                for (int i = 0; i < count; i++)
                    AddDeclaredName(entry.Items[i], names);
                return;
            }

            for (int i = 0; i < entry.Items.Length; i++)
                CollectDeclaredNames(entry.Items[i], names);
        }

        private static void AddDeclaredName(Entry entry, List<string> names)
        {
            if (entry == null || entry.Type != TermType.Operand || entry.Text == null)
                return;

            string text = entry.Text;
            if (text.Length == 0)
                return;

            if (text.Length >= 2 && text[0] == '"' && text[text.Length - 1] == '"')
                text = text.Substring(1, text.Length - 2);

            if (text.StartsWith("'"))
                throw new ArgumentException("A unit cannot be used as an optimisation variable: " + text + ".");

            double dummy;
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out dummy))
                throw new ArgumentException("The variable vector must contain names, not numbers.");

            if (!names.Contains(text))
                names.Add(text);
        }

        private static void CollectFreeNames(Entry entry, Store context, List<string> names)
        {
            if (entry == null)
                return;

            if (entry.Type == TermType.Operand && (entry.Items == null || entry.Items.Length == 0))
            {
                string text = entry.Text;
                if (string.IsNullOrEmpty(text))
                    return;

                if (text[0] == '\'' || text[0] == '"')
                    return;

                double dummy;
                if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out dummy))
                    return;

                if (IsReserved(text))
                    return;

                if (IsDefined(text, context))
                    return;

                if (!names.Contains(text))
                    names.Add(text);
                return;
            }

            if (entry.Items != null)
            {
                for (int i = 0; i < entry.Items.Length; i++)
                    CollectFreeNames(entry.Items[i], context, names);
            }
        }

        public static bool IsReservedName(string text)
        {
            return IsReserved(text);
        }

        private static bool IsReserved(string text)
        {
            for (int i = 0; i < ReservedNames.Length; i++)
                if (string.Equals(ReservedNames[i], text, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        private static bool IsDefined(string text, Store context)
        {
            if (context == null)
                return false;

            try
            {
                Definition def;
                if (context.Contains(new Term(text, TermType.Operand, 0), out def))
                    return true;
            }
            catch
            {
                // A store without a full evaluation scope can reject the
                // lookup; treat the name as free rather than failing here.
            }

            return false;
        }

        /// <summary>
        /// Inline a named worksheet variable whose definition is a matrix literal.
        /// For example M := mat(r1, r2, 2, 1) becomes the matrix entry itself.
        /// Function definitions and scalar variables are left alone.
        /// </summary>
        private static Entry ResolveArrayExpression(Entry expr, Store context)
        {
            if (expr == null || context == null)
                return expr;

            Entry current = expr;
            for (int depth = 0; depth < 8; depth++)
            {
                if (current.Type != TermType.Operand ||
                    (current.Items != null && current.Items.Length != 0))
                    break;

                string text = current.Text;
                if (string.IsNullOrEmpty(text))
                    break;

                Definition def;
                try
                {
                    if (!context.Contains(new Term(text, TermType.Operand, 0), out def))
                        break;
                }
                catch
                {
                    break;
                }

                if (def == null || def.Result == null || def.IsFunction)
                    break;
                if (def.Variables != null && def.Variables.Length > 0)
                    break;
                if (!IsMatrixLiteral(def.Result))
                    break;
                if (ReferenceEquals(def.Result, current))
                    break;

                current = def.Result;
            }

            return current;
        }

        // --------------------------------------------------------- substitution

        /// <summary>
        /// Evaluate expr at x using the given variable names. Returns one value
        /// for a scalar expression or the flattened matrix for a vector.
        /// </summary>
        public static double[] Evaluate(Entry expr, string[] names, double[] x, Store context)
        {
            if (expr == null)
                throw new ArgumentException("The expression is empty.");
            if (names == null || x == null || names.Length != x.Length)
                throw new ArgumentException("The variable vector and x0 must have the same length.");
            if (context == null)
                throw new ArgumentException("No SMath evaluation context is available.");

            // A named array such as M := mat(r1, r2, 2, 1) is stored as a
            // worksheet definition. Inline it before substituting so its cells
            // are treated exactly like an inline matrix literal.
            expr = ResolveArrayExpression(expr, context);

            // Evaluate a literal matrix one element at a time. This is the
            // array form used for residual systems and constrained problems:
            // mat(r1, r2, ..., m, 1) or mat(f, c1, ..., m+1, 1). Evaluating
            // the cells directly avoids depending on the matrix constructor
            // while substituting the optimiser variables.
            double[] cellValues;
            if (TryEvaluateMatrixCells(expr, names, x, context, out cellValues))
                return cellValues;

            double fastScalar;
            if (TryEvaluateFast(expr, names, x, out fastScalar))
                return new double[] { fastScalar };

            Entry substituted = Substitute(expr, names, x);

            TNumber number;
            try
            {
                number = Computation.NumericCalculation(substituted, context);
            }
            catch (Exception ex)
            {
                throw new ArgumentException(
                    "Could not evaluate the expression at x = [" +
                    FormatPoint(x) + "]: " + ex.Message, ex);
            }

            return ToDoubles(number);
        }

        private static bool TryEvaluateMatrixCells(
            Entry expr, string[] names, double[] x, Store context, out double[] values)
        {
            values = null;

            int rows, cols;
            if (!IsMatrixLiteral(expr) || !TryGetMatrixShape(expr, out rows, out cols))
                return false;

            int count = rows * cols;
            values = new double[count];
            for (int i = 0; i < count; i++)
            {
                double fast;
                if (TryEvaluateFast(expr.Items[i], names, x, out fast))
                {
                    values[i] = fast;
                    continue;
                }

                Entry cell = Substitute(expr.Items[i], names, x);

                TNumber number;
                try
                {
                    number = Computation.NumericCalculation(cell, context);
                }
                catch (Exception ex)
                {
                    throw new ArgumentException(
                        "Could not evaluate matrix element " + (i + 1) +
                        " at x = [" + FormatPoint(x) + "]: " + ex.Message, ex);
                }

                double[] cellValues = ToDoubles(number);
                if (cellValues.Length != 1)
                    throw new ArgumentException(
                        "Matrix element " + (i + 1) + " did not produce a scalar.");

                values[i] = cellValues[0];
            }

            return true;
        }

        /// <summary>
        /// Fast path for the arithmetic found in worksheet objective and
        /// residual cells. SMath's numeric calculator is general but costs
        /// milliseconds per call, which dominates a finite-difference solver.
        /// Unsupported nodes return false and fall back to SMath.
        /// </summary>
        private static bool TryEvaluateFast(Entry entry, string[] names, double[] x, out double value)
        {
            value = 0.0;
            if (entry == null)
                return false;

            if (entry.Type == TermType.Operand &&
                (entry.Items == null || entry.Items.Length == 0))
            {
                string text = entry.Text;
                if (string.IsNullOrEmpty(text))
                    return false;

                for (int i = 0; i < names.Length; i++)
                {
                    if (string.Equals(names[i], text, StringComparison.Ordinal))
                    {
                        value = x[i];
                        return true;
                    }
                }

                return double.TryParse(text, NumberStyles.Float,
                                       CultureInfo.InvariantCulture, out value);
            }

            Entry[] items = entry.Items;
            if (items == null || items.Length == 0)
                return false;

            string op = entry.Text;
            if (op == null)
                return false;

            if (items.Length == 1)
            {
                double a;
                if (!TryEvaluateFast(items[0], names, x, out a))
                    return false;
                if (op == "+")
                {
                    value = a;
                    return true;
                }
                if (op == "-" || op == "\u2212")
                {
                    value = -a;
                    return true;
                }
                return false;
            }

            if (items.Length != 2)
                return false;

            double left, right;
            if (!TryEvaluateFast(items[0], names, x, out left) ||
                !TryEvaluateFast(items[1], names, x, out right))
                return false;

            switch (op)
            {
                case "+":
                    value = left + right;
                    return true;
                case "-":
                case "\u2212":
                    value = left - right;
                    return true;
                case "*":
                case "\u00B7":
                case "\u00D7":
                    value = left * right;
                    return true;
                case "/":
                case "\u00F7":
                    if (right == 0.0)
                        return false;
                    value = left / right;
                    return true;
                case "^":
                    value = Math.Pow(left, right);
                    return true;
                default:
                    return false;
            }
        }

        private static Entry Substitute(Entry entry, string[] names, double[] x)
        {
            if (entry == null)
                return null;

            if (entry.Type == TermType.Operand && (entry.Items == null || entry.Items.Length == 0))
            {
                string text = entry.Text;
                if (text != null)
                {
                    for (int i = 0; i < names.Length; i++)
                    {
                        if (string.Equals(names[i], text, StringComparison.Ordinal))
                            return Entry.Create(new TDouble(x[i]).ToTerms());
                    }
                }
                return entry;
            }

            if (entry.Items == null || entry.Items.Length == 0)
                return entry;

            Entry[] items = new Entry[entry.Items.Length];
            for (int i = 0; i < items.Length; i++)
                items[i] = Substitute(entry.Items[i], names, x);

            Entry clone = new Entry(entry.Text, entry.Type, items);
            clone.Tag = entry.Tag;
            clone.TagSubst = entry.TagSubst;
            return clone;
        }

        private static double[] ToDoubles(TNumber number)
        {
            if (number == null)
                throw new ArgumentException("The expression produced no numeric result.");

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
                        values[index++] = matrix[r, c].obj.ToDouble();
                }
                return values;
            }

            TDouble asDouble = number.obj as TDouble;
            if (asDouble != null)
                return new double[] { asDouble.ToDouble() };

            TFraction asFraction = number.obj as TFraction;
            if (asFraction != null)
                return new double[] { asFraction.ToDouble() };

            throw new ArgumentException(
                "The expression produced " + number.obj.GetType().Name +
                ", which is not a number or vector.");
        }

        // -------------------------------------------------------------- helpers

        public static bool IsMatrixLiteralEntry(Entry entry)
        {
            return IsMatrixLiteral(entry);
        }

        private static bool IsMatrixLiteral(Entry entry)
        {
            return entry.Text == "mat" || entry.Text == "matrix" ||
                   string.IsNullOrEmpty(entry.Text);
        }

        private static bool TryGetMatrixShape(Entry entry, out int rows, out int cols)
        {
            rows = 0;
            cols = 0;

            int count = entry.Items.Length;
            if (count < 2)
                return false;

            if (!TryParseInt(entry.Items[count - 2], out rows) ||
                !TryParseInt(entry.Items[count - 1], out cols))
                return false;

            return rows > 0 && cols > 0 && rows * cols == count - 2;
        }

        private static bool TryParseInt(Entry entry, out int value)
        {
            value = 0;
            if (entry == null || entry.Text == null)
                return false;

            double parsed;
            if (!double.TryParse(entry.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
                return false;

            int rounded = (int)Math.Round(parsed);
            if (Math.Abs(parsed - rounded) > 1e-9)
                return false;

            value = rounded;
            return true;
        }

        private static string FormatPoint(double[] x)
        {
            string[] parts = new string[x.Length];
            for (int i = 0; i < x.Length; i++)
                parts[i] = x[i].ToString("G6", CultureInfo.InvariantCulture);
            return string.Join("; ", parts);
        }
    }

    /// <summary>
    /// Managed side of the native eval callback. The native engine computes
    /// gradients/Jacobians by finite differences; this class only maps a point
    /// to the requested values.
    /// </summary>
    internal sealed class CustomEvalContext
    {
        private readonly Entry _expr;
        private readonly string[] _names;
        private readonly Store _store;
        private readonly int _kind;
        private readonly int _m;

        public Exception Error;
        public int Calls;

        public CustomEvalContext(Entry expr, string[] names, Store store, int kind, int m)
        {
            _expr = expr;
            _names = names;
            _store = store;
            _kind = kind;
            _m = m;
        }

        public int Evaluate(IntPtr xPtr, int n, IntPtr outPtr, int outLen, IntPtr user)
        {
            Calls++;
            try
            {
                if (n <= 0 || xPtr == IntPtr.Zero || outPtr == IntPtr.Zero || outLen <= 0)
                    throw new ArgumentException("The native evaluator passed an empty point or buffer.");

                double[] x = new double[n];
                Marshal.Copy(xPtr, x, 0, n);

                double[] values = OptimExpression.Evaluate(_expr, _names, x, _store);

                if (_kind == 0)
                {
                    if (values.Length != 1)
                        throw new ArgumentException(
                            "An objective expression must return one value; it returned " +
                            values.Length + ". Use optim_broyden for residual systems or " +
                            "optim_sumt for [f; constraints].");
                    if (outLen != 1)
                        throw new ArgumentException("The native engine requested an invalid objective buffer.");
                    Marshal.Copy(values, 0, outPtr, 1);
                }
                else if (_kind == 1)
                {
                    if (values.Length != _m + 1)
                        throw new ArgumentException(
                            "A constrained expression must return [f; c1; ...; cm] with " +
                            (_m + 1) + " values; it returned " + values.Length + ".");
                    if (outLen == 1)
                    {
                        Marshal.Copy(values, 0, outPtr, 1);
                    }
                    else if (outLen == _m + 1)
                    {
                        Marshal.Copy(values, 0, outPtr, _m + 1);
                    }
                    else
                    {
                        throw new ArgumentException(
                            "The native engine requested an invalid constraint buffer.");
                    }
                }
                else
                {
                    if (values.Length != _m)
                        throw new ArgumentException(
                            "A residual system must return " + _m + " values; it returned " +
                            values.Length + ".");
                    if (outLen != _m)
                        throw new ArgumentException("The native engine requested an invalid residual buffer.");
                    Marshal.Copy(values, 0, outPtr, _m);
                }

                return 0;
            }
            catch (Exception ex)
            {
                if (Error == null)
                    Error = ex;
                return 1;
            }
        }
    }
}
