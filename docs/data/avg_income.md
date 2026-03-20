# 행정동별 월평균 소득

## 개요

서울시 행정동 단위의 분기별 월평균 소득 및 소비 지출 데이터.
카드 소비 패턴 등을 기반으로 추정된 소득 추정치이며, 신한카드 데이터 기반.

## 출처

- **기관**: 서울특별시 (서울 열린데이터 광장)
- **서비스명**: `VwsmAdstrdNcmCnsmpW`
- **API 엔드포인트**: `http://openapi.seoul.go.kr:8088/{키}/xml/VwsmAdstrdNcmCnsmpW/{start}/{end}/`
- **데이터 페이지**: https://data.seoul.go.kr (행정동별 소득소비)

## 업데이트 주기

- **분기별** (`STDR_YYQU_CD`: 연도 + 분기, e.g. `20241` = 2024년 1분기)
- 수집 시점 기준 전체 누적 제공 (분기 수 × 서울 행정동 수)
- 총 레코드 수: **11,475행** (2024년 1분기 기준)

## 공간 단위 및 커버리지

- **공간 단위**: 행정동 (`ADSTRD_CD`, 8자리)
- **커버리지**: 서울특별시 전체 행정동

## 주요 필드

| 필드명 | 타입 | 설명 |
|---|---|---|
| `STDR_YYQU_CD` | string | 기준 연도+분기 (e.g. `20241` = 2024 Q1) |
| `ADSTRD_CD` | string | 행정동코드 (8자리) — 집계 키 |
| `ADSTRD_CD_NM` | string | 행정동명 |
| `MT_AVRG_INCOME_AMT` | int | **월평균 소득금액 (원)** — 사용 필드 |
| `INCOME_SCTN_CD` | string | 소득 구간 코드 |
| `EXPNDTR_TOTAMT` | int | 총 소비지출 합계 (원) |
| `FDSTFFS_EXPNDTR_TOTAMT` | int | 식료품 지출 합계 (원) |
| `CLTHS_FTWR_EXPNDTR_TOTAMT` | int | 의류·신발 지출 합계 (원) |
| `LVSPL_EXPNDTR_TOTAMT` | int | 생활용품 지출 합계 (원) |
| `MCP_EXPNDTR_TOTAMT` | int | 의료비 지출 합계 (원) |
| `TRNSPORT_EXPNDTR_TOTAMT` | int | 교통 지출 합계 (원) |
| `EDC_EXPNDTR_TOTAMT` | int | 교육 지출 합계 (원) |
| `PLESR_EXPNDTR_TOTAMT` | int | 유흥 지출 합계 (원) |
| `LSR_CLTUR_EXPNDTR_TOTAMT` | int | 여가·문화 지출 합계 (원) |
| `ETC_EXPNDTR_TOTAMT` | int | 기타 지출 합계 (원) |
| `FD_EXPNDTR_TOTAMT` | int | 식음료(외식) 지출 합계 (원) |

## 가공 방법

```
raw: 행정동 × 분기 레코드 (11,475행)
  │
  ▼ AggregateFieldByAdstrd(keyField="ADSTRD_CD", valueField="MT_AVRG_INCOME_AMT")
전기간 단순 평균 → 행정동별 대표값 (424개 내외)
  │
  ▼ MapToLegald()
adstrd_cd → legald_cd 매핑 적용 → 법정동별 평균
  │
  ▼ BuildOverlayValues()
min-max 정규화 [0, 1]
```

- 분기별 가중치 없이 단순 평균 적용 (최신 분기 가중치 우대 미적용)
- 캐시 파일: `avg_income.json` (TTL 30일)

## 알려진 한계

- 카드 소비 기반 추정치이므로 현금 거래 비율이 높은 고령 인구 밀집 지역에서 과소 추정 가능
- 분기 단위 데이터 — 월별·주별 변동 반영 불가
- 소득 구간(`INCOME_SCTN_CD`)별 세분화 정보가 있으나 현재 구간 구분 없이 평균값(`MT_AVRG_INCOME_AMT`)만 사용
- 서울 외 지역(인천, 경기 일부 포함 BBOX) 행정동은 매핑 미존재로 fallback(전체 평균) 처리됨
