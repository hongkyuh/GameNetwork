# 배틀아레나 클라이언트 v2 (3주차)

서버 접속 기능이 추가된 버전입니다.

---

## 2주차 버전과 달라진 점

```
2주차 : 혼자만 플레이 · 서버 없음
3주차 : 서버에 접속해서 닉네임 전송 · 환영 메시지 수신 ★
```

화면 우측 상단 표시가 이렇게 바뀝니다.

```
접속 전 : ● 서버 접속 중...
성공    : ● 서버 연결됨 · Player123
실패    : ● 서버 미연결 (서버를 켜주세요)
```

---

## 실행 순서

### 1. 서버 먼저 켜기

```
BattleArenaServer 폴더
  → BattleArenaServer.sln 더블클릭
  → F5 (실행)
```

콘솔에 이렇게 뜨면 준비 완료입니다.

```
[서버] 포트 7777번에서 접속을 기다립니다...
```

### 2. Unity 클라이언트 실행

```
Unity Hub → Add → BattleArenaClient_v2 폴더
  → Assets/Scenes/Main.unity 열기
  → Play
```

### 3. 확인

클라이언트 화면 좌측 하단에 이렇게 뜹니다.

```
Player123님, 배틀아레나에 오신 것을 환영합니다!
```

서버 콘솔에는 이렇게 찍힙니다.

```
[접속] ID:1  127.0.0.1:54321
[수신] ID:1  {"Type":1,"Nickname":"Player123"}
[로그인] Player123 (ID:1)
[현재 접속자] 1명
```

---

## 여러 명이 접속해보기

Unity를 두 개 띄우기는 번거로우니 이렇게 해보세요.

```
1. Unity에서 File → Build Settings → Build
2. 빌드된 exe를 두 번 실행
3. 서버 콘솔에 접속자 2명이 찍히는지 확인
4. 한쪽을 끄면 다른 쪽 화면에 "나갔습니다" 메시지
```

---

## 조작법

| 키 | 동작 |
|---|---|
| 방향키 / WASD | 캐릭터 이동 |
| R | 다시 시작 (서버 재접속 포함) |
| ESC | 종료 |

---

## 코드 구조

```
Assets/Scripts/
├── NetworkClient.cs     ★ 3주차 추가 — 서버 통신 담당
├── GameManager.cs         메인 로직 (네트워크 연동 부분 추가됨)
├── Bootstrap.cs           자동 시작
├── GameConfig.cs          게임 설정값
├── PlayerState.cs         플레이어 데이터
├── ItemState.cs           아이템 데이터
├── SpriteFactory.cs       코드로 도형 생성
└── UIBuilder.cs           코드로 UI 생성
```

---

## NetworkClient.cs 에서 볼 것

패킷을 보내는 부분입니다.

```csharp
// 1. 문자열 → 바이트
byte[] body = Encoding.UTF8.GetBytes(json);

// 2. 길이를 4바이트로
byte[] header = BitConverter.GetBytes(body.Length);

// 3. [길이][데이터] 합치기
```

서버의 PacketHelper.cs 와 똑같은 구조입니다. 양쪽이 같은 규칙을 써야 통신이 됩니다.

받은 메시지를 큐에 넣는 이유는 이렇습니다.

```csharp
private readonly ConcurrentQueue<string> _receiveQueue = ...
```

수신은 백그라운드 쓰레드에서 하는데, Unity는 다른 쓰레드에서 UI를 건드릴 수 없습니다.
그래서 큐에 담아뒀다가 Update() 에서 꺼내 처리합니다.

---

## 앞으로의 계획

| 주차 | 추가되는 것 |
|---|---|
| 4주차 | 다른 플레이어 위치가 보이기 시작 (UDP) |
| 6주차 | 아이템 획득 판정을 서버가 담당 |
| 9주차 | 게임 결과를 DB에 저장 |
| 10주차 | 랭킹 시스템 (Redis) |

---

## 문제가 생기면

서버 미연결이 계속 뜨는 경우
- 서버를 먼저 켰는지 확인
- 서버 콘솔에 "포트 7777번에서 접속을 기다립니다" 가 떴는지 확인
- 방화벽 허용 창이 뜨면 허용 클릭

서버는 켰는데 안 붙는 경우
- 서버가 에러로 종료되지 않았는지 확인
- 포트 7777을 다른 프로그램이 쓰고 있을 수 있음
  명령 프롬프트에서 netstat -ano | findstr 7777

Console에 빨간 에러가 나면 메시지를 복사해서 Discord에 올려주세요.
