using System;
using System.Drawing;
using Grasshopper;
using Grasshopper.Kernel;
using URSUS.Config;

namespace URSUS.GH
{
    public class URSUSInfo : GH_AssemblyInfo
    {
        public override string Name        => "URSUS";
        public override string Description => "Urban Research with Spatial Utility System";
        public override string Version     => "1.0.0";
        public override string AuthorName  => "TeacherChae";
        public override string AuthorContact => "https://github.com/TeacherChae/URSUS";

        public override Guid Id => new Guid("a049aecf-08a1-40ff-9203-e1e9bf4e9f53");

        public override Bitmap Icon => null;
    }

    /// <summary>
    /// Grasshopper 로드 시 자동 실행되는 어셈블리 우선순위 콜백.
    /// 설치 상태를 검증하고, 문제가 있으면 사용자에게 안내한다.
    ///
    /// 3-Step UX 검증:
    ///   Step 1: 핵심 파일(GHA, DLL) 존재 확인
    ///   Step 2: appsettings.json 유효성 확인
    ///   Step 3: API 키 로드 가능 여부 확인
    /// </summary>
    public sealed class URSUSLoadChecker : GH_AssemblyPriority
    {
        /// <summary>최근 검증 결과 (다른 컴포넌트에서 참조 가능)</summary>
        public static PostInstallVerifier.VerificationReport? LastReport { get; private set; }

        /// <summary>설치가 정상인지 여부</summary>
        public static bool IsHealthy => LastReport?.AllPassed ?? false;

        public override GH_LoadingInstruction PriorityLoad()
        {
            Instances.CanvasCreated += OnCanvasCreated;
            return GH_LoadingInstruction.Proceed;
        }

        private void OnCanvasCreated(Grasshopper.GUI.Canvas.GH_Canvas canvas)
        {
            // 이벤트 해제 — 한 번만 실행
            Instances.CanvasCreated -= OnCanvasCreated;

            try
            {
                LastReport = PostInstallVerifier.Verify();

                if (LastReport.ErrorCount > 0)
                {
                    // 설정 파일 템플릿 자동 생성 시도
                    PostInstallVerifier.CreateTemplateIfMissing();

                    System.Windows.Forms.MessageBox.Show(
                        LastReport.ToSummary(),
                        "URSUS 설치 검증",
                        System.Windows.Forms.MessageBoxButtons.OK,
                        System.Windows.Forms.MessageBoxIcon.Warning);
                }
                else if (LastReport.HasWarnings)
                {
                    // API 키 미설정 등 경미한 문제 → 콘솔 로그만
                    System.Diagnostics.Debug.WriteLine(LastReport.ToSummary());

                    // 설정 파일 템플릿이 없으면 자동 생성
                    PostInstallVerifier.CreateTemplateIfMissing();
                }
                // 모든 항목 정상 → 무음 통과
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[URSUS] 설치 검증 중 예외: {ex.Message}");
            }
        }
    }
}
