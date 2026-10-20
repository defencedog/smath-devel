/*
 * CoolPropPlugin - SMath Studio plugin for the CoolProp thermodynamic library.
 *
 * Windows port of the Mono/Linux CoolPropWrapper. The Java-shaped architecture
 * of the original is kept because it works: an IFunction per CoolProp entry
 * point, discovered by reflection over this assembly, so a new function is one
 * new file and nothing else.
 *
 * What changed relative to the Mono/Linux build:
 *
 *  1. Units. The Mono build's Unit.cs tagged results with NAMED derived units
 *     ('Pa, 'J/'mol, 'W/'m/'K). SMath's BaseEntry.ComUnits compares unit ASTs
 *     for equality when combining components, so a named unit makes a result
 *     dimensionally unequal to a native worksheet expression - Error 74 on the
 *     first subtraction. This build tags base-unit expressions only
 *     ('kg/('m*'s^2) for Pa, 'm^2/'s^2 for J/kg, ...). See Unit.cs.
 *
 *  2. Native binding. Like the Mono build, each entry point is declared twice
 *     (DllImport "CoolProp_x86" / "CoolProp_x64") and a dispatcher picks by
 *     IntPtr.Size: the engines sit side by side at the package root under
 *     those names, because SMath's installer rejects engine subfolders. Each
 *     declaration still binds with a plain EntryPoint - see CoolPropEngine.cs.
 *
 *  3. Explicit engine load. On Windows the native DLL is pulled in by absolute
 *     path from the plugin folder (Windows does not probe a plugin's own
 *     directory for native dependencies). On Mono the dllmap does it.
 *
 *  4. No reflection on GlobalParams. The Mono build had to dig a
 *     CurrentSessionProfile property out of SMath.Manager.GlobalParams because
 *     1.75 exposed it only non-publicly. In 1.73.9126 it is not there at all;
 *     the live profile arrives as the argument of GetTermsHandled, which is
 *     where it is used.
 *
 *  5. MSVC `long` returns are declared `int` (32-bit on Windows); see
 *     CoolPropEngine.cs.
 *
 * Targets .NET Framework 2.0 (CLR 2.0) for Mono/Linux parity, like the other
 * plugins in this collection.
 */

using System.Collections.Generic;
using System.Reflection;
using CoolPropWrapper.Functions;
using SMath.Manager;
using SMath.Math.Numeric;
using SMath.Math.Symbolic;

namespace CoolPropWrapper
{
    public class coolpropPlugin : SMath.Math.IPluginLowLevelEvaluationFast, SMath.Manager.IPlugin, SMath.Manager.IPluginHandleEvaluation
    {
        // SMath Studio >= 0.98. The Mono build kept an AssemblyInfo[] for a
        // Dependences property that 1.73/1.75 no longer declares on IPlugin;
        // the field is gone rather than left as dead weight.
        TermInfo[] termInfos = new TermInfo[] { };
        List<IFunction> functions = new List<IFunction>();
        bool initialized;

        // ------------------------------------------------------------------
        // Diagnostics. DEBUG-only: a logger that runs in a release build would
        // write into the installation folder on every evaluation.
        // ------------------------------------------------------------------

        static string AssemblyDirectory
        {
            get
            {
                var filepath = new System.Uri(Assembly.GetExecutingAssembly().CodeBase).LocalPath;
                return System.IO.Path.GetDirectoryName(filepath);
            }
        }

        static string LogFile
        {
            get { return System.IO.Path.Combine(AssemblyDirectory, "log.txt"); }
        }

        public static void LogInfo(string Category, string Text, params object[] args)
        {
#if DEBUG
            var method = new System.Diagnostics.StackFrame(1).GetMethod();
            System.IO.File.AppendAllText(
                LogFile,
                string.Format(
                    "{0} {1} {2} [{3}.{4}] {5}{6}",
                    System.DateTime.Now.ToShortDateString(),
                    System.DateTime.Now.ToLongTimeString(),
                    Category,
                    method.DeclaringType.Name,
                    method.Name.Substring(method.Name.LastIndexOf('.') + 1),
                    string.Format(Text, args),
                    System.Environment.NewLine()),
                System.Text.Encoding.UTF8);
#endif
        }

        // ------------------------------------------------------------------
        // Argument marshalling
        // ------------------------------------------------------------------

        /// <summary>
        /// Numerically evaluates a function argument. The value is always in SI
        /// base units (SMath converts through UnitInfo.Factor), which is what
        /// the CoolProp C API expects.
        /// </summary>
        public static TNumber GetNumberParam(Entry arg, SMath.Math.Store context)
        {
            // Computation is the current class for this decision; the older
            // SMath.Math.Decision is marked obsolete with exactly the same
            // signature, so either compiles - this is the non-obsolete one.
            return SMath.Math.Computation.NumericCalculation(arg, context);
        }

        /// <summary>
        /// Evaluates an argument to a plain string. String arguments reach a
        /// plugin as text operands, and under Mono the TDouble text carries
        /// SMath's parser escapes ('.' becomes a literal "\002E" and so on), so
        /// the raw Text field is preferred and any residual \NNNN\ sequence is
        /// decoded.
        /// </summary>
        public static string GetStringParam(Entry arg, SMath.Math.Store context)
        {
            var dbl = GetNumberParam(arg, context).obj as TDouble;
            // ReferenceEquals, not == : TDouble does not overload ==, but the
            // check has to let a failed cast (a matrix, say) fall through to
            // the ArgumentMustBeString error rather than NRE.
            if (ReferenceEquals(dbl, null) || !dbl.isText)
                throw new SMath.Manager.MathException(Errors.ArgumentMustBeString);
            var raw = dbl.Text;
            if (string.IsNullOrEmpty(raw))
                raw = dbl.ToString();
            return UnescapeMathString(raw.Trim(StringTrimChars));
        }

        private static readonly char[] StringTrimChars = new char[] {
            '"', '\'',
            (Symbols.StringChar != null && Symbols.StringChar.Length > 0) ? Symbols.StringChar[0] : '"'
        };

        // Decodes SMath's "\NNNN\" character escapes ("\002E\" -> '.'), where
        // NNNN is 4 hex digits and a trailing backslash delimits the escape.
        // A backslash NOT followed by four hex digits is literal; the decoder
        // is idempotent on clean strings.
        internal static string UnescapeMathString(string s)
        {
            if (s == null || s.IndexOf('\\') < 0)
                return s;
            var sb = new System.Text.StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '\\' && i + 4 < s.Length &&
                    System.Uri.IsHexDigit(s[i + 1]) && System.Uri.IsHexDigit(s[i + 2]) &&
                    System.Uri.IsHexDigit(s[i + 3]) && System.Uri.IsHexDigit(s[i + 4]))
                {
                    sb.Append((char)int.Parse(s.Substring(i + 1, 4),
                        System.Globalization.NumberStyles.HexNumber));
                    i += 4;
                    if (i + 1 < s.Length && s[i + 1] == '\\')
                        i++;  // delimiter
                }
                else
                {
                    sb.Append(s[i]);
                }
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------
        // Results
        // ------------------------------------------------------------------

        /// <summary>
        /// Builds a numeric result tagged with the base-unit expression for the
        /// output parameter. A null unit leaves Units untouched (dimensionless).
        /// </summary>
        public static Entry MakeDoubleResult(double result, MItem unit)
        {
            // CoolProp reports failure by returning +Infinity/_HUGE rather than
            // raising, so it has to be converted into a real error here.
            if (double.IsInfinity(result))
                CoolPropError();

            var d = new TDouble(result);
            // ReferenceEquals: MItem overloads == and both operands are
            // dereferenced, so a plain `unit != null` would throw.
            if (!ReferenceEquals(unit, null))
                d.Units = unit;
            return Entry.Create(d.ToTerms());
        }

        public static Entry MakeStringResult(string result)
        {
            return new Entry(Symbols.StringChar + result + Symbols.StringChar, TermType.Operand, new Entry[0]);
        }

        /// <summary>
        /// Converts a non-finite CoolProp result into an exception carrying the
        /// native error string. The errstring is fetched twice on purpose: the
        /// first call usually returns "buffer too small" style text because the
        /// buffer was sized before the message was known.
        /// </summary>
        public static void CoolPropError(string message = "")
        {
            string errStr;
            if (!CoolProp_get_global_param_string.ReadGlobalString("errstring", out errStr))
            {
                string second;
                CoolProp_get_global_param_string.ReadGlobalString("errstring", out second);
                errStr = "The error message could not be retrieved from the CoolProp library (buffer size?); the second try returned: " + second;
            }
            throw new System.Exception(message + errStr);
        }

        /// <summary>
        /// SMath.Manager.IPlugin derives from IDisposable, so this is part of the
        /// interface, not an optional extra. It removes the debug log if one was
        /// created, so a plugin built as DEBUG leaves no litter behind.
        /// </summary>
        public void Dispose()
        {
            try
            {
                if (System.IO.File.Exists(LogFile))
                    System.IO.File.Delete(LogFile);
            }
            catch (System.Exception)
            {
            }
        }

        // ------------------------------------------------------------------
        // IPlugin / IPluginHandleEvaluation / IPluginLowLevelEvaluationFast
        // ------------------------------------------------------------------

        TermInfo[] IPluginHandleEvaluation.GetTermsHandled(SMath.Manager.SessionProfile sessionProfile)
        {
            // The host hands us the LIVE profile here, once at load. Keep it: it
            // is where the UI language and the units system come from, and
            // GlobalParams (the old route) is obsolete.
            if (sessionProfile != null)
                _profile = sessionProfile;
            CoolPropEngine.EnsureLoaded();
            RegisterFromProfile();
            return termInfos;
        }

        void IPlugin.Initialize()
        {
            // Pull the engine in early. Registration is intentionally deferred to
            // GetTermsHandled (and, as a backstop, to the first evaluation)
            // because those are the only callbacks that carry a live
            // SessionProfile - which is where the UI language now lives. The old
            // GlobalParams.CurLang3Letter is obsolete and there is no reason to
            // reach for it.
            CoolPropEngine.EnsureLoaded();
            RegisterFromProfile();
        }

        /// <summary>Profile handed to us by the host, kept for language lookups.</summary>
        private static SMath.Manager.SessionProfile _profile;

        /// <summary>
        /// Three-letter language code of the bound session profile ("eng",
        /// "rus", ...). Falls back to English, which is the only language the
        /// function descriptions in this plugin exist in anyway.
        /// </summary>
        private static string Lang3(SMath.Manager.SessionProfile profile)
        {
            try
            {
                if (profile != null && profile.CurrentLanguage != null)
                    return profile.CurrentLanguage.Abbr;
            }
            catch (System.Exception)
            {
            }
            return "eng";
        }

        private void RegisterFromProfile()
        {
            Register(Lang3(_profile));
        }

        private void Register(string lang3)
        {
            if (initialized)
                return;
            initialized = true;

            var info = new List<TermInfo>();

            try
            {
                // Every function class carries the arities it supports as
                // `public static int[] Arguments`; one class therefore registers
                // both CoolProp_Props(...) style forms if it wants to.
                foreach (var type in Assembly.GetExecutingAssembly().GetTypes())
                {
                    if (!(type.IsClass && typeof(IFunction).IsAssignableFrom(type)))
                        continue;

                    var arguments = type.GetField("Arguments",
                        BindingFlags.GetField | BindingFlags.Static | BindingFlags.Public);

                    if (arguments == null)
                        continue;

                    var args = arguments.GetValue(null) as int[];
                    if (args == null)
                        continue;

                    foreach (var arg in args)
                        functions.Add((IFunction)System.Activator.CreateInstance(type, new object[] { arg }));
                }

                foreach (var func in functions)
                {
                    var item = func.GetTermInfo(lang3);
                    info.Add(item);
                    LogInfo("[INFO]", "{0}({1}) - {2}", item.Text, item.ArgsCount, item.Description);
                }
            }
            catch (System.Exception ex)
            {
                // A reflection failure must leave the plugin loaded with
                // whatever registered, not dead.
                LogInfo("[ERROR]", "{0}", ex.Message);
            }

            termInfos = info.ToArray();
            LogInfo("[INFO]", "Successfully. {0} functions loaded.", termInfos.Length);
        }

        public bool TryEvaluateExpression(Entry value, SMath.Math.Store context, out Entry result)
        {
            result = null;

            if (value == null || value.Type != TermType.Function)
                return false;

            // Backstop: GetTermsHandled should have run before any dispatch, but
            // registration is idempotent so calling it again costs nothing and
            // makes the plugin's own path independent of the host's order.
            RegisterFromProfile();

            foreach (var func in functions)
            {
                if (!func.Info.Equals(value.ToTerm()))
                    continue;

                try
                {
                    return func.TryEvaluateExpression(value, context, out result);
                }
                catch (System.Exception ex)
                {
                    LogInfo("[ERROR]", "{0}({1}) {2}", value.Text, value.ArgsCount, ex.Message);
                    throw;
                }
            }

            return false;
        }
    }
}
