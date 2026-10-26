using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SamProduction.BossArena
{
    public enum ArenaRoundState { Ready, Fighting, Victory, Defeat }

    [Serializable]
    public sealed class ArenaEventRecord
    {
        public double time;
        public string kind, skill, source, target;
        public long strike;
        [NonSerialized] internal bool blackSunOwned; // Immutable presentation ownership for this release.
        public float health;
        public Vector3 point;
    }

    [Serializable] public sealed class ArenaStrikeObservation
    {
        public double battleTime;
        public long strike;
        public string skill, queryTag, source, target;
        public Vector3 a, b, bodyCentre;
        public float radius, bodyRadius, bodyHeight, healthBefore, healthAfter;
        public bool intersects, accepted, invulnerable;
    }

    public sealed class ArenaSession : MonoBehaviour
    {
        public ArenaPlayerController player;
        public ArenaBossBrain boss;
        public ArenaThirdPersonCamera view;
        public ArenaCombatFx effects;
        public ArenaHud hud;
        public ArenaAudio sound;
        public ArenaDefenseFeedback defenseFeedback;
        public bool automaticTick = true;
        [NonSerialized] public bool HostPresentationLocked;
        // Host owns ordering when embedded; standalone keeps its existing controls.
        public Func<bool> HostRetry;
        public Vector3 playerStart = new Vector3(0, .05f, -5);
        public Vector3 bossStart = new Vector3(0, .05f, 4);
        public ArenaRoundState State { get; private set; } = ArenaRoundState.Ready;
        public bool Active => State == ArenaRoundState.Fighting;
        public bool CanAcceptEngageOrRetry
        {
            get
            {
                if(HostPresentationLocked || Active)return false;
                if(State!=ArenaRoundState.Defeat)return true;
                var character=player?player.GetComponent<ArenaCurrentCharacter>():null;
                return !character||character.DeathPresentationComplete;
            }
        }
        public double BattleTime { get; private set; }
        public readonly List<ArenaEventRecord> Events = new List<ArenaEventRecord>();
        // Opt-in QA observes the same real query/Receive, with no allocation or
        // alternate collision path when no reviewer subscribes.
        public event Action<ArenaStrikeObservation> StrikeObserved;
        public long AttemptId => roundOutcomes.AttemptId;
        public event Action<ArenaRoundOutcome> RoundEnded
        {
            add { roundOutcomes.RoundEnded += value; }
            remove { roundOutcomes.RoundEnded -= value; }
        }
        private readonly ArenaRoundOutcomeChannel roundOutcomes =
            new ArenaRoundOutcomeChannel(Debug.LogException);
        private long strikeSerial;
        private double previousInputWall=-1;
        private long soundSerial;
        private readonly Collider[] contacts = new Collider[64];

        private void Start()
        {
            Application.targetFrameRate=60;
            if (defenseFeedback == null && effects != null)
                defenseFeedback = effects.GetComponent<ArenaDefenseFeedback>() ?? effects.gameObject.AddComponent<ArenaDefenseFeedback>();
            defenseFeedback?.UseMaterials(effects);
            if (defenseFeedback != null && player.avatar != null)
                defenseFeedback.blockContactAnchor = player.DefenseContactAnchor;
            if (defenseFeedback != null && !defenseFeedback.Warmup())
                Debug.LogWarning("SAM_DEFENSE_FEEDBACK_WARMUP_FAILED: verify arena materials");
            player.fighter.Damaged += OnDamage;
            boss.fighter.Damaged += OnDamage;
            player.fighter.Avoided += OnAvoid;
            player.fighter.DefenseResolved += OnDefense;
            boss.fighter.Avoided += OnAvoid;
            player.fighter.Died += PlayerDied;
            boss.fighter.Died += BossDied;
            sound?.Bind(this);
            Restart(false);
            if(boss.PatrolPlanner!=null&&boss.PatrolPlanner.Motor!=null)
            {
                bool prepared=boss.PatrolPlanner.Motor.Warmup();
                Debug.Log("SAM_PATROL_PREPARE success="+prepared+" milliseconds="+boss.PatrolPlanner.Motor.PreparationMilliseconds.ToString("F3"));
                if(!prepared)Debug.LogWarning("SAM_PATROL_PREPARE_FAILED: source/rig binding remains unavailable.");
            }
            hud?.Build(this, view.GetComponent<Camera>());
            if(HostPresentationLocked)hud?.SetPresentationVisible(false);
        }

        private void OnDestroy()
        {
            roundOutcomes.Dispose();
            if (player != null && player.fighter != null)
            {
                player.fighter.Damaged -= OnDamage;
                player.fighter.Avoided -= OnAvoid;
                player.fighter.DefenseResolved -= OnDefense;
                player.fighter.Died -= PlayerDied;
            }
            if (boss != null && boss.fighter != null)
            {
                boss.fighter.Damaged -= OnDamage;
                boss.fighter.Avoided -= OnAvoid;
                boss.fighter.Died -= BossDied;
            }
        }

        private void Update()
        {
            double now=Time.realtimeSinceStartupAsDouble;
            double elapsed=previousInputWall<0?Time.unscaledDeltaTime:now-previousInputWall;
            previousInputWall=now;
            if (!automaticTick) return;
            if (!Application.isFocused) { player.SuspendDefense(); defenseFeedback?.ResetFeedback(); return; }
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
            if (keyboard != null && keyboard.rKey.wasPressedThisFrame && Active && Time.timeScale>0f && Cursor.lockState==CursorLockMode.Locked) view?.RecenterView();
            if (keyboard != null && keyboard.enterKey.wasPressedThisFrame && CanAcceptEngageOrRetry) { if (HostRetry != null) HostRetry(); else Restart(true); }
            if (mouse != null && mouse.rightButton.wasPressedThisFrame && Active)
            { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
            ArenaCommand command = default;
            if (Active)
            {
                var intent=KingOfGraphic.Hero.HeroCombatInput.Read(Time.timeScale>0f&&Cursor.lockState==CursorLockMode.Locked);
                command.run=intent.run;command.dodge=intent.dodge;
                if(keyboard!=null){
                    command.move=new Vector2((keyboard.dKey.isPressed?1:0)-(keyboard.aKey.isPressed?1:0),(keyboard.wKey.isPressed?1:0)-(keyboard.sKey.isPressed?1:0));
                    // Combat bindings are shared with the overworld. Space remains a legacy arena alias.
                    command.dodge=intent.dodge||(!(player.Hero&&player.Hero.library.authoredControls)&&Time.timeScale>0f&&Cursor.lockState==CursorLockMode.Locked&&keyboard.spaceKey.wasPressedThisFrame);
                }
                command.author=KingOfGraphic.Hero.HeroAuthorInput.Read(Time.timeScale>0f&&Cursor.lockState==CursorLockMode.Locked&&player.Hero&&player.Hero.library.authoredControls);
                if(GetComponent<ArenaPracticeControls>()?.Active==true)command.author.combo=0;
                command.guardHeld=intent.guard;command.guardPressed=intent.guardPressed;
                command.toggleLock=intent.toggleLock;command.attack=intent.attack;
                command.attackHeld=intent.held;command.attackReleased=intent.released;
                command.look=mouse!=null&&Cursor.lockState==CursorLockMode.Locked?mouse.delta.ReadValue():Vector2.zero;
            }
            SimulateElapsed(command,elapsed*Time.timeScale);
        }

        // Consume the actual elapsed frame interval without dropping long-frame
        // time. Edges and mouse displacement belong to the first step only.
        // Focus loss retains the existing pause policy and resets the clock via
        // Update, so returning to the game does not replay time spent away.
        public void SimulateElapsed(ArenaCommand command,double elapsed)
        {
            if(elapsed<0||double.IsNaN(elapsed)||double.IsInfinity(elapsed))
                throw new ArgumentOutOfRangeException(nameof(elapsed));
            while(elapsed>1e-10)
            {
                float step=(float)Math.Min(elapsed,1d/120);
                elapsed-=step;Simulate(command,step);command=command.WithoutEdges();
            }
        }

        public void Restart(bool begin = true)
        {
            if(begin && HostPresentationLocked)return;
            if (!roundOutcomes.Restart(begin)) return;
            State = begin ? ArenaRoundState.Fighting : ArenaRoundState.Ready;
            BattleTime = 0;
            Events.Clear();
            strikeSerial = 0;
            soundSerial = 0;
            sound?.ResetAudio();
            player.fighter.ResetFighter();
            boss.fighter.ResetFighter();
            player.ResetPlayer(playerStart, Quaternion.identity, Keyboard.current != null && Keyboard.current.qKey.isPressed);
            boss.ResetBoss(bossStart, Quaternion.Euler(0, 180, 0));
            effects?.ResetEffects();
            defenseFeedback?.ResetFeedback();
            view.ResetCamera();
            Record(begin ? "battle_started" : "ready", "", null, 0, Vector3.zero);
            if (automaticTick)
            { Cursor.lockState = begin ? CursorLockMode.Locked : CursorLockMode.None; Cursor.visible = !begin; }
            Physics.SyncTransforms();
        }

        // Hardware and repeatable QA drive the same simulation, including all collision and damage.
        public void Simulate(ArenaCommand command, float delta)
        {
            if(HostPresentationLocked)return;
            if (delta < 0 || float.IsNaN(delta) || float.IsInfinity(delta) || delta > .25f)
                throw new ArgumentOutOfRangeException(nameof(delta));
            float remaining = delta;
            while (remaining > .000001f)
            {
                float step = Mathf.Min(1f / 120, remaining);
                if (Active) BattleTime += step;
                player.Tick(Active ? command : default, step, this);
                boss.Tick(step * player.FocusEnemyScale, this);
                if (view != null && view.CinematicActive) defenseFeedback?.ResetFeedback();
                effects?.Tick(step * player.FocusEnemyScale);
                defenseFeedback?.Advance(step);
                remaining -= step;
                command = command.WithoutEdges();
            }
            sound?.Tick(BattleTime);
        }

        public long NewStrike() => ++strikeSerial;

        public int StrikeCapsule(ArenaFighter source, long strike, string skill, Vector3 a, Vector3 b,
            float radius, float damage, float push, string queryTag = "")
        {
            if (!Active || source == null || source.Dead) return 0;
            Physics.SyncTransforms();
            int count = Physics.OverlapCapsuleNonAlloc(a, b, radius, contacts,
                (1 << 9) | (1 << 10), QueryTriggerInteraction.Ignore);
            if (count == contacts.Length) throw new InvalidOperationException("Arena contact buffer exhausted");
            count = IncludeCombatantCapsules(count);
            int hits = 0;
            for (int i = 0; i < count; i++)
            {
                var target = contacts[i].GetComponentInParent<ArenaFighter>();
                if (target == null || target == source || target.team == source.team) continue;
                // A query candidate is not sufficient: moving CharacterControllers were
                // returned by short capsule queries even beyond the damage radius.
                // Confirm the actual swept fist/sword against the current body capsule.
                var character = contacts[i] as CharacterController;
                bool intersects = character == null || ArenaContactGeometry.Intersects(a, b, radius, character);
                ArenaStrikeObservation observation = null;
                if (StrikeObserved != null) observation = new ArenaStrikeObservation { battleTime = BattleTime, strike = strike,
                    skill = skill, queryTag = queryTag, source = source.team.ToString(), target = target.team.ToString(),
                    a = a, b = b, radius = radius, healthBefore = target.Health, healthAfter = target.Health,
                    invulnerable = target.Invulnerable, intersects = intersects,
                    bodyCentre = character != null ? character.transform.TransformPoint(character.center) : contacts[i].bounds.center,
                    bodyRadius = character != null ? character.radius * Mathf.Max(Mathf.Abs(character.transform.lossyScale.x), Mathf.Abs(character.transform.lossyScale.z)) : 0,
                    bodyHeight = character != null ? character.height * Mathf.Abs(character.transform.lossyScale.y) : 0 };
                if (!intersects) { if (observation != null) StrikeObserved?.Invoke(observation); continue; }
                Vector3 point = contacts[i].ClosestPoint((a + b) * .5f);
                bool accepted = target.Receive(new ArenaHit { source = source, strike = strike, damage = damage, push = push,
                    skill = skill, point = point, direction = target.transform.position - source.transform.position });
                if (observation != null) { observation.accepted = accepted; observation.healthAfter = target.Health; StrikeObserved?.Invoke(observation); }
                if (accepted)
                {
                    hits++;
                    if(source==player.fighter)player.GetComponentInChildren<KingOfGraphic.Hero.HeroAuthorSwordEffects>(true)?.Contact(point,target.transform.position-source.transform.position);
                    if(skill=="drive_punch" && Application.isBatchMode)
                        Debug.Log("ARENA_PUNCH_CONTACT time="+BattleTime.ToString("F5")+" a="+a.ToString("F4")+" b="+b.ToString("F4")
                            +" radius="+radius+" target="+target.transform.position.ToString("F4")+" boundsCentre="+contacts[i].bounds.center.ToString("F4")
                            +" boundsExtents="+contacts[i].bounds.extents.ToString("F4")+" source="+source.transform.position.ToString("F4"));
                }
            }
            return hits;
        }

        public int StrikeRing(ArenaFighter source, long strike, string skill, Vector3 centre,
            float outerRadius, float innerRadius, float damage, float push)
        {
            if (!Active || source.Dead) return 0;
            Physics.SyncTransforms();
            int count = Physics.OverlapCapsuleNonAlloc(centre + Vector3.up * .1f, centre + Vector3.up * 1.3f,
                outerRadius, contacts, (1 << 9) | (1 << 10), QueryTriggerInteraction.Ignore);
            if (count == contacts.Length) throw new InvalidOperationException("Arena ring contact buffer exhausted");
            count = IncludeCombatantCapsules(count);
            int hits = 0;
            for (int i = 0; i < count; i++)
            {
                var target = contacts[i].GetComponentInParent<ArenaFighter>();
                if (target == null || target == source || target.team == source.team) continue;
                var character = contacts[i] as CharacterController;
                Vector3 bodyCentre = character != null ? character.transform.TransformPoint(character.center) : contacts[i].bounds.center;
                float bodyRadius = character != null ? character.radius * Mathf.Max(Mathf.Abs(character.transform.lossyScale.x), Mathf.Abs(character.transform.lossyScale.z))
                    : new Vector2(contacts[i].bounds.extents.x, contacts[i].bounds.extents.z).magnitude;
                Vector3 radial = Vector3.ProjectOnPlane(bodyCentre - centre, Vector3.up);
                float distance = radial.magnitude;
                // A capsule straddling either edge intersects the ring even when its nearest point is inside the hole.
                if (distance + bodyRadius < innerRadius || distance - bodyRadius > outerRadius || contacts[i].bounds.min.y > centre.y + .5f) continue;
                Vector3 nearest = contacts[i].ClosestPoint(centre + (distance > .0001f ? radial / distance : Vector3.forward) * Mathf.Clamp(distance, innerRadius, outerRadius) + Vector3.up * .7f);
                if (target.Receive(new ArenaHit { source = source, strike = strike, damage = damage, push = push,
                    skill = skill, point = nearest, direction = target.transform.position - centre })) hits++;
            }
            return hits;
        }

        private int IncludeCombatantCapsules(int count)
        {
            // A4's real movement trace showed native queries omitting a moved
            // controller even with the fist inside its current body capsule.
            // Include the encounter's registered bodies as candidates, then use
            // the same exact capsule/ring geometry, teams and strike dedup below.
            // This adds no reach and does not manufacture a successful Receive.
            count = IncludeCandidate(count, player != null ? player.capsule : null);
            return IncludeCandidate(count, boss != null ? boss.capsule : null);
        }

        private int IncludeCandidate(int count, CharacterController body)
        {
            if(body == null || !body.enabled || !body.gameObject.activeInHierarchy) return count;
            for(int i = 0; i < count; i++) if(contacts[i] == body) return count;
            if(count == contacts.Length) throw new InvalidOperationException("Arena registered contact buffer exhausted");
            contacts[count] = body;
            return count + 1;
        }

        public void Impact(Vector3 point, bool successful, ArenaTeam source, Vector3 direction = default)
        { effects?.Impact(point, successful, source, direction); }

        public void Record(string kind, string skill, ArenaFighter source, long strike, Vector3 point)
        {
            Events.Add(new ArenaEventRecord { time = BattleTime, kind = kind, skill = skill,
                source = source == null ? "arena" : source.team.ToString(), strike = strike, point = point });
            if(sound == null) return;
            ArenaSound? cue = null;
            if(kind == "boss_attack_started" && skill != "dash_punch") cue = skill == "Stomp" ? ArenaSound.HeavyCharge : ArenaSound.Preload;
            else if(kind == "boss_ground_contact") cue = ArenaSound.StompGround;
            else if(kind == "boss_recovery_started" && skill != "dash_punch") cue = ArenaSound.Vent;
            else if(kind == "player_dodge_started") cue = ArenaSound.Dodge;
            else if(kind == "boss_foot_contact") cue = ArenaSound.StepHeavy;
            if(cue.HasValue) EmitSound(cue.Value, point, strike,
                kind == "boss_attack_started" ? boss.Socket(skill == "Stomp" ? "socket_foot.R" : "socket_hand.R") : null);
            if(kind == "boss_ground_contact") EmitSound(ArenaSound.EmberTail, point, strike);
        }

        public void EmitSound(ArenaSound cue, Vector3 point, long group = 0, Transform follow = null)
        { sound?.Emit(cue, point, BattleTime, ++soundSerial, follow, 1, group); }

        private void OnDamage(ArenaHit hit)
        {
            Events.Add(new ArenaEventRecord { time = BattleTime, kind = "hit", skill = hit.skill,
                source = hit.source.team.ToString(), target = hit.target.team.ToString(), strike = hit.strike,
                health = hit.target.Health, point = hit.point });
            // Dash release already owns its one visible flame burst, including misses.
            // Retain contact sparks here without spawning that burst again on a hit.
            if (!hit.target.LastDefense.IsBlock)
                effects?.Impact(hit.point, true, hit.source.team, hit.direction, hit.skill == "dash_punch");
        }

        private void OnAvoid(ArenaHit hit)
        {
            Events.Add(new ArenaEventRecord { time = BattleTime, kind = "dodge", skill = hit.skill,
                source = hit.source.team.ToString(), target = hit.target.team.ToString(), strike = hit.strike,
                health = hit.target.Health, point = hit.point });
            if (hit.target.team != ArenaTeam.Player || !player.DodgeState.Active)
                effects?.Impact(hit.point, false, ArenaTeam.Player);
        }

        private void OnDefense(ArenaDefenseEvent value)
        {
            string type;
            switch (value.result.outcome)
            {
                case ArenaDefenseOutcome.Dodge: type = ArenaDefenseFeedback.TypeDodge; break;
                case ArenaDefenseOutcome.PerfectDodge: type = ArenaDefenseFeedback.TypePerfectDodge; break;
                case ArenaDefenseOutcome.Block: type = ArenaDefenseFeedback.TypeBlock; break;
                case ArenaDefenseOutcome.PerfectBlock: type = ArenaDefenseFeedback.TypePerfectBlock; break;
                default: return;
            }
            defenseFeedback?.ShowDefense(value.hit.strike, type, value.hit.point, value.hit.direction);
            Events.Add(new ArenaEventRecord { time = BattleTime, kind = "defense_" + value.result.outcome.ToString().ToLowerInvariant(),
                skill = value.hit.skill, source = value.hit.source.team.ToString(), target = value.hit.target.team.ToString(),
                strike = value.hit.strike, health = value.healthAfter, point = value.hit.point });
        }

        private void OnApplicationFocus(bool focused)
        {
            previousInputWall = -1;
            if (!focused && player != null) { player.SuspendDefense(); defenseFeedback?.ResetFeedback(); }
        }

        private void PlayerDied()
        {
            if (player == null || player.fighter == null || !player.fighter.Dead) return;
            ResolveRound(ArenaRoundState.Defeat);
        }
        private void BossDied()
        {
            if (boss == null || boss.fighter == null || !boss.fighter.Dead) return;
            ResolveRound(ArenaRoundState.Victory);
        }
        private void ResolveRound(ArenaRoundState result)
        {
            if (State != ArenaRoundState.Fighting ||
                !roundOutcomes.TryResolve(result, BattleTime, out ArenaRoundOutcome outcome)) return;
            State = result;
            // No session writes after delivery: the host may retry or return
            // synchronously. A real result also survives a cleanup exception.
            try { Finish(); }
            finally { roundOutcomes.Publish(outcome); }
        }
        private void Finish()
        {
            player.SuspendDefense(); defenseFeedback?.ResetFeedback();
            Record(State.ToString().ToLowerInvariant(), "", null, 0, Vector3.zero);
            boss.CancelAttack();
            if (automaticTick) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
        }
    }
}
