using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace WayfarerInventory.Editor
{
    public static class WorldHeroStopChecks
    {
        static IEnumerator steps;static double next;static Keyboard keyboard;static InputSettings original;
        const string Report="tmp/inventory/hero-stop.txt";
        public static void Run()
        {
            if(!Application.isPlaying)throw new Exception("Play mode required");
            File.WriteAllText(Report,"Walking stop regression\n");
            original=InputSystem.settings;InputSystem.settings=UnityEngine.Object.Instantiate(original);
            InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            keyboard=InputSystem.AddDevice<Keyboard>();steps=Checks();next=0;EditorApplication.update+=Tick;
        }
        static void Tick()
        {
            if(!Application.isPlaying){Finish();return;}if(EditorApplication.timeSinceStartup<next)return;
            try{if(steps.MoveNext()){next=EditorApplication.timeSinceStartup+(float)steps.Current;return;}File.AppendAllText(Report,"ALL PASS\n");Finish();}
            catch(Exception e){File.AppendAllText(Report,"FAIL "+e.Message+"\n");Finish();}
        }
        static void Finish()
        {
            EditorApplication.update-=Tick;if(keyboard!=null)InputSystem.RemoveDevice(keyboard);
            if(original){var temporary=InputSystem.settings;InputSystem.settings=original;UnityEngine.Object.DestroyImmediate(temporary);original=null;}
        }
        static IEnumerator Checks()
        {
            var flow=UnityEngine.Object.FindFirstObjectByType<BaselineFlow>();var ui=flow.GetComponent<InventoryLab>();
            ui.Close();flow.SetInventoryOpen(false);flow.saveKeyPrefix="Stop.Validation.20261025";flow.NewGame();flow.Tutorial.Cancel();
            var title=UnityEngine.Object.FindFirstObjectByType<CloudGateTitleScreen>();if(title)title.Hide();yield return 1f;
            var p=flow.GetComponent<BaselineWorld>().player;var a=p.GetComponentInChildren<Animator>();
            for(int i=0;i<3;i++)
            {
                if(i==2)GameSession.Instance.GrantSword();
                keyboard.MakeCurrent();InputSystem.QueueStateEvent(keyboard,i==1?new KeyboardState(Key.W,Key.LeftShift):new KeyboardState(Key.W));
                yield return 1.2f;
                InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return .05f;
                var stop=p.position;yield return .25f;
                var state=a.GetCurrentAnimatorStateInfo(0);float drift=Vector3.ProjectOnPlane(p.position-stop,Vector3.up).magnitude;
                File.AppendAllText(Report,$"case={i} drift={drift:F4} blend={a.GetFloat("MoveAmount"):F4} state={state.shortNameHash}\n");
                if(state.IsName("RunStop")||state.IsName("WalkStop")||state.IsName("RunStart")||state.IsName("WalkStart"))throw new Exception("Stationary controller still plays a travelling start/stop clip");
                if(drift>.015f||a.GetFloat("MoveAmount")>.08f)throw new Exception("Stop does not settle promptly");
                if(i==0)ScreenCapture.CaptureScreenshot("tmp/inventory/hero-stop-fixed.png");
                yield return .3f;
            }
        }
    }
}
