using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SamProduction.BossArena
{
    // Opt-in evidence from the unmodified hardware-input loop; never drives gameplay.
    public sealed class ArenaPlaytestRecorder : MonoBehaviour
    {
        public ArenaSession arena;
        [Serializable] private sealed class Sample
        {
            public float elapsed;
            public double battleTime;
            public string state, playerAction, bossAction, keys;
            public bool focused, lockedOn;
            public Vector3 player, boss;
            public float playerHealth, bossHealth;
        }
        [Serializable] private sealed class Report
        {
            public string status="observed_hardware_playtest_pending_review";
            public string source="Observed Unity InputSystem keyboard and mouse; ArenaSession owns all real input, simulation and damage.";
            public Sample[] samples;
            public string[] errors;
        }
        private readonly List<Sample> samples=new List<Sample>();
        private readonly List<string> errors=new List<string>();
        private string output;
        private float start, nextSample, nextWrite;
        private void Start()
        {
            var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"--arena-live-output");
            if(i<0||i+1>=args.Length){enabled=false;return;}
            output=Path.GetFullPath(args[i+1]);Directory.CreateDirectory(output);
            start=Time.realtimeSinceStartup;Application.logMessageReceived+=Log;
        }
        private void LateUpdate()
        {
            float t=Time.realtimeSinceStartup-start;
            var keys=new List<string>();var k=Keyboard.current;var m=Mouse.current;
            if(k!=null)foreach(var key in new[]{Key.W,Key.A,Key.S,Key.D,Key.LeftShift,Key.Space,Key.Enter,Key.Tab,Key.R,Key.Escape})
                if(k[key].isPressed||k[key].wasPressedThisFrame)keys.Add(key.ToString());
            if(m!=null&&m.leftButton.wasPressedThisFrame)keys.Add("LMB");
            if(t>=nextSample||keys.Count>0)
            {
                nextSample=t+.1f;
                samples.Add(new Sample{elapsed=t,battleTime=arena.BattleTime,state=arena.State.ToString(),
                    playerAction=arena.player.ActionName,bossAction=arena.boss.ActionName,keys=string.Join("+",keys),
                    focused=Application.isFocused,lockedOn=arena.view.LockedOn,player=arena.player.transform.position,boss=arena.boss.transform.position,
                    playerHealth=arena.player.fighter.Health,bossHealth=arena.boss.fighter.Health});
            }
            if(t>=nextWrite){nextWrite=t+2;Save();}
        }
        private void Save()
        {
            if(output==null)return;
            File.WriteAllText(Path.Combine(output,"hardware_playtest.json"),JsonUtility.ToJson(new Report{samples=samples.ToArray(),errors=errors.ToArray()},true));
        }
        private void Log(string message,string stack,LogType type)
        {if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)errors.Add(message+"\n"+stack);}
        private void OnApplicationQuit(){Save();}
        private void OnDestroy(){Application.logMessageReceived-=Log;}
    }
}
