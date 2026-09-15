# Array RAW Viewer 0.11.70

## Array 탭 진단 및 복원 보호

- Options > Structure Templates의 **Debug dump: Search / Array tabs**를 켜고 Save하면 Array 추가/전환/닫기 로그를 기록합니다.
- `%LOCALAPPDATA%\ArrayImageViewer\debug-dump\array-tab-*.log`에 탭 ID, 표현식, 타입과 크기, 단계별 BEGIN/END/소요 시간, 예외 스택, VS 비트 수와 메모리 사용량을 남깁니다.
- 설정 복원 중 정규화/타입/부호 이벤트의 부수 효과를 억제하고, 캐시 탭 복원 중 디버거 정수 표현식 평가를 차단합니다.
- 로그는 기본 꺼짐이며 RAW 픽셀값은 저장하지 않습니다. 표현식은 프로젝트 정보를 포함할 수 있으므로 공유 전에 확인하세요. 파일은 자동 삭제되지 않습니다.

## 검증 및 제한

UInt16 및 구조체 멤버 표현식을 설정한 합성 탭에서 추가/12회 왕복/닫기와 복원 로그 기록을 검증했습니다. 코어/검색/옵션/UI 회귀 테스트도 확인했습니다.

다른 PC에서 보고된 Visual Studio 종료의 근본 원인은 아직 확정되지 않았습니다. 이 로그는 네이티브 크래시 덤프를 대체하지 않으며, 실제 환경에서 재확인이 필요합니다.

## 설치파일

- ArrayImageViewer.vsix: Visual Studio 2017 / 2019 / 2022
- ArrayImageViewer-VS2015.vsix: Visual Studio 2015
