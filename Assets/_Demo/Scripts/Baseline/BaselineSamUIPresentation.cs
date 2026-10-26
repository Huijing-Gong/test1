using System.Collections;
using UnityEngine;
using SamProduction.BossArena;

// Both entry paths use ArenaHud. This bridge owns only the hosted introduction.
[DefaultExecutionOrder(1100)]
public sealed class BaselineSamUIPresentation : MonoBehaviour
{
    ArenaSession arena;
    GuardianIntroDialogue intro;
    bool introPending=true;
    public bool IntroActive=>introPending||(intro&&intro.IsShowing);
    public bool Ready=>arena&&arena.hud&&arena.hud.Ready;
    public void Initialize(BaselineSamBridge bridge,ArenaSession session){arena=session;}
    IEnumerator Start()
    {
        yield return null;
        try
        {
            if(!Ready||!arena.player)yield break;
            arena.hud.SetHosted(true);
            intro=gameObject.AddComponent<GuardianIntroDialogue>();
            intro.FinalPrompt="[E] CONTINUE · ENTER TO ENGAGE";
            intro.Show(arena.player.transform);
            arena.hud.SetPresentationVisible(!intro.IsShowing);
        }
        finally {introPending=false;}
    }
    void LateUpdate()
    {
        if(!Ready)return;
        if(arena.State!=ArenaRoundState.Ready&&intro&&intro.IsShowing)intro.DismissImmediately();
        arena.hud.SetPresentationVisible(!IntroActive);
    }
    void OnDisable()
    {
        introPending=false;
        if(intro)intro.DismissImmediately();
        if(arena&&arena.hud)arena.hud.SetPresentationVisible(true);
    }
}
