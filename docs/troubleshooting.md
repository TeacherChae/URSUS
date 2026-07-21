# Troubleshooting — URSUS 0.3.0

오류 메시지의 `URSxxx` 코드와 아래 항목을 함께 확인하십시오. API 키나 키가 포함된 전체 URL은 이슈에 첨부하지 마십시오.

## network

인터넷, 프록시, 방화벽을 확인하고 잠시 후 재시도합니다. 서울 HTTP opt-in은 [API Keys](api-keys.md#seoul)의 위험을 이해한 경우에만 켭니다.

## vworld-server

VWorld 서비스 상태와 키 권한을 확인하고 재시도합니다.

## seoul-server

서울 열린데이터 포털 상태를 확인합니다. 페이지 응답이 중복·누락되면 URSUS는 결과를 캐시하지 않고 실패 처리합니다.

## json-parse

캐시를 삭제한 뒤 재시도합니다. 반복되면 비밀을 제거한 오류 코드와 시각만 기록합니다.

## api-error

제공자 오류 코드, 요청 기간, 권한을 확인합니다.

## data-collection

요청 경계, 기간, API 키, 레이어 coverage 및 누락 경고를 확인합니다.

## mapping-parse

같은 버전의 전체 패키지로 재설치해 매핑 파일/DLL 불일치를 제거합니다.

## file-permission

내보낼 폴더와 `%LOCALAPPDATA%\URSUS`에 현재 사용자의 쓰기 권한이 있는지 확인합니다.

## file-save

대상 CSV가 다른 프로그램에서 열려 있지 않은지, 경로와 여유 공간이 유효한지 확인합니다.

## visualization

Solver와 Visualizer 입력 리스트의 길이 및 동일 실행 여부를 확인합니다.

## cache

Rhino를 종료한 후 `%LOCALAPPDATA%\URSUS\cache`의 해당 캐시를 삭제하고 다시 실행합니다. 캐시는 스키마가 바뀌면 자동으로 분리됩니다.
