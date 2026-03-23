# URSUS → .gha 플러그인 + .exe 인스톨러 빌드 가이드

---

## 개요

현재 URSUS 레포는 이런 구조다:

```
URSUS.sln
└── src/URSUS/URSUS.csproj     ← .NET 클래스 라이브러리 (로직 전체)
```

GH Script에서 `#r "URSUS.dll"`로 참조하는 방식이다.
이것을 아래 구조로 바꾼다:

```
URSUS.sln
├── src/URSUS/URSUS.csproj           ← 기존 로직 라이브러리 (변경 없음)
├── src/URSUS.GH/URSUS.GH.csproj    ← 신규: .gha 플러그인 프로젝트
└── src/URSUS.Setup/URSUS.Setup.csproj  ← 신규: .exe 설정/설치 도구
```

최종 결과물:
- `URSUS.GH.gha` + `URSUS.dll` + 의존성 DLL → Grasshopper 플러그인
- `URSUS.Setup.exe` → API 키 입력 + 파일 배치를 자동화하는 설치 도구

---

## Phase 1: .gha 플러그인 프로젝트 생성

### 1-1. 프로젝트 폴더 및 .csproj 생성

```bash
mkdir -p src/URSUS.GH
```

`src/URSUS.GH/URSUS.GH.csproj` 파일을 만든다:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net7.0</TargetFramework>
    <Nullable>enable</Nullable>

    <!-- 핵심: 빌드 출력 확장자를 .gha로 변경 -->
    <TargetExt>.gha</TargetExt>

    <!-- Grasshopper가 DLL을 동적 로드하므로 필요 -->
    <EnableDynamicLoading>true</EnableDynamicLoading>

    <!-- 빌드 결과를 솔루션 루트의 bin/ 폴더로 모은다 -->
    <OutputPath>..\..\bin\$(Configuration)\</OutputPath>
    <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
  </PropertyGroup>

  <ItemGroup>
    <!-- Grasshopper NuGet: 런타임은 Rhino가 제공하므로 제외 -->
    <PackageReference Include="Grasshopper"
                      Version="8.*"
                      ExcludeAssets="runtime" />

    <!-- 기존 로직 라이브러리 참조 -->
    <ProjectReference Include="..\URSUS\URSUS.csproj" />
  </ItemGroup>

</Project>
```

### 1-2. 솔루션에 프로젝트 등록

솔루션 루트에서:

```bash
dotnet sln URSUS.sln add src/URSUS.GH/URSUS.GH.csproj
```

### 1-3. GH_AssemblyInfo 작성

`src/URSUS.GH/URSUSInfo.cs`:

```csharp
using System;
using System.Drawing;
using Grasshopper.Kernel;

namespace URSUS.GH
{
    public class URSUSInfo : GH_AssemblyInfo
    {
        // Grasshopper 플러그인 탭에 표시되는 정보
        public override string Name        => "URSUS";
        public override string Description => "Urban Research with Spatial Utility System";
        public override string Version     => "1.0.0";
        public override string AuthorName  => "TeacherChae";
        public override string AuthorContact => "https://github.com/TeacherChae/URSUS";

        // ⚠️ 한번 정하면 절대 바꾸지 않는다
        public override Guid Id => new Guid("여기에-GUID-생성해서-넣기");

        // 24×24 아이콘 (없으면 null 반환, 기본 아이콘 사용됨)
        public override Bitmap Icon => null;
    }
}
```

> **GUID 생성:** 터미널에서 `python3 -c "import uuid; print(uuid.uuid4())"` 실행해서
> 나온 값을 넣는다. URSUSInfo, 각 컴포넌트마다 별도 GUID가 필요하다.

### 1-4. URSUSSolver 컴포넌트 작성

`src/URSUS.GH/URSUSSolverComponent.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Grasshopper.Kernel;

namespace URSUS.GH
{
    public class URSUSSolverComponent : GH_Component
    {
        public URSUSSolverComponent()
            : base(
                "URSUS Solver",           // 컴포넌트 이름
                "Solver",                 // 닉네임 (컴포넌트 축소 시 표시)
                "법정동 경계 + 소득 데이터 수집 파이프라인",
                "URSUS",                  // 탭 카테고리
                "Data")                   // 서브 카테고리
        { }

        // ⚠️ 절대 변경 금지
        public override Guid ComponentGuid
            => new Guid("여기에-별도-GUID");

        // ────────────────────────────────────────
        //  입력 파라미터
        // ────────────────────────────────────────
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            // API 키는 appsettings.json에서 읽으므로 선택적 입력
            // (입력이 있으면 우선 사용, 없으면 설정 파일에서 읽음)
            pManager.AddTextParameter("VWorld Key", "VK",
                "VWorld API 키 (미입력 시 설정 파일 사용)",
                GH_ParamAccess.item);
            pManager[0].Optional = true;

            pManager.AddTextParameter("Seoul Key", "SK",
                "서울 열린데이터 API 키 (미입력 시 설정 파일 사용)",
                GH_ParamAccess.item);
            pManager[1].Optional = true;

            // URSUSSolver.DS_* 상수와 일치하는 데이터셋 이름 목록
            // 예: "월평균 소득", "상주인구", "대중교통 총 승차 승객 수(일일 평균)"
            pManager.AddTextParameter("DataSet", "DS",
                "사용할 데이터셋 이름 목록 (URSUSSolver.DS_* 상수 참고)",
                GH_ParamAccess.list);
        }

        // ────────────────────────────────────────
        //  출력 파라미터
        // ────────────────────────────────────────
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Legal Codes", "LC",
                "법정동 코드", GH_ParamAccess.list);
            pManager.AddTextParameter("Names", "N",
                "법정동 이름", GH_ParamAccess.list);
            pManager.AddCurveParameter("Geometries", "G",
                "법정동 경계 커브", GH_ParamAccess.list);
            pManager.AddPointParameter("Centroids", "C",
                "법정동 중심점", GH_ParamAccess.list);
            pManager.AddNumberParameter("Areas", "A",
                "법정동 면적 (m²)", GH_ParamAccess.list);
            pManager.AddNumberParameter("Values", "V",
                "Weighted overlay 값 (0~1 정규화)", GH_ParamAccess.list);
            pManager.AddCurveParameter("Union Boundary", "U",
                "전체 외곽선", GH_ParamAccess.item);
        }

        // ────────────────────────────────────────
        //  실행 로직
        // ────────────────────────────────────────
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // API 키: 입력 → appsettings.json → 에러 순으로 폴백
            string vk = "", sk = "";
            DA.GetData(0, ref vk);
            DA.GetData(1, ref sk);

            var dataSet = new List<string>();
            if (!DA.GetDataList(2, dataSet)) return;

            var config = LoadConfig();

            if (string.IsNullOrEmpty(vk)) vk = config.VWorldKey;
            if (string.IsNullOrEmpty(sk)) sk = config.SeoulKey;

            if (string.IsNullOrEmpty(vk) || string.IsNullOrEmpty(sk))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "API 키가 설정되지 않았습니다. " +
                    "URSUS.Setup.exe를 실행하여 API 키를 설정하거나, " +
                    "VK/SK 입력에 직접 연결하세요.");
                return;
            }

            try
            {
                // ── 기존 라이브러리 호출 ──
                var solver = new URSUSSolver(vk, sk);
                var result = solver.Run(dataSet);

                DA.SetDataList(0, result.LegalCodes);
                DA.SetDataList(1, result.Names);
                DA.SetDataList(2, result.Geometries);
                DA.SetDataList(3, result.Centroids);
                DA.SetDataList(4, result.Areas);
                DA.SetDataList(5, result.Values);
                DA.SetData(6, result.UnionBoundary);
            }
            catch (Exception ex)
            {
                // 로컬 로그 저장
                LogError(ex);
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"데이터 수집 중 오류가 발생했습니다: {ex.Message}");
            }
        }

        // ────────────────────────────────────────
        //  헬퍼: appsettings.json 읽기
        // ────────────────────────────────────────
        private AppSettings LoadConfig()
        {
            try
            {
                // .gha가 있는 폴더에서 appsettings.json을 찾는다
                string dir = Path.GetDirectoryName(
                    typeof(URSUSSolverComponent).Assembly.Location)!;
                string path = Path.Combine(dir, "appsettings.json");

                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    return JsonSerializer.Deserialize<AppSettings>(json)
                           ?? new AppSettings();
                }
            }
            catch { /* 설정 파일 읽기 실패 시 빈 설정 반환 */ }

            return new AppSettings();
        }

        // ────────────────────────────────────────
        //  헬퍼: 에러 로깅
        // ────────────────────────────────────────
        private void LogError(Exception ex)
        {
            try
            {
                string logDir = Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.ApplicationData),
                    "URSUS", "logs");
                Directory.CreateDirectory(logDir);

                string logFile = Path.Combine(logDir,
                    $"error_{DateTime.Now:yyyyMMdd_HHmmss}.log");

                File.WriteAllText(logFile,
                    $"Time: {DateTime.Now}\n" +
                    $"Version: {typeof(URSUSSolverComponent).Assembly.GetName().Version}\n" +
                    $"Exception: {ex.GetType().Name}\n" +
                    $"Message: {ex.Message}\n" +
                    $"StackTrace:\n{ex.StackTrace}\n");
            }
            catch { /* 로깅 실패는 무시 */ }
        }
    }

    // ────────────────────────────────────────
    //  설정 모델
    // ────────────────────────────────────────
    public class AppSettings
    {
        public string VWorldKey { get; set; } = "";
        public string SeoulKey { get; set; } = "";
        public string CacheDir { get; set; } = "";
        // 참고: CacheDir, MappingJsonPath는 URSUSSolver 내부에서 DLL 위치 기준으로
        // 자동 설정되므로 appsettings.json에 없어도 동작한다.
        public string MappingJsonPath { get; set; } = "";
    }
}
```

### 1-5. Visualizer 컴포넌트 작성

`src/URSUS.GH/VisualizerComponent.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Drawing;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace URSUS.GH
{
    public class VisualizerComponent : GH_Component
    {
        public VisualizerComponent()
            : base(
                "URSUS Visualizer", "Viz",
                "IDW 공간 보간 시각화",
                "URSUS", "Visualization")
        { }

        public override Guid ComponentGuid
            => new Guid("여기에-별도-GUID");

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddCurveParameter("Boundary", "B",
                "전체 외곽선 (SolverResult.UnionBoundary)", GH_ParamAccess.item);
            pManager.AddPointParameter("Centroids", "C",
                "법정동 중심점", GH_ParamAccess.list);
            pManager.AddNumberParameter("Values", "V",
                "보간할 값 (SolverResult.Values)", GH_ParamAccess.list);
            pManager.AddNumberParameter("Resolution", "R",
                "메시 최대 엣지 길이", GH_ParamAccess.item, 100.0);
            pManager.AddNumberParameter("Power", "P",
                "IDW 지수", GH_ParamAccess.item, 2.5);
            pManager.AddNumberParameter("Height Scale", "HS",
                "Z 높이 배율", GH_ParamAccess.item, 0.5);
            pManager.AddNumberParameter("Height Ratio", "HR",
                "Z 최대 높이 = bboxWidth × HR", GH_ParamAccess.item, 0.5);
            pManager.AddIntegerParameter("Legend Steps", "LS",
                "범례 단계 수", GH_ParamAccess.item, 8);
            pManager.AddIntegerParameter("Color Style", "CS",
                "0=Custom 1=BlueRed 2=Heatmap 3=Spectral 4=Viridis 5=Diverging 6=Grayscale",
                GH_ParamAccess.item, 4);
            pManager.AddColourParameter("Color Low", "CL",
                "colorStyle=0일 때 최솟값 색상",
                GH_ParamAccess.item, Color.FromArgb(44, 123, 182));
            pManager.AddColourParameter("Color High", "CH",
                "colorStyle=0일 때 최댓값 색상",
                GH_ParamAccess.item, Color.FromArgb(215, 25, 28));
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddMeshParameter("Mesh", "M",
                "Z 변위 + 컬러 메시", GH_ParamAccess.item);
            pManager.AddMeshParameter("Flat Mesh", "FM",
                "Z=0 컬러 메시 (지도 오버레이용)", GH_ParamAccess.item);
            pManager.AddNumberParameter("Min Value", "Min",
                "범례 최솟값", GH_ParamAccess.item);
            pManager.AddNumberParameter("Max Value", "Max",
                "범례 최댓값", GH_ParamAccess.item);
            pManager.AddMeshParameter("Legend Mesh", "LM",
                "범례 그라디언트 바", GH_ParamAccess.item);
            pManager.AddGenericParameter("Legend Dots", "LD",
                "범례 값 레이블 (TextDot 리스트)", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // 입력 읽기
            Curve boundary = null;
            var centroids = new List<Point3d>();
            var values = new List<double>();
            double resolution = 100, power = 2.5, heightScale = 0.5;
            double heightRatio = 0.5;
            int legendSteps = 8, colorStyle = 4;
            Color colorLow = Color.FromArgb(44, 123, 182);
            Color colorHigh = Color.FromArgb(215, 25, 28);

            if (!DA.GetData(0, ref boundary)) return;
            if (!DA.GetDataList(1, centroids)) return;
            if (!DA.GetDataList(2, values)) return;
            DA.GetData(3, ref resolution);
            DA.GetData(4, ref power);
            DA.GetData(5, ref heightScale);
            DA.GetData(6, ref heightRatio);
            DA.GetData(7, ref legendSteps);
            DA.GetData(8, ref colorStyle);
            DA.GetData(9, ref colorLow);
            DA.GetData(10, ref colorHigh);

            try
            {
                // ── 기존 라이브러리 호출 ──
                // 모든 파라미터는 생성자에 전달, Build()는 boundary만 받음
                var viz = new IDWVisualizer(
                    centroids, values,
                    resolution, power, heightScale,
                    heightRatio, legendSteps, colorStyle,
                    colorLow, colorHigh);
                var result = viz.Build(boundary);

                DA.SetData(0, result.Mesh);
                DA.SetData(1, result.FlatMesh);
                DA.SetData(2, result.MinVal);
                DA.SetData(3, result.MaxVal);
                DA.SetData(4, result.LegendMesh);
                DA.SetDataList(5, result.LegendDots);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"시각화 중 오류: {ex.Message}");
            }
        }
    }
}
```

### 1-6. 빌드 확인

```bash
cd /home/keonheechae/URSUS
dotnet build src/URSUS.GH -c Release
```

성공하면 `bin/Release/` 폴더에 다음 파일들이 생긴다:

```
bin/Release/
├── URSUS.GH.gha          ← Grasshopper 플러그인
├── URSUS.dll              ← 로직 라이브러리
├── Clipper2Lib.dll        ← 의존성
└── (기타 의존성 DLL들)
```

### 1-7. 수동 테스트

빌드된 파일들을 Grasshopper Libraries 폴더에 복사한다:

```
%AppData%\Grasshopper\Libraries\
    URSUS.GH.gha
    URSUS.dll
    Clipper2Lib.dll
```

Rhino를 재시작하면 Grasshopper 탭에 "URSUS" 카테고리가 나타난다.
Solver와 Visualizer 컴포넌트를 캔버스에 드래그해서 동작을 확인한다.

> **주의:** `URSUS.GH.gha` 파일이 "차단"될 수 있다.
> 파일 우클릭 → 속성 → 하단의 "차단 해제" 체크 → 확인.
> 모든 .gha, .dll 파일에 대해 해줘야 한다.

---

## Phase 2: .exe 설치 도구 프로젝트 생성

### 2-1. 프로젝트 생성

```bash
mkdir -p src/URSUS.Setup
```

WinForms 프로젝트로 만든다.
`src/URSUS.Setup/URSUS.Setup.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net7.0-windows</TargetFramework>
    <UseWindowsForms>true</UseWindowsForms>
    <Nullable>enable</Nullable>

    <!-- 단일 .exe로 배포 (자체 포함) -->
    <PublishSingleFile>true</PublishSingleFile>
    <SelfContained>true</SelfContained>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>

    <!-- 선택: .NET 런타임 없는 환경에서도 실행 가능하게 -->
    <IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
  </PropertyGroup>

</Project>
```

솔루션에 등록:

```bash
dotnet sln URSUS.sln add src/URSUS.Setup/URSUS.Setup.csproj
```

### 2-2. 설치 도구 메인 폼

`src/URSUS.Setup/MainForm.cs`:

```csharp
using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows.Forms;

namespace URSUS.Setup
{
    public class MainForm : Form
    {
        // ── UI 컨트롤 ──
        private TextBox txtVWorldKey;
        private TextBox txtSeoulKey;
        private TextBox txtInstallPath;
        private Button btnBrowse;
        private Button btnInstall;
        private Label lblStatus;

        public MainForm()
        {
            InitializeUI();
        }

        private void InitializeUI()
        {
            // ── 폼 기본 설정 ──
            Text = "URSUS 설치";
            Size = new System.Drawing.Size(520, 420);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;

            int y = 20;
            int labelWidth = 140;
            int inputLeft = 150;
            int inputWidth = 330;

            // ── 설치 경로 ──
            AddLabel("설치 경로:", 10, y);
            txtInstallPath = new TextBox
            {
                Location = new System.Drawing.Point(inputLeft, y),
                Size = new System.Drawing.Size(inputWidth - 40, 23),
                Text = GetDefaultGHLibrariesPath()
            };
            Controls.Add(txtInstallPath);

            btnBrowse = new Button
            {
                Text = "...",
                Location = new System.Drawing.Point(inputLeft + inputWidth - 35, y),
                Size = new System.Drawing.Size(35, 23)
            };
            btnBrowse.Click += BtnBrowse_Click;
            Controls.Add(btnBrowse);

            y += 50;

            // ── 구분선 + 설명 ──
            var lblSection = new Label
            {
                Text = "API 키 설정 (나중에 변경 가능)",
                Location = new System.Drawing.Point(10, y),
                Size = new System.Drawing.Size(480, 20),
                Font = new System.Drawing.Font(Font, System.Drawing.FontStyle.Bold)
            };
            Controls.Add(lblSection);
            y += 30;

            // ── VWorld API 키 ──
            AddLabel("VWorld API 키:", 10, y);
            txtVWorldKey = new TextBox
            {
                Location = new System.Drawing.Point(inputLeft, y),
                Size = new System.Drawing.Size(inputWidth, 23),
                PlaceholderText = "https://www.vworld.kr 에서 발급"
            };
            Controls.Add(txtVWorldKey);
            y += 35;

            // ── Seoul API 키 ──
            AddLabel("서울 열린데이터 키:", 10, y);
            txtSeoulKey = new TextBox
            {
                Location = new System.Drawing.Point(inputLeft, y),
                Size = new System.Drawing.Size(inputWidth, 23),
                PlaceholderText = "https://data.seoul.go.kr 에서 발급"
            };
            Controls.Add(txtSeoulKey);
            y += 50;

            // ── 설치 버튼 ──
            btnInstall = new Button
            {
                Text = "설치",
                Location = new System.Drawing.Point(inputLeft + inputWidth - 100, y),
                Size = new System.Drawing.Size(100, 35),
                Font = new System.Drawing.Font(Font.FontFamily, 10f)
            };
            btnInstall.Click += BtnInstall_Click;
            Controls.Add(btnInstall);
            y += 50;

            // ── 상태 표시 ──
            lblStatus = new Label
            {
                Location = new System.Drawing.Point(10, y),
                Size = new System.Drawing.Size(480, 80),
                ForeColor = System.Drawing.Color.DarkGray
            };
            Controls.Add(lblStatus);
        }

        // ────────────────────────────────────────
        //  이벤트 핸들러
        // ────────────────────────────────────────

        private void BtnBrowse_Click(object? sender, EventArgs e)
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "Grasshopper Libraries 폴더를 선택하세요",
                SelectedPath = txtInstallPath.Text
            };

            if (dialog.ShowDialog() == DialogResult.OK)
                txtInstallPath.Text = dialog.SelectedPath;
        }

        private void BtnInstall_Click(object? sender, EventArgs e)
        {
            string installDir = txtInstallPath.Text.Trim();

            // ── 1. 경로 유효성 검사 ──
            if (string.IsNullOrEmpty(installDir))
            {
                ShowError("설치 경로를 지정하세요.");
                return;
            }

            try
            {
                Directory.CreateDirectory(installDir);
            }
            catch (Exception ex)
            {
                ShowError($"폴더를 만들 수 없습니다: {ex.Message}");
                return;
            }

            // ── 2. 플러그인 파일 복사 ──
            try
            {
                lblStatus.Text = "파일 복사 중...";
                lblStatus.ForeColor = System.Drawing.Color.DarkGray;
                Application.DoEvents();

                // Setup.exe와 같은 폴더에 있는 플러그인 파일들을 복사
                string sourceDir = Path.GetDirectoryName(
                    Assembly.GetExecutingAssembly().Location)!;

                string[] filesToCopy = new[]
                {
                    "URSUS.GH.gha",
                    "URSUS.dll",
                    "Clipper2Lib.dll"
                    // 다른 의존성 DLL이 있으면 여기에 추가
                };

                foreach (string file in filesToCopy)
                {
                    string src = Path.Combine(sourceDir, file);
                    string dst = Path.Combine(installDir, file);

                    if (File.Exists(src))
                    {
                        File.Copy(src, dst, overwrite: true);

                        // Windows 보안 차단 해제
                        // (Zone.Identifier ADS 제거)
                        UnblockFile(dst);
                    }
                    else
                    {
                        ShowError($"파일을 찾을 수 없습니다: {file}\n" +
                                  $"찾은 경로: {src}");
                        return;
                    }
                }

                // ── 3. appsettings.json 생성 ──
                var settings = new
                {
                    VWorldKey = txtVWorldKey.Text.Trim(),
                    SeoulKey = txtSeoulKey.Text.Trim(),
                    CacheDir = Path.Combine(
                        Environment.GetFolderPath(
                            Environment.SpecialFolder.ApplicationData),
                        "URSUS", "cache"),
                    MappingJsonPath = Path.Combine(
                        installDir, "adstrd_legald_mapping.json")
                };

                string json = JsonSerializer.Serialize(settings,
                    new JsonSerializerOptions { WriteIndented = true });

                File.WriteAllText(
                    Path.Combine(installDir, "appsettings.json"), json);

                // ── 4. 캐시 폴더 생성 ──
                Directory.CreateDirectory(settings.CacheDir);

                // ── 5. 완료 ──
                lblStatus.Text =
                    $"✓ 설치 완료!\n\n" +
                    $"설치 위치: {installDir}\n" +
                    $"Rhino를 재시작하면 Grasshopper에 URSUS 탭이 나타납니다.";
                lblStatus.ForeColor = System.Drawing.Color.DarkGreen;

                MessageBox.Show(
                    "설치가 완료되었습니다.\nRhino를 재시작해주세요.",
                    "URSUS 설치", MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                ShowError($"설치 중 오류: {ex.Message}");
            }
        }

        // ────────────────────────────────────────
        //  유틸리티
        // ────────────────────────────────────────

        private static string GetDefaultGHLibrariesPath()
        {
            // Grasshopper 기본 Libraries 경로
            return Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.ApplicationData),
                "Grasshopper", "Libraries");
        }

        private static void UnblockFile(string filePath)
        {
            // 인터넷에서 다운받은 파일에 붙는 Zone.Identifier 제거
            // 이게 있으면 Grasshopper가 .gha 로드를 거부할 수 있음
            try
            {
                string adsPath = filePath + ":Zone.Identifier";
                if (File.Exists(adsPath))
                    File.Delete(adsPath);
            }
            catch { /* ADS 삭제 실패는 무시 */ }
        }

        private void AddLabel(string text, int x, int y)
        {
            Controls.Add(new Label
            {
                Text = text,
                Location = new System.Drawing.Point(x, y),
                Size = new System.Drawing.Size(135, 23),
                TextAlign = System.Drawing.ContentAlignment.MiddleRight
            });
        }

        private void ShowError(string message)
        {
            lblStatus.Text = message;
            lblStatus.ForeColor = System.Drawing.Color.DarkRed;
        }
    }
}
```

### 2-3. Program.cs (엔트리 포인트)

`src/URSUS.Setup/Program.cs`:

```csharp
using System;
using System.Windows.Forms;

namespace URSUS.Setup
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
```

### 2-4. 빌드 및 퍼블리시

개발 중 빌드:

```bash
dotnet build src/URSUS.Setup -c Release
```

배포용 단일 .exe 생성:

```bash
dotnet publish src/URSUS.Setup -c Release \
    -r win-x64 \
    --self-contained true \
    -p:PublishSingleFile=true \
    -o publish/
```

`publish/URSUS.Setup.exe` 하나가 나온다.
(.NET 런타임 없는 PC에서도 실행 가능)

---

## Phase 3: 배포 패키지 구성

### 3-1. 최종 배포 폴더 구성

배포할 때 사용자에게 전달하는 폴더(또는 ZIP) 구조:

```
URSUS-v1.0.0/
├── URSUS.Setup.exe            ← 사용자가 실행하는 유일한 파일
├── URSUS.GH.gha               ← Setup이 복사할 파일들
├── URSUS.dll                   │
├── Clipper2Lib.dll             │
├── adstrd_legald_mapping.json  ← KIKmix에서 변환된 매핑 파일
└── README.txt                  ← 간단한 안내
```

### 3-2. 배포 자동화 스크립트

매번 수동으로 파일을 모으기 귀찮으니, 빌드 후 배포 폴더를 자동 구성하는
스크립트를 만든다.

`scripts/make_release.sh` (WSL용):

```bash
#!/bin/bash
set -e

VERSION="1.0.0"
RELEASE_DIR="release/URSUS-v${VERSION}"

echo "=== URSUS v${VERSION} 릴리스 빌드 ==="

# 1. 클린 빌드
dotnet build src/URSUS.GH -c Release
echo "✓ GHA 빌드 완료"

# 2. Setup.exe 퍼블리시
dotnet publish src/URSUS.Setup -c Release \
    -r win-x64 --self-contained true \
    -p:PublishSingleFile=true \
    -o publish/
echo "✓ Setup.exe 퍼블리시 완료"

# 3. 릴리스 폴더 구성
rm -rf "$RELEASE_DIR"
mkdir -p "$RELEASE_DIR"

# GHA 플러그인 파일들
cp bin/Release/URSUS.GH.gha    "$RELEASE_DIR/"
cp bin/Release/URSUS.dll        "$RELEASE_DIR/"
cp bin/Release/Clipper2Lib.dll  "$RELEASE_DIR/"

# Setup.exe
cp publish/URSUS.Setup.exe      "$RELEASE_DIR/"

# 매핑 파일 (이미 생성되어 있다고 가정)
if [ -f "src/cache/adstrd_legald_mapping.json" ]; then
    cp src/cache/adstrd_legald_mapping.json "$RELEASE_DIR/"
fi

echo "✓ 릴리스 폴더 구성 완료: $RELEASE_DIR"
echo ""
ls -la "$RELEASE_DIR"

# 4. ZIP 생성
cd release
zip -r "URSUS-v${VERSION}.zip" "URSUS-v${VERSION}/"
echo ""
echo "✓ 배포 파일 생성: release/URSUS-v${VERSION}.zip"
```

### 3-3. README.txt (사용자용)

```
URSUS - Urban Research with Spatial Utility System
===================================================

설치 방법:
1. URSUS.Setup.exe를 실행합니다.
2. 설치 경로가 맞는지 확인합니다 (보통 자동으로 잡힙니다).
3. VWorld API 키와 서울 열린데이터 API 키를 입력합니다.
   - VWorld 키: https://www.vworld.kr 에서 회원가입 후 발급
   - 서울 키: https://data.seoul.go.kr 에서 회원가입 후 발급
   - 키가 아직 없으면 비워두고 나중에 Grasshopper에서 직접 입력할 수 있습니다.
4. "설치" 버튼을 클릭합니다.
5. Rhino를 재시작합니다.
6. Grasshopper를 열면 "URSUS" 탭이 나타납니다.

API 키 변경:
- URSUS.Setup.exe를 다시 실행하면 됩니다.
- 또는 Grasshopper Libraries 폴더의 appsettings.json을
  텍스트 편집기로 직접 수정할 수 있습니다.

문의:
- 이메일: (연락처)
```

---

## Phase 4: 솔루션 최종 구조 확인

모든 작업이 끝나면 레포 구조는 이렇게 된다:

```
URSUS/
├── URSUS.sln
│
├── src/
│   ├── URSUS/                          ← 기존 로직 라이브러리
│   │   ├── URSUS.csproj
│   │   ├── URSUSSolver.cs
│   │   ├── Parsers/
│   │   │   ├── VworldApiParser.cs
│   │   │   ├── DataSeoulApiParser.cs
│   │   │   └── MappingLoader.cs
│   │   ├── GeoOps/
│   │   │   └── Union.cs
│   │   ├── Visualization/
│   │   │   └── IDWVisualizer.cs
│   │   └── Utils/
│   │       ├── GpsToUtm.cs
│   │       └── XlsxToJson.cs
│   │
│   ├── URSUS.GH/                       ← 신규: GHA 플러그인
│   │   ├── URSUS.GH.csproj
│   │   ├── URSUSInfo.cs
│   │   ├── URSUSSolverComponent.cs
│   │   └── VisualizerComponent.cs
│   │
│   └── URSUS.Setup/                    ← 신규: 설치 도구
│       ├── URSUS.Setup.csproj
│       ├── Program.cs
│       └── MainForm.cs
│
├── works/URSUS/                        ← 기존 GH Script (폐기 예정)
│   ├── URSUSSolver_GH.cs
│   ├── Visualizer_GH.cs
│   ├── GeoUnion.cs
│   └── IDWVisualizer.cs
│
├── scripts/
│   └── make_release.sh
│
├── docs/
├── refs/
├── GH_SETUP_GUIDE.md                  ← 업데이트 필요
├── MIGRATION_PLAN.md                   ← 업데이트 필요
└── .gitignore                          ← bin/, publish/, release/ 추가
```

---

## 체크리스트

### Phase 1 — .gha 프로젝트
- [ ] `src/URSUS.GH/URSUS.GH.csproj` 생성
- [ ] `dotnet sln add` 로 솔루션에 등록
- [ ] `URSUSInfo.cs` 작성 (GUID 생성)
- [ ] `URSUSSolverComponent.cs` 작성 (GUID 생성)
- [ ] `VisualizerComponent.cs` 작성 (GUID 생성)
- [ ] `AppSettings` 클래스 및 `LoadConfig()` 구현
- [ ] 에러 로깅 (`LogError()`) 구현
- [ ] `dotnet build` 성공 확인
- [ ] Libraries 폴더에 수동 복사 후 Grasshopper에서 동작 확인
- [ ] 파일 차단 해제 확인

### Phase 2 — .exe 설치 도구
- [ ] `src/URSUS.Setup/URSUS.Setup.csproj` 생성
- [ ] `MainForm.cs` 작성 (UI + 설치 로직)
- [ ] `Program.cs` 작성
- [ ] `dotnet publish`로 단일 .exe 생성 확인
- [ ] 실제 PC에서 Setup.exe 실행 → 파일 복사 → Grasshopper 로드 확인

### Phase 3 — 배포
- [ ] `scripts/make_release.sh` 작성
- [ ] `README.txt` 작성
- [ ] `.gitignore`에 `bin/`, `publish/`, `release/` 추가
- [ ] ZIP으로 묶어서 배포 테스트

### 기존 파일 정리
- [ ] `works/URSUS/` 내 GH Script 파일들 → 폐기 표시 또는 삭제
- [ ] `GH_SETUP_GUIDE.md` → .gha 기준으로 업데이트
- [ ] `MIGRATION_PLAN.md` → 완료된 항목 체크