# 행정동별 대중교통 총 승차 승객 수

## 개요

서울시 행정동 단위의 일별 대중교통(버스+지하철 통합) 총 승차 승객 수 데이터.
시간대별(00~23시) 세분 데이터도 포함한다.

## 출처

- **기관**: 서울특별시 (서울 열린데이터 광장)
- **서비스명**: `tpssPassengerCnt`
- **API 엔드포인트**: `http://openapi.seoul.go.kr:8088/{키}/xml/tpssPassengerCnt/{start}/{end}/`

## 업데이트 주기

- **일별** (`CRTR_DD`: YYYYMMDD)
- 총 레코드 수: **644,687행** (2026년 3월 기준)
- 예상 구조: 서울 행정동(~424개) × 약 4년치 일자

## 공간 단위 및 커버리지

- **공간 단위**: 행정동 (`DONG_ID`, 8자리)
  - ※ 필드명은 `DONG_ID`이나 코드 체계는 `ADSTRD_CD`와 동일
- **커버리지**: 서울특별시 전체 행정동

## 주요 필드

| 필드명 | 타입 | 설명 |
|---|---|---|
| `CRTR_DD` | string | 기준일자 (YYYYMMDD) |
| `DONG_ID` | string | 행정동코드 (8자리) — 집계 키 |
| `PSNG_NO` | int | **일일 총 승차 승객 수** — 사용 필드 |
| `PSNG_NO_00`~`PSNG_NO_23` | int | 시간대별 승차 승객 수 |

## 가공 방법

```
raw: 행정동 × 날짜 레코드 (644,687행)
  │
  ▼ AggregateFieldByAdstrd(keyField="DONG_ID", valueField="PSNG_NO")
전기간 단순 평균 → 행정동별 일평균 승차 승객 수
  │
  ▼ MapToLegald()
adstrd_cd → legald_cd 매핑 적용 → 법정동별 평균
  │
  ▼ BuildOverlayValues()
min-max 정규화 [0, 1]
```

- 캐시 파일: `transit_boarding.json` (TTL 30일)

## 알려진 한계

- 버스·지하철 통합 승차 수이므로 교통망 밀도에 따라 동 간 편차가 매우 큼
  (환승역·간선 정류장 밀집 동과 주거 중심 동 간 수십 배 차이 가능)
- 승차 기준이므로 해당 동을 지나치는 통과 이용객은 미반영
- 644,687행으로 캐시 미존재 시 첫 호출 시간 상당 소요
