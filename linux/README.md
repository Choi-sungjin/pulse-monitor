# Pulse Monitor for Linux

Ubuntu 24.04용 사용자 계정 설치판. 원래 Windows 소스는 그대로 보관한다.
상단 바에 C(CPU), G(첫 번째 NVIDIA GPU), R(RAM) 사용률을 표시한다.
숫자는 전체 PC 기준이고, 상세 창의 프로세스 CPU 100%는 코어 하나 기준이다.
메모리는 전체 용량에서 즉시 사용 가능한 용량을 뺀 값이다.

## 설치와 실행

필수 시스템 구성: Python 3, python3-gi, python3-psutil, GTK 3,
libayatana-appindicator3.so.1, AppIndicator를 표시하는 데스크톱 확장.
Ubuntu 기본 확장 ubuntu-appindicators를 사용할 수 있다.
GPU에는 기존 NVIDIA 드라이버와 nvidia-smi를 사용한다.

```sh
sh linux/install.sh --autostart
"${XDG_DATA_HOME:-$HOME/.local/share}/pulse-monitor/launch.sh"
```

시스템 라이브러리가 이미 있고 Python 연결 파일만 없으면 설치 스크립트가
Ubuntu 패키지 저장소에서 gir1.2-ayatanaappindicator3-0.1을 내려받아 앱 폴더에만 푼다.
관리자 권한이나 시스템 패키지 변경 없이 설치한다.
시스템 라이브러리 자체가 없으면 설치를 중단하므로 먼저 배포판 패키지를 준비해야 한다.

앱 목록의 **Pulse Monitor**로 다시 열 수 있다. 상단 바 숫자 클릭 → 상세 정보 열기.
창 닫기/Esc는 창만 숨기고, **Pulse 종료**는 앱을 완전히 종료한다.
다시 실행하면 기존 창을 열며, 같은 로그인 세션에서 중복 실행하지 않는다.
`launch.sh --background`는 상세 창 없이 시작한다. 설치할 때 `--autostart`를 지정하면 로그인할 때 창 없이 자동 시작한다.
이 옵션을 생략하면 기존 자동 시작 설정을 변경하지 않는다.
해제하려면 `~/.config/autostart/pulse-monitor.desktop`을 삭제한다.

## 지원 범위

- CPU·메모리·네트워크·프로세스: psutil로 로컬 시스템에서 수집, 2초 간격.
- CPU 온도: coretemp/k10temp/zenpower/cpu_thermal 중 사용 가능한 센서의 최댓값.
- GPU: NVIDIA 사용률·메모리·온도·전력. 여러 장이면 상세 창에 모두 표시.
- NVIDIA가 없거나 센서 읽기 실패 시 `--`를 표시한다. AMD/Intel GPU는 미지원.
- 네트워크는 인터페이스 전체 합계로, 가상 인터페이스가 있으면 중복 집계될 수 있다.
- 전체 프로세스를 표시하며 초기 정렬은 CPU 내림차순이다.
- PID·프로세스 이름·CPU·메모리·GPU 연산·GPU 메모리 열 제목을 클릭하면 오름차순/내림차순을 전환한다. 화살표로 방향을 표시하며 수치 갱신 중에도 유지한다.
- 프로세스별 GPU 연산은 NVIDIA `nvidia-smi pmon`의 SM 사용률, GPU 메모리는 fb 값(도구 표기 MB)이다. 전체 GPU 사용률과는 다른 지표이며 미지원/측정값 없음은 `--`로 표시한다.
- GPU를 사용하는 프로세스가 여러 장에 걸쳐 있으면 관측된 GPU 값들을 합산한다. NVIDIA pmon 기본 조회 범위는 최대 4개 장치다. 한 장치에서 수치를 제공하지 않으면 해당 합계도 `--`로 둔다.
- GPU `--` 행은 양방향 정렬 모두 맨 아래에 둔다. 앱을 종료했다 켜면 기본 CPU 정렬로 돌아간다.
- GPU 조회는 최대 2초까지만 기다리며, 마지막 수집 후 8초가 넘으면 수치를 지운다.
- 수치 저장·외부 전송·프로세스 종료·드라이버 설치는 하지 않는다.
- GTK 시스템 테마와 기본 키보드 포커스를 사용한다. 별도 애니메이션은 없다.
- Windows의 센서 선택 설정, 전역 단축키, 다중 색상 트레이 아이콘은 이식하지 않았다.

## 검증

```sh
cd linux
python3 -m unittest discover -p 'test_*.py' -v
python3 pulse.py --diagnostics
```

실제 상단 바/창 표시와 종료는 로그인한 데스크톱에서 따로 확인해야 한다.

## 제거

메뉴에서 Pulse 종료 후 다음 두 경로를 삭제한다.

- `${XDG_CONFIG_HOME:-$HOME/.config}/autostart/pulse-monitor.desktop`
- `${XDG_DATA_HOME:-$HOME/.local/share}/pulse-monitor`
- `${XDG_DATA_HOME:-$HOME/.local/share}/applications/pulse-monitor.desktop`
