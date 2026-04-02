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
        private const int FORM_HEIGHT = 520;

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
        private readonly Button     _btnValidateVW   = new();
        private readonly Button     _btnValidateSK   = new();
        private readonly Label      _lblVWStatus     = new();
        private readonly Label      _lblSKStatus     = new();
        private readonly CheckBox   _chkSkipKeys     = new();
        private readonly LinkLabel  _lnkVWorld       = new();
        private readonly LinkLabel  _lnkSeoul        = new();

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
        private readonly ApiKeyValidator _validator = new();
        private CancellationTokenSource? _cts;

        // ── 기존 키 로드 ────────────────────────────────────────────────
        private string? _existingVWKey;
        private string? _existingSKKey;

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
            Size            = new Size(FORM_WIDTH, FORM_HEIGHT);
            MinimumSize     = new Size(FORM_WIDTH, FORM_HEIGHT);
            MaximizeBox     = false;
            FormBorderStyle = FormBorderStyle.FixedDialog;
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

            int y = 5;
            AddLabel("API 키 설정", new Font("Segoe UI", 13f, FontStyle.Bold), ACCENT_COLOR, ref y);
            y += 5;
            AddLabel("데이터 수집에 사용할 API 키를 입력하세요. (선택사항)", SUBTITLE_FONT, MUTED_COLOR, ref y);
            y += 15;

            // ── VWorld ──
            AddLabel("VWorld API 키", LABEL_FONT, Color.Black, ref y);
            y += 2;

            // 발급 링크
            _lnkVWorld.Text      = "키가 없다면? → vworld.kr 에서 무료 발급";
            _lnkVWorld.Font      = STATUS_FONT;
            _lnkVWorld.AutoSize  = true;
            _lnkVWorld.Location  = new Point(30, y);
            _lnkVWorld.LinkColor = ACCENT_COLOR;
            _lnkVWorld.LinkClicked += (_, _) =>
                OpenUrl("https://www.vworld.kr/dev/v4dv_2ddataguide2_s001.do");
            _contentPanel.Controls.Add(_lnkVWorld);
            y += 22;

            _txtVWorldKey.Font        = INPUT_FONT;
            _txtVWorldKey.Size        = new Size(380, 28);
            _txtVWorldKey.Location    = new Point(30, y);
            _txtVWorldKey.PlaceholderText = "VWorld API 키를 붙여넣으세요";
            _txtVWorldKey.Text        = _existingVWKey ?? "";
            _txtVWorldKey.TextChanged += (_, _) => { _vwKeyValid = false; UpdateVWStatus("", Color.Black); };
            _contentPanel.Controls.Add(_txtVWorldKey);

            _btnValidateVW.Text      = "검증";
            _btnValidateVW.Font      = BUTTON_FONT;
            _btnValidateVW.Size      = new Size(70, 28);
            _btnValidateVW.Location  = new Point(418, y);
            _btnValidateVW.FlatStyle = FlatStyle.Flat;
            _btnValidateVW.BackColor = ACCENT_COLOR;
            _btnValidateVW.ForeColor = Color.White;
            _btnValidateVW.FlatAppearance.BorderSize = 0;
            _btnValidateVW.Click    += async (_, _) => await ValidateVWorldAsync();
            _contentPanel.Controls.Add(_btnValidateVW);
            y += 34;

            _lblVWStatus.Font     = STATUS_FONT;
            _lblVWStatus.AutoSize = true;
            _lblVWStatus.Location = new Point(30, y);
            _contentPanel.Controls.Add(_lblVWStatus);
            y += 25;

            // ── Seoul ──
            y += 8;
            AddLabel("서울 열린데이터 API 키", LABEL_FONT, Color.Black, ref y);
            y += 2;

            _lnkSeoul.Text      = "키가 없다면? → data.seoul.go.kr 에서 무료 발급";
            _lnkSeoul.Font      = STATUS_FONT;
            _lnkSeoul.AutoSize  = true;
            _lnkSeoul.Location  = new Point(30, y);
            _lnkSeoul.LinkColor = ACCENT_COLOR;
            _lnkSeoul.LinkClicked += (_, _) =>
                OpenUrl("https://data.seoul.go.kr/");
            _contentPanel.Controls.Add(_lnkSeoul);
            y += 22;

            _txtSeoulKey.Font        = INPUT_FONT;
            _txtSeoulKey.Size        = new Size(380, 28);
            _txtSeoulKey.Location    = new Point(30, y);
            _txtSeoulKey.PlaceholderText = "서울 열린데이터 API 키를 붙여넣으세요";
            _txtSeoulKey.Text        = _existingSKKey ?? "";
            _txtSeoulKey.TextChanged += (_, _) => { _skKeyValid = false; UpdateSKStatus("", Color.Black); };
            _contentPanel.Controls.Add(_txtSeoulKey);

            _btnValidateSK.Text      = "검증";
            _btnValidateSK.Font      = BUTTON_FONT;
            _btnValidateSK.Size      = new Size(70, 28);
            _btnValidateSK.Location  = new Point(418, y);
            _btnValidateSK.FlatStyle = FlatStyle.Flat;
            _btnValidateSK.BackColor = ACCENT_COLOR;
            _btnValidateSK.ForeColor = Color.White;
            _btnValidateSK.FlatAppearance.BorderSize = 0;
            _btnValidateSK.Click    += async (_, _) => await ValidateSeoulAsync();
            _contentPanel.Controls.Add(_btnValidateSK);
            y += 34;

            _lblSKStatus.Font     = STATUS_FONT;
            _lblSKStatus.AutoSize = true;
            _lblSKStatus.Location = new Point(30, y);
            _contentPanel.Controls.Add(_lblSKStatus);
            y += 25;

            // ── 건너뛰기 옵션 ──
            y += 15;
            _chkSkipKeys.Text     = "API 키 없이 설치만 진행 (나중에 설정)";
            _chkSkipKeys.Font     = SUBTITLE_FONT;
            _chkSkipKeys.AutoSize = true;
            _chkSkipKeys.Location = new Point(28, y);
            _contentPanel.Controls.Add(_chkSkipKeys);

            // 기존 키가 있으면 상태 표시
            if (!string.IsNullOrWhiteSpace(_existingVWKey))
                UpdateVWStatus("기존 설정에서 키를 불러왔습니다.", MUTED_COLOR);
            if (!string.IsNullOrWhiteSpace(_existingSKKey))
                UpdateSKStatus("기존 설정에서 키를 불러왔습니다.", MUTED_COLOR);
        }

        // ── 검증 실행 ───────────────────────────────────────────────────

        private async Task ValidateVWorldAsync()
        {
            string key = _txtVWorldKey.Text.Trim();
            if (string.IsNullOrEmpty(key))
            {
                UpdateVWStatus("키를 입력해주세요.", ERROR_COLOR);
                return;
            }

            _btnValidateVW.Enabled = false;
            _btnValidateVW.Text    = "...";
            UpdateVWStatus("검증 중...", MUTED_COLOR);

            _cts?.Cancel();
            _cts = new CancellationTokenSource();

            try
            {
                var result = await _validator.ValidateVWorldKeyAsync(key, _cts.Token);
                _vwKeyValid = result.IsValid;
                UpdateVWStatus(
                    (result.IsValid ? "✓ " : "✗ ") + result.Message,
                    result.IsValid ? SUCCESS_COLOR : ERROR_COLOR);
            }
            catch (OperationCanceledException) { }
            finally
            {
                _btnValidateVW.Enabled = true;
                _btnValidateVW.Text    = "검증";
            }
        }

        private async Task ValidateSeoulAsync()
        {
            string key = _txtSeoulKey.Text.Trim();
            if (string.IsNullOrEmpty(key))
            {
                UpdateSKStatus("키를 입력해주세요.", ERROR_COLOR);
                return;
            }

            _btnValidateSK.Enabled = false;
            _btnValidateSK.Text    = "...";
            UpdateSKStatus("검증 중...", MUTED_COLOR);

            _cts?.Cancel();
            _cts = new CancellationTokenSource();

            try
            {
                var result = await _validator.ValidateSeoulKeyAsync(key, _cts.Token);
                _skKeyValid = result.IsValid;
                UpdateSKStatus(
                    (result.IsValid ? "✓ " : "✗ ") + result.Message,
                    result.IsValid ? SUCCESS_COLOR : ERROR_COLOR);
            }
            catch (OperationCanceledException) { }
            finally
            {
                _btnValidateSK.Enabled = true;
                _btnValidateSK.Text    = "검증";
            }
        }

        private void UpdateVWStatus(string text, Color color)
        {
            _lblVWStatus.Text      = text;
            _lblVWStatus.ForeColor = color;
        }

        private void UpdateSKStatus(string text, Color color)
        {
            _lblSKStatus.Text      = text;
            _lblSKStatus.ForeColor = color;
        }

        // ── 키 저장 ─────────────────────────────────────────────────────

        private void SaveApiKeys()
        {
            var keys = new Dictionary<string, string>();

            string vw = _txtVWorldKey.Text.Trim();
            string sk = _txtSeoulKey.Text.Trim();

            if (!string.IsNullOrEmpty(vw))
                keys[ApiKeyProvider.KEY_VWORLD] = vw;
            if (!string.IsNullOrEmpty(sk))
                keys[ApiKeyProvider.KEY_SEOUL] = sk;

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
            string vwStatus = string.IsNullOrWhiteSpace(_txtVWorldKey.Text)
                ? "미설정 (나중에 설정 가능)"
                : (_vwKeyValid ? "검증 완료 ✓" : "입력됨 (미검증)");
            string skStatus = string.IsNullOrWhiteSpace(_txtSeoulKey.Text)
                ? "미설정 (나중에 설정 가능)"
                : (_skKeyValid ? "검증 완료 ✓" : "입력됨 (미검증)");
            AddLabel($"  VWorld: {vwStatus}", SUBTITLE_FONT,
                _vwKeyValid ? SUCCESS_COLOR : MUTED_COLOR, ref y);
            AddLabel($"  서울 열린데이터: {skStatus}", SUBTITLE_FONT,
                _skKeyValid ? SUCCESS_COLOR : MUTED_COLOR, ref y);

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

        private async Task RunInstallAsync()
        {
            _progressBar.Visible = true;
            _progressBar.Value   = 0;

            string targetDir = _txtInstallPath.Text;
            string sourceDir = AppDomain.CurrentDomain.BaseDirectory;

            try
            {
                // 1. 디렉토리 생성
                _lblProgress.Text = "설치 폴더 생성 중...";
                _progressBar.Value = 10;
                Directory.CreateDirectory(targetDir);
                await Task.Delay(200); // UI 업데이트용

                // 2. 파일 복사
                string[] filesToCopy = { "URSUS.GH.gha", "URSUS.dll", "Clipper2Lib.dll" };
                int step = 60 / Math.Max(filesToCopy.Length, 1);

                foreach (string fileName in filesToCopy)
                {
                    string src = Path.Combine(sourceDir, fileName);
                    string dst = Path.Combine(targetDir, fileName);

                    _lblProgress.Text = $"복사 중: {fileName}";

                    if (File.Exists(src))
                    {
                        File.Copy(src, dst, overwrite: true);
                        // Windows Zone.Identifier ADS 제거 (차단 해제)
                        RemoveZoneIdentifier(dst);
                    }

                    _progressBar.Value += step;
                    await Task.Delay(100);
                }

                // 3. appsettings.json 저장 (대상 폴더에도 복사)
                _lblProgress.Text = "설정 파일 저장 중...";
                _progressBar.Value = 80;
                SaveSettingsToInstallDir(targetDir);
                await Task.Delay(200);

                // 4. 완료
                _progressBar.Value = 100;
                _lblProgress.Text  = "설치 완료!";
                _lblProgress.ForeColor = SUCCESS_COLOR;
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

        private void SaveSettingsToInstallDir(string targetDir)
        {
            var keys = new Dictionary<string, string>();
            string vw = _txtVWorldKey.Text.Trim();
            string sk = _txtSeoulKey.Text.Trim();
            if (!string.IsNullOrEmpty(vw)) keys[ApiKeyProvider.KEY_VWORLD] = vw;
            if (!string.IsNullOrEmpty(sk)) keys[ApiKeyProvider.KEY_SEOUL]  = sk;

            if (keys.Count > 0)
            {
                // DLL 인접 경로에도 저장 (GH 로드 시 우선 탐색됨)
                string settingsPath = Path.Combine(targetDir, "appsettings.json");
                var settings = new UrsusSettings();
                if (keys.ContainsKey(ApiKeyProvider.KEY_VWORLD))
                    settings.VWorldKey = keys[ApiKeyProvider.KEY_VWORLD];
                if (keys.ContainsKey(ApiKeyProvider.KEY_SEOUL))
                    settings.SeoulKey = keys[ApiKeyProvider.KEY_SEOUL];

                string json = System.Text.Json.JsonSerializer.Serialize(settings,
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(settingsPath, json, System.Text.Encoding.UTF8);
            }
        }

        /// <summary>
        /// Windows Zone.Identifier ADS를 삭제하여 "이 파일은 다른 컴퓨터에서…" 차단을 해제한다.
        /// </summary>
        private static void RemoveZoneIdentifier(string filePath)
        {
            try
            {
                string adsPath = filePath + ":Zone.Identifier";
                // .NET에서 ADS 직접 삭제가 안 되므로 cmd 활용
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName  = "cmd.exe",
                    Arguments = $"/c echo. > \"{adsPath}\" 2>nul & del /f \"{adsPath}\" 2>nul",
                    CreateNoWindow  = true,
                    UseShellExecute = false,
                };
                System.Diagnostics.Process.Start(psi)?.WaitForExit(3000);
            }
            catch
            {
                // ADS 삭제 실패는 치명적이지 않음 — 사용자가 수동으로 해제 가능
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

            // ── Post-install 3-Step 검증 실행 ──
            string installDir = _txtInstallPath.Text;
            var report = PostInstallVerifier.Verify(installDir);

            int y = 10;

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

            y += 10;

            // ── 검증 결과 표시 ──
            AddLabel("설치 검증 결과:", LABEL_FONT, ACCENT_COLOR, ref y);
            y += 3;

            // Step 1: 파일 검사
            bool step1Ok = report.Step1Passed;
            AddLabel(
                step1Ok ? "  ✓ Step 1: 핵심 파일 확인 완료" : "  ✗ Step 1: 파일 누락 발견",
                STATUS_FONT, step1Ok ? SUCCESS_COLOR : ERROR_COLOR, ref y);

            // Step 2: 설정 파일 검사
            bool step2Ok = report.Step2Passed;
            AddLabel(
                step2Ok ? "  ✓ Step 2: 설정 파일(appsettings.json) 유효" : "  ⚠ Step 2: 설정 파일 확인 필요",
                STATUS_FONT, step2Ok ? SUCCESS_COLOR : Color.FromArgb(180, 120, 0), ref y);

            // Step 3: API 키 검사
            bool step3Ok = report.Step3Passed && !report.Step3_ApiKeys.Any(c => !c.Passed);
            AddLabel(
                step3Ok
                    ? "  ✓ Step 3: API 키 로드 확인"
                    : "  ⚠ Step 3: 일부 API 키 미설정 (나중에 설정 가능)",
                STATUS_FONT, step3Ok ? SUCCESS_COLOR : Color.FromArgb(180, 120, 0), ref y);

            y += 15;

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

            y += 15;

            // ── 오류 시 복구 안내 ──
            if (report.ErrorCount > 0)
            {
                AddLabel(
                    "⚠ 설치에 문제가 있습니다.\n" +
                    "  설치 프로그램을 다시 실행하거나,\n" +
                    "  파일을 수동으로 복사해주세요.",
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
