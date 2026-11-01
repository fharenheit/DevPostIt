# Claude에서 이어서 작업하기

저장소 루트의 `CLAUDE.md`가 `AGENTS.md`와 `HANDOFF.md`를 가져오도록 구성했습니다. 공통 규칙은 한 파일에서 관리하고, 현재 상태는 인계 문서에서 관리합니다. `CLAUDE.md`의 `@경로` 가져오기는 [Claude Code 공식 문서](https://code.claude.com/docs/en/memory#import-additional-files)를 따릅니다.

## 시작

Claude Code가 설치된 터미널에서:

```powershell
cd D:\Projects\DevPostIt
claude
```

[Claude Code 데스크톱](https://code.claude.com/docs/en/desktop)의 로컬 코드 작업 기능을 사용한다면 이 저장소 폴더를 선택합니다. 일반 채팅에 파일을 첨부하는 방식과 로컬 저장소를 직접 수정하는 코드 작업을 구분하세요.

첫 요청 예시:

> CLAUDE.md와 인계 문서를 읽고 현재 브랜치·사용자 변경을 확인해 줘. 한글로 작업하고, 내가 지정한 변경을 실패 테스트부터 구현한 뒤 검증·커밋해 줘. 이전에 완료한 작업은 다시 구현하지 마.

세션에서 `/context`로 지침 파일이 로드되었는지 확인할 수 있습니다. 계정 연결·로그인은 사용자가 사용하는 Claude 환경에서 진행합니다. 이 저장소에는 인증 정보나 권한 우회 설정을 추가하지 않습니다.

개인별 `CLAUDE.local.md`, `.claude/settings.local.json`, `.claude/worktrees/`는 Git에서 제외했습니다. 공유할 규칙은 `AGENTS.md`에서 관리합니다.

## 환경

2026-11-01 인계 준비 시 현재 PowerShell PATH에서 `claude` 명령을 찾지 못했습니다. 따라서 Claude 세션의 실제 로딩·수정 실행까지 검증한 상태는 아닙니다. CLI가 필요하면 [공식 설치 안내](https://code.claude.com/docs/en/setup)를 따라 설치한 뒤 새 터미널에서 `claude --version`을 확인하세요.

프로젝트의 도구·빌드·테스트 명령은 `AGENTS.md`에 있습니다. 이 컴퓨터의 `.tools`는 Git 제외 대상이므로 새 체크아웃에 함께 내려오지 않습니다. WPF·트레이·WebView2 검증에는 Windows와 설치된 WebView2 Runtime이 필요합니다.

Codex와 Claude를 동시에 사용할 때는 별도 브랜치/작업 폴더나 겹치지 않는 파일 범위를 사용합니다. 같은 파일의 변경을 서로 되돌리지 않도록 시작 전과 커밋 전에 Git 상태를 확인하세요.
