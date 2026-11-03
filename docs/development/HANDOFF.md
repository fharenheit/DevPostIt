# 개발 인계

릴리즈 버전: 0.6.3. 소스코드 포맷터와 언어 확장, 마인드맵, 할 일, 달력, 공휴일 받아오기, 표 칸의 `<br>` 줄바꿈, 이름 변경 시 파일명 변경과 중복 확인, 창 폭을 쓰는 미리보기, 복원 후 다시 그리기를 포함합니다. 릴리즈 빌드 명령은 `README.md`의 ‘릴리즈 빌드’에 있습니다.

이 문서는 상태 기록이며 다음 기능을 자동으로 실행하라는 지시가 아닙니다. 시작할 때 현재 요청, 브랜치, 차이를 다시 확인하세요. 설계 결정은 [DESIGN.md](DESIGN.md), 지켜야 할 동작은 `AGENTS.md`에 있습니다.

## 최근 검증

0.6.3 릴리즈 기준 .NET 395개(Core 18, App 144, Storage 233), 웹 개발·제작 빌드 각 379개, WPF 하네스가 통과했습니다. 현재 변경 뒤에는 다시 실행해야 합니다.

자동 검증은 사용자 데스크톱에 입력을 보내지 않습니다. 아래는 실제로 확인하지 못했습니다.

- 화면 캡처: 드래그 영역, 전역 단축키, 배율이 다른 모니터
- 캡처 주석: 실제 마우스 그리기와 글자 입력(한글 IME 포함)
- draw.io Desktop 실제 연동 (개발 PC에 설치되어 있지 않음)
- GitHub에서 실제로 공휴일 파일 받기 (브랜치를 `main`에 올린 뒤에야 파일이 존재함)
- 마인드맵, 할 일, 달력의 실제 마우스 조작과 한글 IME 입력 (마인드맵에서 한글을 바로 쳐서 편집 시작, 조합 중 Enter 포함), 트레이 알림 표시, PNG 저장 창
- Dropbox 같은 동기화 폴더에서 메모 파일 이름이 바뀔 때의 동기화
- 메모 복원 후 흰 화면 해소 (자동 시험은 다시 그리기 동작까지만 확인하고 실제 표시는 확인하지 못함)
- 링크 확인 창과 실제 브라우저 열기, 이미지 폴더의 실제 탐색기 창
- Windows 휴지통 이동, 파일 선택 창
- 트레이의 ‘업데이트 설치’ 전체 흐름, 재시작을 포함한 실제 복원
- 실제 트레이 아이콘 클릭, 실제 Excel 복사, 실제 Windows IME 조합 중 툴바 동작

Smart App Control이 새로 빌드한 하네스 DLL을 가끔 차단합니다(0x800711C7). 정책을 바꾸거나 우회하지 말고 차단되면 그대로 보고합니다.

Java WASM과 파서는 앱에 포함합니다. Monaco 청크 크기와 Java 런타임의 일반 eval 분기에 대한 번들 경고는 기존 경고입니다. 웹 파일은 Mermaid와 언어별 정리 도구로 약 27MB입니다.

## macOS 버전 진행 상황

사용자가 승인한 작업입니다. 설계는 [MACOS_DESIGN.md](MACOS_DESIGN.md)이고 결정은 13절에 있습니다. Developer ID 없음, Windows 검증 환경 있음, Apple Silicon 먼저, 창이 있으면 Dock과 메뉴 막대를 표시하고 없으면 메뉴 막대 아이콘만 둡니다.

| 단계 | 상태 |
| --- | --- |
| 0. 관문 | 실제 한글 IME 시험 통과(2026-11-03, 두벌식). GitHub Actions는 첫 두 실행에서 Windows의 제작 빌드 웹 시험이 `dist` 없음으로 실패했고, 고친 워크플로를 확인 중입니다. Xcode와 `macos` 워크로드 조합이 맞지 않습니다 |
| 1. 이식성 | macOS 통과. Windows는 CI의 `dotnet test DevPostIt.sln`까지 통과했습니다. 끝나는 조건인 양쪽 CI 통과는 아직입니다 |
| 2. 공유 분리 | 코드 완료. `NoteSession`과 포트(`DevPostIt.Desktop`)로 WPF `MainWindow`를 옮겼습니다. 끝나는 조건인 Windows CI의 시험과 DesktopSmoke 통과는 **확인 중**입니다. 이 Mac에서는 WPF를 컴파일까지만 확인했습니다 |
| 3. 메모 MVP | MacSmoke 통과(Debug, Release). 실제 마우스·IME·메뉴 막대 클릭은 사용자 확인이 필요합니다 |
| 4~6 | 시작 전 |

### 3단계에서 동작하는 것

- 메뉴 막대: 왼쪽 클릭은 새 메모, 오른쪽·⌃클릭은 메뉴. 새 메모, 메모 목록, 휴지통, 모두 표시, 모두 접기, 종료가 동작하고 나머지 항목은 비활성입니다.
- 메모 창: WKWebView(`devpostit://`, `devpostit-assets://`), 자동 저장, 24×24 접기와 복원, X의 휴지통 확인, 휴지통 복원과 영구 삭제, 색상과 항상 위, 세션 복원, ⌘Q 저장.
- 아직 없는 것(4~5단계): 설정, 검색, 도움말, 정보, PDF, 클립보드 이미지, draw.io, 이미지 정리, 캡처, 마인드맵·할 일·달력 창, 알림.

### 메뉴 막대 오른쪽 클릭 (2026-11-03)

사용자가 실제 오른쪽 클릭에서 메뉴 대신 새 메모가 열린다고 보고했습니다. 앱이 받은 원본 이벤트는 `LeftMouseDown`, `button=0`이었고, 사용자는 Mac에 오른쪽 버튼이 설정되어 있지 않았던 것 같다고 판단했습니다. 그 뒤 사용자가 실제 클릭으로 메뉴가 나오는 것을 확인했습니다. 확인한 빌드는 버튼 위의 `StatusClickView`가 마우스 이벤트를 직접 받는 방식이며, 원인이 설정이었다면 이 방식이 꼭 필요하지는 않았을 수 있습니다. 사용자가 확인한 코드라 그대로 둡니다. MacSmoke의 클릭 시험은 이벤트 객체를 직접 넘기므로 실제 클릭을 대신하지 못합니다.

### MacSmoke가 확인하지 않는 것

- 합성 입력을 보내지 않으므로 실제 마우스·키보드, 메뉴 막대 아이콘의 모양과 클릭, 빨간 X와 노란 버튼의 실제 클릭, 실제 ⌘Q 키는 확인하지 않습니다. 종료 경로는 `NSApplication.Terminate`로 시험합니다.
- 실제 앱 호스트에서의 한글 IME는 아직 시험하지 않았습니다. 0단계 통과는 시험 도구의 WKWebView 결과입니다.
- 복구 경로는 `NoteSession`으로 그대로 옮겼으며 새 자동 시험이 없습니다.
- CI는 `DevPostIt.Mac`과 `MacSmoke`를 빌드하지 않습니다. 러너의 Xcode가 워크로드와 맞는지 확인하지 않았습니다.

### 다음에 할 일

1. CI의 Windows 작업에서 `dotnet test`, 웹 시험, DesktopSmoke 통과를 확인합니다. 이것이 2단계의 끝나는 조건입니다.
2. 실제 앱에서 위 항목을 사용자가 확인합니다. 시험용 메모를 쓰려면 `DEVPOSTIT_DATA_DIR`를 지정해 실행 파일을 직접 실행합니다.
3. `main` 병합 전에 Windows에서 WPF를 직접 시험합니다.

### 이 Mac의 준비 상태

- Xcode 27.2의 라이선스 동의와 첫 실행 설정이 끝났습니다.
- `.tools`의 SDK에 `macos` 워크로드 27.0.10742를 설치했습니다. 이 버전은 Xcode 27.1을 요구합니다. 사용자 결정(2026-11-03)에 따라 `DevPostIt.Mac`에 `ValidateXcodeVersion=false`를 넣어 Xcode 27.2로 빌드합니다. 지원되는 조합이 아니므로 맞는 워크로드가 나오면 지웁니다. CI는 아직 이 프로젝트를 빌드하지 않습니다.
- macOS 앱 빌드·실행: `dotnet build src/DevPostIt.Mac` 후 `open src/DevPostIt.Mac/bin/Debug/net10.0-macos/osx-arm64/DevPostIt.app`
- 기본 실행은 실제 데이터 폴더 `~/Library/Application Support/DevPostIt/Data`를 씁니다. 빌드는 `EDITOR_TARGET=macos` 웹 빌드(`editor-web/dist-macos`)를 번들에 넣습니다.
- `.tools`(Git 제외)에 .NET, Node, Playwright가 있습니다. `DevPostIt.sln`의 WPF 프로젝트는 macOS에서 빌드되지 않으므로 `DevPostIt.Mac.slnf`를 씁니다.

```zsh
export DOTNET_ROOT="$PWD/.tools/dotnet" DOTNET_CLI_HOME="$PWD/.tools/cli" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
export PATH="$PWD/.tools/dotnet:$PWD/.tools/node/node-v24.22.1-darwin-arm64/bin:$PATH"
export PLAYWRIGHT_BROWSERS_PATH="$PWD/.tools/playwright"
dotnet test DevPostIt.Mac.slnf --no-restore
(cd editor-web && EDITOR_BROWSER=webkit npm test)   # EDITOR_BROWSER가 없으면 Chromium
```

### macOS에서 확인한 결과 (2026-11-02)

- .NET: Core 18, EditorHost(`DevPostIt.App.Tests`) 144, Storage 234 모두 통과.
- 웹 개발 모드 379개: Chromium 통과 337, 실패 42. WebKit 통과 323, 실패 56.
  - 공통 실패 42개는 Windows에서 통과하던 시험이라 OS 차이로 보며, Ctrl/⌘ 차이가 유력합니다. 실패 메시지를 하나씩 확인하지는 않았습니다.
  - WebKit에서만 실패한 14개는 4단계에서 분류합니다: `bridge.spec.ts:159`, `font-settings.spec.ts:8,115,148,167`, `fonts.spec.ts:4,19`, `ime.spec.ts:4`, `note-features.spec.ts:44,233`, `preview-tools.spec.ts:65,81,173,186`. `ime.spec.ts:4`(CDP)와 `note-features.spec.ts`의 2개(클립보드 권한, PDF)는 시험 도구의 한계일 수 있습니다. `bridge.spec.ts:159`(한글 조합 중간값)는 먼저 확인합니다.
- 실제 WKWebView(macOS 26.6.2, 작은 시험 페이지)에서 확인한 내용은 설계 문서 5절에 있습니다. 제작 번들을 `.NET` 호스트로 실행하는 것은 이후 MacSmoke가 확인합니다.

### IME 시험 결과 (2026-11-03)

**사용자가 화면을 보고 모두 정상으로 판단해 통과했습니다.** 입력기는 두벌식이고, 다른 입력기는 시험하지 않았습니다. 제작 번들을 시험 도구의 WKWebView에 띄운 결과이며 실제 앱 호스트에서 시험한 것은 아닙니다.

- **조합 이벤트가 전체 시험에서 0건입니다.** 한글 키의 `keydown`은 `keyCode=229`, `isComposing=false`였습니다. `compositionstart`와 `event.isComposing`에 기대는 코드(`editor.ts`, `bridge.ts`, `mindmap.ts`, `todo.ts`)의 보호가 이 환경에서는 동작하지 않고, 조합 도중의 글자도 `note.changed`로 나갑니다.
- 기록으로 확인: 메모 한글 직후 Enter, Backspace, 실행 취소·다시 실행(안내 문구 유지), 이름 변경 직후 Enter, 할 일과 새 목록 직후 Enter, 마인드맵 편집 칸 직후 Enter와 Esc. 화면에 표시된 글자는 메모 외에는 로그에 없어 사용자의 관찰을 따릅니다.
- 마인드맵에서 주제를 선택하고 바로 `가나다`를 치면 사용자의 화면에서 정상으로 나왔습니다. 로그에 첫 자음이 없는 것은 캔버스의 첫 키가 `keyCode=229`가 아니라 도구의 기록 조건에 걸리지 않았기 때문으로 보이며, 결함의 근거가 아닙니다.
- 로그 없이 사용자의 관찰로만 확인한 항목: 메모 조합 중 ← 후 입력(`한가글`), 마인드맵 이름 칸 직후 Enter.

### IME 시험 도구

시험 도구 `ime-gate`(Swift)는 세션 임시 폴더에 두어 저장소에 없고, 다음 세션에서 없을 수 있습니다. 다시 만들 때의 조건입니다.

- `editor-web/dist`의 `index.html`(브리지 흉내 포함), `mindmap.html`, `todo.html`을 WKWebView 창 셋으로 띄웁니다.
- 비영구 웹 저장소를 쓰고 사용자 메모를 건드리지 않으며 합성 입력을 보내지 않습니다.
- 조합 이벤트와 메모 본문(리비전 포함), 이름 변경, 페이지 메시지를 파일에 기록해 사용자의 관찰과 비교합니다.
- Command Line Tools의 `swiftc`는 `-sdk /Library/Developer/CommandLineTools/SDKs/MacOSX26.5.sdk`가 필요했습니다.

## 다음 후보

코드 서명, CI 빌드, 설치 패키지, 태그·폴더 관리 화면, 가져오는 Markdown의 참조 이미지 복사, 단축키 설정, 캡처 대상 메모 선택. 사용자가 지정한 작업을 우선하고 이 목록을 일괄 구현하지 마세요.

## 이어받기 점검

1. `git status --short`와 최근 커밋을 확인합니다.
2. `AGENTS.md`와 관련 구현 파일을 읽고 현재 요구를 반영합니다.
3. 사용자가 앱을 실행 중일 수 있습니다. 검증에는 별도 데이터와 WebView 프로필을 쓰고, 빌드 파일이 잠기면 `-p:OutDir`로 다른 폴더에 빌드합니다.
4. 요청한 변경을 실패 테스트부터 구현하고 검증한 뒤 커밋합니다. 푸시와 병합은 사용자 지시를 확인합니다.
