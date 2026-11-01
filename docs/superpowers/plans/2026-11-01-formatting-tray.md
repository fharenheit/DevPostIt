# 코드 정리·이름·트레이 구현 계획

승인: 2026-11-01 사용자의 ‘진행해’. 실행 방식: 현재 세션에서 직접 구현하고 최종 별도 검토.

**목표:** 승인된 추가 기능을 자동 저장·복구를 유지하며 제공한다.
**구조:** 코드 정리는 웹 내부, 제목은 기존 리비전 스트림, 다중 창은 앱 소유 저장 세션을 공유한다.
**기술:** WPF, WebView2, Monaco, Prettier와 Java 플러그인, 기존 Markdown·SQLite 저장소.
**설계:** ../specs/2026-11-01-formatting-tray-design.md

## 공통 제약

- 기본 UI 15px, 편집기 13px. 전체 크기 10~24px. D2 Ligature·JetBrains Mono 설정 영구 유지.
- Markdown 정본·고유 ID·원자적 저장·복구 저널·배타적 데이터 임대 유지.
- 사용자 수정 editor-web/README.md를 보존하고 커밋에서 제외.
- 코드 정리는 현재 최상위 블록만, 실패 시 원문 유지. 제목은 앞뒤 공백 제거, 공백 제목 거부, 최대 200자.

## 검토 초점

1. 정리 중 문서 변경·메모 전환: 오래된 결과 미적용(작업 1).
2. 혼합 울타리·CRLF·닫히지 않은 블록: 코드 이외 원문 유지(작업 1).
3. 제목과 본문 동시 변경·종료 직전 입력: 같은 리비전과 복구 경로(작업 2).
4. 두 메모의 독립 저장 및 한 창 닫기: 다른 창 서비스·임대 유지(작업 3).
5. 접힌 창·마지막 창·앱 전체 종료: 초안 유실 방지와 트레이 해제(작업 3).

## 작업 1: 코드 블록 정리

파일: editor-web/src/code-format.ts(탐색·언어별 정리), editor.ts(Monaco 범위 편집), main.ts(버튼·선택), tests/format.spec.ts, package.json·잠금 파일·index.html·Vite 설정(로컬 파서 배포).
인터페이스: Editor.formatCodeBlock(language?: string): Promise<string>. 메시지를 반환하고 변경은 모델 편집으로 발행한다. code-format은 블록 범위와 정리 텍스트를 반환한다.

- [x] JSON·YAML·Java·JS·TS·HTML·CSS, 오류·언어 선택·Undo·앞뒤 보존·닫힘·경합 테스트를 먼저 작성하고 실패 확인.
- [x] 블록 탐색, 필요한 파서의 로컬 번들, 버전·메모 경합 가드와 한 번 Undo를 구현.
- [x] npm run build, 개발 및 제작 Playwright 테스트 통과 확인.
- [x] feat: format fenced code blocks 커밋.

## 작업 2: 메모 이름 저장

파일: Core/NoteChange.cs·NoteService.cs, EditorHost/EditorBridge.cs·EditorDraftSnapshot.cs, App/MainWindow.xaml.cs, 웹 editor.ts·bridge.ts·main.ts, 대응 .NET·웹 테스트.
인터페이스: NoteChange의 선택적 Title, note.load와 note.changed의 선택적 title. Editor.rename(title), getTitle()을 추가하고 기존 getDraft() 계약은 유지한다. 종료 스냅샷에 title을 함께 담는다.

- [x] 제목·본문 변경 후 저장/복구, 브리지 잘못된 제목, 웹 이름 변경·취소·저장 확인 테스트 실패 확인.
- [x] 같은 리비전으로 제목과 본문을 저장하고 창·웹 제목 표시 및 종료 스냅샷을 연결.
- [x] .NET·웹 전체 테스트와 빌드 확인.
- [x] feat: persist editable note titles 커밋.

## 작업 3: 공유 저장 세션과 트레이

파일: Storage/MarkdownNoteRepository.cs(ListAsync), Storage/NoteWorkspace.cs(앱 소유 저장 세션), DesktopNoteManager.cs(창·아이콘 매핑), TrayService.cs(메뉴), CollapsedNoteWindow.cs(작은 창), App.xaml.cs·MainWindow.xaml.cs(수명), 웹 settings.ts(설정 대화상자)·bridge.ts(접기/설정 메시지). 테스트는 저장소 열거·창 상태와 실제 WPF 시나리오로 구분한다.
인터페이스: ListAsync(CancellationToken): Task<IReadOnlyList<Note>>, NoteWorkspace.OpenAsync/OpenNoteAsync/CreateAsync/Service/Notes, DesktopNoteManager.ShowNote/Collapse/Create/ExitAsync. 메모 창은 공유 서비스를 해제하지 않는다.

- [x] 저장소 열거·새 메모 독립성·창 상태 전환·안전 종료 테스트를 먼저 작성하고 실패 확인.
- [x] 저장소 임대를 앱 수명으로 이동하고 고유 메모 생성, 한 메모 한 창, 작은 아이콘 복원, 트레이 메뉴·설정을 구현.
- [x] 다중 창의 글꼴 설정 동기화·설정 대화상자·접기 메시지 웹 테스트 통과.
- [x] 실제 WPF에서 두 메모, 트레이 생성·목록·설정, 제목 접기·복원, 마지막 창 닫기·전체 종료를 확인.
- [x] .NET·웹 전체 테스트·잠금 복원·제작 빌드 확인, 검증 문서 작성.
- [x] 전체 변경 별도 검토, 중요 지적은 실패 테스트부터 수정하고 회귀 확인.
- [x] feat: add tray and collapsible note windows 커밋.
