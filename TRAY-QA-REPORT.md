# Pulse 1.1 작업표시줄 전환 검증 — 2026-09-09

## 변경

사용자가 기존 큰 가로 위젯 대신 작업표시줄 안의 아주 작은 표시를 요청했다.
기본 표시를 정식 Windows 알림 영역 숫자 아이콘 3개로 전환했다.
상시 바탕화면 창과 일반 작업표시줄 앱 버튼을 없앴다.
아이콘은 C(CPU 사용률), G(GPU 사용률), R(RAM 사용률)이며 단위는 모두 %다.
상세 창은 360×304 논리 픽셀로 줄이고 필요할 때만 연다.
기존 센서 수집 코드와 GPU 선택을 유지했다. 드라이버 설치나 탐색기 패치는 없다.

## 실제 확인 결과

- 최종 빌드 및 기존 논리 검사 8개 통과.
- 시작 시 Visible=false, ShowInTaskbar=false 확인. 큰 창 및 일반 앱 버튼 없음.
- Shell_NotifyIcon이 아이콘 3개 등록 성공을 반환함.
- Windows 설정의 현재 Pulse 숫자 아이콘 3개만 표시로 변경함. 과거 일반 아이콘 항목과 다른 프로그램 설정은 변경하지 않음.
- 재실행 후 아이콘 표시 위치 유지 확인. 안정적인 아이콘 GUID 사용.
- Shell_NotifyIconGetRect 실제 좌표: C (2070,1528), G (2022,1528), R (1974,1528).
- 각 클릭 영역 48×72 물리 픽셀, 서로 다른 위치이고 현재 작업표시줄 영역 안에 있음.
- 현재 150% 배율에서 각 숫자 이미지 24×24 물리 픽셀. 세 클릭 영역 합계 폭 144 물리 픽셀.
- CPU/GPU/RAM의 유효한 실제 수치가 갱신되는 것을 진단 파일로 확인.
- Ctrl+Alt+P를 실제 입력해 상세 창 열림 확인. 실제 창 Visible=true, 전경 창 핸들이 상세 창 핸들과 일치.
- 상세 창 크기 540×456 물리 픽셀, 위치 (2008,1060). 앱에서 내보낸 실제 그리기 결과를 시각 검사하여 수치·단위·한글 잘림 없음 확인.
- 다른 창으로 포커스를 옮긴 뒤 상세 창 Visible=false 확인. 창의 포커스 이벤트 외에 실제 전경 창을 비교해 자동으로 닫도록 보완.
- 작업을 위해 열었던 Windows 설정 창은 닫음.
- 현재 검사 요약의 6개 조건 모두 true: 기본 창 숨김, 앱 버튼 없음, 아이콘 등록, 서로 다른 좌표, 작업표시줄 내부, 유효 센서 값.

## 짧은 자원 관찰

진단 모드, 1초 센서 갱신, 40.29초 동안 21회 측정.

- 전체 CPU 평균 0.052%
- 작업 집합 메모리 103.05~103.10MiB
- 전용 메모리 74.77→74.79MiB
- 핸들 543→539

아이콘 교체 시 이전 HICON을 해제하고, 숫자가 같으면 아이콘 그림을 재생성하지 않는다.
이 측정은 단기 관찰이며 장시간 누수 보장이 아니다. 최종 일반 실행에서는 진단 파일·이미지를 쓰지 않는다.

## 검증 범위의 한계

- 숫자 아이콘 자체의 마우스 클릭과 우클릭 입력은 이 환경의 UI 도구가 작업표시줄을 독립된 대상 창으로 노출하지 않아 직접 시험하지 못했다. 등록 콜백과 메뉴 경로를 구현했고 상세 창 열림·닫힘은 실제 전역 단축키 입력 및 다른 창 전환으로 확인했다.
- Explorer 재시작 후 아이콘 복구, 배율 변경, 다른 모니터의 작업표시줄, 밝은 테마에서의 숫자 대비는 실환경 시험하지 않았다.
- CPU 온도·팬·저장장치 온도는 이전과 같이 외부 센서 공급자가 없어 현재 미지원이다.
- 아이콘 순서는 Windows가 관리하며 숫자 위 C/G/R 글자로 구분한다. 이 PC의 실제 왼쪽부터 순서는 R, G, C다.

## 로컬 근거

outputs/system-monitor-widget-qa/tray-checks.json
outputs/system-monitor-widget-qa/tray-after-restart.json
outputs/system-monitor-widget-qa/tray-popup-open.json
outputs/system-monitor-widget-qa/tray-popup-dismissed.json
outputs/system-monitor-widget-qa/tray-performance.json
outputs/system-monitor-widget-qa/tray-self-test.json

preview.png는 숫자 아이콘을 나란히 그려 내보낸 미리보기다. 작업표시줄 전체 스크린샷이 아니다.
이전 버전 백업: outputs/PulseWidget-before-tray-20260909-002739.zip

공식 API 근거: https://learn.microsoft.com/en-us/windows/win32/shell/notification-area
