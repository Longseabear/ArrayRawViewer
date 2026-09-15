# Array RAW Viewer 0.11.66

- Options > Array RAW Viewer > Structure Templates에 Search timeout (sec)을 추가했습니다. 기본 10초, 범위 1~120초이며 Save로 저장합니다. 모든 솔루션에 적용됩니다.
- Search objects 실행 중 진행 막대와 상태 문구를 표시하고 Viewer 입력을 비활성화해 중복 검색을 방지합니다.
- 검색 노드와 멤버 처리 사이에 UI 갱신 기회를 제공하며 디버거 호출은 UI 스레드에서 유지합니다.
- 검색 중 컨텍스트 무효화가 감지되면 결과 적용을 취소합니다.

시간 제한은 디버거 호출 사이에서 확인합니다. 개별 COM 호출이 오래 걸리면 진행 표시 갱신과 종료가 늦어질 수 있습니다. 이 변경이 모든 타입 탐색 실패의 해결을 보장하지는 않습니다.

## 설치파일

- ArrayImageViewer.vsix: Visual Studio 2017 / 2019 / 2022
- ArrayImageViewer-VS2015.vsix: Visual Studio 2015

빌드와 코어·옵션·Viewer 회귀 테스트를 확인했습니다. 실제 사용자 디버거 환경의 지연 및 탐색 성공 여부는 별도 검증이 필요합니다.
