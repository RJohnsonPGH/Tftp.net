using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

[assembly: AssemblyTitle("Tftp.Net")]
[assembly: AssemblyDescription("A .NET# library that allows you to easily integrate a TFTP Client or TFTP Server in your application")]
[assembly: AssemblyProduct("Tftp.Net")]

[assembly: ComVisible(false)]

[assembly: AssemblyVersion("1.0.2")]
[assembly: AssemblyFileVersion("1.0.2")]
[assembly: InternalsVisibleTo("Tftp.Net.Tests")]

// Allows Castle's dynamic proxy generator (used by NSubstitute and other mocking frameworks)
// to mock the library's internal interfaces from test assemblies.
[assembly: InternalsVisibleTo("DynamicProxyGenAssembly2")]
