using System;
using System.Collections.Generic;
using UnityEngine;

namespace DeadAir
{
    /// <summary>
    /// Procedural 3D model and animation rig for the legendary Teke-Teke (テケテケ).
    /// Severed upper torso only, crawling at terrifying speed on elbows and sharp claws.
    /// Features custom crawling locomotion, attack jump, female screams/groans, and sanity-based hunt behavior.
    /// </summary>
    public sealed class DeadAirTekeTeke : MonoBehaviour
    {
        Transform modelRoot, spineRoot, headRoot, jawRoot, hairRoot;
        Transform leftShoulder, rightShoulder, leftElbow, rightElbow, leftHand, rightHand;
        readonly List<Transform> spineVertebrae = new List<Transform>();
        Light auraLight;
        AudioSource audioSource;

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

            var matSkin = CreateMat("TekeSkin", shader, new Color(0.78f, 0.76f, 0.70f), 0.1f);
            var matUniform = CreateMat("TekeSailorFuku", shader, new Color(0.12f, 0.15f, 0.20f), 0.2f);
            var matCollar = CreateMat("TekeCollar", shader, new Color(0.92f, 0.90f, 0.86f), 0.2f);
            var matRibbon = CreateMat("TekeRedRibbon", shader, new Color(0.65f, 0.10f, 0.10f), 0.3f);
            var matBlood = CreateMat("TekeBloodGore", shader, new Color(0.42f, 0.08f, 0.06f), 0.5f);
            var matHair = CreateMat("TekeBlackHair", shader, new Color(0.04f, 0.04f, 0.05f), 0.05f);
            var matDark = CreateMat("TekeMouthDark", shader, new Color(0.08f, 0.08f, 0.08f), 0.05f);
            var matEye = CreateMat("TekeRedEye", shader, new Color(1.0f, 0.1f, 0.05f), 0.9f);

            modelRoot = new GameObject("TekeTeke Model").transform;
            modelRoot.SetParent(transform, false);

            // 1. Torso & Sailor Fuku Uniform (Severed at waist)
            // Ribcage / Chest
            spineRoot = CreatePart(modelRoot, "Spine", new Vector3(0, 0.38f, 0), new Vector3(0.48f, 0.36f, 0.28f), matUniform);
            // White sailor collar
            CreatePart(spineRoot, "Collar", new Vector3(0, 0.16f, 0.01f), new Vector3(0.44f, 0.12f, 0.30f), matCollar);
            // Red neckerchief ribbon
            CreatePart(spineRoot, "Ribbon", new Vector3(0, 0.08f, 0.15f), new Vector3(0.12f, 0.14f, 0.05f), matRibbon);

            // 2. Severed Lower Waist & Trailing Exposed Spine (Cut by train)
            var waist = CreatePart(spineRoot, "Severed Waist", new Vector3(0, -0.22f, -0.02f), new Vector3(0.38f, 0.18f, 0.24f), matBlood);
            // Exposed spinal cord vertebrae dragging on the floor
            for (int i = 0; i < 4; i++)
            {
                var vert = CreatePart(waist, "Vertebra " + i, new Vector3(0, -i * 0.04f - 0.06f, -i * 0.14f - 0.08f),
                    new Vector3(0.14f, 0.08f, 0.12f), matSkin);
                CreatePart(vert, "Torn Flesh", new Vector3(0, -0.03f, 0), new Vector3(0.18f, 0.05f, 0.10f), matBlood);
                spineVertebrae.Add(vert);
            }

            // 3. Ghastly Head, Tangled Black Hair & Screaming Bloody Jaw
            headRoot = CreatePart(spineRoot, "Head", new Vector3(0, 0.28f, 0.04f), new Vector3(0.28f, 0.30f, 0.28f), matSkin);
            // Sunken eye sockets with glowing red embers
            for (int side = -1; side <= 1; side += 2)
            {
                CreatePart(headRoot, "Socket", new Vector3(side * 0.07f, 0.04f, 0.13f), new Vector3(0.06f, 0.05f, 0.04f), matDark);
                CreatePart(headRoot, "Glowing Pupil", new Vector3(side * 0.07f, 0.04f, 0.15f), new Vector3(0.03f, 0.03f, 0.02f), matEye);
            }
            // Wide open screaming jaw with jagged teeth
            jawRoot = CreatePart(headRoot, "Jaw", new Vector3(0, -0.12f, 0.08f), new Vector3(0.20f, 0.14f, 0.14f), matBlood);
            CreatePart(jawRoot, "Teeth Upper", new Vector3(0, 0.05f, 0.06f), new Vector3(0.16f, 0.02f, 0.02f), matSkin);
            CreatePart(jawRoot, "Teeth Lower", new Vector3(0, -0.05f, 0.06f), new Vector3(0.15f, 0.02f, 0.02f), matSkin);

            // Stringy black hair hanging over face (Photo 2)
            hairRoot = new GameObject("Hair").transform;
            hairRoot.SetParent(headRoot, false);
            CreatePart(hairRoot, "Hair Crown", new Vector3(0, 0.12f, -0.02f), new Vector3(0.32f, 0.16f, 0.32f), matHair);
            CreatePart(hairRoot, "Hair Back", new Vector3(0, -0.10f, -0.15f), new Vector3(0.30f, 0.44f, 0.10f), matHair);
            for (int i = -3; i <= 3; i++)
            {
                CreatePart(hairRoot, "Hair Strand " + i, new Vector3(i * 0.045f, -0.08f, 0.15f),
                    new Vector3(0.04f, 0.36f + Mathf.Abs(i) * 0.05f, 0.02f), matHair);
            }

            // 4. Two Long Muscular Crawling Arms with Claws
            // Left arm
            leftShoulder = new GameObject("Left Shoulder").transform;
            leftShoulder.SetParent(spineRoot, false);
            leftShoulder.localPosition = new Vector3(-0.30f, 0.08f, 0);
            CreatePart(leftShoulder, "Upper Arm L", new Vector3(0, -0.16f, 0), new Vector3(0.12f, 0.34f, 0.12f), matUniform);

            leftElbow = new GameObject("Left Elbow").transform;
            leftElbow.SetParent(leftShoulder, false);
            leftElbow.localPosition = new Vector3(0, -0.32f, 0);
            CreatePart(leftElbow, "Forearm L", new Vector3(0, -0.16f, 0.04f), new Vector3(0.10f, 0.36f, 0.10f), matSkin);

            leftHand = new GameObject("Left Hand").transform;
            leftHand.SetParent(leftElbow, false);
            leftHand.localPosition = new Vector3(0, -0.32f, 0.08f);
            CreatePart(leftHand, "Palm L", Vector3.zero, new Vector3(0.14f, 0.04f, 0.16f), matSkin);
            for (int c = -1; c <= 1; c++)
            {
                CreatePart(leftHand, "Claw L " + c, new Vector3(c * 0.045f, 0, 0.10f), new Vector3(0.02f, 0.02f, 0.08f), matDark);
            }

            // Right arm
            rightShoulder = new GameObject("Right Shoulder").transform;
            rightShoulder.SetParent(spineRoot, false);
            rightShoulder.localPosition = new Vector3(0.30f, 0.08f, 0);
            CreatePart(rightShoulder, "Upper Arm R", new Vector3(0, -0.16f, 0), new Vector3(0.12f, 0.34f, 0.12f), matUniform);

            rightElbow = new GameObject("Right Elbow").transform;
            rightElbow.SetParent(rightShoulder, false);
            rightElbow.localPosition = new Vector3(0, -0.32f, 0);
            CreatePart(rightElbow, "Forearm R", new Vector3(0, -0.16f, 0.04f), new Vector3(0.10f, 0.36f, 0.10f), matSkin);

            rightHand = new GameObject("Right Hand").transform;
            rightHand.SetParent(rightElbow, false);
            rightHand.localPosition = new Vector3(0, -0.32f, 0.08f);
            CreatePart(rightHand, "Palm R", Vector3.zero, new Vector3(0.14f, 0.04f, 0.16f), matSkin);
            for (int c = -1; c <= 1; c++)
            {
                CreatePart(rightHand, "Claw R " + c, new Vector3(c * 0.045f, 0, 0.10f), new Vector3(0.02f, 0.02f, 0.08f), matDark);
            }

            // Ominous faint blood red aura
            auraLight = gameObject.AddComponent<Light>();
            auraLight.type = LightType.Point;
            auraLight.color = new Color(0.9f, 0.15f, 0.1f);
            auraLight.intensity = 1.2f;
            auraLight.range = 3.5f;

            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.spatialBlend = 1f;
            audioSource.minDistance = 2f;
            audioSource.maxDistance = 20f;
            audioSource.rolloffMode = AudioRolloffMode.Linear;
        }

        static Material CreateMat(string name, Shader shader, Color color, float gloss)
        {
            var mat = new Material(shader) { name = name, color = color, hideFlags = HideFlags.DontSave };
            mat.SetFloat("_Glossiness", gloss);
            return mat;
        }

        static Transform CreatePart(Transform parent, string name, Vector3 localPos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            var r = go.GetComponent<Renderer>();
            if (r != null) r.sharedMaterial = mat;
            return go.transform;
        }

        /// <summary>
        /// Updates Teke-Teke presentation according to server EnemyState and local Player sanity.
        /// </summary>
        public void Present(EnemyState enemy, float playerSanity, float elapsed, bool paused)
        {
            if (modelRoot == null) Initialize();

            // Ground position: Teke-Teke crawls right on the floor (y ≈ 0.22m)
            var targetPos = new Vector3(-enemy.x, enemy.y + 0.22f, enemy.z);
            float dt = paused ? 0 : Mathf.Min(Time.deltaTime, 0.1f);

            float distance = Vector3.Distance(targetPos, lastPosition);
            bool moving = !paused && distance > 0.001f;

            transform.position = targetPos;
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.Euler(0, enemy.yaw * Mathf.Rad2Deg, 0), dt * 10f);

            // Sanity Check:
            // High sanity (> 40): She only appears, groans, vanishes ("просто появляється і пропадає")
            // Low sanity (<= 40): She enters the active HUNT ("починає охоту за журналістом")
            isHunting = playerSanity <= 40f;

            if (isHunting)
            {
                // Play Bloodcurdling Female Scream when hunting!
                if (elapsed >= screamCooldown)
                {
                    screamCooldown = elapsed + UnityEngine.Random.Range(10f, 18f);
                    DeadAirAudio.PlayAt("teke-scream", transform.position, 1.0f);
                }
                auraLight.intensity = 2.4f;
                auraLight.color = new Color(1.0f, 0.1f, 0.05f);
            }
            else
            {
                // Female groan / moan when manifesting
                if (elapsed >= groanCooldown)
                {
                    groanCooldown = elapsed + UnityEngine.Random.Range(8f, 15f);
                    DeadAirAudio.PlayAt("teke-groan", transform.position, 0.8f);
                }
                auraLight.intensity = 0.8f;
                auraLight.color = new Color(0.6f, 0.2f, 0.2f);
            }

            // Crawling Animation:
            if (moving)
            {
                float speed = distance / Mathf.Max(0.016f, elapsed - lastElapsed);
                gaitPhase += dt * (isHunting ? 14f : 8f);

                // Play rhythmic crawl thuds ("teke... teke... teke...")
                crawlAudioTimer += dt * (isHunting ? 12f : 6f);
                if (crawlAudioTimer >= 1.0f)
                {
                    crawlAudioTimer = 0;
                    DeadAirAudio.PlayAt("teke-crawl", transform.position, 0.65f);
                }
            }

            float stepL = Mathf.Sin(gaitPhase);
            float stepR = Mathf.Sin(gaitPhase + Mathf.PI);

            // Severed torso sways and dips as elbows push
            float bobY = 0.38f + Mathf.Abs(stepL) * 0.04f;
            float rollZ = stepL * 4.5f;
            spineRoot.localPosition = new Vector3(0, bobY, 0);
            spineRoot.localRotation = Quaternion.Euler(6f + Mathf.Abs(stepL) * 3f, 0, rollZ);

            // Head bobs forward hungrily
            headRoot.localRotation = Quaternion.Euler(-12f + Mathf.Sin(gaitPhase * 0.5f) * 6f, stepL * 5f, 0);

            // Trailing spine slithers behind on the ground
            for (int i = 0; i < spineVertebrae.Count; i++)
            {
                float delayPhase = gaitPhase - i * 0.6f;
                spineVertebrae[i].localRotation = Quaternion.Euler(0, Mathf.Sin(delayPhase) * 12f, 0);
            }

            // Left & Right Arm Crawl Motion
            if (enemy.behaviour == "attack")
            {
                // Kill / Attack animation: Reaching high with both claws and gaping jaw!
                attackTimer += dt * 5f;
                float strike = Mathf.PingPong(attackTimer, 1f);

                leftShoulder.localRotation = Quaternion.Euler(Mathf.Lerp(-40, -110, strike), 15, -20);
                rightShoulder.localRotation = Quaternion.Euler(Mathf.Lerp(-40, -110, strike), -15, 20);
                leftElbow.localRotation = Quaternion.Euler(-45, 0, 0);
                rightElbow.localRotation = Quaternion.Euler(-45, 0, 0);
                jawRoot.localPosition = new Vector3(0, -0.16f - strike * 0.06f, 0.10f); // Wide open screaming mouth!
            }
            else
            {
                attackTimer = 0;
                // Crawling arms pulling against the floor
                leftShoulder.localRotation = Quaternion.Euler(stepL * 35f - 20f, 10, -15);
                leftElbow.localRotation = Quaternion.Euler(Mathf.Clamp(stepL * 40f - 20f, -60, -10), 0, 0);

                rightShoulder.localRotation = Quaternion.Euler(stepR * 35f - 20f, -10, 15);
                rightElbow.localRotation = Quaternion.Euler(Mathf.Clamp(stepR * 40f - 20f, -60, -10), 0, 0);

                // Subtle jaw breathing / snarl
                jawRoot.localPosition = new Vector3(0, -0.12f + Mathf.Sin(elapsed * 4f) * 0.02f, 0.08f);
            }

            lastPosition = targetPos;
            lastElapsed = elapsed;
        }
    }
}
