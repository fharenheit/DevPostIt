# 훈민 노트

Windows용 WPF 기반 Markdown 메모 앱. 현재 작업 1의 최소 시작 창과 저장소에 의존하지 않는 도메인을 구현했습니다.

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

검증 결과와 원본 조사 상태는 `docs/development/TASK_1.md`에 기록했습니다.
