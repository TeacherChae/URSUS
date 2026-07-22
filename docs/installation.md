# Installation — URSUS 0.3.0

## installer

Windows 10/11 x64, Rhino 8 환경에서 `URSUS_0.3.0_Setup.exe`를 실행합니다. 설치기는 플러그인과 런타임 DLL, deps/runtimeconfig, 행정동-법정동 매핑, 샘플을 동일한 패키지 계약으로 설치합니다. `URSUS.GH.deps.json`이 지정하는 `runtimes/win/lib/net7.0` RID 구현도 하위 경로를 보존하여 설치합니다.

## manual-install

Portable ZIP의 파일을 `%APPDATA%\Grasshopper\Libraries\URSUS`에 복사하고 Windows 파일 속성에서 차단 해제가 필요한지 확인한 뒤 Rhino를 재시작합니다. `samples` 하위의 예제를 열어 로드를 확인합니다.

## settings-file

사용자 설정은 `%APPDATA%\URSUS\appsettings.json`에 둘 수 있습니다. 현재 JSON 키는 `VWorldKey`, `SeoulKey`입니다. deprecated `DataGoKrKey`는 기존 legacy 설정 호환용으로만 읽힙니다. 파일을 소스 제어하거나 공유하지 마십시오.

## mapping-file

`adstrd_legald_mapping.json`은 DLL에 내장되며 패키지에도 포함됩니다. 누락 경고가 있으면 같은 버전의 전체 패키지로 재설치하십시오.

## folder-permission

관리자 권한 없이 사용자 Grasshopper Libraries 폴더에 설치합니다. 조직 정책이 쓰기를 차단하면 관리자에게 해당 사용자 폴더 권한을 요청하십시오.

## reinstall

Rhino를 종료하고 기존 URSUS 폴더를 제거한 뒤 동일 버전의 설치기 또는 portable ZIP 전체로 다시 설치합니다. 서로 다른 버전의 DLL을 섞지 마십시오.
