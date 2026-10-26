using UnityEngine;

// First two stages share a layout; Eclipse has only the two side platforms.
// No fighter physics, move data or progression is stored here.
[DisallowMultipleComponent, DefaultExecutionOrder(-150)]
public sealed class BattleArenaStages : MonoBehaviour
{
    public GameObject sunlitArt;
    public GameObject starCourtArt;
    public Renderer paintedBackdrop;
    public Texture2D sunlitBackground;
    public Texture2D starCourtBackground;
    public Transform mainGround;
    public Transform[] upperPlatforms;
    public BattleCameraController battleCamera;
    public const float StarCourtWidth = 24f;
    public static Vector3 StarCourtPlatformPosition(int i) =>
        new Vector3(i == 0 ? -10.3f : i == 1 ? -2f : 6.3f, i == 1 ? 6.4f : 3.7f, 0);
    public int ActiveIndex { get; private set; }
    public static int RequestedIndex { get; private set; }
    MaterialPropertyBlock properties;
    Vector3 groundScale;
    bool layoutCaptured;
    GameObject eclipsePrefab, eclipseArt;
    Texture2D eclipseBackground;
    bool eclipseLoaded;
    bool[] upperActive;

    public Texture2D EclipseBackground { get { LoadEclipse(); return eclipseBackground; } }
    void LoadEclipse()
    {
        if (eclipseLoaded) return;
        eclipseLoaded = true;
        eclipsePrefab = Resources.Load<GameObject>("SideA/Eclipse/EclipseArena");
        eclipseBackground = Resources.Load<Texture2D>("SideA/Eclipse/EclipseBackground");
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => RequestedIndex = 0;
    public static void ResetVisit() => RequestedIndex = 0;
    void Awake() { CaptureLayout(); Apply(RequestedIndex); }
    void CaptureLayout()
    {
        if (layoutCaptured) return;
        layoutCaptured = true;
        if (mainGround) groundScale = mainGround.localScale;
        if (upperPlatforms != null)
        {
            upperActive = new bool[upperPlatforms.Length];
            for (int i = 0; i < upperPlatforms.Length; i++)
                upperActive[i] = upperPlatforms[i] && upperPlatforms[i].gameObject.activeSelf;
        }
    }
    public bool IsAvailable(int index)
    {
        if (index == 0) return sunlitArt && sunlitBackground;
        if (index == 1) return starCourtArt && starCourtBackground;
        if (index != 2) return false;
        LoadEclipse(); return eclipsePrefab && eclipseBackground;
    }
    public bool Apply(int index)
    {
        if (!IsAvailable(index)) return false;
        CaptureLayout();
        ActiveIndex = RequestedIndex = index;
        if (mainGround)
            mainGround.localScale = new Vector3(StarCourtWidth / 10f, groundScale.y, groundScale.z);
        if (upperPlatforms != null)
            for (int i = 0; i < upperPlatforms.Length; i++)
                if (upperPlatforms[i])
                {
                    upperPlatforms[i].position = StarCourtPlatformPosition(i);
                    upperPlatforms[i].gameObject.SetActive(upperActive[i] && !(index == 2 && i == 1));
                }
        if (battleCamera)
            battleCamera.SetArenaComposition(new Bounds(new Vector3(-2, 3.2f, 0), new Vector3(StarCourtWidth, 6.4f, 5.8f)));
        Physics.SyncTransforms();
        sunlitArt.SetActive(index == 0); starCourtArt.SetActive(index == 1);
        if (index == 2 && !eclipseArt)
        {
            eclipseArt = Instantiate(eclipsePrefab);
            // Parent with world position retained: the authored root is in arena coordinates.
            eclipseArt.transform.SetParent(transform, true);
            eclipseArt.name = "Eclipse Cathedral Platforms";
        }
        if (eclipseArt) eclipseArt.SetActive(index == 2);
        if (paintedBackdrop)
        {
            if (properties == null) properties = new MaterialPropertyBlock();
            paintedBackdrop.GetPropertyBlock(properties);
            properties.SetTexture("_BaseMap", index == 0 ? sunlitBackground : index == 1 ? starCourtBackground : eclipseBackground);
            paintedBackdrop.SetPropertyBlock(properties);
        }
        GetComponent<BattleStageLighting>()?.ApplyStage(index);
        if(index==2 && eclipseArt && paintedBackdrop)
        {
            var atmosphere=eclipseArt.GetComponent<BattleEclipseAtmosphere>();
            if(!atmosphere)atmosphere=eclipseArt.AddComponent<BattleEclipseAtmosphere>();
            atmosphere.Bind(this,paintedBackdrop.transform);
        }
        var hud = GetComponent<BattleTeamHud>();
        if (hud) hud.SetStageLabels(index == 0 ? "SUNLIT RUINS" : index == 1 ? "TWILIGHT RUINS" : "ECLIPSE CATHEDRAL", index == 0 ? "BEGINNER" : index == 1 ? "INTERMEDIATE" : "ADVANCED");
        return true;
    }
}
