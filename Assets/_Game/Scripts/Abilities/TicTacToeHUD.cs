using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace FPS
{
    /// <summary>
    /// Board of the local player's tic-tac-toe game: 3×3 cells clicked with the mouse, whose move it is, a timer
    /// for your move, the winning line lit up and the result. Opened by <see cref="TicTacToe"/>.
    /// </summary>
    [DefaultExecutionOrder(130)]
    public class TicTacToeHUD : MonoBehaviour
    {
        public static TicTacToeHUD Instance { get; private set; }

        static readonly Color XCol = new Color(1f, 0.35f, 0.28f);
        static readonly Color OCol = new Color(0.35f, 0.7f, 1f);
        static readonly Color CellCol = new Color(0.2f, 0.22f, 0.26f, 0.96f);

        Font font;
        Sprite white;
        CanvasGroup group;
        RectTransform panel, timerFill;
        Image dim, timerImg;
        readonly RectTransform[] cellRt = new RectTransform[9];
        readonly Image[] cellImg = new Image[9];
        readonly Text[] cellTxt = new Text[9];
        readonly float[] cellPop = new float[9];
        readonly int[] shown = new int[9];
        Text title, youText, themText, status, hint;
        TicTacToe game;
        float show, openT;

        public static TicTacToeHUD Ensure()
        {
            if (Instance) return Instance;
            Instance = new GameObject("TicTacToeHUD").AddComponent<TicTacToeHUD>();
            return Instance;
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        void Awake()
        {
            Instance = this;
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 35;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            var tex = Texture2D.whiteTexture;
            white = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
            Build();
        }

        RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        Image Img(RectTransform rt, Color c)
        {
            var i = rt.gameObject.AddComponent<Image>();
            i.sprite = white; i.color = c; i.raycastTarget = false;
            return i;
        }

        Text Txt(RectTransform rt, int size, Color c, FontStyle style = FontStyle.Bold, TextAnchor a = TextAnchor.MiddleCenter)
        {
            var t = rt.gameObject.AddComponent<Text>();
            t.font = font; t.fontSize = size; t.color = c; t.fontStyle = style; t.alignment = a;
            t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            var sh = rt.gameObject.AddComponent<Shadow>();
            sh.effectColor = new Color(0f, 0f, 0f, 0.7f);
            sh.effectDistance = new Vector2(2f, -2f);
            return t;
        }

        void Build()
        {
            var root = Rect("Root", transform, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            root.anchorMin = Vector2.zero; root.anchorMax = Vector2.one;
            group = root.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;
            var dimRt = Rect("Dim", root, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            dimRt.anchorMin = Vector2.zero; dimRt.anchorMax = Vector2.one;
            dim = Img(dimRt, new Color(0f, 0.02f, 0.06f, 0.45f));

            panel = Rect("Panel", root, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(560f, 720f));
            Img(panel, new Color(0.08f, 0.09f, 0.11f, 0.96f));
            Img(Rect("Edge", panel, new Vector2(0.5f, 1f), new Vector2(0f, -3f), new Vector2(560f, 6f)), new Color(0.75f, 0.85f, 1f));
            title = Txt(Rect("Title", panel, new Vector2(0.5f, 1f), new Vector2(0f, -46f), new Vector2(540f, 50f)), 40, new Color(0.85f, 0.92f, 1f));
            title.text = "КРЕСТИКИ-НОЛИКИ";
            hint = Txt(Rect("Hint", panel, new Vector2(0.5f, 1f), new Vector2(0f, -84f), new Vector2(540f, 26f)), 17, new Color(1f, 1f, 1f, 0.7f), FontStyle.Normal);
            hint.text = "Пока идёт партия, вас обоих никто не может ранить";
            youText = Txt(Rect("You", panel, new Vector2(0.5f, 1f), new Vector2(-130f, -128f), new Vector2(260f, 34f)), 26, XCol);
            themText = Txt(Rect("Them", panel, new Vector2(0.5f, 1f), new Vector2(130f, -128f), new Vector2(260f, 34f)), 26, OCol);
            Txt(Rect("Vs", panel, new Vector2(0.5f, 1f), new Vector2(0f, -128f), new Vector2(60f, 34f)), 20, new Color(1f, 1f, 1f, 0.5f)).text = "vs";

            const float cs = 140f, gap = 10f;
            for (int i = 0; i < 9; i++)
            {
                int x = i % 3, y = i / 3;
                var rt = Rect("Cell" + i, panel, new Vector2(0.5f, 0.5f), new Vector2((x - 1) * (cs + gap), (1 - y) * (cs + gap) - 18f), new Vector2(cs, cs));
                cellRt[i] = rt;
                cellImg[i] = Img(rt, CellCol);
                cellTxt[i] = Txt(Rect("Mark", rt, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(cs, cs)), 110, Color.white);
            }

            status = Txt(Rect("Status", panel, new Vector2(0.5f, 0f), new Vector2(0f, 66f), new Vector2(540f, 40f)), 30, Color.white);
            var tb = Rect("Timer", panel, new Vector2(0.5f, 0f), new Vector2(0f, 30f), new Vector2(420f, 10f));
            Img(tb, new Color(1f, 1f, 1f, 0.15f));
            timerFill = Rect("Fill", tb, new Vector2(0f, 0.5f), Vector2.zero, new Vector2(420f, 10f));
            timerFill.pivot = new Vector2(0f, 0.5f);
            timerImg = Img(timerFill, XCol);
        }

        public void Open(TicTacToe g)
        {
            game = g;
            openT = 0f;
            for (int i = 0; i < 9; i++) { shown[i] = 0; cellPop[i] = 0f; }
            ProcAudio.Play(Sfx.ErrorTone, 0.5f, 0f);
        }

        public void Closed(TicTacToe g)
        {
            if (game == g) game = null;
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            show = Mathf.MoveTowards(show, game ? 1f : 0f, dt * (game ? 6f : 3f));
            group.alpha = show;
            openT += dt;
            float pop = Anim.Smooth(openT / 0.25f);
            panel.localScale = Vector3.one * (game ? Mathf.Lerp(0.85f, 1f, pop) : 1f);
            if (!game) return;

            var me = PlayerHealth.Local ? PlayerHealth.Local.GetComponent<Combatant>() : null;
            var mySide = game.Me(me);
            if (mySide == null) return;
            var them = game.Other(mySide);
            int myMark = game.Mark(mySide);
            Color myCol = myMark == 1 ? XCol : OCol, theirCol = myMark == 1 ? OCol : XCol;
            youText.text = "ВЫ  " + (myMark == 1 ? "X" : "O");
            youText.color = myCol;
            themText.text = (them.c ? them.c.displayName : "?") + "  " + (myMark == 1 ? "O" : "X");
            themText.color = theirCol;

            bool myTurn = game.Result == 0 && game.Turn == myMark;
            int hover = -1;
            var m = Mouse.current;
            if (m != null && myTurn && !PauseMenu.IsOpen)
            {
                Vector2 pos = m.position.ReadValue();
                for (int i = 0; i < 9; i++)
                {
                    if (game.board[i] != 0 || !RectTransformUtility.RectangleContainsScreenPoint(cellRt[i], pos, null)) continue;
                    hover = i;
                    if (m.leftButton.wasPressedThisFrame && game.TryHumanMove(me, i)) hover = -1;
                    break;
                }
            }

            for (int i = 0; i < 9; i++)
            {
                int v = game.board[i];
                if (v != shown[i]) { shown[i] = v; cellPop[i] = 1f; }
                cellPop[i] = Mathf.MoveTowards(cellPop[i], 0f, dt * 5f);
                cellTxt[i].text = v == 1 ? "X" : v == 2 ? "O" : hover == i ? (myMark == 1 ? "X" : "O") : "";
                Color mc = v == 1 ? XCol : v == 2 ? OCol : new Color(myCol.r, myCol.g, myCol.b, 0.3f);
                cellTxt[i].color = mc;
                cellTxt[i].rectTransform.localScale = Vector3.one * (1f + 0.35f * cellPop[i]);
                bool lit = game.WinLine != null && System.Array.IndexOf(game.WinLine, i) >= 0;
                cellImg[i].color = lit ? Color.Lerp(CellCol, new Color(1f, 0.85f, 0.3f), 0.55f + 0.25f * Mathf.Sin(Time.unscaledTime * 12f))
                                 : hover == i ? new Color(0.3f, 0.33f, 0.38f, 0.98f) : CellCol;
            }

            if (game.Result == 0)
            {
                status.text = myTurn ? "Ваш ход" : "Ходит " + (them.c ? them.c.displayName : "соперник") + "…";
                status.color = myTurn ? myCol : new Color(1f, 1f, 1f, 0.75f);
                float k = Mathf.Clamp01(game.MoveLeft / TicTacToe.MoveTime);
                timerFill.sizeDelta = new Vector2(420f * (myTurn ? k : 0f), 10f);
                timerImg.color = k < 0.3f ? new Color(1f, 0.3f, 0.2f) : myCol;
            }
            else
            {
                timerFill.sizeDelta = new Vector2(0f, 10f);
                bool won = game.Result == myMark, draw = game.Result == 3;
                status.text = draw ? "НИЧЬЯ — расходитесь" : won ? "ПОБЕДА! Соперник взлетает на воздух" : "ПОРАЖЕНИЕ…";
                status.color = draw ? new Color(1f, 1f, 1f, 0.9f) : won ? new Color(1f, 0.85f, 0.35f) : new Color(1f, 0.3f, 0.25f);
            }
        }
    }
}
