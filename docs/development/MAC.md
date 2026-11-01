# macOS 지원 설계

상태: 설계안이며 아직 구현하지 않았습니다. 이 문서의 단계는 사용자가 지시할 때 하나씩 진행합니다. 지켜야 할 동작은 [AGENTS.md](../../AGENTS.md), 기존 설계 결정은 [DESIGN.md](DESIGN.md)에 있습니다.

이 문서는 소스 검토로 작성했습니다. 이 작업공간에는 Mac과 .NET SDK가 없어 아래의 macOS 동작은 실제로 확인하지 않았고, ‘확인 필요’로 표시한 항목은 1단계 시제품에서 먼저 검증합니다.

## 목표와 범위

- macOS 14(Sonoma) 이상, Apple Silicon(osx-arm64)과 Intel(osx-x64) 유니버설 앱.
- 메모 데이터 형식(Markdown·frontmatter·`.hunmin`)과 백업 zip은 Windows와 같습니다. 한 데이터 폴더를 두 OS가 번갈아 써도 메모가 깨지지 않아야 합니다(동시 사용은 지금처럼 임대로 막습니다).
- 편집기 웹(`editor-web`)은 한 벌을 유지하고 Chromium(WebView2)과 WebKit(WKWebView) 양쪽에서 동작시킵니다.
- 저장·복구·삭제·브리지 보안 규칙은 그대로 유지합니다. Mac 때문에 검사를 약하게 만들지 않습니다.
- Windows 앱의 동작은 바꾸지 않습니다. 공용 코드로 옮기는 리팩터링은 Windows 동작을 그대로 두는 범위에서만 합니다.

범위 밖(이번 설계에서 다루지 않음): App Store 배포와 App Sandbox, iOS, Linux 데스크톱, iCloud 동기화.

## UI 기술 선택

| 후보 | 장점 | 단점 |
| --- | --- | --- |
| **.NET for macOS(`net10.0-macos`, AppKit + WKWebView)** | Core·Storage·EditorHost C# 코드를 그대로 참조. 시스템 WebView라 앱이 작음. 상태 표시줄·휴지통·클립보드를 네이티브 API로 씀 | Mac+Xcode가 있어야 빌드. AppKit 화면 코드를 새로 작성 |
| Avalonia + WebView | Windows와 UI 코드 일부 공유 가능 | 공식 WebView가 없어 서드파티 의존. 이미 WPF가 있어 공유 이득이 적음. 트레이·창 동작은 결국 플랫폼별 |
| Electron/Tauri로 재작성 | 크로스플랫폼 UI 한 벌 | 저장·복구 C# 코드를 다시 작성하거나 별도 프로세스로 묶어야 함. 검증된 직렬화 코드를 버림 |

**결정: `net10.0-macos` AppKit 셸 + WKWebView.** 화면은 이미 대부분 웹이고, 데이터 안전성은 검증된 C# 저장 계층에 있으므로 그 둘을 재사용하고 얇은 네이티브 셸만 플랫폼별로 둡니다. Swift를 쓰지 않아 한 언어로 유지합니다.

## 전체 구조

```
src/DevPostIt.Core          (변경 없음, net10.0)
src/DevPostIt.Storage       (net10.0, 이식성 수정)
src/DevPostIt.EditorHost    (net10.0, 출처 규칙을 플랫폼별로)
src/DevPostIt.Desktop       (신규, net10.0, UI 없음) — 창·세션·설정·검색 메시지 처리, 플랫폼 인터페이스
src/DevPostIt.App           (Windows, WPF·WebView2) — Desktop의 인터페이스 구현
src/DevPostIt.Mac           (신규, net10.0-macos, AppKit·WKWebView) — Desktop의 인터페이스 구현
editor-web                  (공용, 호스트 전송 계층만 분기)
```

### 0단계 핵심: `DevPostIt.Desktop` 추출

지금 `MainWindow.ProcessMessageAsync`, `DesktopNoteManager`, `SettingsWindow.Receive`, `SearchWindow.ReceiveAsync`에 UI와 무관한 판단(메시지 종류별 처리, 저장 요청 확인, 세션 복원·종료 순서, 업데이트 확인 주기)이 WPF 코드와 섞여 있습니다. Mac에서 이것을 다시 쓰면 두 구현이 어긋나므로 먼저 UI 없는 계층으로 옮깁니다.

- `NoteWindowController`: 현재 `ProcessMessageAsync`의 분기와 메시지 큐(`messages` 세마포어, 닫는 중 지연 메시지)를 그대로 옮깁니다. 화면 조작은 `INoteWindowView`로 요청합니다.
- `NoteSessionCoordinator`: `DesktopNoteManager`의 창 목록, 세션 복원, 위치 기억, 종료 순서(저장 → 임대 해제), 업데이트 확인 타이머.
- `SettingsController`, `SearchController`: 설정·검색 페이지 메시지 처리.
- 플랫폼 인터페이스(초안):

```csharp
public interface INoteWindowView        // 창 하나
{
    void PostToPage(string json);        // WebView2: PostWebMessageAsJson, WKWebView: callAsyncJavaScript
    Task SetSourceModeAsync();           // window.hunminEditor.setMode('source')
    void HideLoading();
    void ApplyAppearance(string color, bool topmost);
    Task<bool> ExportPdfAsync(string path);
}

public interface IPlatformShell          // 앱 전체에서 하나
{
    bool ConfirmLink(string url);                     // 링크 확인 창
    void OpenExternal(string url);                    // 웹·메일 주소만 (검사는 공용 코드가 먼저 함)
    void RevealFolder(string path);                   // 탐색기 / Finder
    void MoveToTrash(string path);                    // Windows 휴지통 / macOS 휴지통
    byte[]? ReadClipboardPng();
    string? FindDiagramApp();
    void OpenDiagram(string application, string file);
    string? PickSavePath(string title, string defaultName, string extension);
    string? PickOpenPath(string title, string[] extensions);
    bool AskYesNo(string message, bool defaultYes);   // X의 휴지통 확인은 기본 '아니요'
    void Notify(string message);
    IReadOnlyList<string> InstalledFonts(out IReadOnlyList<string> monospace);
}
```

WPF 앱은 이 인터페이스를 구현하는 쪽으로 바뀌고 동작은 같습니다. 이 단계만으로도 컨트롤러를 Linux·macOS의 `dotnet test`로 시험할 수 있어 Windows에도 이득입니다. 기존 `MainWindow`의 `LinkOpener`, `ClipboardImageReader` 같은 교체 가능한 속성이 그대로 인터페이스 항목이 됩니다.

## 웹 편집기와 WKWebView

### 출처와 리소스 제공

WKWebView는 `https` 같은 표준 스킴에 사용자 처리기를 붙일 수 없습니다. 그래서 Mac에서는 사용자 스킴을 씁니다.

| 용도 | Windows | macOS |
| --- | --- | --- |
| 앱 페이지 | `https://devpostit.local/` | `devpostit://app/` |
| 메모 이미지 | `https://devpostit-assets.local/` | `devpostit-asset://assets/` |

- `EditorOrigin`을 정적 상수에서 `EditorOrigin.WebView2` / `EditorOrigin.WebKit` 두 인스턴스로 바꿉니다. 규칙(경로가 정확히 `/index.html`, 쿼리·조각 없음, 사용자 정보 없음, 이미지 파일명 정규식)은 공유하고 스킴·호스트만 다릅니다. 기존 테스트를 두 인스턴스에 모두 돌립니다.
- `WKURLSchemeHandler`가 `editor-web` 폴더와 `notes/assets`를 제공합니다. `..`·링크·허용 확장자 밖의 요청은 404로 거절하고, `IsNoteAsset` 규칙을 그대로 통과한 이미지만 돌려줍니다. Java WASM 때문에 `.wasm`은 `application/wasm`으로 응답해야 합니다.
- 이미지 URL은 웹이 직접 만들므로(`markdown.ts`의 `https://devpostit-assets.local/...`) 호스트가 시작 시 알려주는 `assetBase`를 쓰도록 바꿉니다. 이 값은 두 후보 중 하나만 허용합니다.
- CSP의 `img-src`도 `assetBase`를 따라야 합니다. 빌드 결과를 하나로 유지하려고 CSP에는 두 출처를 모두 적습니다. 실제 이미지 요청은 각 호스트가 다시 막으므로 허용 범위가 넓어지지 않습니다.

### 외부 리소스 차단

WebView2의 `WebResourceRequested`처럼 모든 요청을 가로채는 API가 WKWebView에는 없습니다. 아래를 함께 써서 같은 효과를 냅니다.

1. **WKContentRuleList**: `devpostit:`·`devpostit-asset:`·`data:`·`blob:` 외 모든 로드를 `block`. 이것이 주 차단막입니다.
2. **CSP**: 기존 정책을 유지(`default-src 'none'`).
3. **WKNavigationDelegate**: 최상위 탐색은 신뢰 페이지만 허용, 하위 프레임 탐색은 모두 취소, 다운로드 거절.
4. **WKUIDelegate**: 새 창 요청은 `nil` 반환, 카메라·마이크 권한은 거절, JS 경고 창 표시 안 함.
5. 개발자 도구(`isInspectable`)는 배포 빌드에서 끕니다.

확인 필요: 규칙 목록이 사용자 스킴 페이지의 하위 리소스에도 빠짐없이 적용되는지, 미리보기의 `<img src="http://...">`가 실제로 요청되지 않는지 Playwright WebKit과 실제 앱에서 모두 확인합니다.

### 브리지 전송

웹은 지금 `window.chrome.webview`만 씁니다(`bridge.ts`, `settings.ts`, `search.ts`). 이를 `editor-web/src/host-transport.ts` 하나로 모읍니다.

- WebView2: 기존 `chrome.webview.postMessage` / `message` 이벤트.
- WebKit: 페이지 → 앱은 `window.webkit.messageHandlers.devpostit.postMessage(json)`, 앱 → 페이지는 `callAsyncJavaScript("window.__devpostitHost.receive(message)", arguments: ["message": json])`. 문자열 이어 붙이기로 스크립트를 만들지 않아 주입 위험이 없습니다.
- 앱 쪽은 `WKScriptMessage.frameInfo`가 주 프레임이고 출처가 `EditorOrigin.WebKit` 신뢰 페이지일 때만 `EditorBridge.ReceiveAsync`로 넘깁니다. ID·리비전·허용 필드·UTF-8 크기 검사는 공용 코드 그대로입니다.
- 브리지를 찾지 못하면 지금처럼 연결하지 않습니다(브라우저 단독 실행 유지).

### WebKit 호환성

- Monaco, DOMPurify, Mermaid, Java WASM 파서는 Safari를 지원하지만 이 앱 구성으로는 확인한 적이 없습니다. Playwright 설정에 `webkit` 프로젝트를 추가해 기존 245개 시험을 WebKit에서도 돌립니다(Linux에서도 실행 가능).
- **한글 IME**: Monaco는 Safari에서 조합 중 커서·실행 취소 문제가 보고된 적이 있습니다. ‘원문·한글 IME·실행 취소 보존’ 규칙 때문에 1단계 시제품의 첫 확인 항목으로 둡니다. 실제 macOS 한글 입력기(2벌식)로 손으로 확인해야 하며 합성 이벤트로 대신하지 않습니다.
- `localStorage`: 글꼴 설정이 페이지 `localStorage`에 있습니다. 사용자 스킴 출처의 저장소가 재실행 뒤에도 남는지 확인 필요입니다. 남지 않으면 설정 원본을 앱 설정 파일로 옮기고 페이지에는 `ui.preferences`로 내려 줍니다(지금 백업 복원 때 쓰는 경로와 같음).
- PDF: `WKWebView.printOperation(with:)`에 A4·여백 0.6in·배경 인쇄를 지정하고 파일로 저장합니다. `createPDF`는 한 장짜리 긴 PDF가 되므로 쓰지 않습니다.

### 단축키 표시

Monaco 단축키는 이미 `KeyMod.CtrlCmd`라 Mac에서 ⌘로 동작합니다. 바꿀 곳은 표시 글자입니다.

- `format-toolbar.ts`, `main.ts`의 `Ctrl+B` 같은 고정 문자열을 `shortcutLabel('Mod+B')`로 바꿔 Mac에서는 `⌘B`로 표시합니다.
- `HelpContent`는 플랫폼별 문장이 필요한 항목(단축키, 저장 위치, 휴지통, 제목 표시줄)에 Mac 판을 둡니다. 합니다체 규칙은 같습니다.
- 메모 쪽 Ctrl+=/-/0, Ctrl+,는 Mac에서 ⌘+=/-/0, ⌘+,입니다. ⌘,는 macOS의 설정 관례와도 맞습니다.

## 저장 계층 이식성

소스 검토에서 찾은 macOS 차이와 대응입니다. 모두 Storage 테스트를 macOS에서 돌려 확인합니다.

1. **경로의 시스템 링크(우선 처리)**: `StoragePathGuard.RejectLinks`는 경로의 모든 상위 폴더를 검사합니다. macOS에서 `/var`, `/tmp`, `/etc`는 `/private/...`로 가는 링크이고 `Path.GetTempPath()`는 `/var/folders/...`입니다. 그래서 임시 폴더를 쓰는 기존 테스트와, 링크가 낀 위치를 데이터 폴더로 고른 경우 저장이 모두 거절될 것으로 예상합니다. 대응: 데이터 루트는 열 때 실제 경로(`realpath`)로 한 번 정규화하고, 링크 검사는 **루트 아래**에만 적용합니다. 루트 아래의 링크 거절 규칙은 그대로입니다. Windows에서도 같은 의미이므로 공용으로 고칩니다.
2. **파일 이름 정규화(NFC/NFD)**: macOS 도구와 Finder로 만든 파일 이름, Mac 앱에서 붙여 넣은 글이 한글을 자모로 분해한 NFD로 올 수 있습니다. 그러면 제목이 깨져 보이고 NFC 검색어로 찾지 못합니다. 대응: 파일 이름에서 얻은 제목과 검색 인덱스에 넣는 글을 NFC로 정규화합니다. **Markdown 원문은 바꾸지 않습니다**(원문 보존 규칙). 경로 비교는 실제 파일 시스템 이름을 그대로 씁니다.
3. **파일 이름 금지 문자**: `Path.GetInvalidFileNameChars()`가 Unix에서는 `/`와 NUL뿐입니다. Mac에서 `:`, `?`, `*`, `|` 등이 들어간 메모 파일을 만들면 그 데이터 폴더·백업을 Windows에서 열 수 없습니다. 대응: 새 파일 이름은 Windows 금지 문자와 예약 이름(`CON` 등)을 모든 플랫폼에서 피합니다. 이미 있는 파일은 이름을 바꾸지 않고 읽기만 합니다(ID·경로 불변 규칙).
4. **대소문자**: 경로 비교가 `OrdinalIgnoreCase`입니다. APFS 기본값은 대소문자 무시라 같지만 대소문자 구분 볼륨도 있습니다. 구분 볼륨에서 `A.md`와 `a.md`가 같이 있으면 지금처럼 충돌로 다루고 덮어쓰지 않습니다. 시험을 추가합니다.
5. **원자적 교체·플러시**: `File.Replace`는 Unix에서 `rename`으로 동작해 원자성은 유지됩니다. 다만 macOS의 `fsync`는 디스크 캐시까지 비우지 않습니다(`F_FULLFSYNC` 필요). 대응: macOS에서는 임시 파일 쓰기 뒤 `fcntl(F_FULLFSYNC)`를 호출하는 작은 플랫폼 분기를 `AtomicFileWriter`와 저널에 둡니다. 실패하면 `fsync`로 대신합니다.
6. **데이터 폴더 임대**: `FileShare.None`은 Unix에서 .NET이 `flock`으로 흉내 냅니다. .NET 프로세스 사이에서는 동작하지만 권고 잠금입니다. Windows와 Mac이 네트워크·동기화 폴더의 같은 데이터를 동시에 여는 경우는 지원하지 않는다고 문서에 적습니다.
7. **파일 감시**: `FileSystemWatcher`는 macOS에서 FSEvents를 씁니다. 이벤트가 묶이거나 늦을 수 있으나 지금도 디바운스 후 전체를 맞추므로 구조는 그대로 둡니다. 실제 지연은 측정합니다.
8. **기본 위치**: .NET 8 이후 macOS의 `LocalApplicationData`는 `~/Library/Application Support`입니다. 기본 데이터는 `~/Library/Application Support/DevPostIt/Data`이고 iCloud로 동기화되지 않습니다. 이전 위치(문서 폴더) 이전은 Windows 전용이므로 Mac에서는 실행하지 않습니다. 사용자가 데이터 폴더로 iCloud Drive의 `문서`를 고르면 ‘클라우드 전용 항목’ 문제가 생길 수 있어 경고합니다.
9. **CP949 읽기**: `CodePagesEncodingProvider`는 macOS에서도 동작합니다. 그대로 둡니다.
10. **백업 zip**: 항목 경로가 `/`이고 `\`·`:`·절대 경로를 거절하므로 Windows ↔ Mac 백업 교환이 됩니다. 교차 복원 시험을 추가합니다.
11. **창 위치**: 저장된 위치는 플랫폼별 키(`windows`, `macos`)로 나눠 둡니다. 좌표계(macOS는 왼쪽 아래 원점)와 화면 구성이 달라 다른 OS의 위치를 쓰면 화면 밖에 창이 뜰 수 있습니다.

## 네이티브 셸 기능 대응

| 기능 | Windows 구현 | macOS 구현 | 비고 |
| --- | --- | --- | --- |
| 트레이 | `NotifyIcon` + `ContextMenuStrip` | `NSStatusItem` + `NSMenu` | 왼쪽 클릭은 Mac 관례대로 메뉴를 엽니다. 첫 항목이 ‘새 메모’. ⌥클릭으로 바로 새 메모 |
| Dock | 해당 없음 | `LSUIElement`(Dock 아이콘 없음) | 트레이 앱 성격 유지. 아래 ‘편집 메뉴’ 주의 |
| 메모 창 | WPF `Window` | `NSWindow` + `WKWebView` | 제목 표시줄은 항상 ‘Post It for Developer’ |
| 창 X(빨간 버튼)·⌘W | 휴지통 확인 | `windowShouldClose`에서 같은 확인(`NSAlert`, 기본 ‘아니요’) | 최신 입력 저장 후 `.hunmin/trash`로 이동 |
| 최소화·제목 더블클릭 → 24px 아이콘 | WPF 상태 가로채기 | `NSWindow` 하위 클래스에서 `miniaturize:`를 가로채 접기 | 제목 더블클릭 동작은 시스템 설정(확대/최소화)을 따르므로 `performZoom`도 가로챌지 확인 필요 |
| 접힌 아이콘 | 24×24 테두리 없는 창 | 24×24 `NSPanel`(테두리·그림자 없음, 모든 Space 표시) | 클릭 복원, 드래그 이동, 창 이동을 따라가지 않음 |
| 항상 위 | `Topmost` | `NSWindow.level = .floating` | |
| 제목 표시줄 색 | DWM 특성 | `titlebarAppearsTransparent` + 배경색 | |
| 휴지통(이미지 정리) | VB `FileSystem.DeleteFile` | `NSFileManager.trashItem` | 앱 메모 휴지통(`.hunmin/trash`)과 별개 |
| 링크·폴더 열기 | `Process.Start` 셸 실행 | `NSWorkspace.open` / `activateFileViewerSelecting` | 링크 검사는 공용 코드 |
| draw.io | `draw.io.exe` 후보 경로 | `/Applications/draw.io.app`, `~/Applications/draw.io.app`, 번들 ID 조회 | `NSWorkspace.open(urls:withApplicationAt:)` |
| 클립보드 이미지 | `PNG` 형식 → 비트맵 → 파일 | `public.png` → `public.tiff`(PNG로 변환) → 파일 URL 1개 | 알파 처리 규칙 동일 |
| 글꼴 목록 | WPF `Fonts` | `NSFontManager.availableFontFamilies`, `isFixedPitch` | 이름 검증 정규식은 공용 |
| 파일 선택 | `SaveFileDialog` 등 | `NSSavePanel` / `NSOpenPanel` | |
| 알림 | 풍선 도움말 | `UNUserNotificationCenter` | 권한 거절 시 상태 표시줄 메뉴에 표시 |
| 전역 단축키 | `RegisterHotKey` Ctrl+Shift+PrintScreen | Carbon `RegisterEventHotKey` ⌃⇧⌘C | Mac에는 PrintScreen이 없음. 접근성 권한 불필요 |

**편집 메뉴 주의**: macOS에서 ⌘C/⌘V/⌘Z/⌘A는 앱 주 메뉴의 ‘편집’ 항목을 통해 WKWebView에 전달됩니다. 주 메뉴가 없으면 붙여 넣기와 실행 취소가 동작하지 않습니다. `LSUIElement` 앱도 `NSApp.mainMenu`에 편집 메뉴를 넣어 두고, 메뉴 막대가 보이지 않는 상태에서도 단축키가 동작하는지 1단계에서 확인합니다. 안 되면 창의 `performKeyEquivalent`에서 직접 전달합니다.

## 화면 캡처

Windows 구현은 UI Automation과 전역 마우스·키보드 후크입니다. macOS에서는 권한 모델이 달라 구조를 바꿉니다.

- **입력**: 전역 후크 대신 **모든 화면을 덮는 투명 오버레이 창**(화면마다 하나, `screenSaver` 수준)이 캡처 세션 동안 마우스와 Esc를 직접 받습니다. 시스템 전역 감시를 설치하지 않으므로 ‘후크는 세션 동안만’ 규칙을 자연스럽게 만족하고, Esc·우클릭·60초 경과로 오버레이를 닫는 규칙은 그대로 둡니다.
- **대상 찾기**: 1단계는 `CGWindowListCopyWindowInfo`의 창 경계와 드래그 영역만 지원합니다. UI 요소 단위 강조(`AXUIElementCopyElementAtPosition`)는 접근성 권한이 필요하므로 2단계에서 선택 기능으로 넣습니다.
- **캡처**: `ScreenCaptureKit`의 `SCScreenshotManager`(macOS 14+). 화면 기록 권한이 필요합니다. 권한이 없으면 시스템 설정의 해당 화면을 여는 안내를 띄우고 세션을 끝냅니다. 권한 요청은 사용자가 캡처를 처음 시작할 때만 합니다.
- **배율**: Retina 배율은 화면마다 `backingScaleFactor`로 계산합니다. 좌표 변환은 `CaptureGeometry`(공용)에 Mac 원점 변환을 추가하고 단위 시험을 붙입니다.
- **주석 창**: 주석 모델(`CaptureAnnotations`)은 공용입니다. 그리기는 AppKit 뷰에서 Core Graphics로 하고, 모자이크는 지금처럼 픽셀 자체를 바꿉니다. 글자 주석은 `NSTextField`라 한글 IME를 시스템이 처리합니다.

## 업데이트와 배포

- **패키지**: `.app` 번들을 담은 `DevPostIt-<버전>-osx-universal.zip`(또는 `.dmg`). `AppUpdater.ParseLatest`의 자산 이름 조건(`-win-x64.exe`)을 RID별로 받도록 바꿉니다.
- **서명·공증**: Gatekeeper 때문에 Developer ID 서명과 Apple 공증이 사실상 필수입니다(Apple Developer Program 필요). 하드닝된 런타임에서 .NET JIT를 쓰려면 `com.apple.security.cs.allow-jit` 권한이 필요합니다. App Sandbox는 쓰지 않습니다(사용자 데이터 폴더 선택, draw.io 실행, 접근성 API 때문).
- **업데이트 설치**: 실행 중인 서명된 번들을 스스로 바꾸는 것은 Windows exe 교체보다 위험합니다(서명 손상, 격리 속성). 1차 Mac 릴리즈는 **새 버전 알림 + 릴리즈 페이지 열기**까지만 하고, 자동 설치는 별도 설계(Sparkle 도입 또는 해시·서명 확인 후 번들 교체)로 미룹니다. 기존 ‘digest 없는 자산 무시, github.com만 허용’ 규칙은 유지합니다.
- **코드 서명이 없을 때**: 개발용 빌드는 서명 없이 만들 수 있지만 다른 Mac에서는 실행이 막힌다는 점을 README에 적습니다. 격리 속성을 지우라고 안내하지 않습니다.

## 빌드와 도구

- Mac 빌드에는 macOS, Xcode, .NET `macos` 워크로드가 필요합니다. SDK는 지금처럼 `global.json`으로 고정하고, 워크로드 버전도 워크로드 세트로 고정합니다(잠금 파일·경고를 오류로 처리하는 정책 유지).
- 솔루션 필터로 나눕니다: `DevPostIt.Windows.slnf`(지금 구성), `DevPostIt.Mac.slnf`(공용 + Mac), 공용 테스트는 양쪽에 포함. Windows에서 Mac 프로젝트를, Mac에서 WPF 프로젝트를 빌드하지 않습니다.
- `DevPostIt.Mac.csproj`도 `BuildEditorWeb`를 수행하되 `npm.cmd` 대신 `npm`을 씁니다. 웹 결과는 번들의 `Contents/Resources/editor-web`에 복사합니다.
- CI 후보(HANDOFF의 ‘CI 빌드’와 연결): macOS 실행기에서 공용 테스트 + Mac 빌드, Linux에서 Playwright Chromium·WebKit, Windows에서 WPF 빌드와 기존 테스트. CI 구성은 이 설계와 별도 작업입니다.

## 시험 계획

- 공용 C#(Core·Storage·EditorHost·Desktop) 시험을 macOS에서도 실행합니다. 위 ‘저장 계층 이식성’ 항목마다 실패하는 시험을 먼저 만듭니다: 링크가 낀 임시 경로, NFD 파일 이름, Windows 금지 문자, 대소문자 구분 볼륨(디스크 이미지로 생성), 교차 백업 복원.
- `EditorOrigin` 시험을 WebView2·WebKit 두 규칙에 모두 실행합니다(사용자 스킴의 대소문자, 포트, 사용자 정보, 경로 순회).
- Playwright `webkit` 프로젝트 추가. 브리지 전송 계층은 가짜 `webkit.messageHandlers`로 시험합니다.
- Mac 하네스(`tests/DevPostIt.MacSmoke`): 별도 `DEVPOSTIT_DATA_DIR`와 별도 WebKit 데이터 저장소로 창 열기·저장·X 확인·접기·종료 순서를 확인합니다. 사용자 데스크톱에 합성 입력을 보내지 않는 규칙은 같습니다.
- 손으로만 확인할 항목(자동 검증과 구분해 기록): 한글 IME 조합, 상태 표시줄 클릭, 화면 기록·접근성 권한 흐름, 다중 모니터·Retina 캡처, draw.io 연동, 공증된 앱의 첫 실행.

## 단계

각 단계는 실패 시험부터 작성하고 검증·커밋한 뒤 다음으로 넘어갑니다.

| 단계 | 내용 | 완료 기준 |
| --- | --- | --- |
| 0. 공용화 | `DevPostIt.Desktop` 추출, WPF가 인터페이스 구현. `EditorOrigin` 인스턴스화. 웹 `host-transport.ts`, 단축키 표시 분리 | Windows 동작 변화 없음. 기존 .NET·웹·WPF 하네스 통과. 컨트롤러 시험이 Linux에서 통과 |
| 1. 이식성·시제품 | 저장 계층 이식성 1~5 수정. Playwright WebKit. Mac 셸 시제품: 메모 창 하나 + 저장 + 편집 메뉴 | macOS에서 공용 시험 통과. 한글 IME·⌘V·자동 저장을 실제 Mac에서 확인 |
| 2. 기본 기능 | 다중 창, 상태 표시줄 메뉴, X 확인·휴지통, 접힌 아이콘, 설정·검색·도움말 창, 이미지 붙여 넣기, PDF, 링크, 글꼴, 백업·복원 | Mac 하네스 통과. AGENTS.md ‘유지할 동작’ 항목별 확인 기록 |
| 3. 캡처·다이어그램 | 오버레이 캡처(창·영역), 권한 안내, 주석 창, draw.io 연동, 전역 단축키 | 권한 거절·Esc·60초 종료 시험. 손 확인 항목 기록 |
| 4. 배포 | 유니버설 빌드, 서명·공증 절차, 릴리즈 자산 이름, 업데이트 알림 | 서명·공증된 zip을 다른 Mac에서 첫 실행 |
| 5. 선택 | 접근성 기반 요소 캡처, 자동 업데이트 설치 | 별도 설계 후 진행 |

## 결정이 필요한 항목

구현 전에 사용자 확인이 필요한 것들입니다. 괄호 안은 이 문서의 기본안입니다.

1. Apple Developer Program 가입과 서명·공증 계정 (가입 전에는 개발용 빌드만 배포)
2. 최소 macOS 버전 (14 Sonoma)
3. 상태 표시줄 왼쪽 클릭 동작 (Mac 관례대로 메뉴, ⌥클릭은 새 메모)
4. 전역 캡처 단축키 (⌃⇧⌘C)
5. Dock 아이콘 표시 여부 (표시 안 함)
6. 첫 Mac 릴리즈의 업데이트 방식 (알림과 릴리즈 페이지 열기만)
