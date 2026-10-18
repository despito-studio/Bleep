using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DeadAir
{
    /// <summary>
    /// Procedural 3D model and animation rig for the legendary Teke-Teke (テケテケ).
    /// Truly legless Japanese urban legend ghost: severed lower torso with exposed
    /// trailing spine, crawling horizontally on forearms and sharp claws at high speed.
    /// Uses sculpted ellipsoid/tube meshes without non-uniform bone scaling.
    /// </summary>
    public sealed class DeadAirTekeTeke : MonoBehaviour
    {
        readonly List<UnityEngine.Object> ownedAssets = new List<UnityEngine.Object>();
        Transform modelRoot, spine, waist, head, jaw, hair;
        Transform leftShoulder, rightShoulder, leftElbow, rightElbow, leftHand, rightHand;
        readonly List<Transform> spineVertebrae = new List<Transform>();
        Light auraLight;

        float gaitPhase;
        Vector3 lastPosition;
        float lastElapsed;
        float attackTimer;
        float screamCooldown;
        float groanCooldown;
        float crawlAudioTimer;
        bool isHunting;

        public void Initialize()
        {
            var shader = Shader.Find("Dead Air/Source Surface") ?? Shader.Find("Standard");

            var matSkin = Surface("Teke / ghostly pale skin", shader, new Color(0.82f, 0.83f, 0.79f), 0.05f);
            var matUniform = Surface("Teke / dark school fuku", shader, new Color(0.11f, 0.13f, 0.18f), 0.15f);
            var matCollar = Surface("Teke / white sailor collar", shader, new Color(0.90f, 0.89f, 0.84f), 0.1f);
            var matRibbon = Surface("Teke / red silk ribbon", shader, new Color(0.70f, 0.08f, 0.08f), 0.25f);
            var matBlood = Surface("Teke / severed blood and gore", shader, new Color(0.50f, 0.04f, 0.04f), 0.4f);
            var matHair = Surface("Teke / wet black hair", shader, new Color(0.03f, 0.03f, 0.04f), 0.05f);
            var matDark = Surface("Teke / hollow sockets and claws", shader, new Color(0.05f, 0.05f, 0.05f), 0.05f);
            var matEye = Surface("Teke / glowing red pupil", shader, new Color(1.0f, 0.08f, 0.04f), 0.9f);
            matEye.SetColor("_EmissionColor", new Color(1.5f, 0.1f, 0.05f));

            Mesh round = DeadAirMonsterBody.Ellipsoid(10, 6);
            Mesh tube = DeadAirMonsterBody.TaperedTube(10);
            ownedAssets.Add(round);
            ownedAssets.Add(tube);

            modelRoot = Group("TekeTeke Model Root", transform, Vector3.zero);

            // 1. Central Spine / Chest Joint (unscaled pivot)
            spine = Group("Spine Pivot", modelRoot, Vector3.zero);

            // Ribcage / Uniform Body (child mesh of spine)
            Part("Torso Uniform", spine, round, matUniform, new Vector3(0, 0, 0), new Vector3(0.36f, 0.32f, 0.22f));
            Part("Sunken Breastbone", spine, round, matSkin, new Vector3(0, 0.06f, 0.09f), new Vector3(0.14f, 0.20f, 0.06f));

            // White Sailor Collar & Red Ribbon
            Part("Sailor Collar Back", spine, round, matCollar, new Vector3(0, 0.12f, -0.06f), new Vector3(0.34f, 0.08f, 0.18f));
            Part("Sailor Collar Lapel L", spine, tube, matCollar, new Vector3(-0.10f, 0.08f, 0.10f), new Vector3(0.06f, 0.20f, 0.03f), new Vector3(0, 0, -18));
            Part("Sailor Collar Lapel R", spine, tube, matCollar, new Vector3(0.10f, 0.08f, 0.10f), new Vector3(0.06f, 0.20f, 0.03f), new Vector3(0, 0, 18));
            Part("Red Ribbon Knot", spine, round, matRibbon, new Vector3(0, 0.06f, 0.12f), new Vector3(0.07f, 0.07f, 0.04f));
            Part("Red Ribbon Tail L", spine, tube, matRibbon, new Vector3(-0.03f, 0.01f, 0.12f), new Vector3(0.03f, 0.12f, 0.02f), new Vector3(0, 0, 15));
            Part("Red Ribbon Tail R", spine, tube, matRibbon, new Vector3(0.03f, 0.01f, 0.12f), new Vector3(0.03f, 0.12f, 0.02f), new Vector3(0, 0, -15));

            // 2. Severed Waist & Trailing Exposed Spinal Cord (NO LEGS)
            waist = Group("Severed Waist Pivot", spine, new Vector3(0, -0.16f, 0));
            Part("Severed Torso Cut", waist, round, matBlood, new Vector3(0, -0.03f, 0), new Vector3(0.32f, 0.14f, 0.20f));
            Part("Ragged Uniform Hem", waist, round, matUniform, new Vector3(0, 0.02f, 0), new Vector3(0.35f, 0.08f, 0.22f));
            Part("Gore Stump", waist, round, matBlood, new Vector3(0, -0.08f, 0.02f), new Vector3(0.24f, 0.10f, 0.16f));

            // Articulated Spinal Column dragging along the floor
            spineVertebrae.Clear();
            Transform lastParent = waist;
            for (int i = 0; i < 5; i++)
            {
                var vert = Group("Vertebra " + i, lastParent, new Vector3(0, -0.09f, -0.02f));
                Part("Bone Vertebra", vert, round, matSkin, Vector3.zero, new Vector3(0.09f, 0.07f, 0.09f));
                Part("Spinal Tissue", vert, round, matBlood, new Vector3(0, 0.02f, 0.01f), new Vector3(0.12f, 0.05f, 0.08f));
                spineVertebrae.Add(vert);
                lastParent = vert;
            }

            // 3. Head & Creepy Face with Tangled Black Hair
            head = Group("Head Pivot", spine, new Vector3(0, 0.22f, 0.04f));
            Part("Ghost Skull", head, round, matSkin, new Vector3(0, 0.04f, 0), new Vector3(0.24f, 0.28f, 0.24f));
            Part("Cheek Left", head, round, matSkin, new Vector3(-0.08f, 0, 0.08f), new Vector3(0.07f, 0.10f, 0.06f));
            Part("Cheek Right", head, round, matSkin, new Vector3(0.08f, 0, 0.08f), new Vector3(0.07f, 0.10f, 0.06f));

            // Sunken Sockets & Glowing Red Pupils
            for (int side = -1; side <= 1; side += 2)
            {
                Part("Eye Socket", head, round, matDark, new Vector3(side * 0.06f, 0.05f, 0.10f), new Vector3(0.055f, 0.05f, 0.04f));
                Part("Glowing Eye", head, round, matEye, new Vector3(side * 0.06f, 0.05f, 0.12f), new Vector3(0.035f, 0.035f, 0.025f));
            }

            // Unhinged Bloody Jaw with Jagged Teeth
            jaw = Group("Jaw Pivot", head, new Vector3(0, -0.08f, 0.06f));
            Part("Mouth Cavity", jaw, round, matDark, new Vector3(0, 0, 0.02f), new Vector3(0.16f, 0.08f, 0.12f));
            Part("Chin", jaw, round, matSkin, new Vector3(0, -0.04f, 0.04f), new Vector3(0.14f, 0.08f, 0.12f));
            Part("Jaw Blood", jaw, round, matBlood, new Vector3(0, -0.02f, 0.05f), new Vector3(0.13f, 0.06f, 0.08f));
            for (int t = -2; t <= 2; t++)
            {
                Part("Tooth Top", head, tube, matSkin, new Vector3(t * 0.025f, -0.06f, 0.105f), new Vector3(0.012f, 0.03f, 0.012f));
                Part("Tooth Bot", jaw, tube, matSkin, new Vector3(t * 0.025f, -0.02f, 0.095f), new Vector3(0.012f, 0.03f, 0.012f));
            }

            // Stringy Black Hair falling over face and crown
            hair = Group("Hair Pivot", head, Vector3.zero);
            Part("Hair Crown", hair, round, matHair, new Vector3(0, 0.13f, -0.02f), new Vector3(0.27f, 0.14f, 0.27f));
            Part("Hair Back", hair, tube, matHair, new Vector3(0, -0.06f, -0.11f), new Vector3(0.24f, 0.36f, 0.08f));
            for (int h = -4; h <= 4; h++)
            {
                float len = 0.30f + Mathf.Abs(h) * 0.03f;
                Part("Hair Strand", hair, tube, matHair, new Vector3(h * 0.032f, -0.04f, 0.11f), new Vector3(0.025f, len, 0.02f), new Vector3(5, 0, h * 3));
            }

            // 4. Two Long Muscular Crawling Arms with Sharp Claws
            // Left Arm
            leftShoulder = Group("Shoulder L", spine, new Vector3(-0.24f, 0.14f, 0));
            Part("Sleeve L", leftShoulder, tube, matUniform, new Vector3(0, -0.12f, 0), new Vector3(0.11f, 0.26f, 0.11f));
            leftElbow = Group("Elbow L", leftShoulder, new Vector3(0, -0.25f, 0));
            Part("Elbow Joint L", leftElbow, round, matSkin, Vector3.zero, new Vector3(0.09f, 0.09f, 0.10f));
            Part("Forearm L", leftElbow, tube, matSkin, new Vector3(0, -0.14f, 0.02f), new Vector3(0.085f, 0.30f, 0.085f));
            leftHand = Group("Hand L", leftElbow, new Vector3(0, -0.28f, 0.04f));
            Part("Palm L", leftHand, round, matSkin, Vector3.zero, new Vector3(0.11f, 0.04f, 0.13f));
            for (int c = -2; c <= 2; c++)
            {
                Part("Finger L", leftHand, tube, matSkin, new Vector3(c * 0.024f, -0.02f, 0.08f), new Vector3(0.02f, 0.09f, 0.02f), new Vector3(25, 0, c * 6));
                Part("Claw L", leftHand, round, matDark, new Vector3(c * 0.024f, -0.04f, 0.13f), new Vector3(0.016f, 0.02f, 0.04f));
            }

            // Right Arm
            rightShoulder = Group("Shoulder R", spine, new Vector3(0.24f, 0.14f, 0));
            Part("Sleeve R", rightShoulder, tube, matUniform, new Vector3(0, -0.12f, 0), new Vector3(0.11f, 0.26f, 0.11f));
            rightElbow = Group("Elbow R", rightShoulder, new Vector3(0, -0.25f, 0));
            Part("Elbow Joint R", rightElbow, round, matSkin, Vector3.zero, new Vector3(0.09f, 0.09f, 0.10f));
            Part("Forearm R", rightElbow, tube, matSkin, new Vector3(0, -0.14f, 0.02f), new Vector3(0.085f, 0.30f, 0.085f));
            rightHand = Group("Hand R", rightElbow, new Vector3(0, -0.28f, 0.04f));
            Part("Palm R", rightHand, round, matSkin, Vector3.zero, new Vector3(0.11f, 0.04f, 0.13f));
            for (int c = -2; c <= 2; c++)
            {
                Part("Finger R", rightHand, tube, matSkin, new Vector3(c * 0.024f, -0.02f, 0.08f), new Vector3(0.02f, 0.09f, 0.02f), new Vector3(25, 0, c * 6));
                Part("Claw R", rightHand, round, matDark, new Vector3(c * 0.024f, -0.04f, 0.13f), new Vector3(0.016f, 0.02f, 0.04f));
            }

            // Faint blood-red glow aura around the ghost
            auraLight = gameObject.AddComponent<Light>();
            auraLight.type = LightType.Point;
            auraLight.color = new Color(0.9f, 0.15f, 0.1f);
            auraLight.intensity = 1.2f;
            auraLight.range = 3.5f;
        }

        static Material Surface(string name, Shader shader, Color color, float gloss)
        {
            var mat = new Material(shader) { name = name, color = color, hideFlags = HideFlags.DontSave };
            mat.SetFloat("_Glossiness", gloss);
            return mat;
        }

        static Transform Group(string name, Transform parent, Vector3 localPos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            return go.transform;
        }

        static Transform Part(string name, Transform parent, Mesh mesh, Material mat, Vector3 localPos, Vector3 scale, Vector3 rotation = default)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            go.transform.localRotation = Quaternion.Euler(rotation);
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.On;
            mr.receiveShadows = true;
            return go.transform;
        }

        public void Present(EnemyState enemy, float playerSanity, float elapsed, bool paused)
        {
            if (modelRoot == null) Initialize();

            // Ground position: crawling flat on the floor (Y offset ≈ 0.15m above floor)
            var targetPos = new Vector3(-enemy.x, enemy.y + 0.15f, enemy.z);
            float dt = paused ? 0 : Mathf.Min(Time.deltaTime, 0.1f);

            float distance = Vector3.Distance(targetPos, lastPosition);
            bool moving = !paused && distance > 0.001f;

            transform.position = targetPos;
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.Euler(0, enemy.yaw * Mathf.Rad2Deg, 0), dt * 12f);

            isHunting = playerSanity <= 40f;

            if (isHunting)
            {
                if (elapsed >= screamCooldown)
                {
                    screamCooldown = elapsed + UnityEngine.Random.Range(9f, 16f);
                    DeadAirAudio.PlayAt("teke-scream", transform.position, 1.0f);
                }
                auraLight.intensity = 2.2f;
                auraLight.color = new Color(1.0f, 0.1f, 0.05f);
            }
            else
            {
                if (elapsed >= groanCooldown)
                {
                    groanCooldown = elapsed + UnityEngine.Random.Range(8f, 15f);
                    DeadAirAudio.PlayAt("teke-groan", transform.position, 0.8f);
                }
                auraLight.intensity = 0.7f;
                auraLight.color = new Color(0.6f, 0.2f, 0.2f);
            }

            // Crawling Gait Cycle:
            if (moving)
            {
                gaitPhase += dt * (isHunting ? 15f : 8.5f);

                crawlAudioTimer += dt * (isHunting ? 13f : 7f);
                if (crawlAudioTimer >= 1.0f)
                {
                    crawlAudioTimer = 0;
                    DeadAirAudio.PlayAt("teke-crawl", transform.position, 0.7f);
                }
            }

            float stepL = Mathf.Sin(gaitPhase);
            float stepR = Mathf.Sin(gaitPhase + Mathf.PI);

            // Kinematics:
            // Torso is pitched forward ~74° (crawling horizontally flat on the ground)
            float pitchBob = Mathf.Abs(stepL) * 5f;
            float rollZ = stepL * 7.5f;
            float yawSway = Mathf.Sin(gaitPhase) * 6f;
            float heaveY = Mathf.Abs(stepL) * 0.03f;

            spine.localPosition = new Vector3(0, heaveY, 0);
            spine.localRotation = Quaternion.Euler(74f + pitchBob, yawSway, rollZ);

            // Head tilts upward ~ -66° relative to spine so her face stares straight ahead horizontally at player!
            float headBob = Mathf.Sin(gaitPhase * 0.5f) * 5f;
            head.localRotation = Quaternion.Euler(-66f + headBob, stepL * 5f, -rollZ * 0.4f);

            // Slithering spinal column trailing behind along the floorboards
            for (int i = 0; i < spineVertebrae.Count; i++)
            {
                float delayPhase = gaitPhase - (i + 1) * 0.6f;
                spineVertebrae[i].localRotation = Quaternion.Euler(0, Mathf.Sin(delayPhase) * 15f, 0);
            }

            // Arm Crawling Locomotion:
            if (enemy.behaviour == "attack")
            {
                attackTimer += dt * 5f;
                float strike = Mathf.PingPong(attackTimer, 1f);

                spine.localRotation = Quaternion.Euler(45f, 0, 0);
                head.localRotation = Quaternion.Euler(-45f, 0, 0);

                leftShoulder.localRotation = Quaternion.Euler(Mathf.Lerp(-40, -110, strike), 15, -25);
                rightShoulder.localRotation = Quaternion.Euler(Mathf.Lerp(-40, -110, strike), -15, 25);
                leftElbow.localRotation = Quaternion.Euler(30, 0, 0);
                rightElbow.localRotation = Quaternion.Euler(30, 0, 0);
                jaw.localRotation = Quaternion.Euler(25f + strike * 15f, 0, 0);
            }
            else
            {
                attackTimer = 0;
                // Alternating crawling stroke:
                // Arm forward: shoulder pitches forward, elbow extends down to the floor
                // Arm backward: shoulder pulls back, elbow flexes
                leftShoulder.localRotation = Quaternion.Euler(-25f + stepL * 38f, 15f + stepL * 10f, -20f + stepL * 8f);
                leftElbow.localRotation = Quaternion.Euler(45f - stepL * 28f, 0, 0);

                rightShoulder.localRotation = Quaternion.Euler(-25f + stepR * 38f, -15f - stepR * 10f, 20f - stepR * 8f);
                rightElbow.localRotation = Quaternion.Euler(45f - stepR * 28f, 0, 0);

                // Subtle jaw snarl
                jaw.localRotation = Quaternion.Euler(10f + Mathf.Sin(elapsed * 4f) * 6f, 0, 0);
            }

            lastPosition = targetPos;
            lastElapsed = elapsed;
        }

        void OnDestroy()
        {
            foreach (var asset in ownedAssets)
            {
                if (asset != null) Destroy(asset);
            }
            ownedAssets.Clear();
        }
    }
}
