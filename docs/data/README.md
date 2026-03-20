# URSUS 데이터 소스 문서

이 디렉토리는 URSUS 분석에 사용되는 원천 데이터의 명세와 가공 방식을 기록한다.

## 데이터 소스 목록

| 파일 | 지표명 | 공간 단위 | 업데이트 주기 | 상태 |
|---|---|---|---|---|
| [avg_income.md](avg_income.md) | 행정동별 월평균 소득 | 행정동 | 분기 | ✅ 사용 중 |
| [living_pop.md](living_pop.md) | 행정동별 생활인구 | 행정동 | 일별 (시간대별) | ✅ 사용 중 |

## 가공 파이프라인 개요

```
원천 API (서울 열린데이터 광장)
  │
  ▼
FetchAllRecords()          페이지네이션 자동 처리 (1,000행/페이지)
  │
  ▼
AggregateFieldByAdstrd()   행정동(adstrd_cd) 기준 전기간 평균
  │
  ▼
캐시 저장 (*.json, TTL 30일)
  │
  ▼
MapToLegald()              adstrd_cd → legald_cd 매핑 (KIKmix 기반)
  │
  ▼
BuildOverlayValues()       min-max 정규화 → 균등 평균 → [0, 1] overlay 값
  │
  ▼
Visualizer (IDW 보간)
```

## 공간 단위 체계

| 단위 | 설명 | 코드 자릿수 | 비고 |
|---|---|---|---|
| 행정동 (adstrd) | 행정 운영 단위 | 8자리 | 서울 열린데이터 API 기준 |
| 법정동 (legald) | 법적 경계 단위 | 8자리 | VWorld WFS API / 시각화 단위 |

- 행정동 ↔ 법정동 매핑: `refs/adstrd_legald_mapping.json` (KIKmix.20240201.xlsx 기반)
- 동일 행정동이 복수의 법정동에 매핑되는 경우 평균값 적용

## 정규화 방식

모든 지표는 시각화 전 **min-max 정규화** 를 적용한다.

```
normalized = (x - min) / (max - min)
```

- 정규화 범위: 분석 대상 법정동 전체 (서울 내 BBOX 내)
- fallback: 매핑 미존재 법정동 → 해당 지표의 전체 평균값 적용
- 복수 지표 선택 시 균등 가중 평균 (weighted overlay)

> 향후 사용자 정의 가중치 입력 지원 예정

## 캐시 정책

| 항목 | 내용 |
|---|---|
| 저장 위치 | `URSUS.dll`과 동일 폴더 |
| TTL | 30일 (이후 자동 재요청) |
| 무효화 | 캐시 파일 삭제 후 재실행 |
| 파일명 | 각 지표 문서 참조 |

## 새 데이터셋 추가 체크리스트

1. 이 디렉토리에 `{metric_name}.md` 문서 작성 (아래 템플릿 참조)
2. `DataSeoulApiParser.cs`에 서비스 상수 및 `Get*ByAdstrd()` 메서드 추가
   - `FetchAndCache(serviceName, keyField, valueField, cacheFileName, cacheDir)` 호출
   - `keyField`: 행정동코드 필드명 확인 필수 (서비스마다 다름)
3. `URSUSSolver.cs`의 `DS_*` 상수 추가 및 `Run()` 분기 추가
4. `URSUSSolver_GH.cs` 주석의 Value List 항목 목록 업데이트

## 지표 문서 템플릿

```markdown
# {지표명}

## 개요
...

## 출처
- 기관: ...
- 서비스명: ...
- API 엔드포인트: `http://openapi.seoul.go.kr:8088/{키}/xml/{SERVICE}/{start}/{end}/`

## 업데이트 주기
...

## 공간 단위 및 커버리지
...

## 주요 필드

| 필드명 | 타입 | 설명 |
|---|---|---|
| ... | ... | ... |

## 가공 방법
...

## 알려진 한계
...
```
