# 구성요소와 라이선스

이 배포본은 외부 센서 라이브러리 DLL, NVML DLL, 드라이버 또는 외부 프로그램을 재배포하지 않는다.

- 앱 소스: 동봉 LICENSE의 MIT 조건.
- Microsoft .NET Framework / Windows API: Windows에 설치된 시스템 구성요소를 참조한다. 시스템 바이너리는 배포 폴더에 복사하지 않는다.
- NVIDIA NVML: 해당 PC의 기존 NVIDIA 드라이버에서 절대 경로로 로드하여 읽기 전용 호출만 수행한다. NVIDIA 바이너리나 SDK 코드를 포함하지 않는다. 이용 가능한 항목과 반환값은 설치된 드라이버·장치에 따라 달라진다.
- Libre Hardware Monitor: 선택적 외부 WMI 공급자. 공식 프로젝트 라이선스는 MPL 2.0이며 일부 구성요소는 별도 조건이다. 이 앱에 코드나 바이너리를 포함하지 않고 로컬 WMI 데이터를 읽는다.
- Open Hardware Monitor: 선택적 외부 WMI 공급자. 공식 프로젝트 MPL 2.0. 이 앱에 코드나 바이너리를 포함하지 않는다.
- 폰트: Windows 설치 글꼴 Segoe UI / Consolas 참조. 폰트 파일을 재배포하지 않는다.

## 확인한 공식 자료

- Windows Forms DPI: https://learn.microsoft.com/en-us/dotnet/desktop/winforms/high-dpi-support-in-windows-forms
- NVML API: https://docs.nvidia.com/deploy/nvml-api/nvml-api-reference.html
- NVML 장치 읽기 함수: https://docs.nvidia.com/deploy/nvml-api/group__nvmlDeviceQueries.html
- DXGI 장치 / LUID / 전용 메모리: https://learn.microsoft.com/en-us/windows/win32/api/dxgi/ns-dxgi-dxgi_adapter_desc1
- 최상위 창: https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowpos
- Libre Hardware Monitor 공식 저장소 / 권한 / 라이선스: https://github.com/LibreHardwareMonitor/LibreHardwareMonitor
- Open Hardware Monitor 공식 저장소: https://github.com/openhardwaremonitor/openhardwaremonitor

확인일: 2026-09-08. 관련 센서 라이브러리를 향후 포함한다면 당시 버전의 LICENSE 및 THIRD-PARTY-LICENSES를 다시 확인해야 한다.
