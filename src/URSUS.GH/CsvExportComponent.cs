using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;
using Grasshopper.Kernel;
using URSUS.Export;
using URSUS.Resources;

namespace URSUS.GH
{
    /// <summary>
    /// PRD F-05: CSV 내보내기 Grasshopper 컴포넌트.
    ///
    /// 원클릭 내보내기 워크플로:
    ///   1) Export 입력을 True로 설정 (Button 연결 권장)
    ///   2) FilePath가 비어 있으면 SaveFileDialog가 자동 표시
    ///   3) CsvExporter로 직렬화 후 파일 저장
    ///   4) 저장 경로와 행 수를 출력 파라미터로 반환
    ///
    /// 학생 사용자 시나리오: Button 하나만 연결하면 바탕화면에 즉시 저장.
    /// </summary>
    public class CsvExportComponent : GH_Component
    {
        /// <summary>마지막으로 저장 성공한 파일 경로 (재실행 시 유지)</summary>
        private string _lastSavedPath = string.Empty;

        /// <summary>마지막 저장 행 수</summary>
        private int _lastRowCount;

        /// <summary>마지막 상태 메시지</summary>
        private string _lastStatus = "대기 중 — Export를 True로 설정하세요.";

        public CsvExportComponent()
            : base(
                "URSUS CSV Export",
                "Export",
                "분석 결과를 CSV 파일로 내보냅니다.\n" +
                "Button을 연결하면 원클릭 내보내기가 가능합니다.",
                "URSUS",
                "Export")
        { }

        public override Guid ComponentGuid
            => new Guid("e7f3a2c1-5d84-4b9e-a6f0-2c8d1e9b7a34");

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

            pManager.AddBooleanParameter("Export", "E",
                "True로 설정 시 CSV 내보내기를 실행합니다.\n" +
                "Button 컴포넌트를 연결하면 원클릭 내보내기가 가능합니다.",
                GH_ParamAccess.item, false);

            pManager.AddTextParameter("File Path", "FP",
                "저장할 파일 경로 (.csv).\n" +
                "비워 두면 SaveFileDialog가 표시되거나 바탕화면에 자동 저장됩니다.",
                GH_ParamAccess.item, string.Empty);
            pManager[5].Optional = true;

            pManager.AddBooleanParameter("Show Dialog", "SD",
                "True이면 FilePath가 비어 있을 때 SaveFileDialog를 표시합니다.\n" +
                "False이면 바탕화면에 자동 저장합니다. (기본: True)",
                GH_ParamAccess.item, true);
            pManager[6].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Saved Path", "SP",
                "마지막으로 저장된 파일의 전체 경로",
                GH_ParamAccess.item);

            pManager.AddIntegerParameter("Row Count", "RC",
                "저장된 데이터 행 수 (헤더 제외)",
                GH_ParamAccess.item);

            pManager.AddTextParameter("Status", "S",
                "내보내기 상태 메시지",
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
            bool export = false;
            string filePath = string.Empty;
            bool showDialog = true;

            if (!DA.GetDataList(0, legalCodes)) return;
            if (!DA.GetDataList(1, names)) return;
            if (!DA.GetDataList(2, areas)) return;
            if (!DA.GetDataList(3, values)) return;
            DA.GetData(4, ref export);
            DA.GetData(5, ref filePath);
            DA.GetData(6, ref showDialog);

            // ── Export가 False이면 이전 상태만 출력 ──────────────────
            if (!export)
            {
                OutputLastState(DA);
                return;
            }

            // ── 입력 데이터 검증 ─────────────────────────────────────
            if (legalCodes.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    ErrorGuideMap.FormatMessageWithGuide(
                        ErrorCodes.LegalCodesEmpty,
                        ErrorMessages.Data.LegalCodesEmpty));
                OutputLastState(DA);
                return;
            }

            if (legalCodes.Count != names.Count ||
                legalCodes.Count != areas.Count ||
                legalCodes.Count != values.Count)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    ErrorGuideMap.FormatMessageWithGuide(
                        ErrorCodes.InputListLengthMismatch,
                        ErrorMessages.Data.InputListLengthMismatchShort(
                            legalCodes.Count, names.Count, areas.Count, values.Count)));
                OutputLastState(DA);
                return;
            }

            // ── 파일 경로 결정 ───────────────────────────────────────
            string resolvedPath = ResolveFilePath(filePath, showDialog);
            if (string.IsNullOrWhiteSpace(resolvedPath))
            {
                _lastStatus = ErrorMessages.CsvExport.ExportCancelled;
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    ErrorGuideMap.FormatMessageWithGuide(
                        ErrorCodes.ExportCancelled, _lastStatus));
                OutputLastState(DA);
                return;
            }

            // ── CSV 직렬화 및 저장 ───────────────────────────────────
            try
            {
                string csv = CsvExporter.Serialize(legalCodes, names, areas, values);
                int rowCount = CsvExporter.WriteToFile(csv, resolvedPath);

                _lastSavedPath = resolvedPath;
                _lastRowCount = rowCount;
                _lastStatus = $"저장 완료: {rowCount}행 → {Path.GetFileName(resolvedPath)}";

                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, _lastStatus);
            }
            catch (UnauthorizedAccessException)
            {
                _lastStatus = ErrorMessages.CsvExport.FileAccessDenied(resolvedPath);
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    ErrorGuideMap.FormatMessageWithGuide(
                        ErrorCodes.FileAccessDenied, _lastStatus));
            }
            catch (IOException ioEx)
            {
                _lastStatus = ErrorMessages.CsvExport.FileSaveFailed(ioEx.Message);
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    ErrorGuideMap.FormatMessageWithGuide(
                        ErrorCodes.FileSaveFailed, _lastStatus));
            }
            catch (Exception ex)
            {
                _lastStatus = ErrorMessages.CsvExport.ExportFailed(ex.Message);
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    ErrorGuideMap.FormatMessageWithGuide(
                        ErrorCodes.Unknown, _lastStatus));
            }

            OutputLastState(DA);
        }

        // ─────────────────────────────────────────────────────────────
        //  File Path Resolution
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// 파일 경로를 결정한다:
        ///   1) 사용자가 명시적으로 경로를 입력한 경우 → 그대로 사용
        ///   2) showDialog=true → SaveFileDialog 표시
        ///   3) showDialog=false → 바탕화면 기본 경로 사용
        /// </summary>
        private string ResolveFilePath(string userPath, bool showDialog)
        {
            // 사용자가 명시적 경로를 지정한 경우
            if (!string.IsNullOrWhiteSpace(userPath))
            {
                return EnsureCsvExtension(userPath);
            }

            // SaveFileDialog 표시
            if (showDialog)
            {
                return ShowSaveFileDialog();
            }

            // 기본 경로 (바탕화면)
            return CsvExporter.GetDefaultFilePath();
        }

        /// <summary>
        /// Windows SaveFileDialog를 표시하고 선택된 경로를 반환한다.
        /// 사용자가 취소하면 빈 문자열을 반환한다.
        /// </summary>
        private string ShowSaveFileDialog()
        {
            string result = string.Empty;

            try
            {
                // Grasshopper는 UI 스레드에서 SolveInstance를 호출하므로
                // SaveFileDialog를 직접 사용할 수 있다.
                using var dialog = new SaveFileDialog
                {
                    Title = "URSUS — CSV 내보내기",
                    Filter = "CSV 파일 (*.csv)|*.csv|모든 파일 (*.*)|*.*",
                    DefaultExt = "csv",
                    FileName = $"URSUS_export_{DateTime.Now:yyyyMMdd_HHmm}.csv",
                    InitialDirectory = !string.IsNullOrEmpty(_lastSavedPath)
                        ? Path.GetDirectoryName(_lastSavedPath)
                        : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                    OverwritePrompt = true
                };

                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    result = dialog.FileName;
                }
            }
            catch (Exception ex)
            {
                // Dialog 표시 실패 시 (헤드리스 환경 등) 기본 경로로 폴백
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    ErrorMessages.CsvExport.DialogFallback(ex.Message));
                result = CsvExporter.GetDefaultFilePath();
            }

            return result;
        }

        /// <summary>.csv 확장자가 없으면 추가한다.</summary>
        private static string EnsureCsvExtension(string path)
        {
            if (!path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                return path + ".csv";
            return path;
        }

        // ─────────────────────────────────────────────────────────────
        //  Output Helper
        // ─────────────────────────────────────────────────────────────

        /// <summary>마지막 저장 상태를 출력 파라미터에 설정한다.</summary>
        private void OutputLastState(IGH_DataAccess DA)
        {
            DA.SetData(0, _lastSavedPath);
            DA.SetData(1, _lastRowCount);
            DA.SetData(2, _lastStatus);
        }

        // ─────────────────────────────────────────────────────────────
        //  Context Menu: 원클릭 내보내기 (우클릭 메뉴)
        // ─────────────────────────────────────────────────────────────

        public override void AppendAdditionalMenuItems(ToolStripDropDown menu)
        {
            base.AppendAdditionalMenuItems(menu);

            Menu_AppendSeparator(menu);
            Menu_AppendItem(menu, "바탕화면에 바로 내보내기", OnQuickExportClick,
                true, false);
            Menu_AppendItem(menu, "다른 이름으로 내보내기...", OnSaveAsClick,
                true, false);

            if (!string.IsNullOrEmpty(_lastSavedPath) && File.Exists(_lastSavedPath))
            {
                Menu_AppendItem(menu, $"마지막 파일 열기: {Path.GetFileName(_lastSavedPath)}",
                    OnOpenLastFileClick, true, false);
            }
        }

        private void OnQuickExportClick(object sender, EventArgs e)
        {
            PerformExportFromMenu(CsvExporter.GetDefaultFilePath());
        }

        private void OnSaveAsClick(object sender, EventArgs e)
        {
            string path = ShowSaveFileDialog();
            if (!string.IsNullOrWhiteSpace(path))
            {
                PerformExportFromMenu(path);
            }
        }

        private void OnOpenLastFileClick(object sender, EventArgs e)
        {
            try
            {
                if (File.Exists(_lastSavedPath))
                {
                    System.Diagnostics.Process.Start(
                        new System.Diagnostics.ProcessStartInfo(_lastSavedPath)
                        {
                            UseShellExecute = true
                        });
                }
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    ErrorMessages.CsvExport.CannotOpenFile(ex.Message));
            }
        }

        /// <summary>
        /// 컨텍스트 메뉴에서 내보내기를 실행한다.
        /// 현재 연결된 입력 데이터를 읽어 CSV로 저장한다.
        /// </summary>
        private void PerformExportFromMenu(string filePath)
        {
            try
            {
                // 입력 파라미터에서 데이터를 직접 읽기
                var legalCodes = CollectInputStrings(0);
                var names = CollectInputStrings(1);
                var areas = CollectInputDoubles(2);
                var values = CollectInputDoubles(3);

                if (legalCodes.Count == 0)
                {
                    MessageBox.Show(
                        ErrorMessages.CsvExport.NoDataToExport,
                        "URSUS CSV Export",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                string csv = CsvExporter.Serialize(legalCodes, names, areas, values);
                int rowCount = CsvExporter.WriteToFile(csv, filePath);

                _lastSavedPath = filePath;
                _lastRowCount = rowCount;
                _lastStatus = ErrorMessages.CsvExport.SaveCompleteShort(rowCount, Path.GetFileName(filePath));

                MessageBox.Show(
                    ErrorMessages.CsvExport.ExportComplete(filePath, rowCount),
                    "URSUS CSV Export",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);

                ExpireSolution(true);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ErrorGuideMap.FormatMessageWithGuide(
                        ErrorCodes.Unknown,
                        ErrorMessages.CsvExport.ExportFailed(ex.Message)),
                    "URSUS CSV Export",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        // ─────────────────────────────────────────────────────────────
        //  Input Data Collection Helpers (for context menu export)
        // ─────────────────────────────────────────────────────────────

        private List<string> CollectInputStrings(int paramIndex)
        {
            var result = new List<string>();
            var param = Params.Input[paramIndex];
            foreach (var source in param.VolatileData.AllData(true))
            {
                if (source != null)
                    result.Add(source.ToString());
            }
            return result;
        }

        private List<double> CollectInputDoubles(int paramIndex)
        {
            var result = new List<double>();
            var param = Params.Input[paramIndex];
            foreach (var source in param.VolatileData.AllData(true))
            {
                if (source != null && double.TryParse(source.ToString(),
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out double val))
                {
                    result.Add(val);
                }
            }
            return result;
        }
    }
}
