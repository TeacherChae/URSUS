; ==========================================================================
; URSUS Installer Script — Inno Setup 6.x
; Urban Research with Spatial Utility System
; Grasshopper plug-in for Rhino 8 (.NET 7)
; ==========================================================================

#define MyAppName      "URSUS"
#define MyAppVersion   "0.2.0"
#define MyAppPublisher "TeacherChae"
#define MyAppURL       "https://github.com/TeacherChae/URSUS"

; Grasshopper Libraries folder (per-user)
#define GHLibDir       "{userappdata}\Grasshopper\Libraries\URSUS"

[Setup]
AppId={{A049AECF-08A1-40FF-9203-E1E9BF4E9F53}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
DefaultDirName={#GHLibDir}
DisableDirPage=yes
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=..\dist
OutputBaseFilename=URSUS_{#MyAppVersion}_Setup
Compression=lzma2/ultra64
SolidCompression=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
; SetupIconFile: uncomment when docs\icon\ursus_icon.ico exists
; SetupIconFile=..\docs\icon\ursus_icon.ico
UninstallDisplayIcon={app}\URSUS.GH.gha
; LicenseFile: uncomment when a root LICENSE file exists
; LicenseFile=..\LICENSE
; Minimum Windows 10
MinVersion=10.0
; Version info shown in Add/Remove Programs
VersionInfoVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription=URSUS Grasshopper Plug-in Installer
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}

[Languages]
Name: "korean";  MessagesFile: "compiler:Languages\Korean.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Messages]
korean.WelcomeLabel2=URSUS (Urban Research with Spatial Utility System)%n%nRhino 8 Grasshopper 플러그인을 설치합니다.%n대지 분석에 필요한 공간 데이터를 한 번에 조회하고 시각화할 수 있습니다.
english.WelcomeLabel2=URSUS (Urban Research with Spatial Utility System)%n%nThis will install the Rhino 8 Grasshopper plug-in.%nAnalyze all spatial data for your site in one place.

[Types]
Name: "full";   Description: "전체 설치 (Full installation)"
Name: "custom"; Description: "사용자 지정 (Custom)"; Flags: iscustom

[Components]
Name: "core";     Description: "URSUS 핵심 컴포넌트 (Core)"; Types: full custom; Flags: fixed
Name: "samples";  Description: "예제 GH 파일 (Sample definitions)"; Types: full
Name: "cache";    Description: "오프라인 캐시 데이터 (Offline cache)"; Types: full

[Files]
; --- Core plug-in assemblies ---
Source: "..\bin\Release\URSUS.GH.gha";                DestDir: "{app}"; Components: core; Flags: ignoreversion
Source: "..\bin\Release\URSUS.dll";                    DestDir: "{app}"; Components: core; Flags: ignoreversion
Source: "..\bin\Release\Clipper2Lib.dll";              DestDir: "{app}"; Components: core; Flags: ignoreversion
Source: "..\bin\Release\System.Drawing.Common.dll";    DestDir: "{app}"; Components: core; Flags: ignoreversion
Source: "..\bin\Release\Microsoft.Win32.SystemEvents.dll"; DestDir: "{app}"; Components: core; Flags: ignoreversion

; --- Data mapping files ---
Source: "..\bin\adstrd_legald_mapping.json";           DestDir: "{app}"; Components: core; Flags: ignoreversion

; --- Runtime config (needed by .NET 7 GHA loader) ---
Source: "..\bin\Release\URSUS.GH.deps.json";          DestDir: "{app}"; Components: core; Flags: ignoreversion
Source: "..\bin\Release\URSUS.GH.runtimeconfig.json"; DestDir: "{app}"; Components: core; Flags: ignoreversion

; --- Sample Grasshopper definitions ---
Source: "..\URSUS_sample.gh";                          DestDir: "{app}\samples"; Components: samples; Flags: ignoreversion
Source: "..\URSUS_demo_1.gh";                          DestDir: "{app}\samples"; Components: samples; Flags: ignoreversion

; --- Offline cache seed data ---
Source: "..\cache\*";                                  DestDir: "{app}\cache"; Components: cache; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist

[Dirs]
Name: "{app}\cache"; Permissions: users-modify

[Registry]
; Store install metadata for programmatic access (e.g., updater, diagnostics)
Root: HKCU; Subkey: "Software\{#MyAppPublisher}\{#MyAppName}"; ValueType: string; ValueName: "InstallPath";  ValueData: "{app}";              Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\{#MyAppPublisher}\{#MyAppName}"; ValueType: string; ValueName: "Version";      ValueData: "{#MyAppVersion}";    Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\{#MyAppPublisher}\{#MyAppName}"; ValueType: string; ValueName: "Publisher";     ValueData: "{#MyAppPublisher}";  Flags: uninsdeletekey

[Icons]
Name: "{group}\URSUS 샘플 열기"; Filename: "{app}\samples"; Components: samples
Name: "{group}\URSUS 제거";      Filename: "{uninstallexe}"

[UninstallDelete]
; Clean up cache and any generated config files on uninstall
Type: filesandordirs; Name: "{app}\cache"
Type: files;          Name: "{app}\appsettings.json"

[Run]
; Post-install: optionally open sample file
Filename: "{app}\samples\URSUS_sample.gh"; Description: "Grasshopper 예제 파일 열기"; Flags: nowait postinstall skipifsilent shellexec; Components: samples

[Code]
// =========================================================================
//  API Key Custom Page — 변수 선언
// =========================================================================
var
  ApiKeyPage: TWizardPage;
  VWorldKeyEdit: TNewEdit;
  SeoulKeyEdit: TNewEdit;
  SkipKeysCheckBox: TNewCheckBox;
  VWorldStatusLabel: TNewStaticText;
  SeoulStatusLabel: TNewStaticText;
  VWorldLinkLabel: TNewStaticText;
  SeoulLinkLabel: TNewStaticText;

// =========================================================================
//  API Key Validation — 입력값 검증
// =========================================================================

// 문자열이 영숫자+하이픈+언더스코어만 포함하는지 검사
function IsValidKeyFormat(const Key: String): Boolean;
var
  I: Integer;
  C: Char;
begin
  Result := True;
  for I := 1 to Length(Key) do
  begin
    C := Key[I];
    if not ((C >= 'A') and (C <= 'Z')) and
       not ((C >= 'a') and (C <= 'z')) and
       not ((C >= '0') and (C <= '9')) and
       (C <> '-') and (C <> '_') then
    begin
      Result := False;
      Exit;
    end;
  end;
end;

// VWorld API 키 형식 검증 (영숫자+하이픈, 최소 10자)
function ValidateVWorldKey(const Key: String): String;
begin
  if Length(Key) = 0 then
  begin
    Result := '';
    Exit;
  end;
  if Length(Key) < 10 then
  begin
    Result := '✗ 키가 너무 짧습니다. (최소 10자)';
    Exit;
  end;
  if Length(Key) > 100 then
  begin
    Result := '✗ 키가 너무 깁니다. (최대 100자)';
    Exit;
  end;
  if not IsValidKeyFormat(Key) then
  begin
    Result := '✗ 영문, 숫자, 하이픈(-), 밑줄(_)만 사용 가능합니다.';
    Exit;
  end;
  Result := '✓ 형식이 올바릅니다. (설치 후 실제 연결 시 검증됩니다)';
end;

// 서울 열린데이터 API 키 형식 검증 (영숫자, 최소 10자)
function ValidateSeoulKey(const Key: String): String;
begin
  if Length(Key) = 0 then
  begin
    Result := '';
    Exit;
  end;
  if Length(Key) < 10 then
  begin
    Result := '✗ 키가 너무 짧습니다. (최소 10자)';
    Exit;
  end;
  if Length(Key) > 100 then
  begin
    Result := '✗ 키가 너무 깁니다. (최대 100자)';
    Exit;
  end;
  if not IsValidKeyFormat(Key) then
  begin
    Result := '✗ 영문, 숫자, 하이픈(-), 밑줄(_)만 사용 가능합니다.';
    Exit;
  end;
  Result := '✓ 형식이 올바릅니다. (설치 후 실제 연결 시 검증됩니다)';
end;

// 키 앞뒤 공백 제거
function TrimKey(const S: String): String;
var
  StartIdx, EndIdx: Integer;
begin
  StartIdx := 1;
  EndIdx := Length(S);
  while (StartIdx <= EndIdx) and (S[StartIdx] = ' ') do
    StartIdx := StartIdx + 1;
  while (EndIdx >= StartIdx) and (S[EndIdx] = ' ') do
    EndIdx := EndIdx - 1;
  Result := Copy(S, StartIdx, EndIdx - StartIdx + 1);
end;

// =========================================================================
//  API Key Storage — appsettings.json 저장
// =========================================================================

// appsettings.json 내용을 생성하여 지정 경로에 저장한다.
// 기존 파일이 있으면 덮어쓴다 (Inno Setup Pascal에서 JSON 파싱이 제한적이므로).
procedure SaveApiKeysToFile(const FilePath, VWKey, SKKey: String);
var
  Lines: TStringList;
  DirPath: String;
begin
  DirPath := ExtractFileDir(FilePath);
  if not DirExists(DirPath) then
    ForceDirectories(DirPath);

  Lines := TStringList.Create;
  try
    Lines.Add('{');
    if (Length(VWKey) > 0) and (Length(SKKey) > 0) then
    begin
      Lines.Add('  "VWorldKey": "' + VWKey + '",');
      Lines.Add('  "SeoulKey": "' + SKKey + '"');
    end
    else if Length(VWKey) > 0 then
    begin
      Lines.Add('  "VWorldKey": "' + VWKey + '"');
    end
    else if Length(SKKey) > 0 then
    begin
      Lines.Add('  "SeoulKey": "' + SKKey + '"');
    end;
    Lines.Add('}');
    Lines.SaveToFile(FilePath);
  finally
    Lines.Free;
  end;
end;

// 설치 디렉토리(DLL 인접)와 사용자 프로필(%APPDATA%/URSUS/) 양쪽에 저장
procedure SaveApiKeys();
var
  VWKey, SKKey: String;
  InstallPath, UserProfilePath: String;
begin
  VWKey := TrimKey(VWorldKeyEdit.Text);
  SKKey := TrimKey(SeoulKeyEdit.Text);

  // 키가 하나도 없으면 저장하지 않음
  if (Length(VWKey) = 0) and (Length(SKKey) = 0) then
    Exit;

  // 1. DLL 인접 appsettings.json (Grasshopper 로드 시 우선 탐색)
  InstallPath := ExpandConstant('{app}') + '\appsettings.json';
  SaveApiKeysToFile(InstallPath, VWKey, SKKey);

  // 2. 사용자 프로필 appsettings.json (백업 + 공유 경로)
  UserProfilePath := ExpandConstant('{userappdata}') + '\URSUS\appsettings.json';
  SaveApiKeysToFile(UserProfilePath, VWKey, SKKey);
end;

// =========================================================================
//  기존 키 로드 — 환경변수 및 기존 설정파일에서 복원
// =========================================================================

// 기존 appsettings.json에서 특정 키의 값을 추출한다.
// (간단한 문자열 검색 — 정식 JSON 파서 없이)
function ExtractJsonValue(const Json, KeyName: String): String;
var
  SearchStr, Remaining: String;
  StartPos, EndPos: Integer;
begin
  Result := '';
  SearchStr := '"' + KeyName + '"';
  StartPos := Pos(SearchStr, Json);
  if StartPos = 0 then
    Exit;

  Remaining := Copy(Json, StartPos + Length(SearchStr), Length(Json));
  // 콜론 이후의 따옴표를 찾는다
  StartPos := Pos('"', Remaining);
  if StartPos = 0 then
    Exit;

  Remaining := Copy(Remaining, StartPos + 1, Length(Remaining));
  EndPos := Pos('"', Remaining);
  if EndPos = 0 then
    Exit;

  Result := Copy(Remaining, 1, EndPos - 1);
end;

// 기존 설정 파일 또는 환경변수에서 키를 불러온다
procedure LoadExistingKeys();
var
  UserProfilePath, InstallPath: String;
  JsonContent: String;
  EnvVW, EnvSK: String;
  Lines: TStringList;
begin
  // 1) 사용자 프로필 appsettings.json
  UserProfilePath := ExpandConstant('{userappdata}') + '\URSUS\appsettings.json';
  if FileExists(UserProfilePath) then
  begin
    Lines := TStringList.Create;
    try
      Lines.LoadFromFile(UserProfilePath);
      JsonContent := Lines.Text;
      VWorldKeyEdit.Text := ExtractJsonValue(JsonContent, 'VWorldKey');
      SeoulKeyEdit.Text := ExtractJsonValue(JsonContent, 'SeoulKey');
    finally
      Lines.Free;
    end;
  end;

  // 2) DLL 인접 appsettings.json (있으면 덮어쓰기 — 더 높은 우선순위)
  InstallPath := ExpandConstant('{app}') + '\appsettings.json';
  if FileExists(InstallPath) then
  begin
    Lines := TStringList.Create;
    try
      Lines.LoadFromFile(InstallPath);
      JsonContent := Lines.Text;
      if Length(ExtractJsonValue(JsonContent, 'VWorldKey')) > 0 then
        VWorldKeyEdit.Text := ExtractJsonValue(JsonContent, 'VWorldKey');
      if Length(ExtractJsonValue(JsonContent, 'SeoulKey')) > 0 then
        SeoulKeyEdit.Text := ExtractJsonValue(JsonContent, 'SeoulKey');
    finally
      Lines.Free;
    end;
  end;

  // 3) 환경변수 (최고 우선순위)
  EnvVW := GetEnv('URSUS_VWORLD_KEY');
  EnvSK := GetEnv('URSUS_SEOUL_KEY');
  if Length(EnvVW) > 0 then
    VWorldKeyEdit.Text := EnvVW;
  if Length(EnvSK) > 0 then
    SeoulKeyEdit.Text := EnvSK;

  // 기존 키가 있으면 상태 표시
  if Length(TrimKey(VWorldKeyEdit.Text)) > 0 then
    VWorldStatusLabel.Caption := '기존 설정에서 키를 불러왔습니다.';
  if Length(TrimKey(SeoulKeyEdit.Text)) > 0 then
    SeoulStatusLabel.Caption := '기존 설정에서 키를 불러왔습니다.';
end;

// =========================================================================
//  Custom Wizard Page — API 키 입력 UI 생성
// =========================================================================

procedure CreateApiKeyPage();
var
  SectionLabel: TNewStaticText;
  DescLabel: TNewStaticText;
  NoteLabel: TNewStaticText;
begin
  ApiKeyPage := CreateCustomPage(
    wpSelectComponents,
    'API 키 설정',
    '데이터 수집에 사용할 API 키를 입력하세요. (선택사항 — 나중에 변경 가능)');

  // ── 설명 ──
  DescLabel := TNewStaticText.Create(ApiKeyPage);
  DescLabel.Parent := ApiKeyPage.Surface;
  DescLabel.Caption :=
    'URSUS는 VWorld와 서울 열린데이터 API를 통해 대지 정보를 조회합니다.' + #13#10 +
    '키가 없어도 설치는 가능하며, Grasshopper에서 직접 입력할 수도 있습니다.';
  DescLabel.Left := 0;
  DescLabel.Top := 0;
  DescLabel.Width := ApiKeyPage.SurfaceWidth;
  DescLabel.AutoSize := False;
  DescLabel.WordWrap := True;
  DescLabel.Height := 40;

  // ── VWorld API 키 섹션 ──
  SectionLabel := TNewStaticText.Create(ApiKeyPage);
  SectionLabel.Parent := ApiKeyPage.Surface;
  SectionLabel.Caption := 'VWorld API 키';
  SectionLabel.Left := 0;
  SectionLabel.Top := 50;
  SectionLabel.Font.Style := [fsBold];

  VWorldLinkLabel := TNewStaticText.Create(ApiKeyPage);
  VWorldLinkLabel.Parent := ApiKeyPage.Surface;
  VWorldLinkLabel.Caption := '키 발급: https://www.vworld.kr (무료 회원가입 후 발급)';
  VWorldLinkLabel.Left := 0;
  VWorldLinkLabel.Top := 68;
  VWorldLinkLabel.Font.Color := clGray;
  VWorldLinkLabel.Font.Size := 7;

  VWorldKeyEdit := TNewEdit.Create(ApiKeyPage);
  VWorldKeyEdit.Parent := ApiKeyPage.Surface;
  VWorldKeyEdit.Left := 0;
  VWorldKeyEdit.Top := 86;
  VWorldKeyEdit.Width := ApiKeyPage.SurfaceWidth;
  VWorldKeyEdit.Font.Name := 'Consolas';

  VWorldStatusLabel := TNewStaticText.Create(ApiKeyPage);
  VWorldStatusLabel.Parent := ApiKeyPage.Surface;
  VWorldStatusLabel.Caption := '';
  VWorldStatusLabel.Left := 0;
  VWorldStatusLabel.Top := 112;
  VWorldStatusLabel.Width := ApiKeyPage.SurfaceWidth;
  VWorldStatusLabel.Font.Size := 7;

  // ── 서울 열린데이터 API 키 섹션 ──
  SectionLabel := TNewStaticText.Create(ApiKeyPage);
  SectionLabel.Parent := ApiKeyPage.Surface;
  SectionLabel.Caption := '서울 열린데이터 API 키';
  SectionLabel.Left := 0;
  SectionLabel.Top := 140;
  SectionLabel.Font.Style := [fsBold];

  SeoulLinkLabel := TNewStaticText.Create(ApiKeyPage);
  SeoulLinkLabel.Parent := ApiKeyPage.Surface;
  SeoulLinkLabel.Caption := '키 발급: https://data.seoul.go.kr (무료 회원가입 후 발급)';
  SeoulLinkLabel.Left := 0;
  SeoulLinkLabel.Top := 158;
  SeoulLinkLabel.Font.Color := clGray;
  SeoulLinkLabel.Font.Size := 7;

  SeoulKeyEdit := TNewEdit.Create(ApiKeyPage);
  SeoulKeyEdit.Parent := ApiKeyPage.Surface;
  SeoulKeyEdit.Left := 0;
  SeoulKeyEdit.Top := 176;
  SeoulKeyEdit.Width := ApiKeyPage.SurfaceWidth;
  SeoulKeyEdit.Font.Name := 'Consolas';

  SeoulStatusLabel := TNewStaticText.Create(ApiKeyPage);
  SeoulStatusLabel.Parent := ApiKeyPage.Surface;
  SeoulStatusLabel.Caption := '';
  SeoulStatusLabel.Left := 0;
  SeoulStatusLabel.Top := 202;
  SeoulStatusLabel.Width := ApiKeyPage.SurfaceWidth;
  SeoulStatusLabel.Font.Size := 7;

  // ── 건너뛰기 옵션 ──
  SkipKeysCheckBox := TNewCheckBox.Create(ApiKeyPage);
  SkipKeysCheckBox.Parent := ApiKeyPage.Surface;
  SkipKeysCheckBox.Caption := 'API 키 없이 설치만 진행 (Grasshopper에서 나중에 입력)';
  SkipKeysCheckBox.Left := 0;
  SkipKeysCheckBox.Top := 240;
  SkipKeysCheckBox.Width := ApiKeyPage.SurfaceWidth;

  // ── 안내 메모 ──
  NoteLabel := TNewStaticText.Create(ApiKeyPage);
  NoteLabel.Parent := ApiKeyPage.Surface;
  NoteLabel.Caption :=
    '※ 입력한 키는 설치 폴더의 appsettings.json과' + #13#10 +
    '   %APPDATA%\URSUS\appsettings.json에 저장됩니다.' + #13#10 +
    '   환경변수(URSUS_VWORLD_KEY, URSUS_SEOUL_KEY)로도 설정 가능합니다.';
  NoteLabel.Left := 0;
  NoteLabel.Top := 275;
  NoteLabel.Width := ApiKeyPage.SurfaceWidth;
  NoteLabel.AutoSize := False;
  NoteLabel.WordWrap := True;
  NoteLabel.Height := 50;
  NoteLabel.Font.Color := clGray;
  NoteLabel.Font.Size := 7;
end;

// =========================================================================
//  Wizard Page Navigation — 검증 + 저장 연동
// =========================================================================

// "다음" 버튼 클릭 시 검증 실행
function NextButtonClick(CurPageID: Integer): Boolean;
var
  VWKey, SKKey: String;
  VWResult, SKResult: String;
  HasError: Boolean;
begin
  Result := True;

  if CurPageID = ApiKeyPage.ID then
  begin
    // 건너뛰기 체크 시 검증 없이 통과
    if SkipKeysCheckBox.Checked then
    begin
      VWorldStatusLabel.Caption := '';
      SeoulStatusLabel.Caption := '';
      Exit;
    end;

    VWKey := TrimKey(VWorldKeyEdit.Text);
    SKKey := TrimKey(SeoulKeyEdit.Text);
    HasError := False;

    // 둘 다 비어있으면 건너뛰기 안내
    if (Length(VWKey) = 0) and (Length(SKKey) = 0) then
    begin
      if MsgBox(
        'API 키가 입력되지 않았습니다.' + #13#10 + #13#10 +
        '키 없이 설치를 계속하시겠습니까?' + #13#10 +
        '(나중에 Grasshopper에서 직접 입력하거나' + #13#10 +
        ' appsettings.json에서 설정할 수 있습니다.)',
        mbConfirmation, MB_YESNO) = IDNO then
      begin
        Result := False;
      end;
      Exit;
    end;

    // VWorld 키 검증
    if Length(VWKey) > 0 then
    begin
      VWResult := ValidateVWorldKey(VWKey);
      VWorldStatusLabel.Caption := VWResult;
      if Pos('✗', VWResult) > 0 then
      begin
        VWorldStatusLabel.Font.Color := clRed;
        HasError := True;
      end
      else
      begin
        VWorldStatusLabel.Font.Color := clGreen;
      end;
    end
    else
    begin
      VWorldStatusLabel.Caption := '';
    end;

    // 서울 키 검증
    if Length(SKKey) > 0 then
    begin
      SKResult := ValidateSeoulKey(SKKey);
      SeoulStatusLabel.Caption := SKResult;
      if Pos('✗', SKResult) > 0 then
      begin
        SeoulStatusLabel.Font.Color := clRed;
        HasError := True;
      end
      else
      begin
        SeoulStatusLabel.Font.Color := clGreen;
      end;
    end
    else
    begin
      SeoulStatusLabel.Caption := '';
    end;

    // 검증 실패 시 진행 차단
    if HasError then
    begin
      MsgBox(
        'API 키 형식이 올바르지 않습니다.' + #13#10 +
        '키를 수정하거나, "API 키 없이 설치만 진행" 체크박스를 선택하세요.',
        mbError, MB_OK);
      Result := False;
    end;
  end;
end;

// =========================================================================
//  Zone.Identifier (Windows file-block) removal
// =========================================================================
// Downloaded DLLs/GHAs carry a Zone.Identifier alternate data stream (ADS)
// that causes .NET assembly loading to fail silently.
// We delete the ADS for every installed file so the user never has to
// right-click → Properties → Unblock manually.
procedure RemoveZoneIdentifier(const FilePath: String);
var
  AdsPath: String;
begin
  AdsPath := FilePath + ':Zone.Identifier';
  if FileExists(AdsPath) then
    DeleteFile(AdsPath);
  // Even if FileExists returns False for ADS paths on some OS versions,
  // attempt deletion anyway — it is harmless if the stream doesn't exist.
  DeleteFile(AdsPath);
end;

procedure UnblockInstalledFiles();
var
  SearchRec: TFindRec;
  InstallDir: String;
begin
  InstallDir := ExpandConstant('{app}');
  // Unblock all DLLs and GHA in the install directory
  if FindFirst(InstallDir + '\*.dll', SearchRec) then
  begin
    try
      repeat
        RemoveZoneIdentifier(InstallDir + '\' + SearchRec.Name);
      until not FindNext(SearchRec);
    finally
      FindClose(SearchRec);
    end;
  end;
  if FindFirst(InstallDir + '\*.gha', SearchRec) then
  begin
    try
      repeat
        RemoveZoneIdentifier(InstallDir + '\' + SearchRec.Name);
      until not FindNext(SearchRec);
    finally
      FindClose(SearchRec);
    end;
  end;
  // Also unblock JSON config files
  if FindFirst(InstallDir + '\*.json', SearchRec) then
  begin
    try
      repeat
        RemoveZoneIdentifier(InstallDir + '\' + SearchRec.Name);
      until not FindNext(SearchRec);
    finally
      FindClose(SearchRec);
    end;
  end;
end;

// =========================================================================
//  Pre-install check: warn if Rhino is running
// =========================================================================
function InitializeSetup(): Boolean;
begin
  Result := True;
  if CheckForMutexes('Rhino8_Mutex') then
  begin
    if MsgBox('Rhino 8이 실행 중입니다. 설치를 계속하시겠습니까?'#13#10 +
              '(Rhino를 닫고 설치하는 것을 권장합니다.)',
              mbConfirmation, MB_YESNO) = IDNO then
      Result := False;
  end;
end;

// =========================================================================
//  Wizard initialization — API 키 페이지 생성 + 기존 키 로드
// =========================================================================
procedure InitializeWizard();
begin
  CreateApiKeyPage();
  LoadExistingKeys();
end;

// =========================================================================
//  Post-install: API 키 저장 + unblock files + notify user
// =========================================================================
// =========================================================================
//  Post-install verification — 3-Step 검증
// =========================================================================

// Step 1: 핵심 파일 존재 확인
function VerifyStep1_Files(const InstallDir: String): String;
var
  MissingFiles: String;
  FileCount, MissCount: Integer;
begin
  MissingFiles := '';
  FileCount := 0;
  MissCount := 0;

  // 필수 파일 검사
  if FileExists(InstallDir + '\URSUS.GH.gha') then
    FileCount := FileCount + 1
  else begin
    MissingFiles := MissingFiles + '  ✗ URSUS.GH.gha' + #13#10;
    MissCount := MissCount + 1;
  end;

  if FileExists(InstallDir + '\URSUS.dll') then
    FileCount := FileCount + 1
  else begin
    MissingFiles := MissingFiles + '  ✗ URSUS.dll' + #13#10;
    MissCount := MissCount + 1;
  end;

  if FileExists(InstallDir + '\Clipper2Lib.dll') then
    FileCount := FileCount + 1
  else begin
    MissingFiles := MissingFiles + '  ✗ Clipper2Lib.dll' + #13#10;
    MissCount := MissCount + 1;
  end;

  if MissCount = 0 then
    Result := '✓ Step 1: 핵심 파일 ' + IntToStr(FileCount) + '개 확인 완료'
  else
    Result := '✗ Step 1: 파일 ' + IntToStr(MissCount) + '개 누락' + #13#10 + MissingFiles;
end;

// Step 2: appsettings.json 존재 및 내용 확인
function VerifyStep2_Config(const InstallDir: String): String;
var
  InstallSettings, UserSettings: String;
  Lines: TStringList;
  JsonContent: String;
  HasInstall, HasUser: Boolean;
begin
  InstallSettings := InstallDir + '\appsettings.json';
  UserSettings := ExpandConstant('{userappdata}') + '\URSUS\appsettings.json';

  HasInstall := FileExists(InstallSettings);
  HasUser := FileExists(UserSettings);

  if (not HasInstall) and (not HasUser) then
  begin
    Result := '⚠ Step 2: appsettings.json이 없습니다 (API 키 미설정)';
    Exit;
  end;

  // 설치 폴더 설정 파일 검증
  if HasInstall then
  begin
    Lines := TStringList.Create;
    try
      Lines.LoadFromFile(InstallSettings);
      JsonContent := Lines.Text;
      // 최소한의 JSON 구조 검증 — { 로 시작하는지
      if (Length(JsonContent) > 2) and (Pos('{', JsonContent) > 0) then
      begin
        if (Pos('"VWorldKey"', JsonContent) > 0) or (Pos('"SeoulKey"', JsonContent) > 0) then
          Result := '✓ Step 2: 설정 파일 유효 (설치 폴더)'
        else
          Result := '⚠ Step 2: 설정 파일 존재하나 API 키 항목 없음';
      end
      else
        Result := '✗ Step 2: 설정 파일 JSON 형식 오류';
    finally
      Lines.Free;
    end;
  end
  else if HasUser then
  begin
    Lines := TStringList.Create;
    try
      Lines.LoadFromFile(UserSettings);
      JsonContent := Lines.Text;
      if (Length(JsonContent) > 2) and (Pos('{', JsonContent) > 0) then
        Result := '✓ Step 2: 설정 파일 유효 (사용자 프로필)'
      else
        Result := '✗ Step 2: 설정 파일 JSON 형식 오류';
    finally
      Lines.Free;
    end;
  end;
end;

// Step 3: API 키 실제 존재 확인
function VerifyStep3_ApiKeys(const InstallDir: String): String;
var
  InstallSettings, UserSettings: String;
  Lines: TStringList;
  JsonContent: String;
  HasVW, HasSK: Boolean;
  VWKey, SKKey: String;
begin
  HasVW := False;
  HasSK := False;

  // 환경변수 확인
  if Length(GetEnv('URSUS_VWORLD_KEY')) > 0 then
    HasVW := True;
  if Length(GetEnv('URSUS_SEOUL_KEY')) > 0 then
    HasSK := True;

  // 설치 폴더 설정 파일 확인
  InstallSettings := InstallDir + '\appsettings.json';
  if FileExists(InstallSettings) then
  begin
    Lines := TStringList.Create;
    try
      Lines.LoadFromFile(InstallSettings);
      JsonContent := Lines.Text;
      VWKey := ExtractJsonValue(JsonContent, 'VWorldKey');
      SKKey := ExtractJsonValue(JsonContent, 'SeoulKey');
      if Length(VWKey) > 0 then HasVW := True;
      if Length(SKKey) > 0 then HasSK := True;
    finally
      Lines.Free;
    end;
  end;

  // 사용자 프로필 설정 파일 확인
  UserSettings := ExpandConstant('{userappdata}') + '\URSUS\appsettings.json';
  if FileExists(UserSettings) then
  begin
    Lines := TStringList.Create;
    try
      Lines.LoadFromFile(UserSettings);
      JsonContent := Lines.Text;
      VWKey := ExtractJsonValue(JsonContent, 'VWorldKey');
      SKKey := ExtractJsonValue(JsonContent, 'SeoulKey');
      if Length(VWKey) > 0 then HasVW := True;
      if Length(SKKey) > 0 then HasSK := True;
    finally
      Lines.Free;
    end;
  end;

  if HasVW and HasSK then
    Result := '✓ Step 3: API 키 2개 모두 확인됨 (VWorld, 서울 열린데이터)'
  else if HasVW then
    Result := '⚠ Step 3: VWorld 키만 확인됨 (서울 열린데이터 키 미설정)'
  else if HasSK then
    Result := '⚠ Step 3: 서울 열린데이터 키만 확인됨 (VWorld 키 미설정)'
  else
    Result := '⚠ Step 3: API 키가 설정되지 않았습니다 (GH에서 직접 입력 가능)';
end;

// 전체 3-Step 검증 실행
function RunPostInstallVerification(): String;
var
  InstallDir: String;
  S1, S2, S3: String;
begin
  InstallDir := ExpandConstant('{app}');
  S1 := VerifyStep1_Files(InstallDir);
  S2 := VerifyStep2_Config(InstallDir);
  S3 := VerifyStep3_ApiKeys(InstallDir);

  Result := '── URSUS 설치 검증 결과 ──' + #13#10 +
            #13#10 + S1 + #13#10 + S2 + #13#10 + S3;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  VWKey, SKKey: String;
  VerifyResult: String;
  HasErrors: Boolean;
begin
  if CurStep = ssPostInstall then
  begin
    // 1. API 키 저장
    SaveApiKeys();

    // 2. Remove Zone.Identifier ADS from all installed assemblies
    UnblockInstalledFiles();

    // 3. Post-install 3-Step 검증
    VerifyResult := RunPostInstallVerification();
    HasErrors := Pos('✗ Step 1', VerifyResult) > 0;

    // 4. 검증 결과 + 다음 단계 안내 메시지
    VWKey := TrimKey(VWorldKeyEdit.Text);
    SKKey := TrimKey(SeoulKeyEdit.Text);

    if HasErrors then
    begin
      MsgBox(VerifyResult + #13#10 + #13#10 +
             '⚠ 설치에 문제가 있습니다.' + #13#10 +
             '설치 프로그램을 다시 실행하거나,' + #13#10 +
             '파일을 수동으로 복사해주세요.' + #13#10 + #13#10 +
             '설치 폴더: ' + ExpandConstant('{app}'),
             mbError, MB_OK);
    end
    else
    begin
      MsgBox(VerifyResult + #13#10 + #13#10 +
             '── 사용 방법 (3단계) ──' + #13#10 +
             '1. Rhino 8을 실행하고 Grasshopper를 엽니다.' + #13#10 +
             '2. URSUS 탭에서 Solver 컴포넌트를 찾습니다.' + #13#10 +
             '3. 캔버스에 놓으면 바로 실행됩니다!' + #13#10 + #13#10 +
             '※ Rhino가 이미 실행 중이면 재시작이 필요합니다.',
             mbInformation, MB_OK);
    end;
  end;
end;

// =========================================================================
//  Uninstall: appsettings.json in user profile is kept intentionally
//  (사용자 설정은 제거하지 않음 — 재설치 시 복원 가능)
// =========================================================================
