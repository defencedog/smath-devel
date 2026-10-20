/*
 * Assembly-level metadata for the SMath Studio plugin loader.
 *
 * SMath reads plugin identity through AssemblyInfo(Assembly):
 *   Title       <- AssemblyTitleAttribute
 *   Description <- AssemblyDescriptionAttribute
 *   Version     <- AssemblyVersion
 *   PublicGuid  <- GuidAttribute   (must match the install folder name)
 *
 * The GUID below is intentionally the same as the existing "IF97 Wrapper"
 * plugin (bdb8d1d9-11bf-4f8b-aca4-c1fa3f6e13ad), so this build is treated as
 * an upgrade of that plugin rather than a second, separate plugin.
 */

using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

[assembly: AssemblyTitle("IF97 Wrapper")]
[assembly: AssemblyDescription("A native SMath Studio wrapper for IAPWS-IF97 steam and water properties in SI units")]
[assembly: AssemblyCompany("IAPWS / xhydongda / SMath Studio Wrapper")]
[assembly: AssemblyProduct("IF97 Wrapper")]
[assembly: AssemblyCopyright("IF97 C# port MIT licensed (c) 2019 Yideyun. IAPWS-IF97 equations per IAPWS R7-97.")]
[assembly: AssemblyVersion("1.1.1.0")]
[assembly: AssemblyFileVersion("1.1.1.0")]
[assembly: ComVisible(false)]
[assembly: Guid("bdb8d1d9-11bf-4f8b-aca4-c1fa3f6e13ad")]

// Test harness access (see _if97harness/). SMath.Manager cannot construct Term
// or ArgumentInfo outside a live session, so the unit tests drive the function
// table directly.
[assembly: InternalsVisibleTo("IF97Harness")]
