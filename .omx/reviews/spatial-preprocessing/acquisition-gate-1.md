# Acquisition Gate 1 Review — Seoul 250m living population + Seoul grid

검증일: 2026-07-24
판정: `HOLD`

## 검증 대상

- 통계: 서울 열린데이터광장 `OA-22784`, `250_LOCAL_RESD_20260719.zip`
- Geometry: 서울 생활인구 페이지의 `서울시_250m격자.zip`
- 매뉴얼: `서울 250M 격자 생활인구추계 매뉴얼`, 2025.05
- 증거 fixture: `docs/fixtures/seoul-250m-acquisition-gate-1.json`

## 통과한 항목

- 두 artifact는 `250M격자` ↔ `CELL_ID`에서 실제 동일한 한글 격자 ID 8,567개를 공유한다.
- Geometry의 `CELL_ID` 10,125개는 모두 유일하다.
- Polygon 10,125개는 모두 non-empty/valid이며 면적은 정확히 62,500m²다.
- `CELL_X/CELL_Y`는 각 250m Polygon의 중심이고 bounds 오차가 없다.
- `.prj`의 투영 파라미터는 EPSG:5179와 같다. 다만 projected CRS authority code가 파일에 직접 기록되지는 않았다.
- 2026-07-19 통계의 숫자형 `생활인구합계`는 모두 parse 가능하며 `*` 다음 최소 공개값은 4다.
- 공공누리 제1유형으로 통계 데이터의 상업적 이용과 변경이 허용된다.

## 차단 사유

### 1. 통계 row grain이 `date-hour-grid`가 아니다

253,371개 row의 실제 unique key는:

```text
일자 + 시간 + 행정동코드 + 250M격자
```

`일자 + 시간 + 250M격자`로 보면 48,502개 extra row가 생기며 한 격자에 최대 4개 행정동 row가 있다. 따라서 raw CSV를 그대로 `CELL_ID`에 join하면 duplicate ID로 실패하는 것이 올바르다.

### 2. 격자 집계 규칙이 공식 문서에 없다

매뉴얼은 결과를 “250M 격자단위”라고 부르지만, 행정동 경계를 가로지르는 여러 row를 합계해야 하는지와 그 의미를 명시하지 않는다. 합계를 추정해서 adapter에 넣으면 source semantics를 발명하게 된다.

### 3. 비식별 `*`가 합계를 막는다

한 격자의 행정동별 row 중 숫자와 `*`가 섞일 수 있다. `*`는 3 이하이지 0이 아니다. 이를 0으로 치환하거나 숫자 row만 더하면 exact value라고 말할 수 없다.

### 4. artifact 집합과 문서가 drift했다

- 통계 ID 8,568개 중 `다사47256125` 하나가 Geometry에 없다.
- 해당 ID의 24개 row는 전부 `*`이지만, artifact ID equality는 성립하지 않는다.
- 매뉴얼은 격자 10,021개를 명시하지만 실제 Shapefile은 10,125개다.
- Geometry artifact는 2025-05-13, 통계는 2026-07-19 기준이다.

### 5. provider가 문서 개편을 예고했다

서울시는 2026-07-31 이후 250m 데이터 정의서, 데이터 매뉴얼, 활용 매뉴얼과 서울 격자 파일을 보완한다고 공지했다. 현재 기준으로 불명확한 계약을 코드로 고정하기보다 새 artifact를 재검증해야 한다.

## 피드백 루프 결과

초기 가설은 “같은 서울시 pair이므로 `250M격자 == CELL_ID` identity projection으로 바로 통과 가능”이었다.

실데이터 검토 후 다음과 같이 수정했다.

1. 공간 ID namespace가 실제로 겹친다는 사실과 observation grain이 grid-unique라는 주장을 분리한다.
2. raw primary key를 네 필드로 기록한다.
3. cross-admin aggregation과 `*` 처리를 별도 semantic gate로 승격한다.
4. 서울 `CELL_ID`를 곧바로 NGII namespace라고 부르지 않는다.
5. 2026-07-31 이후 재배포 자료 확인 전 production adapter를 만들지 않는다.

## 재개 조건

- 2026-07-31 이후 grid/manual을 다시 내려받아 hash, feature count와 ID 집합을 재검사한다.
- provider가 grid-crossing 행정동 row의 집계 방식을 명확히 정의해야 한다.
- `*`는 missing 또는 문서화된 interval로 보존하고 0으로 변환하지 않는다.
- numeric output으로 채택하는 모든 grid에는 정확히 하나의 valid Polygon이 있어야 한다.
- Geometry의 독립된 version/license reference를 기록한다.

이 조건 전에는 `ExactSpatialLayerBinding`을 생성하는 live adapter 구현을 시작하지 않는다.
