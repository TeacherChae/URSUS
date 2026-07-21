using System.Reflection;
using URSUS.Config;
using URSUS.Resources;

namespace URSUS.Tests;

internal static class SupportContractTests
{
    private const string RepositoryDocsUrl =
        "https://github.com/TeacherChae/URSUS/blob/main/docs/";

    [Test]
    internal static void ErrorGuidesPointToTrackedDocumentsAndExistingAnchors()
    {
        foreach (var entry in ErrorGuideMap.Entries.Values)
        {
            AssertEx.False(entry.Url.Contains("/wiki", StringComparison.OrdinalIgnoreCase));
            AssertEx.True(entry.Url.StartsWith(RepositoryDocsUrl, StringComparison.Ordinal),
                $"Guide must be version-controlled: {entry.Url}");

            var uri = new Uri(entry.Url);
            string relative = uri.AbsolutePath.Split("/blob/main/", StringSplitOptions.None)[1];
            string path = RepositoryPath(relative);
            AssertEx.True(File.Exists(path), $"Missing guide document: {relative}");
            if (uri.Fragment.Length > 1)
            {
                string anchor = Uri.UnescapeDataString(uri.Fragment[1..]);
                string markdown = File.ReadAllText(path);
                AssertEx.True(markdown.Contains($"## {anchor}", StringComparison.OrdinalIgnoreCase),
                    $"Missing guide anchor #{anchor} in {relative}");
            }
        }
    }

    [Test]
    internal static void CurrentVersionAndLicenseStayAligned()
    {
        string props = File.ReadAllText(RepositoryPath("Directory.Build.props"));
        string inno = File.ReadAllText(RepositoryPath("installer/URSUS.iss"));
        string ghInfo = File.ReadAllText(RepositoryPath("src/URSUS.GH/URSUSInfo.cs"));
        string readme = File.ReadAllText(RepositoryPath("README.md"));
        string buildGuide = File.ReadAllText(RepositoryPath("DLL2GHA.md"));

        AssertEx.True(props.Contains("<Version>0.3.0</Version>", StringComparison.Ordinal));
        AssertEx.True(props.Contains("<AssemblyVersion>0.3.0.0</AssemblyVersion>", StringComparison.Ordinal));
        AssertEx.True(inno.Contains("#define MyAppVersion   \"0.3.0\"", StringComparison.Ordinal));
        AssertEx.True(inno.Contains("LicenseFile=..\\LICENSE", StringComparison.Ordinal));
        AssertEx.False(ghInfo.Contains("1.0.0", StringComparison.Ordinal));
        AssertEx.True(readme.Contains("URSUS 0.3.0", StringComparison.Ordinal));
        AssertEx.False(buildGuide.Contains("1.0.0", StringComparison.Ordinal));
        AssertEx.False(buildGuide.Contains("bin/Release", StringComparison.Ordinal));
        foreach (string file in DeploymentContract.RequiredRuntimeFiles)
            AssertEx.True(buildGuide.Contains($"bin/dist/{file}", StringComparison.Ordinal),
                $"Build guide omits package payload file {file}");
        AssertEx.True(File.Exists(RepositoryPath("LICENSE")));

        var version = typeof(URSUS.URSUSSolver).Assembly.GetName().Version;
        AssertEx.Equal(new Version(0, 3, 0, 0), version);
    }

    [Test]
    internal static void TransitDocumentationDescribesLatestClosedMonthStreaming()
    {
        string transit = File.ReadAllText(RepositoryPath("docs/data/transit_boarding.md"));
        AssertEx.True(transit.Contains("페이지 단위 스트리밍", StringComparison.Ordinal));
        AssertEx.True(transit.Contains("최근 완결 월", StringComparison.Ordinal));
        AssertEx.True(transit.Contains("달력 월", StringComparison.Ordinal));
        AssertEx.True(transit.Contains("28일", StringComparison.Ordinal));
        AssertEx.True(transit.Contains("모든 관측일", StringComparison.Ordinal));
        AssertEx.True(transit.Contains("95%", StringComparison.Ordinal));
        AssertEx.False(transit.Contains("전기간 단순 평균", StringComparison.Ordinal));
    }

    [Test]
    internal static void ApiKeyDocumentationMatchesRuntimePrecedenceAndNames()
    {
        string docs = File.ReadAllText(RepositoryPath("docs/api-keys.md"));
        string[] precedence =
        {
            "명시적 입력", "환경 변수", "DLL 인접 `.env`", "%APPDATA%\\URSUS\\.env",
            "DLL 인접 `appsettings.json`", "%APPDATA%\\URSUS\\appsettings.json",
        };
        int previous = -1;
        foreach (string item in precedence)
        {
            int current = docs.IndexOf(item, StringComparison.Ordinal);
            AssertEx.True(current > previous, $"API key precedence drift near {item}");
            previous = current;
        }
        AssertEx.True(docs.Contains("`SeoulKey`", StringComparison.Ordinal));
        AssertEx.False(docs.Contains("SeoulOpenDataKey", StringComparison.Ordinal));
    }

    private static string RepositoryPath(string relative)
    {
        string current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            string candidate = Path.Combine(current,
                relative.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate)) return candidate;
            DirectoryInfo? parent = Directory.GetParent(current);
            if (parent == null) break;
            current = parent.FullName;
        }
        throw new FileNotFoundException(relative);
    }
}
