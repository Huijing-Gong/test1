using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public sealed class PlayerInteraction : MonoBehaviour
{
    private readonly NPCDialogue[] nearbyNPCs = new NPCDialogue[8];
    private PlayerMovement playerMovement;
    private ThirdPersonCamera thirdPersonCamera;
    private DialogueUI dialogueUI;
    private NPCDialogue selectedNPC;

    public NPCDialogue SelectedNPC => selectedNPC;
    public bool HasCurrentCandidate=>SelectBestCandidate()!=null;
    public bool IsDialogueActive => dialogueUI != null && dialogueUI.IsDialogueActive;

    private void Awake()
    {
        playerMovement = GetComponent<PlayerMovement>();
        Camera mainCamera = Camera.main;
        thirdPersonCamera = mainCamera != null ? mainCamera.GetComponent<ThirdPersonCamera>() : null;
        dialogueUI = FindFirstObjectByType<DialogueUI>();
    }

    private void Start()
    {
        ResolveSceneReferences();
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (dialogueUI != null && dialogueUI.IsRewardPresentationActive)
        {
            selectedNPC = null;
            dialogueUI.HidePrompt();
            return;
        }

        if (dialogueUI != null && dialogueUI.IsDialogueActive)
        {
            selectedNPC = null;
            dialogueUI.HidePrompt();

            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                dialogueUI.CancelDialogueAnimated();
            }
            else if (keyboard != null && WorldInputBindings.Pressed("Interact"))
            {
                dialogueUI.AdvanceDialogue();
            }
            else if (keyboard != null && WorldInputBindings.Pressed("Journal"))
            {
                dialogueUI.OpenRioJournal();
            }

            return;
        }

        selectedNPC = SelectBestCandidate();
        if (dialogueUI != null)
        {
            if (selectedNPC != null && playerMovement != null && playerMovement.CanStartInteraction)
            {
                dialogueUI.ShowPrompt(selectedNPC.InteractionPrompt, selectedNPC);
            }
            else
            {
                dialogueUI.HidePrompt();
            }
        }

        if (selectedNPC != null &&
            dialogueUI != null &&
            playerMovement != null &&
            playerMovement.CanStartInteraction &&
            keyboard != null &&
            WorldInputBindings.Pressed("Interact"))
        {
            var captured=selectedNPC;var hero=GetComponent<KingOfGraphic.Hero.HeroWorldAdapter>();
            var actions=GetComponent<WorldHeroActions>();
            if(actions){actions.TryPlay("Talk",()=>{if(captured&&SelectBestCandidate()==captured&&playerMovement.CanStartInteraction)dialogueUI.BeginDialogue(captured,playerMovement,thirdPersonCamera);});}
            else if(hero){hero.PresentInteraction("INTERACT_TOUCH",()=>{if(captured&&SelectBestCandidate()==captured&&playerMovement.CanStartInteraction)dialogueUI.BeginDialogue(captured,playerMovement,thirdPersonCamera);});}
            else dialogueUI.BeginDialogue(captured, playerMovement, thirdPersonCamera);
        }
    }

    public void RegisterCandidate(NPCDialogue npc)
    {
        if (npc == null)
        {
            return;
        }

        int freeIndex = -1;
        for (int i = 0; i < nearbyNPCs.Length; i++)
        {
            if (nearbyNPCs[i] == npc)
            {
                return;
            }

            if (freeIndex < 0 && nearbyNPCs[i] == null)
            {
                freeIndex = i;
            }
        }

        if (freeIndex >= 0)
        {
            nearbyNPCs[freeIndex] = npc;
        }
    }

    public void UnregisterCandidate(NPCDialogue npc)
    {
        for (int i = 0; i < nearbyNPCs.Length; i++)
        {
            if (nearbyNPCs[i] == npc)
            {
                nearbyNPCs[i] = null;
            }
        }

        if (selectedNPC == npc)
        {
            selectedNPC = null;
        }

        if (dialogueUI != null && dialogueUI.ActiveNPC == npc)
        {
            dialogueUI.EndDialogue();
        }
    }

    private NPCDialogue SelectBestCandidate()
    {
        NPCDialogue best = null;
        float bestDistanceSquared = float.PositiveInfinity;
        Vector3 origin = transform.position;
        Vector3 playerForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;

        for (int i = 0; i < nearbyNPCs.Length; i++)
        {
            NPCDialogue npc = nearbyNPCs[i];
            if (npc == null || !npc.gameObject.activeInHierarchy || !npc.IsInteractable)
            {
                nearbyNPCs[i] = null;
                continue;
            }

            Vector3 offset = npc.transform.position - origin;
            Vector3 planarOffset = Vector3.ProjectOnPlane(offset, Vector3.up);
            float distanceSquared = planarOffset.sqrMagnitude;
            float maximumDistance = npc.InteractionDistance;
            // Relocation can disable the player collider without an OnTriggerExit.
            // Release distant entries so repeated returns cannot fill all eight slots.
            if (distanceSquared > maximumDistance * maximumDistance)
            {
                nearbyNPCs[i] = null;
                continue;
            }
            if (distanceSquared <= 0.0001f) continue;

            float forwardDot = Vector3.Dot(playerForward, planarOffset.normalized);
            if (forwardDot < npc.ForwardDotThreshold || distanceSquared >= bestDistanceSquared)
            {
                continue;
            }

            best = npc;
            bestDistanceSquared = distanceSquared;
        }

        return best;
    }

    private void ResolveSceneReferences()
    {
        if (dialogueUI == null)
        {
            dialogueUI = FindFirstObjectByType<DialogueUI>();
        }

        if (thirdPersonCamera == null)
        {
            Camera mainCamera = Camera.main;
            thirdPersonCamera = mainCamera != null ? mainCamera.GetComponent<ThirdPersonCamera>() : null;
        }
    }

    private void OnDisable()
    {
        if (dialogueUI != null)
        {
            dialogueUI.EndDialogue();
            dialogueUI.HidePrompt();
        }

        selectedNPC = null;
        for (int i = 0; i < nearbyNPCs.Length; i++)
        {
            nearbyNPCs[i] = null;
        }
    }
}
