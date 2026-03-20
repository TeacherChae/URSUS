# URSUS.dll — Grasshopper 연동 가이드

## 목차

1. [전체 구조 이해](#1-전체-구조-이해)
2. [사전 준비 (1회)](#2-사전-준비-1회)
3. [DLL 참조 방법](#3-dll-참조-방법)
4. [GH Definition 구성](#4-gh-definition-구성)
5. [URSUSSolver_GH 컴포넌트 설정](#5-urssussolver_gh-컴포넌트-설정)
6. [Visualizer_GH 컴포넌트 설정](#6-visualizer_gh-컴포넌트-설정)
7. [입력값 레퍼런스](#7-입력값-레퍼런스)
8. [캐시 동작 방식](#8-캐시-동작-방식)
9. [트러블슈팅](#9-트러블슈팅)

---

## 1. 전체 구조 이해

```
┌──────────────────────────────────────────────────────────┐
│  GH Definition                                           │
│                                                          │
│  [URSUSSolver_GH]          [Visualizer_GH]              │
│   address1/2 입력           boundary 입력                │
│   API 키 입력               centroids/values 입력        │
│   → API 호출 + 캐시          colorStyle 등 파라미터       │
│   → SolverResult 출력        → Mesh + 범례 출력           │
└──────────┬───────────────────────┬───────────────────────┘
           │ URSUS.dll             │ URSUS.dll
┌──────────▼───────────────────────▼───────────────────────┐
│  URSUS.dll (C# 클래스 라이브러리)                         │
│                                                          │
│  URSUSSolver                IDWVisualizer               │
│  VworldApiParser            SpatialField                │
│  DataSeoulApiParser         ColorMapper                 │
│  MappingLoader              MeshBuilder                 │
│  GeoOps.Union               LegendBuilder               │
│  GpsToUtm                                               │
└──────────────────────────────────────────────────────────┘
```

**왜 2개의 GH 컴포넌트로 분리하는가?**

Grasshopper는 입력이 바뀌면 해당 컴포넌트를 처음부터 재실행한다.
Solver와 Visualizer를 분리하면:

| 바뀐 것 | 재실행되는 컴포넌트 | API 호출 여부 |
|---------|-------------------|--------------|
| `address1`, `address2` | URSUSSolver_GH + Visualizer_GH | 있음 |
| `colorStyle`, `resolution` | Visualizer_GH만 | 없음 |
| `power`, `heightScale` | Visualizer_GH만 | 없음 |

시각화 파라미터만 조정할 때 느린 API 호출을 건너뛸 수 있다.

---

## 2. 사전 준비 (1회)

### 2-1. URSUS.dll 빌드

WSL 터미널에서:

```bash
cd /home/keonchae/URSUS/src/URSUS
dotnet build -c Release
```

빌드 성공 시 `/home/keonchae/URSUS/bin/URSUS.dll` 생성됨.

### 2-2. KIKmix.xlsx → adstrd_legald_mapping.json 변환

이 작업은 **1회만** 수행하면 된다. 이후에는 파일을 재사용한다.

**방법 A — GH Script 컴포넌트로 실행 (권장)**

GH에 C# Script 컴포넌트를 추가한다. 기존 틀을 그대로 두고 **3곳만 수정**한다.

**수정 1 — `#r` 지시자를 파일 맨 위에 추가**

```csharp
#r "\\wsl.localhost\Ubuntu\home\keonchae\URSUS\bin\URSUS.dll"

// Grasshopper Script Instance
#region Usings
...
```

**수정 2 — `#region Usings` 안에 using 추가**

```csharp
#region Usings
// ... 기존 using들 그대로 유지 ...
using URSUS.Utils;    // ← 이 줄만 추가
#endregion
```

**수정 3 — `RunScript` 내용 교체**

파라미터는 에디터가 아닌 **컴포넌트 좌우 `+/-` 버튼**으로 수정한다.
기본 `x (object)` 입력을 삭제하고 `run (bool)` 입력을 추가.
기본 `a (object)` 출력은 그대로 유지.

```csharp
private void RunScript(bool run, ref object a)
{
    if (!run) return;
    int count = XlsxToJson.Convert(
        @"C:\Users\brian\JAH\src\sheets\KIKmix.20240201.xlsx",
        @"C:\Users\brian\Desktop\adstrd_legald_mapping.json");
    a = $"변환 완료: {count}건";
}
```

Button 컴포넌트를 `run`에 연결한 뒤 **한 번만** 클릭한다.
완료 후 이 컴포넌트는 삭제해도 된다.

**방법 B — 기존 Python 결과 그대로 사용**

기존 `adstrd_cd_to_legald_cd.py`로 이미 변환한 파일이 있다면 그대로 사용 가능하다.
파일 형식:

```json
[
  { "adstrd_cd": "11110515", "legald_cd": "11110101" },
  ...
]
```

> **주의:** `adstrd_cd`와 `legald_cd` 모두 8자리여야 한다.
> 원본 Python에서 `str[:-2]`로 10자리 → 8자리 변환을 적용했는지 확인할 것.

---

## 3. DLL 참조 방법

GH Script 컴포넌트에서 URSUS.dll을 참조하는 방법은 두 가지다.

### 방법 A — WSL 경로 직접 참조 (권장)

Libraries 폴더 복사 없이 빌드 결과를 즉시 참조한다.
재빌드 후 GH에서 Recompile (F5) 만 하면 반영된다.

```csharp
#r "\\wsl.localhost\Ubuntu\home\keonchae\URSUS\bin\URSUS.dll"
```

> **주의:** DLL을 새로 로드하려면 Recompile이 아닌 **Rhino 완전 재시작**이 필요하다.
> GH Script는 이미 메모리에 로드된 어셈블리를 Recompile만으로 교체하지 않는다.

### 방법 B — Libraries 폴더 복사

```bash
DEST="/mnt/c/Users/brian/AppData/Roaming/Grasshopper/Libraries"

cp /home/keonchae/URSUS/bin/URSUS.dll "$DEST/"

# Clipper2 의존 DLL
cp ~/.nuget/packages/clipper2/2.0.0/lib/netstandard2.0/Clipper2Lib.dll "$DEST/"

# ExcelDataReader (XlsxToJson 사용 시만 필요)
cp ~/.nuget/packages/exceldatareader/3.8.0/lib/netstandard2.0/ExcelDataReader.dll "$DEST/"
cp ~/.nuget/packages/exceldatareader.dataset/3.8.0/lib/netstandard2.0/ExcelDataReader.DataSet.dll "$DEST/"
```

`#r`에 파일명만 지정:

```csharp
#r "URSUS.dll"
```

복사 후 구조:

```
Grasshopper/Libraries/
├── URSUS.dll
├── Clipper2Lib.dll
├── ExcelDataReader.dll          (선택)
└── ExcelDataReader.DataSet.dll  (선택)
```

---

## 4. GH Definition 구성

### 전체 와이어링 다이어그램

```
[Panel] vworldKey      ──────────┐
[Panel] seoulKey       ──────────┤
[Panel] address1       ──────────┤
[Panel] address2       ──────────┼──► [URSUSSolver_GH] ──┬──► legalCodes  →  [Panel]
[Panel] mappingJsonPath──────────┘                       ├──► names        →  [Panel]
                                                         ├──► geometries   →  [Preview]
                                                         ├──► centroids    ──┐
                                                         ├──► areas         →  [Panel]
                                                         ├──► avgIncomes   ──┼─┐
                                                         └──► unionBoundary ─┼─┼─┐
                                                                             │ │ │
[Slider] resolution    ──────────┐                                          │ │ │
[Slider] power         ──────────┤                                          │ │ │
[Slider] heightScale   ──────────┤                                          │ │ │
[Slider] edgeFalloff   ──────────┤                                          │ │ │
[Slider] heightRatio   ──────────┤                                          │ │ │
[Slider] legendSteps   ──────────┤                                          │ │ │
[Slider] colorStyle    ──────────┼──► [Visualizer_GH] ◄────────────────────┘ │ │
[Swatch] colorLow      ──────────┤         ◄── values (avgIncomes) ──────────┘ │
[Swatch] colorHigh     ──────────┘         ◄── boundary (unionBoundary) ───────┘
                                           │
                                           ├──► mesh        →  [Custom Preview]
                                           ├──► flatMesh    →  [Custom Preview]
                                           ├──► minVal      →  [Panel]
                                           ├──► maxVal      →  [Panel]
                                           ├──► legendMesh  →  [Custom Preview]
                                           └──► legendDots  →  [Bake]
```

---

## 5. URSUSSolver_GH 컴포넌트 설정

소스 파일: `works/URSUS/URSUSSolver_GH.cs`

### 5-1. 컴포넌트 생성

1. GH 캔버스에서 더블클릭 → `C# Script` 검색 후 추가
2. 컴포넌트 더블클릭 → 스크립트 에디터 열기

### 5-2. 코드 적용 방법

기존 틀을 지우지 말고 **3곳만 수정**한다.

**수정 1 — 파일 맨 위에 `#r` 추가**

```csharp
#r "\\wsl.localhost\Ubuntu\home\keonchae\URSUS\bin\URSUS.dll"

// Grasshopper Script Instance
#region Usings
...
```

**수정 2 — `#region Usings` 안에 추가**

```csharp
using URSUS;
```

**수정 3 — `RunScript` 시그니처와 내용 교체**

파라미터는 컴포넌트의 `+/-` 버튼으로 추가/삭제한다.

```csharp
private void RunScript(
    string  vworldKey,
    string  seoulKey,
    string  address1,
    string  address2,
    string  mappingJsonPath,
    ref object legalCodes,
    ref object names,
    ref object geometries,
    ref object centroids,
    ref object areas,
    ref object avgIncomes,
    ref object unionBoundary)
{
    try
    {
        if (string.IsNullOrEmpty(vworldKey) || string.IsNullOrEmpty(seoulKey))
        { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "API 키가 비어 있습니다."); return; }
        if (string.IsNullOrEmpty(address1) || string.IsNullOrEmpty(address2))
        { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "주소가 비어 있습니다."); return; }
        if (string.IsNullOrEmpty(mappingJsonPath))
        { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "mappingJsonPath가 비어 있습니다."); return; }

        // cacheDir은 mappingJsonPath와 같은 폴더를 자동 사용
        var solver = new URSUSSolver(vworldKey, seoulKey, mappingJsonPath);
        SolverResult result = solver.Run(address1, address2);

        legalCodes    = result.LegalCodes;
        names         = result.Names;
        geometries    = result.Geometries;
        centroids     = result.Centroids;
        areas         = result.Areas;
        avgIncomes    = result.AvgIncomes;
        unionBoundary = result.UnionBoundary;

        Print($"[Solver] 완료: {result.LegalCodes.Count}개 법정동");
    }
    catch (Exception ex)
    {
        Print($"[ERR] {ex.GetType().Name}: {ex.Message}");
        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
    }
}
```

### 5-3. 입력 파라미터

| 파라미터 이름 | 타입 | 설명 |
|-------------|------|------|
| `vworldKey` | string | VWorld API 키 |
| `seoulKey` | string | 서울 열린데이터 API 키 |
| `address1` | string | BBOX 시작 주소 |
| `address2` | string | BBOX 끝 주소 |
| `mappingJsonPath` | string | adstrd_legald_mapping.json 경로 — 캐시도 같은 폴더에 자동 저장 |

### 5-4. 출력 파라미터

| 파라미터 이름 | 타입 | 설명 |
|-------------|------|------|
| `legalCodes` | List\<string\> | 법정동 코드 (8자리) |
| `names` | List\<string\> | 법정동 이름 |
| `geometries` | List\<Curve\> | 법정동 경계 PolylineCurve |
| `centroids` | List\<Point3d\> | 법정동 중심점 (UTM 미터 좌표) |
| `areas` | List\<double\> | 법정동 면적 (m²) |
| `avgIncomes` | List\<double\> | 법정동 기준 월 평균 소득 (원) |
| `unionBoundary` | Curve | 전체 외곽선 (Clipper2 Boolean Union 결과) |

---

## 6. Visualizer_GH 컴포넌트 설정

소스 파일: `works/URSUS/Visualizer_GH.cs`

### 6-1. 컴포넌트 생성

동일하게 `C# Script` 컴포넌트 추가 후 에디터 열기.

### 6-2. 코드 적용 방법

**수정 1 — 파일 맨 위에 `#r` 추가**

```csharp
#r "\\wsl.localhost\Ubuntu\home\keonchae\URSUS\bin\URSUS.dll"
```

**수정 2 — `#region Usings` 안에 추가**

```csharp
using System.Drawing;
using URSUS.Visualization;
```

**수정 3 — `RunScript` 시그니처와 내용 교체**

```csharp
private void RunScript(
    Curve         boundary,
    List<Point3d> centroids,
    List<double>  values,
    double        resolution,
    double        power,
    double        heightScale,
    double        edgeFalloff,
    double        heightRatio,
    int           legendSteps,
    int           colorStyle,
    Color         colorLow,
    Color         colorHigh,
    ref object    mesh,
    ref object    flatMesh,
    ref object    minVal,
    ref object    maxVal,
    ref object    legendMesh,
    ref object    legendDots)
{
    try
    {
        if (boundary == null)
        { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "boundary가 null입니다."); return; }
        if (centroids == null || centroids.Count == 0)
        { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "centroids가 비어 있습니다."); return; }
        if (values == null || values.Count == 0)
        { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "values가 비어 있습니다."); return; }

        if (resolution   <= 0) resolution   = 100.0;
        if (power        <= 0) power        = 3.0;
        if (heightScale  <= 0) heightScale  = 0.5;
        if (edgeFalloff  <= 0) edgeFalloff  = 2.0;
        if (heightRatio  <= 0) heightRatio  = 0.25;
        if (legendSteps  <= 1) legendSteps  = 8;
        if (colorLow.A   == 0) colorLow     = Color.FromArgb(44,  123, 182);
        if (colorHigh.A  == 0) colorHigh    = Color.FromArgb(215,  25,  28);

        var visualizer = new IDWVisualizer(
            centroids, values,
            resolution, power, heightScale,
            edgeFalloff, heightRatio,
            legendSteps, colorStyle,
            colorLow, colorHigh);

        VisualizerResult result = visualizer.Build(boundary);

        mesh       = result.Mesh;
        flatMesh   = result.FlatMesh;
        minVal     = result.MinVal;
        maxVal     = result.MaxVal;
        legendMesh = result.LegendMesh;
        legendDots = result.LegendDots;

        Print($"[Visualizer] 완료: Mesh Verts={result.Mesh.Vertices.Count}");
    }
    catch (Exception ex)
    {
        Print($"[ERR] {ex.GetType().Name}: {ex.Message}");
        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
    }
}
```

### 6-3. 입력 파라미터

| 파라미터 이름 | 타입 | GH 컴포넌트 | 기본값 | 설명 |
|-------------|------|------------|-------|------|
| `boundary` | Curve | Solver의 `unionBoundary` 연결 | — | 전체 외곽선 |
| `centroids` | List\<Point3d\> | Solver의 `centroids` 연결 | — | 법정동 중심점 |
| `values` | List\<double\> | Solver의 `avgIncomes` 연결 | — | 스칼라 값 |
| `resolution` | double | Number Slider | `100.0` | Mesh 최대 엣지 길이 (m). 낮을수록 세밀하고 느림 |
| `power` | double | Number Slider | `3.0` | IDW 지수 p. 높을수록 가까운 점에 집중 |
| `heightScale` | double | Number Slider | `0.5` | Z 높이 전체 배율 |
| `edgeFalloff` | double | Number Slider | `2.0` | 경계 근처 Z 감쇠 속도. 높을수록 경계에서 급격히 0 |
| `heightRatio` | double | Number Slider | `0.25` | Z 최대 높이 = bbox 너비 × heightRatio |
| `legendSteps` | int | Number Slider (Integer) | `8` | 범례 구간 수 |
| `colorStyle` | int | Number Slider (Integer) | `1` | 아래 색상 스타일 표 참고 |
| `colorLow` | Color | Colour Swatch | — | colorStyle=0 일 때만 사용 |
| `colorHigh` | Color | Colour Swatch | — | colorStyle=0 일 때만 사용 |

**colorStyle 옵션:**

| 값 | 이름 | 설명 |
|----|------|------|
| 0 | Custom | colorLow → colorHigh 사용자 지정 |
| 1 | BlueRed | 파랑 → 빨강 (기본) |
| 2 | Heatmap | 초록 → 노랑 → 빨강 |
| 3 | Spectral | 파랑 → 청록 → 초록 → 노랑 → 빨강 |
| 4 | Viridis | 보라 → 파랑 → 청록 → 초록 → 노랑 |
| 5 | Diverging | 파랑 → 흰색 → 빨강 |
| 6 | Grayscale | 흰색 → 검정 |

### 6-4. 출력 파라미터

| 파라미터 이름 | 타입 | 설명 |
|-------------|------|------|
| `mesh` | Mesh | Z 변위 + 컬러 메시 (3D 산 형태) |
| `flatMesh` | Mesh | Z=0 컬러 메시 (평면 지도 오버레이용) |
| `minVal` | double | 범례 최솟값 |
| `maxVal` | double | 범례 최댓값 |
| `legendMesh` | Mesh | 범례 그라디언트 바 |
| `legendDots` | List\<TextDot\> | 범례 값 레이블 |

---

## 7. 입력값 레퍼런스

### API 키

```
# JAH/works/URSUS/.env
VWORLD_API_KEY=...
DATA_SEOUL_API_KEY=...
```

### 주소 입력 예시

```
address1 = 인천 남동구 도림동             ← BBOX 좌하단 기준
address2 = 경기 남양주시 해밀예당1로 272   ← BBOX 우상단 기준
```

VWorld Geocoding API가 두 주소를 좌표로 변환한 뒤 그 BBOX 안의 법정동을 수집한다.
더 넓은 범위를 원하면 더 멀리 떨어진 주소를 입력하면 된다.

### mappingJsonPath 경로 예시

```
C:\Users\brian\Desktop\adstrd_legald_mapping.json
```

캐시 파일(`legald_boundaries.json`, `avg_income.json`)도 이 파일과 **같은 폴더**에 자동 저장된다.

---

## 8. 캐시 동작 방식

두 파서가 각각 캐시를 관리한다. **유효 기간: 30일**

| 캐시 파일 | 생성 주체 | 내용 |
|----------|----------|------|
| `legald_boundaries.json` | VworldApiParser | 법정동 경계 좌표 + 이름 + 면적 |
| `avg_income.json` | DataSeoulApiParser | 행정동 코드(8자리) → 월 평균 소득 |

캐시가 존재하면 API를 호출하지 않는다.
캐시를 강제로 초기화하려면 해당 파일을 삭제하면 된다.

```
mappingJsonPath와 같은 폴더/
├── adstrd_legald_mapping.json   ← 수동 생성 (2-2 참고)
├── legald_boundaries.json       ← 삭제 시 VWorld API 재호출
└── avg_income.json              ← 삭제 시 서울 열린데이터 API 재호출
```

> **avg_income.json을 재생성해야 하는 경우:**
> DLL 업데이트 이후 소득 데이터가 전부 동일한 값(globalMean)으로 나오면
> 이전 버전의 잘못된 캐시가 남아 있는 것이다. 파일을 삭제하고 다시 실행한다.

---

## 9. 트러블슈팅

### DLL 변경 후에도 동작이 이전과 같다

GH Script는 Recompile만으로 메모리에 로드된 어셈블리를 교체하지 않는다.
**Rhino를 완전히 종료하고 재시작**해야 새 DLL이 로드된다.

### The modifier 'private' is not valid for this item

`RunScript` 전체를 기존 틀 밖에 붙여넣은 경우 발생한다.
`class Script_Instance : GH_ScriptInstance` 클래스 안의 기존 `RunScript` 내용만 교체해야 한다.

### The name 'xxx' does not exist in the current context

GH 컴포넌트의 파라미터 이름과 `RunScript` 매개변수 이름이 다른 경우 발생한다.
컴포넌트 `+/-` 버튼으로 추가한 파라미터 이름이 코드와 정확히 일치해야 한다.

### avgIncomes가 전부 같은 값

**원인 1 — 이전 버전 캐시:** `avg_income.json`을 삭제하고 재실행한다.

**원인 2 — 여전히 같은 값:** Rhino를 재시작하지 않아 구 버전 DLL이 메모리에 남아 있는 것이다. Rhino 완전 종료 후 재시작한다.

**확인 방법:** GH Output 패널에서 아래 로그가 보여야 정상이다.
```
[DEBUG] income keys: 11545600, 11110515, ...   ← 8자리
[DEBUG] mapping keys: 11110515, 11000000, ...  ← 8자리 (일치)
```
6자리 income keys가 보이면 구 버전 DLL이 로드된 상태다.

### Assembly uses higher version than referenced (RhinoCommon 버전 불일치)

URSUS.dll은 `RhinoCommon 8.17.*`로 고정 빌드되어 있다.
설치된 Rhino가 8.17보다 낮다면 `URSUS.csproj`의 버전을 맞춰 재빌드해야 한다:

```xml
<PackageReference Include="RhinoCommon" Version="8.17.*" ExcludeAssets="runtime" />
```

### 오류: DLL을 찾을 수 없습니다

```
Could not load file or assembly 'URSUS'
```

- `#r` 경로의 DLL이 실제로 존재하는지 확인
- WSL 경로 방식이면 WSL 배포판 이름 확인 (`Ubuntu`, `Ubuntu-22.04` 등)
  ```
  \\wsl.localhost\Ubuntu\...     ← 배포판 이름을 정확히 입력해야 함
  ```
- Libraries 폴더 방식이면 `Clipper2Lib.dll`이 같은 폴더에 있는지 확인

### 의존 DLL 경로 확인 (WSL)

```bash
find ~/.nuget/packages/clipper2 -name "*.dll" 2>/dev/null
find ~/.nuget/packages/exceldatareader -name "*.dll" 2>/dev/null
```

### DLL 재빌드 → GH 반영 전체 절차

```bash
# 1. WSL에서 재빌드
cd /home/keonchae/URSUS/src/URSUS
dotnet build -c Release

# 2. Libraries 방식 사용 중이라면 복사 (WSL 경로 방식은 불필요)
cp /home/keonchae/URSUS/bin/URSUS.dll \
   "/mnt/c/Users/brian/AppData/Roaming/Grasshopper/Libraries/URSUS.dll"

# 3. Rhino 완전 종료 후 재실행 → GH 열기
```
