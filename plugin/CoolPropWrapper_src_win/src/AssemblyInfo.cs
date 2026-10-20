using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

[assembly: AssemblyTitle("CoolProp Wrapper")]
[assembly: AssemblyDescription("SMath Studio native wrapper for the CoolProp thermodynamic and transport property library")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("CoolProp team")]
[assembly: AssemblyProduct("CoolPropWrapper")]
[assembly: AssemblyCopyright("CoolProp is (c) Ian Bell and the CoolProp team, MIT licensed")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]

// The GUID determines the plugin's folder under
// %APPDATA%\SMath\extensions\plugins\, so it must stay stable for the plugin
// to be recognised as an update of the existing CoolProp Wrapper installation
// rather than a second, parallel plugin.
[assembly: Guid("ca92ef03-c7da-4888-98ad-528482733e2f")]

[assembly: AssemblyVersion("8.0.0.5")]
[assembly: AssemblyFileVersion("8.0.0.5")]

[assembly: ComVisible(false)]
[assembly: AssemblyDelaySign(false)]

// The test harness exercises the engine layer and the unit table, which are
// internal by design (SMath only needs the public plugin class). Both
// assemblies are unsigned, so the simple name is enough.
[assembly: InternalsVisibleTo("CoolPropHHarness")]
