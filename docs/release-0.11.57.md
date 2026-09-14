# Array RAW Viewer 0.11.57

## 설치

- **ArrayImageViewer.vsix**: Visual Studio 2017 / 2019 / 2022 (VS 2022 amd64 포함)
- **ArrayImageViewer-VS2015.vsix**: Visual Studio 2015 전용

Visual Studio를 종료하고 설치한 뒤 다시 실행하세요.

## 수정사항

- Buffer Expression의 포커스 복원만으로 자동완성 팝업을 열거나 로컬 변수를 탐색하지 않습니다.
- 타이핑 또는 Ctrl+Space로 자동완성을 요청할 수 있습니다.
- 자동완성 팝업이 마우스를 캡처하지 않도록 변경했습니다. 바깥쪽 Watch 버튼 클릭은 팝업을 닫으면서 버튼에도 전달됩니다.
- 기존 Watch Next 좌표 진행, 감시 적중 후 중앙 이동 및 확대 배율 유지 동작을 유지합니다.

## 새 사용 가이드

[GitHub Pages](https://longseabear.github.io/ArrayRawViewer/)를 전면 개편했습니다.
버전별 설치파일, View/Kernel ROI, Watch X/Y, Bayer와 Q-format, 구조체 JSON 예제 및 FAQ를 제공합니다. README는 변경하지 않았습니다.

## 검증 범위

두 설치본 빌드와 실제 WPF 컨트롤의 포커스·자동완성·소비되지 않는 Watch 마우스 입력·좌표 진행·중앙 이동 회귀 테스트를 확인했습니다.
이 테스트는 Visual Studio 네이티브 디버거의 전체 실행 흐름을 재현하지 않으며, 모든 환경의 간헐적 지연이 해소됐음을 보장하지 않습니다.
