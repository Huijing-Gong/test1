using UnityEngine;
using UnityEngine.UI;

namespace SamProduction.BossArena
{
    // This canvas is rendered by the combat camera, including camera-to-texture captures.
    public sealed class ArenaHud : MonoBehaviour
    {
        private static readonly Color Ink = new Color(23f/255,57f/255,50f/255,.90f);
        private static readonly Color Dim = new Color(.72f,.73f,.64f,1);
        private static readonly Color White = new Color32(245,233,198,255);
        private bool Eclipse => arena != null && arena.boss != null && arena.boss.v13Motor != null;
        private string BossTitle => Eclipse ? "ECLIPSE WARDEN" : "FURNACE SENTINEL";
        private Color Orange => new Color32(186,155,87,255);
        private static readonly Color Cyan = new Color(.52f,.77f,.64f,1);
        [SerializeField] private ArenaSession arena;
        [SerializeField] private Camera combatCamera;
        [SerializeField] private RectTransform uiRoot, bossFill, bossTrail, playerFill, playerTrail, skillFill, lockMarker;
        [SerializeField] private Image skillLine;
        [SerializeField] private Text healthLabel, actionLabel, stageLabel, lockLabel, cursorLabel;
        [SerializeField] private RectTransform messagePanel;
        [SerializeField] private Text messageTitle, messageDetail, messagePrompt;
        private Font font;
        private float shownBoss = 1, shownPlayer = 1;
        private ArenaRoundState lastState;
        private bool stateInitialized;
        private RectTransform counterPanel, counterFill;
        private Text counterLabel,bossValue,returnLabel;
        private RectTransform combatCues;
        private WayfarerHealthStrip healthStrip;
        bool hosted;
        public void SetHosted(bool value){hosted=value;if(returnLabel)returnLabel.gameObject.SetActive(value);}
        Material playerArtworkMaterial,bossArtworkMaterial;
        float playerPrevious=1,playerWait,playerFlash;
        public bool Ready=>uiRoot!=null;
        public void SetPresentationVisible(bool visible){if(uiRoot)uiRoot.gameObject.SetActive(visible);}
        void ReleaseMaterials()
        {
            foreach(var m in new[]{playerArtworkMaterial,bossArtworkMaterial})if(m)
            {if(Application.isPlaying)Destroy(m);else DestroyImmediate(m);}
            playerArtworkMaterial=bossArtworkMaterial=null;
        }
        void OnDestroy(){ReleaseMaterials();}
        bool Artwork(string label,RectTransform parent,Vector2 position,Vector2 size,string texturePath,string shaderPath,out Material material)
        {
            material=null;var texture=Resources.Load<Texture2D>(texturePath);var shader=Resources.Load<Shader>(shaderPath);
            if(!texture||!shader||!shader.isSupported)return false;
            material=new Material(shader){name=label,hideFlags=HideFlags.DontSave};
            var image=Rect(label,parent,new Vector2(.5f,1),position,size).gameObject.AddComponent<RawImage>();
            image.texture=texture;image.material=material;image.raycastTarget=false;return true;
        }

        public void Build(ArenaSession session, Camera camera)
        {
            if (session == null || camera == null)
                throw new System.ArgumentNullException(session == null ? nameof(session) : nameof(camera));
            arena = session;
            combatCamera = camera;
            if (uiRoot != null)
            {
                uiRoot.gameObject.SetActive(false);
                if (Application.isPlaying) Destroy(uiRoot.gameObject); else DestroyImmediate(uiRoot.gameObject);
            }
            ReleaseMaterials();playerPrevious=1;playerWait=playerFlash=0;
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var canvasObject = new GameObject("Arena interface", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            canvasObject.layer = 5;
            canvasObject.transform.SetParent(transform, false);
            uiRoot = (RectTransform)canvasObject.transform;
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = Mathf.Clamp(camera.nearClipPlane + .5f, camera.nearClipPlane + .01f,
                Mathf.Max(camera.nearClipPlane + .01f, camera.farClipPlane - .01f));
            canvas.sortingOrder = 50;
            canvas.pixelPerfect = true;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = .5f;

            BuildBossBar();
            BuildPlayerBar();
            BuildInstructions();
            BuildLockMarker();
            BuildMessagePanel();
            shownBoss = shownPlayer = 1;
            stateInitialized = false;
            Refresh();
        }

        private void BuildBossBar()
        {
            var panel=Rect("Boss vitals",uiRoot,new Vector2(.5f,1),new Vector2(0,-28),new Vector2(760,112));
            Label("Name",panel,new Vector2(.5f,1),Vector2.zero,new Vector2(700,36),BossTitle,23,White,TextAnchor.MiddleCenter,FontStyle.Bold);
            var frame=Box("Health gold rim",panel,new Vector2(.5f,1),new Vector2(0,-43),new Vector2(704,14),Orange);
            var track=Box("Boss health",frame,new Vector2(.5f,.5f),Vector2.zero,new Vector2(700,10),Ink);
            bossTrail=Fill("Recent damage",track,new Color32(236,196,151,255));
            bossFill=Fill("Health",track,new Color32(171,62,58,255));
            foreach(int side in new[]{-1,1}){var cap=Box("Gold endpoint",panel,new Vector2(.5f,1),new Vector2(side*361,-43),new Vector2(9,9),Orange);cap.localEulerAngles=new Vector3(0,0,45);}
            // Only the existing area-wide warning gets a banner; ordinary intent is read in the animation.
            combatCues=Box("Combat cues",panel,new Vector2(.5f,1),new Vector2(0,-70),new Vector2(540,38),new Color(Ink.r,Ink.g,Ink.b,.62f));
            actionLabel=Label("Attack",combatCues,new Vector2(0,1),new Vector2(14,-1),new Vector2(430,28),"",17,White,TextAnchor.MiddleLeft);
            stageLabel=Label("Opening",combatCues,new Vector2(1,1),new Vector2(-14,-1),new Vector2(80,28),"",16,Orange,TextAnchor.MiddleRight);
            var skillTrack=Box("Attack progress",combatCues,new Vector2(.5f,0),new Vector2(0,2),new Vector2(512,2),Ink);
            skillFill=Fill("Progress",skillTrack,Orange);skillLine=skillFill.GetComponent<Image>();
        }
        private void BuildPlayerBar()
        {
            healthStrip=new WayfarerHealthStrip(uiRoot,1.5f);
            counterPanel=Box("Counter opportunity",uiRoot,new Vector2(.5f,0),new Vector2(0,95),new Vector2(410,44),new Color(Ink.r,Ink.g,Ink.b,.72f));
            counterLabel=Label("Counter input",counterPanel,new Vector2(.5f,1),new Vector2(0,-1),new Vector2(394,29),"",16,Cyan,TextAnchor.MiddleCenter);
            var counterTrack=Box("Reward time remaining",counterPanel,new Vector2(.5f,0),new Vector2(0,5),new Vector2(380,2),Ink);
            counterFill=Fill("Remaining",counterTrack,Cyan);
        }
        private void BuildInstructions()
        {
            returnLabel=Label("Return to world",uiRoot,new Vector2(0,0),new Vector2(36,27),new Vector2(300,30),"F8  Return · RMB after round",16,Dim,TextAnchor.MiddleLeft);
            returnLabel.gameObject.SetActive(hosted);
            string[] keys={KingOfGraphic.Hero.HeroCombatInput.Label("Attack","LMB"),KingOfGraphic.Hero.HeroCombatInput.Label("Guard","Q"),KingOfGraphic.Hero.HeroCombatInput.Label("Dodge","Shift / RMB")},actions={"Attack","Guard","Dodge / hold run"};
            for(int i=0;i<keys.Length;i++)
            {
                var key=Box("Control "+keys[i],uiRoot,new Vector2(1,0),new Vector2(-40-(keys.Length-1-i)*130,61),new Vector2(120,31),new Color(Ink.r,Ink.g,Ink.b,.78f));
                Box("Key underline",key,new Vector2(.5f,0),Vector2.zero,new Vector2(120,1),Orange);
                Label("Key",key,new Vector2(.5f,.5f),Vector2.zero,new Vector2(116,29),keys[i],15,White,TextAnchor.MiddleCenter,FontStyle.Bold);
                Label("Action",uiRoot,new Vector2(1,0),new Vector2(-40-(keys.Length-1-i)*130,32),new Vector2(120,25),actions[i],14,White,TextAnchor.MiddleCenter);
            }
            Label("Recenter",uiRoot,new Vector2(1,0),new Vector2(-40,7),new Vector2(360,25),"R  Recenter   ·   Esc  Cursor",13,Dim,TextAnchor.MiddleRight);
            lockLabel=Label("Target mode",uiRoot,new Vector2(1,0),new Vector2(-40,98),new Vector2(360,25),"",14,Dim,TextAnchor.MiddleRight);
            cursorLabel=Label("Mouse capture",uiRoot,new Vector2(.5f,0),new Vector2(0,15),new Vector2(500,24),"",14,White,TextAnchor.MiddleCenter);
        }

        private void BuildLockMarker()
        {
            lockMarker = Rect("Target lock", uiRoot, new Vector2(.5f, .5f), Vector2.zero, new Vector2(44, 44));
            Color color = new Color(Orange.r, Orange.g, Orange.b, .92f);
            foreach (int x in new[] { -1, 1 })
                foreach (int y in new[] { -1, 1 })
                {
                    Box("Corner horizontal", lockMarker, new Vector2(.5f, .5f), new Vector2(x * 16, y * 21), new Vector2(10, 2), color);
                    Box("Corner vertical", lockMarker, new Vector2(.5f, .5f), new Vector2(x * 21, y * 16), new Vector2(2, 10), color);
                }
            lockMarker.gameObject.SetActive(false);
        }

        private void BuildMessagePanel()
        {
            messagePanel = Box("Encounter prompt", uiRoot, new Vector2(.5f, .5f), new Vector2(0, 0),
                new Vector2(750, 250), Ink);
            Box("Rule", messagePanel, new Vector2(.5f, 1), new Vector2(0, -1), new Vector2(750, 2), Orange);
            messageTitle = Label("Title", messagePanel, new Vector2(.5f, 1), new Vector2(0, -20), new Vector2(710, 56),
                BossTitle, 32, White, TextAnchor.MiddleCenter, FontStyle.Bold);
            messageDetail = Label("Detail", messagePanel, new Vector2(.5f, .5f), new Vector2(0, 0), new Vector2(710, 60),
                "Read the wind-up. Reach a corner when the black sun rises.", 21, Dim, TextAnchor.MiddleCenter);
            messagePrompt = Label("Prompt", messagePanel, new Vector2(.5f, 0), new Vector2(0, 32), new Vector2(710, 46),
                "PRESS ENTER TO ENGAGE", 23, Orange, TextAnchor.MiddleCenter, FontStyle.Bold);
        }

        private void LateUpdate() { Refresh(); }

        public void Refresh()
        {
            if (arena == null || uiRoot == null) return;
            float bossRatio = HealthRatio(arena.boss != null ? arena.boss.fighter : null);
            float playerRatio = HealthRatio(arena.player != null ? arena.player.fighter : null);
            float delta = Mathf.Min(Time.deltaTime, .1f);
            shownBoss = bossRatio > shownBoss ? bossRatio : Mathf.MoveTowards(shownBoss, bossRatio, delta * .28f);
            shownPlayer = playerRatio > shownPlayer ? playerRatio : playerWait>0?shownPlayer:Mathf.MoveTowards(shownPlayer,playerRatio,delta*1.8f);
            SetFill(bossFill, bossRatio); SetFill(bossTrail, shownBoss);

            if(playerRatio<playerPrevious){playerWait=.15f;playerFlash=1;}
            playerPrevious=playerRatio;playerWait=Mathf.Max(0,playerWait-delta);playerFlash=Mathf.MoveTowards(playerFlash,0,delta*4);
            if(playerArtworkMaterial){playerArtworkMaterial.SetFloat("_Health",playerRatio);playerArtworkMaterial.SetFloat("_Trail",shownPlayer);playerArtworkMaterial.SetFloat("_Flash",playerFlash);}
            if(bossArtworkMaterial)bossArtworkMaterial.SetFloat("_Health",bossRatio);
            if(bossValue&&arena.boss)SetText(bossValue,Mathf.CeilToInt(arena.boss.fighter.Health)+" / "+Mathf.CeilToInt(arena.boss.fighter.maximumHealth));
            if (arena.player != null && arena.player.fighter != null)
                healthStrip.Update(playerRatio,shownPlayer,playerFlash,Mathf.CeilToInt(arena.player.fighter.Health),Mathf.CeilToInt(arena.player.fighter.maximumHealth));

            bool fighting = arena.State == ArenaRoundState.Fighting;
            bool counterReady = fighting && arena.player != null && (arena.player.PerfectCounterReady||arena.player.FocusActive);
            counterPanel.gameObject.SetActive(counterReady);
            SetFill(counterFill, counterReady ? arena.player.FocusActive?arena.player.FocusRemaining/KingOfGraphic.Hero.HeroFocusSequence.MaximumSeconds:arena.player.PerfectCounterRemaining /
                Mathf.Max(.001f, arena.player.perfectCounterSeconds) : 0);
            SetText(counterLabel, !counterReady ? "" : arena.player.FocusActive?arena.player.FocusHint: arena.player.CounterQueued ? "COUNTER +30%  |  QUEUED" :
                arena.player.Guarding ? "COUNTER +30%  |  RELEASE Q + LMB" :
                arena.player.Dodging ? arena.player.CounterInputWindowOpen ? "COUNTER +30%  |  LMB TO QUEUE" :
                "COUNTER +30%  |  FINISH DODGE" : "COUNTER +30%  |  LMB");
            bool attacking = fighting && arena.boss != null && arena.boss.Attacking;
            bool recovering = fighting && arena.boss != null && arena.boss.IsRecovering;
            bool telegraph = fighting && arena.boss != null && arena.boss.Telegraph;
            string movement = arena.boss != null ? arena.boss.StateName : "WATCHING";
            bool approaching = movement == "STARTING" || movement == "APPROACHING" || movement == "MOVING";
            bool settling = movement == "STOPPING" || movement == "REBALANCING";
            string state = !fighting ? arena.State == ArenaRoundState.Ready ? "STANDBY" :
                arena.State == ArenaRoundState.Victory ? "DISABLED" : "GUARD" :
                recovering ? "COUNTER WINDOW" : telegraph ? "INCOMING" : attacking ? "ATTACK" :
                approaching ? "APPROACHING" : settling ? "SETTING STANCE" : "GUARD";
            string intent = !fighting ? arena.State == ArenaRoundState.Ready ? "AWAITING ENGAGEMENT" :
                arena.State == ArenaRoundState.Victory ? (Eclipse ? "ECLIPSE ENDED" : "FURNACE OFFLINE") : "ENGAGEMENT ENDED" :
                attacking ? (arena.boss.ActionName ?? "").Replace('_', ' ').ToUpperInvariant() :
                approaching ? "CLOSING DISTANCE" : settling ? "PREPARING TO ENGAGE" : "HOLDING POSITION";
            if(attacking&&Eclipse&&arena.boss.v13Motor.CurrentAttack==ArenaAttack.Meteor)
            {
                intent=telegraph?"BLACK SUN  ·  REACH A CORNER":"BLACK SUN  ·  IMPACT";
                if(telegraph)state=Mathf.Max(0,arena.boss.v13Motor.MeteorReleaseTime-arena.boss.v13Motor.SkillTime).ToString("F1")+"s";
            }
            combatCues.gameObject.SetActive(attacking && Eclipse && arena.boss.v13Motor.CurrentAttack==ArenaAttack.Meteor);
            SetText(stageLabel, state);
            stageLabel.color = recovering ? Cyan : attacking ? Orange : Dim;
            SetText(actionLabel, intent);
            skillLine.color = recovering ? Cyan : Orange;
            skillFill.parent.gameObject.SetActive(attacking);
            SetFill(skillFill, attacking ? arena.boss.SkillProgress : 0);

            SetText(lockLabel, fighting&&!arena.player.SwordDrawn ? "LMB  DRAW & ATTACK" : "");
            lockLabel.color=Dim;lockMarker.gameObject.SetActive(false);
            SetText(cursorLabel, fighting && arena.automaticTick && Cursor.lockState != CursorLockMode.Locked
                ? "RMB  CAPTURE MOUSE"
                : arena.player.Guarding ? arena.player.guard.PerfectWindowOpen ? "PRECISE GUARD" : "GUARD  |  CHIP DAMAGE" : "");

            if (!stateInitialized || arena.State != lastState)
            {
                lastState = arena.State;
                stateInitialized = true;
                if (lastState == ArenaRoundState.Ready)
                {
                    SetText(messageTitle, BossTitle);
                    SetText(messageDetail, "Read the wind-up. Reach a corner when the black sun rises.");
                    SetText(messagePrompt, "PRESS ENTER TO ENGAGE");
                    messageTitle.color = White;
                }
                else if (lastState == ArenaRoundState.Victory)
                {
                    SetText(messageTitle, Eclipse ? "WARDEN DEFEATED" : "SENTINEL DEFEATED");
                    SetText(messageDetail, Eclipse ? "The court opens beyond the eclipse." : "The furnace falls silent.");
                    SetText(messagePrompt, hosted?"ENTER TO RETRY  ·  RMB / F8 RETURN":"ENTER TO FIGHT AGAIN");
                    messageTitle.color = Cyan;
                }
                else if (lastState == ArenaRoundState.Defeat)
                {
                    SetText(messageTitle, "WAYFARER DOWN");
                    SetText(messageDetail, "Watch the opening. Return with a new approach.");
                    SetText(messagePrompt, hosted?"ENTER TO RETRY  ·  RMB / F8 RETURN":"ENTER TO TRY AGAIN");
                    messageTitle.color = Orange;
                }
            }
            // Let the physical descent and complete death animation remain visible.
            var foundation = arena.boss.FoundationMotor;
            bool deathStillPlaying = arena.State == ArenaRoundState.Victory && (Eclipse ? !arena.boss.v13Motor.DeathComplete : foundation != null && foundation.State != ArenaFoundationMotor.MotionState.DeathHold);
            var heroPresentation=arena.player?arena.player.GetComponent<ArenaCurrentCharacter>():null;
            bool playerDeathPlaying=arena.State==ArenaRoundState.Defeat&&heroPresentation&&!heroPresentation.DeathPresentationComplete;
            messagePanel.gameObject.SetActive(!fighting && !deathStillPlaying&&!playerDeathPlaying);
        }

        private static float HealthRatio(ArenaFighter fighter)
        { return fighter == null || fighter.maximumHealth <= 0 ? 0 : Mathf.Clamp01(fighter.Health / fighter.maximumHealth); }

        private static void SetFill(RectTransform rect, float amount)
        { rect.anchorMax = new Vector2(Mathf.Clamp01(amount), 1); }

        private static void SetText(Text label, string value)
        { if (label.text != value) label.text = value; }

        private RectTransform Fill(string name, RectTransform parent, Color color)
        {
            RectTransform rect = Box(name, parent, Vector2.zero, Vector2.zero, Vector2.zero, color);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = Vector2.zero;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            return rect;
        }

        private RectTransform Box(string name, RectTransform parent, Vector2 anchor, Vector2 position, Vector2 size, Color color)
        {
            RectTransform rect = Rect(name, parent, anchor, position, size);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return rect;
        }

        private Text Label(string name, RectTransform parent, Vector2 anchor, Vector2 position, Vector2 size,
            string value, int sizeInPixels, Color color, TextAnchor alignment, FontStyle style = FontStyle.Normal)
        {
            // Legacy font line metrics exceed fontSize; a font-sized box silently truncates
            // an entire line. All interface labels are deliberately single-line rectangles.
            size.y = Mathf.Max(size.y, sizeInPixels * 1.55f);
            RectTransform rect = Rect(name, parent, anchor, position, size);
            var text = rect.gameObject.AddComponent<Text>();
            text.font = font;
            text.text = value;
            text.fontSize = sizeInPixels;
            text.fontStyle = style;
            text.alignment = alignment;
            text.color = color;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.supportRichText = false;
            return text;
        }

        private static RectTransform Rect(string name, RectTransform parent, Vector2 anchor, Vector2 position, Vector2 size)
        {
            var obj = new GameObject(name, typeof(RectTransform));
            obj.layer = 5;
            var rect = (RectTransform)obj.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = anchor;
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            return rect;
        }
    }
}

namespace SamProduction.BossArena
{
    public sealed class WayfarerHealthStrip
    {
        public readonly RectTransform Root;
        readonly RectTransform fill,trail;
        readonly Image fillImage;
        readonly Text value,warning;
        static readonly Color Gold=new Color32(186,155,87,255),Paper=new Color32(245,233,198,255),Ink=new Color32(23,57,50,235);
        public WayfarerHealthStrip(Transform parent,float scale=1)
        {
            Root=Rect("Wayfarer health strip",parent,new Vector2(.5f,0),new Vector2(0,24*scale),new Vector2(280,34));Root.localScale=Vector3.one*scale;
            var border=Box("Gold rim",Root,new Vector2(.5f,1),new Vector2(8,-4),new Vector2(254,16),Gold);
            var track=Box("Health track",border,Vector2.one*.5f,Vector2.zero,new Vector2(251,13),Ink);
            trail=Fill("Recent damage",track,Paper);fill=Fill("Current health",track,new Color32(117,165,70,255));fillImage=fill.GetComponent<Image>();
            var diamond=Box("Compass cap",Root,new Vector2(0,.5f),new Vector2(11,5),new Vector2(18,18),Gold);diamond.pivot=Vector2.one*.5f;diamond.localEulerAngles=new Vector3(0,0,45);
            Box("Compass inset",diamond,Vector2.one*.5f,Vector2.zero,new Vector2(14,14),Ink);
            var star=Box("Compass light",diamond,Vector2.one*.5f,Vector2.zero,new Vector2(5,5),Paper);star.localEulerAngles=new Vector3(0,0,-45);
            value=Label("Health value",Root,new Vector2(105,-3),new Vector2(150,18),12);value.alignment=TextAnchor.MiddleRight;
            warning=Label("Low health",Root,new Vector2(32,-21),new Vector2(220,15),10);warning.text="LOW HEALTH";warning.color=new Color32(250,158,126,255);
        }
        public void Update(float health,float recent,float flash,int current,int maximum)
        {
            fill.anchorMax=new Vector2(Mathf.Clamp01(health),1);trail.anchorMax=new Vector2(Mathf.Clamp01(recent),1);
            var normal=health<=.25f?new Color32(208,85,65,255):new Color32(117,165,70,255);
            fillImage.color=Color.Lerp(normal,Paper,Mathf.Clamp01(flash)*.5f);
            string next=current+" / "+maximum;if(value.text!=next)value.text=next;
            warning.gameObject.SetActive(health>0&&health<=.25f);
        }
        static RectTransform Rect(string name,Transform parent,Vector2 anchor,Vector2 position,Vector2 size)
        {var t=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();t.gameObject.layer=5;t.SetParent(parent,false);t.anchorMin=t.anchorMax=t.pivot=anchor;t.anchoredPosition=position;t.sizeDelta=size;return t;}
        static RectTransform Box(string name,Transform parent,Vector2 anchor,Vector2 position,Vector2 size,Color color)
        {var t=Rect(name,parent,anchor,position,size);var i=t.gameObject.AddComponent<Image>();i.color=color;i.raycastTarget=false;return t;}
        static RectTransform Fill(string name,Transform parent,Color color)
        {var t=Box(name,parent,Vector2.zero,Vector2.zero,Vector2.zero,color);t.anchorMax=Vector2.one;t.offsetMin=t.offsetMax=Vector2.zero;return t;}
        static Text Label(string name,Transform parent,Vector2 at,Vector2 size,int fontSize)
        {var t=Rect(name,parent,new Vector2(0,1),at,size).gameObject.AddComponent<Text>();t.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");t.fontSize=fontSize;t.fontStyle=FontStyle.Bold;t.color=Paper;t.raycastTarget=false;t.alignment=TextAnchor.MiddleCenter;t.horizontalOverflow=HorizontalWrapMode.Overflow;t.verticalOverflow=VerticalWrapMode.Overflow;var o=t.gameObject.AddComponent<Outline>();o.effectColor=new Color(0,0,0,.8f);o.effectDistance=new Vector2(1,-1);return t;}
    }
}
