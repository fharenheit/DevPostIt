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
- 캡처 그림을 실제 앱에 붙여 넣기 (Windows의 클립보드 쓰기는 DesktopSmoke가 CI에서만 확인합니다)
- 실제 트레이 아이콘 클릭, 실제 Excel 복사, 실제 Windows IME 조합 중 툴바 동작

Smart App Control이 새로 빌드한 하네스 DLL을 가끔 차단합니다(0x800711C7). 정책을 바꾸거나 우회하지 말고 차단되면 그대로 보고합니다.

Java WASM과 파서는 앱에 포함합니다. Monaco 청크 크기와 Java 런타임의 일반 eval 분기에 대한 번들 경고는 기존 경고입니다. 웹 파일은 Mermaid와 언어별 정리 도구로 약 27MB입니다.

## macOS 버전 진행 상황

사용자가 승인한 작업입니다. 설계는 [MACOS_DESIGN.md](MACOS_DESIGN.md)이고 결정은 13절에 있습니다. Developer ID 없음, Windows 검증 환경 있음, Apple Silicon 먼저, 창이 있으면 Dock과 메뉴 막대를 표시하고 없으면 메뉴 막대 아이콘만 둡니다.

| 단계 | 상태 |
| --- | --- |
| 0. 관문 | 실제 한글 IME 시험 통과(2026-11-03, 두벌식). GitHub Actions는 첫 두 실행에서 Windows의 제작 빌드 웹 시험이 `dist` 없음으로 실패했고, 고친 워크플로를 확인 중입니다. Xcode와 `macos` 워크로드 조합이 맞지 않습니다 |
| 1. 이식성 | macOS 통과. Windows는 CI의 `dotnet test DevPostIt.sln`까지 통과했습니다. 끝나는 조건인 양쪽 CI 통과는 아직입니다 |
| 2. 공유 분리 | 코드 완료. `NoteSession`과 포트(`DevPostIt.Desktop`)로 WPF `MainWindow`를 옮겼습니다. Windows CI의 DesktopSmoke가 불안정합니다(아래 ‘CI 실패 원인’). 이 Mac에서는 WPF를 컴파일까지만 확인했습니다 |
| 3. 메모 MVP | MacSmoke 통과(Debug, Release). 실제 마우스·IME·메뉴 막대 클릭은 사용자 확인이 필요합니다 |
| 4. 보조 창 | 구현 완료. 끝나는 조건인 Playwright WebKit은 이 Mac에서 통과했습니다(결과는 아래). CI에 WebKit 시험을 넣었으나 CI 결과는 아직 없습니다 |
| 5. 이미지·캡처 | 구현 완료. 끝나는 조건인 **수동 시험표는 아직 하지 않았습니다**(‘수동 시험이 남은 항목’) |
| 6. 배포 | 시작 전. 서명과 공증 결정이 필요합니다 |

### macOS에 있는 기능

- 메뉴 막대: 어느 버튼으로 클릭해도 메뉴가 열립니다(사용자 요청, 2026-11-03). 새 메모는 메뉴의 ‘새 메모’로 만듭니다.
- 메모 창: WKWebView(`devpostit://`, `devpostit-assets://`), 자동 저장, 24×24 접기와 복원, X의 휴지통 확인, 휴지통 복원과 영구 삭제, 색상과 항상 위, 세션 복원, ⌘Q 저장, PDF, 클립보드 이미지, draw.io.
- 보조 창: 설정, 검색, 도움말, 정보, 포맷터, 마인드맵, 할 일, 달력. `MacWebWindow.cs`, `MacToolWindows.cs`, `MacInfoWindows.cs`에 있습니다.
- 그 밖: 알림(`MacNotifications.cs`), 할 일 알림, 공휴일 받기, 백업과 복원, Markdown 가져오기, 쓰지 않는 이미지 정리.
- 캡처: 메모에 넣는 최종 그림을 클립보드에도 복사합니다(Windows도 같음, 사용자 요청 2026-11-03). 주석 창에서 상하좌우 테두리를 끌어 자릅니다(Windows도 같음, 사용자 요청 2026-11-03). 모자이크와 주석은 원본 좌표로 그린 뒤 자르고, ‘그대로 넣기’는 자르지 않은 원본을 넣습니다. 그림보다 큰 빈 곳을 눌러도 테두리가 잡히지 않도록 그림 안으로 당기기 전의 좌표로 찾습니다. `MacCaptureSession.cs`(화면마다 투명 패널, 전역 후크 없음), `MacScreenCapture.cs`(ScreenCaptureKit, 창·요소 찾기, ⌃⇧⌘C), `MacCaptureAnnotationWindow.cs`(주석).
- 앱 아이콘: `src/DevPostIt.Mac/Resources/AppIcon.icns`, `Info.plist`의 `CFBundleIconFile`. 다시 만들려면 `scripts/macos-icon/make-icon.swift`로 `.iconset`을 만들고 `iconutil -c icns`로 변환합니다. `Info.plist`만 고치면 증분 빌드가 번들의 목록을 갱신하지 않은 적이 있어(2026-11-03) `bin`과 `obj`의 해당 구성을 지우고 빌드했습니다.
- 없는 것: 자동 업데이트(설정에서 숨김), 공증, 설치 패키지.

### 수동 시험이 남은 항목

자동 시험으로 확인하지 못했습니다. 5단계의 끝나는 조건이며, 절차는 [MACOS_MANUAL_TEST.md](MACOS_MANUAL_TEST.md)에 있습니다.

- 캡처: 실제 드래그, 손쉬운 사용 권한을 허용한 뒤의 요소 단위 선택(앱에 직접 묻도록 고쳤으나 권한이 있는 환경에서 실행하지 못했고, 휠로 상위 요소 고르기 포함), 화면 기록과 손쉬운 사용 권한 안내, ⌃⇧⌘C, Esc·오른쪽 클릭·60초 종료, 여러 모니터. 임시 서명이라 빌드마다 화면 기록 권한이 초기화될 수 있습니다.
- 주석 창: 실제 마우스 그리기와 글자 입력(한글 포함), 테두리를 실제로 끌어 자르기와 커서 모양. MacSmoke는 이벤트 객체를 캔버스에 직접 넘겨 확인합니다. Windows는 시험 코드가 `DragCrop`을 직접 부르므로 마우스 처리와 표시가 자동으로 확인되지 않습니다.
- 앱 아이콘의 Dock·Finder 실제 표시. 이전에 실행한 번들이면 시스템의 아이콘 캐시 때문에 늦게 바뀔 수 있습니다.
- 실제 앱 호스트에서의 한글 IME, 마인드맵·할 일·달력의 실제 조작.
- 알림 표시와 클릭, 할 일 알림의 전체 흐름, 백업 복원 후 재시작, 실제 휴지통 이동, 파일 선택 창, 인쇄·PDF 저장 창.
- draw.io 실제 연동. MacSmoke는 그림 표시, 메뉴의 편집 단추, 돌아왔을 때 다시 읽기까지만 확인하고 편집 단추를 누르지 않습니다.

### 웹 시험의 건너뛰기

이미지를 누르는 시험(`preview-tools` 4개)은 Playwright WebKit에서 가로챈 이미지가 표시되지 않아 건너뛰고, 같은 동작을 MacSmoke `CheckPreviewImagesAsync`가 실제 WKWebView로 확인합니다. 건너뛰기를 늘릴 때는 대신 무엇이 확인하는지 실제로 맞춰 보세요. 이 작업에서 `src` 속성만 보는 시험을 잘못 건너뛰었다가 macOS에서 메모 이미지가 표시되지 않는 결함을 놓칠 뻔했습니다. 원인은 DOMPurify가 `devpostit-assets:` 스킴을 지운 것과 자원 검사가 draw.io 그림의 `?v=`를 거부한 것이었습니다. `ALLOWED_URI_REGEXP`는 `data-line` 같은 일반 속성 값에도 적용되므로 좁히면 속성까지 사라집니다.

### CI 실패 원인 (2026-11-03, `9672e35`)

- **macOS의 웹 시험 `size.spec.ts`:** 앱 결함이 아니라 시험 문제입니다. 미리보기 상태에서 숨겨진 Monaco의 옵션과 인라인 스타일은 바로 14px로 바뀌지만, 변경 전에 같은 요소를 읽어 둔 WebKit의 `getComputedStyle`은 8초 뒤에도 13px을 돌려줍니다. 시험이 그 요소를 변경 직전에 읽게 하면 CI와 같은 실패(`Expected 1.0769`, `Received 1`)가 항상 납니다. CI에서 왜 그 조건이 되는지는 확인하지 못했습니다. 이제 원문 모드로 돌아와 보이는 상태로 확인합니다. WebKit에서는 숨겨진 편집기의 계산된 스타일을 시험에 쓰지 마세요.
- **Windows의 DesktopSmoke (`Program.cs`의 `#larger` 다음 대기):** 코드로 찾은 원인이며 Windows에서 재현하지 못했습니다. `EnsureCoreWebView2Async` 뒤 이동 전의 `about:blank`도 `readyState`가 `complete`이고, 이때 `#larger`가 없으면 `click` 오류가 조용히 사라져 30초 뒤 시간 초과가 납니다. 이전의 `readyState` 대기(`8f249db`)가 들어간 실행에서도 실패한 이유입니다. 단추까지 기다리도록 고쳤고 CI에서 확인해야 합니다.
- **`7d53122` 첫 시도의 `ReadyAsync` 시간 초과(초기화 중 삭제 취소 직후):** 재실행은 통과했습니다. **원인을 모르며 위 수정으로 해결되지 않습니다.** 앱의 경합일 가능성도 배제하지 못했으므로 다시 나면 조사합니다.

- **`f0c87e8`의 Windows 작업:** 첫 시도는 `formatter.spec.ts:229`가 개발 서버에서 페이지 준비 30초 초과(재시도에서 나지 않음, 원인 모름), 재시도는 `CrashRecoveryTests`가 강제 종료 직후 잠금을 얻지 못해 실패했습니다. 파일 핸들이 늦게 풀리는 것으로 보고 시험이 재시도하게 고쳤지만 Windows에서 재현하지 못했습니다. 두 시도 모두 DesktopSmoke까지 가지 못했으므로 설정 창 수정과 Windows 클립보드 복사는 CI에서 확인되지 않았습니다.
- **MacSmoke 간헐 실패(원인 모름):** `설치된 글꼴을 설정에 전달` 대기 시간 초과가 11번 중 1번, 메뉴 횟수가 하나 많은 실패가 2번(`⌃클릭` 메뉴 3번, `왼쪽 클릭` 메뉴 4번) 있었습니다. 메뉴 실패는 사용자가 앱을 쓰는 동안 시험이 실제 메뉴 막대 아이콘을 띄워 생긴 클릭일 수 있지만 확인하지 못했습니다.

### 메뉴 막대 오른쪽 클릭 (2026-11-03)

이후 왼쪽 클릭도 메뉴를 열도록 바뀌어 아래의 왼쪽·오른쪽 구분은 더 이상 쓰지 않습니다. 기록으로 남깁니다.

사용자가 실제 오른쪽 클릭에서 메뉴 대신 새 메모가 열린다고 보고했습니다. 앱이 받은 원본 이벤트는 `LeftMouseDown`, `button=0`이었고, 사용자는 Mac에 오른쪽 버튼이 설정되어 있지 않았던 것 같다고 판단했습니다. 그 뒤 사용자가 실제 클릭으로 메뉴가 나오는 것을 확인했습니다. 확인한 빌드는 버튼 위의 `StatusClickView`가 마우스 이벤트를 직접 받는 방식이며, 원인이 설정이었다면 이 방식이 꼭 필요하지는 않았을 수 있습니다. 사용자가 확인한 코드라 그대로 둡니다. MacSmoke의 클릭 시험은 이벤트 객체를 직접 넘기므로 실제 클릭을 대신하지 못합니다.

### MacSmoke가 확인하지 않는 것

- 합성 입력을 보내지 않으므로 실제 마우스·키보드, 메뉴 막대 아이콘의 모양과 클릭, 빨간 X와 노란 버튼의 실제 클릭, 실제 ⌘Q 키는 확인하지 않습니다. 종료 경로는 `NSApplication.Terminate`로 시험합니다.
- 실제 앱 호스트에서의 한글 IME는 아직 시험하지 않았습니다. 0단계 통과는 시험 도구의 WKWebView 결과입니다.
- 할 일 알림과 백업 복원의 전체 흐름은 시험하지 않습니다.
- 복구 경로는 `NoteSession`으로 그대로 옮겼으며 새 자동 시험이 없습니다.
- CI의 `macos-app` 작업은 Release 앱을 빌드해 압축 파일로 올리기만 하고 `MacSmoke`는 실행하지 않습니다. 이 작업은 아직 GitHub에서 실행해 보지 않았고, 같은 명령을 깨끗한 복제본과 새 SDK로 로컬에서만 확인했습니다.
- `MacSmoke`가 한 번 `⌃클릭은 메뉴만 표시 (메뉴 3번)`로 실패했습니다(워크로드 27.0, Debug). 같은 조건의 재실행 3번과 워크로드 26.5의 Debug·Release 각 3번은 통과했습니다. 원인을 모르며, 시험 중 실제 메뉴 막대 아이콘을 누른 것인지도 확인하지 못했습니다.

### 다음에 할 일

1. CI를 다시 확인합니다. `3a39329`(실행 `41620305694`)에서 세 작업이 모두 통과해 Windows의 DesktopSmoke와 macOS의 WebKit 시험을 처음 확인했습니다. 한 번의 통과이므로 위의 간헐 실패가 사라졌다는 뜻은 아닙니다. 그 뒤의 자르기(Windows 포함)와 아이콘은 CI에서 아직 확인하지 않았습니다.
2. 위 ‘수동 시험이 남은 항목’을 사용자가 확인합니다. 시험용 메모를 쓰려면 `DEVPOSTIT_DATA_DIR`를 지정해 실행 파일을 직접 실행합니다.
3. `main` 병합 전에 Windows에서 WPF를 직접 시험합니다.

### 이 Mac의 준비 상태

- 빌드 조합은 Xcode 26.6과 `macos` 워크로드 26.5.10318입니다. 워크로드 버전은 저장소 루트의 `macos-workload.json`에 고정했습니다. 설치: `dotnet workload install macos --from-rollback-file macos-workload.json`. 이미 다른 버전이 있으면 `dotnet workload config --update-mode manifests` 후 `dotnet workload update --from-rollback-file macos-workload.json`을 쓰고, 이전 워크로드로 만든 `bin`과 `obj`를 지웁니다. 남겨 두면 빌드는 되지만 실행할 때 `__Internal` 오류로 중단됩니다.
- 이렇게 고정한 이유(2026-11-03): 워크로드 27.0.10742는 macOS 27.0 SDK(Xcode 27.1)를 요구합니다. Xcode 27.1은 macOS 26.6 이상이 필요하고, GitHub의 `macos-26` 이미지에는 Xcode가 26.6까지만 있습니다. Xcode 26.6에서 27.0 워크로드는 Debug만 빌드되고 Release는 `MT0179`로 실패합니다. 예전 Mac의 Xcode 27.2와 워크로드 26.5 조합은 시험하지 않았습니다.
- 서명: `scripts/macos-signing/create-certificate.sh`가 자체 서명 인증서를 로그인 키체인에 만들고 Git 제외 파일 `.tools/macos-signing.props`로 빌드에 연결합니다. 임시 서명은 권한이 빌드 해시에 묶여 빌드마다 풀리고, 목록에 켜져 있어도 앱은 권한이 없다고 판단합니다(2026-11-03 사용자 보고). 인증서로 서명하면 조건이 `identifier`와 `certificate leaf`가 되어 빌드가 바뀌어도 같습니다. **개인 키를 저장소에 넣지 마세요.** 저장소가 공개이고, 키가 있으면 누구나 같은 앱 ID로 서명해 이 Mac에서 허용한 권한을 물려받을 수 있습니다. 저장소의 `.cer`은 공개 인증서입니다. 다른 Mac에서는 스크립트로 새 인증서를 만듭니다(`.cer`이 바뀝니다). 인증서가 없으면 임시 서명으로 빌드하며 CI가 그렇습니다. 처음 도입한 빌드에서 깨끗한 빌드가 코드 서명 단계에서 한 번 실패했고, 로그가 없어 원인을 모르며 이후 재현되지 않았습니다.
- macOS 앱 빌드·실행: `dotnet build src/DevPostIt.Mac` 후 `open src/DevPostIt.Mac/bin/Debug/net10.0-macos/osx-arm64/DevPostIt.app`
- 기본 실행은 실제 데이터 폴더 `~/Library/Application Support/DevPostIt/Data`를 씁니다. 빌드는 `EDITOR_TARGET=macos` 웹 빌드(`editor-web/dist-macos`)를 번들에 넣습니다.
- `.tools`(Git 제외)에 .NET, Node, Playwright가 있습니다. `DevPostIt.sln`의 WPF 프로젝트는 macOS에서 빌드되지 않으므로 `DevPostIt.Mac.slnf`를 씁니다.

```zsh
export DOTNET_ROOT="$PWD/.tools/dotnet" DOTNET_CLI_HOME="$PWD/.tools/cli" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
export PATH="$PWD/.tools/dotnet:$PWD/.tools/node/node-v24.22.1-darwin-arm64/bin:$PATH"
export PLAYWRIGHT_BROWSERS_PATH="$PWD/.tools/playwright"
dotnet test DevPostIt.Mac.slnf --no-restore
(cd editor-web && EDITOR_TARGET=macos EDITOR_BROWSER=webkit npm test)   # EDITOR_BROWSER가 없으면 Chromium
```

### macOS에서 확인한 결과 (2026-11-03)

- .NET(`DevPostIt.Mac.slnf`): Core 18, EditorHost(`DevPostIt.App.Tests`) 231, Storage 234 모두 통과.
- MacSmoke: Debug와 Release 각 87개 통과.
- 웹 개발 모드: WebKit(macOS 대상) 통과 375, 건너뜀 9. Chromium(Windows 대상) 통과 384.
- 웹 제작 빌드 시험은 이 Mac에서 돌리지 않았고 CI의 Windows 작업에서 확인합니다. macOS 제작 번들은 MacSmoke가 실행합니다.
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
