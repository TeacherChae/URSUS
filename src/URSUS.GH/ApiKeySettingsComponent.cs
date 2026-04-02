using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Grasshopper.Kernel;
using URSUS.Config;
using URSUS.Resources;

namespace URSUS.GH
{
    /// <summary>
    /// Grasshopper 내에서 API 키를 관리하는 컴포넌트.
    /// 더블클릭 시 키 설정 대화상자를 표시하고, 현재 키 상태를 출력한다.
    ///
    /// 학생 사용자가 Setup.exe 없이도 GH 캔버스에서 직접
    /// API 키를 입력/확인/저장할 수 있도록 한다.
    /// </summary>
    public class ApiKeySettingsComponent : GH_Component
    {
        private bool _dialogOpen;

        public ApiKeySettingsComponent()
            : base(
                "API Key Settings",
                "Keys",
                "API 키 확인 및 설정.\n" +
                "캔버스에 놓으면 현재 키 로드 상태를 표시합니다.\n" +
                "Open 입력을 True로 설정하면 키 설정 대화상자가 열립니다.",
                "URSUS",
                "Config")
        { }

        public override Guid ComponentGuid
            => new Guid("e7c3d1a4-5f28-4b09-9c62-8d4a1e7f3b5c");

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddBooleanParameter("Open", "O",
                "True로 설정 시 API 키 설정 대화상자를 엽니다.\n" +
                "Button 컴포넌트를 연결하세요.",
                GH_ParamAccess.item, false);
            pManager[0].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("VWorld Key", "VK",
                "현재 로드된 VWorld API 키 상태", GH_ParamAccess.item);
            pManager.AddTextParameter("Seoul Key", "SK",
                "현재 로드된 서울 열린데이터 API 키 상태", GH_ParamAccess.item);
            pManager.AddTextParameter("DataGoKr Key", "DK",
                "현재 로드된 공공데이터포털 API 키 상태", GH_ParamAccess.item);
            pManager.AddTextParameter("Diagnostic", "D",
                "전체 키 로드 진단 메시지", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            bool open = false;
            DA.GetData(0, ref open);

            // 키 설정 대화상자 열기
            if (open && !_dialogOpen)
            {
                _dialogOpen = true;
                try
                {
                    using var dlg = new ApiKeyDialog();
                    dlg.ShowDialog(Grasshopper.Instances.DocumentEditor);

                    if (dlg.KeysUpdated)
                    {
                        // 키가 변경되었으면 Solver 컴포넌트들도 재실행되도록 문서 만료
                        ExpireSolution(true);
                    }
                }
                finally
                {
                    _dialogOpen = false;
                }
            }

            // 현재 키 상태 출력
            var provider = new ApiKeyProvider();

            string vwStatus = FormatKeyStatus("VWorld", provider.VWorldKey,
                provider.KeySources, ApiKeyProvider.KEY_VWORLD);
            string skStatus = FormatKeyStatus("서울 열린데이터", provider.SeoulKey,
                provider.KeySources, ApiKeyProvider.KEY_SEOUL);
            string dgStatus = FormatKeyStatus("공공데이터포털", provider.DataGoKrKey,
                provider.KeySources, ApiKeyProvider.KEY_DATA_GO_KR);

            DA.SetData(0, vwStatus);
            DA.SetData(1, skStatus);
            DA.SetData(2, dgStatus);
            DA.SetData(3, provider.GetDiagnosticMessage(
                ApiKeyProvider.KEY_VWORLD,
                ApiKeyProvider.KEY_SEOUL,
                ApiKeyProvider.KEY_DATA_GO_KR));

            // 런타임 메시지로 요약 표시
            var missing = provider.GetMissingKeys(
                ApiKeyProvider.KEY_VWORLD, ApiKeyProvider.KEY_SEOUL);

            if (missing.Count > 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    ErrorMessages.ApiKeySettings.MissingRequired(string.Join(", ", missing)));
            }
            else
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    ErrorMessages.ApiKeySettings.LoadComplete);
            }
        }

        private static string FormatKeyStatus(
            string label, string? key,
            IReadOnlyDictionary<string, string> sources, string keyName)
        {
            if (string.IsNullOrWhiteSpace(key))
                return $"{label}: 미설정";

            string source = sources.TryGetValue(keyName, out var s) ? s : "unknown";
            // 키 값 마스킹 (앞 4자만 표시)
            string masked = key!.Length > 4
                ? key[..4] + new string('*', Math.Min(key.Length - 4, 20))
                : new string('*', key.Length);
            return $"{label}: {masked} (출처: {source})";
        }
    }

    // ================================================================
    //  API 키 설정 대화상자 (Grasshopper 내에서 사용)
    // ================================================================

    /// <summary>
    /// Grasshopper 내에서 API 키를 입력/저장하는 경량 대화상자.
    /// SetupForm의 Step 2와 동일한 기능을 독립적인 폼으로 제공한다.
    /// </summary>
    internal sealed class ApiKeyDialog : Form
    {
        private static readonly Color ACCENT   = Color.FromArgb(45, 65, 145);
        private static readonly Color SUCCESS  = Color.FromArgb(34, 139, 34);
        private static readonly Color ERR      = Color.FromArgb(200, 40, 40);
        private static readonly Color MUTED    = Color.FromArgb(120, 120, 120);
        private static readonly Font  LBL_FONT = new("Segoe UI", 9.5f, FontStyle.Bold);
        private static readonly Font  TXT_FONT = new("Consolas", 9.5f);
        private static readonly Font  BTN_FONT = new("Segoe UI", 9f, FontStyle.Bold);
        private static readonly Font  STS_FONT = new("Segoe UI", 8.5f);

        private readonly TextBox _txtVW = new();
        private readonly TextBox _txtSK = new();
        private readonly TextBox _txtDG = new();
        private readonly Label   _lblVW = new();
        private readonly Label   _lblSK = new();
        private readonly Label   _lblDG = new();
        private readonly CheckBox _chkShow = new();

        public bool KeysUpdated { get; private set; }

        public ApiKeyDialog()
        {
            Text            = "URSUS API 키 설정";
            Size            = new Size(560, 440);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox     = false;
            MinimizeBox     = false;
            StartPosition   = FormStartPosition.CenterParent;
            BackColor       = Color.White;

            BuildUI();
            LoadExisting();
        }

        private void BuildUI()
        {
            int y = 15;

            var title = new Label
            {
                Text = "API 키 설정",
                Font = new Font("Segoe UI", 14f, FontStyle.Bold),
                ForeColor = ACCENT,
                AutoSize = true,
                Location = new Point(20, y),
            };
            Controls.Add(title);
            y += 35;

            var desc = new Label
            {
                Text = "API 키를 입력하고 저장하세요. Solver 컴포넌트에서 자동으로 로드됩니다.",
                Font = STS_FONT,
                ForeColor = MUTED,
                AutoSize = true,
                MaximumSize = new Size(500, 0),
                Location = new Point(20, y),
            };
            Controls.Add(desc);
            y += 28;

            // VWorld
            AddKeyField("VWorld API 키 (필수)", _txtVW, _lblVW,
                "VWorld API 키", ref y);

            // Seoul
            AddKeyField("서울 열린데이터 API 키 (필수)", _txtSK, _lblSK,
                "서울 열린데이터 API 키", ref y);

            // DataGoKr
            AddKeyField("공공데이터포털 API 키 (선택)", _txtDG, _lblDG,
                "공공데이터포털 API 키", ref y);

            // Show/hide toggle
            _chkShow.Text     = "키 표시";
            _chkShow.Font     = STS_FONT;
            _chkShow.AutoSize = true;
            _chkShow.Location = new Point(20, y);
            _chkShow.CheckedChanged += (_, _) =>
            {
                char c = _chkShow.Checked ? '\0' : '●';
                _txtVW.PasswordChar = c;
                _txtSK.PasswordChar = c;
                _txtDG.PasswordChar = c;
            };
            Controls.Add(_chkShow);
            y += 30;

            // 기본 마스킹
            _txtVW.PasswordChar = '●';
            _txtSK.PasswordChar = '●';
            _txtDG.PasswordChar = '●';

            // 버튼 패널
            y += 10;
            var btnSave = new Button
            {
                Text      = "저장",
                Font      = BTN_FONT,
                Size      = new Size(100, 34),
                Location  = new Point(310, y),
                BackColor = ACCENT,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
            };
            btnSave.FlatAppearance.BorderSize = 0;
            btnSave.Click += BtnSave_Click;
            Controls.Add(btnSave);

            var btnCancel = new Button
            {
                Text      = "취소",
                Font      = BTN_FONT,
                Size      = new Size(100, 34),
                Location  = new Point(420, y),
                FlatStyle = FlatStyle.Flat,
            };
            btnCancel.Click += (_, _) =>
            {
                DialogResult = DialogResult.Cancel;
                Close();
            };
            Controls.Add(btnCancel);
        }

        private void AddKeyField(string label, TextBox txt, Label status,
            string placeholder, ref int y)
        {
            var lbl = new Label
            {
                Text = label,
                Font = LBL_FONT,
                AutoSize = true,
                Location = new Point(20, y),
            };
            Controls.Add(lbl);
            y += 22;

            txt.Font     = TXT_FONT;
            txt.Size     = new Size(500, 26);
            txt.Location = new Point(20, y);
            // WinForms 4.x 호환: PlaceholderText 대신 ToolTip 사용
            var tip = new ToolTip();
            tip.SetToolTip(txt, placeholder);
            Controls.Add(txt);
            y += 30;

            status.Font     = STS_FONT;
            status.AutoSize = true;
            status.Location = new Point(20, y);
            Controls.Add(status);
            y += 20;
        }

        private void LoadExisting()
        {
            try
            {
                var provider = new ApiKeyProvider();

                if (!string.IsNullOrWhiteSpace(provider.VWorldKey))
                {
                    _txtVW.Text = provider.VWorldKey;
                    SetStatus(_lblVW, ErrorMessages.ApiKeySettings.LoadedFromExisting, MUTED);
                }
                if (!string.IsNullOrWhiteSpace(provider.SeoulKey))
                {
                    _txtSK.Text = provider.SeoulKey;
                    SetStatus(_lblSK, ErrorMessages.ApiKeySettings.LoadedFromExisting, MUTED);
                }
                if (!string.IsNullOrWhiteSpace(provider.DataGoKrKey))
                {
                    _txtDG.Text = provider.DataGoKrKey;
                    SetStatus(_lblDG, ErrorMessages.ApiKeySettings.LoadedFromExisting, MUTED);
                }
            }
            catch { }
        }

        private void BtnSave_Click(object? sender, EventArgs e)
        {
            var keys = new Dictionary<string, string>();
            string vw = _txtVW.Text.Trim();
            string sk = _txtSK.Text.Trim();
            string dg = _txtDG.Text.Trim();

            if (!string.IsNullOrEmpty(vw)) keys[ApiKeyProvider.KEY_VWORLD]     = vw;
            if (!string.IsNullOrEmpty(sk)) keys[ApiKeyProvider.KEY_SEOUL]      = sk;
            if (!string.IsNullOrEmpty(dg)) keys[ApiKeyProvider.KEY_DATA_GO_KR] = dg;

            if (keys.Count == 0)
            {
                MessageBox.Show(
                    ErrorMessages.ApiKeySettings.NoKeysToSave,
                    "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                ApiKeyProvider.SaveToUserProfile(keys);
                KeysUpdated = true;

                // 상태 업데이트
                if (keys.ContainsKey(ApiKeyProvider.KEY_VWORLD))
                    SetStatus(_lblVW, "✓ 저장 완료", SUCCESS);
                if (keys.ContainsKey(ApiKeyProvider.KEY_SEOUL))
                    SetStatus(_lblSK, "✓ 저장 완료", SUCCESS);
                if (keys.ContainsKey(ApiKeyProvider.KEY_DATA_GO_KR))
                    SetStatus(_lblDG, "✓ 저장 완료", SUCCESS);

                MessageBox.Show(
                    ErrorMessages.ApiKeySettings.SaveComplete(keys.Count),
                    "저장 완료", MessageBoxButtons.OK, MessageBoxIcon.Information);

                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ErrorMessages.ApiKeySettings.SaveFailed(ex.Message),
                    "저장 실패", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static void SetStatus(Label lbl, string text, Color color)
        {
            lbl.Text      = text;
            lbl.ForeColor = color;
        }
    }
}
