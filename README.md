# Klipper Firmware Wizard

Windows에서 Klipper/Katapult 펌웨어를 설정하고 빌드하는 C# WinForms 프로그램입니다.
현재 버전: **1.0.23**. .NET Framework 4.8 x64를 사용하며 Python, WSL, .NET 런타임을 배포본에 포함하지 않습니다.
ARM GCC는 릴리즈에 포함합니다. SSH 기능은 원격 Linux의 Python과 필요한 도구를 사용합니다.

## 실행

릴리즈 ZIP을 쓰기 가능한 폴더에 풀고 `KlipperFirmwareWizard.exe`를 실행합니다.
EXE와 `.exe.config`, `lib`, `builder_assets`는 함께 보관해야 합니다.

첫 실행에는 인터넷 연결이 필요합니다. Klipper와 Katapult의 최신 커밋을 조회하고 EXE 옆의 `Klipper`, `Katapult` 폴더에 필요한 소스를 자동으로 받습니다. 버전은 `v0.13.0-786-g461c4e37`처럼 전체 Git 버전으로 표시합니다. 릴리즈 ZIP에는 이 소스 캐시를 포함하지 않습니다.

GitHub 비인증 API 한도에 도달하거나 네트워크가 끊기면 첫 준비가 완료되지 않을 수 있습니다. 안내된 시각 또는 연결 복구 후 다시 실행하세요. 이미 받은 소스가 있으면 사용할 수 있는 캐시로 진행합니다.

## 사용 흐름

1. Klipper 펌웨어 또는 Katapult 부트로더 설치를 선택합니다.
2. Klipper는 Katapult 설치 여부를 선택합니다. 제조사 부트로더의 오프셋도 지정할 수 있습니다.
3. 버전을 선택하고 menuconfig 화면에서 보드에 맞게 설정합니다.
4. 빌드 후 `Out`에 BIN/UF2 업데이트 파일만 복사합니다.
5. USB 직접 연결 또는 SSH로 서버에 연결된 보드를 선택해 업로드합니다.

Klipper 컴파일 자료는 `Firmware`, Katapult 컴파일 자료는 `Bootloader`에 남습니다. Out 파일을 이 프로그램에서 검증할 때 원본 빌드 기록을 참조하므로 컴파일 자료도 보관하세요.
작업에서 Klipper 서비스를 중지했다면 성공/실패와 관계없이 다시 시작하며, 원래 중지돼 있던 서비스는 시작하지 않습니다.

RP2040의 Klipper 업로드는 Katapult 있음/없음 × USB/SSH 네 경로가 사용자 실물 검증을 거쳤습니다. Katapult가 설치된 CH340 연결 STM32F407의 Klipper 업로드도 사용자 검증을 거쳤습니다. 모든 지원 MCU의 실물 검증을 의미하지는 않습니다.

## 폴더 구조

```text
KlipperFirmwareWizard.exe
KlipperFirmwareWizard.exe.config
lib/                 .NET 라이브러리
builder_assets/      ARM GCC와 빌드 규칙 메타데이터
docs/                설명서와 빌드 정보
licenses/            외부 라이브러리 라이선스
Klipper/, Katapult/  첫 실행/버전 선택 시 생성되는 소스
Firmware/, Bootloader/  컴파일 자료와 검증 기록
Out/                 완성된 업데이트 파일
winforms-data/       설정, 호스트 키, 작업 로그, 내보내기 참조
```

## 소스에서 빌드

Windows와 .NET SDK가 필요합니다. .NET Framework 참조 어셈블리는 NuGet으로 복원합니다.

```powershell
./tools/build_nativecs.ps1
```

출력은 항상 `dist/KlipperFirmwareWizard`이며 기존 설정이나 소스 캐시를 삭제하지 않고 EXE/DLL을 갱신합니다.
ARM GCC가 없으면 공식 xPack 아카이브를 받아 SHA256을 검사하고, `assets/toolchain-profile.json`에 명시된 파일만 추출합니다. 이전 릴리즈의 컴파일러를 재사용하려면:

```powershell
./tools/build_nativecs.ps1 -AssetsFrom 'C:/Tools/KlipperFirmwareWizard'
```

컴파일러 없이 C# 코드만 확인할 때는 다음 명령을 사용할 수 있습니다. 이 결과만으로는 펌웨어 빌드 기능을 실행할 수 없습니다.

```powershell
dotnet build nativecs/KlipperNative.csproj -c Release
```

## 개발 검사

개발용 C# 진단 코드는 `tests/Native`에 분리되어 일반 릴리즈에서 제외됩니다.

```powershell
./tools/build_nativecs.ps1 -Diagnostics -OutputDirectory build/diagnostics -AssetsFrom dist/KlipperFirmwareWizard
./tools/run_checks.ps1 -ApplicationDirectory build/diagnostics
```

Python 3가 개발 PC에 있다면 서비스 복구의 모의 검사도 실행할 수 있습니다.

```powershell
./tools/run_checks.ps1 -ApplicationDirectory build/diagnostics -Python python
```

이 검사는 실제 보드에 기록하거나 실제 Linux 서비스를 조작하지 않습니다. 기존 탭 UI는 `--classic` 인수로 사용할 수 있습니다.

## 릴리즈 ZIP 만들기

```powershell
./tools/package_release.ps1
```

스크립트는 EXE, lib, 필수 builder_assets, 공개 문서와 라이선스만 포함합니다. `Klipper`, `Katapult`, `Out`, 컴파일 결과, 설정, SSH 로그, 개발 진단 코드는 포함하지 않습니다. 진단 빌드는 릴리즈로 패키징할 수 없습니다.

## 라이선스

프로그램은 GPL-3.0입니다. 원본 프로젝트와 외부 구성요소는 `THIRD_PARTY_NOTICES.md`, `LICENSE`, 각 구성요소의 라이선스를 참고하세요.
