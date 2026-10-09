using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using DeadAir;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.UI;

// External fixture, compiled against the actual shipping player's managed DLLs.
// Its only IL hook installs this component in a COPY of the player. Automatic
// world/equipment/output cameras and the normal Player/Session/Rules Updates run.
// No Camera.Render, screenshot/readback, disk writes, LINQ, strings or collection
// growth occurs in a measured frame. Unsupported native counters remain blank.
[DefaultExecutionOrder(2000)]
public sealed class NativeBenchmark : MonoBehaviour
{
    const int Capacity = 18000;
    const double WarmupSeconds = 3, SampleSeconds = 6;
    static readonly long[] scopeStarts=new long[5],scopeTicks=new long[5],scopeCalls=new long[5],scopeEnds=new long[5];
    static readonly string[] scopeNames={"Player.Update","Session.Update","World.Apply","OfficeSign.LateUpdate","UI.Update"};
    static bool measureScopes;
    readonly List<string> report = new List<string>();
    readonly List<Result> results = new List<Result>();
    readonly Dictionary<string,int?> intPrefs = new Dictionary<string,int?>();
    readonly Dictionary<string,float?> floatPrefs = new Dictionary<string,float?>();
    readonly FrameTiming[] timing = new FrameTiming[1];
    readonly double[] frameMs = new double[Capacity], cpuMs = new double[Capacity], gpuMs = new double[Capacity];
    readonly long[] allocBytes = new long[Capacity];
    readonly double[] scratch = new double[Capacity];
    DeadAirSession session;
    PlayerState fixturePlayer;
    DeadAirPlayer player;
    DeadAirFrameRenderer renderer;
    DeadAirWorld world;
    Camera uiCamera;
    RenderTexture outputFrame;
    Func<long> allocated;
    ProfilerRecorder mainThread, renderThread, gpu, gcAlloc, drawCalls, triangles, batches;
    string output, cpuName, gpuName, gcName;
    bool prefsSaved, started, sampling, finished, frameTimingEnabled;
    int phase = -1, samples, cpuSamples, gpuSamples, allocSamples, errors, initialVSync, initialCap, gc0;
    double phaseAt, sampleAt, lastFrameAt;
    long allocationAt, cpuSum, renderSum, drawSum, trianglesSum, batchesSum;
    uint lastFrameTimestampLow;
    Vector3 expectedPosition;
    int worldRenders, outputRenders, sampledWorldRenders, sampledOutputRenders, warmupWorldRenders;
    bool hadDraws,hadTriangles,hadBatches;

    sealed class Result
    {
        public string name;
        public int repeat, quality, frames, cpuCount, gpuCount, allocCount, collections;
        public double seconds, fps, medianMs, p95Ms, p99Ms, cpuMeanMs, cpuP95Ms, gpuMeanMs, gpuP95Ms;
        public double meanAllocBytes, p95AllocBytes, mainNs, renderNs, draws, triangles, batches;
        public int width, height, worldRenders, outputRenders;
    }
    struct Phase
    {
        public string name; public int quality, repeat; public float x,z,y,tx,ty,tz; public bool power, torch, ai;
        public Phase(string name,int quality,int repeat,float x,float z,float y,float tx,float ty,float tz,bool power=true,bool torch=false,bool ai=false)
        { this.name=name;this.quality=quality;this.repeat=repeat;this.x=x;this.z=z;this.y=y;this.tx=tx;this.ty=ty;this.tz=tz;this.power=power;this.torch=torch;this.ai=ai; }
    }
    readonly Phase[] phases = {
        new Phase("powered-gallery",2,1,-8,-13,0,-8,.1f,-16.3f),
        new Phase("powered-archive",2,1,-4.6f,-18.9f,0,0,1.05f,-22),
        new Phase("dark-basement-torch",2,1,-2.4f,-34.8f,-4.2f,-5.2f,-3.1f,-31.8f,false,true),
        new Phase("powered-gallery",1,1,-8,-13,0,-8,.1f,-16.3f),
        new Phase("powered-archive",1,1,-4.6f,-18.9f,0,0,1.05f,-22),
        new Phase("powered-gallery",0,1,-8,-13,0,-8,.1f,-16.3f),
        new Phase("powered-archive",0,1,-4.6f,-18.9f,0,0,1.05f,-22),
        new Phase("menu",2,1,0,0,0,0,0,0)
    };

    public static void Install()
    {
        string[] args=Environment.GetCommandLineArgs();
        if(Array.IndexOf(args,"-deadAirBenchmark")<0)return;
        new GameObject("External native performance benchmark").AddComponent<NativeBenchmark>();
    }
    // Copy-only IL hooks. No strings/collections/boxing per call. The fixed
    // recorder overhead is identical in both players; times include child calls.
    public static void Begin(int index)
    {
        if(!measureScopes)return;
        scopeStarts[index]=System.Diagnostics.Stopwatch.GetTimestamp();scopeCalls[index]++;
    }
    public static void End(int index)
    {
        if(!measureScopes)return;
        scopeTicks[index]+=System.Diagnostics.Stopwatch.GetTimestamp()-scopeStarts[index];scopeEnds[index]++;
    }
    void Start()
    {
        try
        {
            string[] args=Environment.GetCommandLineArgs();int index=Array.IndexOf(args,"-deadAirBenchmark");
            if(index<0||index+1>=args.Length||!Path.IsPathRooted(args[index+1]))throw new ArgumentException("Benchmark output must be absolute.");
            output=args[index+1];Directory.CreateDirectory(output);
            session=UnityEngine.Object.FindAnyObjectByType<DeadAirSession>();
            player=UnityEngine.Object.FindAnyObjectByType<DeadAirPlayer>();
            if(session==null||player==null)throw new InvalidOperationException("Original session/player absent.");
            renderer=player.ViewCamera.GetComponent<DeadAirFrameRenderer>();
            Camera.onPostRender+=Rendered;
            SavePrefs();
            initialVSync=QualitySettings.vSyncCount;initialCap=Application.targetFrameRate;
            Application.runInBackground=true;QualitySettings.vSyncCount=0;Application.targetFrameRate=-1;
            DeadAirSettings.Fullscreen=false;Screen.SetResolution(1280,720,FullScreenMode.Windowed);
            DeadAirSettings.Grain=false;DeadAirSettings.Flicker=false;DeadAirSettings.Brightness=1;
            world=UnityEngine.Object.FindAnyObjectByType<DeadAirBootstrap>().World;
            // Hidden Windows players do not reliably schedule automatic cameras.
            // Execute the same full renderer explicitly, without any CPU readback.
            // Normal Update/LateUpdate simulation remains active and timing includes
            // these renders. Both baseline and optimized copies use this mode.
            world.PrepareRender(player.ViewCamera);
            player.ViewCamera.enabled=false;renderer.OutputCamera.enabled=false;
            outputFrame=new RenderTexture(1280,720,24,RenderTextureFormat.ARGB32);
            if(!outputFrame.Create())throw new InvalidOperationException("Benchmark output target allocation failed.");
            renderer.OutputCamera.targetTexture=outputFrame;
            var canvas=GameObject.Find("BUREAU-9 interface").GetComponent<Canvas>();
            foreach(var t in canvas.GetComponentsInChildren<Transform>(true))t.gameObject.layer=5;
            player.ViewCamera.cullingMask&=~(1<<5);
            uiCamera=new GameObject("Benchmark native UI renderer").AddComponent<Camera>();
            uiCamera.clearFlags=CameraClearFlags.Nothing;uiCamera.cullingMask=1<<5;uiCamera.orthographic=true;
            uiCamera.orthographicSize=540;uiCamera.nearClipPlane=.1f;uiCamera.farClipPlane=1000;uiCamera.depth=100;
            uiCamera.enabled=false;uiCamera.targetTexture=outputFrame;
            canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=uiCamera;canvas.planeDistance=1;
            var available=new List<ProfilerRecorderHandle>();ProfilerRecorderHandle.GetAvailable(available);
            foreach(var handle in available){var description=ProfilerRecorderHandle.GetDescription(handle);report.Add("AVAILABLE: "+description.Name+"; unit="+description.UnitType);}
            mainThread=Counter(available,new[]{"CPU Main Thread Frame Time","Main Thread"},out cpuName);
            renderThread=Counter(available,new[]{"CPU Render Thread Frame Time","Render Thread"},out _);
            gpu=Counter(available,new[]{"GPU Frame Time"},out gpuName);
            gcAlloc=Counter(available,new[]{"GC Allocated In Frame"},out gcName);
            drawCalls=Counter(available,new[]{"Draw Calls Count"},out _);
            triangles=Counter(available,new[]{"Triangles Count"},out _);
            batches=Counter(available,new[]{"Batches Count"},out _);
            hadDraws=drawCalls.Valid;hadTriangles=triangles.Valid;hadBatches=batches.Valid;
            MethodInfo getter=typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread",BindingFlags.Public|BindingFlags.Static);
            if(getter!=null)allocated=(Func<long>)Delegate.CreateDelegate(typeof(Func<long>),getter);
            if(allocated!=null)
            {
                long before=allocated();var probe=new byte[4096];GC.KeepAlive(probe);long after=allocated();
                if(after<=before){allocated=null;report.Add("UNAVAILABLE allocation fallback: controlled 4096-byte allocation did not advance Mono counter.");}
            }
            frameTimingEnabled=FrameTimingManager.IsFeatureEnabled();
            report.Insert(0,"NATIVE BENCHMARK version="+Application.version+"; unity="+Application.unityVersion+"; gpu="+SystemInfo.graphicsDeviceName+"; api="+SystemInfo.graphicsDeviceType+"; color="+QualitySettings.activeColorSpace);
            report.Insert(1,"Explicit complete world/equipment/output/UI render pipeline (hidden Windows automatic cameras suppressed); NO READBACK; 3s+at least5 rendered warmup frames +6s sample; vSync=0; target=-1; 1280x720; normal solo simulation; inspectionPose ended; stationary zero-input fixture.");
            report.Insert(2,"COUNTERS main="+(cpuName??"unavailable")+"; gpu="+(gpuName??"unavailable")+"; gc="+(gcName??"unavailable")+"; FrameTiming enabled="+frameTimingEnabled+"; main-thread cumulative allocation="+(allocated!=null));
            string manifest=Path.GetFullPath(Path.Combine(Application.dataPath,"..","build-info.json"));
            using(var stream=File.OpenRead(manifest))using(var sha=SHA256.Create())report.Add("BUILD-INFO-SHA256: "+BitConverter.ToString(sha.ComputeHash(stream)).Replace("-",""));
            session.Host("Native benchmark","engineer",true,false);session.Command(new NetPacket{type="start"});
            phaseAt=Time.realtimeSinceStartupAsDouble;started=true;
        }
        catch(Exception error){Fail(error);}
    }
    ProfilerRecorder Counter(List<ProfilerRecorderHandle> available,string[] names,out string selected)
    {
        foreach(string name in names)foreach(var handle in available)
        {var d=ProfilerRecorderHandle.GetDescription(handle);if(d.Name!=name)continue;var recorder=ProfilerRecorder.StartNew(d.Category,name,1);if(recorder.Valid){selected=name;return recorder;}recorder.Dispose();}
        selected=null;return default;
    }
    void Update()
    {
        if(!started||finished)return;
        if(phase<0)
        {
            if(Time.realtimeSinceStartupAsDouble-phaseAt<2)return;
            try{BeginNextPhase();}catch(Exception error){Fail(error);}return;
        }
        if(phases[phase].name=="menu")return;
        var state=session.State;var me=fixturePlayer;
        if(state==null||state.phase!="playing"||me==null||me.down){Fail(new InvalidOperationException("Playing fixture lost."));return;}
        // Authoritative move/supplies/game logic still executes; keep only threat
        // and resource depletion out of this reproducible stationary fixture.
        me.invulnerableUntil=999999;me.sanity=100;me.flashlightCharge=100;state.spawnCountdown=999999;
    }
    void BeginNextPhase()
    {
        phase++;
        if(phase>=phases.Length){Complete();return;}
        if(phase==0)
        {
            var transforms=UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include,FindObjectsSortMode.None);
            int leaves=0;foreach(var t in transforms)if(t.childCount==0&&t.GetComponents<Component>().Length==1)leaves++;
            report.Add("LOADED LIVE SCENE transforms="+transforms.Length+"; transform-only leaves="+leaves+"; renderers="+UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include,FindObjectsSortMode.None).Length+"; colliders="+UnityEngine.Object.FindObjectsByType<Collider>(FindObjectsInactive.Include,FindObjectsSortMode.None).Length);
            report.Add("MEMORY startup allocated="+UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong()+"; reserved="+UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong()+"; monoUsed="+UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong()+"; monoHeap="+UnityEngine.Profiling.Profiler.GetMonoHeapSizeLong());
        }
        var p=phases[phase];sampling=false;measureScopes=false;
        DeadAirSettings.ApplyGraphicsPreset(p.quality);DeadAirSettings.Brightness=1;
        warmupWorldRenders=worldRenders;
        if(p.name=="menu")
        {
            UnityEngine.Object.FindAnyObjectByType<DeadAirUI>().ReturnToMenu();player.EndInspection();
            phaseAt=Time.realtimeSinceStartupAsDouble;samples=cpuSamples=gpuSamples=allocSamples=0;
            cpuSum=renderSum=drawSum=trianglesSum=batchesSum=0;lastFrameTimestampLow=0;return;
        }
        session.Host("Native benchmark","engineer",true,false);session.Command(new NetPacket{type="start"});
        var state=session.State;if(state==null||state.phase!="playing")throw new InvalidOperationException("Phase restart did not play.");
        state.power=state.quest.power=p.power;state.breakerOn=state.quest.breakerOn=p.power;
        state.spawnCountdown=999999;state.enemy.active=p.ai;state.enemy.behaviour="patrol";
        var me=state.Player(session.LocalId);fixturePlayer=me;me.invulnerableUntil=999999;me.sanity=100;
        object rules=typeof(DeadAirSession).GetField("rules",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(session);
        typeof(DeadAirRules).GetField("nextScare",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(rules,999999f);
        typeof(DeadAirRules).GetField("nextPeek",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(rules,999999f);
        if(p.ai)
        {
            // One deterministic physical navigation route, outside the camera.
            state.enemy.x=0;state.enemy.y=0;state.enemy.z=-32;
            state.enemy.heardX=0;state.enemy.heardY=0;state.enemy.heardZ=-45;state.enemy.heardUntil=999999;
        }
        float dx=p.tx-p.x,dz=p.tz-p.z;
        player.SetInspectionPose(p.x,p.z,Mathf.Atan2(-dx,-dz),Mathf.Atan2(p.ty-p.y-1.65f,Mathf.Sqrt(dx*dx+dz*dz)),false,p.y);
        player.SetVerificationFlashlight(p.torch);
        // Player's phase transition must run before ending inspection, otherwise
        // its first ordinary frame resets the authored yaw/pitch to the spawn.
        typeof(DeadAirPlayer).GetField("previousPhase",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(player,"playing");
        player.EndInspection();player.SetVerificationMovement(Vector2.zero,false);expectedPosition=player.transform.position;
        phaseAt=Time.realtimeSinceStartupAsDouble;samples=cpuSamples=gpuSamples=allocSamples=0;
        cpuSum=renderSum=drawSum=trianglesSum=batchesSum=0;lastFrameTimestampLow=0;
    }
    void LateUpdate()
    {
        if(!started||finished||phase<0)return;
        // This component runs after the game's normal camera/equipment/UI updates.
        renderer.EnsureTargets();
        player.ViewCamera.Render();renderer.RenderEquipment();renderer.OutputCamera.Render();uiCamera.Render();
        double now=Time.realtimeSinceStartupAsDouble;
        if(!sampling)
        {
            if(now-phaseAt<WarmupSeconds)return;
            if(worldRenders-warmupWorldRenders<5)
            {
                if(now-phaseAt>30)Fail(new InvalidOperationException("No world render callbacks during bounded warmup."));
                return;
            }
            if(Screen.width!=1280||Screen.height!=720){Fail(new InvalidOperationException("Native resolution differs from 1280x720."));return;}
            if(phases[phase].name!="menu"&&(player.transform.position-expectedPosition).sqrMagnitude>.08f*.08f){Fail(new InvalidOperationException("Normal Player moved away from benchmark pose."));return;}
            sampling=true;sampleAt=lastFrameAt=now;allocationAt=allocated!=null?allocated():0;gc0=GC.CollectionCount(0);
            sampledWorldRenders=worldRenders;sampledOutputRenders=outputRenders;
            Array.Clear(scopeTicks,0,scopeTicks.Length);Array.Clear(scopeCalls,0,scopeCalls.Length);Array.Clear(scopeEnds,0,scopeEnds.Length);measureScopes=true;
            return;
        }
        if(samples>=Capacity){Fail(new InvalidOperationException("Sample buffer overflow."));return;}
        frameMs[samples]=(now-lastFrameAt)*1000;lastFrameAt=now;
        if(allocated!=null){long current=allocated();allocBytes[allocSamples++]=Math.Max(0,current-allocationAt);allocationAt=current;}
        else if(gcAlloc.Valid&&gcAlloc.Count>0)allocBytes[allocSamples++]=gcAlloc.LastValue;
        if(mainThread.Valid&&mainThread.Count>0){cpuSum+=mainThread.LastValue;if(mainThread.LastValue>0)cpuMs[cpuSamples++]=mainThread.LastValue*1e-6;}
        if(renderThread.Valid&&renderThread.Count>0)renderSum+=renderThread.LastValue;
        if(gpu.Valid&&gpu.Count>0&&gpu.LastValue>0)gpuMs[gpuSamples++]=gpu.LastValue*1e-6;
        if(drawCalls.Valid&&drawCalls.Count>0)drawSum+=drawCalls.LastValue;
        if(triangles.Valid&&triangles.Count>0)trianglesSum+=triangles.LastValue;
        if(batches.Valid&&batches.Count>0)batchesSum+=batches.LastValue;
        if(frameTimingEnabled)
        {
            FrameTimingManager.CaptureFrameTimings();
            if(FrameTimingManager.GetLatestTimings(1,timing)>0)
            {
                uint timestamp=(uint)timing[0].frameStartTimestamp;
                if(timestamp!=lastFrameTimestampLow)
                {
                    lastFrameTimestampLow=timestamp;
                    if(!gpu.Valid&&timing[0].gpuFrameTime>0)gpuMs[gpuSamples++]=timing[0].gpuFrameTime;
                    if(!mainThread.Valid&&timing[0].cpuMainThreadFrameTime>0)cpuMs[cpuSamples++]=timing[0].cpuMainThreadFrameTime;
                }
            }
        }
        samples++;
        if(now-sampleAt<SampleSeconds)return;
        // From this point timing is OFF; sorting, allocations and report work is
        // excluded from the following phase by its full warmup interval.
        sampling=false;measureScopes=false;
        try
        {
            if(phases[phase].name!="menu"&&(player.transform.position-expectedPosition).sqrMagnitude>.08f*.08f)throw new InvalidOperationException("Pose drift during sampling.");
            var p=phases[phase];var result=new Result{name=p.name,repeat=p.repeat,quality=p.quality,frames=samples,seconds=now-sampleAt,
                fps=samples/(now-sampleAt),medianMs=Percentile(frameMs,samples,.5),p95Ms=Percentile(frameMs,samples,.95),p99Ms=Percentile(frameMs,samples,.99),
                cpuCount=cpuSamples,cpuMeanMs=Mean(cpuMs,cpuSamples),cpuP95Ms=Percentile(cpuMs,cpuSamples,.95),gpuCount=gpuSamples,gpuMeanMs=Mean(gpuMs,gpuSamples),gpuP95Ms=Percentile(gpuMs,gpuSamples,.95),
                allocCount=allocSamples,meanAllocBytes=Mean(allocBytes,allocSamples),p95AllocBytes=Percentile(allocBytes,allocSamples,.95),collections=GC.CollectionCount(0)-gc0,
                mainNs=(double)cpuSum/samples,renderNs=(double)renderSum/samples,draws=(double)drawSum/samples,triangles=(double)trianglesSum/samples,batches=(double)batchesSum/samples,
                width=renderer.SceneFrame.width,height=renderer.SceneFrame.height,worldRenders=worldRenders-sampledWorldRenders,outputRenders=outputRenders-sampledOutputRenders};results.Add(result);
            report.Add("PHASE "+p.name+" quality="+p.quality+" repeat="+p.repeat+" fps="+F(result.fps)+" p95ms="+F(result.p95Ms)+" allocationBytesPerFrame="+(allocSamples>0?F(result.meanAllocBytes):"unavailable"));
            for(int i=0;i<scopeNames.Length;i++)
            {
                double milliseconds=scopeTicks[i]*1000.0/System.Diagnostics.Stopwatch.Frequency;
                report.Add("SCOPE pose="+p.name+" quality="+p.quality+" name="+scopeNames[i]+" calls="+scopeCalls[i]+" ends="+scopeEnds[i]+" totalMs="+F(milliseconds)+" msPerFrame="+F(milliseconds/samples)+" usPerCall="+(scopeCalls[i]>0?F(milliseconds*1000/scopeCalls[i]):""));
                if(scopeCalls[i]!=scopeEnds[i])throw new InvalidOperationException("Copy-only profiling hook missed a return: "+scopeNames[i]);
            }
            report.Add("MEMORY after "+p.name+" quality="+p.quality+" allocated="+UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong()+"; reserved="+UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong()+"; monoUsed="+UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong()+"; monoHeap="+UnityEngine.Profiling.Profiler.GetMonoHeapSizeLong());
            BeginNextPhase();
        }
        catch(Exception error){Fail(error);}
    }
    void Rendered(Camera camera){if(player!=null&&camera==player.ViewCamera)worldRenders++;if(renderer!=null&&camera==renderer.OutputCamera)outputRenders++;}
    double Percentile(double[] values,int count,double percentile){if(count==0)return double.NaN;Array.Copy(values,scratch,count);Array.Sort(scratch,0,count);return scratch[Math.Min(count-1,(int)Math.Ceiling(percentile*count)-1)];}
    double Percentile(long[] values,int count,double percentile){if(count==0)return double.NaN;for(int i=0;i<count;i++)scratch[i]=values[i];Array.Sort(scratch,0,count);return scratch[Math.Min(count-1,(int)Math.Ceiling(percentile*count)-1)];}
    static double Mean(double[] values,int count){if(count==0)return double.NaN;double sum=0;for(int i=0;i<count;i++)sum+=values[i];return sum/count;}
    static double Mean(long[] values,int count){if(count==0)return double.NaN;double sum=0;for(int i=0;i<count;i++)sum+=values[i];return sum/count;}
    static string F(double value)=>double.IsNaN(value)?"":value.ToString("0.000",CultureInfo.InvariantCulture);
    void Fail(Exception error){errors++;report.Add("FAIL: "+error);Complete();}
    void Complete()
    {
        if(finished)return;finished=true;sampling=false;measureScopes=false;
        Camera.onPostRender-=Rendered;
        mainThread.Dispose();renderThread.Dispose();gpu.Dispose();gcAlloc.Dispose();drawCalls.Dispose();triangles.Dispose();batches.Dispose();
        RestorePrefs();QualitySettings.vSyncCount=initialVSync;Application.targetFrameRate=initialCap;
        report.Add("COMPLETE: failures="+errors+"; phases="+results.Count+"; original preferences restored="+!prefsSaved);
        if(output!=null)
        {
            File.WriteAllLines(Path.Combine(output,"native-benchmark-report.txt"),report);
            var csv=new StringBuilder("pose,quality,repeat,frames,seconds,fps,median_frame_ms,p95_frame_ms,p99_frame_ms,cpu_counter_samples,cpu_mean_ms,cpu_p95_ms,gpu_samples,gpu_mean_ms,gpu_p95_ms,allocation_samples,mean_allocation_bytes,p95_allocation_bytes,gc_gen0_collections,draw_calls,triangles,batches,world_width,world_height,actual_world_renders,actual_output_renders\n");
            foreach(var r in results)csv.AppendLine(string.Join(",",new[]{r.name,r.quality.ToString(),r.repeat.ToString(),r.frames.ToString(),F(r.seconds),F(r.fps),F(r.medianMs),F(r.p95Ms),F(r.p99Ms),r.cpuCount.ToString(),F(r.cpuMeanMs),F(r.cpuP95Ms),r.gpuCount.ToString(),F(r.gpuMeanMs),F(r.gpuP95Ms),r.allocCount.ToString(),F(r.meanAllocBytes),F(r.p95AllocBytes),r.collections.ToString(),hadDraws?F(r.draws):"",hadTriangles?F(r.triangles):"",hadBatches?F(r.batches):"",r.width.ToString(),r.height.ToString(),r.worldRenders.ToString(),r.outputRenders.ToString()}));
            File.WriteAllText(Path.Combine(output,"native-benchmark.csv"),csv.ToString());
        }
        if(outputFrame!=null){outputFrame.Release();Destroy(outputFrame);}
        Debug.Log("NATIVE BENCHMARK COMPLETE failures="+errors+" output="+output);Application.Quit(errors==0?0:9);
    }
    void SavePrefs()
    {
        foreach(string key in new[]{"DeadAir.Fullscreen","DeadAir.FilmGrain","DeadAir.Flicker","DeadAir.GraphicsQuality","DeadAir.ShadowQuality","DeadAir.AmbientOcclusion","DeadAir.Bloom","DeadAir.Antialiasing","DeadAir.Reflections"})intPrefs[key]=PlayerPrefs.HasKey(key)?(int?)PlayerPrefs.GetInt(key):null;
        foreach(string key in new[]{"DeadAir.RenderScale","DeadAir.Brightness"})floatPrefs[key]=PlayerPrefs.HasKey(key)?(float?)PlayerPrefs.GetFloat(key):null;
        prefsSaved=true;
        var saved=new List<string>();
        foreach(var p in intPrefs)saved.Add(p.Key+"\tint\t"+(p.Value.HasValue?p.Value.Value.ToString(CultureInfo.InvariantCulture):"absent"));
        foreach(var p in floatPrefs)saved.Add(p.Key+"\tfloat\t"+(p.Value.HasValue?p.Value.Value.ToString("R",CultureInfo.InvariantCulture):"absent"));
        File.WriteAllLines(Path.Combine(output,"original-preferences.tsv"),saved);
    }
    void RestorePrefs()
    {
        if(!prefsSaved)return;
        foreach(var p in intPrefs)if(p.Value.HasValue)PlayerPrefs.SetInt(p.Key,p.Value.Value);else PlayerPrefs.DeleteKey(p.Key);
        foreach(var p in floatPrefs)if(p.Value.HasValue)PlayerPrefs.SetFloat(p.Key,p.Value.Value);else PlayerPrefs.DeleteKey(p.Key);
        PlayerPrefs.Save();prefsSaved=false;
    }
    void OnApplicationQuit()=>RestorePrefs();
    void OnDestroy()=>RestorePrefs();
}
