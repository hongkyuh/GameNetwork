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
    ///   6주차  아이템 획득 판정을 서버가 담당
    ///   9주차  게임 결과를 DB에 저장
    ///  10주차  랭킹 (Redis)
    /// ═══════════════════════════════════════════════════
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        // ── 게임 상태 ──
        private PlayerState _localPlayer;
        private List<ItemState> _items = new List<ItemState>();
        private int _nextItemId = 1;

        private float _remainTime;
        private bool  _isPlaying;

        // ── 이동 쿨다운 ──
        private float _moveTimer;

        // ── 아이템 생성 타이머 ──
        private float _itemSpawnTimer;

        // ── 렌더링 오브젝트 ──
        private GameObject _playerObject;
        private Dictionary<int, GameObject> _itemObjects = new Dictionary<int, GameObject>();
        private Transform _boardRoot;

        // ── UI ──
        private Text _scoreText;
        private Text _timeText;
        private Text _statusText;
        private Text _resultText;
        private GameObject _resultPanel;

        // ── 네트워크 (3주차 추가) ──
        private NetworkClient _network;
        private string _nickname = "Player";
        private Text _chatLogText;
        private System.Collections.Generic.List<string> _chatLines
            = new System.Collections.Generic.List<string>();

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

            // 하단 조작 안내
            // 하단 조작 안내
            UIBuilder.CreateText(root, "HelpText",
                "방향키 / WASD 이동     ·     R 다시 시작     ·     ESC 종료",
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
            _itemSpawnTimer = 0f;
            _nextItemId     = 1;

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
            UpdateItemSpawn();
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
                        break;
                    }

                    case 20:  // 입장 알림
                    case 21:  // 퇴장 알림
                    {
                        string msg = JsonUtilityWrapper.GetString(json, "Message");
                        AddChat(msg);
                        break;
                    }
                }
            }
        }

        /// <summary>게임 종료 시 연결도 정리</summary>
        private void OnApplicationQuit()
        {
            _network?.Disconnect();
        }

        private void OnDestroy()
        {
            _network?.Disconnect();
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

            int nx = _localPlayer.X + dx;
            int ny = _localPlayer.Y + dy;

            // 맵 경계 검사
            if (!GameConfig.IsInside(nx, ny)) return;

            _localPlayer.X = nx;
            _localPlayer.Y = ny;
            _moveTimer = GameConfig.MOVE_COOLDOWN;

            _playerObject.transform.position = _localPlayer.WorldPosition;

            // ★ 여기가 나중에 서버로 위치를 보내는 지점입니다 (4주차)
            //   예) NetworkClient.SendMove(_localPlayer.X, _localPlayer.Y);

            CheckItemPickup();
        }

        // ─────────────────────────────────────────
        // 아이템 획득 판정
        //
        // [2주차] 지금은 클라이언트가 판정합니다.
        // [6주차] 서버가 판정하도록 바꿉니다.
        //         클라이언트가 판정하면 조작이 가능하기 때문입니다.
        // ─────────────────────────────────────────
        private void CheckItemPickup()
        {
            for (int i = _items.Count - 1; i >= 0; i--)
            {
                ItemState item = _items[i];

                if (item.X == _localPlayer.X && item.Y == _localPlayer.Y)
                {
                    _localPlayer.Score += item.Score;
                    RemoveItem(item);
                    UpdateUI();
                }
            }
        }

        // ─────────────────────────────────────────
        // 아이템 생성
        // ─────────────────────────────────────────
        private void UpdateItemSpawn()
        {
            if (_items.Count >= GameConfig.ITEM_MAX_COUNT) return;

            _itemSpawnTimer -= Time.deltaTime;
            if (_itemSpawnTimer > 0f) return;

            SpawnItem();
            _itemSpawnTimer = GameConfig.ITEM_SPAWN_SEC;
        }

        private void SpawnItem()
        {
            // 빈 칸 찾기 (최대 30번 시도)
            int x = 0, y = 0;
            bool found = false;

            for (int attempt = 0; attempt < 30; attempt++)
            {
                x = Random.Range(0, GameConfig.MAP_WIDTH);
                y = Random.Range(0, GameConfig.MAP_HEIGHT);

                // 플레이어 위치 제외
                if (_localPlayer != null && x == _localPlayer.X && y == _localPlayer.Y)
                    continue;

                // 다른 아이템과 겹치지 않게
                bool occupied = false;
                foreach (var it in _items)
                {
                    if (it.X == x && it.Y == y) { occupied = true; break; }
                }
                if (occupied) continue;

                found = true;
                break;
            }

            if (!found) return;

            // 15% 확률로 보석
            ItemType type = Random.value < 0.15f ? ItemType.Gem : ItemType.Coin;

            ItemState item = new ItemState
            {
                ItemId = _nextItemId++,
                X = x, Y = y,
                Type = type,
            };
            _items.Add(item);

            GameObject go = SpriteFactory.CreateSpriteObject(
                $"Item_{item.ItemId}", SpriteFactory.Circle,
                item.Color, item.WorldPosition,
                item.Size, 5, null);

            _itemObjects[item.ItemId] = go;
        }

        private void RemoveItem(ItemState item)
        {
            _items.Remove(item);

            if (_itemObjects.TryGetValue(item.ItemId, out GameObject go))
            {
                Destroy(go);
                _itemObjects.Remove(item.ItemId);
            }
        }

        private void ClearItems()
        {
            foreach (var kv in _itemObjects)
            {
                if (kv.Value != null) Destroy(kv.Value);
            }
            _itemObjects.Clear();
            _items.Clear();
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
