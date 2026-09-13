# Array RAW Viewer 0.11.56

## 설치파일

- **ArrayImageViewer.vsix**: Visual Studio 2017 / 2019 / 2022. VS 2022 amd64 설치 대상 포함.
- **ArrayImageViewer-VS2015.vsix**: Visual Studio 2015 전용.

Visual Studio를 종료한 뒤 해당 VSIX를 설치하고 다시 실행하세요.

## 변경사항

- 한 창에서 Frame / Structure / Profiles 설정 탭을 전환합니다. 설정 영역의 높이를 조절해 이미지 공간을 확보할 수 있습니다.
- Go To, Center, Kernel, Watch Next X/Y를 노출하고 Frame map을 기본 표시합니다.
- 큰 Frame map 선택은 View ROI 샘플 한도에 맞춰 비율을 유지하며 축소합니다.
- Watch Next는 좌표 변수식을 유지하면서 현재 커서에서 이어집니다. Watch 좌표 변경으로 발생하던 중복 이동 갱신을 방지합니다.
- Watch 시작 시 이전 화면 읽기와 예약 갱신을 취소하며, 실행 대기와 실제 실행 상태를 구분합니다.
- 감시 적중 후 해당 픽셀을 화면 중앙에 배치하고 확대 배율을 유지합니다. Auto update가 꺼져 있어도 감시 적중 시 주변 View ROI를 한 번 갱신합니다.

## 검증 및 제한

두 설치 대상 프로젝트 빌드, 코어 테스트 및 실제 WPF 컨트롤의 레이아웃·Watch 좌표·중앙 이동 회귀 테스트를 확인했습니다.
자동 UI 테스트는 네이티브 디버거의 COM 중단점 동작을 에뮬레이션하지 않습니다.
표현식 평가, 메모리 읽기 및 기존 감시 해제에 걸리는 시간은 Visual Studio 디버거와 대상 데이터에 따라 달라질 수 있습니다.
