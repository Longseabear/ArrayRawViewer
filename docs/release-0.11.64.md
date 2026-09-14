# Array RAW Viewer 0.11.64

## 설치파일

- **ArrayImageViewer.vsix**: Visual Studio 2017 / 2019 / 2022 (amd64 포함)
- **ArrayImageViewer-VS2015.vsix**: Visual Studio 2015 전용

## 선택과 디버거 실행을 명확하게 구분

- Go To → **Select X/Y**: 입력 좌표의 픽셀 선택, 디버거 실행 없음.
- Center → **Center view** 타깃 아이콘: 선택한 픽셀을 화면 중앙으로 이동.
- Watch Go To X/Y → **▶ Run to write X/Y**.
- Watch Next X / Y → **▶ Run to next X / Y**.
- Run 버튼을 ‘디버거 실행’ 그룹으로 표시하고 각 동작의 툴팁을 추가했습니다.
- 기존 좌표 선택, 메모리 읽기, 하드웨어 데이터 중단점 동작은 유지합니다.

## 컴팩트 툴바

- Center, 상하좌우 이동, Zoom/Export 메뉴, Frame map 버튼을 아이콘으로 표시합니다.
- Frame map에는 생성형 이미지 도구로 만든 투명 Bayer 아이콘을 포함했습니다.
  이미지는 DLL에 내장되어 별도 파일 설치나 네트워크 요청이 필요 없습니다.
- Capture, Select, Run, Stats 등 의미 구분이 중요한 버튼은 텍스트를 유지합니다.
- 아이콘 툴팁과 접근성 이름을 제공하며 기존 메뉴 항목과 단축키는 유지합니다.

## 검증

600 / 900 / 1200px 레이아웃, 아이콘 리소스/투명 배경, 메뉴 항목,
Watch 좌표·표현식·타이머·센터링 및 자동완성 포커스 회귀 테스트를 통과했습니다.
구조체 템플릿 JSON 및 옵션 수명 관리 수정도 포함합니다.
실제 VS 전체 워크플로 검증을 대체하지는 않습니다.

Visual Studio를 종료하고 설치한 뒤 다시 실행하세요.
