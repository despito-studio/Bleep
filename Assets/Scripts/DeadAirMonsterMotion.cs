using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DeadAir
{
    // The source monster is made of rigid, textured parts. Build pivots around
    // those parts instead of replacing the model or stretching its pixel art.
    public sealed class DeadAirMonsterMotion : MonoBehaviour
    {
        sealed class Joint
        {
            public Transform transform;
            public Quaternion rest;
            public Joint(Transform value){transform=value;rest=value.localRotation;}
            public void Pose(float x=0,float y=0,float z=0)
            {if(transform!=null)transform.localRotation=rest*Quaternion.Euler(x,y,z);}
        }

        readonly List<UnityEngine.Object> owned=new List<UnityEngine.Object>();
        Transform model;
        Joint torso,head,leftArm,rightArm,leftElbow,rightElbow,leftHand,rightHand,
            leftLeg,rightLeg,leftKnee,rightKnee,leftFoot,rightFoot;
        Vector3 lastPosition;
        float lastElapsed,gait,moveSpeed,moveAt=-100,lean;
        bool initialized,hasSnapshot;
        public int AnimatedJointCount {get;private set;}
        public bool HasBlindfold {get;private set;}
        public Transform HeadPivot => head?.transform;

        public void Initialize()
        {
            if(initialized)return;
            initialized=true;
            var children=new List<Transform>();
            foreach(Transform child in transform)children.Add(child);
            model=Group("Anchor / articulated source model",transform,Vector3.zero);
            foreach(var child in children)child.SetParent(model,false);
            var upper=Group("Anchor spine",model,new Vector3(0,1.15f,0));
            foreach(var child in children)
            {
                var position=child.localPosition;
                if(child.childCount>=4 && position.y>.95f && position.y<1.3f)
                {
                    var hip=new Joint(child);
                    var knee=new Joint(Regroup("Anchor knee",child,new Vector3(0,-.64f,0),t=>t.localPosition.y<-.64f));
                    var foot=new Joint(Regroup("Anchor ankle",knee.transform,new Vector3(0,-.41f,0),t=>t.localPosition.y<-.41f));
                    if(position.x<0){leftLeg=hip;leftKnee=knee;leftFoot=foot;}
                    else{rightLeg=hip;rightKnee=knee;rightFoot=foot;}
                    continue;
                }
                child.SetParent(upper,true);
                if(child.childCount>=8 && Mathf.Abs(position.x)>.4f && position.y>2)
                {
                    var shoulder=new Joint(child);
                    var elbow=new Joint(Regroup("Anchor elbow",child,new Vector3(0,-.64f,0),t=>t.localPosition.y<-.64f));
                    var hand=new Joint(Regroup("Anchor wrist",elbow.transform,new Vector3(0,-.49f,.02f),t=>t.localPosition.y<-.47f));
                    if(position.x<0){leftArm=shoulder;leftElbow=elbow;leftHand=hand;}
                    else{rightArm=shoulder;rightElbow=elbow;rightHand=hand;}
                }
                else if(child.childCount>=10 && Mathf.Abs(position.x)<.05f && position.y>2.4f)
                {
                    head=new Joint(child);
                    FitBlindfold(child);
                }
            }
            torso=new Joint(upper);
            // The imported soles were 12cm below the source floor plane.
            model.localPosition=new Vector3(0,.13f,0);
            CalibrateSkin();
            foreach(var joint in new[]{torso,head,leftArm,rightArm,leftElbow,rightElbow,leftHand,rightHand,
                leftLeg,rightLeg,leftKnee,rightKnee,leftFoot,rightFoot})
                if(joint!=null)AnimatedJointCount++;
        }

        static Transform Group(string name,Transform parent,Vector3 position)
        {
            var value=new GameObject(name).transform;value.SetParent(parent,false);value.localPosition=position;return value;
        }

        static Transform Regroup(string name,Transform parent,Vector3 position,Predicate<Transform> include)
        {
            var children=new List<Transform>();
            foreach(Transform child in parent)if(include(child))children.Add(child);
            var pivot=Group(name,parent,position);
            foreach(var child in children)child.SetParent(pivot,true);
            return pivot;
        }

        void FitBlindfold(Transform skull)
        {
            foreach(Transform part in skull)
            {
                var position=part.localPosition;
                if(position.z>.17f && position.y>.07f && position.y<.12f && Mathf.Abs(position.x)>.06f)
                    part.gameObject.SetActive(false);
            }
            var texture=new Texture2D(16,16,TextureFormat.RGBA32,false)
                {name="Anchor blindfold / coarse worn cloth",filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Repeat,hideFlags=HideFlags.DontSave};
            var pixels=new Color32[256];
            for(int y=0;y<16;y++)for(int x=0;x<16;x++)
            {
                byte value=(byte)(31+((x*17+y*29+x*y*3)%16)+(y%3==0?7:0));
                pixels[y*16+x]=new Color32(value,(byte)(value+1),(byte)(value-3),255);
            }
            texture.SetPixels32(pixels);texture.Apply(false,true);owned.Add(texture);
            var material=new Material(Shader.Find("Dead Air/Source Surface"))
                {name="Anchor blindfold cloth",hideFlags=HideFlags.DontSave};
            material.SetColor("_Color",new Color(.44f,.43f,.39f));
            material.SetColor("_EmissionColor",Color.black);material.SetFloat("_Glossiness",0);
            material.SetTexture("_MainTex",texture);material.SetFloat("_Cull",2);owned.Add(material);
            Cloth("Opaque eye wrap",skull,new Vector3(0,.085f,.196f),new Vector3(.38f,.106f,.045f),material);
            Cloth("Eye wrap left",skull,new Vector3(-.184f,.085f,.014f),new Vector3(.035f,.106f,.35f),material);
            Cloth("Eye wrap right",skull,new Vector3(.184f,.085f,.014f),new Vector3(.035f,.106f,.35f),material);
            Cloth("Eye wrap knot",skull,new Vector3(.09f,.065f,-.184f),new Vector3(.11f,.13f,.055f),material);
            HasBlindfold=true;
        }

        static void Cloth(string name,Transform parent,Vector3 position,Vector3 scale,Material material)
        {
            var part=GameObject.CreatePrimitive(PrimitiveType.Cube);part.name=name;
            part.transform.SetParent(parent,false);part.transform.localPosition=position;part.transform.localScale=scale;
            var collider=part.GetComponent<Collider>();
            if(Application.isPlaying)Destroy(collider);else DestroyImmediate(collider);
            var renderer=part.GetComponent<MeshRenderer>();renderer.sharedMaterial=material;
            renderer.shadowCastingMode=ShadowCastingMode.On;
        }

        void CalibrateSkin()
        {
            var copies=new Dictionary<Material,Material>();
            var shader=Shader.Find("Dead Air/Source Surface");
            foreach(var renderer in GetComponentsInChildren<Renderer>(true))
            {
                var materials=renderer.sharedMaterials;
                for(int i=0;i<materials.Length;i++)
                {
                    var source=materials[i];if(source==null || source.name=="Anchor blindfold cloth")continue;
                    if(!copies.TryGetValue(source,out var copy))
                    {
                        copy=new Material(source){name=source.name+" / anchor diffuse",hideFlags=HideFlags.DontSave};
                        copy.shader=shader;copy.SetColor("_EmissionColor",Color.black);
                        copies.Add(source,copy);owned.Add(copy);
                    }
                    materials[i]=copy;
                }
                renderer.sharedMaterials=materials;
                renderer.shadowCastingMode=ShadowCastingMode.On;
                renderer.receiveShadows=true;
            }
        }

        public void Present(EnemyState enemy,float elapsed,bool paused)
        {
            Initialize();
            var position=new Vector3(-enemy.x,enemy.y,enemy.z);
            if(!hasSnapshot || elapsed<lastElapsed)
            {
                lastPosition=position;gait=0;moveSpeed=0;moveAt=-100;lean=0;
                transform.rotation=Quaternion.Euler(0,enemy.yaw*Mathf.Rad2Deg,0);
            }
            float distance=Vector3.Distance(position,lastPosition);
            if(!paused && distance>.00001f && distance<3)
            {
                moveSpeed=Mathf.Min(6,distance/Mathf.Max(.016f,elapsed-lastElapsed));
                gait+=distance*4.7f;moveAt=elapsed;
            }
            if(distance>=3 || elapsed-moveAt>.2f)moveSpeed=0;
            float dt=paused?0:Mathf.Min(Time.deltaTime,.1f);
            float wantedLean=enemy.behaviour=="peek"?Mathf.Sign(enemy.peekLean):0;
            lean=Mathf.MoveTowards(lean,wantedLean,dt*4.5f);
            transform.position=position;
            transform.rotation=Quaternion.Slerp(transform.rotation,Quaternion.Euler(0,enemy.yaw*Mathf.Rad2Deg,0),1-Mathf.Exp(-dt*9));
            if(!paused)SamplePose(enemy.stunned?"stunned":enemy.behaviour,elapsed,gait,enemy.stunned?0:moveSpeed,lean);
            hasSnapshot=true;lastPosition=position;lastElapsed=elapsed;
        }

        public void SetPreviewPose(string behaviour,float time,float normalizedGait=.25f,float peekSide=1)
        {
            Initialize();SamplePose(behaviour,time,normalizedGait*Mathf.PI*2,
                behaviour=="patrol"?1:behaviour=="investigate"?3:0,behaviour=="peek"?peekSide:0);
        }

        void SamplePose(string behaviour,float time,float phase,float speed,float side)
        {
            bool listening=behaviour=="search" || behaviour=="peek" || behaviour=="door";
            float stride=Mathf.Clamp01(speed/2.5f),step=Mathf.Sin(phase),other=Mathf.Sin(phase+Mathf.PI);
            float breath=Mathf.Sin(time*1.2f),tilt=Mathf.Sin(time*.67f)*2;
            torso?.Pose(4+stride*6+breath*.7f,Mathf.Sin(phase)*stride*2,-side*17+step*stride*1.6f);
            head?.Pose(8+breath*1.8f,Mathf.Sin(time*.42f)*(listening?12:4),side*5+(listening?9:3)+tilt);
            leftArm?.Pose(other*stride*14-4,0,side<-.1f?-11:1);
            rightArm?.Pose(step*stride*12-7,0,side>.1f?11:-2);
            leftElbow?.Pose(-7-Mathf.Max(0,step)*stride*12,0,0);
            rightElbow?.Pose(-12-Mathf.Max(0,other)*stride*9,0,0);
            leftHand?.Pose(-4,3,Mathf.Sin(time*1.7f)*3);
            rightHand?.Pose(-8,-4,Mathf.Sin(time*1.3f+2)*4);
            leftLeg?.Pose(step*stride*20,0,0);rightLeg?.Pose(other*stride*20,0,0);
            leftKnee?.Pose(Mathf.Max(0,-step)*stride*23);rightKnee?.Pose(Mathf.Max(0,-other)*stride*23);
            leftFoot?.Pose(-Mathf.Max(0,step)*stride*10);rightFoot?.Pose(-Mathf.Max(0,other)*stride*10);
            model.localPosition=new Vector3(0,.13f+Mathf.Abs(step)*stride*.018f,0);
            if(behaviour=="door")
            {
                rightArm?.Pose(-57,-9,8);rightElbow?.Pose(-28);rightHand?.Pose(0,0,-12);
            }
            else if(behaviour=="stunned")
            {
                torso?.Pose(13,0,Mathf.Sin(time*17)*1.5f);
                head?.Pose(29,0,-11);leftArm?.Pose(-18,0,-7);rightArm?.Pose(-21,0,8);
            }
            else if(Mathf.Abs(side)>.1f)
            {
                var arm=side>0?rightArm:leftArm;var elbow=side>0?rightElbow:leftElbow;
                arm?.Pose(-17,side*8,side*12);elbow?.Pose(-31);
            }
        }

        void OnDestroy()
        {
            foreach(var item in owned)if(item!=null)
            {if(Application.isPlaying)Destroy(item);else DestroyImmediate(item);}
            owned.Clear();
        }
    }
}
