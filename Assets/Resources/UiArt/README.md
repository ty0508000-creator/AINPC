# 승인 시안의 UI 아트

SkillIcons.png는 사용자 승인 시안의 스타일을 참고해 이미지 생성 도구로 제작한 투명 3×3 스킬 아이콘 아틀라스다.
자동 추출한 Skill0~Skill8은 저장용 무공 인덱스 순서(검술 3개, 호신 3개, 내공 3개)를 유지한다.
원본 아틀라스는 행별로 검술·호신·내공 한 단계씩 배치되어 있다.

ElderPortrait와 ElderAttack은 Mattz Art Samurai 원본 스프라이트 시트에서 추출한 UI 표시용 이미지다.
플레이어 원본과 License.txt는 Assets/Art/Characters/Samurai에 있다.

전체 시안 이미지와 생성된 배경 맵은 게임 배경으로 사용하지 않는다. 실제 UI는 개별 Image, TMP, Button과 RectTransform으로 구성한다.
# 이기어검

`FlyingSword.png`는 `UltimateSetup`에서 직접 그린 9×31 픽셀 검입니다. 외부 에셋을 추출한 파일이 아니며 런타임에서 같은 스프라이트를 다섯 검에 사용합니다. 궁극기 슬롯의 문양은 기존 `Skill2.png`를 사용합니다.
