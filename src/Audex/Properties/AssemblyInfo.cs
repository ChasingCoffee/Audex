using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

// Lets tests exercise internal-only helper types/methods (e.g. InMemoryComStream,
// AudioPreviewHandler.ResolvePreLoadError) directly instead of via reflection.
[assembly: InternalsVisibleTo("Audex.Tests")]

[assembly: AssemblyTitle("Audex")]
[assembly: AssemblyDescription("Windows Shell Extension for Audio File Preview")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("")]
[assembly: AssemblyProduct("Audex")]
[assembly: AssemblyCopyright("Copyright © 2026")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]

// Default to NOT COM-visible. Only AudioPreviewHandler (explicitly marked [ComVisible(true)]
// in its own class declaration) needs to be reachable via COM/regasm — everything else in this
// assembly (renderers, config types, layout structs, etc.) is a plain managed implementation
// detail. Opt-in per type avoids sweeping unrelated public types into the exported type library
// (this is what caused RegAsm's "public struct contains non-public fields" warning for
// ControlBarRenderer.ControlBarLayout's auto-property backing fields).
[assembly: ComVisible(false)]

// TypeLib GUID for COM interop
[assembly: Guid("8b3c9a5e-4f2a-4d7c-8a1b-3e5f6d7c8a9b")]

[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
