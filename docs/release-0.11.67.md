# Array RAW Viewer 0.11.67

## 검색 진단 모드

Tools > Options > Array RAW Viewer > Structure Templates에서 **Search debug dump**를 켜고 **Save**한 뒤 Search objects를 실행하세요. 기본값은 꺼짐입니다.

- ROOT / TEMPLATE: 검색 시작 객체와 등록 타입 목록
- NODE / CHILD / ENQUEUE: 탐색 순서, 깊이, 부모·자식 경로와 대기열
- TYPE / MATCH / FOUND: 실제 타입과 매칭 결과, 대상 발견 후 내부 탐색 중지
- SKIP / LIMIT: 건너뛴 이유와 한도 도달
- BEGIN / END / ERROR: 디버거 호출별 소요 시간과 오류
- RESULT / FINISH: 검색 결과와 종료

**Open debug folder** 버튼으로 `%LOCALAPPDATA%\ArrayImageViewer\debug-dump`를 엽니다. 로그는 검색별 파일로 즉시 기록되어 느린 호출의 마지막 BEGIN을 확인할 수 있습니다.

RAW 픽셀값은 저장하지 않습니다. 표현식과 타입명은 프로젝트 정보를 포함할 수 있으니 공유 전에 확인하세요. 기록 자체로 지연이 늘 수 있으며 파일은 자동 삭제되지 않습니다. 검색별 로그는 20,000줄로 제한됩니다. 캐시 결과를 사용한 검색은 CACHE HIT로 표시됩니다.

## 설치파일

- ArrayImageViewer.vsix: Visual Studio 2017 / 2019 / 2022
- ArrayImageViewer-VS2015.vsix: Visual Studio 2015

이 릴리즈는 탐색 실패 원인을 확인하기 위한 진단 기능입니다. 사용자 환경의 탐색 실패 원인이 해결되었다는 의미는 아닙니다.
