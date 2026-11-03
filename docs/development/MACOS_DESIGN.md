# macOS 버전 설계안

상태: 승인됨, 구현 중 (진행 상황은 [HANDOFF.md](HANDOFF.md)의 ‘macOS 버전 진행 상황’). 2026-11-02, 커밋 `62b71c9`(0.6.3) 기준. 승인되면 구현한 결정만 [DESIGN.md](DESIGN.md)로 옮기고 이 문서는 지웁니다.

## 1. 목표와 범위

- `AGENTS.md`의 ‘유지할 동작’과 README의 기능을 macOS에서 모두 제공합니다. 다른 것은 Windows 전용 개념을 macOS 개념으로 옮기는 부분(10절의 표)뿐입니다.
- Windows 버전의 동작을 바꾸지 않습니다.
- 데이터 형식은 같습니다. 같은 Markdown과 백업 zip을 두 OS에서 그대로 읽습니다.
- 범위 밖: App Store 배포, iOS, Linux. App Sandbox는 쓰지 않습니다(임의의 데이터 폴더, draw.io 실행, 자가 업데이트와 맞지 않음).

## 2. 핵심 결정: .NET + AppKit + WKWebView

| 안 | 재사용 | 판단 |
| --- | --- | --- |
| **`net10.0-macos` + AppKit + WKWebView** | Core, Storage, EditorHost와 그 테스트, editor-web 전부 | **채택.** 저장 코드가 한 벌이고 트레이·창·캡처를 네이티브로 구현 |
| Swift로 네이티브 재작성 | editor-web만 | 저널·원자적 저장·복구·검색(C# 약 3,000줄과 테스트)을 두 벌로 유지해 데이터 손상 위험이 커짐 |
| Avalonia | 위와 같고 창 코드 일부 추가 | 트레이는 되지만 다중 웹뷰의 요청 차단, 24px 창, 캡처는 결국 네이티브 호출. 추상층만 늘어남 |
| Electron / CEF | editor-web, Chromium 동작 | IME 동일성은 좋지만 200MB 안팎, .NET과 프로세스 분리 필요. **IME 시험이 실패할 때의 대안으로만 남김** |

XAML이 없고 UI가 대부분 웹이라, 네이티브로 새로 짤 양은 WPF 프로젝트(약 2,500줄)보다 적습니다.

## 3. 구조

```
DevPostIt.Core / Storage / EditorHost  (net10.0, 공유 — 이식성 수정만)
DevPostIt.Desktop      (신규, net10.0) 메시지 처리, 닫기 상태 기계, 창 조정, 알림·업데이트 주기
  ├─ DevPostIt.App     (net10.0-windows)  WPF, WebView2, NotifyIcon, UI Automation
  └─ DevPostIt.Mac     (신규, net10.0-macos) AppKit, WKWebView, NSStatusItem, ScreenCaptureKit
editor-web             (공유, 빌드 대상별 CSP·자산 주소·단축키 표기)
```

`MainWindow.xaml.cs`와 `DesktopNoteManager.cs`에는 WPF와 무관한 규칙이 섞여 있습니다. `ProcessMessageAsync`의 메시지 처리, 닫기 중 메시지 지연, 복구 선택, 세션 복원, 접기 상태입니다. 이를 복사하면 저장·삭제 규칙이 갈라지므로 `DevPostIt.Desktop`으로 옮기고 다음 포트 뒤로 OS 기능을 숨깁니다.

| 포트 | 책임 | Windows | macOS |
| --- | --- | --- | --- |
| `IWebSurface` | 메시지 송수신, 스크립트, 다시 그리기, PDF | WebView2 | WKWebView |
| `INoteWindow` | 표시·숨김·위치·항상 위·색 | WPF `Window` | `NSWindow` |
| `IDialogs` | 확인, 파일 선택, 오류 | `MessageBox` 등 | `NSAlert`, `NSOpenPanel`, `NSSavePanel` |
| `IShell` | 링크·폴더 열기, 휴지통, 재실행, 클립보드 PNG, 글꼴 | Win32, WPF | `NSWorkspace`, `NSFileManager`, `NSPasteboard`, `NSFontManager` |
| `ICaptureSession` | 요소 선택과 PNG 반환 | UI Automation + 후크 | AX + 오버레이 (7절) |
| `ISystemTray`, `INotifier` | 메뉴, 알림 | `NotifyIcon` | `NSStatusItem`, `UNUserNotificationCenter` |

트레이 메뉴 항목도 데이터(`MenuModel`)로 공유해 두 OS의 메뉴가 어긋나지 않게 합니다.

## 4. 웹뷰 호스트

WebView2의 가상 호스트와 요청 필터에 해당하는 기능을 아래처럼 대응합니다. 확인한 범위는 5절에 적었습니다.

| 현재 (WebView2) | macOS (WKWebView) |
| --- | --- |
| `https://devpostit.local/*` | `devpostit://app/*` — `WKURLSchemeHandler`. `https`는 WebKit이 직접 처리해 등록할 수 없음 |
| `https://devpostit-assets.local/*` (`DenyCors`) | `devpostit-assets://notes/<ID>/<파일>` — 다른 출처이므로 `<img>`는 표시되고 스크립트는 읽지 못함 |
| `WebResourceRequested` 전체 차단 | `WKContentRuleList`로 두 스킴 외 전부 차단 + CSP + `decidePolicyFor` |
| `chrome.webview` | 문서 시작 스크립트가 같은 모양의 객체를 주입. 웹 코드 수정 없음 |
| `WebMessageReceived`의 `Source` | `WKScriptMessage.frameInfo`의 `isMainFrame`과 `securityOrigin`으로 만든 출처 |
| 프로필 폴더, `WEBVIEW2_USER_DATA_FOLDER` | `WKWebsiteDataStore(forIdentifier:)`, 시험은 환경 변수로 받은 별도 UUID |
| `PrintToPdfAsync` | `WKWebView.printOperation` + `NSPrintInfo`(A4, 여백 0.6in). `createPDF`는 쪽을 나누지 않음 |

필수 규칙:

- 메시지는 항상 `JSON.stringify`한 **문자열**로 넘깁니다. 객체로 넘기면 Cocoa 타입 변환으로 숫자·null이 달라져 `EditorBridge`의 허용 필드·크기 검사를 그대로 쓸 수 없습니다. 네이티브 → 웹은 `callAsyncJavaScript`의 인자로 넘기고 스크립트 문자열로 조립하지 않습니다.
- 스킴 처리기는 경로를 메모 ID에서만 만들고 기존 정규식과 `StoragePathGuard`를 다시 적용합니다. `Content-Type`은 고정하고 `nosniff`를 붙입니다. 자산에 CORS 헤더를 주지 않습니다.
- `EditorOrigin`은 호출부가 91곳이고 `https`·호스트가 고정되어 있습니다. `EditorOriginPolicy` 인스턴스(`Windows`, `MacOS`)로 바꾸어 EditorBridge 테스트를 두 정책으로 돌립니다. 출처 검사는 약해지지 않습니다.
- `webViewWebContentProcessDidTerminate`를 처리합니다. 네이티브에 있는 최신 초안으로 다시 불러오고 사용자에게 알립니다. 숨긴 뒤 다시 보일 때의 흰 화면도 이 경로에서 함께 시험합니다.
- 자동 저장 타이머는 네이티브(`Core`) 쪽에 있어 숨긴 창의 JS 타이머 제한에 영향을 받지 않습니다. 단, 접힌 창에 대한 종료 시 초안 수집은 실제로 시험합니다.

`editor-web` 변경은 작습니다.

- `vite.config.ts`의 CSP와 `markdown.ts:30`의 자산 주소를 빌드 대상(`EDITOR_TARGET=macos`)으로 나눕니다.
- `event.ctrlKey`로 직접 검사하는 `main.ts`, `todo.ts`를 `isPrimary(event)`로 바꿉니다. Monaco의 `CtrlCmd`는 이미 맞습니다.
- `format-toolbar.ts`의 표기를 `⌘B` 형식으로, `fonts.ts`·`style.css`의 대체 글꼴에 `-apple-system`, `Apple SD Gothic Neo`, `Menlo`를 추가합니다. `settings.ts`의 ‘Consolas’ 안내도 고칩니다.
- `HelpContent`의 단축키·경로 문구를 OS별로 나눕니다.

## 5. 이미 확인한 것과 못 한 것

이 Mac(macOS 26.6.2, arm64)에서 임시 Swift 프로그램으로 WKWebView를 직접 돌려 확인했습니다. 실제 앱 번들이 아닌 최소 페이지이며 현재 CSP와 같은 지시문을 썼습니다.

**확인함**

- `https`는 처리기 등록 불가, 사용자 지정 스킴은 가능
- 사용자 지정 스킴이 `isSecureContext = true`, `crypto.subtle`·`navigator.clipboard` 객체 존재
- 모듈 스크립트, `fetch`, `wasm-unsafe-eval` CSP에서 WASM 스트리밍, `eval` 차단
- `localStorage`가 재실행 후 유지, 창 사이 `storage` 이벤트 전달 (설정의 전체 창 반영이 그대로 동작)
- CSP 없이 규칙 목록만으로 외부 스킴 요청 차단 (처리기 도달 0회)
- 자산 스킴: `<img>` 표시, `fetch` 실패, 캔버스 오염 → `DenyCors`와 같은 효과
- 메시지 왕복, 주 프레임 여부와 출처 확인
- 단기력: 2026-02-17 → 1/1 (설날과 일치)
- 쪽 나눔 PDF: A4 12쪽, 한글 텍스트 추출 가능

**아직 확인하지 못함 (구현 전 관문 또는 수동 시험)**

- 실제 한글 IME는 시험 도구의 WKWebView에서 통과했습니다(HANDOFF.md). 두벌식에서 조합 이벤트가 발생하지 않아 조합 상태에 기대는 보호가 동작하지 않으며, 실제 앱 호스트에서 다시 확인해야 합니다.
- `navigator.clipboard.writeText`의 실제 복사, 실제 Excel 붙여넣기
- 실제 제작 번들(Mermaid, 정리 도구 WASM, Java 파서) 동작과 성능
- AppKit 호스트: 메뉴 막대 앱은 빌드·실행했지만 Xcode 버전 검사를 끈 상태입니다(HANDOFF.md). WKWebView를 올린 빌드는 아직입니다.

## 6. 공유 코드의 이식성 수정

macOS에서 공유 테스트를 돌려 확인했고, 처리한 항목은 표에 표시했습니다. 모두 Windows 동작을 유지하면서 고칩니다.

| 위치 | 문제 | 수정 |
| --- | --- | --- |
| `StoragePathGuard.RejectLinks` | 루트까지 올라가며 링크를 거부합니다. macOS의 `/var`, `/tmp`는 링크라서 그 아래의 **모든 저장이 실패**합니다 (테스트 133개 실패로 확인) | 완료: 운영체제가 `/private` 아래로 둔 `/var`, `/tmp`, `/etc`만 허용 |
| `NoteFileName.For`, `MarkdownNoteRepository.Resolve`, `NoteBackup` 검사 | `GetInvalidFileNameChars()`가 Unix에서는 `/`와 NUL뿐입니다. `a:b.md`가 만들어져 Windows나 Dropbox에서 깨지고, 백업이 OS 간에 호환되지 않습니다 | 완료: Windows 금지 문자를 고정 목록으로 모든 OS에 적용 |
| `SqliteNoteIndex:234` `path_key` | 대문자화만 하고 NFC로 맞추지 않습니다. APFS는 NFC·NFD를 같은 파일로 보지만 키는 달라 **한글 파일이 중복 등록**될 수 있습니다 | 완료: macOS에서만 키를 NFC로 정규화. NTFS는 두 이름을 다른 파일로 보므로 Windows 키는 유지 |
| `AppUpdater` | 자산 이름(`-win-x64.exe`)과 `.old.exe` 교체를 고정 | 자산 선택과 교체를 포트로 분리 (9절) |
| `App.OnStartup`의 이전 위치 이전 | 문서 폴더 접근이 macOS 권한 창을 띄웁니다 | macOS는 이전 위치가 없으므로 호출하지 않음 |
| 파일을 잠그는 저장소 테스트 (`AtomicFileWriterTests`, `NoteWorkspaceTests`, `WorkspaceFeatureTests`) | 파일을 열어 둬 교체·삭제 실패를 재현합니다. Unix는 열린 파일도 교체되므로 실패가 재현되지 않습니다 | 완료: `HeldFile`이 Windows는 열어 두고 macOS는 `chflags uchg` 사용. macOS 실패는 `UnauthorizedAccessException`일 수 있어 둘 다 허용 |
| `DevPostIt.sln` | WPF 프로젝트 때문에 macOS에서 통째로 빌드 불가 | 완료: `DevPostIt.Mac.slnf` 추가. `.sln`은 Windows 명령 유지 |

기본 위치는 `~/Library/Application Support/DevPostIt/Data`, `.hunmin` 구조는 같습니다. 데이터 폴더 임대, 강제 종료 복구, 외부 변경 감지 시험은 macOS에서 통과했습니다. 단, macOS에서는 Windows와 달리 다른 프로그램이 파일을 열어 두어도 삭제·교체가 성공합니다. 그 프로그램은 이전 내용을 계속 읽으므로 메모 쪽 데이터 손실은 없습니다. `Flush(true)`의 실제 디스크 플러시 수준은 소스·시험으로 확인하지 못했습니다.

## 7. 화면 캡처

macOS 설계에서 가장 많이 달라지는 부분입니다. **전역 후크를 쓰지 않습니다.**

- **입력:** 모니터마다 전체 화면 투명 `NSPanel`을 띄워 오버레이가 직접 마우스·키를 받습니다. `CGEventTap`이 필요 없어 ‘세션 동안만 설치, Esc·우클릭·60초로 해제’가 창을 닫는 것만으로 보장됩니다. 강조선도 이 패널에 그립니다. 전체 화면 앱 위에서는 `.fullScreenAuxiliary`로 표시합니다.
- **요소 찾기(2단계):**
  1. 창 단위: `CGWindowListCopyWindowInfo`로 커서 아래 창을 찾습니다. 손쉬운 사용 권한 없이 동작합니다.
  2. 요소 단위: 권한이 있으면 `AXUIElementCopyElementAtPosition`과 `kAXParentAttribute`로 부모를 올라갑니다. `MaxLevelsUp`과 드래그 임계값은 Windows와 같게 합니다. 일부 앱은 AX 트리가 부실하므로 그때는 창 단위로 내려갑니다.
- **촬영:** ScreenCaptureKit `SCScreenshotManager`(macOS 14 이상), 오버레이는 필터로 제외합니다. 모니터를 걸친 영역은 모니터별로 찍어 합칩니다. `CGWindowListCreateImage`는 폐기되어 쓰지 않습니다.
- **권한:** 화면 기록은 필수, 손쉬운 사용은 선택입니다. 없으면 이유를 말하고 시스템 설정 해당 패널로 안내합니다. 조용히 실패하지 않습니다. 화면 기록은 허용 후 재시작이 필요할 수 있고, 권한은 **코드 서명에 묶여** 있습니다(9절).
- **전역 단축키:** Carbon `RegisterEventHotKey` (권한 불필요). 등록에 실패하면 지금처럼 넘어가고 트레이로 실행합니다.
- **주석 창:** `NSView` + Core Graphics로 다시 구현합니다. 그림 크기가 브리지 제한을 넘고, 글자 입력은 `NSTextField`가 IME에 안전합니다. 모자이크와 기하 계산은 공유 코드(`CaptureAnnotations`, `CaptureGeometry`)를 그대로 써서 픽셀 교체 규칙을 유지합니다.
- **자동 시험:** 오버레이 없이 선택 로직만 시험합니다. 후크가 없어도 합성 입력은 보내지 않습니다.

## 8. 창과 앱 수명

- **제목 표시줄:** 제목은 항상 ‘Post It for Developer’. `titlebarAppearsTransparent`와 창 배경으로 메모 색을 입힙니다.
- **접기:** 노란 버튼과 ⌘M은 `NSWindow` 하위 클래스의 `miniaturize:`를 재정의해 24×24 아이콘으로 접습니다. 제목 표시줄 더블클릭은 `sendEvent:`에서 시스템 설정보다 먼저 가로챕니다. Dock으로 내려가지 않습니다.
- **접힌 아이콘:** 테두리 없는 `NSPanel`, 일반 레벨(Windows처럼 항상 위 아님), 비활성화해도 숨지 않음, `performDrag`로 이동, 임계값 이내면 복원.
- **빨간 X:** `windowShouldClose:`가 `false`를 돌려주고 기존 비동기 흐름으로 들어갑니다. ‘메모를 휴지통으로 옮기시겠습니까?’, 기본값 ‘아니요’ 그대로입니다. ⌘W도 같습니다.
- **⌘Q와 시스템 종료:** 트레이 종료와 같이 메모를 보존합니다. `applicationShouldTerminate`에서 `.terminateLater`를 돌려주고 초안 수집·저장 후 응답합니다. 갑작스러운 종료와 자동 종료를 끕니다.
- **메인 메뉴:** 메뉴가 없으면 WKWebView에서 ⌘C/V/X/A/Z가 동작하지 않으므로 표준 편집 메뉴를 반드시 넣습니다.
- **좌표:** AppKit은 왼쪽 아래 원점입니다. 저장값은 왼쪽 위 기준 포인트로 경계에서 변환해 모니터 구성이 바뀌어도 `IsOnScreen`을 재사용합니다.

## 9. 배포와 업데이트

Apple Developer ID 없이 배포합니다(사용자 결정). 공증과 강화 런타임은 쓰지 않습니다.

- `.app` 번들을 zip으로 묶어 GitHub 릴리즈에 올립니다(`DevPostIt-x.y.z-osx-arm64.zip`). 웹 파일과 .NET 런타임을 포함합니다.
- arm64는 서명이 필수이므로 번들 전체를 서명합니다. `UNUserNotificationCenter`와 TCC가 번들 ID를 쓰므로 `dotnet run` 실행만으로는 알림·권한 시험이 되지 않습니다.
- 첫 실행: 내려받은 앱은 Gatekeeper에 막힙니다. 시스템 설정 > 개인정보 보호 및 보안 > ‘그래도 열기’ 절차를 README에 적습니다.
- 업데이트는 기존 규칙(draft·prerelease 무시, 호스트 고정, digest, 크기, SHA-256, 사용자가 고를 때만)을 유지합니다. Windows와 같이 GitHub 릴리즈가 안전하다는 전제로만 위변조를 막으며 안내 문구에 그대로 밝힙니다. 새 번들을 같은 볼륨에 풀어 `--update-self-test`를 돌린 뒤 이름 교체로 바꿉니다. Sparkle은 쓰지 않습니다.
- 로그인 시 자동 실행은 README의 Windows 절차 대신 `SMAppService.mainApp`을 씁니다.

**화면 기록 권한 유지:** 애드혹 서명은 빌드마다 신원이 바뀌어 업데이트할 때마다 권한이 초기화됩니다. 고정된 자체 서명 인증서로 서명하면 업데이트 후에도 권한이 유지될 것으로 보고 6단계에서 시험합니다. 유지되지 않으면 업데이트 뒤 다시 허용하도록 안내합니다. 인증서의 개인 키는 저장소에 넣지 않습니다.

## 10. 기능 대응표

| 기능 | Windows | macOS |
| --- | --- | --- |
| 메모·저장·복구·검색·백업·마인드맵·할 일·달력·공휴일·포맷터 | 공유 | 그대로 |
| 트레이 | `NotifyIcon` | `NSStatusItem` |
| 트레이 왼쪽 클릭 = 새 메모 | 클릭 | 왼쪽 클릭 동일, 메뉴는 오른쪽/⌃클릭 + ‘새 메모’ 항목 |
| 알림 풍선 | `ShowBalloonTip` | `UNUserNotificationCenter`, 클릭 시 할 일·달력 열기 |
| 전역 캡처 | Ctrl+Shift+PrtSc | ⌃⇧⌘C (PrtSc 없음) |
| 메모 안 캡처 | Ctrl+Shift+C | ⌘⇧C |
| 설정, 검색 | Ctrl+, / Ctrl+Shift+F | ⌘, / ⌘⇧F |
| 글자 크기 | Ctrl+= / - / 0 | ⌘= / - / 0 |
| 코드 정리 | Ctrl+Alt+F | ⌃⌥F (⌥⌘F는 Monaco의 바꾸기) |
| 찾기·바꾸기 | Ctrl+F / Ctrl+H | ⌘F / ⌥⌘F |
| 이미지 붙여넣기 | 클립보드 `PNG` 형식 | `public.png`, `public.tiff`(PNG로 변환), 파일 URL 하나 |
| 쓰지 않는 이미지 정리 | 휴지통 API | `NSFileManager.trashItem` |
| draw.io | `draw.io.exe` 경로 | 번들 ID로 찾고 `/Applications/draw.io.app` 예비, `NSWorkspace`로 열기 |
| 작업 표시줄 메모 이름 | `TaskbarItemInfo` | `NSWindow.miniwindowTitle`과 Dock 메뉴에 이름 표시 |

## 11. 단계

각 단계는 실패 테스트부터 작성하고 검증 후 범위별로 커밋합니다.

| 단계 | 내용 | 끝나는 조건 |
| --- | --- | --- |
| **0. 관문** | Xcode·.NET·Node 준비, GitHub Actions(windows, macos). **제작 번들을 WKWebView에 띄워 실제 한글 IME 수동 시험**: 조합·Enter·실행 취소·이름 변경·마인드맵 바로 입력 | IME 통과. 실패하면 CEF 안 재검토. 이후 단계로 가지 않음 |
| **1. 이식성** | 6절 수정, macOS에서 Core·Storage·EditorHost 테스트, CrashHarness | 양쪽 CI 통과 |
| **2. 공유 분리** | `DevPostIt.Desktop` 추출, WPF를 포트로 전환 | Windows CI의 테스트와 DesktopSmoke 통과, 동작 변화 없음 |
| **3. 메모 MVP** | 웹뷰 호스트(4절), 메모 창, 접기, X, 트레이, 메뉴, 저장, ⌘Q | MacSmoke로 저장·복원·휴지통 확인 |
| **4. 보조 창** | 설정, 검색, 도움말, 정보, 포맷터, 마인드맵, 할 일, 달력, 알림, PDF | Playwright WebKit 프로젝트 통과 |
| **5. 이미지·캡처** | 클립보드, draw.io, 정리, 7절 전체 | 수동 시험표 통과 (실제 마우스·IME·다중 모니터) |
| **6. 배포** | 번들, 서명, 공증, 업데이트, 자동 실행, 문서 | 깨끗한 Mac 계정에서 설치·업데이트 |

시험 구성:

- `playwright.config.ts`에 `webkit` 프로젝트를 추가해 기존 웹 시험을 재사용합니다. Playwright WebKit은 WKWebView와 같지 않으므로 IME와 스킴 동작의 증거로 쓰지 않습니다.
- `tests/DevPostIt.MacSmoke`는 `DesktopSmoke`에 대응합니다. 별도 `DEVPOSTIT_DATA_DIR`와 웹 저장소 UUID를 쓰고 합성 입력을 보내지 않습니다. 쓰는 중인 저장소는 지울 수 없어 다음 실행을 시작할 때 이전 저장소를 `WKWebsiteDataStore.remove(forIdentifier:)`로 지웁니다. 그래서 시험 앱의 저장소가 하나 남습니다.

## 12. 주요 위험

| 위험 | 대응 |
| --- | --- |
| WebKit의 한글 IME 차이 | 0단계 관문. 차이는 `editor.isComposing()`과 마인드맵 입력 처리에서 흡수 |
| WPF 리팩터링의 회귀 (이 Mac에서 실행 불가) | Windows CI + 실제 Windows에서 수동 확인 후 병합. 트레이 물리 클릭 등은 CI로 확인되지 않음 |
| `macos` 워크로드와 Xcode 버전 고정, `global.json`의 `rollForward: disable` | 조합을 문서화하고 CI로 고정. 현재 SDK와 맞는 Xcode 확인이 0단계 첫 작업 |
| WebKit 업데이트로 인한 동작 변화 (시스템 내장) | Chromium 고정이 불가. 최소 macOS 14부터 최신까지 CI와 수동 시험 |
| Mermaid·WASM 성능 차이 | 4단계에서 측정. 언어 서비스 작업자는 추가하지 않음 |
| 화면 기록 권한의 주기적 재확인 (macOS 15 이상) | 실패 시 안내. 사용자에게 예상 동작으로 문서화 |

## 13. 확정된 결정 (2026-11-02)

1. Apple Developer ID를 쓰지 않습니다 (9절).
2. Windows 검증 환경이 있습니다. 2단계의 WPF 변경은 그 환경에서 시험·확인한 뒤 병합합니다.
3. macOS 14 이상, Apple Silicon 먼저. Intel은 나중에 검토합니다.
4. 창이 하나라도 있으면 Dock과 메뉴 막대를 표시하고, 창이 없으면 메뉴 막대 아이콘만 둡니다.
