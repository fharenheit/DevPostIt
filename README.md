# DevPostIt

Windows용 WPF + WebView2 Markdown 메모 앱입니다. WPF에서 Monaco 소스 편집기와 읽기 전용 미리보기를 직접 실행합니다. 파일 저장·검색 인덱스·자동 저장은 후속 단계입니다.

필수 도구는 .NET SDK 10.0.403, Node.js 24.22.1/npm 11.19.1 및 WebView2 Runtime입니다. SDK와 패키지 버전은 매니페스트 및 잠금 파일로 고정합니다. WPF 빌드가 웹 편집기 제작 빌드와 실행 폴더 복사를 수행하므로 개발 서버를 별도로 띄우지 않아도 됩니다.

```powershell
cd D:\Projects\DevPostIt
$env:DOTNET_ROOT = "$PWD/.tools/dotnet"
$env:DOTNET_CLI_HOME = "$PWD/.tools/cli"
& "$env:DOTNET_ROOT/dotnet.exe" run --project src/HunminNotes.App
```

이 환경에서는 SDK를 `.tools/dotnet`에 설치했습니다. 해당 도구를 사용하려면 다음과 같이 준비합니다.

```powershell
$env:DOTNET_CLI_HOME = "$PWD/.tools/cli"
$env:PATH = "$PWD/.tools/dotnet;$PWD/.tools/node/node-v24.22.1-win-x64;$env:PATH"
```

UI 기본 글자 크기는 15px, Monaco 본문은 13px입니다. `+`·`−`는 모든 UI 글자와 편집기 본문을 한 단계씩 함께 조절합니다. UI는 10~24px, 편집기는 UI보다 2px 작게 유지합니다. 글꼴(D2 Ligature·JetBrains Mono), 합자, 크기 설정은 앱을 정상 종료하고 다시 열어도 기억합니다. 이미 선택한 값이 있으면 기본값보다 저장 설정을 우선합니다.

설정은 `%LOCALAPPDATA%/DevPostIt/WebView2` 프로필의 로컬 저장소에 보관합니다. 메모 본문은 아직 파일에 저장하지 않으며, Ctrl+S를 누르면 앱에서 저장 기능 연결 전임을 안내합니다.

검증 결과는 `docs/development/TASK_1.md`, `TASK_2.md`, `TASK_4.md`에 기록합니다. 브라우저 편집기를 별도로 실행하는 방법은 [편집기 안내](editor-web/README.md)를 참고하세요.
