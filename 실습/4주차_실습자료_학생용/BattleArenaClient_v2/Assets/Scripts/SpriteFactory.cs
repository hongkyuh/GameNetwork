using UnityEngine;

namespace BattleArena
{
    /// <summary>
    /// 코드로 스프라이트를 만들어주는 유틸리티
    ///
    /// 프리팹이나 이미지 파일 없이 원/사각형을 그립니다.
    /// 덕분에 압축만 풀면 바로 실행됩니다.
    /// </summary>
    public static class SpriteFactory
    {
        private static Sprite _circleSprite;
        private static Sprite _squareSprite;

        /// <summary>원형 스프라이트 (플레이어, 아이템용)</summary>
        public static Sprite Circle
        {
            get
            {
                if (_circleSprite == null)
                    _circleSprite = CreateCircle(64);
                return _circleSprite;
            }
        }

        /// <summary>사각형 스프라이트 (배경, 격자용)</summary>
        public static Sprite Square
        {
            get
            {
                if (_squareSprite == null)
                    _squareSprite = CreateSquare();
                return _squareSprite;
            }
        }

        // ─────────────────────────────────────────
        // 원 그리기
        // ─────────────────────────────────────────
        private static Sprite CreateCircle(int size)
        {
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;

            float center = size * 0.5f;
            float radius = center - 1f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - center;
                    float dy = y + 0.5f - center;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);

                    // 가장자리 부드럽게 (안티앨리어싱)
                    float alpha = Mathf.Clamp01(radius - dist);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }

            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size),
                new Vector2(0.5f, 0.5f), size);
        }

        // ─────────────────────────────────────────
        // 사각형 그리기
        // ─────────────────────────────────────────
        private static Sprite CreateSquare()
        {
            Texture2D tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;

            Color[] pixels = new Color[16];
            for (int i = 0; i < 16; i++) pixels[i] = Color.white;
            tex.SetPixels(pixels);
            tex.Apply();

            return Sprite.Create(tex, new Rect(0, 0, 4, 4),
                new Vector2(0.5f, 0.5f), 4);
        }

        // ─────────────────────────────────────────
        // 헬퍼: 스프라이트 오브젝트 생성
        // ─────────────────────────────────────────
        public static GameObject CreateSpriteObject(
            string name, Sprite sprite, Color color,
            Vector3 position, float scale, int sortingOrder, Transform parent = null)
        {
            GameObject go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);

            go.transform.position   = position;
            go.transform.localScale = Vector3.one * scale;

            SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
            sr.sprite       = sprite;
            sr.color        = color;
            sr.sortingOrder = sortingOrder;

            return go;
        }
    }
}
