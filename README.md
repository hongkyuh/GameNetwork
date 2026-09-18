# 🎮 GameNetwork

## 홍규현 22220965

## 👥 4조 구성원

| 이름 | 이메일 | 깃허브링크 |
| :--- | :--- | :--- |
| **서필창** | `spc7696@dyu.ac.kr` | `https://github.com/dyu-spc/animal-fight.git` |
| **이지호** | `jhleeyalee@naver.com` | `https://github.com/Ulsan-Rock/GameNetwork.git` |
| **정채원** | `projcw9663@gmail.com` | `https://github.com/projcw/GameNetwork.git` |
| **홍규현** | `hongkyuh75@gmail.com` | `https://github.com/hongkyuh/GameNetwork.git` |


# 📡 애니멀 파이터 핵심 패킷 정의서

강의계획서의 프로토콜 분리(TCP/UDP 하이브리드), 서버 권위(Server Authority) 검증, DB/Redis 연동을 고려한 핵심 패킷 10종 규격입니다.

---

## 1. Client ➔ Server 패킷 (C2S)

클라이언트는 직접 판정하지 않으며, 서버에 **요청** 또는 **사용자 입력 데이터**만을 전달합니다.

| 패킷 ID | 프로토콜 | 데이터 필드 (Payload) | 설명 |
| :--- | :---: | :--- | :--- |
| **`C2S_Login`** | **TCP** | • `string Username`<br>• `string Password` | 로그인 요청. 서버가 MySQL 계정 검증 및 Redis 랭킹 데이터 조회를 시작함 |
| **`C2S_MatchRequest`** | **TCP** | • `int MatchType` (1: 방 생성, 2: 빠른 매칭) | 4인 매칭 큐 등록 또는 새로운 대기방 생성을 요청함 |
| **`C2S_PlayerInput`** | **UDP** | • `float DirX`<br>• `float DirY`<br>• `int SequenceNumber` | 키보드 8방향 이동 입력 벡터를 30Hz 주기로 서버에 지속 전송함 |
| **`C2S_FireBullet`** | **UDP** | • `float FireAngle`<br>• `int SequenceNumber` | 기본 공격 발사 요청. 지연 없는 즉각 반응을 위해 UDP로 전송하며 서버가 쿨다운 검증 후 생성함 |
| **`C2S_Heartbeat`** | **TCP** | • `long ClientTimestamp` | 1~2초 주기로 핑을 전송하여 세션 활성 상태를 유지하고 비정상 종료를 감지함 |

---

## 2. Server ➔ Client 패킷 (S2C)

서버는 내부 연산 및 검증을 마친 결과, 상태 변화, 동기화 스냅샷을 클라이언트에 **확정 통보(브로드캐스트)**합니다.

| 패킷 ID | 프로토콜 | 데이터 필드 (Payload) | 설명 |
| :--- | :---: | :--- | :--- |
| **`S2C_LoginResult`** | **TCP** | • `bool IsSuccess`<br>• `int PlayerId`<br>• `List<RankEntry> TopRankers` | 로그인 성공 여부와 함께 로비 화면에 표출할 Redis 실시간 Top 10 랭킹을 전달함 |
| **`S2C_GameStart`** | **TCP** | • `int RoomId`<br>• `List<SpawnInfo> Players` (ID, 초기 좌표, HP) | 4인 매칭 완료 시 인게임 씬 전환을 지시하고 초기 스폰 위치를 동기화함 |
| **`S2C_WorldSnapshot`** | **UDP** | • `List<PlayerState> Players` (ID, X, Y)<br>• `List<BulletState> Bullets` (ID, X, Y) | 30fps 서버 틱 루프에서 확정된 모든 플레이어와 투사체의 실시간 좌표를 일괄 브로드캐스트함 |
| **`S2C_PlayerHit`** | **TCP** | • `int TargetPlayerId`<br>• `int Damage`<br>• `int RemainHp`<br>• `bool IsDead` | 서버 권위 피격 연산 결과로 잔여 체력 및 사망 여부를 모든 참가자에게 알림 |
| **`S2C_SuddenDeath`** | **TCP** | • `float CooldownRate` (0.5)<br>• `float BulletSpeedRate` (1.5) | 60초 경과 시 서든데스 모드 진입을 알리고 인게임 가속 배율 파라미터를 동기화함 |
```<ElicitationsGroup></ElicitationsGroup>
