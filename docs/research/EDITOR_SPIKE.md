# 작업 3 편집기 기술 검증

## 사용자 선택

2026-11-01 사용자는 Monaco를 요청했고, 후속 선택에서 **Monaco 소스 편집 + 렌더링 미리보기**를 확정했습니다. 기존 TipTap/ProseMirror·Milkdown 비교와 WYSIWYG 구현은 이 선택으로 대체했습니다. 후보를 실행해 비교한 것처럼 기록하지 않습니다.

| 접근 | 검증 상태 | 결과 |
|---|---|---|
| Monaco 원문 + 읽기 전용 미리보기 | Windows Chromium에서 자동화 실행 | 원문 왕복·보기 전환·한글 조합·글꼴 테스트 통과 |
| TipTap/ProseMirror WYSIWYG | 사용자 선택으로 제외, 미실행 | 비교 결과 없음 |
| Milkdown WYSIWYG | 사용자 선택으로 제외, 미실행 | 비교 결과 없음 |

## 보존·지원 범위

| 입력/동작 | 동작 |
|---|---|
| 빈 문서, CRLF, BOM, 혼합 줄바꿈 | 변경 없는 열기·보기 전환 시 문자열을 정확히 보존 |
| 제목·목록·체크리스트·표·코드 | Monaco에서 원문 편집, 미리보기 렌더링 |
| HTML, `:::warning` 등 확장 구문 | 원문 보존. HTML은 문자로 보이고 확장은 일반 텍스트로 표시 |
| 링크·원격 이미지·스크립트 | 미리보기에서 탐색·요청·실행하지 않음 |
| 실제 본문 편집 | 최초 줄바꿈 형식 사용, 새 단일 행 문서는 LF 기본. 혼합 줄바꿈의 실제 편집 후 정규화는 명시된 한계 |
| Undo | 최초 모델 값과 같아지면 보관한 원문 반환, dirty 해제 |
| 새 메모 로드 | 기존 모델 해제 및 Undo 기록 분리 |

원문 문자열은 그대로 보존하지만 아직 파일 인코딩·실제 파일 저장을 처리하지 않습니다. 파일 바이트 단위 종단 검증은 브리지·저장 서비스 통합 이후 수행해야 합니다.

## 글꼴과 배포

기본 D2 Ligature, 선택 JetBrains Mono·시스템 고정폭입니다. 합자는 기본 활성화하며 별도로 끌 수 있습니다. 두 공식 글꼴과 OFL 원문을 번들링해 외부 CDN 요청 없이 로드합니다. JetBrains Mono의 한글 문자는 D2 Ligature로 대체합니다. 정확한 소스 커밋과 해시는 `editor-web/public/fonts/SOURCES.md`에 있습니다.

Monaco 0.57.0, Vite 8.3.5, TypeScript 7.0.2, Playwright 1.64.1, markdown-it 15.0.3, DOMPurify 3.4.17을 잠금 파일에 고정했습니다. Monaco 전이 의존성의 DOMPurify 구버전 보안 보고를 확인하고 npm override로 3.4.17에 통일했습니다. `npm audit`는 0건입니다.

공식 근거: [Monaco ESM 통합](https://github.com/microsoft/monaco-editor/blob/main/docs/integrate-esm.md), [D2Coding](https://github.com/naver/d2-coding-font), [JetBrains Mono](https://www.jetbrains.com/lp/mono/).

## 실제 검증과 한계

- TDD 시작: 브라우저 테스트 11개가 미구현 `load`에서 실패.
- 입력 테스트의 초기 시간 초과는 보이지 않는 Monaco textarea에 마우스 클릭을 시도한 것이 원인. 실제 편집 표면 클릭과 포커스 검증으로 수정했습니다.
- 실제 편집 테스트가 Windows의 Monaco 기본 CRLF와 기대 LF 차이로 실패한 뒤, 원문 최초 줄바꿈/새 문서 LF 정책을 구현했습니다.
- 늦게 로드한 글꼴이 최신 합자 설정을 되돌리는 재현 테스트 실패를 확인하고 비동기 완료 시 현재 설정을 적용하도록 수정했습니다.
- 최초 개발 서버 및 제작 빌드의 Playwright 테스트 각각 12개 통과. CDP `Input.imeSetComposition`으로 `ㅎ→하→한` 조합 후 커밋·전환을 검증했습니다. 합성 composition 이벤트로 조합 중 전환 지연도 검증했습니다.
- 별도 코드 검토에서 조합 종료 직후 최신 보기 요청을 이전 예약 전환이 덮는 결함 1건을 재현했습니다. 회귀 테스트의 실패를 확인한 후, 즉시 적용하는 전환이 기존 예약을 취소하도록 수정했습니다. 최종 개발 서버·제작 빌드의 테스트 각각 13개가 통과했습니다.
- 실제 Windows 한글 IME 키보드, 긴 조합·후보 선택·WebView2 환경의 수동 테스트는 아직 수행하지 않았습니다. 자동화 결과로 이를 통과했다고 주장하지 않습니다.
- `npm run build` 성공. Monaco 번들 약 2.84MB, gzip 약 751KB, Worker 약 304KB. Vite의 500KB 청크 경고는 남아 있으며 다중 창 성능·지연 로딩은 후속 검증 대상입니다.
- .NET 전체 테스트 34개 통과, WPF 포함 빌드 경고·오류 0.

작업 4는 시작하지 않았습니다. 아직 WPF 창은 기존 최소 시작 창이며, 본 편집기는 브라우저에서 실행합니다.
