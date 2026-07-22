# API Keys — URSUS 0.3.0

키는 아래 우선순위(위가 우선)로 읽습니다.

1. Grasshopper 와이어 등 명시적 입력
2. 환경 변수 (`URSUS_VWORLD_KEY`, `URSUS_SEOUL_KEY`)
3. 플러그인 DLL 인접 `.env`
4. `%APPDATA%\URSUS\.env`
5. 플러그인 DLL 인접 `appsettings.json`
6. `%APPDATA%\URSUS\appsettings.json`

명시적 입력은 현재 실행의 override이며 설정 파일에 자동 저장되지 않습니다. 키를 로그, 이슈, 캔버스 스크린샷에 게시하지 마십시오.

## vworld

[VWorld 개발자센터](https://www.vworld.kr/dev/v4dv_2ddataguide2_s001.do)에서 키를 발급하고 `VWorldKey`에 설정합니다.

## vworld-format

키 앞뒤 공백을 제거하고 발급된 문자열을 그대로 입력합니다.

## vworld-rejected

도메인/사용 API 권한과 유효기간을 확인하고 필요하면 새 키를 발급합니다.

## seoul

[서울 열린데이터 광장](https://data.seoul.go.kr/)에서 키를 발급하고 `SeoulKey`에 설정합니다.

> **보안 경고:** 서울 열린데이터의 해당 XML 엔드포인트는 HTTP만 제공합니다. URSUS는 기본적으로 키가 평문 HTTP로 전송되는 원격 검증과 수집을 차단합니다. 위험을 이해하고 신뢰할 수 있는 네트워크를 사용할 때만 Setup의 명시적 체크박스 또는 Solver의 `Allow Insecure Seoul HTTP` 입력으로 현재 실행을 허용하십시오. 키는 URL에 포함되므로 프록시/네트워크 로그에 남을 수 있습니다.

## seoul-format

키 앞뒤 공백을 제거하고 영문, 숫자, 하이픈, 밑줄만 포함하는지 확인합니다.

## seoul-rejected

명시적 HTTP 허용이 켜져 있는지 확인한 뒤 포털의 키 상태와 서비스 권한을 확인합니다. 키를 오류 보고에 복사하지 마십시오.

## DataGoKrKey — deprecated legacy

`DataGoKrKey` / `URSUS_DATA_GO_KR_KEY`는 현재 기본 설정·실행 flow에서 요구하지 않습니다. Setup과 Grasshopper 키 설정 대화상자도 이 키를 새로 입력받지 않습니다.

기존 자동화와 명시적 `land_price` / `zoning` legacy adapter의 하위 호환을 위해 loader는 기존 `DataGoKrKey`를 읽을 수는 있습니다. 신규 구현은 이 credential에 의존하지 않아야 하며, VWorld 대체 source는 endpoint·의미·coverage·fixture가 별도로 검증된 후에만 연결합니다.
