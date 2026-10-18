using System;
using UnityEditor;
using UnityEngine;

namespace DeadAir.Editor
{
    /// <summary>Checks winter placement boundaries without changing the saved office scene.</summary>
    public static class WinterValidation
    {
        [MenuItem("Dead Air/Validate Blackpine Winter")]
        public static void Validate()
        {
            var world = WorldData.Load();
            Require(DeadAirWinterWorld.IsSnowGround(world, 10, 23, 0), "The front yard must have snowy footsteps.");
            Require(DeadAirWinterWorld.IsSnowGround(world, -24, -40, 0), "The exterior side path must have snow.");
            Require(DeadAirWinterWorld.IsSnowGround(world, 5, -33, 0), "The open inner courtyard must retain snow.");
            Require(!DeadAirWinterWorld.IsSnowGround(world, 0, 2, 0), "Snow classification leaks into the entrance hall.");
            Require(!DeadAirWinterWorld.IsSnowGround(world, 0, -33, -4.2f), "The basement must never have snowy footsteps.");
            Require(!DeadAirWinterWorld.IsSnowGround(world, 12, -67, 0), "The generator canopy must shelter its ground.");
            foreach (var room in world.rooms)
            {
                if (!DeadAirOfficeLayout.IsInterior(room)) continue;
                Require(!DeadAirWinterWorld.IsSnowGround(world, -(room.minX + room.maxX) * .5f,
                    (room.minZ + room.maxZ) * .5f, room.floorY), "Snow classification leaks into " + room.name);
            }
            var station = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/World/station.prefab");
            Require(station != null, "Blackpine station prefab is missing.");
            int roofs = 0, sills = 0;
            foreach (var support in station.GetComponentsInChildren<BoxCollider>(true))
            {
                if (support.name == "Roof slab section") roofs++;
                if (support.name == "Facade sill") sills++;
            }
            Require(roofs > 0 && sills > 0, "Winter's roof and window caps lost their physical supports.");
            Require(Resources.Load<Shader>("Shaders/DeadAirWinterSnow") != null, "Snow shader must survive player-build stripping.");
            Require(Resources.Load<Shader>("Shaders/DeadAirWinterFlake") != null, "Snowflake shader must survive player-build stripping.");
            Debug.Log("Blackpine winter validated: courtyard/office/basement/canopy boundaries, " + roofs +
                " supported roof sections and " + sills + " supported facade sills; build-included shaders.");
        }

        [MenuItem("Dead Air/Validate Live Blackpine Winter")]
        public static void ValidateLive()
        {
            Require(Application.isPlaying, "Enter a Blackpine game before validating its live winter dressing.");
            var winter = UnityEngine.Object.FindFirstObjectByType<DeadAirWinterWorld>();
            Require(winter != null && winter.WinterActive, "Blackpine winter is not initialized and active.");
            Require(winter.ParticleBudget > 0 && winter.ParticleBudget <= 640, "Snowfall exceeds its fixed particle budget.");
            Require(winter.GroundQuadCount > 500, "The winter yard cover was not generated.");
            Require(winter.CaretakerCanopyCount == 2, "The collider-free goat shelter and hen coop must shelter snowfall.");
            var visual = GameObject.Find("BLACKPINE winter / nonblocking snow and weather");
            Require(visual != null && visual.GetComponentsInChildren<Collider>(true).Length == 0,
                "Snow dressing must never alter walkable routes or door collisions.");
            Debug.Log("Live Blackpine winter validated: " + winter.GroundQuadCount + " ground quads, " +
                winter.SurfaceTriangleCount + " exposed prop triangles, " + winter.ParticleBudget +
                " roof-aware particles and no added colliders.");
        }

        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
