# 4주차 실습 자료 — TICKET-007 다중 접속

## 폴더 구성

```
BattleArena.sln          ← 학생용 (이걸 여세요)
BattleArena_Answer.sln          ← 교수용 (배포 시 제외)

AsyncBasics/            예제 A — 동기 vs 비동기 체감
TaskInternals/          예제 B — Task 안을 들여다보기
BattleArenaServer_Sync/ 고장난 서버 (실습 대상) ★
BattleArenaServer_Async/완성 서버 (정답, 배포 X)
TestClient/             접속 테스트 도구
BattleArenaClient_v2/   Unity 클라이언트 (3주차와 동일)
```

---

## 오늘의 흐름

### 1. 장애 재현

```
1. BattleArenaServer 실행 (동기 버전)
2. TestClient 실행
3. "2. 2명 접속" 선택
4. 두 번째가 안 붙는 것 확인
```

동기 서버는 한 명이 나갈 때까지 다음 손님을 못 받습니다.

### 2. 비동기 이해

```
AsyncBasics 실행
  1번 → 동기로 9초
  2번 → 비동기로 3초
  3번 → 쓰레드 번호가 바뀌는 것 확인
  4번 → Thread.Sleep 쓰면 비동기가 무의미해짐

TaskInternals 실행
  1~8번 메뉴를 차례로 실행
  Task가 뭔지, await가 뭘 하는지 확인
```

### 3. 코드 고치기

```
BattleArenaServer_Sync 의 3개 파일을 비동기로 변경

PacketHelper.cs
  Send    → SendAsync
  Receive → ReceiveAsync

ClientSession.cs
  Run()   → RunAsync()
  내부 호출도 await 로

GameServer.cs
  Accept()      → await AcceptAsync()
  session.Run() → _ = session.RunAsync()   ★ 핵심
```

### 4. 확인

```
TestClient 로 10명, 100명 접속 테스트
전원 접속되면 성공
```

---

## 예제 A — AsyncBasics

| 메뉴 | 내용 |
|---|---|
| 1 | 동기 방식 아침 준비 (9초) |
| 2 | 비동기 방식 아침 준비 (3초) |
| 3 | await 전후 쓰레드 번호 비교 |
| 4 | Thread.Sleep 을 쓰면 안 되는 이유 |

## 예제 B — TaskInternals

| 메뉴 | 내용 |
|---|---|
| 1 | Task는 '약속'이다 (세탁소 접수증 비유) |
| 2 | Task 상태 변화 관찰 |
| 3 | await 없이 ContinueWith로 짜보기 |
| 4 | 같은 코드를 await로 (비교) |
| 5 | Task.Run vs async 메서드 차이 |
| 6 | 던져놓기 패턴 (_ =) |
| 7 | await 빼먹으면 생기는 일 |
| 8 | 비동기에서 예외 처리 |

---

## TestClient 사용법

```
서버를 먼저 켠 뒤 실행

1. 1명 접속
2. 2명 접속    ← 동기 서버는 여기서 막힘
3. 10명 접속
4. 100명 접속
5. 직접 입력
```

접속 결과와 소요 시간이 표시됩니다.
동기 서버면 두 번째부터 "응답 없음"이 뜹니다.

---

## .NET 버전

csproj의 TargetFramework 가 net8.0 입니다.
학생 PC 환경에 맞춰 변경하세요.

```xml
<TargetFramework>net8.0</TargetFramework>
```
