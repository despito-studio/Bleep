using UnityEngine;

namespace DeadAir
{
    public sealed class DeadAirBootstrap : MonoBehaviour
    {
        WorldData data;
        DeadAirWorld world;
        DeadAirSession session;
        public DeadAirWorld World => world;

        void Awake()
        {
            Application.targetFrameRate = 60;
            DeadAirSettings.ApplyDisplay();
            data = WorldData.Load();
            world = new DeadAirWorld(data);
            var game = new GameObject("Dead Air gameplay");
            session = game.AddComponent<DeadAirSession>();
            session.Initialize(data);
            new GameObject("Signal mushrooms").AddComponent<DeadAirMushrooms>().Initialize(data, session);
            var actor = new GameObject("Local operative").AddComponent<DeadAirPlayer>();
            var interfaceController = new GameObject("Interface controller").AddComponent<DeadAirUI>();
            actor.Initialize(data, session, interfaceController);
            interfaceController.Initialize(data, session, actor);
            new GameObject("VHS atmosphere").AddComponent<DeadAirAtmosphere>().Initialize(session, actor);
            if(Application.isPlaying)
                new GameObject("Visual capture").AddComponent<DeadAirVisualCapture>().Initialize(session, actor);
        }

        void Update() { world.Apply(session.State); }
    }
}
