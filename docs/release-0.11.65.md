# Array RAW Viewer 0.11.65

## 변경 사항

- Center view가 선택 픽셀을 중앙으로 옮기면서 최소 48px/픽셀로 확대합니다. 더 높은 배율은 유지합니다.
- 구조체 타입 비교에서 템플릿 구두점 주변 공백과 중복 공백을 정규화합니다. 서로 다른 타입 인수나 typedef 별칭을 동일시하지 않습니다.
- 구조체 검색 깊이를 4에서 12로, 노드 한도를 64에서 512로 확대했습니다. 제한에 도달해도 발견한 결과를 표시하고 미완료 이유를 안내합니다.
- 검색 시간 제한은 3초이며 디버거 COM 호출 사이에서 확인합니다. 개별 COM 호출을 강제 중단하는 시간 제한은 아닙니다.
- Options > Structure Templates에 Open JSON / Load edited JSON을 추가했습니다. 메모장에서 편집본을 저장하고 불러온 뒤 Save로 적용합니다.
- README에 JSON 편집 가이드와 두 구조체 예시를 추가했습니다.

## 설치파일

- ArrayImageViewer.vsix: Visual Studio 2017 / 2019 / 2022 (amd64 포함)
- ArrayImageViewer-VS2015.vsix: Visual Studio 2015

기존 Center view 변경과 구조체 검색 개선을 포함합니다. 실제 사용자 프레임워크의 디버거 탐색 및 메모장 편집 왕복 동작은 별도 현장 검증이 필요합니다.
