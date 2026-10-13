using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace DeadAir.Editor
{
    // Additional import checks: the existing importer covers clips, skinning,
    // dimensions and Walk; this checks all actions and actual hand provenance.
    public static class DeadAirImportedCharacterValidation
    {
        static readonly string[] Roles={"engineer","operator","journalist","dispatcher"};
        static readonly string[] Clips={"Idle","Walk","Run","UseItem"};
        static readonly string[] Modes={"flashlight","map","camera","keycard","tape","fuse","fuel-canister","personal"};

        [MenuItem("Dead Air/Validate Imported Cast and Hands")]
        public static void Validate()
        {
            DeadAirCharacterImporter.Validate();
            var evidence=new List<string>();
            foreach(string role in Roles)ValidateModel(role,evidence);
            ValidateHands(evidence);
            string directory=Path.GetFullPath(Path.Combine(Application.dataPath,"../Verification-ImportedCharacters"));
            Directory.CreateDirectory(directory);File.WriteAllLines(Path.Combine(directory,"structural-report.txt"),evidence);
            Debug.Log("IMPORTED CAST PASSED: one source rig per role, finite textured meshes, all four actions deform the skin, and original imported hand meshes/materials survive every class/equipment switch.");
        }

        static void ValidateModel(string role,List<string> evidence)
        {
            var prefab=Resources.Load<GameObject>("World/character_"+role);
            Require(prefab!=null,"Missing imported character "+role);
            Require(prefab.GetComponentsInChildren<DeadAirCharacterMotion>(true).Length==1,
                role+" contains more than one character presentation driver.");
            var visual=prefab.transform.Find("Blender character");
            Require(visual!=null&&prefab.transform.childCount==1,role+" contains an alternate source body or sibling character.");
            var skins=prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            bool restored=IsRestored(visual);
            Require((restored ? skins.Length>1 : skins.Length==1)&&skins.All(renderer=>renderer.transform.IsChildOf(visual)),
                role+(restored ? " must contain separate restored meshes inside one source character." : " must contain exactly one supplied skinned body inside its source character."));
            Require(prefab.GetComponentsInChildren<Animation>(true).Length==1,role+" contains more than one animation rig.");
            var rigRoots=visual.GetComponentsInChildren<Transform>(true).Where(t=>t.name=="root").ToArray();
            if(restored)Require(rigRoots.Length==1,role+" restored clothing must share one skeleton root.");
            var signatures=new HashSet<string>();var textures=new HashSet<Texture>();
            int triangles=0;
            foreach(var skin in skins)
            {
                Require(skin.sharedMesh!=null&&skin.sharedMesh.vertexCount>0,role+" has an empty body mesh.");
                ValidateMesh(skin.sharedMesh,role+"/"+skin.name);
                triangles+=skin.sharedMesh.triangles.Length/3;
                Require(skin.bones.Length>0&&skin.bones.All(b=>b!=null&&b.IsChildOf(visual)),role+" has missing or external skin bones.");
                if(restored)Require(skin.bones.All(b=>b.IsChildOf(rigRoots[0])),role+" clothing uses a different skeleton.");
                string signature=MeshSignature(skin.sharedMesh)+"/"+string.Join("|",skin.bones.Select(b=>AnimationUtility.CalculateTransformPath(b,visual)))+"/"+skin.transform.localToWorldMatrix;
                Require(signatures.Add(signature),role+" renders the same body mesh and skeleton twice.");
                Require(skin.sharedMaterials.Length>=skin.sharedMesh.subMeshCount&&skin.sharedMaterials.All(m=>m!=null),role+" has an unassigned body material.");
                foreach(var material in skin.sharedMaterials)
                {
                    Require(material.shader!=null&&material.shader.name=="Dead Air/Source Surface",role+" has an unsupported body material.");
                    Require(Finite(material.color)&&Finite(material.GetColor("_EmissionColor")),role+" has nonfinite material colors.");
                    if(restored)RestoredAlbedo(role,material);
                    foreach(string property in material.GetTexturePropertyNames())
                    {
                        var texture=material.GetTexture(property);if(texture==null)continue;
                        Require(texture.width>0&&texture.height>0,role+" has an empty "+property+" texture.");
                        if(property=="_MainTex")textures.Add(texture);
                    }
                }
                Require(skin.sharedMesh.boneWeights.Length==skin.sharedMesh.vertexCount,role+" has unweighted imported vertices.");
                foreach(var weight in skin.sharedMesh.boneWeights)
                {
                    float sum=weight.weight0+weight.weight1+weight.weight2+weight.weight3;
                    Require(Finite(sum)&&Mathf.Abs(sum-1)<.02f,role+" has invalid or unnormalised skin weights.");
                    CheckBone(weight.boneIndex0,weight.weight0,skin.bones.Length,role);
                    CheckBone(weight.boneIndex1,weight.weight1,skin.bones.Length,role);
                    CheckBone(weight.boneIndex2,weight.weight2,skin.bones.Length,role);
                    CheckBone(weight.boneIndex3,weight.weight3,skin.bones.Length,role);
                }
            }
            if(restored)Require(triangles<=60000,role+" complete restored character exceeds 60000 triangles: "+triangles);
            Require(textures.Count>0,role+" lost its supplied character texture/atlas.");
            var instance=UnityEngine.Object.Instantiate(prefab);
            try
            {
                var animation=instance.GetComponentInChildren<Animation>(true);
                foreach(string clipName in Clips)
                {
                    var clip=animation.GetClip(clipName);Require(clip!=null,role+" missing "+clipName);
                    clip.SampleAnimation(animation.gameObject,clip.length*.08f);var initial=Bake(instance);
                    float maximum=0;
                    foreach(float phase in new[]{.32f,.67f})
                    {
                        clip.SampleAnimation(animation.gameObject,clip.length*phase);var sampled=Bake(instance);
                        Require(sampled.Length==initial.Length,role+" changed mesh topology while sampling "+clipName);
                        for(int i=0;i<sampled.Length;i++)
                        {
                            Require(Finite(sampled[i]),role+" "+clipName+" deformed a vertex to NaN/Infinity.");
                            maximum=Mathf.Max(maximum,Vector3.Distance(initial[i],sampled[i]));
                        }
                    }
                    Require(maximum>(clipName=="Idle" ? .0005f : .01f),role+" "+clipName+" does not deform the actual character skin.");
                    evidence.Add(role+" "+clipName+": max skin displacement "+maximum.ToString("F4")+" m");
                }
                evidence.Add(role+": one source character, "+skins.Length+" skinned meshes, "+triangles+" total triangles, "+textures.Count+" source textures, no duplicated mesh/skeleton pair.");
            }
            finally{UnityEngine.Object.DestroyImmediate(instance);}
        }

        internal static bool IsRestored(Transform root)
        {
            return root!=null&&root.GetComponentsInChildren<Transform>(true).Any(t=>t.name=="RestoredCast");
        }

        internal static Texture2D RestoredAlbedo(string role,Material material)
        {
            Require(material!=null,role+" has an unassigned restored material.");
            // ImportMaterial prefixes the source Blender name with the role
            // when creating the Unity .mat asset (e.g. operator_operator_restored_body).
            string sourceName=material.name;
            if(sourceName.StartsWith(role+"_"+role+"_restored_",StringComparison.Ordinal))
                sourceName=sourceName.Substring(role.Length+1);
            string prefix=role+"_restored_";
            Require(sourceName.StartsWith(prefix,StringComparison.Ordinal)&&sourceName.Length>prefix.Length&&sourceName!=prefix+"atlas",
                role+" material is not an independently authored restored part: "+material.name);
            string materialPath="Assets/Characters/Materials/"+role+"_"+sourceName+".mat";
            Require(AssetDatabase.GetAssetPath(material)==materialPath,role+" restored material provenance differs: "+material.name);
            string texturePath="Assets/Characters/Textures/Restored/"+role+"/"+sourceName+"_albedo.png";
            var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            Require(texture!=null&&material.mainTexture==texture,role+" restored material must use its matching part texture: "+texturePath);
            return texture;
        }

        static void ValidateHands(List<string> evidence)
        {
            var diagnostic=typeof(DeadAirFirstPersonRig).GetProperty("UsingImportedHands",BindingFlags.Instance|BindingFlags.Public);
            var sourceRole=typeof(DeadAirFirstPersonRig).GetProperty("ImportedHandsRole",BindingFlags.Instance|BindingFlags.Public);
            Require(diagnostic!=null&&sourceRole!=null,"First person rig must expose imported-hand provenance for verification.");
            var stage=new GameObject("Imported hand provenance validation");
            try
            {
                var camera=stage.AddComponent<Camera>();camera.enabled=false;
                var rig=stage.AddComponent<DeadAirFirstPersonRig>();rig.Initialize(camera,WorldData.Load());
                foreach(string role in Roles)
                {
                    var resource=Resources.Load<GameObject>("Characters/Hands/hand_"+role);
                    Require(resource!=null,"Missing extracted hand resource for "+role);
                    var parts=resource.GetComponentsInChildren<MeshFilter>(true);
                    Require(parts.Length==44,role+" must provide all 44 imported forearm/cuff/palm/finger/thumb segments.");
                    foreach(var part in parts)ValidateMesh(part.sharedMesh,role+" imported "+part.name);
                    var sourceMeshes=new HashSet<Mesh>(parts.Select(part=>part.sharedMesh));
                    var sourceMaterials=new HashSet<Material>(resource.GetComponentsInChildren<Renderer>(true).SelectMany(renderer=>renderer.sharedMaterials));
                    foreach(string mode in Modes)
                    foreach(float actionAge in mode=="personal" ? role=="operator" ? new[]{-1f,.6f,2.3f,4.5f,6f} : new[]{-1f,1.25f} : new[]{-1f})
                    {
                        var player=new PlayerState{characterClass=role,inventory=new List<string>{mode=="personal"||mode=="map"||mode=="camera"?"flashlight":mode},selectedSlot=0,
                            uniqueItemEquipped=mode=="personal",cigarettesRemaining=12};
                        rig.ResetPresentation();rig.SynchronizeUniqueAction(actionAge);rig.Present(player,mode=="camera",mode=="map",true,0,true);rig.CompleteEquipTransition();
                        Require((bool)diagnostic.GetValue(rig)&&(string)sourceRole.GetValue(rig)==role,role+" "+mode+" fell back to procedural hands or the previous class.");
                        var right=camera.transform.Find("Low-poly first person hands/Right articulated hand");
                        var left=camera.transform.Find("Low-poly first person hands/Left articulated hand");
                        Require(right!=null&&left!=null,role+" "+mode+" lost the equipment hand transforms.");
                        Require(right.gameObject.activeSelf,role+" "+mode+" hides the selected hand.");
                        if(mode=="map"||mode=="camera"||mode=="personal"&&role=="operator")
                            Require(left.gameObject.activeSelf,role+" "+mode+" requires both imported hands.");
                        foreach(var renderer in right.GetComponentsInChildren<Renderer>(true).Concat(left.GetComponentsInChildren<Renderer>(true)))
                        {
                            var filter=renderer.GetComponent<MeshFilter>();
                            Require(filter!=null&&sourceMeshes.Contains(filter.sharedMesh),role+" "+mode+" renders a procedural hand segment instead of its imported mesh.");
                            Require(renderer.sharedMaterials.All(sourceMaterials.Contains),role+" "+mode+" retains another class's hand materials.");
                            Require(Finite(renderer.bounds.center)&&Finite(renderer.bounds.size)&&renderer.bounds.size.magnitude<1.3f,
                                role+" "+mode+" has an invalid or oversized hand segment.");
                        }
                        evidence.Add(role+" "+mode+(actionAge>=0?" action "+actionAge.ToString("F2")+" s":"")+": imported mesh and material identity verified for both hands.");
                    }
                }
            }
            finally{UnityEngine.Object.DestroyImmediate(stage);}
        }

        static Vector3[] Bake(GameObject instance)
        {
            var vertices=new List<Vector3>();
            foreach(var renderer in instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var baked=new Mesh();
                try{renderer.BakeMesh(baked);vertices.AddRange(baked.vertices.Select(vertex=>instance.transform.InverseTransformPoint(renderer.transform.TransformPoint(vertex))));}
                finally{UnityEngine.Object.DestroyImmediate(baked);}
            }
            return vertices.ToArray();
        }
        static void ValidateMesh(Mesh mesh,string name)
        {
            Require(mesh!=null&&mesh.vertexCount>0,name+" mesh is missing.");
            Require(mesh.vertices.All(Finite)&&mesh.normals.All(Finite)&&mesh.uv.All(uv=>Finite(uv.x)&&Finite(uv.y)),name+" has nonfinite geometry or UV data.");
            Require(mesh.uv.Length==mesh.vertexCount,name+" lost its texture coordinates.");
            Require(mesh.normals.Length==mesh.vertexCount,name+" lost its surface normals.");
            Require(Finite(mesh.bounds.center)&&Finite(mesh.bounds.size)&&mesh.bounds.size.magnitude>0,name+" has invalid geometry bounds.");
        }
        static string MeshSignature(Mesh mesh)
        {
            unchecked
            {
                long hash=17;foreach(var vertex in mesh.vertices){hash=hash*31+Mathf.RoundToInt(vertex.x*10000);hash=hash*31+Mathf.RoundToInt(vertex.y*10000);hash=hash*31+Mathf.RoundToInt(vertex.z*10000);}
                foreach(int index in mesh.triangles)hash=hash*31+index;return mesh.vertexCount+"/"+mesh.triangles.Length+"/"+hash;
            }
        }
        static void CheckBone(int index,float weight,int count,string role){Require(Finite(weight)&&weight>=0&&weight<=1,role+" has a nonfinite or invalid skin weight.");if(weight>.00001f)Require(index>=0&&index<count,role+" has a skin weight outside its actual skeleton.");}
        static bool Finite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);
        static bool Finite(Vector3 value)=>Finite(value.x)&&Finite(value.y)&&Finite(value.z);
        static bool Finite(Color value)=>Finite(value.r)&&Finite(value.g)&&Finite(value.b)&&Finite(value.a);
        static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException("Imported character validation: "+message);}
    }
}
