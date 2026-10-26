using UnityEngine;

public sealed class GameControlsTutorial : MonoBehaviour
{
    private static bool shownThisPlaySession;
    public static void ResetForNewVisit() { shownThisPlaySession = false; }
    private readonly string[] titles = { "MOVE", "DOUBLE JUMP", "DASH / SPRINT", "ATTACK" };
    private readonly string[] descriptions =
    {
        "Press A and D to move left and right.",
        "Press SPACE to jump. Press SPACE again while airborne to perform a double jump.",
        "Press SHIFT while airborne to dash forward. Hold SHIFT while moving on a platform to run faster.",
        "Press the LEFT MOUSE BUTTON to attack enemies. The Princess fires magic; the Fighter uses melee attacks."
    };
    private int page;
    private bool visible = true;
    private GUIStyle titleStyle, bodyStyle, buttonStyle, pageStyle;
    private GUIStyle keyStyle, cardHeadingStyle, cardLabelStyle, cardHintStyle;
    private Sprite entranceBackground, entrancePlatform;
    private bool entranceArtFound;
    private GameObject demoActor;
    private Animator demoAnimator;
    private SpriteRenderer demoSprite;
    private int demoPage = -1;
    private string demoState;
    private float demoStarted;
    private float demoUpdated;
    private Camera demoCamera;
    private RenderTexture demoTexture;
    private SpriteRenderer sourcePlayerSprite;
    private Vector3 demoOrigin;
    private float demoLastRender = -1f;
    private float demoDashSpeed, demoDashDuration, demoJumpSpeed, demoGravity;
    private SpriteRenderer demoWind;
    private SpriteRenderer demoBackground;
    private SpriteRenderer demoPlatform;
    private SpriteRenderer[] demoObstacles;
    private bool[] demoObstacleVisibility;
    private bool showDemoWind;
    private float entranceStarted;
    private bool entering;
    private const float EntranceDuration = 4f;

    private void Awake()
    {
        if (SideGameBDifficulty.HasSelected)
        {
            shownThisPlaySession = true;
            visible = false;
            gameObject.SetActive(false);
            return;
        }
        if (shownThisPlaySession)
        {
            visible = false;
            gameObject.SetActive(false);
            return;
        }
        shownThisPlaySession = true;
        entranceStarted = Time.realtimeSinceStartup;
        entering = true;
        Time.timeScale = 0f;
    }

    public bool TryInterfaceBack()
    {
        if(!visible||page<=0)return false;
        page--;return true;
    }
    private void OnGUI()
    {
        if (!visible) return;
        EnsureStyles();
        if (entering)
        {
            float elapsed = Time.realtimeSinceStartup - entranceStarted;
            if (elapsed < EntranceDuration)
            {
                DrawEntrance(elapsed);
                return;
            }
            entering = false;
        }
        Color old = GUI.color;
        GUI.color = new Color(0.025f, 0.03f, 0.045f, 0.97f);
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = old;

        float scale = Mathf.Min(1f, Screen.width / 1180f, Screen.height / 700f);
        UpdateStyleSizes(scale);
        float width = 1080f * scale, height = 610f * scale;
        Rect panel = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);
        GUI.color = new Color(0.10f, 0.11f, 0.15f, 1f);
        GUI.Box(panel, GUIContent.none);
        GUI.color = old;

        Rect visual = new Rect(panel.x + 38f * scale, panel.y + 55f * scale, 620f * scale, 430f * scale);
        GUI.color = new Color(0.055f, 0.065f, 0.09f, 1f);
        GUI.Box(visual, GUIContent.none);
        GUI.color = old;
        DrawControlCard(visual, scale);

        float textX = visual.xMax + 42f * scale;
        GUI.Label(new Rect(textX, panel.y + 75f * scale, panel.xMax - textX - 35f * scale, 70f * scale), titles[page], titleStyle);
        GUI.Label(new Rect(textX, panel.y + 170f * scale, panel.xMax - textX - 42f * scale, 230f * scale), descriptions[page], bodyStyle);
        GUI.Label(new Rect(panel.x + 42f * scale, panel.y + 530f * scale, 100f * scale, 40f * scale), $"{page + 1} / 4", pageStyle);
        string label = page == 3 ? "Start Game" : "Next";
        buttonStyle.fontSize = Mathf.Max(14, Mathf.RoundToInt((page == 3 ? 19f : 22f) * scale));
        if (page > 0 && GUI.Button(new Rect(panel.xMax - 435f * scale, panel.y + 515f * scale, 170f * scale, 62f * scale), "Previous [RMB]", buttonStyle)) page--;
        if (GUI.Button(new Rect(panel.xMax - 235f * scale, panel.y + 515f * scale, 190f * scale, 62f * scale), label, buttonStyle))
        {
            if (page < 3) page++;
            else CloseTutorial();
        }
    }

    private void DrawEntrance(float elapsed)
    {
        Color oldColor = GUI.color;
        Matrix4x4 oldMatrix = GUI.matrix;
        float reveal = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / .8f));
        float fade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((elapsed - 3.2f) / .8f));
        float alpha = reveal * fade;
        float scale = Mathf.Min(Screen.width / 1000f, Screen.height / 700f);
        Vector2 center = new Vector2(Screen.width * .5f, Screen.height * .40f);
        float rise = (1f - reveal) * 12f * scale;
        if (!entranceArtFound)
        {
            entranceArtFound = true;
            foreach (var renderer in FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None))
            {
                if (!renderer.sprite) continue;
                string name = renderer.sprite.texture.name;
                if (name.Contains("FantasyValleyBackground")) entranceBackground = renderer.sprite;
                if (name.Contains("ForestGrassPlatformA")) entrancePlatform = renderer.sprite;
            }
        }
        GUI.color = new Color(.055f, .10f, .13f);
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
        if (entranceBackground)
        {
            GUI.color = new Color(.40f, .50f, .48f);
            DrawEntranceSprite(new Rect(0, 0, Screen.width, Screen.height), entranceBackground);
        }
        GUI.color = new Color(.025f, .065f, .075f, .55f);
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
        // Stepped stone arch, moss and square motes match the level's pixel artwork.
        for (int y = -9; y <= 9; y++)
        for (int x = -7; x <= 7; x++)
        {
            float distance = x * x / 49f + y * y / 81f;
            if (distance > 1.12f) continue;
            float cell = 16f * scale;
            Rect tile = new Rect(Mathf.Round(center.x + x * cell), Mathf.Round(center.y + y * cell), cell + 1, cell + 1);
            bool stone = distance > .76f;
            float variation = Mathf.Repeat(x * .37f + y * .23f, 1f);
            Color color = stone ? Color.Lerp(new Color(.22f, .28f, .29f), new Color(.43f, .48f, .43f), variation)
                : Color.Lerp(new Color(.035f, .16f, .20f), new Color(.16f, .60f, .59f), .35f + .3f * Mathf.Sin(elapsed * 2f + y * .5f + x * .3f));
            if (stone && y < -5 && variation > .5f) color = new Color(.30f, .43f, .20f);
            GUI.color = new Color(color.r, color.g, color.b, alpha);
            GUI.DrawTexture(tile, Texture2D.whiteTexture);
            if (stone && (x + y) % 3 == 0)
            {
                GUI.color = new Color(.65f, .90f, .68f, alpha * (.6f + .3f * Mathf.Sin(elapsed * 3f)));
                GUI.DrawTexture(new Rect(tile.x + cell * .35f, tile.y + cell * .25f, cell * .25f, cell * .5f), Texture2D.whiteTexture);
            }
        }
        for (int i = 0; i < 24; i++)
        {
            float phase = Mathf.Repeat(elapsed * .28f + i * .137f, 1f);
            float angle = i * 2.39996f;
            float radius = Mathf.Lerp(240f, 10f, phase) * scale;
            float pixel = (i % 3 + 2) * scale;
            Vector2 point = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
            GUI.color = new Color(.85f, .89f, .55f, Mathf.Sin(phase * Mathf.PI) * alpha);
            GUI.DrawTexture(new Rect(Mathf.Round(point.x), Mathf.Round(point.y), pixel, pixel), Texture2D.whiteTexture);
        }
        if (entrancePlatform)
        {
            GUI.color = new Color(1, 1, 1, alpha);
            DrawEntranceSprite(new Rect(center.x - 170f * scale, center.y + 145f * scale, 340f * scale, 75f * scale), entrancePlatform);
        }
        var heading = new GUIStyle(GUI.skin.label)
        {
            fontSize = Mathf.Max(20, Mathf.RoundToInt(32f * scale)),
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };
        heading.normal.textColor = new Color(.96f, .88f, .65f);
        var subtitle = new GUIStyle(heading) { fontSize = Mathf.Max(11, Mathf.RoundToInt(16f * scale)), fontStyle = FontStyle.Normal };
        subtitle.normal.textColor = new Color(.65f, .83f, .84f);
        GUI.color = new Color(1f, 1f, 1f, alpha);
        GUI.Label(new Rect(0, center.y + 230f * scale + rise, Screen.width, 70f * scale), "ENTERING ANOTHER DOMAIN...", heading);
        GUI.Label(new Rect(0, center.y + 292f * scale + rise, Screen.width, 32f * scale), "PASSING THROUGH THE PORTAL", subtitle);
        GUI.color = new Color(.85f, .71f, .4f, alpha);
        float width = 240f * scale * reveal;
        GUI.DrawTexture(new Rect(center.x - width * .5f, center.y + 338f * scale, width, 2f * scale), Texture2D.whiteTexture);
        GUI.color = oldColor;
        GUI.matrix = oldMatrix;
    }

    private static void DrawEntranceSprite(Rect destination, Sprite sprite)
    {
        Rect r = sprite.textureRect;
        GUI.DrawTextureWithTexCoords(destination, sprite.texture, new Rect(r.x / sprite.texture.width,
            r.y / sprite.texture.height, r.width / sprite.texture.width, r.height / sprite.texture.height));
    }

    private void DrawControlCard(Rect area, float scale)
    {
        EnsureDemoActor();
        GUI.BeginGroup(area);
        GUI.Label(CardRect(20, 12, 580, 45, scale), "LIVE GAMEPLAY PREVIEW", cardHeadingStyle);
        float now = Time.realtimeSinceStartup;
        if (demoPage != page)
        {
            demoPage = page;
            demoStarted = now;
            demoState = null;
        }
        float t = Mathf.Repeat(now - demoStarted, 4f);
        float x = 310f, height = 0f;
        string state = "idle";
        string keys = "";
        showDemoWind = false;
        if (demoSprite && page != 0) demoSprite.flipX = false;
        switch (page)
        {
            case 0:
                x = 160 + Mathf.PingPong(t * 300, 300);
                state = "run";
                bool movingLeft = Mathf.Repeat(t, 2f) >= 1f;
                keys = movingLeft ? "A  /  MOVE LEFT" : "D  /  MOVE RIGHT";
                if (demoSprite) demoSprite.flipX = movingLeft;
                break;
            case 1:
                // Two successive impulses visibly distinguish the second jump.
                float jumpT = t - .4f;
                if (jumpT >= 0 && jumpT < .65f)
                    height = 180f * jumpT - 95f * jumpT * jumpT;
                else if (jumpT >= .65f)
                {
                    float second = jumpT - .65f;
                    height = Mathf.Max(0, 77f + 180f * second - 145f * second * second);
                }
                state = height > 0 ? (jumpT >= .65f ? "double jump" : "jump") : "idle";
                keys = jumpT >= .65f && height > 0 ? "SPACE AGAIN  /  DOUBLE JUMP" : "SPACE  /  JUMP";
                break;
            case 2:
                // Match gameplay: an actual jump arc, a brief horizontal dash with
                // gravity suspended, then gravity resumes from zero vertical speed.
                float jumpStart = .4f, dashStart = .8f;
                float dashEnd = dashStart + demoDashDuration;
                float riseTime = dashStart - jumpStart;
                float dashHeight = Mathf.Max(.2f, demoJumpSpeed * riseTime - .5f * demoGravity * riseTime * riseTime);
                float startX = -1.1f;
                float endX = startX + demoDashSpeed * demoDashDuration;
                float worldX = startX;
                keys = "SPACE  /  JUMP";
                if (t >= jumpStart && t < dashStart)
                {
                    float flight = t - jumpStart;
                    height = Mathf.Max(0, demoJumpSpeed * flight - .5f * demoGravity * flight * flight) * 80f;
                    state = "jump";
                }
                else if (t >= dashStart && t < dashEnd)
                {
                    worldX += (t - dashStart) * demoDashSpeed;
                    height = dashHeight * 80f;
                    state = "dash";
                    showDemoWind = true;
                    keys = "SHIFT  /  AIR DASH";
                }
                else if (t >= dashEnd)
                {
                    worldX = endX;
                    float fallTime = t - dashEnd;
                    height = Mathf.Max(0, dashHeight - .5f * demoGravity * fallTime * fallTime) * 80f;
                    state = height > 0 ? "jump" : "idle";
                    keys = height > 0 ? "RELEASE SHIFT  /  LAND" : "JUMP → SHIFT → LAND";
                    if (t >= 2.6f && t < 3.6f)
                    {
                        worldX = Mathf.Lerp(endX, startX, t - 2.6f);
                        state = "run";
                        demoSprite.flipX = true;
                    }
                    else if (t >= 3.6f) worldX = startX;
                }
                x = 310f + worldX * 100f;
                break;
            default:
                state = t < 1.2f ? "skill3" : "idle";
                keys = "LEFT MOUSE BUTTON  /  ATTACK";
                break;
        }
        if (demoAnimator)
        {
            if (demoState != state)
            {
                if (!demoAnimator.HasState(0, Animator.StringToHash(state))) state = "run";
                demoAnimator.Play(state, 0, 0);
                demoState = state;
            }
            // Reuse the actual level geometry and background with a small camera target.
            if (Event.current.type == EventType.Repaint)
            {
                demoAnimator.Update(Mathf.Clamp(now - demoUpdated, 0, .5f));
                demoUpdated = now;
                demoActor.transform.position = demoOrigin + new Vector3((x - 310f) / 100f, height / 80f, 0);
            }
            GUI.DrawTexture(CardRect(15, 60, 590, 285, scale), demoTexture, ScaleMode.ScaleToFit, false);
        }
        GUI.Label(CardRect(15, 360, 590, 48, scale), keys, cardLabelStyle);
        GUI.EndGroup();
    }

    private void LateUpdate()
    {
        // Render outside OnGUI: IMGUI runs inside URP's render submission and
        // recursively rendering there corrupts its per-camera frame data.
        if (!visible || entering || !demoCamera || !sourcePlayerSprite) return;
        float now = Time.realtimeSinceStartup;
        if (now - demoLastRender < 1f / 60f) return;
        bool previousHidden = sourcePlayerSprite.forceRenderingOff;
        try
        {
            sourcePlayerSprite.forceRenderingOff = true;
            for (int i = 0; i < demoObstacles.Length; i++)
            {
                if (!demoObstacles[i]) continue;
                demoObstacleVisibility[i] = demoObstacles[i].forceRenderingOff;
                demoObstacles[i].forceRenderingOff = true;
            }
            demoSprite.forceRenderingOff = false;
            if (demoWind) demoWind.forceRenderingOff = !showDemoWind;
            // A closer camera follows horizontally so the enlarged hero stays in frame.
            demoCamera.transform.position = new Vector3(demoOrigin.x,
                demoOrigin.y + .8f + (demoActor.transform.position.y - demoOrigin.y) * .35f,
                demoCamera.transform.position.z);
            if (demoBackground) demoBackground.forceRenderingOff = false;
            demoCamera.Render();
        }
        finally
        {
            sourcePlayerSprite.forceRenderingOff = previousHidden;
            for (int i = 0; i < demoObstacles.Length; i++)
                if (demoObstacles[i]) demoObstacles[i].forceRenderingOff = demoObstacleVisibility[i];
            demoSprite.forceRenderingOff = true;
            if (demoWind) demoWind.forceRenderingOff = true;
            if (demoBackground) demoBackground.forceRenderingOff = true;
        }
        demoLastRender = now;
    }

    private void EnsureDemoActor()
    {
        if (demoActor) return;
        var player = FindFirstObjectByType<PlayerController2D>();
        if (!player) return;
        var sourceAnimator = player.GetComponent<Animator>();
        var sourceSprite = player.GetComponent<SpriteRenderer>();
        if (!sourceAnimator || !sourceSprite || !Camera.main) return;
        sourcePlayerSprite = sourceSprite;
        demoOrigin = player.transform.position;
        demoDashSpeed = player.TutorialDashSpeed;
        demoDashDuration = player.TutorialDashDuration;
        demoJumpSpeed = player.TutorialJumpSpeed;
        demoGravity = Mathf.Max(1f, Mathf.Abs(Physics2D.gravity.y * player.GetComponent<Rigidbody2D>().gravityScale));
        var sceneSprites = FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None);
        // Select by the actual green artwork, not an object name or a stale collider.
        foreach (var renderer in sceneSprites)
        {
            if (renderer.gameObject.scene != player.gameObject.scene || !renderer.sprite) continue;
            if (renderer.sprite.texture.name != "ForestGrassPlatformA") continue;
            demoPlatform = renderer;
            break;
        }
        if (demoPlatform)
        {
            Bounds bounds = demoPlatform.bounds;
            // The grass walking line is below the decorative leaves at the top
            // of this artwork (about one quarter of the image height).
            float grassTop = bounds.max.y - bounds.size.y * .25f;
            float footOffset = player.transform.position.y - player.GetComponent<BoxCollider2D>().bounds.min.y;
            demoOrigin = new Vector3(bounds.center.x, grassTop + footOffset + .05f, player.transform.position.z);
        }
        var obstacleList = new System.Collections.Generic.List<SpriteRenderer>();
        foreach (var renderer in sceneSprites)
        {
            if (renderer.gameObject.scene != player.gameObject.scene || renderer == demoPlatform || renderer == sourceSprite) continue;
            obstacleList.Add(renderer);
        }
        demoObstacles = obstacleList.ToArray();
        demoObstacleVisibility = new bool[demoObstacles.Length];
        // Copy only visual components. No physics, damage, input or world objects.
        demoActor = new GameObject("Tutorial Visual Preview");
        demoActor.hideFlags = HideFlags.HideAndDontSave;
        demoActor.transform.position = demoOrigin;
        demoActor.transform.localScale = player.transform.lossyScale;
        demoSprite = demoActor.AddComponent<SpriteRenderer>();
        demoSprite.sprite = sourceSprite.sprite;
        demoSprite.sortingLayerID = sourceSprite.sortingLayerID;
        demoSprite.sortingOrder = sourceSprite.sortingOrder;
        demoSprite.sharedMaterial = sourceSprite.sharedMaterial;
        demoSprite.color = sourceSprite.color;
        demoTexture = new RenderTexture(640, 320, 16, RenderTextureFormat.ARGB32)
        { name = "Tutorial Scene Preview", filterMode = FilterMode.Point };
        demoTexture.Create();
        var cameraObject = new GameObject("Tutorial Scene Camera");
        cameraObject.hideFlags = HideFlags.HideAndDontSave;
        demoCamera = cameraObject.AddComponent<Camera>();
        demoCamera.CopyFrom(Camera.main);
        demoCamera.enabled = false;
        demoCamera.orthographic = true;
        demoCamera.orthographicSize = 2f;
        demoCamera.aspect = 2f;
        demoCamera.targetTexture = demoTexture;
        demoCamera.transform.rotation = Camera.main.transform.rotation;
        demoCamera.transform.position = new Vector3(demoOrigin.x, demoOrigin.y + 1f, Camera.main.transform.position.z);
        // The real forest backdrop is attached to the gameplay camera, so it
        // stays at the old view when this preview looks at another platform.
        // Share its sprite/material with a backdrop attached to the preview camera.
        foreach (var background in FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None))
        {
            if (!background.sprite || background.gameObject.scene != player.gameObject.scene) continue;
            if (background.name != "Fantasy Valley Background") continue;
            var backdrop = new GameObject("Tutorial Forest Background");
            backdrop.transform.SetParent(demoCamera.transform, false);
            backdrop.transform.localPosition = Camera.main.transform.InverseTransformPoint(background.transform.position);
            backdrop.transform.localScale = background.transform.lossyScale;
            demoBackground = backdrop.AddComponent<SpriteRenderer>();
            demoBackground.sprite = background.sprite;
            demoBackground.sharedMaterial = background.sharedMaterial;
            demoBackground.color = background.color;
            demoBackground.sortingLayerID = background.sortingLayerID;
            demoBackground.sortingOrder = background.sortingOrder;
            demoBackground.forceRenderingOff = true;
            break;
        }
        demoSprite.forceRenderingOff = true;
        var windSprite = Resources.Load<Sprite>("DashWind");
        if (windSprite)
        {
            var wind = new GameObject("Tutorial Dash Wind");
            wind.transform.SetParent(demoActor.transform, false);
            wind.transform.localPosition = new Vector3(-.58f, -.02f, 0);
            wind.transform.localScale = Vector3.one * .45f;
            demoWind = wind.AddComponent<SpriteRenderer>();
            demoWind.sprite = windSprite;
            demoWind.flipX = true;
            demoWind.sortingOrder = 9;
            demoWind.forceRenderingOff = true;
        }
        demoAnimator = demoActor.AddComponent<Animator>();
        demoAnimator.runtimeAnimatorController = sourceAnimator.runtimeAnimatorController;
        demoAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        demoAnimator.fireEvents = false;
        demoAnimator.enabled = false;
        demoUpdated = Time.realtimeSinceStartup;
    }

    private void ReleaseDemo()
    {
        if (demoActor) Destroy(demoActor);
        if (demoCamera) Destroy(demoCamera.gameObject);
        if (demoTexture) { demoTexture.Release(); Destroy(demoTexture); }
        demoActor = null;
        demoCamera = null;
        demoTexture = null;
    }

    private void OnDestroy() { ReleaseDemo(); }

    private void DrawKeycap(Rect area, string label, float scale)
    {
        float border = 2f * scale;
        float depth = 7f * scale;
        DrawSolidRect(new Rect(area.x, area.y + depth, area.width, area.height), new Color(0.02f, 0.025f, 0.04f));
        DrawSolidRect(area, new Color(0.62f, 0.67f, 0.78f));
        DrawSolidRect(new Rect(area.x + border, area.y + border, area.width - border * 2f, area.height - border * 2f),
            new Color(0.17f, 0.20f, 0.28f));
        GUI.Label(area, label, keyStyle);
    }

    private static void DrawSolidRect(Rect area, Color color)
    {
        Color old = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(area, Texture2D.whiteTexture);
        GUI.color = old;
    }

    private static Rect CardRect(float x, float y, float width, float height, float scale)
    {
        return new Rect(x * scale, y * scale, width * scale, height * scale);
    }

    private void CloseTutorial()
    {
        visible = false;
        Time.timeScale = 1f;
        SideGameBDifficulty.Show();
        gameObject.SetActive(false);
    }

    private void OnDisable()
    {
        ReleaseDemo();
        if (visible) Time.timeScale = 1f;
    }

    private void EnsureStyles()
    {
        if (titleStyle != null) return;
        titleStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft, fontSize = 32, fontStyle = FontStyle.Bold, wordWrap = false };
        titleStyle.normal.textColor = new Color(1f, 0.82f, 0.35f);
        bodyStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperLeft, fontSize = 22, wordWrap = true };
        bodyStyle.normal.textColor = new Color(0.94f, 0.95f, 1f);
        buttonStyle = new GUIStyle(GUI.skin.button) { alignment = TextAnchor.MiddleCenter, fontSize = 24, fontStyle = FontStyle.Bold };
        pageStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft, fontSize = 18, fontStyle = FontStyle.Bold };
        pageStyle.normal.textColor = new Color(0.72f, 0.75f, 0.82f);
        keyStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 32, fontStyle = FontStyle.Bold };
        keyStyle.normal.textColor = Color.white;
        cardHeadingStyle = new GUIStyle(keyStyle) { fontSize = 24 };
        cardHeadingStyle.normal.textColor = new Color(1f, 0.82f, 0.35f);
        cardLabelStyle = new GUIStyle(keyStyle) { fontSize = 21 };
        cardHintStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 18, wordWrap = true };
        cardHintStyle.normal.textColor = new Color(0.72f, 0.75f, 0.82f);
        foreach (var style in new[] { titleStyle, bodyStyle, pageStyle, keyStyle, cardHeadingStyle, cardLabelStyle, cardHintStyle })
            WayfarerUITheme.KeepLabelColor(style);
    }

    private void UpdateStyleSizes(float scale)
    {
        titleStyle.fontSize = Mathf.Max(16, Mathf.RoundToInt(32f * scale));
        bodyStyle.fontSize = Mathf.Max(12, Mathf.RoundToInt(22f * scale));
        pageStyle.fontSize = Mathf.Max(11, Mathf.RoundToInt(18f * scale));
        keyStyle.fontSize = Mathf.Max(12, Mathf.RoundToInt(28f * scale));
        cardHeadingStyle.fontSize = Mathf.Max(12, Mathf.RoundToInt(24f * scale));
        cardLabelStyle.fontSize = Mathf.Max(11, Mathf.RoundToInt(21f * scale));
        cardHintStyle.fontSize = Mathf.Max(10, Mathf.RoundToInt(18f * scale));
    }
}
