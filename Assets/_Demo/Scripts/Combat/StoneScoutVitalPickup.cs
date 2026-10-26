using UnityEngine;
using UnityEngine.Rendering;

// Collected by proximity only when the player can actually use the health.
// No physics collider, save entry, random roll or new economy is required.
[DisallowMultipleComponent]
public sealed class StoneScoutVitalPickup : MonoBehaviour
{
    private int healAmount;
    bool pending;KingOfGraphic.Hero.HeroWorldAdapter pickupHero;
    private float lifetime;
    private Vector3 restingPosition;
    private Transform shardVisual;
    private AudioClip pickupClip;
    private PlayerVitals player;
    private Health playerHealth;
    private BaselineWorld world;

    public static StoneScoutVitalPickup Spawn(Vector3 position, Material material, int heal,
        float seconds, AudioClip pickupSound = null)
    {
        var root = new GameObject("Stone Scout vital shard");
        root.transform.position = position;
        var pickup = root.AddComponent<StoneScoutVitalPickup>();
        pickup.healAmount = Mathf.Max(1, heal);
        pickup.lifetime = Mathf.Max(1f, seconds);
        pickup.restingPosition = position;
        pickup.pickupClip = pickupSound;

        GameObject shard = GameObject.CreatePrimitive(PrimitiveType.Cube);
        shard.name = "Floating cyan shard";
        shard.transform.SetParent(root.transform, false);
        shard.transform.localScale = new Vector3(0.34f, 0.42f, 0.34f);
        shard.transform.localRotation = Quaternion.Euler(35f, 45f, 35f);
        Collider collider = shard.GetComponent<Collider>();
        collider.enabled = false;
        Destroy(collider);
        MeshRenderer renderer = shard.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        var glow = new MaterialPropertyBlock();
        glow.SetColor("_BaseColor", new Color(0.35f, 1f, 0.95f));
        glow.SetColor("_Color", new Color(0.35f, 1f, 0.95f));
        renderer.SetPropertyBlock(glow);
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        pickup.shardVisual = shard.transform;

        GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        marker.name = "Vital shard ground marker";
        marker.transform.SetParent(root.transform, false);
        marker.transform.localPosition = new Vector3(0f, -0.5f, 0f);
        marker.transform.localScale = new Vector3(0.35f, 0.012f, 0.35f);
        Collider markerCollider = marker.GetComponent<Collider>();
        markerCollider.enabled = false;
        Destroy(markerCollider);
        MeshRenderer markerRenderer = marker.GetComponent<MeshRenderer>();
        markerRenderer.sharedMaterial = material;
        markerRenderer.SetPropertyBlock(glow);
        markerRenderer.shadowCastingMode = ShadowCastingMode.Off;
        markerRenderer.receiveShadows = false;
        return pickup;
    }

    private void Awake()
    {
        player = FindFirstObjectByType<PlayerVitals>();
        playerHealth = player != null ? player.GetComponent<Health>() : null;
        world = FindFirstObjectByType<BaselineWorld>();
    }

    private void Update()
    {
        lifetime -= Time.deltaTime;
        if (lifetime <= 0f) { Destroy(gameObject); return; }

        float bob = Mathf.Sin(Time.time * 4f) * 0.08f;
        transform.position = restingPosition + Vector3.up * bob;
        if (shardVisual != null)
        {
            shardVisual.Rotate(Vector3.up, 90f * Time.deltaTime, Space.World);
            shardVisual.localScale = new Vector3(0.34f, 0.42f, 0.34f) *
                                     (1f + 0.1f * Mathf.Sin(Time.time * 6f));
        }

        if (player == null || playerHealth == null || !player.SimulationActive || playerHealth.IsDead ||
            Vector3.Distance(player.transform.position + Vector3.up * 0.9f, transform.position) > 1.05f)
            return;

        if(pending){if(!pickupHero||!pickupHero.HasPresentation("INTERACT_PICKUP"))pending=false;return;}
        pickupHero=player.GetComponent<KingOfGraphic.Hero.HeroWorldAdapter>();
        if(pickupHero){if(playerHealth.CurrentHealth>=playerHealth.MaxHealth)return;pending=pickupHero.PresentInteraction("INTERACT_PICKUP",()=>{pending=false;if(this&&player&&Vector3.Distance(player.transform.position+Vector3.up*.9f,transform.position)<1.4f)Collect();});return;}
        Collect();
    }
    void Collect(){
        if (!playerHealth.TryHeal(healAmount)) return;
        if (pickupClip != null) AudioSource.PlayClipAtPoint(pickupClip, transform.position, 0.45f);
        if (world != null) world.ShowNotice("Vital shard: +" + healAmount + " health.");
        Destroy(gameObject);
    }
}
