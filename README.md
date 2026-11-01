# Post It for Developer (DevPostIt)

개발자를 위한 Windows용 Markdown 포스트잇 메모앱입니다. 훈민정음의 부속물인 훈민메모패드의 사용성을 제공하면서 개발자를 위한 Markdown 지원 그리고 Snagit의 Object Capture 기능으로 바로 캡춰하고 파일로 저장하고, MD 파일에 링크까지 걸어주는 메모앱입니다. WPF와 WebView2로 만들었고, 메모를 개별 Markdown 파일로 저장하며 Monaco 편집기를 사용합니다.

<p align="center">
  <img src="docs/images/source-editing.png" alt="소스 편집 화면" width="45%">
  <img src="docs/images/preview.png" alt="미리보기 화면" width="45%">
</p>

## 기능

**메모와 창**

- 여러 메모를 각각의 창으로 사용
- 24px 아이콘으로 접기 (제목 표시줄 더블클릭 또는 최소화), 클릭으로 복원, 드래그로 이동
- 메모별 항상 위와 색상 6가지
- 창 위치·크기, 접힌 아이콘 위치, 열어 둔 메모 복원
- 트레이 메뉴: 새 메모, 화면 캡처, 검색, 메모 목록, 휴지통, 설정, 도움말, 정보, 모두 표시/접기, 종료
- 트레이로만 시작 옵션

**편집**

- Monaco 소스 편집과 미리보기
- 서식 아이콘 툴바와 단축키: 제목, 굵게, 기울임, 취소선, 코드, 인용, 목록, 할 일, 링크, 표, 구분선
- 찾기·바꾸기, 줄 이동, 명령 팔레트, 여러 커서, 줄 옮기기·복제, 주석, 구역 접기, 자동 완성
- 현재 줄 강조, 줄 번호, 자동 줄바꿈, 미니맵, 공백 문자, 괄호 색, 구역 제목 고정
- 미리보기의 체크박스 클릭과 코드 복사

**코드**

- Monaco가 지원하는 언어와 JSON의 코드 블록 하이라이트 (소스 편집, 미리보기)
- 코드 블록 정리 19개 언어: JSON, JSON5, YAML, Java, JavaScript, JSX, Flow, TypeScript, TSX, HTML, Vue, Angular, Handlebars, CSS, SCSS, Less, GraphQL, Markdown, MDX

**붙여넣기**

- Excel, Google Sheets에서 복사한 셀을 Markdown 표로 변환
- 스크린샷과 복사한 이미지 파일을 메모에 저장하고 미리보기와 PDF에 표시
- 미리보기 버튼 오른쪽의 폴더 아이콘으로 메모의 이미지 폴더 열기

**화면 캡처**

- 커서 아래 UI 요소를 강조하고 클릭해 캡처 (휠로 상위·하위 요소 선택)
- 드래그 영역 캡처, 캡처 전 대기
- 메모의 버튼과 Ctrl+Shift+C, 트레이, 전역 단축키 Ctrl+Shift+PrintScreen
- 캡처한 그림을 최근 메모의 커서 위치에 삽입

**검색과 내보내기**

- 전체 메모 검색 (제목, 본문, 태그)
- PDF로 저장

**데이터 보호**

- Markdown 파일이 원본이며 SQLite 검색 DB는 다시 만들 수 있음
- 500ms 자동 저장과 크래시 복구
- 휴지통에서 복원과 영구 삭제
- 전체 백업과 복원 (복원 전 데이터는 지우지 않음)
- 앱 밖에서 바뀐 파일을 다시 불러오고, 편집 중이면 양쪽 내용을 보존
- 데이터 폴더 변경 (OneDrive 폴더도 선택 가능)

**글꼴과 설정**

- D2 Ligature, JetBrains Mono, Consolas, 설치된 고정폭 글꼴, 합자
- 본문 글꼴 선택
- 전체, 편집기, 미리보기 글자 크기를 각각 설정하고 줄 간격 조절
- 메모에서 Ctrl+=/-/0으로 전체 글자 크기 조절
- 별도 설정 창, 설정은 열린 메모에 바로 적용

**업데이트**

- GitHub 릴리즈에서 새 버전을 확인해 알리고, 트레이에서 고르면 해시와 시험 실행을 확인한 뒤 설치
- 기능 안내는 트레이의 ‘도움말’에서 볼 수 있음

**아직 없는 기능:** `id`가 없는 외부 Markdown 파일 가져오기, 태그·폴더 관리 화면, 설치 패키지, 코드 서명

## 개발 환경

필수 도구는 .NET SDK 10.0.403, Node.js 24.22.1/npm 11.19.1 및 WebView2 Runtime입니다. SDK와 패키지 버전은 매니페스트 및 잠금 파일로 고정합니다. WPF 빌드가 웹 편집기 제작 빌드와 실행 폴더 복사를 수행하므로 개발 서버를 별도로 띄우지 않아도 됩니다.

```powershell
cd D:\Projects\DevPostIt
$env:DOTNET_ROOT = "$PWD/.tools/dotnet"
$env:DOTNET_CLI_HOME = "$PWD/.tools/cli"
& "$env:DOTNET_ROOT/dotnet.exe" run --project src/DevPostIt.App
```

이 환경에서는 SDK를 `.tools/dotnet`에 설치했습니다. 해당 도구를 사용하려면 다음과 같이 준비합니다.

```powershell
$env:DOTNET_CLI_HOME = "$PWD/.tools/cli"
$env:PATH = "$PWD/.tools/dotnet;$PWD/.tools/node/node-v24.22.1-win-x64;$env:PATH"
```

UI 기본 글자 크기는 15px, Monaco 본문은 13px입니다. 트레이의 ‘설정’(또는 메모에서 Ctrl+,)으로 여는 별도 설정 창에서 `+`·`−`로 전체 크기를 함께 조절합니다. 설정과 이름 변경 화면의 글자는 14px로 고정합니다. 글꼴 제어는 메모 화면에 표시하지 않습니다. UI는 10~24px, 편집기는 UI보다 2px 작게 유지합니다. 글꼴(D2 Ligature·JetBrains Mono), 합자, 크기 설정은 재실행 후에도 기억합니다.

설정은 `%LOCALAPPDATA%/DevPostIt/WebView2` 프로필의 로컬 저장소에 보관하며 열린 다른 메모에도 적용합니다. 메모는 `%LOCALAPPDATA%/DevPostIt/Data/notes/Inbox/`에 개별 Markdown 파일로 저장합니다. 이전 버전의 문서 폴더 데이터는 첫 실행 때 복사해 옮기며 원본은 남겨 둡니다. 이 위치는 OneDrive로 동기화되지 않습니다. 변경을 복구 저널에 기록한 뒤 마지막 입력에서 500ms 후 자동 저장하며 Ctrl+S와 정상 종료 시에도 저장합니다. 크래시 후 초안이 있으면 복구 여부를 묻습니다.

Windows 제목 표시줄에는 항상 ‘Post It for Developer’를 표시합니다. 메모 이름은 본문 위 제목과 트레이의 메모 목록에서 확인합니다. 실행하면 트레이에 노란 메모장 아이콘이 등록됩니다. 왼쪽 클릭은 새 메모, 우클릭은 메모 목록·설정·모두 표시/접기·종료 메뉴입니다. 메모 화면의 제품명·새 메모·이름 변경 버튼은 표시하지 않습니다. 본문 위 제목을 더블클릭하면 이름을 수정합니다. Windows 제목 표시줄을 더블클릭하거나 최소화하면 그림 없는 24px 노란 아이콘으로 접히며, 클릭하면 복원하고 드래그하면 이동합니다.

메모 창의 X는 ‘메모를 휴지통으로 옮기시겠습니까?’를 묻습니다. ‘예’는 최신 입력을 저장한 뒤 메모를 휴지통으로 옮기고, ‘아니요’는 편집을 계속합니다. 트레이의 ‘휴지통’에서 복원하거나 영구 삭제합니다. 앱을 종료하면서 메모를 보존하려면 트레이의 ‘종료’를 사용하세요.

트레이의 ‘메모 검색’이나 메모에서 Ctrl+Shift+F로 검색 창을 엽니다. 메모 머리글의 버튼으로 항상 위와 메모 색상을 정합니다. 창 위치·크기와 열어 둔 메모는 다음 실행에도 복원하며, 설정에서 ‘트레이로만 시작’을 켤 수 있습니다. 설정의 ‘폴더 변경’으로 데이터 폴더를 옮기면 다음 실행부터 적용합니다(OneDrive 폴더도 선택 가능). 앱 밖에서 파일이 바뀌면 다시 불러오고, 편집 중이었다면 외부 내용을 ‘(외부 변경)’ 메모로 따로 보존합니다.

서식 단축키는 Ctrl+B·I·E·K, Ctrl+Shift+X, Ctrl+1~3입니다. 스크린샷 같은 이미지나 탐색기에서 복사한 이미지 파일을 소스 편집에 붙여넣으면 `notes/assets`에 저장하고 미리보기에 표시합니다. 글자가 함께 복사된 경우에는 글자를 붙여넣습니다. 미리보기에서 체크박스를 눌러 원문을 바꾸고 코드 블록을 복사할 수 있습니다.

코드 블록 안에 커서를 두고 ‘코드 정리’ 또는 Ctrl+Alt+F를 누르세요. JSON·JSON5·YAML·Java·JavaScript·JSX·Flow·TypeScript·TSX·HTML·Vue·Angular·Handlebars·CSS·SCSS·Less·GraphQL·Markdown·MDX를 지원하며, 언어가 없는 블록은 ‘코드 언어’를 선택합니다. 미리보기와 소스 편집의 코드 블록은 Monaco가 지원하는 언어(C++, Go, Rust, PHP 등)와 JSON에 맞게 색으로 구분합니다. Excel 등에서 복사한 표는 Markdown 표로 붙여넣습니다. 보기 전환 아래의 아이콘 툴바로 제목·굵게·기울임·취소선·코드·인용·목록·링크·표·구분선 서식을 넣습니다. 코드 부분만 정리하며 한 번의 실행 취소로 되돌릴 수 있습니다. 최상위의 닫힌 코드 블록을 대상으로 하며 문법 오류가 있으면 원문을 유지합니다. 포맷터와 Java 구문 파일은 오프라인 사용을 위해 앱에 포함합니다.

복구 기록과 검색 DB는 `%LOCALAPPDATA%/DevPostIt/Data/.hunmin/`에 있습니다. 설정 창의 ‘폴더 열기’로 Markdown과 DB가 있는 데이터 폴더를 열 수 있습니다. 같은 데이터 폴더는 한 프로세스만 사용할 수 있습니다. 데이터 위치를 바꾸려면 실행 전 `DEVPOSTIT_DATA_DIR`을 지정합니다. 저장 실패·파일 외부 변경·메시지 크기 제한은 화면에 표시하며 기존 파일을 임의로 덮어쓰지 않습니다.

검증 결과는 `docs/development/`에 기록합니다. 추가 기능 검증은 [코드 정리·트레이 검증](docs/development/FORMATTING_TRAY.md), 브라우저 편집기를 별도로 실행하는 방법은 [편집기 안내](editor-web/README.md)를 참고하세요.

Claude에서도 이어서 개발할 수 있도록 [공통 작업 지침](AGENTS.md), [Claude 진입 문서](CLAUDE.md), [현재 상태 인계](docs/development/HANDOFF.md)를 제공합니다. 시작 방법은 [Claude 작업 안내](docs/development/CLAUDE_SETUP.md)를 참고하세요.

## 글꼴

설정에서 편집 글꼴(D2 Ligature, JetBrains Mono, Consolas, 설치된 고정폭 글꼴), 본문 글꼴, 전체 글자 크기, 편집기 글자 크기, 미리보기 글자 크기, 줄 간격, 합자를 바꾸고 예시로 바로 확인합니다. 미리보기 크기를 따로 정하지 않으면 소스 편집 크기와 같습니다. ‘기본값으로’는 글꼴과 편집기 표시 설정을 처음 상태로 되돌립니다. 메모에서는 Ctrl+=, Ctrl+-, Ctrl+0으로 전체 크기를 바꿉니다. 글꼴 설정은 전체 백업에 함께 들어가고 복원 때 적용됩니다.

## 편집 기능

Monaco의 편집 기능을 사용합니다. 현재 줄 강조, 찾기(Ctrl+F)와 바꾸기(Ctrl+H), 줄 이동(Ctrl+G), 명령 팔레트(F1), 여러 커서(Ctrl+D, Alt+클릭), 줄 이동·복제(Alt/Shift+Alt+방향키), 주석(Ctrl+/), 구역 접기(Ctrl+Shift+[ ]), 자동 완성(Ctrl+Space), 오른쪽 클릭 메뉴가 있습니다. 설정에서 줄 번호, 자동 줄바꿈, 미니맵, 공백 문자, 괄호 색, 구역 제목 고정을 켜고 끕니다.

메모 안의 링크는 안전을 위해 열지 않으며, 코드 언어별 언어 서비스(타입 검사 등)는 넣지 않았습니다. 찾기와 바꾸기는 소스 편집에서만 동작합니다. Ctrl+K는 링크 서식에 쓰므로 Monaco의 Ctrl+K 조합 단축키는 동작하지 않고, 해당 명령은 F1에서 실행합니다.

트레이의 ‘도움말’에서 기능 안내를, ‘정보’에서 버전, 개발자, GitHub 주소를 볼 수 있습니다.

## 백업과 복원

설정의 ‘백업’은 메모, 첨부 이미지, 휴지통, 복구 기록을 zip 파일 하나로 저장합니다. 검색 DB는 다시 만들 수 있어 넣지 않고, 앱 설정과 창 위치도 넣지 않습니다.

‘복원’은 백업을 검증한 뒤 앱을 다시 시작해 모든 메모를 바꿉니다. 현재 데이터는 지우지 않고 데이터 폴더 옆의 `.before-restore-날짜` 폴더로 옮깁니다. 필요 없으면 직접 지우면 됩니다.

## PDF로 저장

메모 머리글의 ‘PDF로 저장’ 버튼은 제목과 미리보기 내용을 A4 PDF로 저장합니다. 원문은 바꾸지 않습니다.

## 자동 업데이트

게시된 exe로 실행하면 시작 후와 하루 간격으로 GitHub 릴리즈의 새 버전을 확인해 알립니다. 설치는 트레이의 ‘업데이트 설치’를 눌렀을 때만 합니다. 내려받은 파일의 크기와 SHA-256을 릴리즈 정보와 비교하고, 시험 실행(`--update-self-test`)이 통과해야 교체합니다. 실패하면 현재 버전을 유지합니다. 자동 확인은 설정에서 끌 수 있고, 소스 실행에서는 동작하지 않습니다. exe에 코드 서명이 없어 해시는 GitHub 릴리즈 자체가 안전하다는 전제로만 위변조를 막습니다.

## Windows 로그인 시 자동 실행

Windows에 로그인할 때 앱이 시작되게 하려면 현재 사용자의 시작프로그램 폴더에 바로 가기를 만듭니다. 관리자 권한은 필요 없습니다. 이 방법은 로그인한 뒤에 실행되며, PC를 켜고 로그인하기 전에는 실행되지 않습니다.

먼저 트레이의 ‘종료’로 앱을 끄고, 내려받은 exe가 있는 폴더의 PowerShell에서 실행합니다. exe를 고정 위치에 복사한 뒤 바로 가기를 만듭니다. 복사한 exe를 옮기거나 지우면 자동 실행이 동작하지 않습니다.

```powershell
$target = "$env:LOCALAPPDATA\Programs\DevPostIt\DevPostIt.exe"
New-Item -ItemType Directory -Force (Split-Path $target) | Out-Null
Copy-Item .\DevPostIt-0.4.0-win-x64.exe $target
$startup = [Environment]::GetFolderPath('Startup')
$shortcut = (New-Object -ComObject WScript.Shell).CreateShortcut("$startup\DevPostIt.lnk")
$shortcut.TargetPath = $target
$shortcut.Save()
```

명령 대신 직접 만들 수도 있습니다. Win+R에서 `shell:startup`을 실행해 열린 폴더에 exe의 바로 가기를 넣으면 됩니다.

- **동작:** 로그인하면 트레이 아이콘이 등록되고 최근에 수정한 메모 창 하나가 열립니다. 창을 숨긴 채 트레이로만 시작하는 옵션은 아직 없습니다.
- **끄기:** 설정 > 앱 > 시작 프로그램에서 끄거나, 아래 명령으로 바로 가기를 지웁니다. 메모는 지워지지 않습니다.
- **새 버전:** 트레이의 ‘종료’로 앱을 끈 뒤 같은 경로의 exe를 새 파일로 덮어씁니다.
- **차단될 때:** exe에 코드 서명이 없어 SmartScreen 경고가 뜨거나 Smart App Control에 차단될 수 있으며, 차단되면 자동 실행도 되지 않습니다.

```powershell
Remove-Item "$([Environment]::GetFolderPath('Startup'))\DevPostIt.lnk"
```

## 릴리즈 빌드

Windows x64용 단일 exe는 저장소 루트에서 만듭니다. .NET을 함께 포함하므로 별도 설치가 필요 없고 WebView2 Runtime만 있으면 실행됩니다. 이 빌드의 패키지 잠금 파일은 `obj` 아래에 따로 만들어져 저장소의 잠금 파일을 바꾸지 않습니다.

```powershell
dotnet publish src/DevPostIt.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=true -p:EnableCompressionInSingleFile=true -o .tools/release/publish
```

버전은 `src/DevPostIt.App/DevPostIt.App.csproj`의 `Version`에 있습니다.
