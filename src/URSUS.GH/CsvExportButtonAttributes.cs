using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using URSUS.Export;
using URSUS.Resources;

namespace URSUS.GH
{
    /// <summary>
    /// Solver/CsvExport 컴포넌트 하단에 원클릭 CSV 내보내기 버튼을 렌더링하는
    /// 커스텀 GH_ComponentAttributes.
    ///
    /// student_first: 별도의 Button 컴포넌트를 연결할 필요 없이
    /// 컴포넌트 자체에 표시되는 버튼을 클릭하면 바탕화면에 즉시 CSV가 저장된다.
    ///
    /// 버튼 상태:
    ///   - 비활성(회색): 분석 결과가 없을 때
    ///   - 활성(파란색): 데이터가 준비되어 내보내기 가능할 때
    ///   - 성공(녹색):  저장 직후 잠시 표시
    /// </summary>
    public sealed class CsvExportButtonAttributes : GH_ComponentAttributes
    {
        // ─────────────────────────────────────────────────────────────
        //  Constants
        // ─────────────────────────────────────────────────────────────

        /// <summary>버튼 높이 (px)</summary>
        private const int BUTTON_HEIGHT = 22;

        /// <summary>버튼 좌우 마진 (px)</summary>
        private const int BUTTON_MARGIN_X = 3;

        /// <summary>컴포넌트 본체와 버튼 사이 간격 (px)</summary>
        private const int BUTTON_GAP = 3;

        /// <summary>버튼 라운드 반경</summary>
        private const int CORNER_RADIUS = 3;

        // ── 색상 정의 ────────────────────────────────────────────────
        private static readonly Color ColorActive   = Color.FromArgb(230, 56, 136, 216);
        private static readonly Color ColorActiveHover = Color.FromArgb(255, 42, 120, 200);
        private static readonly Color ColorDisabled = Color.FromArgb(180, 160, 160, 160);
        private static readonly Color ColorSuccess  = Color.FromArgb(230, 46, 160, 67);
        private static readonly Color ColorText     = Color.White;
        private static readonly Color ColorBorder   = Color.FromArgb(100, 0, 0, 0);

        // ─────────────────────────────────────────────────────────────
        //  State
        // ─────────────────────────────────────────────────────────────

        private RectangleF _buttonBounds;
        private bool _isHovering;

        /// <summary>성공 표시 플래그 (잠시 동안 녹색으로 표시)</summary>
        private DateTime _successUntil = DateTime.MinValue;

        /// <summary>마지막 저장 경로</summary>
        private string _lastSavedPath = string.Empty;

        /// <summary>마지막 저장 행 수</summary>
        private int _lastRowCount;

        // ─────────────────────────────────────────────────────────────
        //  Data Provider Interface
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// 호스트 컴포넌트가 구현해야 하는 데이터 제공 인터페이스.
        /// 컴포넌트의 현재 분석 결과에 접근하기 위해 사용한다.
        /// </summary>
        public interface ICsvExportDataProvider
        {
            /// <summary>내보낼 데이터가 준비되었는지 여부</summary>
            bool HasExportData { get; }

            /// <summary>현재 데이터를 CSV 문자열로 직렬화한다.</summary>
            string SerializeCsv();
        }

        private readonly ICsvExportDataProvider? _dataProvider;

        // ─────────────────────────────────────────────────────────────
        //  Constructor
        // ─────────────────────────────────────────────────────────────

        public CsvExportButtonAttributes(GH_Component owner)
            : base(owner)
        {
            _dataProvider = owner as ICsvExportDataProvider;
        }

        // ─────────────────────────────────────────────────────────────
        //  Layout
        // ─────────────────────────────────────────────────────────────

        protected override void Layout()
        {
            base.Layout();

            // 기본 레이아웃 아래에 버튼 영역 추가
            var baseBounds = Bounds;

            _buttonBounds = new RectangleF(
                baseBounds.Left + BUTTON_MARGIN_X,
                baseBounds.Bottom + BUTTON_GAP,
                baseBounds.Width - BUTTON_MARGIN_X * 2,
                BUTTON_HEIGHT);

            // 전체 Bounds를 버튼 영역까지 확장
            Bounds = new RectangleF(
                baseBounds.X,
                baseBounds.Y,
                baseBounds.Width,
                baseBounds.Height + BUTTON_GAP + BUTTON_HEIGHT);
        }

        // ─────────────────────────────────────────────────────────────
        //  Render
        // ─────────────────────────────────────────────────────────────

        protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
        {
            base.Render(canvas, graphics, channel);

            if (channel != GH_CanvasChannel.Objects)
                return;

            // ── 버튼 상태 결정 ──────────────────────────────────────
            bool hasData = _dataProvider?.HasExportData ?? false;
            bool isSuccess = DateTime.Now < _successUntil;

            Color bgColor;
            string label;

            if (isSuccess)
            {
                bgColor = ColorSuccess;
                label = $"CSV 저장 완료 ({_lastRowCount}행)";
            }
            else if (hasData)
            {
                bgColor = _isHovering ? ColorActiveHover : ColorActive;
                label = "CSV 내보내기";
            }
            else
            {
                bgColor = ColorDisabled;
                label = "CSV (데이터 없음)";
            }

            // ── 버튼 배경 ───────────────────────────────────────────
            var buttonRect = GH_Convert.ToRectangle(_buttonBounds);
            using var path = CreateRoundedRectPath(buttonRect, CORNER_RADIUS);
            using var brush = new SolidBrush(bgColor);
            graphics.FillPath(brush, path);

            // ── 버튼 테두리 ─────────────────────────────────────────
            using var borderPen = new Pen(ColorBorder, 1f);
            graphics.DrawPath(borderPen, path);

            // ── 아이콘 + 텍스트 ─────────────────────────────────────
            using var font = GH_FontServer.NewFont(
                GH_FontServer.StandardAdjusted, 7f, FontStyle.Bold);
            using var textBrush = new SolidBrush(ColorText);
            using var sf = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter
            };

            // 다운로드 화살표 아이콘 추가
            string displayText = hasData || isSuccess ? $"\u2913 {label}" : label;
            graphics.DrawString(displayText, font, textBrush, _buttonBounds, sf);
        }

        // ─────────────────────────────────────────────────────────────
        //  Mouse Interaction
        // ─────────────────────────────────────────────────────────────

        public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            if (e.Button == MouseButtons.Left && _buttonBounds.Contains(e.CanvasLocation))
            {
                bool hasData = _dataProvider?.HasExportData ?? false;
                if (hasData)
                {
                    PerformOneClickExport();
                    return GH_ObjectResponse.Handled;
                }
            }

            return base.RespondToMouseDown(sender, e);
        }

        public override GH_ObjectResponse RespondToMouseMove(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            bool wasHovering = _isHovering;
            _isHovering = _buttonBounds.Contains(e.CanvasLocation);

            if (wasHovering != _isHovering)
                sender.Invalidate();

            if (_isHovering)
                return GH_ObjectResponse.Capture;

            return base.RespondToMouseMove(sender, e);
        }

        // ─────────────────────────────────────────────────────────────
        //  Export Logic
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// 원클릭 내보내기를 실행한다.
        /// 바탕화면에 타임스탬프 파일명으로 즉시 저장한다.
        /// </summary>
        private void PerformOneClickExport()
        {
            if (_dataProvider == null)
                return;

            try
            {
                string csv = _dataProvider.SerializeCsv();
                if (string.IsNullOrEmpty(csv))
                {
                    Owner.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                        ErrorMessages.CsvExport.NoDataToExport);
                    return;
                }

                string filePath = CsvExporter.GetDefaultFilePath();
                int rowCount = CsvExporter.WriteToFile(csv, filePath);

                _lastSavedPath = filePath;
                _lastRowCount = rowCount;
                _successUntil = DateTime.Now.AddSeconds(3);

                Owner.AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    ErrorMessages.CsvExport.SaveComplete(rowCount, Path.GetFileName(filePath)));

                // 캔버스를 다시 그려 성공 상태 표시
                Grasshopper.Instances.ActiveCanvas?.Invalidate();

                // 3초 후 캔버스 재렌더링 (성공 표시 제거)
                System.Threading.Tasks.Task.Delay(3200).ContinueWith(_ =>
                {
                    try { Grasshopper.Instances.ActiveCanvas?.Invalidate(); }
                    catch { /* UI 스레드 접근 실패 무시 */ }
                });
            }
            catch (UnauthorizedAccessException)
            {
                Owner.AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    ErrorMessages.CsvExport.AccessDenied(CsvExporter.GetDefaultFilePath()));
            }
            catch (IOException ioEx)
            {
                Owner.AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    ErrorMessages.CsvExport.WriteFailed(ioEx.Message));
            }
            catch (Exception ex)
            {
                Owner.AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    ErrorMessages.CsvExport.ExportError(ex.Message));
            }
        }

        /// <summary>
        /// 컨텍스트 메뉴에서 호출하는 원클릭 내보내기.
        /// 내부 PerformOneClickExport와 동일하지만 완료 시 MessageBox를 표시한다.
        /// </summary>
        public void PerformOneClickExport_FromMenu(GH_Component owner)
        {
            if (_dataProvider == null || !_dataProvider.HasExportData)
            {
                MessageBox.Show(
                    ErrorMessages.CsvExport.NoDataToExport,
                    "URSUS CSV Export",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            try
            {
                string csv = _dataProvider.SerializeCsv();
                string filePath = CsvExporter.GetDefaultFilePath();
                int rowCount = CsvExporter.WriteToFile(csv, filePath);

                _lastSavedPath = filePath;
                _lastRowCount = rowCount;
                _successUntil = DateTime.Now.AddSeconds(3);

                MessageBox.Show(
                    ErrorMessages.CsvExport.ExportComplete(filePath, rowCount),
                    "URSUS CSV Export",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);

                Grasshopper.Instances.ActiveCanvas?.Invalidate();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ErrorMessages.CsvExport.ExportFailed(ex.Message),
                    "URSUS CSV Export",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// "다른 이름으로 내보내기" — SaveFileDialog를 표시한다.
        /// 컨텍스트 메뉴에서 호출된다.
        /// </summary>
        public void PerformSaveAsExport()
        {
            if (_dataProvider == null || !_dataProvider.HasExportData)
            {
                MessageBox.Show(
                    ErrorMessages.CsvExport.NoDataToExport,
                    "URSUS CSV Export",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            try
            {
                string? filePath = ShowSaveFileDialog();
                if (string.IsNullOrWhiteSpace(filePath))
                    return;

                string csv = _dataProvider.SerializeCsv();
                int rowCount = CsvExporter.WriteToFile(csv, filePath);

                _lastSavedPath = filePath;
                _lastRowCount = rowCount;
                _successUntil = DateTime.Now.AddSeconds(3);

                MessageBox.Show(
                    ErrorMessages.CsvExport.ExportComplete(filePath, rowCount),
                    "URSUS CSV Export",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);

                Grasshopper.Instances.ActiveCanvas?.Invalidate();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ErrorMessages.CsvExport.ExportFailed(ex.Message),
                    "URSUS CSV Export",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        /// <summary>마지막 저장 파일을 기본 프로그램으로 연다.</summary>
        public void OpenLastSavedFile()
        {
            if (string.IsNullOrEmpty(_lastSavedPath) || !File.Exists(_lastSavedPath))
                return;

            try
            {
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(_lastSavedPath)
                    { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Owner.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    ErrorMessages.CsvExport.CannotOpenFile(ex.Message));
            }
        }

        /// <summary>마지막 저장 경로가 유효한지 확인한다.</summary>
        public bool HasLastSavedFile
            => !string.IsNullOrEmpty(_lastSavedPath) && File.Exists(_lastSavedPath);

        /// <summary>마지막 저장 파일 이름</summary>
        public string LastSavedFileName
            => string.IsNullOrEmpty(_lastSavedPath) ? string.Empty : Path.GetFileName(_lastSavedPath);

        // ─────────────────────────────────────────────────────────────
        //  Dialog Helper
        // ─────────────────────────────────────────────────────────────

        private string? ShowSaveFileDialog()
        {
            using var dialog = new SaveFileDialog
            {
                Title = "URSUS -- CSV 내보내기",
                Filter = "CSV 파일 (*.csv)|*.csv|모든 파일 (*.*)|*.*",
                DefaultExt = "csv",
                FileName = $"URSUS_export_{DateTime.Now:yyyyMMdd_HHmm}.csv",
                InitialDirectory = !string.IsNullOrEmpty(_lastSavedPath)
                    ? Path.GetDirectoryName(_lastSavedPath)
                    : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                OverwritePrompt = true
            };

            return dialog.ShowDialog() == DialogResult.OK ? dialog.FileName : null;
        }

        // ─────────────────────────────────────────────────────────────
        //  Drawing Helper
        // ─────────────────────────────────────────────────────────────

        private static GraphicsPath CreateRoundedRectPath(Rectangle rect, int radius)
        {
            var path = new GraphicsPath();
            int d = radius * 2;

            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();

            return path;
        }
    }
}
