using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class BattleTrialMenu
{
    void BuildIllustratedResult()
    {
        bool won=manager.PlayerHasWon;
        int stage=Mathf.Clamp(manager.CurrentTrialIndex,0,Names.Length-1);
        var air=Rect("Result floating flecks",content,0,0,1920,1080).gameObject.AddComponent<BattleTrialMenuAtmosphere>();
        air.raycastTarget=false;
        if(resultHero && won)
        {
            var hero=Picture("Result ranger",content,resultHero,170,55,670,900);
            // Aspect fitted independently: transparent art must never stretch.
            var fit=hero.gameObject.AddComponent<AspectRatioFitter>();
            fit.aspectMode=AspectRatioFitter.AspectMode.FitInParent;fit.aspectRatio=(float)resultHero.width/resultHero.height;
            // Put the fitter inside a fixed frame rather than fitting the full canvas.
            var frame=Rect("Ranger frame",content,145,45,730,910);
            hero.rectTransform.SetParent(frame,false);
            hero.rectTransform.anchorMin=Vector2.zero;hero.rectTransform.anchorMax=Vector2.one;
            hero.rectTransform.offsetMin=hero.rectTransform.offsetMax=Vector2.zero;
            motion.Reveal(frame,.03f,new Vector2(-35,0),.38f);
        }
        else
        {
            var team=manager.GetComponent<BattleTeamHud>();
            if(team && team.fighters.Count>0)
            {
                var frame=Rect("Defeat portrait",content,310,280,400,400);
                var circle=frame.gameObject.AddComponent<BattleHudShape>();circle.shape=BattleHudShape.Kind.Circle;
                circle.color=Ink;circle.rim=Gold;circle.stroke=6;circle.raycastTarget=false;
                frame.gameObject.AddComponent<Mask>().showMaskGraphic=true;
                var portrait=Rect("Defeat face",frame,10,10,380,380).gameObject.AddComponent<Image>();
                portrait.sprite=team.fighters[0].portrait;portrait.preserveAspect=true;portrait.raycastTarget=false;
                motion.Reveal(frame,.03f,new Vector2(-25,0),.35f);
            }
            Paper("Defeat encouragement backing",content,150,705,720,74,Ink,false,1).outline=Bronze;
            Text("Defeat encouragement",content,"STAY IN THE FIGHT",170,722,680,45,36,Cream,TextAlignmentOptions.Center).fontStyle=FontStyles.Bold;
        }
        ResultPortraits(won);
        var panel=Card("Illustrated result paper",content,1020,55,820,970,Cream);
        Text("Result heading",panel,won?"VICTORY":"DEFEAT",35,36,750,119,91,
            won?Ink:new Color32(141,66,50,255),TextAlignmentOptions.Center).fontStyle=FontStyles.Bold;
        Text("Result subtitle",panel,won?"TRIAL COMPLETE":"RISE AND TRY AGAIN",55,157,710,39,28,Muted,TextAlignmentOptions.Center).characterSpacing=4;
        Paper("Result stage chip",panel,62,213,365,44,new Color32(239,229,203,255),false,1).outline=Bronze;
        Text("Result stage",panel,Names[stage].ToUpperInvariant(),70,220,349,31,23,Ink,TextAlignmentOptions.Center).fontStyle=FontStyles.Bold;
        Paper("Result difficulty chip",panel,452,213,306,44,new Color32(166,211,190,255),false,0);
        Text("Result difficulty",panel,Difficulties[stage],460,220,290,31,23,Ink,TextAlignmentOptions.Center).fontStyle=FontStyles.Bold;
        Ornament("Result stats rule",panel,BattleTrialOrnament.Motif.Rule,58,279,704,12,Ink);
        Text("Time label",panel,"TIME",65,309,330,28,21,Ink,TextAlignmentOptions.Center).fontStyle=FontStyles.Bold;
        int seconds=Mathf.FloorToInt(manager.BattleDuration);
        Text("Result elapsed",panel,$"{seconds/60:00}:{seconds%60:00}",65,341,330,64,48,Ink,TextAlignmentOptions.Center).fontStyle=FontStyles.Bold;
        float damage=0;var player=FindFirstObjectByType<PlayerDamage>();if(player)damage=player.damagePercent;
        Text("Damage label",panel,"FINAL DAMAGE",425,309,330,28,21,Ink,TextAlignmentOptions.Center).fontStyle=FontStyles.Bold;
        Text("Result damage",panel,$"{Mathf.RoundToInt(damage)}%",425,341,330,64,48,Ink,TextAlignmentOptions.Center).fontStyle=FontStyles.Bold;
        var reward=Rect("Result reward",panel,56,420,708,266);
        Paper("Reward border",reward,0,0,708,266,new Color32(250,240,209,255),false,1).outline=new Color(.42f,.55f,.43f,.5f);
        Paper("Reward strip",reward,0,0,708,48,new Color32(185,215,194,255),false,0);
        Text("Reward heading",reward,"FIRST CLEAR REWARD",22,12,664,32,24,Ink,TextAlignmentOptions.Center).fontStyle=FontStyles.Bold;
        Paper("Result icon tile",reward,23,73,137,137,Ink,false,3).outline=Bronze;
        var icon=Picture("Result reward art",reward,rewardIcon,30,80,123,123);
        icon.color=won?Color.white:new Color(.65f,.65f,.65f,.7f);
        rewardName=Heading("Reward name",reward,"",187,70,495,53,34,Ink);
        rewardEffect=Text("Reward effect",reward,"",188,126,485,62,21,Muted);
        rewardState=Text("Reward state",reward,"",188,190,485,32,19,Muted);
        rewardState.fontStyle=FontStyles.Bold;
        ClaimButton=Button("Claim",panel,"CLAIM FIRST REWARD",244,643,490,40,ClaimReward,false);
        ClaimButton.GetComponentInChildren<TMP_Text>().fontSize=20;
        Button("Retry",panel,"RETRY   >",58,706,704,80,manager.RestartBattle,true);
        Button("Select",panel,"TRIAL SELECT [RMB]   >",104,801,612,60,manager.ReturnToTrialSelection,false);
        Button("Return",panel,"RETURN TO MAIN WORLD",150,877,520,42,ReturnToMain,false).GetComponentInChildren<TMP_Text>().fontSize=21;
        notice=Text("Result note",panel,"",45,933,730,33,17,Muted,TextAlignmentOptions.Center);
    }

    void ResultPortraits(bool won)
    {
        var team=manager.GetComponent<BattleTeamHud>();
        if(!team)return;
        int count=Mathf.Min(team.fighters.Count,2);
        for(int i=0;i<count;i++)
        {
            var fighter=team.fighters[i];float x=265+i*290;
            var frame=Rect("Result portrait "+i,content,x,850,180,180);
            var rim=frame.gameObject.AddComponent<BattleHudShape>();rim.shape=BattleHudShape.Kind.Circle;
            rim.color=new Color32(247,236,205,255);rim.raycastTarget=false;
            var clip=Rect("Portrait mask",frame,7,7,166,166);
            var circle=clip.gameObject.AddComponent<BattleHudShape>();circle.shape=BattleHudShape.Kind.Circle;circle.color=fighter.identity;circle.raycastTarget=false;
            var mask=clip.gameObject.AddComponent<Mask>();mask.showMaskGraphic=true;
            var portrait=Rect("Portrait",clip,0,0,166,166).gameObject.AddComponent<Image>();
            portrait.sprite=fighter.portrait;portrait.preserveAspect=true;portrait.raycastTarget=false;
            bool winner=i==0?won:!won;
            Paper("Portrait label",frame,-34,150,248,45,winner?Gold:new Color32(239,229,203,255),false,2).outline=Ink;
            Text("Fighter result",frame,i==0?(won?"YOU  WIN":"YOU"):(won?"RIVAL":"RIVAL  WIN"),-28,157,236,31,23,Ink,TextAlignmentOptions.Center).fontStyle=FontStyles.Bold;
            motion.Reveal(frame,.16f+i*.06f,new Vector2(0,-14),.28f);
        }
    }

    void RefreshIllustratedResult()
    {
        if(CurrentView!=View.Result)return;
        bool won=manager.PlayerHasWon;
        bool available=won&&PrimaryReward&&HasHost;
        rewardName.text=!won?"Another chance awaits":PrimaryReward?"Trial A blade core":"Practice complete";
        rewardEffect.text=!won?"Build damage, then push your rival off the stage.":!PrimaryReward?"This stage has no assigned reward.":HasHost?Host.SideATrialRewardEffect:"Practice preview. Rewards are not saved.";
        rewardState.text=!won?"NO REWARD THIS ROUND":!PrimaryReward?"NO SAVED REWARD":!HasHost?"PRACTICE ONLY":Claimed?"REWARD RECEIVED":"FIRST CLEAR REWARD READY";
        rewardState.color=available&&Claimed?new Color32(48,119,88,255):Muted;
        ClaimButton.gameObject.SetActive(available&&!Claimed);
        ClaimButton.interactable=available&&!Claimed&&!Busy;
        if(!resultReceipt)notice.text=available&&!Claimed?"Claim your reward here, or collect it later in the main world.":"Replay freely. Each trial reward can be claimed once.";
    }
}
