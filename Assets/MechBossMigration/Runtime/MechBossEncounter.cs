using UnityEngine;
namespace KingOfGraphic.MechBossMigration
{
    [DefaultExecutionOrder(-200)]
    public sealed class MechBossEncounter:MonoBehaviour
    {
        public PlayerMovement player;
        public MechSentinelBattle boss;
        public GameObject barrier;
        float nextBind;bool bound;
        void Awake(){if(barrier)barrier.SetActive(false);if(boss)boss.enabled=false;}
        void Update()
        {
            if(bound&&player)return;if(Time.unscaledTime<nextBind)return;nextBind=Time.unscaledTime+.25f;
            if(!player)player=FindFirstObjectByType<PlayerMovement>();
            if(!boss||!player||!player.GetComponent<Health>())return;
            boss.BindPlayer(player);boss.enabled=true;bound=true;
        }
        public void ResetEncounter()
        {
            if(!boss||boss.GetComponent<MechCorpseLifecycle>()?.Defeated==true||boss.GetComponent<Health>().IsDead)return;
            boss.GetComponent<MistGardenBossFeedback>().ResetForLab();boss.ResetForLab();
        }
    }
}
