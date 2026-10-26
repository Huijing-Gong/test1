using UnityEngine;

// Authored anchors for this map. Progress and bridge persistence remain owned by BaselineFlow.
[DisallowMultipleComponent]
[RequireComponent(typeof(BaselineWorld))]
public sealed class OverworldV2Layout : MonoBehaviour
{
    public const int CurrentMapVersion = 3;
    [SerializeField] int authoredMapVersion = 2;
    [SerializeField, Min(.01f)] float positionScaleFromVersion2 = 1f;
    public int AuthoredMapVersion => authoredMapVersion;
    public float PositionScaleFromVersion2 => positionScaleFromVersion2;
    [SerializeField] Vector3 arrivalPosition = new Vector3(0, 2.5f, -75);
    [SerializeField] Vector3 campPosition = new Vector3(0, 3.3f, -43);
    [SerializeField] Vector3 introShorePosition = new Vector3(-35, 1.1f, -8);
    [SerializeField] Vector3 islandRestPosition = new Vector3(-37, .73f, 11);
    [SerializeField] Vector3 islandDestination = new Vector3(-41, 2.8f, 22);
    [SerializeField] Vector3 lakePosition = new Vector3(-47, .22f, 23);
    [SerializeField] Vector3 westRegionPosition = new Vector3(-61, 2, -24);
    [SerializeField] Vector3 eastRegionPosition = new Vector3(45, 5, 8);
    [SerializeField] Vector2 mapCenter;
    [SerializeField, Min(1)] float mapHalfExtent = 85;
    [SerializeField] GameObject northBridge;
    [SerializeField] Collider[] navigationSurfaces = new Collider[0];
    [SerializeField] MeshFilter[] mapSurfaces = new MeshFilter[0];
    [Header("Optional physical tutorial practice spur")]
    [SerializeField] bool tutorialPracticeReady;
    [SerializeField] Vector3 jumpPracticePosition = new Vector3(-16, 2.36f, -28);
    [SerializeField] Vector3 dropPlatformPosition = new Vector3(-26, 6.4f, -19);
    [SerializeField] Vector3 dropLandingPosition = new Vector3(-30, 1.28f, -16);
    [SerializeField] Vector3 climbBasePosition = new Vector3(-36.5f, 1.11f, 12.8f);
    [SerializeField] Vector3 climbTopPosition = new Vector3(-36.5f, 2.9f, 15.75f);

    public bool TutorialPracticeReady => tutorialPracticeReady;
    public Vector3 JumpPracticePosition => jumpPracticePosition;
    public Vector3 DropPlatformPosition => dropPlatformPosition;
    public Vector3 DropLandingPosition => dropLandingPosition;
    public Vector3 ClimbBasePosition => climbBasePosition;
    public Vector3 ClimbTopPosition => climbTopPosition;

    public Vector3 ArrivalPosition => arrivalPosition;
    public Vector3 CampPosition => campPosition;
    public Vector3 IntroShorePosition => introShorePosition;
    [SerializeField] Vector3 islandSwimEntry = new Vector3(-53.06f, 3.5f, 11.5f);
    [SerializeField] Vector3 islandSwimExit = new Vector3(-53.06f, 5.85f, 21.1f);
    public Vector3 IslandSwimEntry => islandSwimEntry;
    public Vector3 IslandSwimExit => islandSwimExit;
    public Vector3 IslandRestPosition => islandRestPosition;
    public Vector3 IslandDestination => islandDestination;
    public Vector3 LakePosition => lakePosition;
    public Vector3 IslandPosition => islandDestination;
    public Vector3 WestRegionPosition => westRegionPosition;
    public Vector3 EastRegionPosition => eastRegionPosition;
    public Vector2 MapCenter => mapCenter;
    public float MapHalfExtent => mapHalfExtent;
    public MeshFilter[] MapSurfaces => mapSurfaces;
    public bool BridgeOpen => northBridge && northBridge.activeSelf;

    public bool IsNavigationSurface(Collider surface)
    {
        if (!surface || !surface.enabled || !surface.gameObject.activeInHierarchy) return false;
        foreach (var candidate in navigationSurfaces)
            if (candidate == surface) return true;
        return false;
    }

    public void ApplyBridgeState(bool open)
    {
        if (northBridge && northBridge.activeSelf != open)
        {
            northBridge.SetActive(open);
            Physics.SyncTransforms();
        }
        var world = GetComponent<BaselineWorld>();
        if (world) world.RefreshNavigation();
    }
}
