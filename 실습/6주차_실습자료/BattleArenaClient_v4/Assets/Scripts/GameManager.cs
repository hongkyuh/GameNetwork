using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BattleArena
{
    /// <summary>
    /// 배틀아레나 게임 매니저
    ///
    /// ═══════════════════════════════════════════════════
    /// [2주차 — 현재 상태]
    ///
    /// 지금 이 코드는 "서버 없이" 혼자 돌아갑니다.
    ///   - 캐릭터를 움직여도 나만 보입니다
    ///   - 아이템도 내 화면에만 있습니다
    ///   - 다른 사람과 같이 놀 수 없습니다
    ///
    /// 앞으로 이렇게 바뀝니다:
    ///   3주차  서버에 접속 (TCP)
    ///   4주차  내 위치를 서버에 보내고, 남의 위치를 받기 (UDP)
    ///   6주차  서버 권위 · 아이템 판정 · 예측 보정 (지금 이 버전)
    ///   7주차  게임 결과를 DB에 저장
    ///   8주차  랭킹 (Redis)
    /// ═══════════════════════════════════════════════════
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        // ── 게임 상태 ──
        private PlayerState _localPlayer;
        // 6주차 : 아이템 목록도 점수도 서버가 갖습니다.
        //         클라이언트는 받아서 보여주기만 합니다.
        private string _scoreBoard = "";

        private float _remainTime;
        private bool  _isPlaying;

        // ── 이동 쿨다운 ──
        private float _moveTimer;

        // ── 아이템 생성 타이머 ──

        // ── 렌더링 오브젝트 ──
        private GameObject _playerObject;
        private Dictionary<int, GameObject> _itemObjects = new Dictionary<int, GameObject>();
        private Transform _boardRoot;

        // ── UI ──
        private Text _scoreText;
        private Text _timeText;
        private Text _statusText;
        private Text _predictText;      // 예측 상태 표시
        private Text _resultText;
        private GameObject _resultPanel;

        // ── 네트워크 (3주차 추가) ──
        private NetworkClient _network;
        private string _nickname = "Player";
        private Text _chatLogText;
        private System.Collections.Generic.List<string> _chatLines
            = new System.Collections.Generic.List<string>();

        // ── UDP 위치 동기화 (5주차 추가) ──
        private UdpNetworkClient _udp;
        private int _myPlayerId = 0;

        // ═══════════════════════════════════════════════
        // 오늘 후반부 : 클라이언트 예측
        //
        //  판정을 서버로 옮기면 이렇게 됩니다.
        //
        //      키를 누름 → 서버로 보냄 → 서버가 계산 → STATE 로 받음 → 화면
        //                  └────────── 왕복 시간 ──────────┘
        //
        //  로컬에서는 거의 안 느껴지지만 실제 서비스에서는
        //  50~150ms 입니다. 키를 누르고 0.1초 뒤에 캐릭터가 움직이면
        //  "조작이 무겁다" 는 말을 듣습니다.
        //
        //  그래서 이렇게 바꿉니다.
        //
        //      키를 누름 → 화면을 먼저 움직임 (예측)
        //               → 서버로 보냄
        //               → STATE 가 오면 맞춰봄 (보정)
        //
        //  예측은 "서버도 같은 결과를 낼 것이다" 라는 가정입니다.
        //  그래서 서버와 같은 규칙으로 계산해야 합니다.
        //  규칙이 다르면 예측이 아니라 추측이고, 화면이 자꾸 튑니다.
        // ═══════════════════════════════════════════════

        /// <summary>아직 서버 확인을 못 받은 내 입력 하나</summary>
        private struct PendingInput
        {
            public int Seq;
            public int Dx;
            public int Dy;
        }

        /// <summary>
        /// 보냈지만 아직 서버가 처리했다는 답이 안 온 입력들
        ///
        /// STATE 의 AckSeq 보다 번호가 큰 것들만 남습니다.
        /// 보정할 때 이 목록을 서버 좌표에 다시 적용합니다.
        /// </summary>
        private readonly List<PendingInput> _pendingInputs = new List<PendingInput>();

        /// <summary>예측 켜기/끄기 (P 키) — 끄면 서버 답을 기다리는 동작이 됩니다</summary>
        private bool _predictOn = true;

        /// <summary>예측이 틀려서 위치를 고친 횟수 (화면 표시용)</summary>
        private int _corrections;

        /// <summary>서버가 마지막으로 반영했다고 알려준 내 입력 번호</summary>
        private int _lastAck;

        /// <summary>다른 플레이어의 캐릭터 오브젝트</summary>
        private Dictionary<int, GameObject> _remoteObjects = new Dictionary<int, GameObject>();

        /// <summary>다른 플레이어가 가야 할 목표 위치 (보간용)</summary>
        private Dictionary<int, Vector3> _remoteTargets = new Dictionary<int, Vector3>();

        /// <summary>다른 플레이어 닉네임 라벨</summary>
        private Dictionary<int, GameObject> _remoteLabels = new Dictionary<int, GameObject>();

        /// <summary>위치 전송 타이머 (30fps)</summary>

        // ═══════════════════════════════════════
        // 시작
        // ═══════════════════════════════════════
        private void Start()
        {
            SetupCamera();
            BuildBoard();
            BuildUI();
            StartGame();

            // ── 3주차 추가 : 서버 접속 ──
            ConnectToServer();
        }

        /// <summary>서버에 접속</summary>
        private async void ConnectToServer()
        {
            // ★ 재접속이면 이전 연결을 먼저 정리합니다
            //   안 하면 서버에 옛 TCP 세션이 유령으로 남아서
            //   가만히 서 있는 내 캐릭터가 하나 더 보입니다.
            _network?.Disconnect();
            _udp?.Stop();
            _udp        = null;
            _myPlayerId = 0;

            // 닉네임 자동 생성 (나중에 입력 화면으로 바꿔도 됨)
            _nickname = "Player" + UnityEngine.Random.Range(100, 999);

            _network = new NetworkClient();
            bool ok = await _network.ConnectAsync(_nickname);

            if (ok)
            {
                SetStatus("● 서버 연결됨 · " + _nickname, new Color(0.18f, 0.75f, 0.44f));
            }
            else
            {
                SetStatus("● 서버 미연결 (서버를 켜주세요)", new Color(0.98f, 0.45f, 0.45f));
                AddChat("서버에 접속하지 못했습니다.");
                AddChat("BattleArenaServer 를 실행한 뒤 R 키를 눌러주세요.");
            }
        }

        /// <summary>
        /// 예측 상태를 왼쪽 아래에 보여줍니다
        ///
        ///   확인 안 됨   서버 답을 기다리는 내 입력 수.
        ///                왕복 시간이 길수록 늘어납니다.
        ///   보정         예측이 틀려서 좌표를 고친 횟수.
        ///                정상이면 0 근처에 머물러야 합니다.
        /// </summary>
        private void UpdatePredictText()
        {
            if (_predictText == null) return;

            if (_predictOn)
            {
                _predictText.text =
                    $"예측 ON    확인 안 됨 {_pendingInputs.Count}    " +
                    $"보정 {_corrections}회    서버 확인 번호 {_lastAck}";
                _predictText.color = new Color(0.55f, 0.85f, 0.98f);
            }
            else
            {
                _predictText.text = "예측 OFF   (서버 답을 기다립니다 — 조작이 무거워집니다)";
                _predictText.color = new Color(0.98f, 0.62f, 0.14f);
            }
        }

        /// <summary>상단 상태 표시 변경</summary>
        private void SetStatus(string text, Color color)
        {
            if (_statusText == null) return;
            _statusText.text  = text;
            _statusText.color = color;
        }

        /// <summary>하단 로그에 한 줄 추가</summary>
        private void AddChat(string line)
        {
            _chatLines.Add(line);
            if (_chatLines.Count > 5) _chatLines.RemoveAt(0);

            if (_chatLogText != null)
                _chatLogText.text = string.Join("\n", _chatLines);
        }

        // ─────────────────────────────────────────
        // 카메라 설정
        // ─────────────────────────────────────────
        private void SetupCamera()
        {
            Camera cam = Camera.main;

            if (cam == null)
            {
                GameObject camObj = new GameObject("Main Camera");
                camObj.tag = "MainCamera";
                cam = camObj.AddComponent<Camera>();
            }

            cam.orthographic     = true;
            cam.orthographicSize = GameConfig.MAP_HEIGHT * 0.5f + 1.5f;
            cam.transform.position = new Vector3(0, 0, -10);
            cam.backgroundColor  = GameConfig.ColorBackground;
            cam.clearFlags       = CameraClearFlags.SolidColor;
        }

        // ─────────────────────────────────────────
        // 맵(격자) 생성
        // ─────────────────────────────────────────
        private void BuildBoard()
        {
            GameObject root = new GameObject("Board");
            _boardRoot = root.transform;

            // 배경 판
            GameObject bg = SpriteFactory.CreateSpriteObject(
                "Background", SpriteFactory.Square,
                new Color(0.07f, 0.09f, 0.18f),
                Vector3.zero,
                1f, -10, _boardRoot);

            bg.transform.localScale = new Vector3(
                GameConfig.MAP_WIDTH + 0.6f,
                GameConfig.MAP_HEIGHT + 0.6f, 1f);

            // 격자 점
            for (int y = 0; y < GameConfig.MAP_HEIGHT; y++)
            {
                for (int x = 0; x < GameConfig.MAP_WIDTH; x++)
                {
                    SpriteFactory.CreateSpriteObject(
                        $"Grid_{x}_{y}", SpriteFactory.Circle,
                        GameConfig.ColorGrid,
                        PlayerState.GridToWorld(x, y),
                        0.12f, -5, _boardRoot);
                }
            }
        }

        // ─────────────────────────────────────────
        // UI 생성
        // ─────────────────────────────────────────
        private void BuildUI()
        {
            Canvas canvas = UIBuilder.CreateCanvas("GameCanvas");
            Transform root = canvas.transform;

            // 상단 바
            UIBuilder.CreatePanel(root, "TopBar",
                new Color(0.11f, 0.14f, 0.28f, 0.95f),
                new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1),
                Vector2.zero, new Vector2(0, 70));

            // 점수
            _scoreText = UIBuilder.CreateText(root, "ScoreText", "점수  0",
                30, new Color(0.98f, 0.91f, 0.58f),
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(30, -20), new Vector2(300, 40),
                TextAnchor.MiddleLeft, FontStyle.Bold);

            // 남은 시간
            _timeText = UIBuilder.CreateText(root, "TimeText", "60",
                34, Color.white,
                new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1),
                new Vector2(0, -20), new Vector2(200, 40),
                TextAnchor.MiddleCenter, FontStyle.Bold);

            // 서버 연결 상태
            _statusText = UIBuilder.CreateText(root, "StatusText",
                "● 서버 접속 중...",
                20, new Color(0.98f, 0.45f, 0.45f),
                new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1),
                new Vector2(-30, -22), new Vector2(400, 36),
                TextAnchor.MiddleRight);

            // 예측 상태 (왼쪽 아래)
            _predictText = UIBuilder.CreateText(root, "PredictText",
                "예측 ON",
                20, new Color(0.55f, 0.85f, 0.98f),
                new Vector2(0, 0), new Vector2(0, 0), new Vector2(0, 0),
                new Vector2(30, 60), new Vector2(520, 32),
                TextAnchor.MiddleLeft);

            // 하단 조작 안내
            UIBuilder.CreateText(root, "HelpText",
                "방향키 / WASD 이동   ·   P 예측 끄기/켜기   ·   R 다시 시작   ·   ESC 종료",
                20, new Color(0.55f, 0.60f, 0.75f),
                new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0),
                new Vector2(0, 24), new Vector2(800, 34),
                TextAnchor.MiddleCenter);

            // ── 3주차 추가 : 서버 메시지 로그 (좌측 하단) ──
            _chatLogText = UIBuilder.CreateText(root, "ChatLog",
                "",
                18, new Color(0.75f, 0.80f, 0.92f),
                new Vector2(0, 0), new Vector2(0, 0), new Vector2(0, 0),
                new Vector2(30, 70), new Vector2(600, 130),
                TextAnchor.LowerLeft);

            // 결과 패널 (처음엔 숨김)
            Image panel = UIBuilder.CreatePanel(root, "ResultPanel",
                new Color(0.05f, 0.06f, 0.14f, 0.92f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(560, 280));
            _resultPanel = panel.gameObject;

            _resultText = UIBuilder.CreateText(_resultPanel.transform, "ResultText",
                "", 40, Color.white,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0, 30), new Vector2(520, 120),
                TextAnchor.MiddleCenter, FontStyle.Bold);

            UIBuilder.CreateText(_resultPanel.transform, "RestartHint",
                "R 키를 눌러 다시 시작",
                24, new Color(0.60f, 0.65f, 0.80f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0, -70), new Vector2(520, 40),
                TextAnchor.MiddleCenter);

            _resultPanel.SetActive(false);
        }

        // ═══════════════════════════════════════
        // 게임 시작
        // ═══════════════════════════════════════
        private void StartGame()
        {
            // 기존 오브젝트 정리
            ClearItems();
            if (_playerObject != null) Destroy(_playerObject);

            // 플레이어 생성 (맵 중앙)
            _localPlayer = new PlayerState
            {
                PlayerId = 1,
                Nickname = "나",
                X = GameConfig.MAP_WIDTH  / 2,
                Y = GameConfig.MAP_HEIGHT / 2,
                Score = 0,
                IsLocal = true,
            };

            _playerObject = SpriteFactory.CreateSpriteObject(
                "LocalPlayer", SpriteFactory.Circle,
                GameConfig.PlayerColors[0],
                _localPlayer.WorldPosition,
                0.8f, 10, null);

            // 상태 초기화
            _remainTime     = GameConfig.ROUND_TIME;
            _isPlaying      = true;
            _moveTimer      = 0f;

            _resultPanel.SetActive(false);

            // 시작 시 아이템 몇 개 뿌리기
            for (int i = 0; i < 5; i++) SpawnItem();

            UpdateUI();
        }

        // ═══════════════════════════════════════
        // 매 프레임
        // ═══════════════════════════════════════
        private void Update()
        {
            // ── 3주차 추가 : 서버에서 온 메시지 처리 ──
            ProcessServerMessages();

            // ── 6주차 : 위치도 아이템도 전부 서버가 보내줍니다 ──
            ProcessRemotePositions();  // 내 캐릭터 포함 전원 위치 갱신
            ProcessServerItems();      // 서버가 뿌린 아이템 갱신
            InterpolateRemotes();      // 부드럽게 이동

            // 종료
            if (Input.GetKeyDown(KeyCode.Escape))
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
                return;
            }

            // ── 예측 끄기/켜기 ──
            //
            //   끄면 서버 답을 기다리는 동작이 됩니다.
            //   로컬 서버라 차이가 작지만, 서버를 다른 PC 에 두면
            //   확실히 느껴집니다. 예제 H 로는 지연을 직접 넣어볼 수 있습니다.
            if (Input.GetKeyDown(KeyCode.P))
            {
                _predictOn = !_predictOn;
                _corrections = 0;
                AddChat(_predictOn
                    ? "예측 ON — 키를 누르면 바로 움직입니다"
                    : "예측 OFF — 서버 답이 와야 움직입니다");
            }

            UpdatePredictText();

            // 다시 시작 (서버 재접속도 함께)
            if (Input.GetKeyDown(KeyCode.R))
            {
                StartGame();

                if (_network == null || !_network.IsConnected)
                    ConnectToServer();

                return;
            }

            if (!_isPlaying) return;

            HandleInput();
            UpdateTimer();
        }

        /// <summary>
        /// 서버에서 받은 패킷 처리
        ///
        /// 수신은 백그라운드 쓰레드에서 하고,
        /// 실제 처리(UI 변경)는 여기 메인 쓰레드에서 합니다.
        /// Unity는 다른 쓰레드에서 UI를 건드릴 수 없기 때문입니다.
        /// </summary>
        private void ProcessServerMessages()
        {
            if (_network == null) return;

            while (_network.TryGetMessage(out string json))
            {
                Debug.Log($"[수신] {json}");

                int type = JsonUtilityWrapper.GetInt(json, "Type");

                switch (type)
                {
                    case 11:  // 환영 메시지
                    {
                        string msg = JsonUtilityWrapper.GetString(json, "Message");
                        AddChat(msg);

                        // ── 5주차 추가 ──
                        // 서버가 알려준 내 플레이어 ID로 UDP를 시작합니다.
                        // UDP 패킷에 이 ID를 넣어야 서버가 누구인지 압니다.
                        _myPlayerId = JsonUtilityWrapper.GetInt(json, "PlayerId");

                        if (_myPlayerId > 0)
                        {
                            // ★ 재접속 대비 : 이전 UDP 클라이언트를 반드시 닫습니다
                            //
                            //   안 닫으면 옛 소켓이 옛 포트에서 계속 살아 있고,
                            //   서버가 그 옛 주소로 STATE를 보냅니다.
                            //   새 소켓의 큐는 영원히 비어 있게 됩니다.
                            //   → 재접속하면 다른 사람이 안 보이던 원인
                            _udp?.Stop();

                            _udp = new UdpNetworkClient();
                            _udp.Start(_myPlayerId);

                            // 새 연결이면 시퀀스가 1부터 다시 시작하므로
                            //         예전 입력 기록은 버려야 합니다.
                            //         안 버리면 보정이 엉뚱한 좌표를 계산합니다.
                            _pendingInputs.Clear();
                            _corrections = 0;
                            _lastAck     = 0;

                            AddChat("위치 동기화를 시작합니다.");
                        }
                        break;
                    }

                    case 20:  // 입장 알림
                    case 21:  // 퇴장 알림
                    {
                        string msg = JsonUtilityWrapper.GetString(json, "Message");
                        AddChat(msg);
                        break;
                    }

                    // ── 6주차 추가 ──
                    //   놓치면 점수가 틀어지는 것들이라 TCP로 옵니다

                    case 30:  // 누가 아이템을 먹었다
                    {
                        string nick = JsonUtilityWrapper.GetString(json, "Nickname");
                        int who     = JsonUtilityWrapper.GetInt(json, "PlayerId");

                        AddChat(who == _myPlayerId
                            ? "아이템을 먹었습니다!"
                            : $"{nick}님이 아이템을 먹었습니다.");
                        break;
                    }

                    case 31:  // 점수판
                    {
                        _scoreBoard = JsonUtilityWrapper.GetString(json, "Board");
                        ApplyScoreBoard();
                        break;
                    }
                }
            }
        }

        // ═══════════════════════════════════════
        // [5주차] UDP 위치 동기화
        // ═══════════════════════════════════════

        /// <summary>
        /// 내 위치를 서버로 보냅니다 (초당 30번)
        ///
        /// 매 프레임 보내면 낭비입니다.
        /// 모니터가 144Hz면 초당 144번 보내게 되니까요.
        /// 30번이면 사람 눈에는 충분합니다.
        /// </summary>
        /// <summary>
        /// 서버가 보내준 전체 플레이어 위치를 반영합니다
        /// </summary>
        private void ProcessRemotePositions()
        {
            if (_udp == null || !_udp.IsRunning) return;

            if (!_udp.TryGetLatestState(out List<RemotePlayer> players, out int ackSeq)) return;
            if (players == null) return;

            _lastAck = ackSeq;

            // 이번에 들어온 플레이어 ID 목록
            HashSet<int> alive = new HashSet<int>();

            foreach (var p in players)
            {
                // ★ 내 캐릭터는 "맞춰보기" 를 합니다
                //
                //   서버가 준 좌표를 그대로 쓰면
                //   내 입력이 왕복 시간만큼 늦게 반영됩니다.
                //
                //   이미 예측으로 움직여 놓았으니,
                //   서버 답이 오면 맞는지 확인만 합니다.
                if (p.PlayerId == _myPlayerId)
                {
                    ReconcileLocal(p.X, p.Y, ackSeq);
                    continue;
                }

                alive.Add(p.PlayerId);

                Vector3 worldPos = PlayerState.GridToWorld(p.X, p.Y);
                _remoteTargets[p.PlayerId] = worldPos;

                // 처음 보는 플레이어면 캐릭터를 만듭니다
                if (!_remoteObjects.ContainsKey(p.PlayerId))
                {
                    CreateRemotePlayer(p, worldPos);
                    AddChat($"{p.Nickname}님이 보이기 시작했습니다.");
                }

                // 닉네임 라벨 위치 갱신
                if (_remoteLabels.TryGetValue(p.PlayerId, out GameObject label) && label != null)
                    label.transform.position = _remoteObjects[p.PlayerId].transform.position
                                               + Vector3.up * 0.65f;
            }

            // 목록에서 사라진 플레이어는 캐릭터를 지웁니다
            RemoveDisappeared(alive);
        }

        /// <summary>다른 플레이어 캐릭터 만들기</summary>
        private void CreateRemotePlayer(RemotePlayer p, Vector3 pos)
        {
            // 플레이어 ID로 색을 정합니다 (1~3번 색 사용, 0번은 내 색)
            Color color = GameConfig.PlayerColors[
                1 + (p.PlayerId % (GameConfig.PlayerColors.Length - 1))];

            GameObject go = SpriteFactory.CreateSpriteObject(
                $"Remote_{p.PlayerId}", SpriteFactory.Circle,
                color, pos, 0.8f, 9, null);

            _remoteObjects[p.PlayerId] = go;

            // 머리 위 닉네임
            GameObject labelGo = new GameObject($"Label_{p.PlayerId}");
            labelGo.transform.position = pos + Vector3.up * 0.65f;

            TextMesh tm = labelGo.AddComponent<TextMesh>();
            tm.text = p.Nickname;
            tm.fontSize = 48;
            tm.characterSize = 0.08f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.color = color;

            MeshRenderer mr = labelGo.GetComponent<MeshRenderer>();
            if (mr != null) mr.sortingOrder = 20;

            _remoteLabels[p.PlayerId] = labelGo;
        }

        /// <summary>목록에 없는 플레이어 정리</summary>
        private void RemoveDisappeared(HashSet<int> alive)
        {
            List<int> toRemove = new List<int>();

            foreach (var id in _remoteObjects.Keys)
                if (!alive.Contains(id)) toRemove.Add(id);

            foreach (int id in toRemove)
            {
                if (_remoteObjects.TryGetValue(id, out GameObject go) && go != null)
                    Destroy(go);
                if (_remoteLabels.TryGetValue(id, out GameObject label) && label != null)
                    Destroy(label);

                _remoteObjects.Remove(id);
                _remoteLabels.Remove(id);
                _remoteTargets.Remove(id);
            }
        }

        /// <summary>
        /// 보간 (Interpolation)
        ///
        /// 서버는 초당 30번만 위치를 보냅니다.
        /// 받은 위치로 바로 순간이동시키면 뚝뚝 끊겨 보입니다.
        ///
        /// 그래서 목표 위치를 향해 조금씩 이동시킵니다.
        /// 한 프레임에 다 가지 않고 나눠서 가는 거예요.
        /// </summary>
        private void InterpolateRemotes()
        {
            foreach (var pair in _remoteObjects)
            {
                GameObject go = pair.Value;
                if (go == null) continue;

                if (!_remoteTargets.TryGetValue(pair.Key, out Vector3 target)) continue;

                // Lerp : 현재 위치에서 목표 위치로 조금씩
                go.transform.position = Vector3.Lerp(
                    go.transform.position, target, Time.deltaTime * 12f);
            }
        }

        /// <summary>게임 종료 시 연결도 정리</summary>
        private void OnApplicationQuit()
        {
            _network?.Disconnect();
            _udp?.Stop();
        }

        private void OnDestroy()
        {
            _network?.Disconnect();
            _udp?.Stop();
        }

        // ─────────────────────────────────────────
        // 입력 처리
        // ─────────────────────────────────────────
        private void HandleInput()
        {
            _moveTimer -= Time.deltaTime;
            if (_moveTimer > 0f) return;

            int dx = 0, dy = 0;

            if (Input.GetKey(KeyCode.UpArrow)    || Input.GetKey(KeyCode.W)) dy =  1;
            if (Input.GetKey(KeyCode.DownArrow)  || Input.GetKey(KeyCode.S)) dy = -1;
            if (Input.GetKey(KeyCode.LeftArrow)  || Input.GetKey(KeyCode.A)) dx = -1;
            if (Input.GetKey(KeyCode.RightArrow) || Input.GetKey(KeyCode.D)) dx =  1;

            if (dx == 0 && dy == 0) return;

            _moveTimer = GameConfig.MOVE_COOLDOWN;

            if (_udp == null || !_udp.IsRunning) return;

            // ── 1. 보내고 번호를 받아둡니다 ──
            int seq = _udp.SendDirection(dx, dy);
            if (seq == 0) return;

            // ── 2. 아직 확인 안 된 입력으로 적어둡니다 ──
            //
            //   서버가 "이 번호까지 처리했다" 고 알려줄 때까지
            //   이 입력은 "내 예측에만 반영된 상태" 입니다.
            _pendingInputs.Add(new PendingInput { Seq = seq, Dx = dx, Dy = dy });

            // ── 3. 예측 : 서버를 기다리지 않고 화면을 먼저 움직입니다 ──
            //
            //   ★ 여기가 "예측" 입니다.
            //
            //     예측 없이 : 방향만 보내고 가만히 기다립니다.
            //                 STATE 가 와야 캐릭터가 움직입니다.
            //     예측 있이 : 서버가 어떻게 계산할지 내가 알고 있으니
            //                 같은 계산을 먼저 해서 화면에 반영합니다.
            //
            //   StepGrid 가 서버의 GameLoop.StepPlayers 와 같은 계산입니다.
            //   같아야 예측이 맞습니다.
            if (_predictOn)
            {
                StepGrid(_localPlayer, dx, dy);
                DrawLocalPlayer();
            }
        }

        /// <summary>
        /// 서버와 똑같은 이동 계산
        ///
        /// 서버 : s.X = Math.Clamp(s.X + cmd.Dx, 0, MAP_W - 1);
        /// 클라 : 아래 한 줄
        ///
        /// 이 두 줄이 달라지는 순간 예측이 틀리기 시작합니다.
        /// 나중에 벽이나 장애물이 생기면 이 함수에도 같이 넣어야 합니다.
        /// </summary>
        private static void StepGrid(PlayerState p, int dx, int dy)
        {
            p.X = Mathf.Clamp(p.X + dx, 0, GameConfig.MAP_WIDTH  - 1);
            p.Y = Mathf.Clamp(p.Y + dy, 0, GameConfig.MAP_HEIGHT - 1);
        }

        /// <summary>내 캐릭터를 현재 격자 좌표 자리에 그립니다</summary>
        private void DrawLocalPlayer()
        {
            if (_playerObject == null || _localPlayer == null) return;

            _playerObject.transform.position =
                PlayerState.GridToWorld(_localPlayer.X, _localPlayer.Y);
        }

        // ─────────────────────────────────────────
        // 보정 (Server Reconciliation)
        //
        //  서버 답이 도착했을 때 하는 일 세 가지입니다.
        //
        //    1. 서버가 처리했다는 입력은 목록에서 지운다
        //    2. 서버가 알려준 좌표를 출발점으로 삼는다      ← 서버가 진실
        //    3. 아직 확인 안 된 입력을 다시 적용한다
        //
        //  예측이 맞았으면 3번 결과가 지금 화면과 같습니다. 아무 일도 없습니다.
        //  틀렸으면 좌표가 바뀝니다. 그게 게임에서 보이는 "고무줄 현상" 입니다.
        //
        //  왜 그냥 서버 좌표로 덮지 않고 다시 적용하나 —
        //  서버 좌표는 왕복 시간만큼 과거입니다.
        //  그대로 쓰면 캐릭터가 뒤로 끌려갑니다.
        //  그 뒤에 보낸 입력을 다시 적용해야 "지금" 자리가 나옵니다.
        // ─────────────────────────────────────────
        private void ReconcileLocal(int serverX, int serverY, int ackSeq)
        {
            if (_localPlayer == null) return;

            // 1. 서버가 처리한 입력은 지웁니다
            //    예측을 쓰든 안 쓰든 지워야 합니다. 안 지우면 끝없이 쌓입니다.
            _pendingInputs.RemoveAll(i => i.Seq <= ackSeq);

            if (!_predictOn)
            {
                // 예측 OFF — 서버 좌표가 곧 화면입니다.
                // 그래서 왕복 시간만큼 늦게 움직입니다.
                _localPlayer.X = serverX;
                _localPlayer.Y = serverY;
                DrawLocalPlayer();
                return;
            }

            int beforeX = _localPlayer.X;
            int beforeY = _localPlayer.Y;

            // 2. 서버 좌표에서 출발
            _localPlayer.X = serverX;
            _localPlayer.Y = serverY;

            // 3. 확인 안 된 입력을 순서대로 다시 적용
            for (int i = 0; i < _pendingInputs.Count; i++)
                StepGrid(_localPlayer, _pendingInputs[i].Dx, _pendingInputs[i].Dy);

            if (_localPlayer.X != beforeX || _localPlayer.Y != beforeY)
                _corrections++;

            DrawLocalPlayer();
        }

        // ─────────────────────────────────────────
        // 아이템 획득 판정
        //
        // [2주차] 클라이언트가 판정했습니다.
        // [6주차] 서버로 옮겼습니다. 여기에는 이제 아무것도 없습니다.
        //
        //   클라이언트가 "먹었다" 고 주장하면 서버가 믿을 수밖에 없고,
        //   그러면 맵 반대편 아이템도 먹을 수 있게 됩니다.
        //   판정은 상태를 가진 쪽이 해야 합니다.
        //
        //   → GameLoop.CheckPickupsAsync() 를 보세요
        // ─────────────────────────────────────────

        // ─────────────────────────────────────────
        // 아이템 — 6주차부터 서버가 뿌립니다
        //
        //   클라이언트는 받아서 그리기만 합니다.
        //   어디에 생길지, 누가 먹었는지는 전혀 모릅니다.
        // ─────────────────────────────────────────
        private void ProcessServerItems()
        {
            if (_udp == null || !_udp.IsRunning) return;

            if (!_udp.TryGetLatestItems(out List<RemoteItem> items)) return;
            if (items == null) return;

            HashSet<int> alive = new HashSet<int>();

            foreach (var it in items)
            {
                alive.Add(it.ItemId);

                if (_itemObjects.ContainsKey(it.ItemId)) continue;

                // 처음 보는 아이템이면 만들어 줍니다
                Vector3 pos = PlayerState.GridToWorld(it.X, it.Y);

                GameObject go = SpriteFactory.CreateSpriteObject(
                    $"Item_{it.ItemId}", SpriteFactory.Circle,
                    new Color(1f, 0.82f, 0.25f), pos,
                    0.45f, 5, null);

                _itemObjects[it.ItemId] = go;
            }

            // 서버 목록에서 사라진 아이템은 누가 먹은 것입니다
            var gone = new List<int>();
            foreach (var kv in _itemObjects)
                if (!alive.Contains(kv.Key)) gone.Add(kv.Key);

            foreach (int id in gone)
            {
                if (_itemObjects.TryGetValue(id, out GameObject go) && go != null)
                    Destroy(go);

                _itemObjects.Remove(id);
            }
        }

        private void ClearItems()
        {
            foreach (var kv in _itemObjects)
            {
                if (kv.Value != null) Destroy(kv.Value);
            }
            _itemObjects.Clear();
        }

        // ─────────────────────────────────────────
        // 타이머
        // ─────────────────────────────────────────
        private void UpdateTimer()
        {
            _remainTime -= Time.deltaTime;

            if (_remainTime <= 0f)
            {
                _remainTime = 0f;
                EndGame();
            }

            UpdateUI();
        }

        private void EndGame()
        {
            _isPlaying = false;

            _resultText.text = $"게임 종료\n\n최종 점수  {_localPlayer.Score}점";
            _resultPanel.SetActive(true);

            // ★ 나중에 여기서 서버에 결과를 전송합니다 (9주차)
            //   예) NetworkClient.SendGameResult(_localPlayer.Score);
        }

        /// <summary>
        /// 서버가 보낸 점수판을 반영합니다
        ///
        /// 형식 : "1,홍길동,5|3,철수,2"
        /// </summary>
        private void ApplyScoreBoard()
        {
            if (string.IsNullOrEmpty(_scoreBoard)) return;

            foreach (string row in _scoreBoard.Split('|'))
            {
                string[] f = row.Split(',');
                if (f.Length < 3) continue;

                if (!int.TryParse(f[0], out int id)) continue;
                if (!int.TryParse(f[2], out int score)) continue;

                if (id == _myPlayerId && _localPlayer != null)
                    _localPlayer.Score = score;
            }

            UpdateUI();
        }

        // ─────────────────────────────────────────
        // UI 갱신
        // ─────────────────────────────────────────
        private void UpdateUI()
        {
            if (_scoreText != null)
                _scoreText.text = $"점수  {_localPlayer.Score}";

            if (_timeText != null)
            {
                int sec = Mathf.CeilToInt(_remainTime);
                _timeText.text = sec.ToString();

                // 10초 이하면 빨갛게
                _timeText.color = sec <= 10
                    ? new Color(0.98f, 0.38f, 0.40f)
                    : Color.white;
            }
        }
    }
}
