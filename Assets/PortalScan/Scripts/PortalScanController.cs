using UnityEngine;

public class PortalScanController : MonoBehaviour
{
    // References and settings exposed in the Unity Inspector.
    [SerializeField] private Renderer portalRenderer;
    [SerializeField] private Transform player;
    [SerializeField, Min(0f)] private float activationDistance = 5f;
    [SerializeField, Min(0.01f)] private float revealDuration = 3f;

    // Stores the runtime material and current reveal state.
    private Material portalMaterial;
    private float reveal;
    private bool manualReplay;

    private void Start()
    {
        // Finds the portal renderer and prepares its material for animation.
        if (portalRenderer == null) portalRenderer = GetComponent<Renderer>();
        if (portalRenderer == null) { enabled = false; return; }
        portalMaterial = portalRenderer.material;
        reveal = 0f;
        portalMaterial.SetFloat("_Reveal", reveal);
    }

    private void Update()
    {
        // Lets the demo replay button run the reveal without a nearby player.
        if (manualReplay)
        {
            AnimateReveal(1f);
            if (reveal >= 1f) manualReplay = false;
            return;
        }

        // Automatically finds the object tagged as Player when needed.
        if (player == null)
        {
            GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
            if (playerObject != null) player = playerObject.transform;
        }

        if (player == null) return;

        // Uses squared distance to check the player range efficiently.
        float activationDistanceSquared = activationDistance * activationDistance;
        float playerDistanceSquared = (player.position - transform.position).sqrMagnitude;

        // Plays the reveal backwards after the player leaves the activation range.
        if (playerDistanceSquared > activationDistanceSquared)
        {
            AnimateReveal(-1f);
            return;
        }

        // Reveals the portal while the player is inside the activation range.
        AnimateReveal(1f);
    }

    private void AnimateReveal(float direction)
    {
        // Moves the reveal value forwards or backwards over the chosen duration.
        SetReveal(reveal + direction * Time.deltaTime / revealDuration);
    }

    private void SetReveal(float value)
    {
        // Sends a safe 0-to-1 reveal value to the shader.
        reveal = Mathf.Clamp01(value);
        portalMaterial.SetFloat("_Reveal", reveal);
    }

    public void Replay()
    {
        // Restarts the effect when called by the demo interface.
        manualReplay = true;
        if (portalMaterial != null) SetReveal(0f);
    }

    private void OnDestroy()
    {
        // Removes the runtime material instance when the object is destroyed.
        if (portalMaterial != null) Destroy(portalMaterial);
    }
}
