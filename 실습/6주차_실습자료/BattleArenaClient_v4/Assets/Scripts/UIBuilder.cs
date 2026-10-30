using UnityEngine;
using UnityEngine.UI;

namespace BattleArena
{
    /// <summary>
    /// 코드로 UI를 만들어주는 유틸리티
    /// Canvas, Text 등을 런타임에 생성합니다.
    /// </summary>
    public static class UIBuilder
    {
        private static Font _defaultFont;

        public static Font DefaultFont
        {
            get
            {
                if (_defaultFont == null)
                {
                    // Unity 6 내장 폰트
                    _defaultFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

                    // 구버전 대응
                    if (_defaultFont == null)
                        _defaultFont = Resources.GetBuiltinResource<Font>("Arial.ttf");

                    // 그래도 없으면 OS 폰트
                    if (_defaultFont == null)
                        _defaultFont = Font.CreateDynamicFontFromOSFont("Arial", 16);
                }
                return _defaultFont;
            }
        }

        /// <summary>화면 전체를 덮는 Canvas 생성</summary>
        public static Canvas CreateCanvas(string name)
        {
            GameObject go = new GameObject(name);

            Canvas canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            CanvasScaler scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            scaler.matchWidthOrHeight  = 0.5f;

            go.AddComponent<GraphicRaycaster>();

            return canvas;
        }

        /// <summary>텍스트 생성</summary>
        public static Text CreateText(
            Transform parent, string name, string content,
            int fontSize, Color color,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
            Vector2 anchoredPos, Vector2 sizeDelta,
            TextAnchor alignment = TextAnchor.MiddleLeft,
            FontStyle style = FontStyle.Normal)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);

            RectTransform rt = go.AddComponent<RectTransform>();
            rt.anchorMin        = anchorMin;
            rt.anchorMax        = anchorMax;
            rt.pivot            = pivot;
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta        = sizeDelta;

            Text text = go.AddComponent<Text>();
            text.text      = content;
            text.font      = DefaultFont;
            text.fontSize  = fontSize;
            text.color     = color;
            text.alignment = alignment;
            text.fontStyle = style;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow   = VerticalWrapMode.Overflow;

            return text;
        }

        /// <summary>배경 패널 생성</summary>
        public static Image CreatePanel(
            Transform parent, string name, Color color,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
            Vector2 anchoredPos, Vector2 sizeDelta)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);

            RectTransform rt = go.AddComponent<RectTransform>();
            rt.anchorMin        = anchorMin;
            rt.anchorMax        = anchorMax;
            rt.pivot            = pivot;
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta        = sizeDelta;

            Image img = go.AddComponent<Image>();
            img.color = color;

            return img;
        }
    }
}
