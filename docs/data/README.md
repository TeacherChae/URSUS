# URSUS 데이터 소스 기술 문서

이 디렉토리는 원천 API의 공간·시간 단위와 사용 field를 기록한다. 사용자가 결과를 해석할 때 필요한 공통 설명은 [데이터셋 해석 가이드](../dataset_interpretation_guide.md)를 기준으로 한다.

## 데이터 소스 목록

| 파일 | 지표 | 원천 공간 단위 | 관측 단위 | 제품 상태 |
|---|---|---|---|---|
| [avg_income.md](avg_income.md) | 월평균 소득 | 행정동 | 분기 | 사용 중 |
| [resident_pop.md](resident_pop.md) | 상주인구 | 행정동 | 분기 | 사용 중 |
| [transit_boarding.md](transit_boarding.md) | 대중교통 승차 | 행정동 | 일 | 사용 중 |
| [living_pop.md](living_pop.md) | 생활인구 | 행정동 | 일×시간 | 비활성 — 제품 계약/성능 검증 필요 |

공시지가, 용도지역과 VWorld 경계는 서울 XML parser 계열이 아니므로 현재 [데이터셋 해석 가이드](../dataset_interpretation_guide.md)에 함께 설명한다.

## 현재 파이프라인

```text
원천 API
  → source별 bounded parser / complete response 검증
  → 원천 공간 단위와 집계 의미를 보존한 값
  → query-keyed atomic cache
  → 행정동↔법정동 mapping 또는 canonical legal ID 병합
  → DistrictDataSet + Observation/Coverage/Provenance
  → snapshot normalization / weighted overlay
  → choropleth·extrusion 또는 사용자가 선택한 IDW trend
```

- 서울 XML은 page 단위 streaming projection을 사용한다.
- 서울 분기 지표는 최신 닫힌 분기 또는 명시 기간을 사용한다.
- 교통은 최신 닫힌 월 또는 명시 기간을 사용한다.
- pagination total, 실제 row count와 중복 identity가 맞아야 cache할 수 있다.
- mapping되지 않은 지역을 전체 평균으로 채우지 않는다. missing과 mapping quality를 결과에 남긴다.
- source별 집계는 `Mean`, `Sum`, `Category` 의미를 구분한다.

## 공간 단위

| 단위 | 설명 | 코드 |
|---|---|---|
| 행정동 | 서울 통계의 원천 행정 단위 | provider field 기준 8자리 |
| 법정동 | 경계·분석·시각화의 canonical 단위 | 내부 canonical legal ID 10자리 |
| PNU | 필지 식별자 | 앞 10자리를 canonical legal ID로 변환 |

행정동↔법정동 mapping은 패키지에 포함된 `adstrd_legald_mapping.json`을 사용한다. 사용자가 이 파일을 직접 배치하지 않으며 package contract가 누락을 차단한다.

## 정규화와 overlay

정규화와 가중치는 원천 parser가 아니라 snapshot derived analysis에서 처리한다.

```text
normalized = (x - min) / (max - min)
overlay = Σ(normalized × weight) / Σ(active weight)
```

- 실제 값이 없는 layer/district는 전체 평균으로 대치하지 않는다.
- 존재하는 layer만 district별로 weight를 재정규화한다.
- zoning 같은 category는 기본 numeric overlay에서 제외한다.
- IDW는 기본 표현이 아니라 행정구역 값을 연속장으로 가정한 선택적 trend mode다.

## 캐시 정책

- 저장 위치: OS 사용자 `LocalApplicationData` 아래 URSUS cache
- key: source, schema, query intent, 기간, 요청 지역, CRS와 source별 parameter
- 저장: temp file 후 atomic replace
- 동시 동일 key 요청: origin fetch coalescing
- 무효화: TTL, schema 변경, corruption, force refresh
- force refresh 실패 시 이전 유효 cache는 보존

## 새 데이터셋 추가 체크리스트

1. `IDataSource` 또는 `IBoundaryDataSource`로 원천 계약을 구현한다.
2. `DataSourceMetadata`에 ID, 단위, TTL과 API key requirement를 선언한다.
3. 원천 공간 단위, 기간 선택, 집계 의미와 complete 조건을 먼저 문서화한다.
4. parser는 cancellation, resource 상한과 secret redaction을 따른다.
5. cache 전에 구조뿐 아니라 값·기간·coverage의 의미를 검증한다.
6. `DataSourceRegistry`와 Solver display-name mapping에 등록한다.
7. 정상, 0건, auth/error, schema drift, duplicate/pagination과 cache 회귀 테스트를 추가한다.
8. 이 디렉토리의 source 문서와 [해석 가이드](../dataset_interpretation_guide.md)를 갱신한다.

## source 문서 템플릿

```markdown
# 지표명

## 개요
## 출처와 서비스 ID
## 원천 공간 단위와 coverage
## 관측 기간과 선택 정책
## 사용 field
## 집계와 mapping 의미
## complete 조건
## cache identity
## 알려진 한계와 편향
```
