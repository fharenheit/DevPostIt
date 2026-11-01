# 작업 3 Monaco 편집기 구현 계획

> 실행 스킬: `superpowers:executing-plans`. 모든 구현은 TDD로 진행합니다.

**목표:** 원문을 보존하는 Monaco Markdown 편집기와 안전한 읽기 전용 미리보기, 기본 글꼴 선택을 제공합니다.
**설계:** `docs/superpowers/specs/2026-11-01-monaco-editor-addendum.md`.
**기술:** TypeScript, Vite, Monaco, markdown-it, DOMPurify, Playwright. npm 버전과 모든 의존성을 잠금 파일에 고정합니다.

## 작업 3: 편집기 기술 검증

파일: `editor-web/src/{editor,markdown,main,fonts}.ts`, `style.css`, `index.html`, 구성 파일과 `tests/{roundtrip,ime,fonts,preview}.spec.ts`, `docs/research/EDITOR_SPIKE.md`.

계약: `load(noteId:string,markdown:string,revision:number):void`, `setMode(mode:'source'|'preview'):void`, `getDraft():{markdown:string,revision:number,dirty:boolean}`. 작업 4는 이 계약을 사용하며 `wysiwyg` 모드는 `preview`로 대체됩니다.

- [x] 브라우저 테스트부터 작성합니다. BOM/CRLF/혼합 줄바꿈/확장 구문/빈 본문의 열기와 보기 전환 보존, 실제 편집 및 Undo, 새 메모 로드, 한글 조합 전환, 로컬 글꼴 로딩·선택·합자, Markdown 미리보기와 위험 콘텐츠 차단을 검증합니다.
- [x] `npm test`로 미구현 상태의 실패를 확인합니다.
- [x] Monaco 로컬 Worker와 원문 버퍼, 보기 전환, 안전한 미리보기, 기본 글꼴 선택을 구현합니다. 조합 중 전환은 완료 이후로 지연합니다.
- [x] Playwright 전체 테스트, `npm run build`, .NET 전체 테스트·빌드를 실행합니다. 제작 빌드도 브라우저에서 검증하고 화면을 확인합니다.
- [x] 검토 결과와 IME 수동 검증 한계를 기록하고 `feat: add loss-aware Monaco Markdown editor`로 커밋합니다. 작업 4는 시작하지 않습니다.

## 검토 중점

원문 버퍼가 보기 전환/Undo로 덮어써지는지, 스크립트·외부 리소스가 실행되는지, 글꼴이 네트워크 없이 로딩되는지, 조합 도중 전환이 한글을 중복시키는지, Worker/CSP가 제작 빌드에서도 동작하는지 확인합니다.
