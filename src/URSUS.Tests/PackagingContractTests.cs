using URSUS.Config;
using URSUS.Setup;

namespace URSUS.Tests;

internal static class PackagingContractTests
{
    [Test]
    internal static void RuntimePayload_IsCompleteAndHasNoDuplicateEntries()
    {
        AssertEx.Equal(10, DeploymentContract.RequiredRuntimeFiles.Count);
        AssertEx.Equal(
            DeploymentContract.RequiredRuntimeFiles.Count,
            DeploymentContract.RequiredRuntimeFiles.Distinct(StringComparer.Ordinal).Count());
        AssertEx.False(DeploymentContract.RequiredRuntimeFiles is string[]);

        AssertEx.True(DeploymentContract.RequiredRuntimeFiles.Contains("URSUS.GH.gha"));
        AssertEx.True(DeploymentContract.RequiredRuntimeFiles.Contains("URSUS.dll"));
        AssertEx.True(DeploymentContract.RequiredRuntimeFiles.Contains("Clipper2Lib.dll"));
        AssertEx.True(DeploymentContract.RequiredRuntimeFiles.Contains("System.Drawing.Common.dll"));
        AssertEx.True(DeploymentContract.RequiredRuntimeFiles.Contains("Microsoft.Win32.SystemEvents.dll"));
        AssertEx.True(DeploymentContract.RequiredRuntimeFiles.Contains("URSUS.GH.deps.json"));
        AssertEx.True(DeploymentContract.RequiredRuntimeFiles.Contains("URSUS.GH.runtimeconfig.json"));
        AssertEx.True(DeploymentContract.RequiredRuntimeFiles.Contains("adstrd_legald_mapping.json"));
        AssertEx.True(DeploymentContract.RequiredRuntimeFiles.Contains(
            "runtimes/win/lib/net7.0/System.Drawing.Common.dll"));
        AssertEx.True(DeploymentContract.RequiredRuntimeFiles.Contains(
            "runtimes/win/lib/net7.0/Microsoft.Win32.SystemEvents.dll"));
    }

    [Test]
    internal static void StandaloneInstaller_FailsWhenARequiredPayloadFileIsMissing()
    {
        string sourceDir = CreateTempDirectory();
        string targetDir = CreateTempDirectory();

        try
        {
            string missingFile = DeploymentContract.RequiredRuntimeFiles[
                DeploymentContract.RequiredRuntimeFiles.Count - 1];
            foreach (string file in DeploymentContract.RequiredRuntimeFiles.Take(
                         DeploymentContract.RequiredRuntimeFiles.Count - 1))
                WritePayloadFile(sourceDir, file);

            var check = DependencyInstaller.CheckDependencies(sourceDir, targetDir);
            AssertEx.Equal(1, check.MissingRequired.Count);
            AssertEx.Equal(missingFile, check.MissingRequired[0]);

            var result = DependencyInstaller.InstallDependencies(sourceDir, targetDir);
            AssertEx.False(result.AllSucceeded);
            AssertEx.Equal(1, result.FailureCount);
            AssertEx.Equal(0, result.SkippedCount);
            AssertEx.True(result.Errors.Any(error =>
                error.Contains(missingFile, StringComparison.Ordinal)));
        }
        finally
        {
            Directory.Delete(sourceDir, recursive: true);
            Directory.Delete(targetDir, recursive: true);
        }
    }

    [Test]
    internal static void StandaloneInstaller_CopiesTheEntireRuntimePayload()
    {
        string sourceDir = CreateTempDirectory();
        string targetDir = CreateTempDirectory();

        try
        {
            foreach (string file in DeploymentContract.RequiredRuntimeFiles)
                WritePayloadFile(sourceDir, file);

            var result = DependencyInstaller.InstallDependencies(sourceDir, targetDir);

            AssertEx.True(result.AllSucceeded);
            AssertEx.Equal(DeploymentContract.RequiredRuntimeFiles.Count, result.SuccessCount);
            AssertEx.True(DeploymentContract.RequiredRuntimeFiles.All(file =>
                File.Exists(Path.Combine(targetDir, file))));
        }
        finally
        {
            Directory.Delete(sourceDir, recursive: true);
            Directory.Delete(targetDir, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), $"ursus-package-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void WritePayloadFile(string root, string relativePath)
    {
        string path = Path.Combine(root, relativePath);
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllText(path, relativePath);
    }
}
