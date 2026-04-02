using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using URSUS.Resources;

namespace URSUS.Export
{
    /// <summary>
    /// 분석 결과 데이터를 CSV 문자열로 직렬화하는 서비스.
    /// PRD F-05 (결과 데이터 CSV 내보내기)의 핵심 직렬화 레이어.
    ///
    /// 설계 원칙:
    /// - Grasshopper / RhinoCommon 의존성 없음 → 단위 테스트 용이
    /// - 컬럼 정의를 CsvColumnDef 리스트로 관리 → 새 데이터셋 추가 시 기존 코드 변경 불필요
    /// - UTF-8 BOM 포함 → 한글 Excel에서 깨짐 없이 열림
    /// </summary>
    public static class CsvExporter
    {
        /// <summary>기본 CSV 헤더/값 구분자</summary>
        private const char DELIMITER = ',';

        /// <summary>줄바꿈 문자 (RFC 4180: CRLF)</summary>
        private const string NEWLINE = "\r\n";

        // ─────────────────────────────────────────────────────────────────
        //  Public API
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// SolverResult의 핵심 필드를 CSV 문자열로 변환한다.
        /// PRD 형식: 법정동코드,법정동명,면적(㎡),오버레이값
        /// </summary>
        /// <param name="legalCodes">법정동 코드 목록</param>
        /// <param name="names">법정동 이름 목록</param>
        /// <param name="areas">면적 목록 (㎡)</param>
        /// <param name="values">오버레이 값 목록 (0~1 정규화)</param>
        /// <param name="extraColumns">추가 컬럼 정의 (확장용). null이면 기본 4컬럼만 출력</param>
        /// <returns>UTF-8 CSV 문자열 (BOM 미포함 — BOM은 파일 쓰기 시 Encoding이 처리)</returns>
        public static string Serialize(
            IReadOnlyList<string> legalCodes,
            IReadOnlyList<string> names,
            IReadOnlyList<double> areas,
            IReadOnlyList<double> values,
            IReadOnlyList<CsvColumnDef>? extraColumns = null)
        {
            ValidateInputs(legalCodes, names, areas, values);

            int rowCount = legalCodes.Count;
            var columns = BuildColumnDefs(legalCodes, names, areas, values, extraColumns);

            var sb = new StringBuilder();

            // ── 헤더 행 ──────────────────────────────────────────────────
            sb.Append(string.Join(DELIMITER, columns.Select(c => Escape(c.Header))));
            sb.Append(NEWLINE);

            // ── 데이터 행 ────────────────────────────────────────────────
            for (int i = 0; i < rowCount; i++)
            {
                sb.Append(string.Join(DELIMITER, columns.Select(c => Escape(c.GetValue(i)))));
                sb.Append(NEWLINE);
            }

            return sb.ToString();
        }

        /// <summary>
        /// CSV 문자열을 파일로 저장한다. UTF-8 BOM 포함.
        /// </summary>
        /// <param name="csv">Serialize()가 반환한 CSV 문자열</param>
        /// <param name="filePath">저장 경로</param>
        /// <returns>저장된 행 수 (헤더 제외)</returns>
        public static int WriteToFile(string csv, string filePath)
        {
            if (string.IsNullOrEmpty(csv))
                throw new ArgumentException(ErrorMessages.CsvExport.ContentEmpty, nameof(csv));
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException(ErrorMessages.CsvExport.FilePathEmpty, nameof(filePath));

            string? dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            // UTF-8 BOM → 한글 Windows Excel에서 자동 인코딩 감지
            File.WriteAllText(filePath, csv, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

            // 행 수 계산 (헤더 1행 제외)
            int lineCount = csv.Split(new[] { NEWLINE }, StringSplitOptions.RemoveEmptyEntries).Length;
            return Math.Max(0, lineCount - 1);
        }

        /// <summary>
        /// 기본 저장 경로를 반환한다 (바탕화면/URSUS_export.csv).
        /// </summary>
        public static string GetDefaultFilePath()
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            return Path.Combine(desktop, "URSUS_export.csv");
        }

        // ─────────────────────────────────────────────────────────────────
        //  Column Definition
        // ─────────────────────────────────────────────────────────────────

        private static List<CsvColumnDef> BuildColumnDefs(
            IReadOnlyList<string> legalCodes,
            IReadOnlyList<string> names,
            IReadOnlyList<double> areas,
            IReadOnlyList<double> values,
            IReadOnlyList<CsvColumnDef>? extraColumns)
        {
            var columns = new List<CsvColumnDef>
            {
                new("법정동코드", i => legalCodes[i]),
                new("법정동명",   i => names[i]),
                new("면적(㎡)",   i => areas[i].ToString("F1", CultureInfo.InvariantCulture)),
                new("오버레이값", i => values[i].ToString("F4", CultureInfo.InvariantCulture)),
            };

            if (extraColumns != null)
                columns.AddRange(extraColumns);

            return columns;
        }

        // ─────────────────────────────────────────────────────────────────
        //  Validation
        // ─────────────────────────────────────────────────────────────────

        private static void ValidateInputs(
            IReadOnlyList<string> legalCodes,
            IReadOnlyList<string> names,
            IReadOnlyList<double> areas,
            IReadOnlyList<double> values)
        {
            if (legalCodes == null) throw new ArgumentNullException(nameof(legalCodes));
            if (names      == null) throw new ArgumentNullException(nameof(names));
            if (areas      == null) throw new ArgumentNullException(nameof(areas));
            if (values     == null) throw new ArgumentNullException(nameof(values));

            int count = legalCodes.Count;
            if (names.Count != count || areas.Count != count || values.Count != count)
                throw new ArgumentException(
                    ErrorMessages.Data.InputListLengthMismatch(
                        legalCodes.Count, names.Count, areas.Count, values.Count));
        }

        // ─────────────────────────────────────────────────────────────────
        //  CSV Escaping (RFC 4180)
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// RFC 4180 규칙에 따라 CSV 필드를 이스케이프한다.
        /// - 쉼표, 큰따옴표, 줄바꿈이 포함된 필드는 큰따옴표로 감싼다
        /// - 필드 내 큰따옴표는 두 번 반복한다 ("")
        /// </summary>
        private static string Escape(string field)
        {
            if (string.IsNullOrEmpty(field))
                return string.Empty;

            bool needsQuoting = field.IndexOfAny(new[] { DELIMITER, '"', '\r', '\n' }) >= 0;
            if (!needsQuoting)
                return field;

            return '"' + field.Replace("\"", "\"\"") + '"';
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    //  확장용 컬럼 정의 (data_extensibility 원칙)
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// CSV 출력 컬럼 하나를 정의한다.
    /// 새 데이터셋 추가 시 CsvColumnDef 인스턴스만 생성하면
    /// CsvExporter 내부 코드 변경 없이 컬럼이 추가된다.
    /// </summary>
    public sealed class CsvColumnDef
    {
        /// <summary>CSV 헤더에 표시될 컬럼 이름</summary>
        public string Header { get; }

        private readonly Func<int, string> _valueAccessor;

        /// <summary>
        /// 컬럼 정의를 생성한다.
        /// </summary>
        /// <param name="header">컬럼 헤더 이름</param>
        /// <param name="valueAccessor">행 인덱스 → 셀 값 문자열 변환 함수</param>
        public CsvColumnDef(string header, Func<int, string> valueAccessor)
        {
            Header         = header ?? throw new ArgumentNullException(nameof(header));
            _valueAccessor = valueAccessor ?? throw new ArgumentNullException(nameof(valueAccessor));
        }

        /// <summary>지정 행의 셀 값을 반환한다.</summary>
        public string GetValue(int rowIndex) => _valueAccessor(rowIndex);
    }
}
