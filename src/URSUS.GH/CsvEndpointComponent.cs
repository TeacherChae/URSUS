using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using URSUS.Export;
using URSUS.Resources;

namespace URSUS.GH
{
    /// <summary>
    /// CSV 다운로드 API 엔드포인트를 관리하는 Grasshopper 컴포넌트.
    ///
    /// 로컬 HTTP 서버를 자동 시작하여 외부 도구가
    /// GET/POST 요청으로 분석 결과를 CSV로 다운로드할 수 있게 한다.
    ///
    /// 사용법:
    ///   1) Solver 출력(LC, N, A, V)을 연결
    ///   2) Enable을 True로 설정 (Toggle 연결 권장)
    ///   3) 브라우저에서 http://localhost:58720/api/csv/download 접속
    ///
    /// student_first: Toggle 하나만 연결하면 즉시 API 서버가 구동된다.
    /// </summary>
    public class CsvEndpointComponent : GH_Component
    {
        private CsvEndpointServer? _server;
        private string _statusMessage = ErrorMessages.CsvEndpoint.WaitingForEnable;
        private int _activePort = -1;

        public CsvEndpointComponent()
            : base(
                "URSUS CSV API",
                "CSV API",
                "분석 결과를 HTTP API로 제공합니다.\n" +
                "외부 도구에서 GET/POST 요청으로 CSV를 다운로드할 수 있습니다.\n" +
                "GET  /api/csv/download — CSV 다운로드\n" +
                "POST /api/csv/download — JSON → CSV 변환\n" +
                "GET  /api/csv/status   — 상태 확인",
                "URSUS",
                "Export")
        { }

        public override Guid ComponentGuid
            => new Guid("a1b2c3d4-e5f6-7890-abcd-ef1234567890");

        // ─────────────────────────────────────────────────────────────
        //  Parameters
        // ─────────────────────────────────────────────────────────────

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("Legal Codes", "LC",
                "법정동 코드 목록 (Solver의 LC 출력 연결)",
                GH_ParamAccess.list);

            pManager.AddTextParameter("Names", "N",
                "법정동 이름 목록 (Solver의 N 출력 연결)",
                GH_ParamAccess.list);

            pManager.AddNumberParameter("Areas", "A",
                "법정동 면적 목록 (Solver의 A 출력 연결)",
                GH_ParamAccess.list);

            pManager.AddNumberParameter("Values", "V",
                "오버레이 값 목록 (Solver의 V 출력 연결)",
                GH_ParamAccess.list);

            pManager.AddBooleanParameter("Enable", "E",
                "True로 설정 시 CSV API 서버를 시작합니다.\n" +
                "Toggle 컴포넌트를 연결하면 ON/OFF 전환이 가능합니다.",
                GH_ParamAccess.item, false);

            pManager.AddIntegerParameter("Port", "P",
                "HTTP 서버 포트 (기본: 58720).\n" +
                "포트 충돌 시 자동으로 다음 포트를 시도합니다.",
                GH_ParamAccess.item, CsvEndpointServer.DEFAULT_PORT);
            pManager[5].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("URL", "U",
                "CSV 다운로드 URL (브라우저에 붙여넣기 가능)",
                GH_ParamAccess.item);

            pManager.AddIntegerParameter("Port", "P",
                "실제 바인딩된 포트 번호",
                GH_ParamAccess.item);

            pManager.AddTextParameter("Status", "S",
                "서버 상태 메시지",
                GH_ParamAccess.item);
        }

        // ─────────────────────────────────────────────────────────────
        //  Solve
        // ─────────────────────────────────────────────────────────────

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // ── 입력 수집 ────────────────────────────────────────────
            var legalCodes = new List<string>();
            var names = new List<string>();
            var areas = new List<double>();
            var values = new List<double>();
            bool enable = false;
            int port = CsvEndpointServer.DEFAULT_PORT;

            if (!DA.GetDataList(0, legalCodes)) return;
            if (!DA.GetDataList(1, names)) return;
            if (!DA.GetDataList(2, areas)) return;
            if (!DA.GetDataList(3, values)) return;
            DA.GetData(4, ref enable);
            DA.GetData(5, ref port);

            // ── 서버 비활성화 요청 ──────────────────────────────────
            if (!enable)
            {
                StopServer();
                _statusMessage = ErrorMessages.CsvEndpoint.ServerStopped;
                OutputState(DA);
                return;
            }

            // ── 입력 검증 ───────────────────────────────────────────
            if (legalCodes.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    ErrorMessages.Data.LegalCodesEmpty);
                OutputState(DA);
                return;
            }

            // ── 서버 시작 (미시작 또는 포트 변경 시) ────────────────
            try
            {
                EnsureServerRunning(port);
            }
            catch (Exception ex)
            {
                _statusMessage = ErrorMessages.CsvEndpoint.ServerStartFailed(ex.Message);
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, _statusMessage);
                OutputState(DA);
                return;
            }

            // ── 데이터 업데이트 ─────────────────────────────────────
            try
            {
                _server!.UpdateData(legalCodes, names, areas, values);
                _statusMessage = ErrorMessages.CsvEndpoint.ServerRunning(
                    legalCodes.Count, _server.BaseUrl);
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    $"CSV API: {_server.BaseUrl}/api/csv/download");
            }
            catch (Exception ex)
            {
                _statusMessage = ErrorMessages.CsvEndpoint.DataUpdateFailed(ex.Message);
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, _statusMessage);
            }

            OutputState(DA);
        }

        // ─────────────────────────────────────────────────────────────
        //  Server Management
        // ─────────────────────────────────────────────────────────────

        private void EnsureServerRunning(int preferredPort)
        {
            // 이미 실행 중이고 같은 포트이면 유지
            if (_server?.IsRunning == true && _activePort == preferredPort)
                return;

            // 포트 변경 시 기존 서버 중지
            StopServer();

            _server = new CsvEndpointServer();
            _activePort = _server.Start(preferredPort);
        }

        private void StopServer()
        {
            if (_server != null)
            {
                _server.Dispose();
                _server = null;
                _activePort = -1;
            }
        }

        // ─────────────────────────────────────────────────────────────
        //  Output
        // ─────────────────────────────────────────────────────────────

        private void OutputState(IGH_DataAccess DA)
        {
            string url = _server?.IsRunning == true
                ? $"{_server.BaseUrl}/api/csv/download"
                : string.Empty;

            DA.SetData(0, url);
            DA.SetData(1, _server?.ActivePort ?? -1);
            DA.SetData(2, _statusMessage);
        }

        // ─────────────────────────────────────────────────────────────
        //  Cleanup
        // ─────────────────────────────────────────────────────────────

        public override void RemovedFromDocument(GH_Document document)
        {
            StopServer();
            base.RemovedFromDocument(document);
        }
    }
}
