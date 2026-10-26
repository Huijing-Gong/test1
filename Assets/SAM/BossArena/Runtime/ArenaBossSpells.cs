using System;
using System.Collections.Generic;
using UnityEngine;

namespace SamProduction.BossArena
{
    // ArenaSession remains the only gameplay clock and damage authority.
    public sealed class ArenaBossSpells : MonoBehaviour
    {
        [Serializable]
        public sealed class Spell
        {
            public ArenaAttack attack;
            public AnimationClip clip;
            public GameObject effect, chargeEffect, impactEffect;
            [Range(.1f, .9f)] public float releaseFraction = .55f;
            public float damage = 20, radius = 1.4f, visualScale = 1;
        }
        public Spell[] spells = Array.Empty<Spell>();
        readonly Dictionary<ArenaAttack, float> cooldowns = new Dictionary<ArenaAttack, float>();
        readonly List<Hazard> hazards = new List<Hazard>();
        readonly List<Visual> visuals = new List<Visual>();
        ArenaBossBrain owner;
        ArenaSession arena;
        Spell casting;
        Vector3 target, direction;
        bool released;
        int selectionCursor;
        public float CastTime { get; private set; }
        public bool Casting => casting != null;
        public float ReleaseTime => casting != null ? casting.clip.length * casting.releaseFraction : 0;
        public float Duration => casting != null ? Mathf.Max(casting.clip.length, ReleaseTime + .55f) : 0;
        public AnimationClip Clip => casting != null ? casting.clip : null;
        public int HazardCount => hazards.Count;
        public int VisualCount => visuals.Count;
        sealed class Hazard
        {
            public Spell spell;
            public Vector3 point, velocity, heading;
            public float age, delay, lifetime;
            public bool projectile, fired;
            public long strike;
            public Visual visual;
        }
        sealed class Visual
        {
            public GameObject root;
            public ParticleSystem[] particles;
            public float age, lifetime;
        }
        public void Initialize(ArenaBossBrain brain) { owner = brain; }
        public Spell Find(ArenaAttack attack) => Array.Find(spells, s => s != null && s.attack == attack);
        public bool Ready(ArenaAttack attack) => !cooldowns.TryGetValue(attack, out var remaining) || remaining <= 0;
        public bool TryChoose(float distance, out ArenaAttack attack)
        {
            for (int i = 0; i < spells.Length; i++)
            {
                int index = (selectionCursor + i) % spells.Length;
                var s = spells[index];
                if (s == null || !s.clip || !s.effect || !Ready(s.attack) || !ArenaSpellRules.CanSelect(s.attack, distance)) continue;
                selectionCursor = (index + 1) % spells.Length;
                attack = s.attack; return true;
            }
            attack = default; return false;
        }
        public bool Begin(ArenaAttack attack, ArenaSession session)
        {
            var s = Find(attack);
            if (Casting || s == null || !s.clip || !s.effect || !Ready(attack) || session == null || !session.Active || owner.fighter.Dead) return false;
            arena = session; casting = s; CastTime = 0; released = false;
            cooldowns[attack] = ArenaSpellRules.Cooldown(attack);
            target = Ground(session.player.transform.position);
            direction = Vector3.ProjectOnPlane(target - owner.transform.position, Vector3.up).normalized;
            if (direction.sqrMagnitude < .1f) direction = owner.transform.forward;
            if (!ArenaSpellRules.IsMobile(attack)) owner.transform.rotation = Quaternion.LookRotation(direction);
            if (s.chargeEffect) Spawn(s.chargeEffect, owner.Socket("socket_hand.R").position, Quaternion.identity, .35f, ReleaseTime);
            arena.Record("boss_spell_started", attack.ToString(), owner.fighter, 0, target);
            return true;
        }
        public bool AdvanceCast(float dt)
        {
            if (casting == null) return true;
            CastTime += dt;
            if (!released && CastTime >= ReleaseTime) { released = true; Release(casting); }
            return CastTime >= Duration;
        }
        public void EndCast() { casting = null; CastTime = 0; }
        public void Tick(float dt, ArenaSession session)
        {
            arena = session;
            if (arena == null || !arena.Active || owner.fighter.Dead) { ClearEffects(); EndCast(); return; }
            for (int i = 0; i < spells.Length; i++)
                if (spells[i] != null && cooldowns.ContainsKey(spells[i].attack)) cooldowns[spells[i].attack] = Mathf.Max(0, cooldowns[spells[i].attack] - dt);
            for (int i = hazards.Count - 1; i >= 0; i--)
            {
                var h = hazards[i]; h.age += dt;
                if (h.age < h.delay)
                {
                    if(h.spell.attack==ArenaAttack.SwordFall&&h.visual!=null&&h.visual.root)
                        h.visual.root.transform.position=h.point+Vector3.up*(7*(1-h.age/h.delay));
                    continue;
                }
                if (!h.fired)
                {
                    h.fired = true;
                    if(h.spell.attack==ArenaAttack.SwordFall){Expire(h.visual);h.visual=Spawn(h.spell.impactEffect,h.point,Quaternion.identity,h.spell.visualScale,2);}
                    else h.visual = Spawn(h.spell.effect, h.point, Quaternion.LookRotation(h.heading), h.spell.visualScale, h.lifetime + 2);
                    arena.Record("boss_spell_impact", h.spell.attack.ToString(), owner.fighter, h.strike, h.point);
                }
                if (h.projectile)
                {
                    var before = h.point; h.point += h.velocity * dt;
                    // Swept volumes prevent a fast projectile tunnelling through the player.
                    bool wall = Physics.Linecast(before, h.point, (1 << 8), QueryTriggerInteraction.Ignore);
                    int hit = wall ? 0 : arena.StrikeCapsule(owner.fighter, h.strike, h.spell.attack.ToString(), before, h.point, h.spell.radius, h.spell.damage, 2, "boss_spell_projectile");
                    // A lethal Receive synchronously ends the round and clears this list (the host may even retry).
                    if (i >= hazards.Count || hazards[i] != h) return;
                    if (h.visual != null && h.visual.root) h.visual.root.transform.position = h.point;
                    if (wall || hit > 0 || h.age >= h.delay + h.lifetime)
                    {
                        // A close orb reaches the player quickly; retain its burst briefly
                        // so a valid hit does not erase the entire visible effect in one frame.
                        if(hit>0&&h.spell.attack==ArenaAttack.ThunderOrb&&h.visual!=null)
                            h.visual.lifetime=h.visual.age+.3f;
                        else Expire(h.visual);
                        if(hit>0&&h.spell.chargeEffect)Spawn(h.spell.chargeEffect,h.point,Quaternion.identity,.7f,.3f);
                        hazards.RemoveAt(i);
                    }
                }
                else
                {
                    arena.StrikeRing(owner.fighter, h.strike, h.spell.attack.ToString(), h.point, h.spell.radius, 0, h.spell.damage, 3);
                    if (i >= hazards.Count || hazards[i] != h) return;
                    hazards.RemoveAt(i);
                }
            }
            for (int i = visuals.Count - 1; i >= 0; i--)
            {
                var v = visuals[i]; v.age += dt;
                if (!v.root || v.age >= v.lifetime) { if (v.root) { v.root.SetActive(false); Destroy(v.root); } visuals.RemoveAt(i); continue; }
                foreach (var ps in v.particles) if (ps) { ps.Simulate(dt, false, false, true); ps.Pause(false); }
            }
        }
        void Release(Spell s)
        {
            var origin = owner.transform.position;
            arena.Record("boss_spell_released", s.attack.ToString(), owner.fighter, 0, origin);
            switch (s.attack)
            {
                case ArenaAttack.FireWave:
                    Projectile(s, Ground(origin) + Vector3.up * .8f + direction, direction * 8, 2.6f); break;
                case ArenaAttack.FireBird:
                    Projectile(s, owner.Socket("socket_hand.R").position + direction * .5f, direction * 10, 2.3f); break;
                case ArenaAttack.LightningCall:
                    Area(s, target, .45f); break;
                case ArenaAttack.GroundRift:
                    // Aim at the player's release-time position, then commit all five spikes.
                    // Each hazard retains its own heading even if another spell starts later.
                    direction=Vector3.ProjectOnPlane(arena.player.transform.position-origin,Vector3.up).normalized;
                    if(direction.sqrMagnitude<.01f)direction=owner.transform.forward;
                    var start = Ground(origin);
                    for (int i = 1; i <= 5; i++) Area(s, Ground(start + direction * (i * 2f)), .35f + (i - 1) * .24f);
                    break;
                case ArenaAttack.SwordFall:
                    Area(s, target, .85f); break;
                case ArenaAttack.ThunderOrb:
                    // F is deliberately short-lived/short-travel: it belongs to the melee pool.
                    Projectile(s, origin + Vector3.up * 1.1f + direction * .8f, direction * 1.5f, 1.8f); break;
                case ArenaAttack.DelayedBlast:
                    // The downstroke IS the explosion: no second fuse after the hand lands.
                    Area(s, Ground(origin + Vector3.ClampMagnitude(target - origin, 3f)), 0); break;
            }
        }
        void Projectile(Spell s, Vector3 p, Vector3 velocity, float life)
        {
            hazards.Add(new Hazard { spell = s, point = p, velocity = velocity, heading=velocity.normalized, projectile = true, lifetime = life, strike = arena.NewStrike() });
        }
        void Area(Spell s, Vector3 p, float delay)
        {
            // A local particle charge supplies anticipation, without restoring the removed ground outlines.
            if (s.chargeEffect && delay > 0) Spawn(s.chargeEffect, p + Vector3.up * .15f, Quaternion.identity, .45f, delay);
            var h=new Hazard { spell = s, point = p, heading=direction, delay = delay, lifetime = 1.8f, strike = arena.NewStrike() };
            if(s.attack==ArenaAttack.SwordFall)h.visual=Spawn(s.effect,p+Vector3.up*7,Quaternion.identity,s.visualScale,delay+.1f);
            hazards.Add(h);
        }
        Vector3 Ground(Vector3 p)
        {
            return Physics.Raycast(p + Vector3.up * 3, Vector3.down, out var hit, 12, 1 << 8, QueryTriggerInteraction.Ignore) ? hit.point : p;
        }
        Visual Spawn(GameObject prefab, Vector3 p, Quaternion q, float scale, float lifetime)
        {
            if (!prefab) return null;
            var root = Instantiate(prefab, p, q); root.name = "Boss spell FX: " + prefab.name;
            root.transform.localScale *= scale;
            var v = new Visual { root = root, particles = root.GetComponentsInChildren<ParticleSystem>(true), lifetime = lifetime };
            foreach (var ps in v.particles) { var main = ps.main; main.stopAction = ParticleSystemStopAction.None; main.useUnscaledTime = false; ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear); ps.Simulate(0, false, true); ps.Pause(false); }
            visuals.Add(v); return v;
        }
        void Expire(Visual v) { if (v != null) v.lifetime = v.age; }
        void ClearEffects()
        {
            hazards.Clear();
            foreach (var v in visuals) if (v.root) { v.root.SetActive(false); Destroy(v.root); }
            visuals.Clear();
        }
        public void ResetSpells() { ClearEffects(); EndCast(); cooldowns.Clear(); selectionCursor = 0; }
        void OnDisable() { ResetSpells(); }
    }
}
