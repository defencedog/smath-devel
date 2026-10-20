/*
 * Assembly-level metadata for the SMath Studio plugin loader.
 *
 * SMath reads plugin identity through AssemblyInfo(Assembly):
 *   Title       <- AssemblyTitleAttribute
 *   Description <- AssemblyDescriptionAttribute
 *   Version     <- AssemblyVersion
 *   PublicGuid  <- GuidAttribute   (must match the install folder name)
 *
 * The GUID below is intentionally the same as the existing
 * "PsychroLib Wrapper" plugin (36a0265c-b529-4440-88e1-88f9226cee6a),
 * so this build is treated as an upgrade of that plugin rather than a
 * second, separate plugin.
 */

using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

[assembly: AssemblyTitle("PsychroLib Wrapper")]
[assembly: AssemblyDescription("A native SMath Studio wrapper for ASHRAE 2017 psychrometric properties of moist air in SI units")]
[assembly: AssemblyCompany("ASHRAE / PsychroLib / SMath Studio Wrapper")]
[assembly: AssemblyProduct("PsychroLib Wrapper")]
[assembly: AssemblyCopyright("PsychroLib is MIT licensed. ASHRAE equations per Handbook - Fundamentals (2017).")]
[assembly: AssemblyVersion("1.2.0.0")]
[assembly: AssemblyFileVersion("1.2.0.0")]
[assembly: ComVisible(false)]
[assembly: Guid("36a0265c-b529-4440-88e1-88f9226cee6a")]

// Test harness access (see _harness/). SMath.Manager cannot construct Term or
// ArgumentInfo outside a live session, so the unit tests drive the function
// objects directly instead of going through NumericEvaluation.
[assembly: InternalsVisibleTo("PsychroLibHarness")]
