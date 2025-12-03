# **Arena Rush – 3D Action Shooter (Prototype)**

**Unity 기반 / 기능 우선 개발 / 아트 미적용 테스트 버전**

---

## **1. 프로젝트 개요**

Arena Rush는 소형 아레나에서 몰려오는 적 웨이브를 생존하며 가능한 한 오래 전투를 지속하는 **3D 액션 슈팅 미니게임**이다. 짧은 세션, 즉각 반응성, 반복 플레이 루프를 목표로 설계되었다.

- **장르:** 3D Action Shooter · Wave Survival
- **카메라:** Top-Down / TPS Short Distance
- **플레이타임:** 3~5분
- **개발 목적:** 기능 프로토타입, 시스템 검증, 반복 플레이 설계 테스트

## 1-1. 개발 환경
- Unity 버전: Unity 6 (6000.x LTS)
- Render Pipeline: URP(Universal Render Pipeline) 고정

---

## **2. 핵심 컨셉**

- 좁은 아레나에서 빠른 전투
- 웨이브 진행 → 강화 선택 → 성장 → 다음 웨이브
- 즉각 입력 반응, 단순 UI, 명확한 피드백
- 최소 아트 기반 기능 검증 중심

---

## **3. 핵심 시스템 구성**

### **3.1 Player System**

- WASD 이동 / 마우스 조준
- 연사형 기본 사격
- Dash(쿨다운)
- HP / MoveSpeed / Damage / FireRate / DashCooldown
- CharacterController 또는 Rigidbody 기반 이동

### **3.2 Weapon System**

- 기본 Gun 1종
- HitScan 기반 Raycast 탄환
- FireRate, Damage, Range 설정
- 총구 플래시·히트 스파크 최소 연출

### **3.3 Enemy System**

- 타입 2종
    - **Chaser:** 플레이어 추적
    - **Shooter:** 사거리 내 정지 후 원거리 공격
- 공통: Health, Collision, Attack Cooldown
- 스폰 포인트 4개, 웨이브별 증가

### **3.4 Wave System**

- WaveConfig(ScriptableObject) 기반
- 웨이브 번호, 적 수, 타입 구성
- 남은 적 카운트 → 다음 웨이브 자동 전환
- 10웨이브 기준 기본 생존 난이도 조정

### **3.5 Perk System**

- 웨이브 종료 시 Perk 선택 UI 팝업
- Perk 예시
    - Damage +20%
    - Fire Rate +15%
    - Move Speed +10%
    - Max HP +30
    - Dash Cooldown -20%
- ScriptableObject 기반 정의 / 즉시 적용

### **3.6 UI / HUD**

- HP Bar
- Wave/남은 적 수
- Dash Cooldown
- Perk Popup(3종 중 1 선택)
- Game Over 화면(최고 웨이브 표시)

---

## **4. 기본 게임 루프**

1. 스테이지 로드
2. Wave 1 시작
3. 적 처리
4. 웨이브 종료 → Perk 선택
5. 다음 웨이브
6. 플레이어 HP 0 → Game Over
7. Restart

---

## **5. 개발 Todo (기능 우선 / 아트 없음)**

### **초기 우선순위(P1)**

- [ ]  씬 구성 + 카메라 + 기본 바닥/벽
- [ ]  Input Actions 설정
- [ ]  Player 이동 / 조준 / 사격
- [ ]  Health / Damage / Death 이벤트
- [ ]  EnemyChaser 기본 AI
- [ ]  WaveManager / 스폰 포인트
- [ ]  Perk 시스템 / Perk 선택 UI
- [ ]  HUD 구현
- [ ]  GameState(Playing / PerkSelect / GameOver)

### **보강(P2)**

- [ ]  Dash 기능
- [ ]  EnemyShooter 구현
- [ ]  ObjectPooler 구축
- [ ]  기본 사운드 / 간단 파티클
- [ ]  Wave 밸런싱

### **후순위(P3)**

- [ ]  Boss(옵션)
- [ ]  다양한 무기(Shotgun, Laser 등)
- [ ]  Perk 확장
- [ ]  Arena Theme 적용
- [ ]  세션 기록 저장

---

## **6. 폴더 구조 (초기안)**

```
Assets/
 └ _Game/
     ├ Scripts/
     │   ├ Core/
     │   ├ Player/
     │   ├ Enemy/
     │   ├ Wave/
     │   ├ Perk/
     │   └ UI/
     ├ Prefabs/
     ├ Scenes/
     ├ Materials/
     ├ Audio/
     ├ UI/
     └ Dev/
```

---

## **7. 스크립트 구성 (Minimum Set)**

### **Core**

- GameState.cs
- Pooler.cs

### **Player**

- PlayerController.cs
- Gun.cs
- Dash.cs
- Health.cs

### **Enemy**

- EnemyChaser.cs
- EnemyShooter.cs
- EnemySpawner.cs

### **Wave**

- WaveManager.cs
- WaveConfig.cs (ScriptableObject)

### **Perk**

- Perk.cs (ScriptableObject)
- PerkUI.cs

### **UI**

- HUDController.cs
- PopupController.cs

### **Dev**

- DevConsole.cs
- DebugDrawUtil.cs

---

## **8. 개발 일정(프로토타입 기준)**

| 단계 | 기간 | 작업 |
| --- | --- | --- |
| Core Player | 1주 | 이동, 사격, 카메라, 조준 |
| Enemy AI | 1.5주 | 추적·공격, 스폰 |
| Wave/Loop | 1주 | WaveManager, 적 구성 |
| Perk System | 1주 | 강화 선택 UI |
| Combat FX | 1주 | 파티클/사운드 |
| Arena Map | 0.5주 | 기본 구조물 |
| Polish/Balancing | 1~1.5주 | 튜닝, 리그레션 |

총 **약 6~7주** 예상.

---

## **9. 릴리즈 목표**

- 10웨이브 이상 생존 가능한 완전한 게임 루프
- Perk 5종 이상의 체감 변화
- 60fps 유지
- 최소 아트 기반의 기능 완성 프로토타입 빌드(Win64)

---

## **10. 라이선스 / 기타**

- 사운드· 폰트· 텍스처는 프로토타입용 Placeholder
- 릴리즈 시 교체 예정

---