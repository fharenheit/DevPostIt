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
| 0. 관문 | **진행 중.** 실제 한글 IME 시험 결과를 사용자에게서 아직 받지 못했습니다. GitHub Actions 첫 실행(2026-11-03, `e941a0f`)에서 Windows의 제작 빌드 웹 시험이 실패했고 원인은 확인하지 못했습니다. Xcode와 `macos` 워크로드 조합이 맞지 않습니다 |
| 1. 이식성 | macOS 통과. Windows는 CI의 `dotnet test DevPostIt.sln`까지 통과했습니다. 끝나는 조건인 양쪽 CI 통과는 아직입니다 |
| 2~6 | 시작 전. **IME 관문을 통과해야 2단계로 넘어갑니다.** 실패하면 CEF 안을 다시 검토합니다 |

### 다음에 할 일

1. 사용자에게 IME 결과를 묻습니다. 시험 항목은 메모의 조합 중 Enter, 방향키·Backspace, ⌘Z/⌘⇧Z, 제목 이름 변경, 마인드맵 바로 입력, 할 일 Enter입니다. 기록 파일이 있다는 것만으로 통과로 판단하지 않습니다.
2. CI의 Windows 작업 실패 원인을 확인합니다. 제작 빌드 웹 시험에서 멈춰 DesktopSmoke는 실행되지 않았고, 러너에서 동작하는지 아직 모릅니다. 로그는 인증 없이 받을 수 없습니다(403). macOS 작업은 .NET 시험과 웹 빌드만 하며, 웹 시험은 실패를 분류하는 4단계에서 추가합니다.
3. 3단계 전에 Xcode와 `macos` 워크로드 조합을 정합니다.
4. 관문을 통과하면 2단계 `DevPostIt.Desktop` 분리를 시작합니다. WPF 변경은 Windows에서 시험한 뒤 병합합니다.

### 이 Mac의 준비 상태

- Xcode 27.2의 라이선스 동의와 첫 실행 설정이 끝났습니다.
- `.tools`의 SDK에 `macos` 워크로드 27.0.10742를 설치했습니다. 이 버전은 Xcode 27.1을 요구해 27.2에서는 기본 빌드가 오류로 멈춥니다. `-p:ValidateXcodeVersion=false`로 검사를 끄면 임시 템플릿 앱은 경고·오류 없이 빌드되고 애드혹 서명된 arm64 번들이 나오지만(2026-11-03), 실행해 보지 않았고 지원되는 조합도 아닙니다. 3단계 전에 Xcode 27.1 또는 맞는 워크로드 조합을 정해야 합니다.
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
- 실제 WKWebView(macOS 26.6.2, 작은 시험 페이지)에서 확인한 내용은 설계 문서 5절에 있습니다. 제작 번들에서 `.NET` 호스트로 실행한 적은 없습니다.

### IME 시험 중간 결과 (2026-11-03)

실제 입력 1회의 기록입니다. 합격 판정이 아닙니다.

- 메모 창의 한글은 ‘한글이 잘 입력이 되고..’처럼 올바른 글자로 기록되었고, 조합 중 오타도 고쳐졌습니다.
- **조합 이벤트가 0건입니다.** 한글 키의 `keydown`은 `keyCode=229`, `isComposing=false`였고 `compositionstart`와 조합 `beforeinput`이 없었습니다. 입력 소스 종류는 확인하지 못했습니다.
- 그래서 `compositionstart`와 `event.isComposing`에 기대는 코드(`editor.ts`, `bridge.ts`, `mindmap.ts`, `todo.ts`)의 보호가 이 환경에서 동작하지 않을 수 있습니다. 조합 도중의 글자도 `note.changed`로 나갔습니다.
- 기록에 없는 항목: 조합 중 Enter, 이름 변경, 마인드맵, 할 일. 실행 취소는 기록만으로 판단할 수 없습니다.

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
