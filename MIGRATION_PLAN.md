# URSUS C# Migration Plan

## 목표

Python 기반 URSUS 파이프라인을 C# .NET 클래스 라이브러리로 전환.
GH Script 컴포넌트는 얇은 wrapper로만 사용.

---

## 현재 구조 (Python + GH)

```
solver.py (URSUSSolver)
├── vworld_api_parser.py      WFS API → 법정동 경계 (Rhino Geometry 포함)
├── data_seoul_api_parser.py  서울 열린데이터 API → 행정동 소득 (XML)
└── adstrd_cd_to_legald_cd.py KIKmix.xlsx → 행정동↔법정동 코드 매핑

GeoUnion.cs     법정동 경계 Boolean Union (RhinoCommon, 불안정)
IDWVisualizer.cs IDW 공간 보간 + Mesh 시각화
```

**문제점**
- Rhino Geometry 생성이 파서 내부에 혼재 → 관심사 분리 안 됨
- Excel / pandas 의존성
- RhinoCommon Boolean Union 불안정 (fallback 로직 필요)
- 시각화 파라미터 변경 시 데이터 재수집까지 재실행됨

---

## 목표 구조 (C#)

### 레이어 구조

```
┌──────────────────────────────────────────────────────────────┐
│  GH Wrappers  (works/URSUS/)                                  │
│                                                               │
│  URSUSSolver_GH.cs          Visualizer_GH.cs                 │
│  · URSUSSolver.Run() 호출   · IDWVisualizer.Build() 호출     │
│  · GH 입출력 연결            · GH 입출력 연결                 │
└──────────────┬───────────────────────┬───────────────────────┘
               │ URSUS.dll             │ URSUS.dll
┌──────────────▼───────────────────────▼───────────────────────┐
│  .NET Class Library  (src/URSUS/)                             │
│  · RhinoCommon 참조 (ExcludeAssets="runtime")                 │
│  · Rhino 타입 직접 사용 가능                                   │
│                                                               │
│  URSUSSolver          IDWVisualizer                           │
│  Parsers/             Visualization/                          │
│  GeoOps/              Utils/                                  │
└───────────────────────────────────────────────────────────────┘
```

**GH 재연산 분리 효과**
```
address1 변경  →  URSUSSolver_GH 재실행  →  Visualizer_GH 연쇄 실행
colorStyle 변경 →  Visualizer_GH만 재실행  (API 호출 없음)
resolution 변경 →  Visualizer_GH만 재실행  (삼각분할만 재수행)
```

### 프로젝트 파일 구조

```
JAH/
├── src/URSUS/                         .NET 클래스 라이브러리
│   ├── URSUS.csproj
│   ├── URSUSSolver.cs                 데이터 파이프라인 오케스트레이터
│   ├── Parsers/
│   │   ├── VworldApiParser.cs         WFS API + GeoJSON 캐시
│   │   ├── DataSeoulApiParser.cs      XML API + 페이지네이션
│   │   └── MappingLoader.cs           JSON → 행정동↔법정동 매핑
│   ├── GeoOps/
│   │   └── Union.cs                   Clipper2 기반 Boolean Union
│   ├── Visualization/
│   │   └── IDWVisualizer.cs           IDW 보간 + Mesh 생성 + 범례
│   └── Utils/
│       ├── GpsToUtm.cs                좌표 변환
│       └── XlsxToJson.cs              KIKmix.xlsx → JSON 1회 변환 유틸
│
└── works/URSUS/
    ├── URSUSSolver_GH.cs              GH wrapper (신규)
    ├── Visualizer_GH.cs               GH wrapper (신규)
    ├── GeoUnion.cs                    (폐기 예정 → GeoOps/Union.cs로 대체)
    ├── IDWVisualizer.cs               (폐기 예정 → Visualization/IDWVisualizer.cs로 대체)
    └── MIGRATION_PLAN.md
```

---

## 설계 결정사항

| 항목 | 결정 | 이유 |
|------|------|------|
| 빌드 방식 | .NET 클래스 라이브러리 → URSUS.dll | 파일 간 import 가능 |
| GH 연동 | GH Script에서 `#r "URSUS.dll"` 참조 | wrapper만 GH에 존재 |
| GH 컴포넌트 수 | 2개 (Solver / Visualizer) | 재연산 범위 분리 — 시각화 파라미터 변경 시 데이터 재수집 방지 |
| Rhino 의존성 | RhinoCommon NuGet 참조 (`ExcludeAssets="runtime"`) | 어차피 GH 전용, Rhino 타입 직접 사용 |
| 라이브러리 출력 타입 | Rhino 타입 포함 (`Point3d`, `Curve`, `Mesh` 등) | wrapper 변환 코드 불필요 |
| 캐시 포맷 | GeoJSON | API 응답 그대로 저장, Python 호환 |
| Excel 매핑 | XlsxToJson.cs로 1회 변환 → JSON | NuGet: ExcelDataReader |
| 2D Geometry 연산 | Clipper2 (`GeoOps/Union.cs`) | RhinoCommon Boolean Union 불안정 |
| 3D Geometry 연산 | RhinoCommon | 출력이 Rhino Mesh, 외부 라이브러리 실익 없음 |
| JSON 파싱 | System.Text.Json | .NET 7 기본 내장 |
| HTTP | HttpClient (동기: `.GetAwaiter().GetResult()`) | WebClient deprecated in .NET 5+ |
| Target Framework | net7.0 | Rhino 8 기반 |

---

## 좌표 처리 분기 원칙

라이브러리가 RhinoCommon을 직접 참조하므로, Rhino Geometry 생성도 라이브러리 내부에서 처리.
Clipper2 연산도 내부에서 처리 (GH 와이어에 노출 안 함).

```
VworldApiParser (raw coords)
        │
        ├──→ Clipper2 Paths (GeoOps/Union.cs)   내부 연산 — GH 와이어 비노출
        │
        └──→ PolylineCurve / Point3d             라이브러리 내부 생성 → SolverResult로 반환
```

---

## 인터페이스 설계

### URSUSSolver 입력
| 이름 | 타입 | 설명 |
|------|------|------|
| `vworldKey` | `string` | VWorld API 키 |
| `seoulKey` | `string` | 서울 열린데이터 API 키 |
| `address1` | `string` | BBOX 시작 주소 |
| `address2` | `string` | BBOX 끝 주소 |
| `cacheDir` | `string` | 캐시 디렉토리 경로 |
| `mappingJsonPath` | `string` | adstrd_legald_mapping.json 경로 |

### URSUSSolver 출력 (`SolverResult`)
| 이름 | 타입 | 설명 |
|------|------|------|
| `LegalCodes` | `List<string>` | 법정동 코드 |
| `Names` | `List<string>` | 법정동 이름 |
| `Geometries` | `List<Curve>` | 법정동 경계 PolylineCurve |
| `Centroids` | `List<Point3d>` | 법정동 중심점 |
| `Areas` | `List<double>` | 법정동 면적 (m²) |
| `AvgIncomes` | `List<double>` | 법정동 기준 월 평균 소득 |
| `UnionBoundary` | `Curve` | 전체 외곽선 (GeoOps.Union 결과) |

### IDWVisualizer 입력
| 이름 | 타입 | 설명 |
|------|------|------|
| `boundary` | `Curve` | `SolverResult.UnionBoundary` |
| `centroids` | `List<Point3d>` | `SolverResult.Centroids` |
| `values` | `List<double>` | `SolverResult.AvgIncomes` |
| `resolution` | `double` | 메시 최대 엣지 길이 |
| `power` | `double` | IDW 지수 p |
| `heightScale` | `double` | Z 높이 배율 |
| `edgeFalloff` | `double` | 경계 감쇠 지수 |
| `heightRatio` | `double` | Z 최대 높이 = bboxWidth × heightRatio |
| `legendSteps` | `int` | 범례 단계 수 |
| `colorStyle` | `int` | 0=Custom 1=BlueRed 2=Heatmap 3=Spectral 4=Viridis 5=Diverging 6=Grayscale |
| `colorLow` | `Color` | colorStyle=0 최솟값 색상 |
| `colorHigh` | `Color` | colorStyle=0 최댓값 색상 |

### IDWVisualizer 출력 (`VisualizerResult`)
| 이름 | 타입 | 설명 |
|------|------|------|
| `Mesh` | `Mesh` | Z 변위 + 컬러 메시 |
| `FlatMesh` | `Mesh` | Z=0 컬러 메시 (지도 오버레이용) |
| `MinVal` | `double` | 범례 최솟값 |
| `MaxVal` | `double` | 범례 최댓값 |
| `LegendMesh` | `Mesh` | 범례 그라디언트 바 |
| `LegendDots` | `List<TextDot>` | 범례 값 레이블 |

---

## NuGet 의존성 (URSUS.csproj)

| 패키지 | 용도 |
|--------|------|
| `RhinoCommon` (`ExcludeAssets="runtime"`) | Rhino Geometry 타입 참조 |
| `Clipper2Lib` | 2D Polygon 연산 (GeoOps/Union.cs) |
| `ExcelDataReader` | XlsxToJson 1회 변환용 |
| `ExcelDataReader.DataSet` | ExcelDataReader 보조 |

---

## 캐시 파일 위치

```
src/cache/
  legald_boundaries.geojson    VworldApiParser 캐시
  avg_income.json              DataSeoulApiParser 캐시
  adstrd_legald_mapping.json   KIKmix 변환 결과 (XlsxToJson 출력)
```

---

## 작업 순서

- [x] 1. `src/URSUS/URSUS.csproj` 프로젝트 세팅 (net7.0, NuGet)
- [x] 2. `Utils/GpsToUtm.cs` — gps_to_upm.py 포팅
- [x] 3. `Utils/XlsxToJson.cs` — KIKmix.xlsx → adstrd_legald_mapping.json
- [x] 4. `Parsers/VworldApiParser.cs` — WFS + Geocoding + GeoJSON 캐시
- [x] 5. `Parsers/DataSeoulApiParser.cs` — XML 페이지네이션
- [x] 6. `Parsers/MappingLoader.cs` — JSON 로드
- [x] 7. `GeoOps/Union.cs` — Clipper2 기반 Boolean Union (GeoUnion.cs 포팅)
- [x] 8. `URSUSSolver.cs` — merge 로직, GeoOps.Union 호출, SolverResult 반환
- [x] 9. `Visualization/IDWVisualizer.cs` — IDWVisualizer.cs 포팅
- [x] 10. `works/URSUS/URSUSSolver_GH.cs` — GH wrapper
- [x] 11. `works/URSUS/Visualizer_GH.cs` — GH wrapper
- [ ] 12. GH Definition 업데이트 (`URSUS_test.ghx`)
- [ ] 13. `works/URSUS/GeoUnion.cs`, `IDWVisualizer.cs` 폐기

---

## 미결 사항

- [ ] GH wrapper에서 DLL 로드 경로 관리 방식
