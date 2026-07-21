# 행정동별 상주인구

## 개요

서울시 행정동 단위의 분기별 주민등록 상주인구 데이터.
실제 해당 동에 주민등록된 인구 수 및 세대 구성 정보를 제공한다.
생활인구(체류 추정)와 달리 행정 등록 기반의 확정 수치.

> 제품은 전체 기간 평균이 아니라 현재 분기보다 이전인 **최신 닫힌 분기** 또는 사용자가 명시한 분기만 사용한다.

## 출처

- **기관**: 서울특별시 (서울 열린데이터 광장)
- **서비스명**: `VwsmAdstrdRepopW`
- **API 엔드포인트**: `http://openapi.seoul.go.kr:8088/{키}/xml/VwsmAdstrdRepopW/{start}/{end}/`

## 업데이트 주기

- **분기별** (`STDR_YYQU_CD`: 연도 + 분기, e.g. `20241` = 2024년 1분기)
- 총 레코드 수: **11,454행** (2024년 1분기 기준)
- 예상 구조: 서울 행정동(~424개) × 분기 수

## 공간 단위 및 커버리지

- **공간 단위**: 행정동 (`ADSTRD_CD`, 8자리) — 소득 데이터와 동일 필드명
- **커버리지**: 서울특별시 전체 행정동

## 주요 필드

| 필드명 | 타입 | 설명 |
|---|---|---|
| `STDR_YYQU_CD` | string | 기준 연도+분기 (e.g. `20241` = 2024 Q1) |
| `ADSTRD_CD` | string | 행정동코드 (8자리) — 집계 키 |
| `ADSTRD_CD_NM` | string | 행정동명 |
| `TOT_REPOP_CO` | int | **총 상주인구 수** — 사용 필드 |
| `ML_REPOP_CO` | int | 남성 상주인구 수 |
| `FML_REPOP_CO` | int | 여성 상주인구 수 |
| `AGRDE_10_REPOP_CO` | int | 10대 이하 상주인구 수 |
| `AGRDE_20_REPOP_CO` | int | 20대 상주인구 수 |
| `AGRDE_30_REPOP_CO` | int | 30대 상주인구 수 |
| `AGRDE_40_REPOP_CO` | int | 40대 상주인구 수 |
| `AGRDE_50_REPOP_CO` | int | 50대 상주인구 수 |
| `AGRDE_60_ABOVE_REPOP_CO` | int | 60대 이상 상주인구 수 |
| `MAG_*_REPOP_CO` | int | 남성 연령대별 상주인구 |
| `FAG_*_REPOP_CO` | int | 여성 연령대별 상주인구 |
| `TOT_HSHLD_CO` | int | 총 세대 수 |
| `APT_HSHLD_CO` | int | 아파트 세대 수 |
| `NON_APT_HSHLD_CO` | int | 비아파트 세대 수 |

## 가공 방법

```
raw: 행정동 × 분기 레코드 (page streaming)
  │
  ▼ 최신 닫힌 분기 또는 명시 분기만 선택
선택 분기의 행정동별 상주인구 합계
  │
  ▼ MapToLegald()
adstrd_cd → legald_cd 매핑 적용 → 총량 보존 균등 분배(EstimatedEqualSplit)
  │
  ▼ BuildOverlayValues()
min-max 정규화 [0, 1]
```

- query intent, 기간, 예상 지역 set과 요청 지역을 포함한 cache key 사용 (TTL 30일)
- 하나의 행정동이 여러 법정동에 대응할 때 실제 세부 분포가 없으므로 균등 분배한다. 이는 실측값이 아니라 mapping assumption이다.

## 알려진 한계

- 주민등록 기준이므로 실거주 인구와 괴리 존재 (장기 미거주 등록 세대 포함 가능)
- 외국인 등록 인구 포함 여부 불명확
- 분기 단위 데이터 — 신규 입주·이사 등 단기 변동 반영 불가
