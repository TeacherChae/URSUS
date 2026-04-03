using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using URSUS.Config;

namespace URSUS.Setup
{
    /// <summary>
    /// URSUS 플러그인 파일을 Grasshopper Libraries 폴더에 설치한다.
    ///
    /// Phase 1: CheckDependencies — 소스/대상 디렉토리에서 누락 파일 검사
    /// Phase 2: InstallDependencies — 파일 복사 + Windows 차단 해제
    /// Phase 4: VerifyInstallation — PostInstallVerifier로 최종 검증
    /// </summary>
    public static class DependencyInstaller
    {
        // ── 배포 대상 파일 ──────────────────────────────────────────────
        private static readonly string[] REQUIRED_FILES = new[]
        {
            "URSUS.dll",
            "URSUS.GH.gha",
        };

        private static readonly string[] OPTIONAL_FILES = new[]
        {
            "Clipper2Lib.dll",
            "URSUS.deps.json",
            "URSUS.GH.deps.json",
            "URSUS.GH.runtimeconfig.json",
            "adstrd_legald_mapping.json",
        };

        // ── 결과 모델 ──────────────────────────────────────────────────

        /// <summary>의존성 검사 결과</summary>
        public sealed class DependencyCheck
        {
            public List<string> MissingRequired { get; } = new();
            public List<string> MissingOptional { get; } = new();
            public List<string> FoundFiles { get; } = new();

            public string ToSummary()
            {
                var lines = new List<string>();

                if (MissingRequired.Count > 0)
                {
                    lines.Add($"필수 파일 누락 ({MissingRequired.Count}개):");
                    foreach (var f in MissingRequired)
                        lines.Add($"  ✗ {f}");
                }

                if (MissingOptional.Count > 0)
                {
                    lines.Add($"선택 파일 누락 ({MissingOptional.Count}개):");
                    foreach (var f in MissingOptional)
                        lines.Add($"  ⚠ {f}");
                }

                if (FoundFiles.Count > 0)
                {
                    lines.Add($"확인된 파일 ({FoundFiles.Count}개):");
                    foreach (var f in FoundFiles)
                        lines.Add($"  ✓ {f}");
                }

                return string.Join(Environment.NewLine, lines);
            }
        }

        /// <summary>설치 실행 결과</summary>
        public sealed class InstallResult
        {
            public int SuccessCount { get; set; }
            public int FailureCount { get; set; }
            public int SkippedCount { get; set; }
            public List<string> Errors { get; } = new();
            public TimeSpan Elapsed { get; set; }

            public bool AllSucceeded => FailureCount == 0;
        }

        // ── Phase 1: 의존성 검사 ───────────────────────────────────────

        /// <summary>
        /// 소스 디렉토리에서 배포 대상 파일의 존재 여부를 검사한다.
        /// </summary>
        public static DependencyCheck CheckDependencies(string sourceDir, string targetDir)
        {
            var check = new DependencyCheck();

            foreach (var file in REQUIRED_FILES)
            {
                if (File.Exists(Path.Combine(sourceDir, file)))
                    check.FoundFiles.Add(file);
                else
                    check.MissingRequired.Add(file);
            }

            foreach (var file in OPTIONAL_FILES)
            {
                if (File.Exists(Path.Combine(sourceDir, file)))
                    check.FoundFiles.Add(file);
                else
                    check.MissingOptional.Add(file);
            }

            return check;
        }

        // ── Phase 2: 파일 복사 + 차단 해제 ─────────────────────────────

        /// <summary>
        /// 소스 디렉토리의 파일을 대상 디렉토리로 복사하고 Windows 차단을 해제한다.
        /// </summary>
        /// <param name="sourceDir">빌드 산출물 디렉토리</param>
        /// <param name="targetDir">Grasshopper Libraries 폴더</param>
        /// <param name="onProgress">진행률 콜백 (0-90 범위, 메시지)</param>
        public static InstallResult InstallDependencies(
            string sourceDir,
            string targetDir,
            Action<int, string>? onProgress = null)
        {
            var sw = Stopwatch.StartNew();
            var result = new InstallResult();

            // 대상 디렉토리 생성
            Directory.CreateDirectory(targetDir);

            var allFiles = REQUIRED_FILES.Concat(OPTIONAL_FILES).ToList();
            int total = allFiles.Count;

            for (int i = 0; i < total; i++)
            {
                string file = allFiles[i];
                string srcPath = Path.Combine(sourceDir, file);
                string dstPath = Path.Combine(targetDir, file);

                int progress = 10 + (int)(80.0 * i / total);
                onProgress?.Invoke(progress, $"설치 중: {file}");

                if (!File.Exists(srcPath))
                {
                    result.SkippedCount++;
                    continue;
                }

                try
                {
                    File.Copy(srcPath, dstPath, overwrite: true);
                    UnblockFile(dstPath);
                    result.SuccessCount++;
                }
                catch (Exception ex)
                {
                    result.FailureCount++;
                    result.Errors.Add($"{file}: {ex.Message}");
                }
            }

            sw.Stop();
            result.Elapsed = sw.Elapsed;
            return result;
        }

        // ── Phase 4: 설치 후 검증 ──────────────────────────────────────

        /// <summary>
        /// PostInstallVerifier를 사용하여 설치 결과를 검증한다.
        /// </summary>
        public static PostInstallVerifier.VerificationReport VerifyInstallation(string targetDir)
        {
            return PostInstallVerifier.Verify(targetDir);
        }

        // ── Windows 파일 차단 해제 ─────────────────────────────────────

        /// <summary>
        /// Windows Zone.Identifier ADS를 제거하여 파일 차단을 해제한다.
        /// </summary>
        private static void UnblockFile(string filePath)
        {
            try
            {
                string adsPath = filePath + ":Zone.Identifier";
                if (File.Exists(adsPath))
                {
                    File.Delete(adsPath);
                }
            }
            catch
            {
                // Zone.Identifier 삭제 실패 — 무시 (Linux/macOS에서는 해당 없음)
            }
        }
    }
}
