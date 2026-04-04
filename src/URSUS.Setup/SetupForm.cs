using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using URSUS.Config;

namespace URSUS.Setup
{
    /// <summary>
    /// URSUS 설치 마법사 — API 키 입력 및 유효성 검증 UI.
    ///
    /// 화면 구성:
    ///   Step 1: 환영 + 설명
    ///   Step 2: API 키 입력 + 검증
    ///   Step 3: 설치 경로 확인 + 설치 실행
    ///   Step 4: 완료
    /// </summary>
    public sealed class SetupForm : Form
    {
        // ── UI 상수 ─────────────────────────────────────────────────────
        private const int FORM_WIDTH  = 620;
        private const int FORM_MIN_HEIGHT = 480;

        private static readonly Color BG_COLOR      = Color.White;
        private static readonly Color ACCENT_COLOR   = Color.FromArgb(45, 65, 145);   // URSUS 브랜드 블루
        private static readonly Color SUCCESS_COLOR  = Color.FromArgb(34, 139, 34);
        private static readonly Color ERROR_COLOR    = Color.FromArgb(200, 40, 40);
        private static readonly Color MUTED_COLOR    = Color.FromArgb(120, 120, 120);
        private static readonly Font  TITLE_FONT     = new("Segoe UI", 16f, FontStyle.Bold);
        private static readonly Font  SUBTITLE_FONT  = new("Segoe UI", 10f, FontStyle.Regular);
        private static readonly Font  LABEL_FONT     = new("Segoe UI", 9.5f, FontStyle.Bold);
        private static readonly Font  INPUT_FONT     = new("Consolas", 9.5f, FontStyle.Regular);
        private static readonly Font  BUTTON_FONT    = new("Segoe UI", 9.5f, FontStyle.Bold);
        private static readonly Font  STATUS_FONT    = new("Segoe UI", 8.5f, FontStyle.Regular);

        // ── 컨트롤 ─────────────────────────────────────────────────────
        private readonly Panel      _headerPanel     = new();
        private readonly Panel      _contentPanel    = new();
        private readonly Panel      _footerPanel     = new();

        // Step 2: API 키 입력
        private readonly TextBox    _txtVWorldKey    = new();
        private readonly TextBox    _txtSeoulKey     = new();
        private readonly TextBox    _txtDataGoKrKey  = new();
        private readonly Button     _btnValidateVW   = new();
        private readonly Button     _btnValidateSK   = new();
        private readonly Button     _btnValidateDG   = new();
        private readonly Button     _btnValidateAll  = new();
        private readonly Label      _lblVWStatus     = new();
        private readonly Label      _lblSKStatus     = new();
        private readonly Label      _lblDGStatus     = new();
        private readonly CheckBox   _chkSkipKeys     = new();
        private readonly CheckBox   _chkShowKeys     = new();
        private readonly LinkLabel  _lnkVWorld       = new();
        private readonly LinkLabel  _lnkSeoul        = new();
        private readonly LinkLabel  _lnkDataGoKr     = new();

        // Step 3: 설치 경로
        private readonly TextBox    _txtInstallPath  = new();
        private readonly ProgressBar _progressBar    = new();
        private readonly Label      _lblProgress     = new();

        // Navigation
        private readonly Button     _btnBack         = new();
        private readonly Button     _btnNext         = new();
        private readonly Button     _btnCancel       = new();

        // ── 상태 ────────────────────────────────────────────────────────
        private int _currentStep = 1;
        private bool _vwKeyValid;
        private bool _skKeyValid;
        private bool _dgKeyValid;
        private readonly ApiKeyValidator _validator = new();
        private CancellationTokenSource? _cts;

        // ── 기존 키 로드 ────────────────────────────────────────────────
        private string? _existingVWKey;
        private string? _existingSKKey;
        private string? _existingDGKey;

        // ── 설치 결과 (Step 3 → Step 4 전달) ────────────────────────────
        private PostInstallVerifier.VerificationReport? _lastVerificationReport;
        private DependencyInstaller.InstallResult? _lastInstallResult;
        private SetupConfigGenerator.ConfigGenerationResult? _lastConfigResult;

        public SetupForm()
        {
            InitializeComponent();
            LoadExistingKeys();
            ShowStep(1);
        }

        // ================================================================
        //  초기화
        // ================================================================

        private void InitializeComponent()
        {
            SuspendLayout();

            // Form
            Text            = "URSUS Setup";
            Size            = new Size(FORM_WIDTH, FORM_MIN_HEIGHT);
            MinimumSize     = new Size(FORM_WIDTH, FORM_MIN_HEIGHT);
            MaximizeBox     = false;
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition   = FormStartPosition.CenterScreen;
            BackColor       = BG_COLOR;

            // Header
            _headerPanel.Dock      = DockStyle.Top;
            _headerPanel.Height    = 70;
            _headerPanel.BackColor = ACCENT_COLOR;
            _headerPanel.Padding   = new Padding(20, 12, 20, 12);
            Controls.Add(_headerPanel);

            var lblTitle = new Label
            {
                Text      = "URSUS Setup",
                Font      = TITLE_FONT,
                ForeColor = Color.White,
                AutoSize  = true,
                Location  = new Point(20, 10),
            };
            var lblSubtitle = new Label
            {
                Text      = "Urban Research with Spatial Utility System",
                Font      = SUBTITLE_FONT,
                ForeColor = Color.FromArgb(200, 210, 240),
                AutoSize  = true,
                Location  = new Point(20, 42),
            };
            _headerPanel.Controls.AddRange(new Control[] { lblTitle, lblSubtitle });

            // Content
            _contentPanel.Dock    = DockStyle.Fill;
            _contentPanel.Padding = new Padding(30, 20, 30, 10);
            Controls.Add(_contentPanel);

            // Footer
            _footerPanel.Dock      = DockStyle.Bottom;
            _footerPanel.Height    = 55;
            _footerPanel.BackColor = Color.FromArgb(245, 245, 248);
            Controls.Add(_footerPanel);

            // Footer buttons
            _btnCancel.Text      = "취소";
            _btnCancel.Font      = BUTTON_FONT;
            _btnCancel.Size      = new Size(90, 34);
            _btnCancel.Location  = new Point(15, 10);
            _btnCancel.FlatStyle = FlatStyle.Flat;
            _btnCancel.Click    += (_, _) => Close();
            _footerPanel.Controls.Add(_btnCancel);

            _btnBack.Text      = "< 이전";
            _btnBack.Font      = BUTTON_FONT;
            _btnBack.Size      = new Size(90, 34);
            _btnBack.Location  = new Point(FORM_WIDTH - 230, 10);
            _btnBack.FlatStyle = FlatStyle.Flat;
            _btnBack.Click    += (_, _) => ShowStep(_currentStep - 1);
            _footerPanel.Controls.Add(_btnBack);

            _btnNext.Text      = "다음 >";
            _btnNext.Font      = BUTTON_FONT;
            _btnNext.Size      = new Size(100, 34);
            _btnNext.Location  = new Point(FORM_WIDTH - 130, 10);
            _btnNext.BackColor = ACCENT_COLOR;
            _btnNext.ForeColor = Color.White;
            _btnNext.FlatStyle = FlatStyle.Flat;
            _btnNext.FlatAppearance.BorderSize = 0;
            _btnNext.Click    += BtnNext_Click;
            _footerPanel.Controls.Add(_btnNext);

            // Ensure proper Z-order (header on top)
            _headerPanel.BringToFront();
            _footerPanel.BringToFront();

            ResumeLayout(true);
        }

        private void LoadExistingKeys()
        {
            try
            {
                var provider = new ApiKeyProvider();
                _existingVWKey = provider.VWorldKey;
                _existingSKKey = provider.SeoulKey;
                _existingDGKey = provider.DataGoKrKey;
            }
            catch
            {
                // 기존 설정 로드 실패 — 무시
            }
        }

        // ================================================================
        //  스텝 네비게이션
        // ================================================================

        private void ShowStep(int step)
        {
            _currentStep = step;
            _contentPanel.Controls.Clear();
            _contentPanel.SuspendLayout();

            _btnBack.Visible  = step > 1;
            _btnCancel.Visible = step < 4;

            switch (step)
            {
                case 1: BuildStep1_Welcome();    break;
                case 2: BuildStep2_ApiKeys();    break;
                case 3: BuildStep3_Install();    break;
                case 4: BuildStep4_Complete();   break;
            }

            _contentPanel.ResumeLayout(true);

            // 컨텐츠 높이에 맞게 폼 크기 자동 조정
            AdjustFormHeight();
        }

        private void AdjustFormHeight()
        {
            int contentBottom = 0;
            foreach (Control c in _contentPanel.Controls)
            {
                int bottom = c.Bottom;
                if (bottom > contentBottom)
                    contentBottom = bottom;
            }

            // 헤더(70) + 컨텐츠 패딩(상20+하10) + 컨텐츠 높이 + 푸터(55) + 프레임 여백(40)
            int requiredHeight = 70 + 30 + contentBottom + 55 + 40;
            int newHeight = Math.Max(requiredHeight, FORM_MIN_HEIGHT);
            if (Height != newHeight)
                Height = newHeight;
        }

        private async void BtnNext_Click(object? sender, EventArgs e)
        {
            switch (_currentStep)
            {
                case 1:
                    ShowStep(2);
                    break;

                case 2:
                    // API 키 저장 (비어있어도 진행 가능)
                    SaveApiKeys();
                    ShowStep(3);
                    break;

                case 3:
                    // 설치 실행
                    _btnNext.Enabled = false;
                    _btnBack.Enabled = false;
                    await RunInstallAsync();
                    ShowStep(4);
                    break;

                case 4:
                    Close();
                    break;
            }
        }

        // ================================================================
        //  Step 1: 환영
        // ================================================================

        private void BuildStep1_Welcome()
        {
            _btnNext.Text = "다음 >";

            int y = 10;
            AddLabel("URSUS 설치를 시작합니다", TITLE_FONT, ACCENT_COLOR, ref y);
            y += 10;

            AddLabel(
                "URSUS는 건축 설계 초기 단계에서 대지의 공간 데이터를\n" +
                "한 번에 분석할 수 있는 Grasshopper 플러그인입니다.",
                SUBTITLE_FONT, Color.Black, ref y);
            y += 20;

            AddLabel("설치 과정:", LABEL_FONT, Color.Black, ref y);
            y += 5;

            string[] steps =
            {
                "1.  API 키 입력 (VWorld, 서울 열린데이터)",
                "     - 나중에 변경할 수 있습니다",
                "2.  Grasshopper Libraries 폴더에 파일 복사",
                "3.  Windows 파일 차단 자동 해제",
                "",
                "소요 시간: 약 1분",
            };
            foreach (string line in steps)
            {
                AddLabel(line, SUBTITLE_FONT,
                    line.StartsWith("     ") ? MUTED_COLOR : Color.Black, ref y);
            }

            y += 20;
            AddLabel(
                "※ API 키가 없어도 설치는 가능합니다.\n" +
                "   키는 나중에 appsettings.json에서 설정하거나\n" +
                "   Grasshopper 컴포넌트에서 직접 입력할 수 있습니다.",
                STATUS_FONT, MUTED_COLOR, ref y);
        }

        // ================================================================
        //  Step 2: API 키 입력 + 검증
        // ================================================================

        private void BuildStep2_ApiKeys()
        {
            _btnNext.Text    = "다음 >";
            _btnNext.Enabled = true;

            int y = 0;
            AddLabel("API 키 설정", new Font("Segoe UI", 13f, FontStyle.Bold), ACCENT_COLOR, ref y);
            AddLabel("데이터 수집에 사용할 API 키를 입력하세요. (선택사항)", SUBTITLE_FONT, MUTED_COLOR, ref y);
            y += 8;

            // ── VWorld ──
            BuildKeyRow(
                "VWorld API 키 (필수)", "키가 없다면? → vworld.kr 에서 무료 발급",
                "https://www.vworld.kr/dev/v4dv_2ddataguide2_s001.do",
                _lnkVWorld, _txtVWorldKey, _btnValidateVW, _lblVWStatus,
                _existingVWKey, "VWorld API 키를 붙여넣으세요",
                () => { _vwKeyValid = false; UpdateKeyStatus(_lblVWStatus, "", Color.Black); },
                async () => await ValidateVWorldAsync(),
                ref y);

            // ── Seoul ──
            BuildKeyRow(
                "서울 열린데이터 API 키 (필수)", "키가 없다면? → data.seoul.go.kr 에서 무료 발급",
                "https://data.seoul.go.kr/",
                _lnkSeoul, _txtSeoulKey, _btnValidateSK, _lblSKStatus,
                _existingSKKey, "서울 열린데이터 API 키를 붙여넣으세요",
                () => { _skKeyValid = false; UpdateKeyStatus(_lblSKStatus, "", Color.Black); },
                async () => await ValidateSeoulAsync(),
                ref y);

            // ── DataGoKr ──
            BuildKeyRow(
                "공공데이터포털 API 키 (선택)", "키가 없다면? → data.go.kr 에서 무료 발급",
                "https://www.data.go.kr/data/15058747/openapi.do",
                _lnkDataGoKr, _txtDataGoKrKey, _btnValidateDG, _lblDGStatus,
                _existingDGKey, "공공데이터포털 API 키를 붙여넣으세요",
                () => { _dgKeyValid = false; UpdateKeyStatus(_lblDGStatus, "", Color.Black); },
                async () => await ValidateDataGoKrAsync(),
                ref y);

            // ── 키 표시/숨김 토글 ──
            y += 4;
            _chkShowKeys.Text     = "API 키 표시";
            _chkShowKeys.Font     = STATUS_FONT;
            _chkShowKeys.AutoSize = true;
            _chkShowKeys.Location = new Point(28, y);
            _chkShowKeys.Checked  = false;
            _chkShowKeys.CheckedChanged += (_, _) =>
            {
                char passChar = _chkShowKeys.Checked ? '\0' : '●';
                _txtVWorldKey.PasswordChar   = passChar;
                _txtSeoulKey.PasswordChar    = passChar;
                _txtDataGoKrKey.PasswordChar = passChar;
            };
            _contentPanel.Controls.Add(_chkShowKeys);

            // ── 모두 검증 버튼 ──
            _btnValidateAll.Text      = "모두 검증";
            _btnValidateAll.Font      = BUTTON_FONT;
            _btnValidateAll.Size      = new Size(100, 26);
            _btnValidateAll.Location  = new Point(180, y - 2);
            _btnValidateAll.FlatStyle = FlatStyle.Flat;
            _btnValidateAll.BackColor = Color.FromArgb(60, 130, 80);
            _btnValidateAll.ForeColor = Color.White;
            _btnValidateAll.FlatAppearance.BorderSize = 0;
            _btnValidateAll.Click    += async (_, _) => await ValidateAllKeysAsync();
            _contentPanel.Controls.Add(_btnValidateAll);
            y += 26;

            // ── 건너뛰기 옵션 ──
            y += 4;
            _chkSkipKeys.Text     = "API 키 없이 설치만 진행 (나중에 설정)";
            _chkSkipKeys.Font     = SUBTITLE_FONT;
            _chkSkipKeys.AutoSize = true;
            _chkSkipKeys.Location = new Point(28, y);
            _contentPanel.Controls.Add(_chkSkipKeys);

            // 키 기본 마스킹 적용
            _txtVWorldKey.PasswordChar   = '●';
            _txtSeoulKey.PasswordChar    = '●';
            _txtDataGoKrKey.PasswordChar = '●';

            // 기존 키가 있으면 상태 표시
            if (!string.IsNullOrWhiteSpace(_existingVWKey))
                UpdateKeyStatus(_lblVWStatus, "기존 설정에서 키를 불러왔습니다.", MUTED_COLOR);
            if (!string.IsNullOrWhiteSpace(_existingSKKey))
                UpdateKeyStatus(_lblSKStatus, "기존 설정에서 키를 불러왔습니다.", MUTED_COLOR);
            if (!string.IsNullOrWhiteSpace(_existingDGKey))
                UpdateKeyStatus(_lblDGStatus, "기존 설정에서 키를 불러왔습니다.", MUTED_COLOR);
        }

        /// <summary>
        /// API 키 입력 행을 공통 생성하는 헬퍼.
        /// 라벨, 링크, 텍스트박스, 검증 버튼, 상태 라벨을 일괄 배치한다.
        /// </summary>
        private void BuildKeyRow(
            string label, string linkText, string linkUrl,
            LinkLabel lnk, TextBox txt, Button btnValidate, Label lblStatus,
            string? existingKey, string placeholder,
            Action onTextChanged, Func<Task> onValidateClick,
            ref int y)
        {
            AddLabel(label, LABEL_FONT, Color.Black, ref y);

            lnk.Text      = linkText;
            lnk.Font      = STATUS_FONT;
            lnk.AutoSize  = true;
            lnk.Location  = new Point(30, y);
            lnk.LinkColor = ACCENT_COLOR;
            lnk.LinkClicked += (_, _) => OpenUrl(linkUrl);
            _contentPanel.Controls.Add(lnk);
            y += 20;

            txt.Font            = INPUT_FONT;
            txt.Size            = new Size(380, 28);
            txt.Location        = new Point(30, y);
            txt.PlaceholderText = placeholder;
            txt.Text            = existingKey ?? "";
            txt.TextChanged    += (_, _) => onTextChanged();
            _contentPanel.Controls.Add(txt);

            btnValidate.Text      = "검증";
            btnValidate.Font      = BUTTON_FONT;
            btnValidate.Size      = new Size(70, 28);
            btnValidate.Location  = new Point(418, y);
            btnValidate.FlatStyle = FlatStyle.Flat;
            btnValidate.BackColor = ACCENT_COLOR;
            btnValidate.ForeColor = Color.White;
            btnValidate.FlatAppearance.BorderSize = 0;
            btnValidate.Click    += async (_, _) => await onValidateClick();
            _contentPanel.Controls.Add(btnValidate);
            y += 32;

            lblStatus.Font     = STATUS_FONT;
            lblStatus.AutoSize = true;
            lblStatus.Location = new Point(30, y);
            _contentPanel.Controls.Add(lblStatus);
            y += 18;
        }

        // ── 검증 실행 ───────────────────────────────────────────────────

        private async Task ValidateVWorldAsync()
        {
            await ValidateSingleKeyAsync(
                _txtVWorldKey, _btnValidateVW, _lblVWStatus,
                key => _validator.ValidateVWorldKeyAsync(key, GetOrCreateCts()),
                valid => _vwKeyValid = valid);
        }

        private async Task ValidateSeoulAsync()
        {
            await ValidateSingleKeyAsync(
                _txtSeoulKey, _btnValidateSK, _lblSKStatus,
                key => _validator.ValidateSeoulKeyAsync(key, GetOrCreateCts()),
                valid => _skKeyValid = valid);
        }

        private async Task ValidateDataGoKrAsync()
        {
            await ValidateSingleKeyAsync(
                _txtDataGoKrKey, _btnValidateDG, _lblDGStatus,
                key => _validator.ValidateDataGoKrKeyAsync(key, GetOrCreateCts()),
                valid => _dgKeyValid = valid);
        }

        /// <summary>
        /// 단일 키 검증 공통 로직: 빈 값 체크 → 버튼 비활성화 → 검증 → 결과 표시.
        /// </summary>
        private async Task ValidateSingleKeyAsync(
            TextBox txt, Button btn, Label lbl,
            Func<string, Task<ApiKeyValidator.ValidationResult>> validateFunc,
            Action<bool> setValid)
        {
            string key = txt.Text.Trim();
            if (string.IsNullOrEmpty(key))
            {
                UpdateKeyStatus(lbl, "키를 입력해주세요.", ERROR_COLOR);
                return;
            }

            btn.Enabled = false;
            btn.Text    = "...";
            UpdateKeyStatus(lbl, "검증 중...", MUTED_COLOR);

            try
            {
                var result = await validateFunc(key);
                setValid(result.IsValid);
                UpdateKeyStatus(lbl,
                    (result.IsValid ? "✓ " : "✗ ") + result.Message,
                    result.IsValid ? SUCCESS_COLOR : ERROR_COLOR);
            }
            catch (OperationCanceledException) { }
            finally
            {
                btn.Enabled = true;
                btn.Text    = "검증";
            }
        }

        /// <summary>
        /// 입력된 모든 키를 병렬로 일괄 검증한다.
        /// </summary>
        private async Task ValidateAllKeysAsync()
        {
            _btnValidateAll.Enabled = false;
            _btnValidateAll.Text    = "검증 중...";

            var keys = new Dictionary<string, string>();
            string vw = _txtVWorldKey.Text.Trim();
            string sk = _txtSeoulKey.Text.Trim();
            string dg = _txtDataGoKrKey.Text.Trim();

            bool anyKey = false;

            if (!string.IsNullOrEmpty(vw)) { keys[ApiKeyProvider.KEY_VWORLD] = vw; anyKey = true; }
            if (!string.IsNullOrEmpty(sk)) { keys[ApiKeyProvider.KEY_SEOUL] = sk;  anyKey = true; }
            if (!string.IsNullOrEmpty(dg)) { keys[ApiKeyProvider.KEY_DATA_GO_KR] = dg; anyKey = true; }

            if (!anyKey)
            {
                UpdateKeyStatus(_lblVWStatus, "키를 입력해주세요.", ERROR_COLOR);
                _btnValidateAll.Enabled = true;
                _btnValidateAll.Text    = "모두 검증";
                return;
            }

            // 각 키의 상태를 "검증 중"으로 갱신
            if (keys.ContainsKey(ApiKeyProvider.KEY_VWORLD))
                UpdateKeyStatus(_lblVWStatus, "검증 중...", MUTED_COLOR);
            if (keys.ContainsKey(ApiKeyProvider.KEY_SEOUL))
                UpdateKeyStatus(_lblSKStatus, "검증 중...", MUTED_COLOR);
            if (keys.ContainsKey(ApiKeyProvider.KEY_DATA_GO_KR))
                UpdateKeyStatus(_lblDGStatus, "검증 중...", MUTED_COLOR);

            _cts?.Cancel();
            _cts = new CancellationTokenSource();

            try
            {
                var results = await _validator.ValidateAllAsync(keys, _cts.Token);

                foreach (var entry in results)
                {
                    string prefix = entry.Result.IsValid ? "✓ " : "✗ ";
                    Color  color  = entry.Result.IsValid ? SUCCESS_COLOR : ERROR_COLOR;

                    switch (entry.KeyName)
                    {
                        case ApiKeyProvider.KEY_VWORLD:
                            _vwKeyValid = entry.Result.IsValid;
                            UpdateKeyStatus(_lblVWStatus, prefix + entry.Result.Message, color);
                            break;
                        case ApiKeyProvider.KEY_SEOUL:
                            _skKeyValid = entry.Result.IsValid;
                            UpdateKeyStatus(_lblSKStatus, prefix + entry.Result.Message, color);
                            break;
                        case ApiKeyProvider.KEY_DATA_GO_KR:
                            _dgKeyValid = entry.Result.IsValid;
                            UpdateKeyStatus(_lblDGStatus, prefix + entry.Result.Message, color);
                            break;
                    }
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                _btnValidateAll.Enabled = true;
                _btnValidateAll.Text    = "모두 검증";
            }
        }

        private CancellationToken GetOrCreateCts()
        {
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            return _cts.Token;
        }

        private static void UpdateKeyStatus(Label lbl, string text, Color color)
        {
            lbl.Text      = text;
            lbl.ForeColor = color;
        }

        // ── 키 저장 ─────────────────────────────────────────────────────

        private void SaveApiKeys()
        {
            var keys = CollectEnteredKeys();

            if (keys.Count > 0)
            {
                try
                {
                    ApiKeyProvider.SaveToUserProfile(keys);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        $"API 키 저장 중 오류가 발생했습니다:\n{ex.Message}\n\n" +
                        "설치 후 appsettings.json에서 직접 설정할 수 있습니다.",
                        "경고", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        /// <summary>UI 텍스트박스에서 비어 있지 않은 키만 수집한다.</summary>
        private Dictionary<string, string> CollectEnteredKeys()
        {
            var keys = new Dictionary<string, string>();
            string vw = _txtVWorldKey.Text.Trim();
            string sk = _txtSeoulKey.Text.Trim();
            string dg = _txtDataGoKrKey.Text.Trim();

            if (!string.IsNullOrEmpty(vw)) keys[ApiKeyProvider.KEY_VWORLD]     = vw;
            if (!string.IsNullOrEmpty(sk)) keys[ApiKeyProvider.KEY_SEOUL]      = sk;
            if (!string.IsNullOrEmpty(dg)) keys[ApiKeyProvider.KEY_DATA_GO_KR] = dg;
            return keys;
        }

        // ================================================================
        //  Step 3: 설치 경로 + 실행
        // ================================================================

        private void BuildStep3_Install()
        {
            _btnNext.Text    = "설치";
            _btnNext.Enabled = true;
            _btnBack.Enabled = true;

            int y = 5;
            AddLabel("설치 준비", new Font("Segoe UI", 13f, FontStyle.Bold), ACCENT_COLOR, ref y);
            y += 10;

            string ghLibPath = GetGrasshopperLibrariesPath();

            AddLabel("설치 경로:", LABEL_FONT, Color.Black, ref y);
            y += 2;
            _txtInstallPath.Font     = INPUT_FONT;
            _txtInstallPath.Size     = new Size(500, 28);
            _txtInstallPath.Location = new Point(30, y);
            _txtInstallPath.Text     = ghLibPath;
            _txtInstallPath.ReadOnly = true;
            _txtInstallPath.BackColor = Color.FromArgb(245, 245, 248);
            _contentPanel.Controls.Add(_txtInstallPath);
            y += 40;

            AddLabel("설치할 파일:", LABEL_FONT, Color.Black, ref y);
            y += 5;
            string[] files = { "URSUS.GH.gha", "URSUS.dll", "Clipper2Lib.dll", "appsettings.json" };
            foreach (string f in files)
            {
                AddLabel($"  •  {f}", SUBTITLE_FONT, Color.Black, ref y);
            }
            y += 15;

            // API 키 요약
            AddLabel("API 키 설정:", LABEL_FONT, Color.Black, ref y);
            y += 5;
            AddKeyStatusSummary("VWorld", _txtVWorldKey.Text, _vwKeyValid, ref y);
            AddKeyStatusSummary("서울 열린데이터", _txtSeoulKey.Text, _skKeyValid, ref y);
            AddKeyStatusSummary("공공데이터포털", _txtDataGoKrKey.Text, _dgKeyValid, ref y);

            y += 25;

            _progressBar.Size     = new Size(500, 22);
            _progressBar.Location = new Point(30, y);
            _progressBar.Style    = ProgressBarStyle.Continuous;
            _progressBar.Visible  = false;
            _contentPanel.Controls.Add(_progressBar);
            y += 30;

            _lblProgress.Font     = STATUS_FONT;
            _lblProgress.AutoSize = true;
            _lblProgress.Location = new Point(30, y);
            _contentPanel.Controls.Add(_lblProgress);
        }

        /// <summary>
        /// URSUS.GH 빌드 산출물이 있는 디렉토리를 탐색한다.
        /// 모든 프로젝트가 bin/dist/로 출력되므로 Setup.exe와 같은 폴더를 우선 사용.
        /// </summary>
        private static string ResolveSourceDir()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;

            // Setup.exe와 같은 폴더 (bin/dist/win-x64/)에 gha가 없으면 상위(bin/dist/) 탐색
            if (File.Exists(Path.Combine(baseDir, "URSUS.GH.gha")))
                return baseDir;

            string? parentDir = Directory.GetParent(baseDir.TrimEnd(Path.DirectorySeparatorChar))?.FullName;
            if (parentDir != null && File.Exists(Path.Combine(parentDir, "URSUS.GH.gha")))
                return parentDir;

            return baseDir;
        }

        private async Task RunInstallAsync()
        {
            _progressBar.Visible = true;
            _progressBar.Value   = 0;

            string targetDir = _txtInstallPath.Text;
            string sourceDir = ResolveSourceDir();

            try
            {
                // ── Phase 1: 의존성 검사 ──
                _lblProgress.Text  = "의존성 검사 중...";
                _progressBar.Value = 5;
                await Task.Delay(100); // UI 업데이트

                var check = DependencyInstaller.CheckDependencies(sourceDir, targetDir);

                if (check.MissingRequired.Count > 0)
                {
                    _lblProgress.Text      = $"필수 파일 {check.MissingRequired.Count}개 누락";
                    _lblProgress.ForeColor = ERROR_COLOR;

                    MessageBox.Show(
                        check.ToSummary() + "\n\n설치를 계속하시겠습니까?\n(누락 파일은 건너뜁니다)",
                        "파일 누락 경고", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }

                // ── Phase 2: 파일 복사 + 차단 해제 ──
                _lblProgress.Text  = "파일 설치 중...";
                _progressBar.Value = 10;

                var installResult = await Task.Run(() =>
                    DependencyInstaller.InstallDependencies(sourceDir, targetDir,
                        (progress, message) =>
                        {
                            try
                            {
                                Invoke((Action)(() =>
                                {
                                    _progressBar.Value = Math.Min(progress, 90);
                                    _lblProgress.Text  = message;
                                }));
                            }
                            catch (ObjectDisposedException) { }
                        }));

                // ── Phase 3: 설정 파일 생성 ──
                _lblProgress.Text  = "설정 파일 생성 중...";
                _progressBar.Value = 90;
                await Task.Delay(100);

                var keys = CollectEnteredKeys();
                var configResult = SetupConfigGenerator.GenerateConfig(keys, targetDir);

                _progressBar.Value = 95;
                await Task.Delay(100);

                // ── Phase 4: 설치 후 검증 ──
                _lblProgress.Text  = "설치 검증 중...";
                _progressBar.Value = 97;
                _lastVerificationReport = DependencyInstaller.VerifyInstallation(targetDir);
                _lastInstallResult      = installResult;
                _lastConfigResult       = configResult;

                // ── 완료 ──
                _progressBar.Value = 100;
                if (installResult.AllSucceeded)
                {
                    _lblProgress.Text      = $"설치 완료! ({installResult.Elapsed.TotalSeconds:F1}초)";
                    _lblProgress.ForeColor = SUCCESS_COLOR;
                }
                else
                {
                    _lblProgress.Text      = $"설치 완료 (경고 {installResult.FailureCount}건)";
                    _lblProgress.ForeColor = Color.FromArgb(180, 120, 0);
                }
            }
            catch (Exception ex)
            {
                _lblProgress.Text      = $"설치 실패: {ex.Message}";
                _lblProgress.ForeColor = ERROR_COLOR;
                _progressBar.Value     = 0;

                MessageBox.Show(
                    $"설치 중 오류가 발생했습니다:\n\n{ex.Message}\n\n" +
                    "수동으로 파일을 복사해주세요:\n" +
                    $"  대상 폴더: {targetDir}",
                    "설치 오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ================================================================
        //  Step 4: 완료
        // ================================================================

        private void BuildStep4_Complete()
        {
            _btnNext.Text    = "닫기";
            _btnNext.Enabled = true;
            _btnBack.Visible = false;

            // ── 검증 보고서: 캐시된 결과 사용 또는 새로 실행 ──
            string installDir = _txtInstallPath.Text;
            var report = _lastVerificationReport
                         ?? PostInstallVerifier.Verify(installDir);

            int y = 10;

            // ── 타이틀 ──
            if (report.AllPassed && !report.HasWarnings)
            {
                AddLabel("설치 완료!", TITLE_FONT, SUCCESS_COLOR, ref y);
            }
            else if (report.ErrorCount > 0)
            {
                AddLabel("설치 확인 필요", TITLE_FONT, ERROR_COLOR, ref y);
            }
            else
            {
                AddLabel("설치 완료!", TITLE_FONT, SUCCESS_COLOR, ref y);
            }

            // ── 설치 소요 시간 ──
            if (_lastInstallResult != null)
            {
                AddLabel(
                    $"설치 시간: {_lastInstallResult.Elapsed.TotalSeconds:F1}초 | " +
                    $"파일: {_lastInstallResult.SuccessCount}개 성공" +
                    (_lastInstallResult.FailureCount > 0
                        ? $", {_lastInstallResult.FailureCount}개 실패" : ""),
                    STATUS_FONT, MUTED_COLOR, ref y);
            }

            y += 8;

            // ── 3단계 검증 결과 ──
            AddLabel("설치 검증 결과:", LABEL_FONT, ACCENT_COLOR, ref y);
            y += 3;

            // Step 1: 파일 검사
            bool step1Ok = report.Step1Passed;
            AddLabel(
                step1Ok ? "  ✓ Step 1: 핵심 파일 확인 완료" : "  ✗ Step 1: 파일 누락 발견",
                STATUS_FONT, step1Ok ? SUCCESS_COLOR : ERROR_COLOR, ref y);

            if (!step1Ok)
            {
                foreach (var item in report.Step1_Files.Where(c => !c.Passed))
                    AddLabel($"      {item.Message}", STATUS_FONT, MUTED_COLOR, ref y);
            }

            // Step 2: 설정 파일 검사
            bool step2Ok = report.Step2Passed;
            AddLabel(
                step2Ok ? "  ✓ Step 2: 설정 파일(appsettings.json) 유효" : "  ⚠ Step 2: 설정 파일 확인 필요",
                STATUS_FONT, step2Ok ? SUCCESS_COLOR : Color.FromArgb(180, 120, 0), ref y);

            // 설정 파일 경로 표시
            if (_lastConfigResult is { AnyFileCreated: true })
            {
                if (_lastConfigResult.InstallDirPath != null)
                    AddLabel($"      저장: {_lastConfigResult.InstallDirPath}", STATUS_FONT, MUTED_COLOR, ref y);
                if (_lastConfigResult.UserProfilePath != null)
                    AddLabel($"      백업: {_lastConfigResult.UserProfilePath}", STATUS_FONT, MUTED_COLOR, ref y);
            }

            // Step 3: API 키 검사
            bool step3Ok = report.Step3Passed && !report.Step3_ApiKeys.Any(c => !c.Passed);
            AddLabel(
                step3Ok
                    ? "  ✓ Step 3: API 키 로드 확인"
                    : "  ⚠ Step 3: 일부 API 키 미설정 (나중에 설정 가능)",
                STATUS_FONT, step3Ok ? SUCCESS_COLOR : Color.FromArgb(180, 120, 0), ref y);

            y += 12;

            // ── 다음 단계 안내 ──
            AddLabel("사용 방법 (3단계):", LABEL_FONT, Color.Black, ref y);
            y += 5;

            string[] nextSteps =
            {
                "1.  Rhino 8을 재시작합니다.",
                "2.  Grasshopper를 열고 URSUS 탭을 찾습니다.",
                "3.  Solver 컴포넌트를 캔버스에 놓으면 바로 실행됩니다!",
                "",
                "※ DataSet 입력을 비워두면 전체 데이터가 자동 분석됩니다.",
            };
            foreach (string line in nextSteps)
            {
                AddLabel(line, SUBTITLE_FONT, Color.Black, ref y);
            }

            y += 12;

            // ── 오류/경고/성공별 안내 ──
            if (report.ErrorCount > 0)
            {
                AddLabel(
                    "⚠ 설치에 문제가 있습니다.\n" +
                    "  설치 프로그램을 다시 실행하거나,\n" +
                    "  파일을 수동으로 복사해주세요.\n" +
                    $"  대상 폴더: {installDir}",
                    STATUS_FONT, ERROR_COLOR, ref y);
            }
            else if (report.HasWarnings)
            {
                AddLabel(
                    "ℹ API 키가 일부 미설정되었습니다.\n" +
                    "  Grasshopper에서 VK/SK 입력에 직접 연결하거나,\n" +
                    "  설치 폴더의 appsettings.json을 편집하세요.",
                    STATUS_FONT, Color.FromArgb(180, 120, 0), ref y);
            }
            else
            {
                AddLabel(
                    "✓ 모든 항목이 정상입니다. 바로 사용할 수 있습니다!",
                    STATUS_FONT, SUCCESS_COLOR, ref y);
            }
        }

        // ================================================================
        //  헬퍼
        // ================================================================

        private void AddKeyStatusSummary(string label, string keyText, bool isValid, ref int y)
        {
            string status = string.IsNullOrWhiteSpace(keyText)
                ? "미설정 (나중에 설정 가능)"
                : (isValid ? "검증 완료 ✓" : "입력됨 (미검증)");
            AddLabel($"  {label}: {status}", SUBTITLE_FONT,
                isValid ? SUCCESS_COLOR : MUTED_COLOR, ref y);
        }

        private Label AddLabel(string text, Font font, Color color, ref int y)
        {
            var lbl = new Label
            {
                Text      = text,
                Font      = font,
                ForeColor = color,
                AutoSize  = true,
                MaximumSize = new Size(_contentPanel.Width - 60, 0),
                Location  = new Point(30, y),
            };
            _contentPanel.Controls.Add(lbl);
            y += lbl.PreferredHeight + 4;
            return lbl;
        }

        private static string GetGrasshopperLibrariesPath()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "Grasshopper", "Libraries", "URSUS");
        }

        private static void OpenUrl(string url)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName        = url,
                    UseShellExecute = true,
                });
            }
            catch { }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _cts?.Cancel();
                _cts?.Dispose();
                _validator.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
