# DevPostIt

Windows용 WPF + WebView2 Markdown 메모 앱입니다. Monaco 소스 편집기와 미리보기, 코드 블록 정리, 이름 변경, 여러 메모의 자동 저장·복구, 트레이와 작은 포스트잇 아이콘을 제공합니다. SQLite 검색 인덱스를 사용하며 중앙 검색 화면과 외부 파일 감시는 후속 단계입니다.

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

UI 기본 글자 크기는 15px, Monaco 본문은 13px입니다. 트레이의 ‘설정’(또는 Ctrl+,)에서 `+`·`−`로 전체 크기를 함께 조절합니다. 글꼴 제어는 메모 화면에 표시하지 않습니다. UI는 10~24px, 편집기는 UI보다 2px 작게 유지합니다. 글꼴(D2 Ligature·JetBrains Mono), 합자, 크기 설정은 재실행 후에도 기억합니다.

설정은 `%LOCALAPPDATA%/DevPostIt/WebView2` 프로필의 로컬 저장소에 보관하며 열린 다른 메모에도 적용합니다. 메모는 문서 폴더의 `DevPostIt/notes/Inbox/`에 개별 Markdown 파일로 저장합니다. 변경을 복구 저널에 기록한 뒤 마지막 입력에서 500ms 후 자동 저장하며 Ctrl+S와 정상 종료 시에도 저장합니다. 크래시 후 초안이 있으면 복구 여부를 묻습니다.

실행하면 트레이에 노란 메모장 아이콘이 등록됩니다. 왼쪽 클릭은 새 메모, 우클릭은 메모 목록·설정·모두 표시/접기·종료 메뉴입니다. 메모 화면의 제품명·새 메모·이름 변경 버튼은 표시하지 않습니다. 제목이나 Windows 제목 표시줄을 더블클릭하면 이름을 수정합니다. 최소화하면 그림 없는 24px 노란 아이콘으로 접히며, 클릭하면 복원하고 드래그하면 이동합니다.

메모 창의 X는 ‘메모를 영구 삭제하시겠습니까?’를 묻습니다. ‘예’는 해당 메모 파일·복구 기록·검색 항목을 삭제하고, ‘아니요’는 편집을 계속합니다. 앱을 종료하면서 메모를 보존하려면 트레이의 ‘종료’를 사용하세요.

코드 블록 안에 커서를 두고 ‘코드 정리’ 또는 Ctrl+Alt+F를 누르세요. JSON·YAML·Java·JavaScript·TypeScript·HTML·CSS를 지원하며, 언어가 없는 블록은 ‘코드 언어’를 선택합니다. 코드 부분만 정리하며 한 번의 실행 취소로 되돌릴 수 있습니다. 최상위의 닫힌 코드 블록을 대상으로 하며 문법 오류가 있으면 원문을 유지합니다. 포맷터와 Java 구문 파일은 오프라인 사용을 위해 앱에 포함합니다.

복구 기록과 검색 DB는 `DevPostIt/.hunmin/`에 있습니다. 같은 데이터 폴더는 한 프로세스만 사용할 수 있습니다. 데이터 위치를 바꾸려면 실행 전 `DEVPOSTIT_DATA_DIR`을 지정합니다. 저장 실패·파일 외부 변경·메시지 크기 제한은 화면에 표시하며 기존 파일을 임의로 덮어쓰지 않습니다.

검증 결과는 `docs/development/`에 기록합니다. 추가 기능 검증은 [코드 정리·트레이 검증](docs/development/FORMATTING_TRAY.md), 브라우저 편집기를 별도로 실행하는 방법은 [편집기 안내](editor-web/README.md)를 참고하세요.
