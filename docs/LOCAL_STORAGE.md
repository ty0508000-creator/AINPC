# 로컬 용량 관리

## 모델 위치 (2026-09-21)

- 게임 실행용: `C:/AINPC/Assets/StreamingAssets/Models/darkness-Q4_K_M.gguf`
- 학습용 원본: `C:/AINPC-ModelArchive/Meta-Llama-3-8B-Instruct-Q4_K_M.gguf`
- 학습 코드와 노트북: 프로젝트의 `Training/` 유지

원본 모델은 프로젝트 루트의 Models 폴더에서 외부 보관 폴더로 이동했으며, 이동 후 SHA-256 일치를 확인했습니다. 다른 PC는 자체 보관 경로를 사용하면 됩니다. Git으로 모델을 배포하거나 모델을 새로 다운로드한 작업은 아닙니다.

SHA-256:

- darkness: `EDC894577C68EBED9FFAC6666C9BD0DBC1662D56CA546F4AFB3AF4416513EEA1`
- 원본 Llama: `8BA9BAF3A7345F705A11878397500FB25174034F0FD784E83AA4A96AAA47735F`

Main 씬의 모델 경로는 변경하지 않았습니다. Chat 샘플 씬은 여전히 StreamingAssets 기준 원본 파일명을 참조하지만, 정리 전에도 그 위치에 원본 모델이 없었습니다. Chat 샘플을 실행하려면 별도 모델 설정이 필요합니다. 이번 작업에서 샘플 씬을 임의로 변경하지 않았습니다.

## 검증 결과 보존

`VerificationResults/Cleanup-20260921/VerificationResults`에 테스트 결과·이미지·테스트 저장 파일 47개를 복사하고 원본과 해시 비교했습니다. Unity 검증 로그는 같은 보관 폴더의 `Logs`에 복사했습니다. 이 경로는 기존 gitignore 규칙으로 Git에서 제외됩니다.

## 삭제 대기 — 실제 삭제되지 않음

실행 정책이 삭제 명령을 차단했으므로 다음 파일은 그대로 남아 있습니다.

- `C:/AINPC/Temp/WoodenVerificationProject`: 약 6.83GiB. 검증 결과는 위 경로에 보존했습니다. 삭제하면 임포트 캐시와 테스트 프로젝트 복사본은 다시 만들어야 합니다.
- `C:/AINPC/Models/darkness-Q4_K_M.gguf`: 약 4.58GiB. 실행용 StreamingAssets 파일과 SHA-256이 동일합니다. 삭제 후에도 실행용 파일에서 복사하여 복원할 수 있습니다.

위 두 대상만 삭제하면 실제 디스크 공간 약 11.4GiB를 확보할 수 있습니다. `Assets/StreamingAssets/Models`의 실행 모델, `Assets/_Recovery`, 원본 프로젝트의 Library, `.git/lfs`는 이번 정리 대상이 아닙니다.
