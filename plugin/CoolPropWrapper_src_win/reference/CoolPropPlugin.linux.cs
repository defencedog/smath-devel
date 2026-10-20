using System.Collections.Generic;
using System.Reflection;
using SMath.Manager;

namespace coolprop_wrapper
{
    public class coolpropPlugin : SMath.Math.IPluginLowLevelEvaluationFast
    {
        AssemblyInfo[] asseblyInfos = new []
        {
            new AssemblyInfo("SMath Studio", new System.Version(0, 98), new System.Guid("a37cba83-b69c-4c71-9992-55ff666763bd"))
        };
        TermInfo[] termInfos = new TermInfo[] {};
        List<IFunction> functions = new List<IFunction>();

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
                    method.Name.Substring(method.Name.LastIndexOf('.')+1),
                    string.Format(Text, args),
                    System.Environment.NewLine),
                System.Text.Encoding.UTF8);
#endif
        }

        public static SMath.Math.Numeric.TNumber GetNumberParam(Entry arg, SMath.Math.Store context)
        {
            //      var arg1 = SMath.Math.Decision.Preprocessing(arg, ref context);
            //      return SMath.Math.Numeric.Expression.Calculate(arg1, context).obj as SMath.Math.Numeric.TDouble;
            return SMath.Math.Decision.NumericCalculation(arg, context);
        }

        public static void CoolPropError(string message = "")
        {
            string errStr;
            if (!Functions.CoolProp_get_global_param_string.CoolPropDLLfunc("errstring", out errStr))
            {
                // Call it second time - most probably will get something like "buffer too small"
                Functions.CoolProp_get_global_param_string.CoolPropDLLfunc("errstring", out errStr);
                errStr = "Error message couldn't be retrieved from CoolProp library (buffer size issue?); the second try returned: " + errStr;
            }
            throw new System.Exception(message + errStr);
        }

        private static readonly char[] StringTrimChars = new char[] {
            '"', '\'',
            (Symbols.StringChar != null && Symbols.StringChar.Length > 0) ? Symbols.StringChar[0] : '"'
        };

        public static string GetStringParam(Entry arg, SMath.Math.Store context)
        {
            var dbl = GetNumberParam(arg, context).obj as SMath.Math.Numeric.TDouble;
            if (!dbl.isText)
                throw new SMath.Manager.MathException(Errors.ArgumentMustBeString);
            // Bug fix (round 41, 8.0.0.4): under Mono the in-app TDouble string
            // value carries SMath's parser-round-trip escapes — '.' -> literal
            // "\002E", '&' -> "\0026" (witness: 8.0.0.3 log.txt showed a mixture
            // string reaching native as ETHANE[0\002E\5]\0026\METHANE..., so
            // CoolProp's strsplit('&') found no separator).  Upstream relies on
            // ToString(), which under Windows .NET yields the raw text.  Prefer
            // the raw Text field and ALWAYS decode residual \NNNN sequences.
            var raw = dbl.Text;
            if (string.IsNullOrEmpty(raw)) raw = dbl.ToString();
            return UnescapeMathString(raw.Trim(StringTrimChars));
        }

        // Decodes SMath's "\NNNN\" character escapes ("\002E\" -> '.'), where
        // NNNN is 4 hex digits and a trailing backslash DELIMITES the escape
        // (8.0.0.3 log.txt witness: "0\002E\5" means "0.5").  A backslash NOT
        // followed by four hex digits is literal; the decoder is idempotent on
        // clean strings (Windows .NET paths pass them through untouched).
        internal static string UnescapeMathString(string s)
        {
            if (s == null || s.IndexOf('\\') < 0) return s;
            var sb = new System.Text.StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++) {
                if (s[i] == '\\' && i + 4 < s.Length &&
                    System.Uri.IsHexDigit(s[i + 1]) && System.Uri.IsHexDigit(s[i + 2]) &&
                    System.Uri.IsHexDigit(s[i + 3]) && System.Uri.IsHexDigit(s[i + 4])) {
                    sb.Append((char)int.Parse(s.Substring(i + 1, 4),
                        System.Globalization.NumberStyles.HexNumber));
                    i += 4;
                    if (i + 1 < s.Length && s[i + 1] == '\\') i++;  // delimiter
                } else {
                    sb.Append(s[i]);
                }
            }
            return sb.ToString();
        }

        public static Entry MakeDoubleResult(double result, SMath.Math.Symbolic.MItem unit)
        {
            if (double.IsInfinity(result))
            {
                CoolPropError();
            }

            var d = new SMath.Math.Numeric.TDouble(result);
            d.Units = unit;
            return Entry.Create(d.ToTerms());
        }

        public static Entry MakeStringResult(string result)
        {
            return new Entry(Symbols.StringChar + result + Symbols.StringChar, TermType.Operand, new Entry[0]);
        }

        TermInfo[] IPluginHandleEvaluation.GetTermsHandled(SMath.Manager.SessionProfile sessionProfile)
        {
            // r37c (8.0.0.2): the host hands us the LIVE SessionProfile here (MathcadToolbox
            // stashes it the same way) — bind it BEFORE any unit evaluation can touch Unit;
            // a failed Unit static-ctor otherwise poisons the type for the process lifetime.
            Unit.Bind(sessionProfile);
            Register();
            return termInfos;
        }

        void System.IDisposable.Dispose()
        {
            try
            {
                if (System.IO.File.Exists(LogFile))
                    System.IO.File.Delete(LogFile);
            }
            catch (System.Exception) {}
        }

        // NOTE (1.75 adaptation, 2026-10-03): SMath.Manager.IPlugin in 1.75.9678 has
        // only Initialize(); the obsolete `Dependences` property was removed from the
        // interface, so the asseblyInfos field above is now unused (kept for shape).

        // 1.75 adaptation (2026-10-03): registration is made idempotent + lazy.
        // The plugin's OWN TryEvaluateExpression path (which SMath calls per
        // evaluation) now self-registers via Register() — proven safe against
        // double-registration and independent of whether Initialize() ran.
        private bool initialized = false;
        private void Register()
        {
            if (initialized) return;
            initialized = true;

            var info = new List<TermInfo>();

            try
            {
                foreach (var type in Assembly.GetExecutingAssembly().GetTypes()) {

                    if (!(type.IsClass && typeof(IFunction).IsAssignableFrom(type)))
                        continue;

                    var arguments = type.GetField("Arguments", BindingFlags.GetField | BindingFlags.Static | BindingFlags.Public);

                    if (arguments == null)
                        continue;

                    var args = arguments.GetValue(null) as int[];

                    foreach (var arg in args)
                        functions.Add((IFunction)System.Activator.CreateInstance(type, new object[] {arg}));
                }

                foreach (var func in functions)
                {
                    var item = func.GetTermInfo(GlobalParams.CurLang3Letter);
                    info.Add(item);
                    LogInfo("[INFO]", "{0}({1}) - {2}", item.Text, item.ArgsCount, item.Description);
                }
            }
            catch (System.Exception ex)
            {
                LogInfo("[ERROR]", "{0}", ex.Message);
            }

            termInfos = info.ToArray();
            LogInfo("[INFO]", "Successfully. {0} functions loaded.", termInfos.Length);
        }

        void SMath.Manager.IPlugin.Initialize() { Register(); }

        public bool TryEvaluateExpression(Entry value, SMath.Math.Store context, out Entry result)
        {
            result = null;

            if (value.Type != TermType.Function)
                return false;

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
