using UnityEngine;
using UnityEngine.UI;

namespace FPS
{
    /// <summary>
    /// Dynamic crosshair, animated hitmarker, ammo / weapon / grenade info, interaction prompts,
    /// sniper scope overlay, speedometer, stats and control hints.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class HUD : MonoBehaviour
    {
        public static HUD Instance { get; private set; }

        Canvas canvas;
        Font font;
        Loadout loadout;
        MeleeController melee;
        PlayerMotor motor;
        Camera cam;

        readonly RectTransform[] cross = new RectTransform[4];
        readonly RectTransform[] hit = new RectTransform[4];
        readonly Image[] hitImg = new Image[4];
        Text ammoText, reserveText, modeText, weaponText, slotsText, grenadeText, statsText, hintText, promptText, speedText, fpsText, sprintText;
        readonly System.Collections.Generic.List<Image> crossImgs = new System.Collections.Generic.List<Image>();
        float fpsAcc; int fpsFrames;
        CanvasGroup crossGroup, scopeGroup, promptGroup;

        float gap = 10f, crossAlpha = 1f, scopeAlpha, promptAlpha;
        float hitTimer, hitDuration = 0.25f, hitScale = 1f;
        Color hitColor = Color.white;
        int shots, hits, kills;
        int shownAmmo = -1;
        float ammoPunch;
        bool scopeOn;
        WeaponController lastWeapon;

        public static HUD Ensure()
        {
            if (Instance) return Instance;
            Instance = new GameObject("HUD").AddComponent<HUD>();
            return Instance;
        }

        void Awake()
        {
            Instance = this;
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 20;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
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

        Image Img(string name, Transform parent, Vector2 size, Color c, bool outline = true)
        {
            var rt = Rect(name, parent, new Vector2(0.5f, 0.5f), Vector2.zero, size);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = c;
            img.raycastTarget = false;
            if (outline)
            {
                var o = rt.gameObject.AddComponent<Outline>();
                o.effectColor = new Color(0f, 0f, 0f, 0.55f);
                o.effectDistance = new Vector2(1f, -1f);
            }
            return img;
        }

        Text Label(string name, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size, int fontSize, TextAnchor align)
        {
            var rt = Rect(name, parent, anchor, pos, size);
            rt.pivot = anchor;
            var t = rt.gameObject.AddComponent<Text>();
            t.font = font;
            t.fontSize = fontSize;
            t.alignment = align;
            t.color = Color.white;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            var sh = rt.gameObject.AddComponent<Shadow>();
            sh.effectColor = new Color(0f, 0f, 0f, 0.6f);
            sh.effectDistance = new Vector2(2f, -2f);
            return t;
        }

        void Build()
        {
            // ---- sniper scope overlay (behind everything else)
            var scope = Rect("Scope", transform, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            scope.anchorMin = Vector2.zero; scope.anchorMax = Vector2.one; scope.sizeDelta = Vector2.zero;
            scopeGroup = scope.gameObject.AddComponent<CanvasGroup>();
            scopeGroup.alpha = 0f; scopeGroup.blocksRaycasts = false;
            var lens = Rect("Lens", scope, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1080f, 1080f));
            var raw = lens.gameObject.AddComponent<RawImage>();
            raw.texture = ScopeTexture(512);
            raw.raycastTarget = false;
            foreach (int sx in new[] { -1, 1 })
            {
                var side = Rect("Side", scope, new Vector2(0.5f, 0.5f), new Vector2(sx * (540f + 1000f), 0f), new Vector2(2000f, 1200f));
                var im = side.gameObject.AddComponent<Image>(); im.color = Color.black; im.raycastTarget = false;
            }

            // ---- crosshair
            var center = Rect("Crosshair", transform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(10, 10));
            crossGroup = center.gameObject.AddComponent<CanvasGroup>();
            crossGroup.blocksRaycasts = false;
            Color cc = new Color(1f, 1f, 1f, 0.92f);
            for (int i = 0; i < 4; i++)
            {
                var li = Img("Line" + i, center, i < 2 ? new Vector2(2.5f, 11f) : new Vector2(11f, 2.5f), cc);
                cross[i] = li.rectTransform;
                crossImgs.Add(li);
            }
            crossImgs.Add(Img("Dot", center, new Vector2(3f, 3f), cc));

            var hm = Rect("Hitmarker", transform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(10, 10));
            for (int i = 0; i < 4; i++)
            {
                var img = Img("Hit" + i, hm, new Vector2(3f, 13f), Color.white);
                img.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f + 90f * i);
                hit[i] = img.rectTransform;
                hitImg[i] = img;
                img.color = new Color(1f, 1f, 1f, 0f);
            }

            // ---- prompt
            var pr = Rect("Prompt", transform, new Vector2(0.5f, 0.5f), new Vector2(0f, -90f), new Vector2(600f, 50f));
            promptGroup = pr.gameObject.AddComponent<CanvasGroup>();
            promptGroup.alpha = 0f; promptGroup.blocksRaycasts = false;
            var bg = pr.gameObject.AddComponent<Image>(); bg.color = new Color(0f, 0f, 0f, 0.45f); bg.raycastTarget = false;
            promptText = Label("PromptText", pr, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(600f, 50f), 26, TextAnchor.MiddleCenter);
            promptText.fontStyle = FontStyle.Bold;

            // ---- weapon panel (bottom right): soft dark plate with an accent edge
            var plate = Rect("WeaponPlate", transform, new Vector2(1f, 0f), new Vector2(-24f, 24f), new Vector2(470f, 226f));
            plate.pivot = new Vector2(1f, 0f); plate.anchoredPosition = new Vector2(-24f, 24f);
            var praw = plate.gameObject.AddComponent<RawImage>();
            praw.texture = PlateTexture(); praw.raycastTarget = false;
            var edge = Rect("Edge", plate, new Vector2(1f, 0f), Vector2.zero, new Vector2(3f, 150f));
            edge.pivot = new Vector2(1f, 0f);
            var eimg = edge.gameObject.AddComponent<Image>(); eimg.color = new Color(1f, 0.74f, 0.26f, 0.85f); eimg.raycastTarget = false;
            ammoText = Label("Ammo", transform, new Vector2(1f, 0f), new Vector2(-150f, 48f), new Vector2(200f, 80f), 64, TextAnchor.LowerRight);
            ammoText.fontStyle = FontStyle.Bold;
            reserveText = Label("Reserve", transform, new Vector2(1f, 0f), new Vector2(-50f, 56f), new Vector2(100f, 40f), 30, TextAnchor.LowerRight);
            reserveText.color = new Color(1f, 1f, 1f, 0.65f);
            weaponText = Label("Weapon", transform, new Vector2(1f, 0f), new Vector2(-50f, 128f), new Vector2(400f, 34f), 28, TextAnchor.LowerRight);
            weaponText.fontStyle = FontStyle.Bold;
            modeText = Label("Mode", transform, new Vector2(1f, 0f), new Vector2(-50f, 160f), new Vector2(300f, 26f), 18, TextAnchor.LowerRight);
            modeText.color = new Color(1f, 1f, 1f, 0.7f);
            slotsText = Label("Slots", transform, new Vector2(1f, 0f), new Vector2(-50f, 190f), new Vector2(500f, 26f), 18, TextAnchor.LowerRight);
            slotsText.color = new Color(1f, 1f, 1f, 0.6f);
            grenadeText = Label("Grenades", transform, new Vector2(1f, 0f), new Vector2(-50f, 218f), new Vector2(300f, 26f), 20, TextAnchor.LowerRight);
            grenadeText.color = new Color(0.75f, 1f, 0.6f, 0.9f);

            fpsText = Label("Fps", transform, new Vector2(0f, 1f), new Vector2(16f, -12f), new Vector2(200f, 26f), 18, TextAnchor.UpperLeft);
            fpsText.color = new Color(0.6f, 1f, 0.6f, 0.85f);
            sprintText = Label("Sprint", transform, new Vector2(0.5f, 0f), new Vector2(0f, 74f), new Vector2(300f, 26f), 18, TextAnchor.LowerCenter);
            sprintText.color = new Color(1f, 0.74f, 0.26f, 0.9f);
            sprintText.fontStyle = FontStyle.Bold;

            statsText = Label("Stats", transform, new Vector2(1f, 1f), new Vector2(-40f, -30f), new Vector2(600f, 40f), 22, TextAnchor.UpperRight);
            statsText.color = new Color(1f, 1f, 1f, 0.85f);
            speedText = Label("Speed", transform, new Vector2(0.5f, 0f), new Vector2(0f, 40f), new Vector2(300f, 30f), 22, TextAnchor.LowerCenter);
            speedText.color = new Color(1f, 1f, 1f, 0.55f);

            hintText = Label("Hints", transform, new Vector2(0f, 0f), new Vector2(40f, 40f), new Vector2(1000f, 220f), 18, TextAnchor.LowerLeft);
            hintText.color = new Color(1f, 1f, 1f, 0.6f);
            RefreshKeys();
            GameSettings.Changed += RefreshKeys;
        }

        void OnDestroy()
        {
            GameSettings.Changed -= RefreshKeys;
            if (Instance == this) Instance = null;
        }

        static string K(BindAction a) => GameSettings.KeyName(a);

        /// <summary>Control hints and crosshair color follow the settings.</summary>
        void RefreshKeys()
        {
            if (hintText)
                hintText.text =
                    $"{K(BindAction.MoveForward)}{K(BindAction.MoveLeft)}{K(BindAction.MoveBack)}{K(BindAction.MoveRight)} — движение    {K(BindAction.Sprint)} — бег ({(GameSettings.SprintToggle ? "нажатие" : "зажатие")})    {K(BindAction.Jump)} — прыжок / перелезть (держать — распрыжка)\n" +
                    $"{K(BindAction.CrouchToggle)} — присед    {K(BindAction.CrouchHold)} — присед ({(GameSettings.CrouchHoldIsToggle ? "нажатие" : "зажатие")})    {K(BindAction.Prone)} — лечь    {K(BindAction.LeanLeft)} / {K(BindAction.LeanRight)} — выглянуть    {K(BindAction.CrouchToggle)} на бегу — подкат\n" +
                    $"{K(BindAction.Fire)} — огонь    {K(BindAction.Aim)} — прицел    {K(BindAction.Reload)} — перезарядка    {K(BindAction.FireMode)} — режим огня    {K(BindAction.Primary)} / {K(BindAction.Secondary)} / колесо — оружие\n" +
                    $"{K(BindAction.Grenade)} (держать) — граната    {K(BindAction.Melee)} — нож / добивание со спины    {K(BindAction.Pickup)} — подобрать\n" +
                    $"{K(BindAction.Ability)} — способность (светящиеся предметы на карте)    {K(BindAction.Pickup)} — заменить способность    PgUp/PgDn — сменить (тест)\n" +
                    $"Esc — пауза, снаряжение и настройки    {K(BindAction.Hints)} — скрыть подсказки";
            var c = GameSettings.CrosshairColors[Mathf.Clamp(GameSettings.CrosshairColor, 0, GameSettings.CrosshairColors.Length - 1)];
            foreach (var img in crossImgs) if (img) img.color = c;
        }

        static Texture2D PlateTexture()
        {
            const int W = 64, H = 32;
            var t = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float u = x / (W - 1f), v = y / (H - 1f);
                    float a = Mathf.Clamp01(u * 1.6f - 0.25f) * Mathf.Clamp01((1f - v) * 1.4f) * 0.55f;
                    t.SetPixel(x, y, new Color(0.02f, 0.025f, 0.03f, a));
                }
            t.Apply();
            return t;
        }

        static Texture2D ScopeTexture(int size)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[size * size];
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f - half) / half, dy = (y + 0.5f - half) / half;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01((r - 0.9f) / 0.08f);                // black outside the lens
                    float vign = Mathf.Clamp01((r - 0.65f) / 0.3f) * 0.55f;      // dark lens edge
                    float line = 0f;
                    float ax = Mathf.Abs(dx), ay = Mathf.Abs(dy);
                    float thick = ax > 0.55f || ay > 0.55f ? 0.012f : 0.003f;   // thick posts outside, thin crosshair inside
                    if ((ax < thick && ay > 0.025f) || (ay < thick && ax > 0.025f)) line = 1f;
                    for (int m = 1; m <= 4; m++)                                  // mil dots
                    {
                        float o = m * 0.11f;
                        if ((Mathf.Abs(ay - o) < 0.004f && ax < 0.025f) || (Mathf.Abs(ax - o) < 0.004f && ay < 0.025f)) line = 1f;
                    }
                    float alpha = Mathf.Max(a, Mathf.Max(line * 0.95f, vign));
                    byte rr = 0;
                    if (r < 0.006f) { rr = 230; alpha = 1f; }                    // red center dot
                    px[y * size + x] = new Color32(rr, 0, 0, (byte)(alpha * 255));
                }
            t.SetPixels32(px); t.Apply();
            return t;
        }

        public void SetScope(bool on) => scopeOn = on;

        public void ShowHitmarker(bool kill, bool crit)
        {
            hitDuration = kill ? 0.45f : crit ? 0.32f : 0.24f;
            hitTimer = hitDuration;
            hitColor = kill ? new Color(1f, 0.25f, 0.2f) : crit ? new Color(1f, 0.82f, 0.25f) : Color.white;
            hitScale = kill ? 1.7f : crit ? 1.4f : 1.2f;
        }

        public void RegisterShot() => shots++;
        public void RegisterHit(bool killed) { hits++; if (killed) kills++; }

        void Update()
        {
            float dt = Time.deltaTime;
            if (!loadout) loadout = FindAnyObjectByType<Loadout>();
            if (!melee) melee = FindAnyObjectByType<MeleeController>();
            if (!motor) motor = FindAnyObjectByType<PlayerMotor>();
            if (!cam) cam = Camera.main;
            var weapon = loadout ? loadout.Active : FindAnyObjectByType<WeaponController>();
            if (!weapon || !cam) return;

            if (GameInput.HintsPressed) hintText.enabled = !hintText.enabled;

            scopeAlpha = Mathf.MoveTowards(scopeAlpha, scopeOn ? 1f : 0f, dt * 12f);
            scopeGroup.alpha = scopeAlpha;

            // dynamic crosshair: gap matches actual bullet spread on screen
            float sf = Mathf.Max(0.01f, canvas.scaleFactor);
            float px = Mathf.Tan(weapon.CurrentSpread * Mathf.Deg2Rad) / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * (Screen.height * 0.5f) / sf;
            if (weapon.pellets > 1) px += Mathf.Tan(weapon.pelletSpread * Mathf.Deg2Rad) / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * (Screen.height * 0.5f) / sf;
            gap = Anim.Damp(gap, 5f + px, 22f, dt);
            bool busy = weapon.IsBusy || (motor && ((motor.IsSprinting && motor.Speed01 > 0.45f) || motor.IsVaulting)) || (melee && melee.Busy) || (loadout && loadout.Throwing);
            float targetAlpha = (1f - weapon.AimBlend) * (busy ? 0.2f : 1f);
            crossAlpha = Anim.Damp(crossAlpha, targetAlpha, 18f, dt);
            crossGroup.alpha = crossAlpha;
            float o = gap + 5.5f;
            cross[0].anchoredPosition = new Vector2(0f, o);
            cross[1].anchoredPosition = new Vector2(0f, -o);
            cross[2].anchoredPosition = new Vector2(-o, 0f);
            cross[3].anchoredPosition = new Vector2(o, 0f);

            // hitmarker
            if (hitTimer > 0f) hitTimer -= dt;
            float t = Mathf.Clamp01(hitTimer / hitDuration);
            float s = Mathf.Lerp(1f, hitScale, t * t);
            float d = (9f + 7f * (1f - t)) * s;
            for (int i = 0; i < 4; i++)
            {
                float a = (45f + 90f * i) * Mathf.Deg2Rad;
                hit[i].anchoredPosition = new Vector2(-Mathf.Sin(a), Mathf.Cos(a)) * d;
                hit[i].localScale = new Vector3(1f, s, 1f);
                var c = hitColor; c.a = t > 0f ? Mathf.Clamp01(t * 1.6f) : 0f;
                hitImg[i].color = c;
            }

            // prompt
            string prompt = null;
            if (melee && melee.Candidate != null) prompt = "[" + K(BindAction.Melee) + "]  Добить";
            else if (loadout && loadout.LookedPickup)
                prompt = loadout.LookedPickup.kind == WeaponPickup.Kind.Ammo ? "[" + K(BindAction.Pickup) + "]  Пополнить боезапас" : "[" + K(BindAction.Pickup) + "]  Взять " + loadout.LookedPickup.weaponName;
            if (prompt != null) promptText.text = prompt;
            promptAlpha = Mathf.MoveTowards(promptAlpha, prompt != null ? 1f : 0f, dt * 10f);
            promptGroup.alpha = promptAlpha;

            // ammo & weapon
            if (weapon != lastWeapon) { lastWeapon = weapon; shownAmmo = -1; ammoPunch = 1f; }
            if (weapon.Ammo != shownAmmo)
            {
                if (weapon.Ammo < shownAmmo) ammoPunch = 1f;
                shownAmmo = weapon.Ammo;
                ammoText.text = shownAmmo.ToString();
            }
            ammoPunch = Mathf.MoveTowards(ammoPunch, 0f, dt * 8f);
            ammoText.rectTransform.localScale = Vector3.one * (1f + 0.12f * ammoPunch);
            bool low = weapon.Ammo <= Mathf.Max(1, weapon.magSize / 4);
            ammoText.color = low ? new Color(1f, 0.35f, 0.3f) : Color.white;
            reserveText.text = "/ " + weapon.Reserve;
            weaponText.text = weapon.weaponName;
            modeText.text = weapon.IsReloading ? "ПЕРЕЗАРЯДКА" : weapon.pumpAction ? "ПОМПА" : (weapon.FullAuto ? "АВТО" : "ОДИНОЧНЫЙ");
            if (loadout)
            {
                string p = loadout.primaryName, sc = loadout.secondaryName;
                bool onP = weapon.weaponName == p;
                slotsText.text = (onP ? "<b>1  " + p + "</b>" : "1  " + p) + "      " + (!onP ? "<b>2  " + sc + "</b>" : "2  " + sc);
                grenadeText.text = "Гранаты  " + new string('●', loadout.grenades) + new string('○', Mathf.Max(0, loadout.maxGrenades - loadout.grenades));
            }

            if (motor) speedText.text = motor.HorizontalSpeed > 0.3f ? $"{motor.HorizontalSpeed:0.0} м/с" : "";
            sprintText.text = GameSettings.SprintToggle && GameInput.SprintLatched ? "БЕГ" : "";
            fpsAcc += Time.unscaledDeltaTime; fpsFrames++;
            if (fpsAcc >= 0.5f) { fpsText.text = GameSettings.ShowFps ? Mathf.RoundToInt(fpsFrames / fpsAcc) + " FPS" : ""; fpsAcc = 0f; fpsFrames = 0; }
            float acc = shots > 0 ? hits * 100f / shots : 0f;
            statsText.text = $"Выстрелы {shots}   Попадания {hits} ({acc:0}%)   Сбито {kills}";
        }
    }
}
