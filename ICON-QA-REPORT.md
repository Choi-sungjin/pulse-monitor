# 실행파일 아이콘 갱신 — 1.1.1

2026-09-09, Windows 11 x64에서 확인.

- 독자 제작한 민트색 파형과 파란색 점, 어두운 둥근 배경 사용. 외부 이미지나 폰트 자산 없음.
- create-icon.ps1은 Windows 기본 System.Drawing으로 ICO/PNG를 재현한다. PowerShell 7에서는 Windows PowerShell로 이미지 생성 단계를 실행한다.
- ICO에 16/20/24/32/40/48/64/128/256 px, 32비트 알파 이미지 9개 포함 확인.
- C# 컴파일 성공, 파일 버전 1.1.1.0 확인.
- Icon.ExtractAssociatedIcon으로 배포 실행파일에서 아이콘을 직접 추출해 육안 확인.
- 새 배포 EXE를 실행한 진단에서 작업표시줄 C/G/R 아이콘 3개 모두 등록 및 좌표 확인.
- CPU와 RAM 실제 값 및 정상 상태 확인. 기존 자체 검사 8개 통과.
- 변경된 아이콘의 탐색기 캐시 갱신 및 설정 창 제목줄 모습은 직접 UI 조작으로 확인하지 않음.
- 이전 버전은 outputs/PulseWidget-before-icon-날짜.zip에 보존.

증거: outputs/system-monitor-widget-qa/icon-extracted.png, icon-live.json, icon-self-test.json.
아이콘 내장 방식: https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/compiler-options/resources
