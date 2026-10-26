using UnityEngine;
using UnityEngine.InputSystem;

public sealed partial class BaselineFlow
{
    public CloudGateTitleScreen titleScreen;
    public bool TitleScreenVisible => titleEntering || (titleScreen && titleScreen.Visible);
    public bool TitleTransitionInProgress => titleEntering;
    public bool TitleConfirmationOpen => TitleScreenVisible && confirmNew;
    public bool TitleSettingsOpen => TitleScreenVisible && titlePanel==1;
    public const string TitleVolumeKey = "LakeDomain.Presentation.Volume";
    public const string TitleMotionKey = "LakeDomain.Presentation.ReducedMotion";
    bool titleEntering, titleApplied, titleReady, titleNewJourney, titleReducedMotion;
    float titleEntryStarted;
    int titlePanel, titleFocus, titleKeyboardActionFrame=-1;
    bool titleKeyboardFocus;
    public int TitleFocusedControl => titleFocus;
    public bool TitleReducedMotion => titleReducedMotion;
    void SetTitlePanel(int panel)
    {titlePanel=panel;titleFocus=panel==1?20:panel==2?30:panel==3?41:0;}
    void TitleKeyboard(Keyboard keyboard)
    {
        if(keyboard.escapeKey.wasPressedThisFrame){CancelTitleAction();titleKeyboardActionFrame=Time.frameCount;return;}
        bool forward=keyboard.tabKey.wasPressedThisFrame || keyboard.downArrowKey.wasPressedThisFrame;
        bool backward=keyboard.upArrowKey.wasPressedThisFrame || (keyboard.tabKey.wasPressedThisFrame && keyboard.shiftKey.isPressed);
        if(forward || backward)
        {
            int[] items=confirmNew?new[]{11,10}:titlePanel==1?new[]{20,21,22}:titlePanel==2?new[]{30}:titlePanel==3?new[]{41,40}:
                Application.platform==RuntimePlatform.WebGLPlayer?(slotExists?new[]{0,1,2,3}:new[]{0,2,3}):(slotExists?new[]{0,1,2,3,4}:new[]{0,2,3,4});
            int index=System.Array.IndexOf(items,titleFocus);index=(index+(backward?-1:1)+items.Length)%items.Length;
            titleFocus=items[index];titleKeyboardFocus=true;titleKeyboardActionFrame=Time.frameCount;return;
        }
        if(titlePanel==1 && titleFocus==20 && (keyboard.leftArrowKey.wasPressedThisFrame || keyboard.rightArrowKey.wasPressedThisFrame))
        {AudioListener.volume=Mathf.Clamp01(AudioListener.volume+(keyboard.rightArrowKey.wasPressedThisFrame?.05f:-.05f));titleKeyboardFocus=true;titleKeyboardActionFrame=Time.frameCount;return;}
        if(!(keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame || keyboard.spaceKey.wasPressedThisFrame))return;
        titleKeyboardFocus=true;titleKeyboardActionFrame=Time.frameCount;
        if(confirmNew){if(titleFocus==10)RequestTitleEntry(true,true);else CancelTitleAction();return;}
        switch(titleFocus)
        {
            case 0:RequestTitleEntry(false);break;case 1:RequestTitleEntry(true);break;
            case 2:OpenTitleSettings();break;case 3:SetTitlePanel(2);break;case 4:SetTitlePanel(3);break;
            case 21:titleReducedMotion=!titleReducedMotion;break;
            case 22:case 30:case 41:CancelTitleAction();break;case 40:ExitFromTitle();break;
        }
    }
    void ExitFromTitle()
    {
        RememberTitleOptions();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying=false;
#else
        Application.Quit();
#endif
    }
    Vector2 titleStatusScroll;
    GUIStyle cloudLogo, cloudText, cloudSmall, cloudButton, cloudHeading;
    static readonly Color CloudNavy = new Color(.055f,.11f,.23f,.96f);
    static readonly Color CloudGold = new Color(.87f,.72f,.39f,1);
    static readonly Color CloudPaper = new Color(.98f,.96f,.88f,1);

    public void OpenTitleSettings()
    { if(TitleScreenVisible&&!titleEntering){confirmNew=false;SetTitlePanel(1);} }
    public void CancelTitleAction()
    {
        if(!TitleScreenVisible||titleEntering)return;
        if(titlePanel==1)RememberTitleOptions();
        SetTitlePanel(0);confirmNew=false;
    }

    void InitializeCloudTitle()
    {
        if (!titleScreen) return;
        titleReducedMotion = PlayerPrefs.GetInt(TitleMotionKey,0)!=0;
        if(PlayerPrefs.HasKey(TitleVolumeKey)) AudioListener.volume=Mathf.Clamp01(PlayerPrefs.GetFloat(TitleVolumeKey));
        try { titleScreen.Show(Camera.main); }
        catch(System.Exception exception) { titleScreen.Hide(); Debug.LogError("Title presentation could not start: "+exception.Message,this); }
    }

    // Only the initial title calls this. Existing journal/save APIs retain their behavior.
    public bool RequestTitleEntry(bool newJourney, bool replaceConfirmed=false)
    {
        if(!TitleScreenVisible || titleEntering || Started || titlePanel!=0)return false;
        hasSave=BaselineSaveStore.TryLoad(saveKeyPrefix,out availableSave,out var message);
        RefreshSlotPresence();
        if(!newJourney && !hasSave && slotExists)
        { Status=message; return false; }
        newJourney = newJourney || !hasSave;
        if(newJourney && slotExists && !replaceConfirmed)
        { confirmNew=true;titleFocus=11; return false; }
        confirmNew=false; titleNewJourney=newJourney;
        titleApplied=titleReady=false;titleEntering=true;titleEntryStarted=Time.unscaledTime;
        Lock();return true;
    }

    bool TickCloudTitle()
    {
        if(!TitleScreenVisible)return false;
        // Existing direct Continue/NewGame callers need no presentation-specific API.
        if(Started && !titleEntering){titleScreen.Hide();return false;}
        if(!titleEntering)
        {
            titleScreen.SetProgress(0,titleReducedMotion);
            var keyboard=Keyboard.current;
            if(keyboard!=null)TitleKeyboard(keyboard);
            return true;
        }
        float duration=titleReducedMotion?.4f:1.8f;
        float progress=Mathf.Clamp01((Time.unscaledTime-titleEntryStarted)/duration);
        titleScreen.SetProgress(progress,titleReducedMotion);
        if(!titleApplied && progress>=.78f)
        {
            titleApplied=true;
            try
            {
                if(titleNewJourney){NewGame();titleReady=Started;}
                else titleReady=ContinueGame() && Started;
            }
            catch(System.Exception exception)
            { Status="Could not enter the world: "+exception.Message;titleReady=false; }
            if(!titleReady)
            {
                Started=false;MenuOpen=true;titleEntering=false;Lock();
                titleScreen.SetProgress(0,titleReducedMotion);return true;
            }
        }
        if(progress>=.86f && titleReady)titleScreen.Hide();
        if(progress>=1 && titleReady)
        {
            titleEntering=false;titleScreen.Hide();openingClosing=false;
            // Tutorial.Begin owns its modal lock. Its practice action releases it.
            if(!(Tutorial && Tutorial.ModalOpen))unlockAfterFrame=Time.frameCount;
        }
        return true;
    }

    void RememberTitleOptions()
    {
        try
        {
            PlayerPrefs.SetFloat(TitleVolumeKey,AudioListener.volume);
            PlayerPrefs.SetInt(TitleMotionKey,titleReducedMotion?1:0);
            PlayerPrefs.Save();
        }
        catch(System.Exception exception) { Status="Settings apply for this session, but could not be saved: "+exception.Message; }
    }

    void CloudStyles()
    {
        if(cloudLogo!=null)return;
        cloudLogo=CloudStyle(61,FontStyle.Bold);cloudLogo.normal.textColor=CloudPaper;
        cloudText=CloudStyle(20,FontStyle.Normal);cloudSmall=CloudStyle(16,FontStyle.Normal);
        cloudButton=CloudStyle(27,FontStyle.Normal);cloudHeading=CloudStyle(30,FontStyle.Bold);
    }
    GUIStyle CloudStyle(int size,FontStyle weight)
    {
        var s=new GUIStyle(GUIStyle.none){font=GUI.skin.font,fontSize=size,fontStyle=weight,alignment=TextAnchor.MiddleCenter,wordWrap=true};
        s.normal.textColor=CloudPaper;WayfarerUITheme.KeepLabelColor(s);return s;
    }
    static void CloudFill(Rect rect,Color color)
    { var old=GUI.color;GUI.color=old*color;GUI.DrawTexture(rect,Texture2D.whiteTexture);GUI.color=old; }
    void CloudFrame(Rect rect,Color fill)
    {
        CloudFill(rect,CloudGold);CloudFill(new Rect(rect.x+1.5f,rect.y+1.5f,rect.width-3,rect.height-3),fill);
    }
    bool CloudButton(Rect rect,string text,bool primary=false,int control=-1)
    {
        bool hover=rect.Contains(Event.current.mousePosition) && GUI.enabled;
        bool focused=titleKeyboardFocus && control>=0 && titleFocus==control && GUI.enabled;
        if(primary || focused)CloudFrame(rect,(hover||focused)?new Color(.13f,.24f,.40f,.99f):CloudNavy);
        else if(hover)CloudFill(rect,new Color(.22f,.34f,.49f,.7f));
        GUI.Label(rect,text,primary?cloudButton:cloudText);
        bool clicked=GUI.Button(rect,GUIContent.none,GUIStyle.none) && titleKeyboardActionFrame!=Time.frameCount;
        if(clicked && control>=0){titleFocus=control;titleKeyboardFocus=false;}
        return clicked;
    }
    void CloudLabel(Rect rect,string text,GUIStyle style)
    {
        var old=GUI.color;GUI.color=old*new Color(.03f,.08f,.15f,.85f);
        GUI.Label(new Rect(rect.x+1,rect.y+2,rect.width,rect.height),text,style);GUI.color=old;GUI.Label(rect,text,style);
    }

    void DrawCloudTitle()
    {
        CloudStyles();
        var oldMatrix=GUI.matrix;var oldColor=GUI.color;var oldEnabled=GUI.enabled;int oldDepth=GUI.depth;
        float scale=Mathf.Max(.01f,Mathf.Min(Screen.width/1600f,Screen.height/900f));
        float w=Screen.width/scale,h=Screen.height/scale;
        GUI.matrix=Matrix4x4.Scale(new Vector3(scale,scale,1));GUI.color=Color.white;GUI.depth=-200;
        try
        {
            float p=titleEntering?titleScreen.EntryProgress:0;
            float visible=1-Mathf.SmoothStep(0,1,Mathf.Clamp01(p/.35f));
            GUI.color=new Color(1,1,1,visible);GUI.enabled=!titleEntering;
            for(int i=0;i<20;i++)CloudFill(new Rect(0,h-220+i*11,w,11),new Color(.025f,.065f,.14f,.015f+i*.023f));
            CloudLabel(new Rect((w-1000)/2,45,1000,85),"L A K E   D O M A I N",cloudLogo);
            CloudFill(new Rect(w/2-240,140,480,1),CloudGold);
            CloudLabel(new Rect(w/2-300,153,600,28),"THE WAYFARER'S JOURNAL",cloudSmall);
            float buttonY=h-181;
            bool modal=titlePanel!=0 || confirmNew;
            GUI.enabled=!titleEntering && !modal;
            if(CloudButton(new Rect(w/2-235,buttonY,470,64),"ENTER WORLD",true,0))RequestTitleEntry(false);
            string hint=hasSave?"Continue your journey":slotExists?"Your saved journey needs attention":"Begin a new journey";
            CloudLabel(new Rect(w/2-300,buttonY+68,600,31),hint,cloudSmall);
            if(slotExists && CloudButton(new Rect(w/2-130,buttonY+105,260,37),"New Journey",false,1))RequestTitleEntry(true);
            if(CloudButton(new Rect(35,h-62,125,38),"Settings",false,2))OpenTitleSettings();
            if(CloudButton(new Rect(175,h-62,125,38),"Credits",false,3))SetTitlePanel(2);
            if(Application.platform!=RuntimePlatform.WebGLPlayer && CloudButton(new Rect(w-160,h-62,125,38),"Exit",false,4))SetTitlePanel(3);
            if(!string.IsNullOrEmpty(Status) && Status!="Explore freely. Your progress can be saved in this browser or Editor." && Status!="Saved progress loaded." && Status!="No saved progress was found.")
            {
                float fullHeight=Mathf.Max(38,cloudSmall.CalcHeight(new GUIContent(Status),760)+8);
                float boxHeight=Mathf.Min(150,fullHeight);
                Rect notice=new Rect(w/2-400,buttonY-15-boxHeight,800,boxHeight);
                CloudFill(notice,new Color(.035f,.085f,.18f,.86f));
                titleStatusScroll=GUI.BeginScrollView(notice,titleStatusScroll,new Rect(0,0,775,fullHeight),false,fullHeight>boxHeight);
                GUI.Label(new Rect(10,0,760,fullHeight),Status,cloudSmall);GUI.EndScrollView();
            }
            GUI.enabled=!titleEntering;GUI.color=new Color(1,1,1,visible);
            if(modal)
            {
                CloudFill(new Rect(0,0,w,h),new Color(.025f,.05f,.10f,.74f));
                Rect panel=new Rect(w/2-320,h/2-200,640,400);CloudFrame(panel,CloudNavy);
                if(confirmNew)
                {
                    GUI.Label(new Rect(panel.x+30,panel.y+27,580,48),"START A NEW JOURNEY?",cloudHeading);
                    GUI.Label(new Rect(panel.x+50,panel.y+90,540,105),hasSave?"This replaces your current saved journey.\nYour existing progress will be lost.":"An existing save could not be read.\nReplace it only if you want to start again.",cloudText);
                    if(CloudButton(new Rect(panel.x+70,panel.y+230,500,58),"Replace save and start",true,10))RequestTitleEntry(true,true);
                    if(CloudButton(new Rect(panel.x+70,panel.y+310,500,48),"Keep existing save",false,11))CancelTitleAction();
                }
                else if(titlePanel==1)
                {
                    GUI.Label(new Rect(panel.x+30,panel.y+25,580,50),"SETTINGS",cloudHeading);
                    GUI.Label(new Rect(panel.x+55,panel.y+105,180,35),"Volume "+Mathf.RoundToInt(AudioListener.volume*100)+"%",cloudText);
                    if(titleKeyboardFocus && titleFocus==20)CloudFrame(new Rect(panel.x+251,panel.y+98,310,55),new Color(.12f,.22f,.35f,.9f));
                    float volume=GUI.HorizontalSlider(new Rect(panel.x+265,panel.y+117,280,30),AudioListener.volume,0,1);
                    if(!Mathf.Approximately(volume,AudioListener.volume))AudioListener.volume=volume;
                    GUI.Label(new Rect(panel.x+55,panel.y+180,275,40),"Reduce motion",cloudText);
                    if(CloudButton(new Rect(panel.x+365,panel.y+180,180,40),titleReducedMotion?"ON":"OFF",false,21))
                    {titleReducedMotion=!titleReducedMotion;}
                    GUI.Label(new Rect(panel.x+60,panel.y+232,520,40),"Uses a short fade instead of the camera push.",cloudSmall);
                    if(CloudButton(new Rect(panel.x+70,panel.y+315,500,48),"Back",false,22))CancelTitleAction();
                }
                else if(titlePanel==2)
                {
                    GUI.Label(new Rect(panel.x+30,panel.y+25,580,50),"CREDITS",cloudHeading);
                    GUI.Label(new Rect(panel.x+50,panel.y+110,540,150),"LAKE DOMAIN\n\nCreated by KingOfGraphic\nCOMP30019 · Project 2",cloudText);
                    if(CloudButton(new Rect(panel.x+70,panel.y+315,500,48),"Back",false,30))CancelTitleAction();
                }
                else
                {
                    GUI.Label(new Rect(panel.x+30,panel.y+35,580,50),"LEAVE LAKE DOMAIN?",cloudHeading);
                    GUI.Label(new Rect(panel.x+50,panel.y+115,540,70),"Your saved journey will be kept.",cloudText);
                    if(CloudButton(new Rect(panel.x+70,panel.y+235,500,54),"Exit game",true,40))ExitFromTitle();
                    if(CloudButton(new Rect(panel.x+70,panel.y+310,500,48),"Stay",false,41))CancelTitleAction();
                }
            }
            if(!titleEntering)CloudLabel(new Rect(w/2-360,h-31,720,25),titlePanel==1?"Tab / Up / Down  Select   ·   Left / Right  Volume   ·   Enter  Confirm   ·   Esc  Back":"Tab / Up / Down  Select   ·   Enter / Space  Confirm   ·   Esc  Back",cloudSmall);
            GUI.color=Color.white;
            if(titleEntering)
            {
                float veil=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.58f,.77f,p))*(1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.86f,1,p)));
                CloudFill(new Rect(0,0,w,h),new Color(.79f,.88f,.96f,veil));
                CloudFill(new Rect(w/2-220,h-100,440,40),new Color(.035f,.085f,.18f,.86f));
                CloudLabel(new Rect(w/2-210,h-100,420,40),"Entering world…",cloudText);
            }
        }
        finally {GUI.matrix=oldMatrix;GUI.color=oldColor;GUI.enabled=oldEnabled;GUI.depth=oldDepth;}
    }
}
