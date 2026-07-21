# URSUS 0.3.0

URSUS (Urban Research with Spatial Utility System)는 Rhino 8 / Grasshopper에서 행정·공간 데이터를 법정동 경계에 결합해 분석하는 플러그인입니다.

## 시작하기

1. Rhino 8과 Grasshopper를 설치합니다.
2. 릴리스의 `URSUS_0.3.0_Setup.exe`를 실행하거나 portable ZIP의 런타임 파일을 Grasshopper `Libraries/URSUS` 폴더에 복사합니다.
3. [API 키 설정](docs/api-keys.md)을 완료합니다.
4. `URSUS_sample.gh` 또는 `URSUS_pipeline_demo.ghx`를 열고 Solver를 실행합니다.

자세한 절차는 [Getting Started](docs/getting-started.md)와 [Installation](docs/installation.md)을 참고하세요.

## 문서

- [API keys](docs/api-keys.md)
- [Installation](docs/installation.md)
- [Troubleshooting](docs/troubleshooting.md)
- [CSV export](docs/csv-export.md)
- [Dataset interpretation](docs/dataset_interpretation_guide.md)

## 개발 검증

```bash
dotnet restore URSUS.sln
dotnet run --project src/URSUS.Tests/URSUS.Tests.csproj -c Release --no-restore
dotnet build src/URSUS/URSUS.csproj -c Release --no-restore
dotnet build src/URSUS.GH/URSUS.GH.csproj -c Release --no-restore
python installer/verify_package_contract.py
```

Setup/전체 솔루션 빌드는 Windows Desktop SDK가 있는 Windows 환경에서 수행합니다.

## 라이선스

[MIT License](LICENSE)
