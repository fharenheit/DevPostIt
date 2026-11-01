# 훈민 노트

Windows용 WPF 기반 Markdown 메모 앱. 현재 최소 시작 창과 도메인, 작업 2의 Markdown·Frontmatter 코덱 및 파일 저장소를 구현했습니다. 저장소는 아직 앱에 연결하지 않았습니다.

필수 도구는 .NET SDK 10.0.403입니다. `global.json`에 버전을 고정했으며 패키지는 각 프로젝트의 `packages.lock.json`으로 고정합니다.

```powershell
dotnet restore HunminNotes.sln --locked-mode
dotnet test HunminNotes.sln --no-restore
dotnet build HunminNotes.sln --no-restore
dotnet run --project src/HunminNotes.App
```

이 환경에서는 SDK를 `.tools/dotnet`에 설치했습니다. 해당 도구를 사용하려면 다음과 같이 준비합니다.

```powershell
$env:DOTNET_CLI_HOME = "$PWD/.tools/cli"
$env:PATH = "$PWD/.tools/dotnet;$PWD/.tools/node/node-v24.22.1-win-x64;$env:PATH"
```

검증 결과는 `docs/development/TASK_1.md`, `docs/development/TASK_2.md`에, 원본 조사 상태는 작업 1 기록에 남겼습니다.

작업 3의 Monaco 편집기는 `editor-web`에서 별도로 실행할 수 있습니다. 소스 편집·읽기 전용 미리보기, D2 Ligature·JetBrains Mono 선택과 합자 설정을 제공합니다. 실행 방법은 [편집기 안내](editor-web/README.md)를 참고하세요. WPF 연결은 다음 단계입니다.
