using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using URSUS.Config;

namespace URSUS.Setup
{
    /// <summary>
    /// 설치 시 appsettings.json 설정 파일을 생성한다.
    ///
    /// 두 곳에 저장:
    ///   1. 설치 디렉토리 (DLL 인접) — Grasshopper 로드 시 우선 참조
    ///   2. 사용자 프로필 (%APPDATA%/URSUS) — 백업 + 재설치 시 유지
    /// </summary>
    public static class SetupConfigGenerator
    {
        private const string SETTINGS_FILENAME = "appsettings.json";

        /// <summary>설정 파일 생성 결과</summary>
        public sealed class ConfigGenerationResult
        {
            /// <summary>설치 디렉토리에 생성된 파일 경로 (실패 시 null)</summary>
            public string? InstallDirPath { get; set; }

            /// <summary>사용자 프로필에 생성된 파일 경로 (실패 시 null)</summary>
            public string? UserProfilePath { get; set; }

            /// <summary>하나라도 파일이 생성되었는지</summary>
            public bool AnyFileCreated => InstallDirPath != null || UserProfilePath != null;

            public List<string> Errors { get; } = new();
        }

        /// <summary>
        /// API 키를 포함한 appsettings.json을 설치 디렉토리와 사용자 프로필에 생성한다.
        /// 기존 파일이 있으면 키를 병합한다.
        /// </summary>
        /// <param name="keys">API 키 딕셔너리 (ApiKeyProvider.KEY_* 상수를 키로 사용)</param>
        /// <param name="installDir">설치 디렉토리 경로</param>
        public static ConfigGenerationResult GenerateConfig(
            Dictionary<string, string> keys,
            string installDir)
        {
            var result = new ConfigGenerationResult();

            // 1. 설치 디렉토리에 저장
            string installPath = Path.Combine(installDir, SETTINGS_FILENAME);
            try
            {
                SaveSettingsFile(keys, installPath);
                result.InstallDirPath = installPath;
            }
            catch (Exception ex)
            {
                result.Errors.Add($"설치 디렉토리 저장 실패: {ex.Message}");
            }

            // 2. 사용자 프로필에 백업 저장
            try
            {
                string userDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "URSUS");
                Directory.CreateDirectory(userDir);

                string userPath = Path.Combine(userDir, SETTINGS_FILENAME);
                SaveSettingsFile(keys, userPath);
                result.UserProfilePath = userPath;
            }
            catch (Exception ex)
            {
                result.Errors.Add($"사용자 프로필 저장 실패: {ex.Message}");
            }

            return result;
        }

        private static void SaveSettingsFile(Dictionary<string, string> keys, string path)
        {
            // 기존 파일이 있으면 로드하여 병합
            UrsusSettings settings;
            if (File.Exists(path))
            {
                try
                {
                    string existing = File.ReadAllText(path, System.Text.Encoding.UTF8);
                    settings = JsonSerializer.Deserialize<UrsusSettings>(existing) ?? new UrsusSettings();
                }
                catch
                {
                    settings = new UrsusSettings();
                }
            }
            else
            {
                settings = new UrsusSettings();
            }

            // 키 병합 (빈 문자열이 아닌 값만 덮어씀)
            if (keys.TryGetValue(ApiKeyProvider.KEY_VWORLD, out var vw) && !string.IsNullOrEmpty(vw))
                settings.VWorldKey = vw;

            if (keys.TryGetValue(ApiKeyProvider.KEY_SEOUL, out var sk) && !string.IsNullOrEmpty(sk))
                settings.SeoulKey = sk;

            if (keys.TryGetValue(ApiKeyProvider.KEY_DATA_GO_KR, out var dg) && !string.IsNullOrEmpty(dg))
                settings.DataGoKrKey = dg;

            // 저장
            string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
            {
                WriteIndented = true,
            });

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, json, System.Text.Encoding.UTF8);
        }
    }
}
