# Array RAW Viewer 0.11.62

## 설치파일

- **ArrayImageViewer.vsix**: Visual Studio 2017 / 2019 / 2022 (amd64 포함)
- **ArrayImageViewer-VS2015.vsix**: Visual Studio 2015 전용

## 템플릿 설정 직렬화 오류 수정

`StructureTemplateDocument 형식을 직렬화할 수 없습니다` 오류를 수정했습니다.
내부 설정 모델인 StructureTemplateDocument와 StructureTemplateItem에
DataContract 및 DataMember 선언이 누락되어 JSON 저장/불러오기가 실패하던
문제입니다. 사용자가 등록한 C++ 타입 이름이나 RAW 포인터 문제는 아닙니다.

기존 FormatVersion=1 및 JSON 필드명은 유지합니다. 기존 옵션 멈춤 및
재열기 오류 수정도 포함되어 있습니다.

## 검증 범위

- 기존 DLL에서 직렬화 오류를 재현하고 수정 DLL에서 통과를 확인했습니다.
- 실제 저장 코드의 JSON 변환, 임시 파일 Export/Import 왕복을 검증했습니다.
- 여러 템플릿, 제네릭 타입 이름, 한글, 빈 목록과 모든 필드 보존을 확인했습니다.
- 옵션 포커스 전환, Enter, 닫기/재활성화/재편집 회귀 테스트를 통과했습니다.
- 테스트는 사용자 솔루션 설정 파일을 변경하지 않습니다. 실제 VS의 Save 버튼을
  통한 전체 저장 흐름 검증은 별도이며, 모든 버그가 없음을 보장하지는 않습니다.

Visual Studio를 종료하고 해당 버전용 VSIX를 설치한 뒤 다시 실행하세요.
