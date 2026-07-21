using System;
using System.Collections.Generic;

namespace URSUS.Config
{
    /// <summary>
    /// Files that must be present together for a usable Grasshopper installation.
    /// Keep this list aligned with installer/package-manifest.json; the packaging
    /// contract verifier enforces that alignment during CI.
    /// URSUS.deps.json is intentionally excluded: URSUS.GH.deps.json is the host
    /// entry-point manifest and already declares URSUS and Clipper2 dependencies.
    /// </summary>
    public static class DeploymentContract
    {
        public static IReadOnlyList<string> RequiredRuntimeFiles { get; } = Array.AsReadOnly(
            new[]
            {
                "URSUS.GH.gha",
                "URSUS.dll",
                "Clipper2Lib.dll",
                "System.Drawing.Common.dll",
                "Microsoft.Win32.SystemEvents.dll",
                "URSUS.GH.deps.json",
                "URSUS.GH.runtimeconfig.json",
                "adstrd_legald_mapping.json",
                "runtimes/win/lib/net7.0/System.Drawing.Common.dll",
                "runtimes/win/lib/net7.0/Microsoft.Win32.SystemEvents.dll",
            });
    }
}
