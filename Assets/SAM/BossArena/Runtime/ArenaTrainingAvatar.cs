using UnityEngine;

namespace SamProduction.BossArena
{
    // Editable geometric training character. Pose and weapon geometry never apply damage.
    public sealed class ArenaTrainingAvatar : MonoBehaviour
    {
        public bool renderTrainingGeometry=true;
        public Material suitMaterial, plateMaterial, accentMaterial, bladeMaterial;
        [SerializeField] private Transform visual, hips, abdomen, torso, neck, head, visor, sword;
        public Transform DefenseContactAnchor => sword;
        [SerializeField] private Transform[] upperArms, forearms, hands, thighs, shins, feet, shoulders, elbows, knees;
        private float gait, locomotion, death, recoil, guardBlend;
        private readonly float[] lengths = { .285f, .275f };
        private readonly Vector3[] footWorld = new Vector3[2], stepStart = new Vector3[2], stepTarget = new Vector3[2];
        private readonly Quaternion[] footRotation = new Quaternion[2], stepRotation = new Quaternion[2];
        private readonly float[] stepTime = { -1, -1 }, stepDuration = new float[2];
        private float stepClock;
        private int nextFoot;
        private bool feetReady, wasDashing;
        private const float UpperLeg = .435f, LowerLeg = .435f;
        public Vector3 WeaponBase => sword != null ? sword.TransformPoint(new Vector3(0, 0, .15f)) : transform.position + transform.forward * .4f + Vector3.up;
        public Vector3 WeaponTip => sword != null ? sword.TransformPoint(new Vector3(0, 0, .86f)) : WeaponBase;
        public bool Built => visual != null && sword != null;
        public event System.Action<float, Vector3, bool, float, float, int, bool, bool> Posed;
        public Transform TorsoTarget => torso;
        public Transform HandTarget(int side) => hands[side];
        public Transform ElbowTarget(int side) => elbows[side];

        private void Awake() { if (!Built) Build(); }

        public void Build()
        {
            var previous = transform.Find("TrainingVisual");
            if (previous != null)
            {
                previous.gameObject.SetActive(false);
                if (Application.isPlaying) Destroy(previous.gameObject); else DestroyImmediate(previous.gameObject);
            }
            if(renderTrainingGeometry)EnsureMaterials();
            visual = new GameObject("TrainingVisual").transform;
            visual.SetParent(transform, false);
            hips = Part("Pelvis", PrimitiveType.Cube, suitMaterial, new Vector3(.30f, .21f, .23f));
            abdomen = Part("Abdomen", PrimitiveType.Capsule, suitMaterial, new Vector3(.25f, .135f, .215f));
            torso = Part("Torso", PrimitiveType.Capsule, plateMaterial, new Vector3(.36f, .24f, .24f));
            neck = Part("Neck", PrimitiveType.Cylinder, suitMaterial, new Vector3(.105f, .057f, .105f));
            head = Part("Helmet", PrimitiveType.Sphere, plateMaterial, new Vector3(.235f, .28f, .235f));
            visor = Part("Visor", PrimitiveType.Cube, accentMaterial, new Vector3(.185f, .062f, .03f));
            upperArms = new Transform[2]; forearms = new Transform[2]; hands = new Transform[2];
            thighs = new Transform[2]; shins = new Transform[2]; feet = new Transform[2];
            shoulders = new Transform[2]; elbows = new Transform[2]; knees = new Transform[2];
            for (int i = 0; i < 2; i++)
            {
                string side = i == 0 ? "R" : "L";
                shoulders[i] = Part("Shoulder_" + side, PrimitiveType.Sphere, plateMaterial, Vector3.one * .19f);
                upperArms[i] = Part("UpperArm_" + side, PrimitiveType.Capsule, suitMaterial, Vector3.one);
                forearms[i] = Part("Forearm_" + side, PrimitiveType.Capsule, plateMaterial, Vector3.one);
                elbows[i] = Part("Elbow_" + side, PrimitiveType.Sphere, suitMaterial, Vector3.one * .108f);
                hands[i] = Part("Glove_" + side, PrimitiveType.Sphere, suitMaterial, new Vector3(.105f, .12f, .13f));
                thighs[i] = Part("Thigh_" + side, PrimitiveType.Capsule, suitMaterial, Vector3.one);
                shins[i] = Part("Shin_" + side, PrimitiveType.Capsule, plateMaterial, Vector3.one);
                feet[i] = Part("Boot_" + side, PrimitiveType.Cube, suitMaterial, new Vector3(.155f, .11f, .27f));
                knees[i] = Part("Knee_" + side, PrimitiveType.Sphere, accentMaterial, new Vector3(.125f, .135f, .11f));
            }
            sword = new GameObject("WeaponGrip").transform;
            sword.SetParent(visual, false);
            WeaponPart("Grip", PrimitiveType.Cylinder, suitMaterial, new Vector3(.044f, .10f, .044f), Vector3.zero, Quaternion.Euler(90, 0, 0));
            WeaponPart("Guard", PrimitiveType.Cube, plateMaterial, new Vector3(.24f, .045f, .045f), new Vector3(0, 0, .12f), Quaternion.identity);
            WeaponPart("Blade", PrimitiveType.Cube, bladeMaterial, new Vector3(.055f, .018f, .7f), new Vector3(0, 0, .51f), Quaternion.identity);
            WeaponPart("BladeEdge", PrimitiveType.Cube, accentMaterial, new Vector3(.012f, .020f, .69f), new Vector3(.024f, 0, .51f), Quaternion.identity);
            ResetPose();
        }

        private void EnsureMaterials()
        {
            if (suitMaterial == null) suitMaterial = Material("Training navy fabric", new Color(.025f, .060f, .09f), .08f, .27f);
            if (plateMaterial == null) plateMaterial = Material("Training blue armour", new Color(.035f, .24f, .34f), .32f, .44f);
            if (accentMaterial == null) accentMaterial = Material("Training cyan identification", new Color(.10f, .85f, .95f), .18f, .50f);
            if (bladeMaterial == null) bladeMaterial = Material("Training blade steel", new Color(.55f, .67f, .74f), .83f, .65f);
        }

        private static Material Material(string name, Color color, float metal, float smooth)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            var material = new Material(shader) { name = name };
            material.SetColor("_BaseColor", color); material.SetFloat("_Metallic", metal); material.SetFloat("_Smoothness", smooth);
            return material;
        }

        private Transform Part(string name, PrimitiveType shape, Material material, Vector3 scale)
        {
            var obj = renderTrainingGeometry?GameObject.CreatePrimitive(shape):new GameObject(); obj.name = name;
            obj.transform.SetParent(visual, false); obj.transform.localScale = scale;
            var collider = obj.GetComponent<Collider>();
            if (collider != null)
            {
                collider.enabled = false;
                if (Application.isPlaying) Destroy(collider); else DestroyImmediate(collider);
            }
            if(renderTrainingGeometry)obj.GetComponent<Renderer>().sharedMaterial = material;
            obj.layer = 9;
            return obj.transform;
        }

        private void WeaponPart(string name, PrimitiveType shape, Material material, Vector3 scale, Vector3 position, Quaternion rotation)
        {
            Transform part = Part(name, shape, material, scale);
            part.SetParent(sword, false); part.localPosition = position; part.localRotation = rotation;
        }

        public void ResetPose()
        {
            gait = locomotion = death = recoil = guardBlend = 0;
            feetReady = wasDashing = false;
            stepClock = 0; nextFoot = 0;
            stepTime[0] = stepTime[1] = -1;
            Pose(0, Vector3.zero, true, -1, -1, 0, false);
        }

        public void ReactToHit() { recoil = 1; }

        public void Pose(float delta, Vector3 localVelocity, bool grounded, float attackTime, float dodgeTime, int combo, bool dead, bool guarding = false)
        {
            if (!Built) return;
            float speed = Vector3.ProjectOnPlane(localVelocity, Vector3.up).magnitude;
            float wantedMove = Mathf.InverseLerp(.10f, .8f, speed);
            locomotion = Mathf.MoveTowards(locomotion, wantedMove, delta * 9);
            death = Mathf.MoveTowards(death, dead ? 1 : 0, delta * 1.5f);
            recoil = Mathf.MoveTowards(recoil, 0, delta * 5);
            guardBlend = Mathf.MoveTowards(guardBlend, guarding && !dead ? 1 : 0, delta * 9);
            float guardWeight = Mathf.SmoothStep(0, 1, guardBlend);
            float cadence = Mathf.Lerp(1.55f, 2.25f, Mathf.InverseLerp(.2f, 5.2f, speed));
            gait = Mathf.Repeat(gait + (speed > .1f ? cadence * delta : 0), 1);
            Vector3 travel = speed > .05f ? Vector3.ProjectOnPlane(localVelocity, Vector3.up).normalized : Vector3.forward;
            float duck = dodgeTime >= 0 ? Mathf.Sin(Mathf.Clamp01(dodgeTime / .54f) * Mathf.PI) : 0;
            float attack = attackTime >= 0 ? AttackWeight(attackTime) : 0;
            float twist = attackTime >= 0 ? AttackTwist(attackTime) * (combo % 2 == 0 ? 1 : -.7f) : 0;
            float bob = Mathf.Cos(gait * Mathf.PI * 4) * .022f * locomotion;
            UpdateFeet(delta, localVelocity, grounded, dodgeTime, dead, cadence);
            Vector3 hip = new Vector3(0, .91f + bob - duck * .20f - death * .48f, -death * .12f);
            // Keep two rigid leg lengths. Lower the pelvis before ever asking a leg
            // to reach beyond its ankle; an IK distance clamp alone stretches its shin.
            for (int i = 0; i < 2; i++)
            {
                Vector3 ankle = visual.InverseTransformPoint(footWorld[i]);
                float side = i == 0 ? 1 : -1;
                float horizontal = new Vector2(ankle.x - side * .112f, ankle.z - hip.z).sqrMagnitude;
                float reach = UpperLeg + LowerLeg - .012f;
                hip.y = Mathf.Min(hip.y, ankle.y + Mathf.Sqrt(Mathf.Max(.04f, reach * reach - horizontal)));
            }
            Quaternion chestTurn = Quaternion.Euler(duck * 22 * travel.z + death * 60 + recoil * -12,
                twist, -travel.x * (locomotion * 6 + duck * 20));
            Vector3 chest = hip + chestTurn * new Vector3(0, .33f, .015f);
            hips.localPosition = hip; hips.localRotation = Quaternion.Euler(0, twist * .32f, 0);
            abdomen.localPosition = hip + chestTurn * new Vector3(0, .17f, .012f);
            abdomen.localRotation = chestTurn;
            torso.localPosition = chest; torso.localRotation = chestTurn;
            neck.localPosition = hip + chestTurn * new Vector3(0, .61f, .015f);
            neck.localRotation = chestTurn;
            head.localPosition = hip + chestTurn * new Vector3(0, .74f, .015f);
            head.localRotation = Quaternion.Euler(duck * 8 + death * 30, twist * .22f, 0);
            visor.localPosition = head.localPosition + head.localRotation * new Vector3(0, .01f, .114f);
            visor.localRotation = head.localRotation;
            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? 1 : -1;
                Vector3 ankle = visual.InverseTransformPoint(footWorld[i]);
                Vector3 hipJoint = hip + new Vector3(side * .112f, 0, 0);
                ankle = Reachable(hipJoint, ankle, UpperLeg, LowerLeg);
                Vector3 knee = Joint(hipJoint, ankle, UpperLeg, LowerLeg, new Vector3(side * .04f, 0, .4f));
                Segment(thighs[i], hipJoint, knee, .165f); Segment(shins[i], knee, ankle, .128f);
                knees[i].localPosition = knee + Vector3.forward * .058f;
                feet[i].localRotation = Quaternion.Inverse(visual.rotation) * footRotation[i];
                feet[i].localPosition = ankle + feet[i].localRotation * new Vector3(0, -.05f, .055f);
                Vector3 shoulder = hip + chestTurn * new Vector3(side * .225f, .52f, 0);
                shoulders[i].localPosition = shoulder;
                Vector3 wrist;
                if (i == 0)
                {
                    wrist = new Vector3(.29f, 1.14f, .24f);
                    if (attackTime >= 0) wrist = AttackHand(attackTime, combo);
                    else wrist += new Vector3(0, 0, Mathf.Sin(gait * Mathf.PI * 2) * .075f * locomotion);
                }
                else
                {
                    wrist = new Vector3(-.28f, 1.05f, .17f - Mathf.Sin(gait * Mathf.PI * 2) * .15f * locomotion);
                    wrist = Vector3.Lerp(wrist, new Vector3(-.31f, 1.0f, -.08f), attack);
                }
                wrist.y -= duck * .23f + death * .52f;
                if (guardWeight > 0 && attackTime < 0 && dodgeTime < 0)
                    wrist = Vector3.Lerp(wrist, i == 0 ? new Vector3(.12f, 1.28f, .36f) : new Vector3(-.13f, 1.20f, .30f), guardWeight);
                wrist.y += hip.y - (.91f + bob - duck * .20f - death * .48f);
                wrist.z += death * .24f;
                wrist = Reachable(shoulder, wrist, lengths[0], lengths[1]);
                Vector3 elbow = Joint(shoulder, wrist, lengths[0], lengths[1], new Vector3(side * .45f, -.28f, -.22f));
                Segment(upperArms[i], shoulder, elbow, .115f); Segment(forearms[i], elbow, wrist, .11f);
                elbows[i].localPosition = elbow;
                hands[i].localPosition = wrist;
                hands[i].localRotation = Quaternion.LookRotation((wrist - elbow).normalized, Vector3.up);
                if (i == 0)
                {
                    Vector3 bladeDirection = attackTime >= 0 ? AttackBlade(attackTime, combo) : new Vector3(.16f, .12f, 1);
                    if (guardWeight > 0 && !dead) bladeDirection = Vector3.Lerp(bladeDirection, new Vector3(-.6f, .8f, .12f), guardWeight);
                    bladeDirection = Vector3.Lerp(bladeDirection, new Vector3(.55f, -.85f, .05f), death);
                    sword.localPosition = wrist;
                    sword.localRotation = Quaternion.LookRotation(bladeDirection.normalized, Vector3.up);
                }
            }
            Posed?.Invoke(delta, localVelocity, grounded, attackTime, dodgeTime, combo, dead, guarding);
        }

        private static Vector3 NeutralAnkle(int side) => new Vector3(side == 0 ? .14f : -.14f, .11f, side == 0 ? .04f : -.04f);

        private void UpdateFeet(float delta, Vector3 localVelocity, bool grounded, float dodgeTime, bool dead, float cadence)
        {
            Vector3 worldVelocity = visual.TransformDirection(Vector3.ProjectOnPlane(localVelocity, Vector3.up));
            float speed = worldVelocity.magnitude;
            float ground = visual.position.y + .11f;
            if (!feetReady)
            {
                for (int i = 0; i < 2; i++)
                {
                    footWorld[i] = visual.TransformPoint(NeutralAnkle(i));
                    footRotation[i] = visual.rotation * Quaternion.Euler(0, i == 0 ? 5 : -5, 0);
                    stepTime[i] = -1;
                }
                feetReady = true;
            }

            // The dodge is a low evasive bound. Its brief airborne slide is a deliberate
            // action, while ordinary locomotion below keeps every support foot in world space.
            if (dodgeTime >= 0)
            {
                if (!wasDashing)
                    for (int i = 0; i < 2; i++) { stepStart[i] = footWorld[i]; stepTime[i] = -1; }
                float p = Mathf.Clamp01(dodgeTime / .54f), lift = Mathf.Sin(p * Mathf.PI) * .115f;
                for (int i = 0; i < 2; i++)
                {
                    Vector3 ankle = NeutralAnkle(i);
                    ankle.z += i == 0 ? .12f : -.10f;
                    ankle.y += lift;
                    footWorld[i] = Vector3.Lerp(stepStart[i], visual.TransformPoint(ankle), Smooth(dodgeTime / .1f));
                    footRotation[i] = visual.rotation * Quaternion.Euler(0, i == 0 ? 7 : -7, 0);
                }
                wasDashing = true;
                return;
            }
            if (wasDashing) { stepClock = 0; wasDashing = false; }

            float run = Mathf.InverseLerp(2.8f, 5.2f, speed);
            float interval = 1 / (Mathf.Max(1, cadence) * 2);
            float duration = interval * Mathf.Lerp(.94f, 1.35f, run);
            float support = Mathf.Max(.09f, interval * 2 - duration);
            bool moving = speed > .18f && !dead;
            stepClock -= delta;

            for (int i = 0; i < 2; i++)
            {
                if (stepTime[i] < 0) { footWorld[i].y = ground; continue; }
                stepTime[i] = Mathf.Min(stepDuration[i], stepTime[i] + delta);
                float p = stepTime[i] / stepDuration[i];
                Vector3 desired = visual.TransformPoint(NeutralAnkle(i)) + worldVelocity *
                    (Mathf.Max(0, stepDuration[i] - stepTime[i]) + (moving ? support * .5f : 0));
                desired.y = ground;
                // Retarget an airborne foot for a genuine change of speed or direction;
                // the planted foot is never pulled along with the capsule.
                stepTarget[i] = Vector3.Lerp(stepTarget[i], desired, 1 - Mathf.Exp(-18 * delta));
                footWorld[i] = Vector3.Lerp(stepStart[i], stepTarget[i], Smooth(p));
                footWorld[i].y += Mathf.Sin(p * Mathf.PI) * Mathf.Lerp(.075f, .155f, run) * (grounded ? 1 : .75f);
                Quaternion desiredRotation = visual.rotation * Quaternion.Euler(0, i == 0 ? 5 : -5, 0);
                footRotation[i] = Quaternion.Slerp(stepRotation[i], desiredRotation, Smooth(p));
                if (p >= 1) { stepTime[i] = -1; footWorld[i] = stepTarget[i]; }
            }

            if (dead) return;
            int chosen = -1;
            if (moving && stepClock <= 0 && stepTime[nextFoot] < 0) chosen = nextFoot;
            // Acceleration and a sharp strafe can exhaust a support step before the
            // steady cadence does. Lift it early instead of dragging or stretching it.
            if (moving)
                for (int i = 0; i < 2; i++)
                {
                    Vector3 local = visual.InverseTransformPoint(footWorld[i]);
                    if (stepTime[i] < 0 && Vector3.ProjectOnPlane(local - NeutralAnkle(i), Vector3.up).magnitude > .40f)
                        chosen = i;
                }
            if (!moving && stepTime[0] < 0 && stepTime[1] < 0)
            {
                float largest = .085f;
                for (int i = 0; i < 2; i++)
                {
                    Vector3 local = visual.InverseTransformPoint(footWorld[i]);
                    float error = Vector3.ProjectOnPlane(local - NeutralAnkle(i), Vector3.up).magnitude;
                    float yawError = Quaternion.Angle(footRotation[i], visual.rotation * Quaternion.Euler(0, i == 0 ? 5 : -5, 0));
                    if (error > largest || yawError > 32) { largest = error; chosen = i; }
                }
            }
            if (chosen < 0) return;
            stepTime[chosen] = 0;
            stepDuration[chosen] = moving ? duration : .23f;
            stepStart[chosen] = footWorld[chosen];
            stepRotation[chosen] = footRotation[chosen];
            stepTarget[chosen] = visual.TransformPoint(NeutralAnkle(chosen)) + worldVelocity *
                (stepDuration[chosen] + (moving ? support * .5f : 0));
            stepTarget[chosen].y = ground;
            nextFoot = 1 - chosen;
            stepClock = interval;
        }

        private static void Segment(Transform part, Vector3 a, Vector3 b, float width)
        {
            part.localPosition = (a + b) * .5f;
            part.localRotation = Quaternion.FromToRotation(Vector3.up, b - a);
            part.localScale = new Vector3(width, (b - a).magnitude * .5f, width);
        }

        private static Vector3 Reachable(Vector3 root, Vector3 end, float first, float second)
        {
            Vector3 direction = end - root;
            float distance = direction.magnitude;
            if (distance < .00001f) return root + Vector3.down * Mathf.Max(.025f, Mathf.Abs(first - second) + .003f);
            return root + direction / distance * Mathf.Clamp(distance,
                Mathf.Max(.025f, Mathf.Abs(first - second) + .003f), first + second - .008f);
        }

        private static Vector3 Joint(Vector3 root, Vector3 end, float first, float second, Vector3 pole)
        {
            Vector3 line = end - root;
            float distance = Mathf.Max(.00001f, line.magnitude);
            Vector3 forward = line.sqrMagnitude > .00001f ? line.normalized : Vector3.down;
            Vector3 bend = Vector3.ProjectOnPlane(pole, forward).normalized;
            if (bend.sqrMagnitude < .1f) bend = Vector3.ProjectOnPlane(Vector3.forward, forward).normalized;
            float along = (first * first - second * second + distance * distance) / (2 * distance);
            return root + forward * along + bend * Mathf.Sqrt(Mathf.Max(.00001f, first * first - along * along));
        }

        private static float Smooth(float t) { t = Mathf.Clamp01(t); return t * t * (3 - 2 * t); }
        private static float AttackWeight(float t) => t < .16f ? Smooth(t / .16f) : 1 - Smooth((t - .40f) / .27f);
        private static float AttackTwist(float t)
        {
            if (t < .16f) return Mathf.Lerp(0, 33, Smooth(t / .16f));
            if (t < .39f) return Mathf.Lerp(33, -38, Smooth((t - .16f) / .23f));
            return Mathf.Lerp(-38, 0, Smooth((t - .39f) / .28f));
        }

        private static Vector3 AttackHand(float t, int combo)
        {
            Vector3 rest = new Vector3(.29f, 1.14f, .24f);
            Vector3 start = combo % 2 == 0 ? new Vector3(.39f, 1.48f, -.06f) : new Vector3(-.15f, 1.35f, .12f);
            Vector3 contact = new Vector3(.12f, 1.27f, .49f);
            Vector3 follow = combo % 2 == 0 ? new Vector3(-.29f, 1.07f, .24f) : new Vector3(.43f, 1.18f, .18f);
            if (t < .16f) return Vector3.Lerp(rest, start, Smooth(t / .16f));
            if (t < .27f) return Vector3.Lerp(start, contact, Smooth((t - .16f) / .11f));
            if (t < .41f) return Vector3.Lerp(contact, follow, Smooth((t - .27f) / .14f));
            return Vector3.Lerp(follow, rest, Smooth((t - .41f) / .26f));
        }

        private static Vector3 AttackBlade(float t, int combo)
        {
            Vector3 rest = new Vector3(.16f, .12f, 1).normalized;
            Vector3 start = (combo % 2 == 0 ? new Vector3(.1f, .87f, -.5f) : new Vector3(-.9f, .3f, .15f)).normalized;
            Vector3 contact = (combo % 2 == 0 ? new Vector3(-.25f, -.15f, 1) : new Vector3(.27f, -.1f, 1)).normalized;
            Vector3 follow = (combo % 2 == 0 ? new Vector3(-.9f, -.3f, .12f) : new Vector3(.95f, .1f, .12f)).normalized;
            if (t < .16f) return Vector3.Slerp(rest, start, Smooth(t / .16f));
            if (t < .27f) return Vector3.Slerp(start, contact, Smooth((t - .16f) / .11f));
            if (t < .41f) return Vector3.Slerp(contact, follow, Smooth((t - .27f) / .14f));
            return Vector3.Slerp(follow, rest, Smooth((t - .41f) / .26f));
        }
    }
}
