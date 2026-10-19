using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DeadAir
{
	public sealed partial class DeadAirSchoolWorld
	{
		private struct PickupPlacement
		{
			public readonly string id;

			public readonly string label;

			public readonly Vector3 position;

			public PickupPlacement(string id, string label, float x, float y, float z)
			{
				this.id = id;
				this.label = label;
				position = new Vector3(x, y, z);
			}
		}

		private sealed class FlickeringLamp
		{
			public Light light;

			public Renderer bulb;

			public float baseIntensity;

			public bool worksNormally;

			public float flickerOffset;

			public bool lastBulbOn = true;

			public bool isInterior;
		}

		public sealed class PillItem
		{
			public string id;

			public GameObject go;

			public Vector3 position;

			public bool collected;
		}

		public sealed class EvidenceTapeItem
		{
			public string id;

			public string label;

			public GameObject go;

			public Vector3 position;

			public bool collected;
		}

		public const int LoadingStepCount = 17;

		private static readonly PickupPlacement[] TapePlacements = new PickupPlacement[5]
		{
			new PickupPlacement("tape-1", "Касета 1: Щоденник чергового учня", -12.75f, 1.55f, -1.45f),
			new PickupPlacement("tape-2", "Касета 2: Звіт вчительської ради", -28.25f, 1.75f, -16f),
			new PickupPlacement("tape-3", "Касета 3: Запис гуртка окультизму", 31.75f, 5.17f, -4.25f),
			new PickupPlacement("tape-4", "Касета 4: Запис спостереження 2-A", -28.25f, 4.75f, -1.45f),
			new PickupPlacement("tape-5", "Касета 5: Останній репортаж журналіста", -12f, 7.55f, -35.5f)
		};

		private static readonly PickupPlacement[] PillPlacements = new PickupPlacement[9]
		{
			new PickupPlacement("pill-1", null, -28.25f, 1.55f, -1.45f),
			new PickupPlacement("pill-2", null, 24.75f, 1.73f, -2.5f),
			new PickupPlacement("pill-3", null, -27f, 4.73f, -16.2f),
			new PickupPlacement("pill-4", null, 15.95f, 4.63f, -17.75f),
			new PickupPlacement("pill-5", null, 0f, 7.55f, -11f),
			new PickupPlacement("pill-6", null, 12f, 7.55f, -29.4f),
			new PickupPlacement("pill-7", null, 18f, 0.54f, -51f),
			new PickupPlacement("pill-8", null, -14f, 0.5f, 64f),
			new PickupPlacement("pill-9", null, 0f, 1.27f, 97f)
		};

		private Material matCassetteShell;

		private Material matCassetteFace;

		private Material matCassetteWindow;

		private Material matCassetteReel;

		private Material matCassetteLabel;

		private Material matCassetteAccent;

		private Material matPillAmber;

		private Material matPillLabel;

		public readonly Transform Root;

		private readonly WorldData data;



		private readonly List<FlickeringLamp> lamps = new List<FlickeringLamp>();

		private readonly List<PillItem> pills = new List<PillItem>();

		private readonly List<EvidenceTapeItem> evidenceTapes = new List<EvidenceTapeItem>();

		private readonly MaterialPropertyBlock lampPropertyBlock = new MaterialPropertyBlock();

		private Camera viewCamera;
        private DeadAirArtReflectionProbes artReflectionProbes;

		private float nextLightUpdate;

		private static readonly int LampColorId = Shader.PropertyToID("_Color");
		private Renderer generatorIndicatorRenderer;
		private Renderer breakerIndicatorRenderer;
		private Transform breakerHandleTransform;

		private Material matPlaster;

		private Material matAsphalt;

		private Material matRoadZebra;

		private Material matWoodFloor;

		private Material matBark;

		private Material matFoliage;

		private Material matTorii;

		private Material matStone;

		private Material matGlass;

		private Material matDarkMetal;

		private Material matConcrete;

		private Material matChalkboard;

		private Material matWhite;

		private Material matHouseStucco;

		private Material matKawaraRoof;

		private Material matBlockWall;

		private Material matGuardrail;

		private Material matBooks;

		private Material matLabCounter;

		private Material matGold;

		private Material matDoorWood;

		private Material matShoeLocker;

		private Material matDeskWood;

		private Material matPaper;

		private Material matTrashCan;

		private Material matCan;

		internal static int PillPickupCount => PillPlacements.Length;

		public IReadOnlyList<PillItem> Pills => pills;

		public IReadOnlyList<EvidenceTapeItem> EvidenceTapes => evidenceTapes;

		private void BuildRearSchoolExtension()
		{
			Transform transform = new GameObject("Rear Teaching Wing (20m Extension)").transform;
			transform.SetParent(Root, worldPositionStays: false);
			for (int i = 0; i < 3; i++)
			{
				float num = i switch
				{
					1 => 3.8f, 
					0 => 0.6f, 
					_ => 7.4f, 
				};
				float num2 = ((i == 0) ? 3.2f : 3.6f);
				Transform transform2 = new GameObject("Rear Wing Floor " + (i + 1)).transform;
				transform2.SetParent(transform, worldPositionStays: false);
				switch (i)
				{
				case 0:
					CreateBox(transform2, new Vector3(0f, 0.3f, -32f), new Vector3(72f, 0.6f, 20f), matWoodFloor, addCollider: true, "Ground Slab");
					break;
				case 1:
					CreateBox(transform2, new Vector3(0f, num + 0.05f, -32f), new Vector3(72f, 0.1f, 20f), matWoodFloor, addCollider: true, "Second Floor Deck");
					break;
				default:
					CreateBox(transform2, new Vector3(-19.4f, num + 0.05f, -32f), new Vector3(33.2f, 0.1f, 20f), matWoodFloor, addCollider: true, "Third Floor West Deck");
					CreateBox(transform2, new Vector3(19.4f, num + 0.05f, -32f), new Vector3(33.2f, 0.1f, 20f), matWoodFloor, addCollider: true, "Third Floor East Deck");
					CreateBox(transform2, new Vector3(0f, num + 0.05f, -23.5f), new Vector3(5.6f, 0.1f, 3f), matWoodFloor, addCollider: true, "Third Floor South Landing");
					CreateBox(transform2, new Vector3(0f, num + 0.05f, -39.5f), new Vector3(5.6f, 0.1f, 5f), matWoodFloor, addCollider: true, "Third Floor North Landing");
					break;
				}
				BuildRearTransitionWall(transform2, num, num2);
				for (int j = -1; j <= 1; j += 2)
				{
					BuildRearCorridorSide(transform2, (float)j * 5.2f, num, num2, i);
					CreateBox(transform2, new Vector3((float)j * 20.6f, num + num2 * 0.5f, -32f), new Vector3(30.8f, num2, 0.22f), matPlaster, addCollider: true, "Rear Classroom Divider");
					float centerX = (float)j * 20.5f;
					BuildCompactClassroom(transform2, centerX, num, -27f, i + 1 + ((j < 0) ? "-D" : "-E"), i * 4 + ((j < 0) ? 1 : 2));
					BuildCompactClassroom(transform2, centerX, num, -37f, i + 1 + ((j < 0) ? "-F" : "-G"), i * 4 + ((j < 0) ? 3 : 4));
				}
				CreateBox(transform2, new Vector3(-4.85f, num + 1.1f, -31.5f), new Vector3(0.12f, 1.8f, 2.6f), matWoodFloor, addCollider: false, "Campus Map Board");
				CreateBox(transform2, new Vector3(-4.76f, num + 1.1f, -31.5f), new Vector3(0.03f, 1.45f, 2.25f), matPaper, addCollider: false, "Campus Map Paper");
				CreateBox(transform2, new Vector3(4.8f, num + 0.75f, -31.5f), new Vector3(0.55f, 1.5f, 2.4f), matShoeLocker, addCollider: true, "Cleaning Supply Cabinet");
				CreateInteriorLamp(transform2, new Vector3(0f, num + 3.05f, -24.5f));
				CreateInteriorLamp(transform2, new Vector3(0f, num + 3.05f, -39f));
			}
			BuildRearUpperStairs(transform);
		}

		private void BuildRearTransitionWall(Transform parent, float floorY, float height)
		{
			float y = floorY + height * 0.5f;
			CreateBox(parent, new Vector3(-25f, y, -22f), new Vector3(22f, height, 0.22f), matPlaster, addCollider: true, "Old-to-New West Wall");
			CreateBox(parent, new Vector3(-8.6f, y, -22f), new Vector3(6.8f, height, 0.22f), matPlaster);
			CreateBox(parent, new Vector3(0f, y, -22f), new Vector3(10.6f, height, 0.22f), matPlaster, addCollider: true, "Stairwell North Wall");
			CreateBox(parent, new Vector3(8.6f, y, -22f), new Vector3(6.8f, height, 0.22f), matPlaster);
			CreateBox(parent, new Vector3(25f, y, -22f), new Vector3(22f, height, 0.22f), matPlaster, addCollider: true, "Old-to-New East Wall");
			int[] array = new int[2] { -1, 1 };
			foreach (int num in array)
			{
				CreateBox(parent, new Vector3((float)num * 13f, floorY + 2.4f + (height - 2.4f) * 0.5f, -22f), new Vector3(2f, height - 2.4f, 0.22f), matPlaster, addCollider: true, "Passage Lintel");
				BuildClassroomDoor(parent, new Vector3((float)num * 13f, floorY, -22f), 0f, "Rear Wing Passage");
			}
		}

		private void BuildRearCorridorSide(Transform parent, float x, float floorY, float height, int level)
		{
			float y = floorY + height * 0.5f;
			float[] array = new float[3] { -23.75f, -31.5f, -39.75f };
			float[] array2 = new float[3] { 3.5f, 8f, 4.5f };
			for (int i = 0; i < array.Length; i++)
			{
				CreateBox(parent, new Vector3(x, y, array[i]), new Vector3(0.22f, height, array2[i]), matPlaster, addCollider: true, "Rear Hall Partition");
			}
			float[] array3 = new float[2] { -26.5f, -36.5f };
			foreach (float num in array3)
			{
				CreateBox(parent, new Vector3(x, floorY + 2.4f + (height - 2.4f) * 0.5f, num), new Vector3(0.22f, height - 2.4f, 2f), matPlaster, addCollider: true, "Classroom Door Lintel");
				BuildClassroomDoor(parent, new Vector3(x, floorY, num), 90f, level + 1 + " / Rear " + ((num > -30f) ? "Front" : "Back"));
			}
		}

		private void BuildRearUpperStairs(Transform parent)
		{
			Transform transform = new GameObject("Rear Wing Stair 2F to 3F").transform;
			transform.SetParent(parent, worldPositionStays: false);
			for (int i = 0; i < 20; i++)
			{
				float y = 3.8f + ((float)i + 0.5f) * 0.17999999f;
				float z = -25f - ((float)i + 0.5f) * 0.6f;
				CreateBox(transform, new Vector3(0f, y, z), new Vector3(5.3f, 0.17999999f, 0.6f), matConcrete, addCollider: true, "Third Floor Stair Tread");
			}
			CreateRamp(transform, new Vector3(0f, 5.6f, -31f), new Vector3(5.3f, 0.1f, 12f), 16.7f, "Third Floor Smooth Stair Collider");
			for (int j = -1; j <= 1; j += 2)
			{
				CreateBox(transform, new Vector3((float)j * 2.7f, 6.1f, -31f), new Vector3(0.08f, 0.08f, 12f), matDarkMetal, addCollider: false, "Sloped Stair Handrail").transform.localRotation = Quaternion.Euler(16.7f, 0f, 0f);
				CreateBox(transform, new Vector3((float)j * 2.8f, 7.95f, -31f), new Vector3(0.08f, 1f, 11.5f), matDarkMetal, addCollider: true, "Third Floor Void Guardrail");
			}
		}

		private void BuildThirdFloorInteriors()
		{
			Transform transform = new GameObject("Third Floor Interiors").transform;
			transform.SetParent(Root, worldPositionStays: false);
			CreateBox(transform, new Vector3(-21f, 7.45f, -11f), new Vector3(30f, 0.1f, 22f), matWoodFloor, addCollider: true, "Third Floor West Deck");
			CreateBox(transform, new Vector3(21f, 7.45f, -11f), new Vector3(30f, 0.1f, 22f), matWoodFloor, addCollider: true, "Third Floor East Deck");
			CreateBox(transform, new Vector3(0f, 7.45f, -4.25f), new Vector3(12f, 0.1f, 8.5f), matWoodFloor, addCollider: true, "Third Floor South Deck");
			CreateBox(transform, new Vector3(0f, 7.45f, -11f), new Vector3(12f, 0.1f, 5f), matWoodFloor, addCollider: true, "Third Floor Hall Deck");
			float[] array = new float[2] { -8.5f, -13.5f };
			foreach (float num in array)
			{
				int[] array2 = new int[2] { -1, 1 };
				foreach (int num2 in array2)
				{
					CreateBox(transform, new Vector3((float)num2 * 32.6f, 9.2f, num), new Vector3(6.8f, 3.6f, 0.22f), matPlaster);
					CreateBox(transform, new Vector3((float)num2 * 20.75f, 9.2f, num), new Vector3(14f, 3.6f, 0.22f), matPlaster);
					CreateBox(transform, new Vector3((float)num2 * 8.5f, 9.2f, num), new Vector3(7.5f, 3.6f, 0.22f), matPlaster);
					float[] array3 = new float[2] { 13f, 28.5f };
					foreach (float num3 in array3)
					{
						CreateBox(transform, new Vector3((float)num2 * num3, 10.45f, num), new Vector3(1.5f, 1.1f, 0.22f), matPlaster);
						BuildClassroomDoor(transform, new Vector3((float)num2 * num3, 7.4f, num), (num < -10f) ? 180f : 0f, "3F " + ((num2 < 0) ? "West" : "East"));
					}
				}
			}
			CreateBox(transform, new Vector3(-3f, 9.2f, -8.5f), new Vector3(4f, 3.6f, 0.22f), matPlaster);
			CreateBox(transform, new Vector3(3f, 9.2f, -8.5f), new Vector3(4f, 3.6f, 0.22f), matPlaster);
			CreateBox(transform, new Vector3(0f, 10.45f, -8.5f), new Vector3(2f, 1.1f, 0.22f), matPlaster);
			BuildClassroomDoor(transform, new Vector3(0f, 7.4f, -8.5f), 0f, "3F Media Club");
			array = new float[4] { -20.5f, -5f, 5f, 20.5f };
			foreach (float x in array)
			{
				CreateBox(transform, new Vector3(x, 9.2f, -4.25f), new Vector3(0.22f, 3.6f, 8.5f), matPlaster);
			}
			array = new float[4] { -20.5f, -6f, 6f, 20.5f };
			foreach (float x2 in array)
			{
				CreateBox(transform, new Vector3(x2, 9.2f, -17.75f), new Vector3(0.22f, 3.6f, 8.5f), matPlaster);
			}
			BuildCompactClassroom(transform, -28.25f, 7.4f, -4.25f, "3-A", 31);
			BuildCompactClassroom(transform, -12.75f, 7.4f, -4.25f, "3-B", 32);
			BuildCompactClassroom(transform, 12.75f, 7.4f, -4.25f, "3-C", 33);
			BuildCompactClassroom(transform, 28.25f, 7.4f, -4.25f, "3-D", 34);
			BuildCompactClassroom(transform, -28.25f, 7.4f, -17.75f, "3-E", 35);
			BuildCompactClassroom(transform, -12.75f, 7.4f, -17.75f, "3-F", 36);
			BuildCompactClassroom(transform, 12.75f, 7.4f, -17.75f, "3-G", 37);
			BuildCompactClassroom(transform, 28.25f, 7.4f, -17.75f, "3-H", 38);
			CreateBox(transform, new Vector3(0f, 8.4f, -4.5f), new Vector3(5.5f, 1.5f, 0.55f), matShoeLocker, addCollider: true, "Media Archive Cabinets");
			CreateBox(transform, new Vector3(0f, 9.4f, -0.3f), new Vector3(5.5f, 1.7f, 0.08f), matChalkboard, addCollider: false, "Media Club Planning Board");
			BuildExitSign(transform, new Vector3(-6f, 10.3f, -13.4f));
			BuildExitSign(transform, new Vector3(6f, 10.3f, -13.4f));
			CreateInteriorLamp(transform, new Vector3(0f, 10.7f, -11f));
			CreateBox(transform, new Vector3(-3f, 7.45f, -13.65f), new Vector3(5.6f, 0.1f, 0.35f), matWoodFloor, addCollider: true, "Third Floor Stair Arrival Threshold");
			CreateBox(transform, new Vector3(2.7f, 7.95f, -13.55f), new Vector3(6.3f, 1f, 0.08f), matDarkMetal, addCollider: true, "Third Floor South Stair Guardrail");
			CreateBox(transform, new Vector3(-0.6f, 7.95f, -16f), new Vector3(0.08f, 1f, 4.8f), matDarkMetal, addCollider: true, "Third Floor Inner Stair Guardrail");
			CreateBox(transform, new Vector3(2.7f, 7.95f, -18.4f), new Vector3(6.3f, 1f, 0.08f), matDarkMetal, addCollider: true, "Third Floor North Stair Guardrail");
		}

		private void BuildCompactClassroom(Transform parent, float centerX, float floorY, float centerZ, string label, int seed)
		{
			Transform transform = new GameObject("Mapped Classroom " + label).transform;
			transform.SetParent(parent, worldPositionStays: false);
			bool broadRearRoom = centerZ < -22f;
            float teachingSide = ClassroomTeachingSideX(centerX, centerZ);
            BuildTeachingWall(transform, broadRearRoom ? new Vector3(centerX, floorY, centerZ + 4.78f) : new Vector3(teachingSide, floorY, centerZ), broadRearRoom ? 180f : 90f);
            if (!broadRearRoom)
            {
                Transform teachingDesk = BuildWorktable(transform, new Vector3(teachingSide + 1.35f, floorY + .45f, centerZ), new Vector3(1.25f, .9f, .7f), matDeskWood, "Teacher teaching desk", true);
                teachingDesk.localRotation = Quaternion.Euler(0f, 90f, 0f);
            }
			CreateBox(transform, new Vector3(centerX, floorY + 0.9f, centerZ + 2.7f), new Vector3(1.7f, 0.7f, 0.8f), matDeskWood, addCollider: true, "Teacher Desk");
			if (centerZ != -17.75f)
			{
				int[] array = new int[2] { -1, 1 };
				foreach (int num in array)
				{
					float x = centerX + (float)num * 2.85f;
					CreateBox(transform, new Vector3(x, floorY + 1.1f, centerZ - 4.05f), new Vector3(2.8f, 2f, 0.45f), matShoeLocker, addCollider: true, "Classroom Storage");
					CreateBox(transform, new Vector3(x, floorY + 1.65f, centerZ - 4.32f), new Vector3(2.6f, 0.06f, 0.04f), matPaper, addCollider: false, "Storage Labels");
				}
			}
			for (int j = 0; j < 2; j++)
			{
				for (int k = 0; k < 3; k++)
				{
					float x2 = centerX + (float)(k - 1) * 2.25f;
					float z = centerZ + 0.6f - (float)j * 2.1f;
                    BuildAuthenticDesk(transform, new Vector3(x2, floorY, z), (broadRearRoom ? 180f : 90f) + (((seed + j + k) % 5 == 0) ? 9f : 0f), false, false);
				}
			}
			CreateBox(transform, new Vector3(centerX - 3.5f, floorY + 1.7f, centerZ - 4.38f), new Vector3(0.7f, 0.9f, 0.05f), matPaper, addCollider: false, "Class Timetable");
			CreateInteriorLamp(transform, new Vector3(centerX, floorY + 3.1f, centerZ));
		}

		private void BuildRearPlayground(Transform parent)
		{
			Transform transform = new GameObject("Rear School Playground & Sports Court").transform;
			transform.SetParent(parent, worldPositionStays: false);
			Material mat = CreateMat("CourtLinePaint", matWhite.shader, new Color(0.91f, 0.91f, 0.82f), 0.12f);
			Material mat2 = CreateMat("CourtWeatheredBlue", matAsphalt.shader, new Color(0.19f, 0.3f, 0.37f), 0.18f);
			Material mat3 = CreateMat("PlaygroundRubber", matAsphalt.shader, new Color(0.28f, 0.22f, 0.18f), 0.08f);
            DeadAirArtMaterials.Apply(mat2, "asphalt");
            DeadAirArtMaterials.Apply(mat3, "rubber");
			CreateBox(transform, new Vector3(0f, 0.03f, -43.8f), new Vector3(5.2f, 0.06f, 3.6f), matStone, addCollider: false, "Rear Exit Walkway");
			CreateBox(transform, new Vector3(0f, 0.32f, -43.15f), new Vector3(2.25f, 0.12f, 3f), matConcrete, addCollider: true, "Rear Exit Accessible Ramp").transform.localRotation = Quaternion.Euler(9.5f, 0f, 0f);
			CreateBox(transform, new Vector3(0f, 0.015f, -55f), new Vector3(28f, 0.03f, 19f), mat2, addCollider: false, "Basketball Court Surface");
			for (int i = -1; i <= 1; i += 2)
			{
				CreateBox(transform, new Vector3((float)i * 13.5f, 0.037f, -55f), new Vector3(0.11f, 0.012f, 18f), mat, addCollider: false, "Court Sideline");
				CreateBox(transform, new Vector3(0f, 0.037f, -55f + (float)i * 9f), new Vector3(27f, 0.012f, 0.11f), mat, addCollider: false, "Court Baseline");
			}
			CreateBox(transform, new Vector3(0f, 0.038f, -55f), new Vector3(27f, 0.012f, 0.11f), mat, addCollider: false, "Half Court Line");
			for (int j = 0; j < 12; j++)
			{
				float f = (float)j * MathF.PI / 6f;
				CreateBox(transform, new Vector3(Mathf.Cos(f) * 2.1f, 0.039f, -55f + Mathf.Sin(f) * 2.1f), new Vector3(0.11f, 0.012f, 1.08f), mat, addCollider: false, "Center Circle Mark").transform.localRotation = Quaternion.Euler(0f, (float)(-j) * 30f, 0f);
			}
			BuildBasketballHoop(transform, -47.5f, 1);
			BuildBasketballHoop(transform, -62.5f, -1);
			CreateBox(transform, new Vector3(-23f, 0.022f, -56f), new Vector3(13f, 0.04f, 18f), mat3, addCollider: false, "Swing Safety Surface");
			CreateBox(transform, new Vector3(23f, 0.022f, -58f), new Vector3(12f, 0.04f, 17f), mat3, addCollider: false, "Slide Safety Surface");
			BuildPlaygroundSwings(transform);
			BuildPlaygroundSlide(transform);
			for (int k = -1; k <= 1; k += 2)
			{
				float x = (float)k * 35.7f;
				CreateRamp(transform, new Vector3(x, 1.25f, -56f), new Vector3(0.15f, 2.5f, 28f), 0f, "Playground Side Fence Collision");
				for (float num = -44f; num >= -68f; num -= 4f)
				{
					CreateCylinder(transform, new Vector3(x, 1.25f, num), 0.045f, 2.5f, matDarkMetal, addCollider: true, "Fence Post");
				}
				for (int l = 1; l <= 4; l++)
				{
					CreateBox(transform, new Vector3(x, (float)l * 0.5f, -56f), new Vector3(0.045f, 0.035f, 26f), matDarkMetal, addCollider: false, "Fence Wire");
				}
			}
			CreateBox(transform, new Vector3(0f, 2.55f, -70f), new Vector3(74f, 0.07f, 0.07f), matDarkMetal, addCollider: false, "Rear Fence Top Rail");
			BuildPlaygroundBench(transform, new Vector3(18f, 0f, -51f), 90f);
			BuildPlaygroundBench(transform, new Vector3(-19f, 0f, -66f), -90f);
			CreateBox(transform, new Vector3(30f, 0.3f, -65f), new Vector3(2.4f, 0.6f, 0.8f), matConcrete, addCollider: true, "Low Planter");
			CreateBox(transform, new Vector3(30f, 0.7f, -65f), new Vector3(2f, 0.35f, 0.6f), matFoliage, addCollider: false, "Planter Shrubs");
		}

		private void BuildRearFence(Transform parent)
		{
			Transform transform = new GameObject("Rear Sports Fence").transform;
			transform.SetParent(parent, worldPositionStays: false);
			CreateRamp(transform, new Vector3(0f, 1.3f, -70f), new Vector3(74f, 2.6f, 0.15f), 0f, "Rear Fence Collision");
			CreateBox(transform, new Vector3(0f, 0.22f, -70f), new Vector3(74f, 0.44f, 0.24f), matConcrete, addCollider: false, "Concrete Fence Curb");
			for (float num = -36f; num <= 36f; num += 4f)
			{
				CreateCylinder(transform, new Vector3(num, 1.35f, -70f), 0.045f, 2.7f, matDarkMetal, addCollider: false, "Mesh Fence Post");
			}
			for (int i = 1; i <= 5; i++)
			{
				CreateBox(transform, new Vector3(0f, (float)i * 0.48f, -70f), new Vector3(74f, 0.025f, 0.035f), matDarkMetal, addCollider: false, "Mesh Fence Wire");
			}
		}

		private void BuildBasketballHoop(Transform parent, float z, int direction)
		{
			Transform transform = new GameObject("Basketball Goal").transform;
			transform.SetParent(parent, worldPositionStays: false);
			transform.localPosition = new Vector3(0f, 0f, z);
			CreateCylinder(transform, new Vector3(0f, 1.8f, (float)direction * 0.9f), 0.12f, 3.6f, matDarkMetal, addCollider: true, "Goal Pole");
			CreateBox(transform, new Vector3(0f, 3.55f, (float)direction * 0.55f), new Vector3(0.09f, 0.09f, 0.8f), matDarkMetal, addCollider: false, "Goal Arm");
			CreateBox(transform, new Vector3(0f, 3.45f, (float)direction * 0.1f), new Vector3(1.8f, 1.05f, 0.08f), matWhite, addCollider: false, "Backboard");
			CreateBox(transform, new Vector3(0f, 3.45f, (float)direction * 0.03f), new Vector3(0.65f, 0.47f, 0.02f), matDarkMetal, addCollider: false, "Backboard Target");
			for (int i = 0; i < 8; i++)
			{
				float f = (float)i * MathF.PI * 0.25f;
				CreateBox(transform, new Vector3(Mathf.Cos(f) * 0.36f, 3.07f, (float)(-direction) * 0.5f + Mathf.Sin(f) * 0.36f), new Vector3(0.045f, 0.045f, 0.28f), matTorii, addCollider: false, "Basketball Rim Segment").transform.localRotation = Quaternion.Euler(0f, (float)(-i) * 45f, 0f);
			}
		}

		private void BuildPlaygroundSwings(Transform parent)
		{
			Transform transform = new GameObject("Two Seat Swing Set").transform;
			transform.SetParent(parent, worldPositionStays: false);
			transform.localPosition = new Vector3(-23f, 0f, -56f);
			CreateBox(transform, new Vector3(0f, 2.95f, 0f), new Vector3(6f, 0.15f, 0.15f), matDarkMetal, addCollider: false, "Swing Crossbeam");
			float[] array = new float[2] { -2.8f, 2.8f };
			foreach (float x in array)
			{
				CreateCylinder(transform, new Vector3(x, 1.48f, -0.8f), 0.075f, 2.96f, matDarkMetal, addCollider: true, "Swing Frame Post");
				CreateCylinder(transform, new Vector3(x, 1.48f, 0.8f), 0.075f, 2.96f, matDarkMetal, addCollider: true, "Swing Frame Post");
			}
			array = new float[2] { -1.5f, 1.5f };
			foreach (float num in array)
			{
				CreateBox(transform, new Vector3(num, 0.58f, 0f), new Vector3(1.05f, 0.09f, 0.4f), matWoodFloor, addCollider: true, "Swing Seat");
				float[] array2 = new float[2] { -0.43f, 0.43f };
				foreach (float num2 in array2)
				{
					CreateCylinder(transform, new Vector3(num + num2, 1.77f, 0f), 0.024f, 2.35f, matDarkMetal, addCollider: false, "Swing Chain");
				}
			}
		}

		private void BuildPlaygroundSlide(Transform parent)
		{
			Transform transform = new GameObject("Playground Slide").transform;
			transform.SetParent(parent, worldPositionStays: false);
			transform.localPosition = new Vector3(23f, 0f, -58f);
			CreateBox(transform, new Vector3(0f, 2.15f, 0f), new Vector3(1.65f, 0.16f, 1.65f), matWoodFloor, addCollider: true, "Slide Platform");
			int[] array = new int[2] { -1, 1 };
			foreach (int num in array)
			{
				CreateCylinder(transform, new Vector3((float)num * 0.72f, 1.05f, 0.72f), 0.065f, 2.1f, matDarkMetal, addCollider: true, "Platform Leg");
				CreateCylinder(transform, new Vector3((float)num * 0.72f, 1.05f, -0.72f), 0.065f, 2.1f, matDarkMetal, addCollider: true, "Platform Leg");
				CreateBox(transform, new Vector3((float)num * 0.77f, 2.55f, 0f), new Vector3(0.08f, 0.78f, 1.6f), matTorii, addCollider: false, "Safety Rail");
			}
			CreateBox(transform, new Vector3(0f, 1.2f, -2.1f), new Vector3(1.28f, 0.11f, 4f), matDarkMetal, addCollider: true, "Slide Chute").transform.localRotation = Quaternion.Euler(-29f, 0f, 0f);
			for (int j = 0; j < 5; j++)
			{
				CreateBox(transform, new Vector3(0f, 0.45f + (float)j * 0.34f, 1.72f - (float)j * 0.34f), new Vector3(1.3f, 0.1f, 0.22f), matWoodFloor, addCollider: true, "Ladder Rung");
			}
		}

		private void BuildPlaygroundBench(Transform parent, Vector3 pos, float yaw)
		{
            GameObject scanned = DeadAirSchoolPhysics.InstallScanned(parent, "wooden-bench", pos, Quaternion.Euler(0f, yaw, 0f), new Vector3(2.5f, .49f, 0.6f));
            if (scanned != null)
            {
                AddCollisionProxy(scanned.transform, new Vector3(0f, 0.45f, 0f), new Vector3(2.5f, 0.08f, 0.58f), "Bench seat collision");

                return;
            }
			Transform transform = new GameObject("Schoolyard Bench").transform;
			transform.SetParent(parent, worldPositionStays: false);
			transform.localPosition = pos;
			transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
			CreateBox(transform, new Vector3(0f, 0.48f, 0f), new Vector3(2.5f, 0.12f, 0.58f), matWoodFloor, addCollider: true, "Bench Seat");
			CreateBox(transform, new Vector3(0f, 0.87f, 0.25f), new Vector3(2.5f, 0.65f, 0.11f), matWoodFloor, addCollider: false, "Bench Back");
			CreateBox(transform, new Vector3(-0.95f, 0.23f, 0f), new Vector3(0.08f, 0.46f, 0.58f), matDarkMetal, addCollider: false, "Bench Leg");
			CreateBox(transform, new Vector3(0.95f, 0.23f, 0f), new Vector3(0.08f, 0.46f, 0.58f), matDarkMetal, addCollider: false, "Bench Leg");
		}

		public IEnumerator InitializeSteps()
		{
			BuildRoadAndCrosswalk();
			yield return null;
			BuildLeftNeighborhood();
			yield return null;
			BuildRightNeighborhood();
			yield return null;
			BuildBackYardAndForest();
			yield return null;
			BuildSchoolCourtyard();
			yield return null;
			BuildSchoolBuilding();
			yield return null;
			BuildFirstFloorInteriors();
			yield return null;
			BuildSecondFloorInteriors();
			yield return null;
			BuildThirdFloorInteriors();
			yield return null;
			BuildStairs();
			yield return null;
			BuildRearSchoolExtension();
			yield return null;
			BuildTraditionalPark();
			yield return null;
			UpgradePark();
			yield return null;
			BuildNewsVan();
			yield return null;
			SpawnSanityPills();
			yield return null;
			SpawnEvidenceTapes();
			yield return null;
			SetupLighting();
            schoolDoorLeaves = Root.GetComponentsInChildren<DeadAirArtMotionSlidingLeaf>(true);
            BatchSchoolArchitecture();
			artReflectionProbes = DeadAirArtReflectionProbes.Attach(Root);
			yield return null;
		}

		private void UpgradePark()
		{
			Transform transform = Root.Find("Traditional Japanese Park");
			if (transform == null)
			{
				return;
			}
			Shader shader = ((matStone != null) ? matStone.shader : Shader.Find("Standard"));
			Material material = DeadAirArtMaterials.Create("Park grass and soil", "soil", new Color(0.45f, 0.48f, 0.35f), 0.05f);
			material.mainTextureScale = Vector2.one;
			Material material2 = DeadAirArtMaterials.Create("Compacted park gravel", "concrete", new Color(0.59f, 0.56f, 0.48f), 0.08f);
			material2.mainTextureScale = Vector2.one;
			Material material3 = DeadAirArtMaterials.Create("Mixed park foliage", "foliage", new Color(0.32f, 0.4f, 0.24f), 0.04f);
			Material material4 = DeadAirArtMaterials.Create("Park understory", "foliage", new Color(0.38f, 0.43f, 0.25f), 0.03f);
			Material water = CreateMat("Shallow rain garden", shader, new Color(0.19f, 0.27f, 0.26f), 0.62f);
			Transform transform2 = transform.Find("Park Moss Ground");
			if (transform2 != null)
			{
				Renderer component = transform2.GetComponent<Renderer>();
				if (component != null)
				{
					component.sharedMaterial = material;
					component.shadowCastingMode = ShadowCastingMode.Off;
				}
			}
			for (int i = 0; i < transform.childCount; i++)
			{
				Transform child = transform.GetChild(i);
				if (child.name.StartsWith("Stone Path", StringComparison.Ordinal) || child.name == "Shrine Approach Path")
				{
					child.gameObject.SetActive(value: false);
				}
			}
			AddParkPath(transform, "Winding entrance path", material2, 2.35f, new Vector2(0f, 50f), new Vector2(0.6f, 57f), new Vector2(-0.4f, 64f), new Vector2(0.5f, 72f), new Vector2(0f, 80f), new Vector2(0f, 86f), new Vector2(0.2f, 93f), new Vector2(0f, 98f));
			AddParkPath(transform, "West garden walk", material2, 1.35f, new Vector2(0f, 79f), new Vector2(-7f, 80.4f), new Vector2(-15f, 79.6f), new Vector2(-21f, 81f));
			AddParkPath(transform, "Gazebo walk", material2, 1.35f, new Vector2(0.5f, 72f), new Vector2(7f, 73.3f), new Vector2(12f, 72f), new Vector2(16f, 72f));
			AddParkPath(transform, "East garden walk", material2, 1.25f, new Vector2(0f, 87f), new Vector2(8f, 86f), new Vector2(16f, 86.7f), new Vector2(21f, 88f));
			int num = 0;
			for (int j = 0; j < transform.childCount; j++)
			{
				Transform child2 = transform.GetChild(j);
				if (child2.name != "Ancient Tree")
				{
					continue;
				}
                if (child2.GetComponentInChildren<LODGroup>(true) != null) { num++; continue; }
				for (int k = 0; k < child2.childCount; k++)
				{
					Transform child3 = child2.GetChild(k);
					if (!(child3.localPosition.y < 4.8f))
					{
						Renderer component2 = child3.GetComponent<Renderer>();
						if (component2 != null)
						{
							component2.enabled = false;
						}
					}
				}
				List<Vector3> vertices = new List<Vector3>(180);
				List<int> triangles = new List<int>(900);
				float num2 = 0.86f + (float)(num % 5) * 0.055f;
				AddCanopyLobe(vertices, triangles, new Vector3(0f, 7.3f, 0f), new Vector3(3.3f, 2.15f, 3.1f) * num2);
				AddCanopyLobe(vertices, triangles, new Vector3(-2.1f, 7.8f, 0.7f), new Vector3(2.3f, 1.7f, 2f) * num2);
				AddCanopyLobe(vertices, triangles, new Vector3(2.1f, 7.2f, -0.7f), new Vector3(2.35f, 1.85f, 2.1f) * num2);
				AddCanopyLobe(vertices, triangles, new Vector3(0.1f, 9f, -0.2f), new Vector3(2.2f, 1.55f, 2.1f) * num2);
				AddParkMesh(child2, "Irregular tree canopy", vertices, triangles, material3, castShadows: true);
				num++;
			}
			List<Vector3> vertices2 = new List<Vector3>(2000);
			List<int> triangles2 = new List<int>(9000);
			List<Vector3> vertices3 = new List<Vector3>(1200);
			List<int> triangles3 = new List<int>(5400);
			System.Random random = new System.Random(41219);
			for (int l = 0; l < 54; l++)
			{
				float num3 = -40f + (float)random.NextDouble() * 80f;
				float num4 = 54f + (float)random.NextDouble() * 52f;
				if (!(Mathf.Abs(num3) < 6f) && (!(num3 > 10f) || !(num3 < 21f) || !(num4 > 68f) || !(num4 < 77f)) && (!(Mathf.Abs(num3) < 7f) || !(num4 > 95f)) && (!(num3 < -19f) || !(num4 > 96f)))
				{
					float num5 = 0.55f + (float)random.NextDouble() * 0.65f;
					if (!TryInstallSchoolShrub(transform, new Vector3(num3, .03f, num4), new Vector3(num5 * 2f, .76f + num5 * .7f, num5 * 1.6f), Mathf.Repeat(num3 * 17f + num4 * 11f, 360f)))
                        AddCanopyLobe(vertices2, triangles2, new Vector3(num3, 0.35f, num4), new Vector3(num5, 0.38f + num5 * 0.35f, num5 * 0.8f), 7, 3);
				}
			}
			for (int m = 0; m < 30; m++)
			{
				float num6 = -39f + (float)random.NextDouble() * 78f;
				float num7 = 53f + (float)random.NextDouble() * 55f;
				if (!(Mathf.Abs(num6) < 4.3f) && (!(num6 > 12f) || !(num6 < 20f) || !(num7 > 68f) || !(num7 < 76f)))
				{
					float num8 = 0.18f + (float)random.NextDouble() * 0.32f;
					AddCanopyLobe(vertices3, triangles3, new Vector3(num6, 0.08f, num7), new Vector3(num8, num8 * 0.38f, num8 * 0.8f), 6, 2);
				}
			}
			AddParkMesh(transform, "Batched native shrubs", vertices2, triangles2, material4, castShadows: false);
			AddParkMesh(transform, "Batched weathered stones", vertices3, triangles3, matStone, castShadows: false);
			AddRainGarden(transform, water);
			int num9 = 0;
			for (int n = 0; n < transform.childCount; n++)
			{
				Transform child4 = transform.GetChild(n);
				if (!(child4.name != "Street Lamp"))
				{
					Light component3 = child4.GetComponent<Light>();
					if (component3 != null)
					{
						component3.shadows = ((num9 == 1 || num9 == 6) ? LightShadows.Hard : LightShadows.None);
						component3.range = 12f;
					}
					num9++;
				}
			}
		}



		private void AddParkPath(Transform parent, string name, Material material, float halfWidth, params Vector2[] points)
		{
			List<Vector3> list = new List<Vector3>(points.Length * 2);
			List<int> list2 = new List<int>((points.Length - 1) * 6);
			List<Vector2> list3 = new List<Vector2>(points.Length * 2);
			float num = 0f;
			for (int i = 0; i < points.Length; i++)
			{
				Vector2 normalized = (points[Mathf.Min(i + 1, points.Length - 1)] - points[Mathf.Max(i - 1, 0)]).normalized;
				Vector2 vector = new Vector2(0f - normalized.y, normalized.x);
				if (i > 0)
				{
					num += Vector2.Distance(points[i - 1], points[i]);
				}
				Vector2 vector2 = points[i] + vector * halfWidth;
				Vector2 vector3 = points[i] - vector * halfWidth;
				list.Add(new Vector3(vector2.x, 0.034f, vector2.y));
				list.Add(new Vector3(vector3.x, 0.034f, vector3.y));
				list3.Add(new Vector2(0f, num * 0.22f));
				list3.Add(new Vector2(1f, num * 0.22f));
				if (i != 0)
				{
					int num2 = (i - 1) * 2;
					list2.Add(num2);
					list2.Add(num2 + 2);
					list2.Add(num2 + 1);
					list2.Add(num2 + 1);
					list2.Add(num2 + 2);
					list2.Add(num2 + 3);
				}
			}
			AddParkMesh(parent, name, list, list2, material, castShadows: false, list3);
		}

		private static void AddCanopyLobe(List<Vector3> vertices, List<int> triangles, Vector3 center, Vector3 radius, int sides = 9, int rings = 5)
		{
			int count = vertices.Count;
			for (int i = 0; i <= rings; i++)
			{
				float f = Mathf.Lerp(-1.48f, 1.48f, (float)i / (float)rings);
				float num = Mathf.Cos(f);
				for (int j = 0; j < sides; j++)
				{
					float f2 = (float)j * MathF.PI * 2f / (float)sides;
					float num2 = 0.9f + 0.1f * Mathf.Sin((float)j * 2.9f + center.x * 0.35f);
					vertices.Add(center + new Vector3(Mathf.Cos(f2) * radius.x * num * num2, Mathf.Sin(f) * radius.y, Mathf.Sin(f2) * radius.z * num * num2));
				}
			}
			for (int k = 0; k < rings; k++)
			{
				for (int l = 0; l < sides; l++)
				{
					int num3 = (l + 1) % sides;
					int item = count + k * sides + l;
					int item2 = count + k * sides + num3;
					int item3 = count + (k + 1) * sides + l;
					int item4 = count + (k + 1) * sides + num3;
					triangles.Add(item);
					triangles.Add(item3);
					triangles.Add(item2);
					triangles.Add(item2);
					triangles.Add(item3);
					triangles.Add(item4);
				}
			}
		}

		private static GameObject AddParkMesh(Transform parent, string name, List<Vector3> vertices, List<int> triangles, Material material, bool castShadows, List<Vector2> uv = null)
		{
			if (vertices.Count == 0)
			{
				return null;
			}
			Mesh mesh = new Mesh
			{
				name = name
			};
			mesh.SetVertices(vertices);
			mesh.SetTriangles(triangles, 0);
			if (uv != null)
			{
				mesh.SetUVs(0, uv);
			}
            mesh.RecalculateNormals();
            if (uv == null)
            {
                var metricUV = new List<Vector2>(vertices.Count);
                Vector3[] normals = mesh.normals;
                for (int i = 0; i < vertices.Count; i++)
                {
                    Vector3 p = vertices[i], n = normals[i];
                    metricUV.Add(Mathf.Abs(n.y) > 0.7f ? new Vector2(p.x, p.z) : Mathf.Abs(n.x) > Mathf.Abs(n.z) ? new Vector2(p.z, p.y) : new Vector2(p.x, p.y));
                }
                mesh.SetUVs(0, metricUV);
            }
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            GameObject gameObject = new GameObject(name);
			gameObject.transform.SetParent(parent, worldPositionStays: false);
			gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
			MeshRenderer meshRenderer = gameObject.AddComponent<MeshRenderer>();
			meshRenderer.sharedMaterial = material;
			meshRenderer.shadowCastingMode = (castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off);
			return gameObject;
		}

		private void AddRainGarden(Transform park, Material water)
		{
			List<Vector3> list = new List<Vector3>(15)
			{
				new Vector3(-27f, 0.042f, 101f)
			};
			List<int> list2 = new List<int>(39);
			for (int i = 0; i <= 13; i++)
			{
				float num = (float)i * MathF.PI * 2f / 13f;
				float num2 = 3.9f + Mathf.Sin(num * 3f) * 0.25f;
				list.Add(new Vector3(-27f + Mathf.Cos(num) * num2, 0.042f, 101f + Mathf.Sin(num) * num2 * 0.7f));
				if (i > 0)
				{
					list2.Add(0);
					list2.Add(i + 1);
					list2.Add(i);
				}
			}
			AddParkMesh(park, "Shallow rain garden water", list, list2, water, castShadows: false);
			List<Vector3> vertices = new List<Vector3>(250);
			List<int> triangles = new List<int>(1000);
			for (int j = 0; j < 18; j++)
			{
				float num3 = (float)j * MathF.PI * 2f / 18f;
				float num4 = 4.2f + Mathf.Sin(num3 * 3f) * 0.25f;
				AddCanopyLobe(vertices, triangles, new Vector3(-27f + Mathf.Cos(num3) * num4, 0.1f, 101f + Mathf.Sin(num3) * num4 * 0.7f), new Vector3(0.48f, 0.17f, 0.4f), 6, 2);
			}
			AddParkMesh(park, "Batched rain garden stones", vertices, triangles, matStone, castShadows: false);
		}

		internal static bool TryGetPillPosition(string id, out Vector3 position)
		{
			return TryGetPosition(PillPlacements, id, out position);
		}

		internal static bool TryGetTapePosition(string id, out Vector3 position)
		{
			return TryGetPosition(TapePlacements, id, out position);
		}

		private static bool TryGetPosition(PickupPlacement[] placements, string id, out Vector3 position)
		{
			for (int i = 0; i < placements.Length; i++)
			{
				if (string.Equals(placements[i].id, id, StringComparison.Ordinal))
				{
					position = placements[i].position;
					return true;
				}
			}
			position = default(Vector3);
			return false;
		}

		private void EnsurePickupMaterials()
		{
			if (!(matCassetteShell != null))
			{
				Shader shader = matDarkMetal.shader;
				matCassetteShell = CreateMat("CassetteShell", shader, new Color(0.07f, 0.08f, 0.09f), 0.25f);
				matCassetteFace = CreateMat("CassetteFace", shader, new Color(0.15f, 0.17f, 0.18f), 0.36f);
				matCassetteWindow = CreateMat("CassetteWindow", shader, new Color(0.025f, 0.03f, 0.035f), 0.56f);
				matCassetteReel = CreateMat("CassetteReel", shader, new Color(0.68f, 0.65f, 0.56f), 0.52f);
				matCassetteLabel = CreateMat("CassettePaperLabel", shader, new Color(0.82f, 0.79f, 0.67f), 0.08f);
				matCassetteAccent = CreateMat("CassetteAccent", shader, new Color(0.72f, 0.33f, 0.16f), 0.18f);
				matCassetteAccent.SetColor("_EmissionColor", new Color(0.13f, 0.045f, 0.015f));
				matPillAmber = CreateMat("MedicineAmberGlass", shader, new Color(0.49f, 0.22f, 0.06f), 0.68f);
				matPillLabel = CreateMat("MedicineLabel", shader, new Color(0.83f, 0.87f, 0.78f), 0.12f);
				matPillLabel.SetColor("_EmissionColor", new Color(0.09f, 0.11f, 0.08f));
			}
		}

		private void SpawnSanityPills()
		{
			EnsurePickupMaterials();
			for (int i = 0; i < PillPlacements.Length; i++)
			{
				PickupPlacement pickupPlacement = PillPlacements[i];
				GameObject gameObject = new GameObject("SanityPill_" + pickupPlacement.id);
				gameObject.transform.SetParent(Root, worldPositionStays: false);
				gameObject.transform.localPosition = pickupPlacement.position;
				CreateCylinder(gameObject.transform, new Vector3(0f, 0.12f, 0f), 0.085f, 0.24f, matPillAmber, addCollider: false, "Amber Bottle");
				CreateCylinder(gameObject.transform, new Vector3(0f, 0.25f, 0f), 0.085f, 0.045f, matWhite, addCollider: false, "Safety Cap");
				CreateBox(gameObject.transform, new Vector3(0f, 0.125f, -0.085f), new Vector3(0.145f, 0.09f, 0.009f), matPillLabel, addCollider: false, "Medicine Label");
				CreateBox(gameObject.transform, new Vector3(0f, 0.165f, -0.091f), new Vector3(0.13f, 0.009f, 0.004f), matCassetteAccent, addCollider: false, "Label Stripe");
				pills.Add(new PillItem
				{
					id = pickupPlacement.id,
					go = gameObject,
					position = pickupPlacement.position
				});
			}
		}

		private void SpawnEvidenceTapes()
		{
			EnsurePickupMaterials();
			for (int i = 0; i < TapePlacements.Length; i++)
			{
				PickupPlacement pickupPlacement = TapePlacements[i];
				GameObject gameObject = new GameObject("EvidenceTape_" + pickupPlacement.id);
				gameObject.transform.SetParent(Root, worldPositionStays: false);
				gameObject.transform.localPosition = pickupPlacement.position;
				CreateBox(gameObject.transform, new Vector3(0f, 0.026f, 0f), new Vector3(0.31f, 0.05f, 0.195f), matCassetteShell, addCollider: false, "Cassette Chassis");
				CreateBox(gameObject.transform, new Vector3(0f, 0.052f, 0f), new Vector3(0.295f, 0.008f, 0.18f), matCassetteFace, addCollider: false, "Cassette Face");
				CreateBox(gameObject.transform, new Vector3(0f, 0.058f, 0.047f), new Vector3(0.19f, 0.005f, 0.075f), matCassetteWindow, addCollider: false, "Tape Window");
				CreateBox(gameObject.transform, new Vector3(0f, 0.058f, -0.048f), new Vector3(0.23f, 0.005f, 0.056f), matCassetteLabel, addCollider: false, "Paper Label");
				CreateBox(gameObject.transform, new Vector3(0f, 0.062f, -0.064f), new Vector3(0.21f, 0.003f, 0.008f), matCassetteAccent, addCollider: false, "Recording Stripe");
				for (int j = -1; j <= 1; j += 2)
				{
					CreateCylinder(gameObject.transform, new Vector3((float)j * 0.058f, 0.063f, 0.047f), 0.028f, 0.006f, matCassetteReel, addCollider: false, "Tape Reel");
					CreateCylinder(gameObject.transform, new Vector3((float)j * 0.058f, 0.067f, 0.047f), 0.01f, 0.004f, matCassetteWindow, addCollider: false, "Reel Hub");
				}
				evidenceTapes.Add(new EvidenceTapeItem
				{
					id = pickupPlacement.id,
					label = pickupPlacement.label,
					go = gameObject,
					position = pickupPlacement.position
				});
			}
		}

		public DeadAirSchoolWorld(Transform parent, WorldData data)
		{
			this.data = data;
			GameObject gameObject = new GameObject("CHAPTER 2 - JAPANESE SCHOOL & NEIGHBORHOOD");
			gameObject.transform.SetParent(parent, worldPositionStays: false);
			Root = gameObject.transform;
			Root.localScale = new Vector3(-1f, 1f, 1f);
			InitMaterials();
		}

		private void InitMaterials()
		{
			Shader shader = Shader.Find("Dead Air/Source Surface") ?? Shader.Find("Standard");
			Shader shader2 = Shader.Find("Dead Air/Window Glass") ?? Shader.Find("Standard");
            matPlaster = CreateMat("SchoolPlaster", shader, new Color(0.82f, 0.81f, 0.75f), 0.15f);
            matWoodFloor = CreateMat("ParquetWood", shader, new Color(0.7f, 0.57f, 0.4f), 0.35f);
            matDeskWood = CreateMat("DeskWood", shader, new Color(0.67f, 0.54f, 0.37f), 0.25f);
            matConcrete = CreateMat("ConcreteLight", shader, new Color(0.64f, 0.64f, 0.59f), 0.2f);
            matChalkboard = CreateMat("Chalkboard", shader, new Color(0.12f, 0.21f, 0.18f), 0.05f);
            matPaper = CreateMat("PaperSheet", shader, new Color(0.88f, 0.85f, 0.76f), 0.1f);
            matBooks = CreateMat("BookCovers", shader, new Color(0.32f, 0.38f, 0.34f), 0.2f);
			matGlass = new Material(shader2)
			{
				name = "SchoolWindowGlass",
				hideFlags = HideFlags.DontSave
			};
			matGlass.SetColor("_Color", new Color(0.25f, 0.45f, 0.55f, 0.18f));
			matGlass.SetFloat("_Glossiness", 0.95f);
			matGlass.SetFloat("_ReflectionStrength", 0.35f);
            DeadAirArtMaterials.Apply(matGlass, "glass");
			matAsphalt = CreateMat("RoadAsphalt", shader, new Color(0.18f, 0.19f, 0.2f), 0.35f);
			matRoadZebra = CreateMat("RoadZebraWhite", shader, new Color(0.92f, 0.92f, 0.88f), 0.25f);
			matBark = CreateMat("DarkBark", shader, new Color(0.24f, 0.19f, 0.14f), 0.05f);
			matFoliage = CreateMat("DeepFoliage", shader, new Color(0.12f, 0.22f, 0.14f), 0.08f);
			matTorii = CreateMat("ShrineVermilion", shader, new Color(0.85f, 0.2f, 0.12f), 0.4f);
			matStone = CreateMat("WeatheredStone", shader, new Color(0.46f, 0.47f, 0.45f), 0.15f);
			matDarkMetal = CreateMat("MetalDark", shader, new Color(0.2f, 0.22f, 0.22f), 0.65f);
			matWhite = CreateMat("TrimWhite", shader, new Color(0.92f, 0.9f, 0.86f), 0.35f);
			matHouseStucco = CreateMat("HouseStucco", shader, new Color(0.78f, 0.76f, 0.7f), 0.1f);
			matKawaraRoof = CreateMat("KawaraRoof", shader, new Color(0.22f, 0.25f, 0.28f), 0.35f);
			matBlockWall = CreateMat("BlockWall", shader, new Color(0.55f, 0.54f, 0.5f), 0.15f);
			matGuardrail = CreateMat("GuardrailWhite", shader, new Color(0.88f, 0.88f, 0.88f), 0.5f);
			matLabCounter = CreateMat("LabBlackStone", shader, new Color(0.14f, 0.15f, 0.15f), 0.7f);
			matGold = CreateMat("SignGold", shader, new Color(0.88f, 0.75f, 0.25f), 0.6f);
			matDoorWood = CreateMat("DoorWood", shader, new Color(0.62f, 0.47f, 0.3f), 0.25f);
			matShoeLocker = CreateMat("GetabakoWood", shader, new Color(0.58f, 0.42f, 0.26f), 0.2f);
			matTrashCan = CreateMat("TrashCanBlue", shader, new Color(0.22f, 0.42f, 0.68f), 0.4f);
			matCan = CreateMat("CanAluminum", shader, new Color(0.85f, 0.35f, 0.25f), 0.7f);
            DeadAirArtMaterials.Apply(matPlaster, "plaster");
            DeadAirArtMaterials.Apply(matWoodFloor, "parquet");
            DeadAirArtMaterials.Apply(matDeskWood, "wood");
            DeadAirArtMaterials.Apply(matDoorWood, "wood");
            DeadAirArtMaterials.Apply(matShoeLocker, "wood");
            DeadAirArtMaterials.Apply(matConcrete, "concrete");
            DeadAirArtMaterials.Apply(matStone, "concrete");
            DeadAirArtMaterials.Apply(matAsphalt, "asphalt");
            DeadAirArtMaterials.Apply(matBark, "bark");
            DeadAirArtMaterials.Apply(matFoliage, "foliage");
            DeadAirArtMaterials.Apply(matDarkMetal, "steel");
            DeadAirArtMaterials.Apply(matGuardrail, "steel");
            DeadAirArtMaterials.Apply(matWhite, "painted-wall");
            DeadAirArtMaterials.Apply(matTorii, "painted-wall");
            DeadAirArtMaterials.Apply(matHouseStucco, "plaster");
            DeadAirArtMaterials.Apply(matBlockWall, "brick");
            DeadAirArtMaterials.Apply(matKawaraRoof, "roof-tile");
            DeadAirArtMaterials.Apply(matLabCounter, "ceramic");
            DeadAirArtMaterials.Apply(matPaper, "paper");
            DeadAirArtMaterials.Apply(matBooks, "paper");
            DeadAirArtMaterials.Apply(matTrashCan, "steel");
            DeadAirArtMaterials.Apply(matCan, "steel");
            matSchoolFabric = DeadAirArtMaterials.Create("School cotton and upholstery", "fabric", new Color(0.49f, 0.51f, 0.44f), 0.16f);
            matSchoolRubber = DeadAirArtMaterials.Create("School foot caps and soles", "rubber", new Color(0.12f, 0.13f, 0.12f), 0.12f);
            matSchoolCeramic = DeadAirArtMaterials.Create("School laboratory porcelain", "ceramic", new Color(0.81f, 0.83f, 0.78f), 0.62f);
            matSchoolCeiling = DeadAirArtMaterials.Create("School matte lime plaster ceiling", "plaster", new Color(.88f, .87f, .81f), .045f);
            matSchoolCeiling.SetFloat("_Glossiness", .045f);
            matSchoolCeiling.SetFloat("_MaskMapEnabled", 0f);
            matSchoolCeiling.SetFloat("_Metallic", 0f);
            matSchoolCeiling.SetFloat("_BumpScale", .12f);
            matWoodFloor.SetFloat("_Glossiness", .45f);
            matSchoolBookBindings = new[] {
                DeadAirArtMaterials.Create("School oxblood book cloth", "fabric", new Color(0.27f, 0.11f, 0.09f), 0.12f),
                DeadAirArtMaterials.Create("School navy book cloth", "fabric", new Color(0.09f, 0.14f, 0.19f), 0.12f),
                DeadAirArtMaterials.Create("School green book cloth", "fabric", new Color(0.15f, 0.23f, 0.17f), 0.12f),
                DeadAirArtMaterials.Create("School ochre book cloth", "fabric", new Color(0.42f, 0.32f, 0.15f), 0.12f),
                DeadAirArtMaterials.Create("School charcoal book cloth", "fabric", new Color(0.15f, 0.16f, 0.15f), 0.12f)
            };
            matWoodFloor.mainTextureScale = Vector2.one;
            matBooks.mainTextureScale = Vector2.one;

		}

		private static Material CreateMat(string name, Shader shader, Color color, float gloss)
		{
			Material material = new Material(shader);
			material.name = name;
			material.color = color;
			material.hideFlags = HideFlags.DontSave;
			material.SetFloat("_Glossiness", gloss);
			material.enableInstancing = true;
			return material;
		}



		public void Initialize()
		{
			BuildRoadAndCrosswalk();
			BuildLeftNeighborhood();
			BuildRightNeighborhood();
			BuildBackYardAndForest();
			BuildSchoolCourtyard();
			BuildSchoolBuilding();
			BuildFirstFloorInteriors();
			BuildSecondFloorInteriors();
			BuildThirdFloorInteriors();
			BuildStairs();
			BuildRearSchoolExtension();
			BuildTraditionalPark();
			UpgradePark();
			BuildNewsVan();
			SpawnSanityPills();
			SpawnEvidenceTapes();
			SetupLighting();
            schoolDoorLeaves = Root.GetComponentsInChildren<DeadAirArtMotionSlidingLeaf>(true);
            BatchSchoolArchitecture();
			artReflectionProbes = DeadAirArtReflectionProbes.Attach(Root);
		}

		public void SetActive(bool active)
		{
			if (Root != null)
			{
				Root.gameObject.SetActive(active);
                if (active)
                {
                    Physics.SyncTransforms();
                    BatchSchoolArchitecture();
                }
			}
		}

		private GameObject CreateBox(Transform parent, Vector3 localPos, Vector3 size, Material mat, bool addCollider = true, string name = "Cube")
		{
            if (TryCreateFurnishedAssembly(parent, localPos, size, mat, addCollider, name, out GameObject assembly)) return assembly;
            GameObject gameObject = new GameObject(name);
            gameObject.transform.SetParent(parent, false);
            gameObject.transform.localPosition = localPos;
            bool glass = mat == matGlass;
            float edge = glass || size.y < 0.015f ? 0f : Mathf.Min(0.014f, Mathf.Min(size.x, Mathf.Min(size.y, size.z)) * 0.12f);
            bool hasCeiling = name.StartsWith("Ceiling ", StringComparison.Ordinal) || name.StartsWith("Floor 2 Parquet", StringComparison.Ordinal) ||
                name.Contains("Deck") || name == "Roof Slab" || name == "Canopy Roof" ||
                mat == matWoodFloor && localPos.y > 2f && size.y <= .15f && size.x > 4f && size.z > 2f;
            gameObject.AddComponent<MeshFilter>().sharedMesh = hasCeiling ? DeadAirSchoolMeshes.FloorWithPlasterCeiling(size, edge) : DeadAirSchoolMeshes.Box(size, edge);
            MeshRenderer renderer = gameObject.AddComponent<MeshRenderer>();
            if (hasCeiling) renderer.sharedMaterials = new[] { mat, matSchoolCeiling };
            else renderer.sharedMaterial = mat;
            renderer.shadowCastingMode = glass ? ShadowCastingMode.Off : ShadowCastingMode.On;
            renderer.receiveShadows = !glass;
            renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
            if (addCollider) DeadAirSchoolPhysics.AddBox(gameObject.transform, size);
            return gameObject;
        }

		private GameObject CreateCylinder(Transform parent, Vector3 localPos, float radius, float height, Material mat, bool addCollider = true, string name = "Cylinder")
		{
            if (name == "Trash Bin")
            {
                GameObject scanned = BuildScannedFixture(parent, localPos, new Vector3(radius * 2f, height, radius * 2f), "trash-can", name);
                if (scanned != null)
                {
                    if (addCollider) AddCollisionProxy(scanned.transform, Vector3.zero, new Vector3(radius * 2f, height, radius * 2f));
                    return scanned;
                }
            }
            GameObject gameObject = new GameObject(name);
            gameObject.transform.SetParent(parent, false);
            gameObject.transform.localPosition = localPos;
            bool glass = mat == matGlass;
            bool hollow = name.IndexOf("Beaker", StringComparison.OrdinalIgnoreCase) >= 0 || name == "Trash Bin" || name == "Test tube";
            float innerRadius = hollow ? Mathf.Max(radius * 0.65f, radius - 0.008f) : 0f;
            gameObject.AddComponent<MeshFilter>().sharedMesh = DeadAirSchoolMeshes.Cylinder(radius, height, innerRadius);
            MeshRenderer renderer = gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = mat;
            renderer.shadowCastingMode = glass ? ShadowCastingMode.Off : ShadowCastingMode.On;
            renderer.receiveShadows = !glass;
            if (addCollider)
            {
                if (height < radius * 2f) DeadAirSchoolPhysics.AddBox(gameObject.transform, new Vector3(radius * 2f, height, radius * 2f));
                else
                {
                    DeadAirSchoolPhysics.AddCapsule(gameObject.transform, radius, height);
                }
            }
            return gameObject;
        }



		private GameObject CreateRamp(Transform parent, Vector3 center, Vector3 size, float tiltAngleX, string name = "StairRamp")
		{
			GameObject gameObject = new GameObject(name);
			gameObject.transform.SetParent(parent, worldPositionStays: false);
			gameObject.transform.localPosition = center;
			gameObject.transform.localRotation = Quaternion.Euler(tiltAngleX, 0f, 0f);
			DeadAirSchoolPhysics.AddBox(gameObject.transform, size);
			return gameObject;
		}

		private void SetupLighting()
		{
			GameObject gameObject = new GameObject("Japanese Night Moonlight");
			gameObject.transform.SetParent(Root, worldPositionStays: false);
			Light light = gameObject.AddComponent<Light>();
			light.type = LightType.Directional;
			light.color = new Color(0.48f, 0.58f, 0.72f);
			light.intensity = 0.65f;
			light.shadows = LightShadows.Soft;
			gameObject.transform.rotation = Quaternion.Euler(52f, -40f, 0f);
			RenderSettings.fog = true;
			RenderSettings.fogMode = FogMode.ExponentialSquared;
			RenderSettings.fogColor = new Color(0.05f, 0.07f, 0.11f);
			RenderSettings.fogDensity = 0.01f;
		}

		private void BuildRoadAndCrosswalk()
		{
			Transform transform = new GameObject("Street & Sidewalks").transform;
			transform.SetParent(Root, worldPositionStays: false);
			CreateBox(transform, new Vector3(0f, -0.05f, 43f), new Vector3(120f, 0.1f, 10f), matAsphalt, addCollider: true, "Main Street Asphalt");
			for (float num = -58f; num <= 58f; num += 4f)
			{
				CreateBox(transform, new Vector3(num, 0.01f, 43f), new Vector3(2.4f, 0.01f, 0.18f), matTorii, addCollider: false, "Yellow Center Line");
			}
			CreateBox(transform, new Vector3(0f, 0.01f, 38.6f), new Vector3(120f, 0.01f, 0.18f), matWhite, addCollider: false);
			CreateBox(transform, new Vector3(0f, 0.01f, 47.4f), new Vector3(120f, 0.01f, 0.18f), matWhite, addCollider: false);
			for (float num2 = -3.5f; num2 <= 3.5f; num2 += 1.2f)
			{
				CreateBox(transform, new Vector3(num2, 0.015f, 43f), new Vector3(0.65f, 0.01f, 8.8f), matRoadZebra, addCollider: false, "Zebra Stripe");
			}
			CreateBox(transform, new Vector3(0f, 0.09f, 37f), new Vector3(120f, 0.18f, 2f), matConcrete, addCollider: true, "School Sidewalk");
			CreateBox(transform, new Vector3(0f, 0.09f, 49f), new Vector3(120f, 0.18f, 2f), matConcrete, addCollider: true, "Park Sidewalk");
			for (int i = -1; i <= 1; i += 2)
			{
				for (float num3 = 38.2f; num3 <= 47.8f; num3 += 9.6f)
				{
					float num4 = 4.5f;
					float num5 = 55f;
					float num6 = num5 - num4;
					float x = (float)i * (num4 + num6 * 0.5f);
					CreateBox(transform, new Vector3(x, 0.75f, num3), new Vector3(num6, 0.05f, 0.05f), matGuardrail, addCollider: false, "Guardrail Top");
					CreateBox(transform, new Vector3(x, 0.45f, num3), new Vector3(num6, 0.05f, 0.05f), matGuardrail, addCollider: false, "Guardrail Mid");
					for (float num7 = num4; num7 <= num5; num7 += 3f)
					{
						CreateCylinder(transform, new Vector3((float)i * num7, 0.45f, num3), 0.035f, 0.9f, matGuardrail, addCollider: false, "Rail Post");
					}
				}
			}
			BuildTrafficMirror(transform, new Vector3(-6f, 0f, 48.5f));
			BuildTrafficMirror(transform, new Vector3(6f, 0f, 36.5f));
		}

		private void BuildTrafficMirror(Transform parent, Vector3 pos)
		{
			Transform transform = new GameObject("Curved Traffic Mirror").transform;
			transform.SetParent(parent, worldPositionStays: false);
			transform.localPosition = pos;
			CreateCylinder(transform, new Vector3(0f, 1.8f, 0f), 0.05f, 3.6f, matGuardrail);
			GameObject gameObject = CreateCylinder(transform, new Vector3(0f, 3.3f, 0.2f), 0.45f, 0.08f, matTorii, addCollider: false);
			gameObject.transform.rotation = Quaternion.Euler(75f, 0f, 0f);
			GameObject mirrorDisc = CreateCylinder(gameObject.transform, new Vector3(0f, 0.04f, 0f), 0.4f, 0.02f, matGlass, addCollider: false, "mirror:traffic");
            DeadAirArtMirror.Attach(mirrorDisc.GetComponent<MeshRenderer>(), Vector3.up);
		}

		private void BuildLeftNeighborhood()
		{
			Transform transform = new GameObject("Left Neighborhood").transform;
			transform.SetParent(Root, worldPositionStays: false);
			CreateBox(transform, new Vector3(-38f, 1.1f, 3f), new Vector3(0.35f, 2.2f, 94f), matBlockWall, addCollider: true, "Left Perimeter Wall");
			float[] array = new float[5] { -28f, -10f, 8f, 26f, 44f };
			foreach (float z in array)
			{
				BuildJapaneseHouse(transform, new Vector3(-48f, 0f, z), 0f);
			}
			BuildUtilityPole(transform, new Vector3(-39.5f, 0f, 45f));
			BuildUtilityPole(transform, new Vector3(-39.5f, 0f, 15f));
			BuildUtilityPole(transform, new Vector3(-39.5f, 0f, -15f));
		}

		private void BuildRightNeighborhood()
		{
			Transform transform = new GameObject("Right Neighborhood").transform;
			transform.SetParent(Root, worldPositionStays: false);
			CreateBox(transform, new Vector3(38f, 1.1f, 3f), new Vector3(0.35f, 2.2f, 94f), matBlockWall, addCollider: true, "Right Perimeter Wall");
			float[] array = new float[5] { -28f, -10f, 8f, 26f, 44f };
			foreach (float z in array)
			{
				BuildJapaneseHouse(transform, new Vector3(48f, 0f, z), 180f);
			}
			BuildUtilityPole(transform, new Vector3(39.5f, 0f, 45f));
			BuildUtilityPole(transform, new Vector3(39.5f, 0f, 15f));
			BuildUtilityPole(transform, new Vector3(39.5f, 0f, -15f));
		}

		private void BuildJapaneseHouse(Transform parent, Vector3 pos, float yaw)
		{
            Transform house = Assembly(parent, "Japanese House", pos, yaw);
            CreateBox(house, new Vector3(0f, 0.2f, 0f), new Vector3(12.1f, 0.4f, 11.1f), matConcrete, true, "House foundation");
            CreateBox(house, new Vector3(0f, 3.4f, -5.4f), new Vector3(12f, 6.8f, 0.2f), matHouseStucco, true, "House rear wall");
            for (int side = -1; side <= 1; side += 2)
                CreateBox(house, new Vector3(side * 5.9f, 3.4f, 0f), new Vector3(0.2f, 6.8f, 11f), matHouseStucco, true, "House side wall");
            CreateBox(house, new Vector3(-2.3f, 3.4f, 5.4f), new Vector3(7.4f, 6.8f, 0.2f), matHouseStucco, true, "House front left pier");
            CreateBox(house, new Vector3(4.8f, 3.4f, 5.4f), new Vector3(2.4f, 6.8f, 0.2f), matHouseStucco, true, "House front right pier");
            CreateBox(house, new Vector3(2.5f, 0.55f, 5.4f), new Vector3(2.2f, 1.1f, 0.2f), matHouseStucco, true, "House lower window sill wall");
            CreateBox(house, new Vector3(2.5f, 3.5f, 5.4f), new Vector3(2.2f, 2f, 0.2f), matHouseStucco, true, "House intermediate window wall");
            CreateBox(house, new Vector3(2.5f, 6.35f, 5.4f), new Vector3(2.2f, 0.9f, 0.2f), matHouseStucco, true, "House upper window lintel");
            CreateBox(house, new Vector3(0f, 3.4f, 0f), new Vector3(11.6f, 0.15f, 10.6f), matDeskWood, false, "House internal floor");
            BuildPitchedRoof(house, new Vector3(0f, 8.15f, 0f), new Vector3(13.6f, 0.1f, 12.6f));
            CreateBox(house, new Vector3(0f, 3.8f, 6.1f), new Vector3(8.4f, 0.15f, 1.2f), matConcrete, true, "Balcony floor");
            CreateBox(house, new Vector3(0f, 4.35f, 6.65f), new Vector3(8.4f, 0.065f, 0.065f), matDarkMetal, false, "Balcony top rail");
            for (float x = -4.1f; x <= 4.1f; x += 0.35f)
                CreateBox(house, new Vector3(x, 4.05f, 6.65f), new Vector3(0.028f, 0.58f, 0.028f), matDarkMetal, false, "Balcony baluster");
            for (int floor = 0; floor < 2; floor++)
            {
                Transform window = Assembly(house, "Separate house window", new Vector3(2.5f, 1.8f + floor * 3.4f, 5.5f));
                for (int side = -1; side <= 1; side += 2)
                {
                    CreateBox(window, new Vector3(side * 1.07f, 0f, 0f), new Vector3(0.06f, 1.4f, 0.09f), matWhite, false, "House window jamb");
                    CreateBox(window, new Vector3(0f, side * 0.67f, 0f), new Vector3(2.2f, 0.06f, 0.09f), matWhite, false, "House window rail");
                }
                CreateBox(window, Vector3.zero, new Vector3(0.035f, 1.35f, 0.06f), matWhite, false, "House window mullion");
                CreateBox(window, new Vector3(0f, 0f, -0.016f), new Vector3(2.08f, 1.28f, 0.008f), matGlass, false, "House window glass");
                CreateBox(window, new Vector3(0f, 0f, -1.2f), new Vector3(2.3f, 1.5f, 0.04f), matSchoolFabric, false, "Recessed interior curtain");
                if (floor == 1)
                {
                    Light light = window.gameObject.AddComponent<Light>();
                    light.type = LightType.Point; light.color = new Color(1f, 0.82f, 0.55f); light.intensity = 0.8f; light.range = 5f;
                }
            }
            CreateBox(house, new Vector3(-3.5f, 0.5f, 5.85f), new Vector3(0.9f, 0.65f, 0.4f), matWhite, false, "AC Unit");
            for (int slat = 0; slat < 7; slat++) CreateBox(house, new Vector3(-3.5f, 0.28f + slat * 0.065f, 6.055f), new Vector3(0.76f, 0.022f, 0.008f), matDarkMetal, false, "AC condenser grille slat");
        }

		private void BuildUtilityPole(Transform parent, Vector3 pos)
		{
			Transform transform = new GameObject("Utility Pole (電柱)").transform;
			transform.SetParent(parent, worldPositionStays: false);
			transform.localPosition = pos;
			CreateCylinder(transform, new Vector3(0f, 5f, 0f), 0.18f, 10f, matConcrete);
			CreateBox(transform, new Vector3(0f, 8.8f, 0f), new Vector3(2.4f, 0.1f, 0.1f), matDarkMetal, addCollider: false);
			CreateBox(transform, new Vector3(0f, 9.4f, 0f), new Vector3(1.8f, 0.1f, 0.1f), matDarkMetal, addCollider: false);
			CreateCylinder(transform, new Vector3(0.35f, 8.2f, 0f), 0.28f, 0.9f, matDarkMetal, addCollider: false);
		}

		private void BuildBackYardAndForest()
		{
			Transform transform = new GameObject("Back Schoolyard & Forest").transform;
			transform.SetParent(Root, worldPositionStays: false);
			CreateBox(transform, new Vector3(0f, -0.05f, -56f), new Vector3(72f, 0.1f, 28f), matConcrete, addCollider: true, "Backyard Ground");
			Transform transform2 = new GameObject("Bicycle Shed").transform;
			transform2.SetParent(transform, worldPositionStays: false);
			transform2.localPosition = new Vector3(-29f, 0f, -52f);
			CreateBox(transform2, new Vector3(0f, 2.5f, 0f), new Vector3(10f, 0.12f, 3.8f), matDarkMetal, addCollider: true, "Shed Roof");
			for (float num = -4.5f; num <= 4.5f; num += 4.5f)
			{
				CreateCylinder(transform2, new Vector3(num, 1.25f, -1.6f), 0.05f, 2.5f, matDarkMetal, addCollider: false);
				CreateCylinder(transform2, new Vector3(num, 1.25f, 1.6f), 0.05f, 2.5f, matDarkMetal, addCollider: false);
			}
			for (float num2 = -4.2f; num2 <= 4.2f; num2 += 0.8f)
			{
				CreateBox(transform2, new Vector3(num2, 0.25f, 0f), new Vector3(0.04f, 0.5f, 1.4f), matDarkMetal, addCollider: false);
			}
			CreateBox(transform, new Vector3(30f, 1.6f, -52f), new Vector3(5.2f, 3.2f, 3.6f), matDarkMetal, addCollider: true, "Storage Lockup");
			GameObject gameObject = CreateBox(transform, new Vector3(31f, 1.2f, -64f), new Vector3(1.8f, 2.4f, 1.8f), matConcrete, addCollider: true, "Incinerator");
			CreateCylinder(gameObject.transform, new Vector3(0f, 2f, 0f), 0.22f, 2.2f, matDarkMetal, addCollider: false, "Chimney");
			BuildRearPlayground(transform);
			BuildRearFence(transform);
			CreateBox(transform, new Vector3(0f, -0.05f, -93f), new Vector3(96f, 0.1f, 46f), matFoliage, addCollider: true, "Forest Floor");
			System.Random random = new System.Random(88812);
			for (int i = 0; i < 32; i++)
			{
				float x = (float)(random.NextDouble() * 84.0 - 42.0);
				float z = (float)(random.NextDouble() * 42.0 - 114.0);
				BuildForestTree(transform, new Vector3(x, 0f, z));
			}
		}

        private void BuildForestTree(Transform parent, Vector3 pos)
        {
            BuildRefinedForestTree(parent, pos);
        }

		private void BuildSchoolCourtyard()
		{
			Transform transform = new GameObject("School Courtyard").transform;
			transform.SetParent(Root, worldPositionStays: false);
			CreateBox(transform, new Vector3(0f, -0.05f, 18f), new Vector3(72f, 0.1f, 36f), matAsphalt, addCollider: true, "Courtyard Asphalt");
			CreateBox(transform, new Vector3(0f, 0.02f, 18f), new Vector3(6.5f, 0.02f, 36f), matConcrete, addCollider: false, "Approach Path");
			Material mat = CreateMat("CircleYellow", matPlaster.shader, new Color(0.92f, 0.78f, 0.2f), 0.2f);
			Material mat2 = CreateMat("CircleBlue", matPlaster.shader, new Color(0.24f, 0.6f, 0.88f), 0.2f);
			Material mat3 = CreateMat("CircleRed", matPlaster.shader, new Color(0.88f, 0.26f, 0.22f), 0.2f);
			Material mat4 = CreateMat("CircleGreen", matPlaster.shader, new Color(0.26f, 0.82f, 0.36f), 0.2f);
			CreateRing(transform, new Vector3(8f, 0.015f, 14f), 2.8f, mat);
			CreateRing(transform, new Vector3(-10f, 0.015f, 18f), 2f, mat2);
			CreateRing(transform, new Vector3(14f, 0.015f, 22f), 1.6f, mat3);
			CreateRing(transform, new Vector3(-16f, 0.015f, 12f), 3.4f, mat);
			CreateRing(transform, new Vector3(20f, 0.015f, 15f), 1.4f, mat4);
			CreateRing(transform, new Vector3(-8f, 0.015f, 26f), 2.3f, mat2);
			Material coneMat = CreateMat("ConeOrange", matPlaster.shader, new Color(0.95f, 0.35f, 0.1f), 0.3f);
			CreateTrafficCone(transform, new Vector3(14f, 0f, 28f), coneMat);
			CreateTrafficCone(transform, new Vector3(16f, 0f, 26f), coneMat);
			CreateTrafficCone(transform, new Vector3(-12f, 0f, 32f), coneMat);
			for (int i = -1; i <= 1; i += 2)
			{
				float num = 30f;
				float x = (float)i * (5.5f + num * 0.5f);
				CreateBox(transform, new Vector3(x, 1.1f, 36f), new Vector3(num, 2.2f, 0.35f), matConcrete, addCollider: true, "Front Wall");
				for (float num2 = 6f; num2 < 35f; num2 += 1f)
				{
					CreateCylinder(transform, new Vector3((float)i * num2, 2.6f, 36f), 0.025f, 0.8f, matDarkMetal, addCollider: false);
				}
			}
			for (int j = -1; j <= 1; j += 2)
			{
				CreateBox(transform, new Vector3((float)j * 5.5f, 1.8f, 36f), new Vector3(1f, 3.6f, 1f), matConcrete, addCollider: true, "Gate Pillar");
				CreateBox(transform, new Vector3((float)j * 5.5f, 3.65f, 36f), new Vector3(1.2f, 0.2f, 1.2f), matDarkMetal, addCollider: false);
			}
			BuildSchoolGenerator(transform, new Vector3(-8.0f, 0f, 2.5f));
		}

		private void CreateRing(Transform parent, Vector3 center, float radius, Material mat)
		{
			CreateCylinder(parent, center, radius, 0.02f, mat, addCollider: false, "PaintRing");
		}

		private void CreateTrafficCone(Transform parent, Vector3 pos, Material coneMat)
		{
			Transform transform = new GameObject("TrafficCone").transform;
			transform.SetParent(parent, worldPositionStays: false);
			transform.localPosition = pos;
			CreateBox(transform, new Vector3(0f, 0.02f, 0f), new Vector3(0.4f, 0.04f, 0.4f), matDarkMetal);
			GameObject gameObject = CreateCylinder(transform, new Vector3(0f, 0.38f, 0f), 0.13f, 0.76f, coneMat);
			CreateCylinder(gameObject.transform, Vector3.zero, 0.135f, 0.22f, matWhite, addCollider: false);
		}

		private void BuildSchoolBuilding()
		{
			Transform transform = new GameObject("School Outer Structure").transform;
			transform.SetParent(Root, worldPositionStays: false);
			float num = 72f;
			float num2 = 11.2f;
			float num3 = 42f;
			CreateBox(transform, new Vector3(0f, 11.15f, (0f - num3) * 0.5f), new Vector3(num, 0.25f, num3), matConcrete, addCollider: true, "Roof Slab");
			CreateBox(transform, new Vector3(0f, 11.55f, 0.1f), new Vector3(num + 0.6f, 0.6f, 0.35f), matConcrete, addCollider: true, "Roof Parapet South");
			CreateBox(transform, new Vector3(0f, 11.55f, 0f - num3 - 0.1f), new Vector3(num + 0.6f, 0.6f, 0.35f), matConcrete, addCollider: true, "Roof Parapet North");
			CreateBox(transform, new Vector3((0f - num) * 0.5f - 0.1f, 11.55f, (0f - num3) * 0.5f), new Vector3(0.35f, 0.6f, num3 + 0.6f), matConcrete, addCollider: true, "Roof Parapet West");
			CreateBox(transform, new Vector3(num * 0.5f + 0.1f, 11.55f, (0f - num3) * 0.5f), new Vector3(0.35f, 0.6f, num3 + 0.6f), matConcrete, addCollider: true, "Roof Parapet East");
			CreateBox(transform, new Vector3(-36f, num2 * 0.5f, (0f - num3) * 0.5f), new Vector3(0.4f, num2, num3), matPlaster, addCollider: true, "Exterior Wall West");
			CreateBox(transform, new Vector3(36f, num2 * 0.5f, (0f - num3) * 0.5f), new Vector3(0.4f, num2, num3), matPlaster, addCollider: true, "Exterior Wall East");
			BuildFacadeWallWithWindows(transform, 0f, isNorth: false);
			BuildFacadeWallWithWindows(transform, 0f - num3, isNorth: true);
			CreateBox(transform, new Vector3(0f, 5.7f, 0f), new Vector3(10f, 3.8f, 0.35f), matPlaster, addCollider: true, "South Facade Center 2F");
			CreateBox(transform, new Vector3(0f, 9.4f, 0f), new Vector3(10f, 3.6f, 0.35f), matPlaster, addCollider: true, "South Facade Center 3F");
            BuildSchoolWallClock(transform, new Vector3(0f, 6.2f, .24f), 1.7f);
			for (int i = -1; i <= 1; i += 2)
			{
				for (int j = 0; j < 5; j++)
				{
					float num4 = (float)i * (8.5f + (float)j * 5.6f);
					CreateBox(transform, new Vector3(num4, 0.35f, 1.4f), new Vector3(4.2f, 0.7f, 1f), matConcrete, addCollider: true, "Planter");
					CreateBox(transform, new Vector3(num4, 0.9f, 1.4f), new Vector3(3.8f, 0.8f, 0.8f), matFoliage, addCollider: false, "Shrub");
					if (j == 1 || j == 3)
					{
                        if (!TryInstallSchoolTree(transform, "pine-tree", new Vector3(num4 + 1.2f, .6f, 1.4f), new Vector3(2.4f, 3.3f, 2.4f), .11f, 2.8f, j * 43f))
                        {
                            CreateCylinder(transform, new Vector3(num4 + 1.2f, 2f, 1.4f), 0.22f, 2.8f, matBark);
                            CreateBox(transform, new Vector3(num4 + 1.2f, 3.5f, 1.4f), new Vector3(2.4f, 0.8f, 2.4f), matFoliage, addCollider: false);
                        }
					}
				}
			}
			BuildSchoolEntrance(transform);
		}

		private void BuildFacadeWallWithWindows(Transform parent, float facadeZ, bool isNorth)
		{
			Transform transform = new GameObject((isNorth ? "North" : "South") + " Facade Wall").transform;
			transform.SetParent(parent, worldPositionStays: false);
			CreateBox(transform, new Vector3(-20.5f, 0.55f, facadeZ), new Vector3(31f, 1.1f, 0.35f), matPlaster);
			CreateBox(transform, new Vector3(20.5f, 0.55f, facadeZ), new Vector3(31f, 1.1f, 0.35f), matPlaster);
			CreateBox(transform, new Vector3(-20.5f, 3.7f, facadeZ), new Vector3(31f, 1.2f, 0.35f), matPlaster);
			CreateBox(transform, new Vector3(20.5f, 3.7f, facadeZ), new Vector3(31f, 1.2f, 0.35f), matPlaster);
			CreateBox(transform, new Vector3(-20.5f, 6.95f, facadeZ), new Vector3(31f, 1.3f, 0.35f), matPlaster);
			CreateBox(transform, new Vector3(20.5f, 6.95f, facadeZ), new Vector3(31f, 1.3f, 0.35f), matPlaster);
			CreateBox(transform, new Vector3(-20.5f, 10.55f, facadeZ), new Vector3(31f, 1.3f, 0.35f), matPlaster);
			CreateBox(transform, new Vector3(20.5f, 10.55f, facadeZ), new Vector3(31f, 1.3f, 0.35f), matPlaster);
			if (isNorth)
			{
				CreateBox(transform, new Vector3(-3.1f, 0.55f, facadeZ), new Vector3(3.8f, 1.1f, 0.35f), matPlaster);
				CreateBox(transform, new Vector3(3.1f, 0.55f, facadeZ), new Vector3(3.8f, 1.1f, 0.35f), matPlaster);
				CreateBox(transform, new Vector3(0f, 3.65f, facadeZ), new Vector3(10f, 0.9f, 0.35f), matPlaster);
				CreateBox(transform, new Vector3(0f, 7.25f, facadeZ), new Vector3(10f, 0.9f, 0.35f), matPlaster);
				CreateBox(transform, new Vector3(0f, 10.55f, facadeZ), new Vector3(10f, 1.3f, 0.35f), matPlaster);
				for (int i = 0; i < 3; i++)
				{
					float num = i switch
					{
						1 => 5.3f, 
						0 => 2.1f, 
						_ => 8.9f, 
					};
					if (i == 0)
					{
						CreateBox(transform, new Vector3(-3.1f, num, facadeZ), new Vector3(3.8f, 2f, 0.08f), matGlass, addCollider: true, "Rear Exit Sidelight L");
						CreateBox(transform, new Vector3(3.1f, num, facadeZ), new Vector3(3.8f, 2f, 0.08f), matGlass, addCollider: true, "Rear Exit Sidelight R");
						CreateBox(transform, new Vector3(-1.15f, num, facadeZ), new Vector3(0.1f, 2.1f, 0.18f), matDarkMetal);
						CreateBox(transform, new Vector3(1.15f, num, facadeZ), new Vector3(0.1f, 2.1f, 0.18f), matDarkMetal);
						CreateBox(transform, new Vector3(0f, 3.15f, facadeZ), new Vector3(2.3f, 0.1f, 0.18f), matDarkMetal, addCollider: false);
					}
					else
					{
						CreateBox(transform, new Vector3(0f, num, facadeZ), new Vector3(9.3f, 1.9f, 0.08f), matGlass, addCollider: true, "Rear Corridor Window");
						CreateBox(transform, new Vector3(0f, num - 1f, facadeZ), new Vector3(9.5f, 0.1f, 0.16f), matWhite, addCollider: false);
						CreateBox(transform, new Vector3(0f, num + 1f, facadeZ), new Vector3(9.5f, 0.1f, 0.16f), matWhite, addCollider: false);
					}
				}
			}
			for (int j = 0; j < 3; j++)
			{
				float num2 = j switch
				{
					1 => 5.3f, 
					0 => 2.1f, 
					_ => 8.9f, 
				};
				for (int k = -1; k <= 1; k += 2)
				{
					CreateBox(transform, new Vector3((float)k * 6.8f, num2, facadeZ), new Vector3(3.6f, 2f, 0.35f), matPlaster);
					for (int l = 0; l < 5; l++)
					{
						float num3 = (float)k * (10.5f + (float)l * 5.4f);
						CreateBox(transform, new Vector3(num3, num2, facadeZ), new Vector3(3.8f, 1.9f, 0.02f), matGlass, addCollider: true, "Transparent Classroom Window");
						CreateBox(transform, new Vector3(num3 - 1.95f, num2, facadeZ), new Vector3(0.12f, 2f, 0.14f), matWhite, addCollider: false);
						CreateBox(transform, new Vector3(num3 + 1.95f, num2, facadeZ), new Vector3(0.12f, 2f, 0.14f), matWhite, addCollider: false);
						CreateBox(transform, new Vector3(num3, num2 - 0.95f, facadeZ), new Vector3(4f, 0.12f, 0.14f), matWhite, addCollider: false);
						CreateBox(transform, new Vector3(num3, num2 + 0.95f, facadeZ), new Vector3(4f, 0.12f, 0.14f), matWhite, addCollider: false);
						CreateBox(transform, new Vector3(num3, num2, facadeZ), new Vector3(0.06f, 1.9f, 0.06f), matDarkMetal, addCollider: false);
						CreateBox(transform, new Vector3(num3, num2, facadeZ), new Vector3(3.8f, 0.06f, 0.06f), matDarkMetal, addCollider: false);
						if (l < 4)
						{
							CreateBox(transform, new Vector3((float)k * (13.2f + (float)l * 5.4f), num2, facadeZ), new Vector3(1.6f, 2f, 0.35f), matPlaster);
						}
					}
					CreateBox(transform, new Vector3((float)k * 35.05f, num2, facadeZ), new Vector3(1.9f, 2f, 0.35f), matPlaster);
				}
			}
		}

		private void BuildSchoolEntrance(Transform parent)
		{
			Transform transform = new GameObject("School Entrance (玄関)").transform;
			transform.SetParent(parent, worldPositionStays: false);
			CreateBox(transform, new Vector3(0f, 0.225f, 2.1f), new Vector3(10.5f, 0.45f, 4.2f), matConcrete, addCollider: true, "Porch Landing");
			CreateBox(transform, new Vector3(0f, 0.12f, 4.8f), new Vector3(10.5f, 0.15f, 0.6f), matConcrete, addCollider: true, "Porch Step 1");
			CreateBox(transform, new Vector3(0f, 0.27f, 4.5f), new Vector3(10.5f, 0.15f, 0.6f), matConcrete, addCollider: true, "Porch Step 2");
			CreateBox(transform, new Vector3(0f, 3.95f, 2.4f), new Vector3(11.2f, 0.35f, 4.8f), matConcrete, addCollider: true, "Canopy Roof");
			CreateBox(transform, new Vector3(-4.8f, 1.9f, 4.2f), new Vector3(0.7f, 3.8f, 0.7f), matConcrete, addCollider: true, "Canopy Pillar L");
			CreateBox(transform, new Vector3(4.8f, 1.9f, 4.2f), new Vector3(0.7f, 3.8f, 0.7f), matConcrete, addCollider: true, "Canopy Pillar R");
			GameObject gameObject = CreateBox(transform, new Vector3(0f, 3.25f, 0.22f), new Vector3(4.8f, 0.65f, 0.08f), matDarkMetal, addCollider: false, "School Plaque");
			CreateBox(gameObject.transform, new Vector3(0f, 0f, 0.05f), new Vector3(4.5f, 0.48f, 0.04f), matGold, addCollider: false, "Gold Lettering Plate");
			CreateBox(transform, new Vector3(-4.8f, 1.7f, 0.08f), new Vector3(0.4f, 3.4f, 0.18f), matDarkMetal);
			CreateBox(transform, new Vector3(4.8f, 1.7f, 0.08f), new Vector3(0.4f, 3.4f, 0.18f), matDarkMetal);
			CreateBox(transform, new Vector3(0f, 3.25f, 0.08f), new Vector3(9.6f, 0.35f, 0.18f), matDarkMetal, addCollider: false);
			CreateBox(transform, new Vector3(-3.3f, 1.6f, 0.08f), new Vector3(2.6f, 2.9f, 0.04f), matGlass, addCollider: true, "Entrance Glass Fixed L");
			CreateBox(transform, new Vector3(3.3f, 1.6f, 0.08f), new Vector3(2.6f, 2.9f, 0.04f), matGlass, addCollider: true, "Entrance Glass Fixed R");
			CreateBox(transform, new Vector3(-1.1f, 1.6f, 0.14f), new Vector3(1.8f, 2.9f, 0.04f), matGlass, addCollider: true, "Entrance Sliding Door Closed");
			CreateBox(transform, new Vector3(2.2f, 1.6f, 0.14f), new Vector3(1.4f, 2.9f, 0.04f), matGlass, addCollider: false, "Entrance Door Slid Open");
			CreateBox(transform, new Vector3(0f, 0.225f, -1.5f), new Vector3(9.6f, 0.45f, 3f), matConcrete, addCollider: true, "Genkan Doma Floor");
			CreateBox(transform, new Vector3(0f, 0.525f, -3.15f), new Vector3(9.6f, 0.15f, 0.35f), matWoodFloor, addCollider: true, "Shikidai Threshold Step");
			CreateBox(transform, new Vector3(0f, 0.3f, -5.9f), new Vector3(9.6f, 0.6f, 5.2f), matWoodFloor, addCollider: true, "Genkan Hall Floor");
			CreateBox(transform, new Vector3(-4.9f, 2.2f, -4.25f), new Vector3(0.25f, 3.2f, 8.5f), matPlaster, addCollider: true, "Genkan Wall L");
			CreateBox(transform, new Vector3(4.9f, 2.2f, -4.25f), new Vector3(0.25f, 3.2f, 8.5f), matPlaster, addCollider: true, "Genkan Wall R");
			BuildShoeLockers(transform, new Vector3(-4.1f, 0.6f, -5.5f), 90f);
			BuildShoeLockers(transform, new Vector3(4.1f, 0.6f, -5.5f), -90f);
			CreateInteriorLamp(transform, new Vector3(0f, 3.65f, -4.5f));
			BuildSchoolBreaker(transform, new Vector3(4.75f, 1.6f, -1.8f));
		}

		private void BuildShoeLockers(Transform parent, Vector3 pos, float yaw)
		{
            Transform root = Assembly(parent, "Getabako Shoe Lockers", pos, yaw);
            const float width = 5.2f, height = 1.9f, depth = 0.65f, board = 0.025f;
            CreateBox(root, new Vector3(0f, height * 0.5f, -depth * 0.5f + board * 0.5f), new Vector3(width, height, board), matShoeLocker, true, "Locker recessed back");
            for (int col = 0; col <= 10; col++)
                CreateBox(root, new Vector3(-width * 0.5f + board * 0.5f + col * (width - board) / 10f, height * 0.5f, 0f), new Vector3(board, height, depth), matShoeLocker, true, "Cubby vertical partition");
            for (int row = 0; row <= 4; row++)
                CreateBox(root, new Vector3(0f, board * 0.5f + row * (height - board) / 4f, 0f), new Vector3(width, board, depth), matShoeLocker, true, "Cubby shelf board");
            for (int row = 0; row < 4; row++)
            for (int col = 0; col < 10; col++)
            {
                float x = -width * 0.5f + (col + 0.5f) * width / 10f;
                float y = (row + 0.5f) * height / 4f;
                CreateBox(root, new Vector3(x, y - 0.18f, depth * 0.5f + 0.003f), new Vector3(0.15f, 0.035f, 0.005f), matPaper, false, "Cubby number card");
                if ((row + col) % 3 != 0) continue;
                for (int shoe = -1; shoe <= 1; shoe += 2)
                {
                    CreateBox(root, new Vector3(x + shoe * 0.085f, y - 0.16f, 0.045f), new Vector3(0.12f, 0.045f, 0.23f), matSchoolRubber, false, "Shoe sole");
                    CreateBox(root, new Vector3(x + shoe * 0.085f, y - 0.115f, 0.015f), new Vector3(0.105f, 0.065f, 0.16f), matWhite, false, "Shoe upper");
                }
            }
        }

		private void BuildFirstFloorInteriors()
		{
			Transform transform = new GameObject("First Floor Interiors").transform;
			transform.SetParent(Root, worldPositionStays: false);
			float num = 0.6f;
			float num2 = 3.8f;
			CreateBox(transform, new Vector3(-20.45f, num * 0.5f, -4.25f), new Vector3(31.1f, num, 8.5f), matWoodFloor, addCollider: true, "Floor 1 West Front Slab");
			CreateBox(transform, new Vector3(20.45f, num * 0.5f, -4.25f), new Vector3(31.1f, num, 8.5f), matWoodFloor, addCollider: true, "Floor 1 East Front Slab");
			CreateBox(transform, new Vector3(0f, num * 0.5f, -15.25f), new Vector3(72f, num, 13.5f), matWoodFloor, addCollider: true, "Floor 1 Rear Slab");
			CreateBox(transform, new Vector3(-21f, num2 + 0.05f, -11f), new Vector3(30f, 0.15f, 22f), matConcrete, addCollider: true, "Ceiling Left Wing");
			CreateBox(transform, new Vector3(21f, num2 + 0.05f, -11f), new Vector3(30f, 0.15f, 22f), matConcrete, addCollider: true, "Ceiling Right Wing");
			CreateBox(transform, new Vector3(0f, num2 + 0.05f, -6.75f), new Vector3(12f, 0.15f, 13.5f), matConcrete, addCollider: true, "Ceiling Center South");
			for (float num3 = -30f; num3 <= 30f; num3 += 10f)
			{
				CreateInteriorLamp(transform, new Vector3(num3, num2 - 0.15f, -11f));
			}
			BuildExitSign(transform, new Vector3(-20.5f, 2.9f, -8.65f));
			BuildExitSign(transform, new Vector3(20.5f, 2.9f, -8.65f));
			BuildExitSign(transform, new Vector3(-6f, 2.9f, -13.35f));
			BuildFireHydrant(transform, new Vector3(-18f, 1.4f, -8.65f));
			BuildFireHydrant(transform, new Vector3(18f, 1.4f, -8.65f));
			BuildNoticeBoard(transform, new Vector3(-8.5f, 2f, -8.65f));
			BuildNoticeBoard(transform, new Vector3(8.5f, 2f, -8.65f));
			CreateBox(transform, new Vector3(-32.6f, 2.2f, -8.5f), new Vector3(6.8f, 3.2f, 0.25f), matPlaster);
			CreateBox(transform, new Vector3(-20.75f, 2.2f, -8.5f), new Vector3(14f, 3.2f, 0.25f), matPlaster);
			CreateBox(transform, new Vector3(-8.5f, 2.2f, -8.5f), new Vector3(7.5f, 3.2f, 0.25f), matPlaster);
			CreateBox(transform, new Vector3(8.5f, 2.2f, -8.5f), new Vector3(7.5f, 3.2f, 0.25f), matPlaster);
			CreateBox(transform, new Vector3(20.75f, 2.2f, -8.5f), new Vector3(14f, 3.2f, 0.25f), matPlaster);
			CreateBox(transform, new Vector3(32.6f, 2.2f, -8.5f), new Vector3(6.8f, 3.2f, 0.25f), matPlaster);
			CreateBox(transform, new Vector3(-28.5f, 3.3f, -8.5f), new Vector3(1.5f, 1f, 0.25f), matPlaster);
			CreateBox(transform, new Vector3(-13f, 3.3f, -8.5f), new Vector3(1.5f, 1f, 0.25f), matPlaster);
			CreateBox(transform, new Vector3(13f, 3.3f, -8.5f), new Vector3(1.5f, 1f, 0.25f), matPlaster);
			CreateBox(transform, new Vector3(28.5f, 3.3f, -8.5f), new Vector3(1.5f, 1f, 0.25f), matPlaster);
			BuildClassroomDoor(transform, new Vector3(-28.5f, num, -8.5f), 0f, "1-A");
			BuildClassroomDoor(transform, new Vector3(-13f, num, -8.5f), 0f, "1-B");
			BuildClassroomDoor(transform, new Vector3(13f, num, -8.5f), 0f, "1-C");
			BuildClassroomDoor(transform, new Vector3(28.5f, num, -8.5f), 0f, "保健室");
			CreateBox(transform, new Vector3(-32.6f, 2.2f, -13.5f), new Vector3(6.8f, 3.2f, 0.25f), matPlaster);
			CreateBox(transform, new Vector3(-20.75f, 2.2f, -13.5f), new Vector3(14f, 3.2f, 0.25f), matPlaster);
			CreateBox(transform, new Vector3(-9.25f, 2.2f, -13.5f), new Vector3(6.5f, 3.2f, 0.25f), matPlaster);
			CreateBox(transform, new Vector3(9.25f, 2.2f, -13.5f), new Vector3(6.5f, 3.2f, 0.25f), matPlaster);
			CreateBox(transform, new Vector3(20.75f, 2.2f, -13.5f), new Vector3(14f, 3.2f, 0.25f), matPlaster);
			CreateBox(transform, new Vector3(32.6f, 2.2f, -13.5f), new Vector3(6.8f, 3.2f, 0.25f), matPlaster);
			CreateBox(transform, new Vector3(-28.5f, 3.3f, -13.5f), new Vector3(1.5f, 1f, 0.25f), matPlaster);
			CreateBox(transform, new Vector3(-13f, 3.3f, -13.5f), new Vector3(1.5f, 1f, 0.25f), matPlaster);
			CreateBox(transform, new Vector3(13f, 3.3f, -13.5f), new Vector3(1.5f, 1f, 0.25f), matPlaster);
			CreateBox(transform, new Vector3(28.5f, 3.3f, -13.5f), new Vector3(1.5f, 1f, 0.25f), matPlaster);
			BuildClassroomDoor(transform, new Vector3(-28.5f, num, -13.5f), 180f, "職員室");
			BuildClassroomDoor(transform, new Vector3(-13f, num, -13.5f), 180f, "校長室");
			BuildClassroomDoor(transform, new Vector3(13f, num, -13.5f), 180f, "PC室");
			BuildClassroomDoor(transform, new Vector3(28.5f, num, -13.5f), 180f, "資料室");
			CreateBox(transform, new Vector3(-20.5f, 2.2f, -4.25f), new Vector3(0.25f, 3.2f, 8.5f), matPlaster);
			CreateBox(transform, new Vector3(-5f, 2.2f, -4.25f), new Vector3(0.25f, 3.2f, 8.5f), matPlaster);
			CreateBox(transform, new Vector3(5f, 2.2f, -4.25f), new Vector3(0.25f, 3.2f, 8.5f), matPlaster);
			CreateBox(transform, new Vector3(20.5f, 2.2f, -4.25f), new Vector3(0.25f, 3.2f, 8.5f), matPlaster);
			CreateBox(transform, new Vector3(-20.5f, 2.2f, -17.75f), new Vector3(0.25f, 3.2f, 8.5f), matPlaster);
			CreateBox(transform, new Vector3(-6f, 2.2f, -17.75f), new Vector3(0.25f, 3.2f, 8.5f), matPlaster);
			CreateBox(transform, new Vector3(6f, 2.2f, -17.75f), new Vector3(0.25f, 3.2f, 8.5f), matPlaster);
			CreateBox(transform, new Vector3(20.5f, 2.2f, -17.75f), new Vector3(0.25f, 3.2f, 8.5f), matPlaster);
			BuildClassroom(transform, -28.25f, num, -4.25f, "1-A", 1);
			BuildClassroom(transform, -12.75f, num, -4.25f, "1-B", 2);
			BuildClassroom(transform, 12.75f, num, -4.25f, "1-C", 3);
			BuildNurseOffice(transform, 28.25f, num);
			BuildStaffRoom(transform, -28.25f, num);
			BuildPrincipalOffice(transform, -12.75f, num);
			BuildComputerLab(transform, 12.75f, num);
			BuildArchiveRoom(transform, 28.25f, num);
		}

		private void BuildSecondFloorInteriors()
		{
			Transform transform = new GameObject("Second Floor Interiors").transform;
			transform.SetParent(Root, worldPositionStays: false);
			float num = 3.8f;
			float num2 = 7.4f;
			CreateBox(transform, new Vector3(-21f, num + 0.05f, -11f), new Vector3(30f, 0.1f, 22f), matWoodFloor, addCollider: true, "Floor 2 Parquet Left");
			CreateBox(transform, new Vector3(21f, num + 0.05f, -11f), new Vector3(30f, 0.1f, 22f), matWoodFloor, addCollider: true, "Floor 2 Parquet Right");
			CreateBox(transform, new Vector3(0f, num + 0.05f, -4.25f), new Vector3(12f, 0.1f, 8.5f), matWoodFloor, addCollider: true, "Floor 2 Parquet South Center");
			CreateBox(transform, new Vector3(0f, num + 0.05f, -11f), new Vector3(12f, 0.1f, 5f), matWoodFloor, addCollider: true, "Floor 2 Parquet Corridor Center");
			for (float num3 = -30f; num3 <= 30f; num3 += 10f)
			{
				CreateInteriorLamp(transform, new Vector3(num3, num2 - 0.15f, -11f));
			}
			BuildExitSign(transform, new Vector3(-20.5f, num + 2.9f, -8.65f));
			BuildExitSign(transform, new Vector3(20.5f, num + 2.9f, -8.65f));
			CreateBox(transform, new Vector3(-32.6f, num + 1.8f, -8.5f), new Vector3(6.8f, 3.6f, 0.25f), matPlaster);
			CreateBox(transform, new Vector3(-20.75f, num + 1.8f, -8.5f), new Vector3(14f, 3.6f, 0.25f), matPlaster);
			CreateBox(transform, new Vector3(-8.5f, num + 1.8f, -8.5f), new Vector3(7.5f, 3.6f, 0.25f), matPlaster);
			CreateBox(transform, new Vector3(0f, num + 1.8f, -8.5f), new Vector3(10f, 3.6f, 0.25f), matPlaster);
			CreateBox(transform, new Vector3(8.5f, num + 1.8f, -8.5f), new Vector3(7.5f, 3.6f, 0.25f), matPlaster);
			CreateBox(transform, new Vector3(20.75f, num + 1.8f, -8.5f), new Vector3(14f, 3.6f, 0.25f), matPlaster);
			CreateBox(transform, new Vector3(32.6f, num + 1.8f, -8.5f), new Vector3(6.8f, 3.6f, 0.25f), matPlaster);
			CreateBox(transform, new Vector3(-28.5f, num + 3.3f, -8.5f), new Vector3(1.5f, 1f, 0.25f), matPlaster);
			CreateBox(transform, new Vector3(-13f, num + 3.3f, -8.5f), new Vector3(1.5f, 1f, 0.25f), matPlaster);
			CreateBox(transform, new Vector3(13f, num + 3.3f, -8.5f), new Vector3(1.5f, 1f, 0.25f), matPlaster);
			CreateBox(transform, new Vector3(28.5f, num + 3.3f, -8.5f), new Vector3(1.5f, 1f, 0.25f), matPlaster);
			BuildClassroomDoor(transform, new Vector3(-28.5f, num, -8.5f), 0f, "2-A");
			BuildClassroomDoor(transform, new Vector3(-13f, num, -8.5f), 0f, "2-B");
			BuildClassroomDoor(transform, new Vector3(13f, num, -8.5f), 0f, "2-C");
			BuildClassroomDoor(transform, new Vector3(28.5f, num, -8.5f), 0f, "音楽室");
			CreateBox(transform, new Vector3(-32.6f, num + 1.8f, -13.5f), new Vector3(6.8f, 3.6f, 0.25f), matPlaster);
			CreateBox(transform, new Vector3(-20.75f, num + 1.8f, -13.5f), new Vector3(14f, 3.6f, 0.25f), matPlaster);
			CreateBox(transform, new Vector3(-9.25f, num + 1.8f, -13.5f), new Vector3(6.5f, 3.6f, 0.25f), matPlaster);
			CreateBox(transform, new Vector3(9.25f, num + 1.8f, -13.5f), new Vector3(6.5f, 3.6f, 0.25f), matPlaster);
			CreateBox(transform, new Vector3(20.75f, num + 1.8f, -13.5f), new Vector3(14f, 3.6f, 0.25f), matPlaster);
			CreateBox(transform, new Vector3(32.6f, num + 1.8f, -13.5f), new Vector3(6.8f, 3.6f, 0.25f), matPlaster);
			CreateBox(transform, new Vector3(-28.5f, num + 3.3f, -13.5f), new Vector3(1.5f, 1f, 0.25f), matPlaster);
			CreateBox(transform, new Vector3(-13f, num + 3.3f, -13.5f), new Vector3(1.5f, 1f, 0.25f), matPlaster);
			CreateBox(transform, new Vector3(13f, num + 3.3f, -13.5f), new Vector3(1.5f, 1f, 0.25f), matPlaster);
			CreateBox(transform, new Vector3(28.5f, num + 3.3f, -13.5f), new Vector3(1.5f, 1f, 0.25f), matPlaster);
			BuildClassroomDoor(transform, new Vector3(-28.5f, num, -13.5f), 180f, "理科室");
			BuildClassroomDoor(transform, new Vector3(-13f, num, -13.5f), 180f, "準備室");
			BuildClassroomDoor(transform, new Vector3(13f, num, -13.5f), 180f, "図書室");
			BuildClassroomDoor(transform, new Vector3(28.5f, num, -13.5f), 180f, "放送室");
			CreateBox(transform, new Vector3(-20.5f, num + 1.8f, -4.25f), new Vector3(0.25f, 3.6f, 8.5f), matPlaster);
			CreateBox(transform, new Vector3(-5f, num + 1.8f, -4.25f), new Vector3(0.25f, 3.6f, 8.5f), matPlaster);
			CreateBox(transform, new Vector3(5f, num + 1.8f, -4.25f), new Vector3(0.25f, 3.6f, 8.5f), matPlaster);
			CreateBox(transform, new Vector3(20.5f, num + 1.8f, -4.25f), new Vector3(0.25f, 3.6f, 8.5f), matPlaster);
			CreateBox(transform, new Vector3(-20.5f, num + 1.8f, -17.75f), new Vector3(0.25f, 3.6f, 8.5f), matPlaster);
			CreateBox(transform, new Vector3(-6f, num + 1.8f, -17.75f), new Vector3(0.25f, 3.6f, 8.5f), matPlaster);
			CreateBox(transform, new Vector3(6f, num + 1.8f, -17.75f), new Vector3(0.25f, 3.6f, 8.5f), matPlaster);
			CreateBox(transform, new Vector3(20.5f, num + 1.8f, -17.75f), new Vector3(0.25f, 3.6f, 8.5f), matPlaster);
			CreateBox(transform, new Vector3(0f, num + 0.05f, -13.65f), new Vector3(12f, 0.1f, 0.35f), matWoodFloor, addCollider: true, "Floor 2 Stair Threshold");
			BuildClassroom(transform, -28.25f, num, -4.25f, "2-A", 4);
			BuildClassroom(transform, -12.75f, num, -4.25f, "2-B", 5);
			BuildClassroom(transform, 12.75f, num, -4.25f, "2-C", 6);
			BuildMusicRoom(transform, 28.25f, num);
			BuildScienceLab(transform, -28.25f, num);
			BuildSciencePrepRoom(transform, -12.75f, num);
			BuildLibrary(transform, 12.75f, num);
			BuildBroadcastRoom(transform, 28.25f, num);
		}

		private void BuildStairs()
		{
			Transform transform = new GameObject("Grand Switchback Stairwell").transform;
			transform.SetParent(Root, worldPositionStays: false);
			float num = 0.6f;
			float num2 = 2.2f;
			float num3 = 3.8f;
			float numLanding2 = 5.6f;
			float numFloor3 = 7.4f;

			// --- Floor 1 to Floor 2 ---
			CreateBox(transform, new Vector3(0f, num2 - 0.09f, -20f), new Vector3(11.2f, 0.18f, 3.6f), matConcrete, addCollider: true, "Midway Landing Platform 1F-2F");
			CreateBox(transform, new Vector3(0f, 2f, -16f), new Vector3(0.2f, 3.2f, 4.4f), matConcrete, addCollider: true, "Stair Central Spine Lower");
			int num4 = 10;
			float num5 = (num2 - num) / (float)num4;
			float num6 = 4.4f / (float)num4;
			for (int i = 0; i < num4; i++)
			{
				float y = num + ((float)i + 0.5f) * num5;
				float z = -13.8f - ((float)i + 0.5f) * num6;
				CreateBox(transform, new Vector3(3f, y, z), new Vector3(4.4f, num5, num6), matConcrete, addCollider: true, "Step F1 " + i);
			}
			CreateRamp(transform, new Vector3(3f, 1.4f, -16f), new Vector3(4.4f, 0.1f, 4.8f), 19.5f, "Flight 1 Smooth Ramp");
			CreateBox(transform, new Vector3(5.3f, 2.25f, -16f), new Vector3(0.08f, 0.9f, 4.4f), matDarkMetal, addCollider: false, "Handrail F1 Wall");
			CreateBox(transform, new Vector3(0.7f, 2.25f, -16f), new Vector3(0.08f, 0.9f, 4.4f), matDarkMetal, addCollider: false, "Handrail F1 Inner");

			int num7 = 10;
			float num8 = (num3 - num2) / (float)num7;
			float num9 = 4.4f / (float)num7;
			for (int j = 0; j < num7; j++)
			{
				float y2 = num2 + ((float)j + 0.5f) * num8;
				float z2 = -18.2f + ((float)j + 0.5f) * num9;
				CreateBox(transform, new Vector3(-3f, y2, z2), new Vector3(4.4f, num8, num9), matConcrete, addCollider: true, "Step F2 " + j);
			}
			CreateRamp(transform, new Vector3(-3f, 3f, -16f), new Vector3(4.4f, 0.1f, 4.8f), -19.5f, "Flight 2 Smooth Ramp");
			CreateBox(transform, new Vector3(-5.3f, 3.85f, -16f), new Vector3(0.08f, 0.9f, 4.4f), matDarkMetal, addCollider: false, "Handrail F2 Wall");
			CreateBox(transform, new Vector3(-0.7f, 3.85f, -16f), new Vector3(0.08f, 0.9f, 4.4f), matDarkMetal, addCollider: false, "Handrail F2 Inner");
			CreateBox(transform, new Vector3(0f, num2 + 0.5f, -21.8f), new Vector3(11f, 1f, 0.08f), matDarkMetal, addCollider: false, "Landing Railing 1");

			// --- Floor 2 to Floor 3 ---
			CreateBox(transform, new Vector3(0f, numLanding2 - 0.09f, -20f), new Vector3(11.2f, 0.18f, 3.6f), matConcrete, addCollider: true, "Midway Landing Platform 2F-3F");
			CreateBox(transform, new Vector3(0f, 5.6f, -16f), new Vector3(0.2f, 3.6f, 4.4f), matConcrete, addCollider: true, "Stair Central Spine Upper");

			int numStepsUpper = 10;
			float stepHeightUpper = (numLanding2 - num3) / (float)numStepsUpper;
			float stepDepthUpper = 4.4f / (float)numStepsUpper;

			// Flight 3 (Floor 2 up North to Landing 2 at X = +3f)
			for (int k = 0; k < numStepsUpper; k++)
			{
				float y3 = num3 + ((float)k + 0.5f) * stepHeightUpper;
				float z3 = -13.8f - ((float)k + 0.5f) * stepDepthUpper;
				CreateBox(transform, new Vector3(3f, y3, z3), new Vector3(4.4f, stepHeightUpper, stepDepthUpper), matConcrete, addCollider: true, "Step F3 " + k);
			}
			CreateRamp(transform, new Vector3(3f, 4.7f, -16f), new Vector3(4.4f, 0.1f, 4.8f), 22.25f, "Flight 3 Smooth Ramp");
			CreateBox(transform, new Vector3(5.3f, 5.55f, -16f), new Vector3(0.08f, 0.9f, 4.4f), matDarkMetal, addCollider: false, "Handrail F3 Wall");
			CreateBox(transform, new Vector3(0.7f, 5.55f, -16f), new Vector3(0.08f, 0.9f, 4.4f), matDarkMetal, addCollider: false, "Handrail F3 Inner");

			// Flight 4 (Landing 2 up South to Floor 3 at X = -3f)
			float stepHeightF4 = (numFloor3 - numLanding2) / (float)numStepsUpper;
			for (int l = 0; l < numStepsUpper; l++)
			{
				float y4 = numLanding2 + ((float)l + 0.5f) * stepHeightF4;
				float z4 = -18.2f + ((float)l + 0.5f) * stepDepthUpper;
				CreateBox(transform, new Vector3(-3f, y4, z4), new Vector3(4.4f, stepHeightF4, stepDepthUpper), matConcrete, addCollider: true, "Step F4 " + l);
			}
			CreateRamp(transform, new Vector3(-3f, 6.5f, -16f), new Vector3(4.4f, 0.1f, 4.8f), -22.25f, "Flight 4 Smooth Ramp");
			CreateBox(transform, new Vector3(-5.3f, 7.35f, -16f), new Vector3(0.08f, 0.9f, 4.4f), matDarkMetal, addCollider: false, "Handrail F4 Wall");
			CreateBox(transform, new Vector3(-0.7f, 7.35f, -16f), new Vector3(0.08f, 0.9f, 4.4f), matDarkMetal, addCollider: false, "Handrail F4 Inner");
			CreateBox(transform, new Vector3(0f, numLanding2 + 0.5f, -21.8f), new Vector3(11f, 1f, 0.08f), matDarkMetal, addCollider: false, "Landing Railing 2");

			// Stairwell Lighting
			CreateInteriorLamp(transform, new Vector3(0f, 3.4f, -20f));
			CreateInteriorLamp(transform, new Vector3(0f, 7.0f, -20f));
			CreateInteriorLamp(transform, new Vector3(0f, 10.6f, -16f));
		}

		private void BuildClassroomDoor(Transform parent, Vector3 basePos, float yaw, string roomName)
		{
            Transform door = Assembly(parent, "Sliding Door " + roomName, basePos, yaw);
            CreateBox(door, new Vector3(-0.75f, 1.15f, 0f), new Vector3(0.12f, 2.3f, 0.28f), matDarkMetal, true, "Left door jamb");
            CreateBox(door, new Vector3(0.75f, 1.15f, 0f), new Vector3(0.12f, 2.3f, 0.28f), matDarkMetal, true, "Right door jamb");
            CreateBox(door, new Vector3(0f, 2.36f, 0f), new Vector3(1.62f, 0.12f, 0.28f), matDarkMetal, false, "Sliding door head track");
            Transform plate = Assembly(door, "Nameplate", new Vector3(0f, 2.58f, 0.15f));
            CreateBox(plate, Vector3.zero, new Vector3(0.75f, 0.28f, 0.06f), matDoorWood, false, "Nameplate backing");
            CreateBox(plate, new Vector3(0f, 0f, 0.04f), new Vector3(0.68f, 0.22f, 0.02f), matPaper, false, "Label Base");
            Transform leaf = Assembly(door, "Sliding Leaf", new Vector3(0.85f, 1.12f, 0.08f));
            CreateBox(leaf, new Vector3(0f, -0.465f, 0f), new Vector3(0.95f, 1.27f, 0.052f), matDoorWood, false, "Solid lower door panel");
            CreateBox(leaf, new Vector3(0f, 1.005f, 0f), new Vector3(0.95f, 0.19f, 0.052f), matDoorWood, false, "Door top rail");
            for (int side = -1; side <= 1; side += 2)
                CreateBox(leaf, new Vector3(side * 0.4325f, 0.54f, 0f), new Vector3(0.085f, 0.74f, 0.052f), matDoorWood, false, "Door glazing stile");
            CreateBox(leaf, new Vector3(0f, 0.54f, 0f), new Vector3(0.78f, 0.74f, 0.008f), matGlass, false, "Door Window Glass");
            CreateBox(leaf, new Vector3(0f, 0.54f, 0f), new Vector3(0.026f, 0.74f, 0.026f), matDoorWood, false, "Window centre mullion");
            CreateBox(leaf, new Vector3(-0.38f, -0.06f, 0.044f), new Vector3(0.025f, 0.18f, 0.025f), matDarkMetal, false, "Separate sliding pull");
            CreateBox(leaf, new Vector3(-0.38f, -0.06f, -0.044f), new Vector3(0.025f, 0.18f, 0.025f), matDarkMetal, false, "Reverse sliding pull");
            DeadAirArtMotionSlidingLeaf.Attach(leaf, new Vector3(0.85f, 1.12f, 0.08f), new Vector3(1.42f, 1.12f, 0.08f), door, 2.5f);
        }

		private void BuildClassroom(Transform parent, float centerX, float floorY, float centerZ, string roomName, int classroomIndex)
		{
			Transform transform = new GameObject("Classroom " + roomName).transform;
			transform.SetParent(parent, worldPositionStays: false);
            float teachingSide = ClassroomTeachingSideX(centerX, centerZ);
            BuildTeachingWall(transform, new Vector3(teachingSide, floorY, centerZ), 90f);
            Transform frontDesk = BuildWorktable(transform, new Vector3(teachingSide + 1.35f, floorY + .45f, centerZ), new Vector3(1.25f, .9f, .7f), matDeskWood, "Teacher teaching desk", true);
            frontDesk.localRotation = Quaternion.Euler(0f, 90f, 0f);
			CreateBox(transform, new Vector3(centerX, floorY + 0.08f, centerZ + 2.8f), new Vector3(2.8f, 0.16f, 1.4f), matWoodFloor, addCollider: true, "Podium Stage");
			BuildWorktable(transform, new Vector3(centerX, floorY + 0.55f, centerZ + 2.8f), new Vector3(1.6f, 0.75f, 0.85f), matDeskWood, "Teacher preparation table", true);
			CreateBox(transform, new Vector3(centerX + 0.4f, floorY + 0.95f, centerZ + 2.8f), new Vector3(0.35f, 0.04f, 0.25f), matPaper, addCollider: false, "Grade Binder");
			int num = 0;
			for (int i = 0; i < 3; i++)
			{
				for (int j = 0; j < 4; j++)
				{
					num++;
					float x = centerX - 4.2f + (float)j * 2.8f;
					float z = centerZ + 0.9f - (float)i * 2.2f;
					bool isOverturned = (classroomIndex + num) % 11 == 0;
					bool num2 = (classroomIndex * 3 + num) % 5 == 0;
					bool isTippedChair = (classroomIndex * 2 + num) % 7 == 0;
					float yaw = (num2 ? ((num % 2 == 0) ? 22f : (-28f)) : UnityEngine.Random.Range(-3f, 3f));
					BuildAuthenticDesk(transform, new Vector3(x, floorY, z), 90f + yaw, isOverturned, isTippedChair);
				}
			}
			for (int k = -1; k <= 1; k += 2)
			{
				float num3 = centerX + (float)k * 3.8f;
				CreateBox(transform, new Vector3(num3, floorY + 0.5f, centerZ - 3.95f), new Vector3(4.2f, 1f, 0.4f), matDeskWood, addCollider: true, "Cubby Unit");

			}
			BuildClassroomTrashAndClutter(transform, centerX, floorY, centerZ, classroomIndex);
			CreateInteriorLamp(transform, new Vector3(centerX - 2.8f, floorY + 3.1f, centerZ));
			CreateInteriorLamp(transform, new Vector3(centerX + 2.8f, floorY + 3.1f, centerZ));
		}

		private void BuildAuthenticDesk(Transform parent, Vector3 pos, float yaw, bool isOverturned, bool isTippedChair)
		{
            Transform furniture = Assembly(parent, "Authentic School Desk", pos, yaw);
            Transform desk = Assembly(furniture, "Independent student desk", isOverturned ? new Vector3(0f, 0.34f, 0f) : Vector3.zero);
            desk.localRotation = Quaternion.Euler(0f, 0f, isOverturned ? 85f : 0f);
            GameObject scannedDesk = DeadAirSchoolPhysics.InstallScanned(desk, "school-desk", Vector3.zero, Quaternion.identity, new Vector3(0.68f, 0.75f, 0.48f));
            if (scannedDesk == null)
            {
                CreateBox(desk, new Vector3(0f, 0.72f, 0f), new Vector3(0.68f, 0.035f, 0.48f), matDeskWood, false, "Laminated student desktop");
                CreateBox(desk, new Vector3(0f, 0.61f, 0f), new Vector3(0.56f, 0.018f, 0.36f), matDarkMetal, false, "Open book tray base");
                for (int side = -1; side <= 1; side += 2)
                {
                    CreateBox(desk, new Vector3(side * 0.27f, 0.65f, 0f), new Vector3(0.018f, 0.08f, 0.36f), matDarkMetal, false, "Book tray side wall");
                    for (int front = -1; front <= 1; front += 2)
                    {
                        CreateCylinder(desk, new Vector3(side * 0.29f, 0.35f, front * 0.19f), 0.016f, 0.7f, matDarkMetal, false, "Student desk tubular leg");
                        CreateCylinder(desk, new Vector3(side * 0.29f, 0.018f, front * 0.19f), 0.021f, 0.036f, matSchoolRubber, false, "Student desk foot cap");
                    }
                }
            }
            AddCollisionProxy(desk, new Vector3(0f, 0.72f, 0f), new Vector3(0.68f, 0.035f, 0.48f), "Desktop collision");
            for (int x = -1; x <= 1; x += 2)
                AddCollisionProxy(desk, new Vector3(x * 0.29f, 0.35f, 0f), new Vector3(0.045f, 0.7f, 0.4f), "Desk frame collision");
            Transform chair = Assembly(furniture, "Independent student chair", isTippedChair ? new Vector3(0f, 0.21f, 0.8f) : new Vector3(0f, 0f, 0.45f));
            chair.localRotation = isTippedChair ? Quaternion.Euler(-75f, 15f, 0f) : Quaternion.identity;
            GameObject scannedChair = DeadAirSchoolPhysics.InstallScanned(chair, "school-chair", Vector3.zero, Quaternion.identity, new Vector3(0.4f, 0.9f, 0.4f));
            if (scannedChair == null) BuildChair(chair, new Vector3(0f, 0.45f, 0f), new Vector3(0.4f, 0.9f, 0.4f), false, "Student chair frame", true);
            else
            {
                AddCollisionProxy(chair, new Vector3(0f, 0.43f, 0f), new Vector3(0.4f, 0.035f, 0.38f), "Chair seat collision");
                AddCollisionProxy(chair, new Vector3(0f, 0.72f, 0.17f), new Vector3(0.4f, 0.35f, 0.035f), "Chair back collision");
            }
            CreateBox(desk, new Vector3(-0.37f, 0.44f, 0f), new Vector3(0.12f, 0.29f, 0.22f), matSchoolFabric, false, "Canvas school bag");
            if (!isOverturned && (int)(pos.x + pos.z) % 2 == 0)
                CreateBox(desk, new Vector3(0.02f, 0.743f, 0f), new Vector3(0.24f, 0.005f, 0.32f), matPaper, false, "Desk Paper");
        }

		private void BuildClassroomTrashAndClutter(Transform parent, float centerX, float floorY, float centerZ, int seed)
		{
			Transform transform = new GameObject("Classroom Debris & Trash").transform;
			transform.SetParent(parent, worldPositionStays: false);
			System.Random random = new System.Random(seed * 777 + 123);
			int num = 12;
			for (int i = 0; i < num; i++)
			{
				float x = centerX + (float)(random.NextDouble() * 10.0 - 5.0);
				float z = centerZ + (float)(random.NextDouble() * 6.5 - 3.2);
				float y = (float)(random.NextDouble() * 360.0);
				CreateBox(transform, new Vector3(x, floorY + 0.01f, z), new Vector3(0.28f, 0.005f, 0.2f), matPaper, addCollider: false, "Loose Exam Paper").transform.rotation = Quaternion.Euler(0f, y, 0f);
			}
			for (int j = 0; j < 6; j++)
			{
				float x2 = centerX + (float)(random.NextDouble() * 8.0 - 4.0);
				float z2 = centerZ + (float)(random.NextDouble() * 6.0 - 3.0);
				CreateBox(transform, new Vector3(x2, floorY + 0.04f, z2), new Vector3(0.08f, 0.08f, 0.08f), matPaper, addCollider: false, "Crumpled Paper Ball");
			}
			GameObject gameObject = CreateCylinder(transform, new Vector3(centerX + 5.8f, floorY + 0.3f, centerZ + 3.2f), 0.18f, 0.6f, matTrashCan, addCollider: true, "Trash Bin");
			CreateBox(gameObject.transform, new Vector3(0f, 0.32f, 0f), new Vector3(0.22f, 0.14f, 0.22f), matPaper, addCollider: false, "Trash Overflow");
			CreateCylinder(transform, new Vector3(centerX - 2.5f, floorY + 0.035f, centerZ - 1.8f), 0.035f, 0.12f, matCan, addCollider: false, "Soda Can").transform.rotation = Quaternion.Euler(90f, 45f, 0f);
			CreateCylinder(transform, new Vector3(centerX + 3.2f, floorY + 0.035f, centerZ + 0.5f), 0.035f, 0.12f, matCan, addCollider: false, "Tea Can").transform.rotation = Quaternion.Euler(90f, -30f, 0f);
			for (int k = 0; k < 3; k++)
			{
				float x3 = centerX + (float)(random.NextDouble() * 8.0 - 4.0);
				float z3 = centerZ + (float)(random.NextDouble() * 5.0 - 2.5);
				float y2 = (float)(random.NextDouble() * 180.0);
				CreateBox(transform, new Vector3(x3, floorY + 0.02f, z3), new Vector3(0.22f, 0.035f, 0.3f), matBooks, addCollider: false, "Dropped Textbook").transform.rotation = Quaternion.Euler(0f, y2, 0f);
			}
		}

		private void BuildNurseOffice(Transform parent, float centerX, float floorY)
		{
			Transform transform = new GameObject("Nurse Office (保健室)").transform;
			transform.SetParent(parent, worldPositionStays: false);
			for (int i = 0; i < 2; i++)
			{
				float num = -3.2f - (float)i * 2.8f;
				CreateBox(transform, new Vector3(centerX + 4.2f, floorY + 0.45f, num), new Vector3(1.3f, 0.4f, 2.3f), matWhite, addCollider: true, "Bed");
				CreateBox(transform, new Vector3(centerX + 4.2f, floorY + 0.7f, num + 0.9f), new Vector3(0.9f, 0.15f, 0.5f), matSchoolFabric, addCollider: false, "Pillow");
				CreateBox(transform, new Vector3(centerX + 3f, floorY + 1.2f, num), new Vector3(0.05f, 1.8f, 2.2f), matSchoolFabric, addCollider: false, "Privacy Curtain");
			}
			CreateBox(transform, new Vector3(centerX - 3.5f, floorY + 1.5f, -6.5f), new Vector3(2.4f, 2.8f, 0.6f), matDarkMetal, addCollider: true, "Medicine Cabinet");
			CreateBox(transform, new Vector3(centerX - 3.5f, floorY + 0.75f, -2.5f), new Vector3(1.8f, 0.75f, 1f), matDeskWood, addCollider: true, "Nurse Desk");
			CreateBox(transform, new Vector3(centerX + 1f, floorY + 1f, -7.5f), new Vector3(0.35f, 2f, 0.35f), matWhite, addCollider: true, "Height Scale");
			CreateBox(transform, new Vector3(centerX - 1.5f, floorY + 0.03f, -4.5f), new Vector3(0.14f, 0.06f, 0.14f), matWhite, addCollider: false, "Bandage Roll");
			CreateBox(transform, new Vector3(centerX - 1.8f, floorY + 0.02f, -4.2f), new Vector3(0.12f, 0.04f, 0.08f), matPaper, addCollider: false, "Medicine Box");
			CreateInteriorLamp(transform, new Vector3(centerX, floorY + 3.1f, -4.25f));
		}

		private void BuildStaffRoom(Transform parent, float centerX, float floorY)
		{
			Transform transform = new GameObject("Staff Room (職員室)").transform;
			transform.SetParent(parent, worldPositionStays: false);
			for (int i = 0; i < 2; i++)
			{
				float z = -16f - (float)i * 3.4f;
				GameObject gameObject = CreateBox(transform, new Vector3(centerX, floorY + 0.75f, z), new Vector3(7.2f, 0.75f, 1.8f), matDeskWood, addCollider: true, "Staff Pod Desk");
				for (float num = -2.4f; num <= 2.4f; num += 2.4f)
				{
					CreateBox(gameObject.transform, new Vector3(num, 0.45f, 0.3f), new Vector3(0.4f, 0.25f, 0.3f), matPaper, addCollider: false, "Paper Stack");
					CreateBox(gameObject.transform, new Vector3(num, 0.45f, -0.3f), new Vector3(0.4f, 0.25f, 0.3f), matPaper, addCollider: false, "Paper Stack");
				}
			}
			CreateBox(transform, new Vector3(centerX - 5.5f, floorY + 1.6f, -17.75f), new Vector3(0.8f, 2.8f, 6f), matDarkMetal, addCollider: true, "Filing Cabinets");
			for (int j = 0; j < 8; j++)
			{
				float x = centerX + UnityEngine.Random.Range(-4.5f, 4.5f);
				float z2 = -17.75f + UnityEngine.Random.Range(-2.5f, 2.5f);
				CreateBox(transform, new Vector3(x, floorY + 0.01f, z2), new Vector3(0.28f, 0.005f, 0.22f), matPaper, addCollider: false, "Printout Document");
			}
			CreateInteriorLamp(transform, new Vector3(centerX, floorY + 3.1f, -17.75f));
		}

		private void BuildPrincipalOffice(Transform parent, float centerX, float floorY)
		{
			Transform transform = new GameObject("Principal Office (校長室)").transform;
			transform.SetParent(parent, worldPositionStays: false);
			CreateBox(transform, new Vector3(centerX - 3.4f, floorY + 0.75f, -18.9f), new Vector3(2.8f, 0.75f, 1.4f), matDeskWood, addCollider: true, "Executive Desk");
			CreateBox(transform, new Vector3(centerX - 3.4f, floorY + 0.65f, -19.9f), new Vector3(0.8f, 1.1f, 0.8f), matDarkMetal, addCollider: true, "Leather Chair");
			CreateBox(transform, new Vector3(centerX - 2.5f, floorY + 0.45f, -16.5f), new Vector3(1.8f, 0.65f, 0.8f), matDarkMetal, addCollider: true, "Sofa L");
			CreateBox(transform, new Vector3(centerX + 2.5f, floorY + 0.45f, -16.5f), new Vector3(1.8f, 0.65f, 0.8f), matDarkMetal, addCollider: true, "Sofa R");
			CreateBox(transform, new Vector3(centerX - 3.4f, floorY + 0.35f, -16.5f), new Vector3(1.6f, 0.45f, 0.8f), matDeskWood, addCollider: true, "Coffee Table");
			CreateInteriorLamp(transform, new Vector3(centerX, floorY + 3.1f, -17.75f));
		}

		private void BuildComputerLab(Transform parent, float centerX, float floorY)
		{
			Transform transform = new GameObject("Computer Lab (PC室)").transform;
			transform.SetParent(parent, worldPositionStays: false);
			for (int i = 0; i < 2; i++)
			{
				float z = -16f - (float)i * 3.2f;
				GameObject gameObject = CreateBox(transform, new Vector3(centerX, floorY + 0.72f, z), new Vector3(8f, 0.72f, 1.4f), matLabCounter, addCollider: true, "Computer Bench");
				for (float num = -2.8f; num <= 2.8f; num += 1.8f)
				{
					CreateBox(gameObject.transform, new Vector3(num, 0.55f, 0f), new Vector3(0.55f, 0.42f, 0.12f), matDarkMetal, addCollider: false, "Monitor");
					CreateBox(gameObject.transform, new Vector3(num, 0.38f, 0.35f), new Vector3(0.45f, 0.03f, 0.18f), matWhite, addCollider: false, "Keyboard");
				}
			}
			CreateInteriorLamp(transform, new Vector3(centerX, floorY + 3.1f, -17.75f));
		}

		private void BuildArchiveRoom(Transform parent, float centerX, float floorY)
		{
			Transform transform = new GameObject("Archive Room (資料室)").transform;
			transform.SetParent(parent, worldPositionStays: false);
			for (int i = 0; i < 3; i++)
			{
				float z = -15.5f - (float)i * 2.2f;
				GameObject gameObject = CreateBox(transform, new Vector3(centerX - 1.5f, floorY + 1.4f, z), new Vector3(6.5f, 2.6f, 0.7f), matDeskWood, addCollider: true, "Archive Shelf");
			}
			CreateInteriorLamp(transform, new Vector3(centerX, floorY + 3.1f, -17.75f));
		}

		private void BuildMusicRoom(Transform parent, float centerX, float floorY)
		{
			Transform transform = new GameObject("Music Room (音楽室)").transform;
			transform.SetParent(parent, worldPositionStays: false);
            // The first-floor ceiling slab also forms the visible second-floor cap above the parquet.
            float furnishingFloor = floorY + .125f;
            BuildUprightPiano(transform, new Vector3(centerX + 3.5f, floorY, -4.25f));
            BuildPianoStool(transform, new Vector3(centerX + 3.5f, furnishingFloor, -3.2f));
			for (int i = 0; i < 9; i++)
			{
				float x = centerX + UnityEngine.Random.Range(-3.5f, 3.5f);
				float z = -4.25f + UnityEngine.Random.Range(-2.5f, 2.5f);
				CreateBox(transform, new Vector3(x, floorY + 0.01f, z), new Vector3(0.24f, 0.005f, 0.32f), matPaper, addCollider: false, "Sheet Music Page");
			}
			for (int j = 0; j < 3; j++)
			{
				float num = -2.5f - (float)j * 1.8f;
				for (int k = 0; k < 4; k++)
				{
					float x2 = centerX - 3.8f + (float)k * 1.8f;
                    CreateBox(transform, new Vector3(x2, furnishingFloor + .45f, num), new Vector3(.45f, .9f, .45f), matDeskWood, true, "Chair")
                        .transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                    BuildMusicStand(transform, new Vector3(x2, furnishingFloor, num + .6f));
				}
			}
			CreateInteriorLamp(transform, new Vector3(centerX, floorY + 3.1f, -4.25f));
		}

		private void BuildScienceLab(Transform parent, float centerX, float floorY)
		{
			Transform transform = new GameObject("Science Lab (理科室)").transform;
			transform.SetParent(parent, worldPositionStays: false);
			for (int i = 0; i < 2; i++)
			{
				float z = -16.2f - (float)i * 3.4f;
                BuildSinkBench(transform, new Vector3(centerX, floorY + 0.45f, z));
			}
			Transform transform2 = new GameObject("Skeleton Stand").transform;
			transform2.SetParent(transform, worldPositionStays: false);
			transform2.localPosition = new Vector3(centerX + 5.5f, floorY, -20.5f);
			CreateCylinder(transform2, new Vector3(0f, 0.9f, 0f), 0.025f, 1.8f, matDarkMetal, addCollider: false, "Stand");
			CreateBox(transform2, new Vector3(0f, 1.6f, 0f), new Vector3(0.24f, 0.28f, 0.24f), matWhite, addCollider: false, "Skull");
			CreateBox(transform2, new Vector3(0f, 1.15f, 0f), new Vector3(0.35f, 0.55f, 0.22f), matWhite, addCollider: false, "Ribcage");
			CreateInteriorLamp(transform, new Vector3(centerX, floorY + 3.1f, -17.75f));
		}

		private void BuildSciencePrepRoom(Transform parent, float centerX, float floorY)
		{
			Transform transform = new GameObject("Science Prep (準備室)").transform;
			transform.SetParent(parent, worldPositionStays: false);
			CreateBox(transform, new Vector3(centerX - 3.5f, floorY + 1.5f, -17.75f), new Vector3(2f, 2.8f, 6f), matDarkMetal, addCollider: true, "Reagent Cabinets");
			CreateBox(transform, new Vector3(centerX + 2f, floorY + 0.45f, -17.75f), new Vector3(4.5f, 0.9f, 1.6f), matLabCounter, addCollider: true, "Prep Desk");
			CreateInteriorLamp(transform, new Vector3(centerX, floorY + 3.1f, -17.75f));
		}

		private void BuildLibrary(Transform parent, float centerX, float floorY)
		{
			Transform transform = new GameObject("Library (図書室)").transform;
			transform.SetParent(parent, worldPositionStays: false);
			for (int i = 0; i < 3; i++)
			{
				float z = -15.5f - (float)i * 2.6f;
				GameObject gameObject = CreateBox(transform, new Vector3(centerX - 3f, floorY + 1.4f, z), new Vector3(5.5f, 2.8f, 0.65f), matDeskWood, addCollider: true, "Bookshelf");
			}
			GameObject gameObject2 = CreateBox(transform, new Vector3(centerX + 3.2f, floorY + 0.4f, -17.75f), new Vector3(2.6f, 0.8f, 4.8f), matDeskWood, addCollider: true, "Study Table");
			for (float num = -1.5f; num <= 1.5f; num += 1.5f)
			{
				Light light = CreateBox(gameObject2.transform, new Vector3(0f, 0.55f, num), new Vector3(0.22f, 0.35f, 0.22f), matTorii, addCollider: false, "Desk Lamp").AddComponent<Light>();
				light.type = LightType.Point;
				light.color = new Color(0.9f, 1f, 0.7f);
				light.intensity = 0.9f;
				light.range = 3.2f;
			}
			for (int k = 0; k < 6; k++)
			{
				float x = centerX + UnityEngine.Random.Range(-4f, 4f);
				float z2 = -17.75f + UnityEngine.Random.Range(-3f, 3f);
				CreateBox(transform, new Vector3(x, floorY + 0.02f, z2), new Vector3(0.22f, 0.04f, 0.32f), matBooks, addCollider: false, "Fallen Library Book");
			}
			CreateInteriorLamp(transform, new Vector3(centerX, floorY + 3.1f, -17.75f));
		}

		private void BuildBroadcastRoom(Transform parent, float centerX, float floorY)
		{
			Transform transform = new GameObject("AV Room (放送室)").transform;
			transform.SetParent(parent, worldPositionStays: false);
			CreateBox(transform, new Vector3(centerX, floorY + 0.45f, -17.75f), new Vector3(4.2f, 0.9f, 1.6f), matDarkMetal, addCollider: true, "Console Desk");
			CreateBox(transform, new Vector3(centerX, floorY + 1.25f, -17.75f), new Vector3(3.2f, 0.55f, 0.35f), matLabCounter, addCollider: false, "Rack Equipment");
            DeadAirSchoolPhysics.InstallScanned(transform, "cassette-player", new Vector3(centerX - 0.8f, floorY + 0.9f, -17.22f), Quaternion.identity, new Vector3(0.6f, 0.28f, 0.28f));
            DeadAirSchoolPhysics.InstallScanned(transform, "radio-transceiver", new Vector3(centerX + 0.65f, floorY + 0.9f, -17.25f), Quaternion.identity, new Vector3(0.62f, 0.25f, 0.3f));
			CreateBox(transform, new Vector3(centerX - 2.2f, floorY + 1.2f, -17.75f), new Vector3(0.4f, 0.6f, 0.4f), matDarkMetal, addCollider: false, "Speaker L");
			CreateBox(transform, new Vector3(centerX + 2.2f, floorY + 1.2f, -17.75f), new Vector3(0.4f, 0.6f, 0.4f), matDarkMetal, addCollider: false, "Speaker R");
			CreateInteriorLamp(transform, new Vector3(centerX, floorY + 3.1f, -17.75f));
		}

		private void CreateInteriorLamp(Transform parent, Vector3 pos)
		{
			Transform transform = new GameObject("Hallway Lamp").transform;
			transform.SetParent(parent, worldPositionStays: false);
			transform.localPosition = pos;
			CreateBox(transform, Vector3.zero, new Vector3(1.6f, 0.08f, 0.35f), matWhite, addCollider: false);
			GameObject gameObject = CreateBox(transform, new Vector3(0f, -0.04f, 0f), new Vector3(1.4f, 0.04f, 0.25f), matPlaster, addCollider: false);
			Light light = transform.gameObject.AddComponent<Light>();
			light.type = LightType.Point;
			light.color = new Color(1f, 0.95f, 0.84f);
			light.intensity = 1.6f;
			light.range = 8.5f;
			lamps.Add(new FlickeringLamp
			{
				light = light,
				bulb = gameObject.GetComponent<Renderer>(),
				baseIntensity = 1.6f,
				worksNormally = true,
				flickerOffset = UnityEngine.Random.value * 20f,
				isInterior = true
			});
		}

		private void BuildExitSign(Transform parent, Vector3 pos)
		{
			Light light = CreateBox(parent, pos, new Vector3(0.6f, 0.3f, 0.08f), matTorii, addCollider: false, "ExitSign").AddComponent<Light>();
			light.type = LightType.Point;
			light.color = new Color(0.1f, 1f, 0.4f);
			light.intensity = 1.1f;
			light.range = 2.8f;
		}

		private void BuildFireHydrant(Transform parent, Vector3 pos)
		{
			GameObject gameObject = CreateBox(parent, pos, new Vector3(0.7f, 1.2f, 0.15f), matTorii, addCollider: true, "FireHydrant");
			CreateBox(gameObject.transform, new Vector3(0f, 0.25f, 0.08f), new Vector3(0.3f, 0.3f, 0.04f), matWhite, addCollider: false);
		}

		private void BuildNoticeBoard(Transform parent, Vector3 pos)
		{
			Transform boardRoot = new GameObject("NoticeBoard").transform;
			boardRoot.SetParent(parent, worldPositionStays: false);
			boardRoot.localPosition = pos;
			CreateBox(boardRoot, Vector3.zero, new Vector3(2.4f, 1.2f, 0.05f), matDeskWood, addCollider: false, "Board Frame");
			CreateBox(boardRoot, new Vector3(0f, 0f, -0.005f), new Vector3(2.28f, 1.08f, 0.05f), matPaper, addCollider: false, "Board Cork");
			CreateBox(boardRoot, new Vector3(-0.6f, 0.15f, -0.035f), new Vector3(0.55f, 0.7f, 0.02f), matWhite, addCollider: false, "Poster 1");
			CreateBox(boardRoot, new Vector3(0.45f, -0.1f, -0.035f), new Vector3(0.65f, 0.5f, 0.02f), matPaper, addCollider: false, "Poster 2");
			CreateBox(boardRoot, new Vector3(-0.05f, -0.2f, -0.035f), new Vector3(0.35f, 0.45f, 0.02f), matWhite, addCollider: false, "Poster 3");
		}

		private void BuildSchoolGenerator(Transform parent, Vector3 pos)
		{
			Transform gen = new GameObject("School Generator").transform;
			gen.SetParent(parent, worldPositionStays: false);
			gen.localPosition = pos;

			CreateBox(gen, new Vector3(0f, 0.08f, 0f), new Vector3(1.8f, 0.16f, 1.4f), matConcrete, addCollider: true, "Pad");
			CreateBox(gen, new Vector3(0f, 0.6f, 0f), new Vector3(1.5f, 0.88f, 1.1f), matDarkMetal, addCollider: true, "Generator Body");
			CreateBox(gen, new Vector3(0f, 1.12f, 0f), new Vector3(1.2f, 0.22f, 0.8f), matTorii, addCollider: false, "Fuel Tank");
			CreateCylinder(gen, new Vector3(0.4f, 1.26f, 0f), 0.08f, 0.08f, matDarkMetal, addCollider: false);
			CreateCylinder(gen, new Vector3(-0.55f, 1.35f, -0.3f), 0.05f, 0.55f, matDarkMetal, addCollider: false);
			CreateBox(gen, new Vector3(0f, 0.65f, 0.56f), new Vector3(0.7f, 0.45f, 0.05f), matPlaster, addCollider: false, "Control Panel");
			GameObject ind = CreateBox(gen, new Vector3(-0.2f, 0.75f, 0.6f), new Vector3(0.08f, 0.08f, 0.04f), matWhite, addCollider: false, "Gen Indicator");
			generatorIndicatorRenderer = ind.GetComponent<Renderer>();
		}

		private void BuildSchoolBreaker(Transform parent, Vector3 pos)
		{
			Transform breaker = new GameObject("School Breaker Box").transform;
			breaker.SetParent(parent, worldPositionStays: false);
			breaker.localPosition = pos;

			CreateBox(breaker, Vector3.zero, new Vector3(0.1f, 0.85f, 0.6f), matDarkMetal, addCollider: true, "Panel Body");
			CreateBox(breaker, new Vector3(-0.045f, 0f, 0f), new Vector3(0.02f, 0.78f, 0.54f), matPlaster, addCollider: false, "Face Plate");
			CreateBox(breaker, new Vector3(-0.05f, 0.22f, 0f), new Vector3(0.02f, 0.24f, 0.35f), matDarkMetal, addCollider: false, "Meter Window");
			CreateBox(breaker, new Vector3(-0.062f, 0.22f, 0f), new Vector3(0.01f, 0.2f, 0.3f), matGlass, addCollider: false, "Meter Glass");
			GameObject ind = CreateBox(breaker, new Vector3(-0.06f, -0.05f, 0.15f), new Vector3(0.03f, 0.06f, 0.06f), matWhite, addCollider: false, "Breaker Light");
			breakerIndicatorRenderer = ind.GetComponent<Renderer>();
			GameObject handle = CreateBox(breaker, new Vector3(-0.07f, -0.15f, -0.08f), new Vector3(0.04f, 0.22f, 0.06f), matTorii, addCollider: false, "Breaker Lever");
			breakerHandleTransform = handle.transform;
		}

		private void BuildTraditionalPark()
		{
			Transform transform = new GameObject("Traditional Japanese Park").transform;
			transform.SetParent(Root, worldPositionStays: false);
			CreateBox(transform, new Vector3(0f, -0.05f, 80f), new Vector3(90f, 0.1f, 60f), matFoliage, addCollider: true, "Park Moss Ground");
			for (int i = -1; i <= 1; i += 2)
			{
				float num = 38f;
				float x = (float)i * (5.5f + num * 0.5f);
				CreateBox(transform, new Vector3(x, 0.65f, 50f), new Vector3(num, 1.3f, 0.45f), matStone, addCollider: true, "Park Stone Wall");
				CreateBox(transform, new Vector3((float)i * 5.5f, 1.2f, 50f), new Vector3(0.9f, 2.4f, 0.9f), matStone, addCollider: true, "Park Gate Post");
			}
			CreateBox(transform, new Vector3(0f, 0.02f, 68f), new Vector3(4.8f, 0.02f, 36f), matStone, addCollider: false, "Stone Path Main");
			CreateBox(transform, new Vector3(-14f, 0.02f, 80f), new Vector3(20f, 0.02f, 3.4f), matStone, addCollider: false, "Stone Path Left");
			CreateBox(transform, new Vector3(14f, 0.02f, 80f), new Vector3(20f, 0.02f, 3.4f), matStone, addCollider: false, "Stone Path Right");
			CreateBox(transform, new Vector3(0f, 0.02f, 96f), new Vector3(5.2f, 0.02f, 24f), matStone, addCollider: false, "Shrine Approach Path");
			Vector2[] array = new Vector2[16]
			{
				new Vector2(-16f, 56f),
				new Vector2(16f, 56f),
				new Vector2(-26f, 68f),
				new Vector2(26f, 68f),
				new Vector2(-12f, 78f),
				new Vector2(12f, 78f),
				new Vector2(-24f, 88f),
				new Vector2(24f, 88f),
				new Vector2(-8f, 98f),
				new Vector2(8f, 98f),
				new Vector2(-30f, 82f),
				new Vector2(30f, 82f),
				new Vector2(-20f, 62f),
				new Vector2(20f, 62f),
				new Vector2(-34f, 98f),
				new Vector2(34f, 98f)
			};
			for (int j = 0; j < array.Length; j++)
			{
				Vector2 vector = array[j];
				BuildAncientTree(transform, new Vector3(vector.x, 0f, vector.y));
			}
			array = new Vector2[8]
			{
				new Vector2(-3.4f, 54f),
				new Vector2(3.4f, 54f),
				new Vector2(-3.6f, 68f),
				new Vector2(3.6f, 68f),
				new Vector2(-3.6f, 82f),
				new Vector2(3.6f, 82f),
				new Vector2(-3.6f, 94f),
				new Vector2(3.6f, 94f)
			};
			for (int j = 0; j < array.Length; j++)
			{
				Vector2 vector2 = array[j];
				BuildStoneLantern(transform, new Vector3(vector2.x, 0f, vector2.y));
			}
			BuildToriiGate(transform, new Vector3(0f, 0f, 86f));
			BuildShintoShrine(transform, new Vector3(0f, 0f, 100f));
			BuildAzumayaGazebo(transform, new Vector3(16f, 0f, 72f));
			BuildParkBench(transform, new Vector3(-14f, 0f, 64f), 45f);
			BuildParkBench(transform, new Vector3(-16f, 0f, 84f), 90f);
			BuildParkBench(transform, new Vector3(18f, 0f, 86f), -90f);
			Vector3[] array2 = new Vector3[8]
			{
				new Vector3(-12f, 0f, 48.5f),
				new Vector3(12f, 0f, 48.5f),
				new Vector3(-6f, 0f, 60f),
				new Vector3(6f, 0f, 60f),
				new Vector3(-7f, 0f, 75f),
				new Vector3(7f, 0f, 75f),
				new Vector3(-5f, 0f, 90f),
				new Vector3(5f, 0f, 90f)
			};
			for (int k = 0; k < array2.Length; k++)
			{
				bool flag = k % 2 == 1;
				BuildStreetLamp(transform, array2[k], !flag);
			}
		}

		private void BuildAncientTree(Transform parent, Vector3 pos)
		{
			Transform transform = new GameObject("Ancient Tree").transform;
			transform.SetParent(parent, worldPositionStays: false);
			transform.localPosition = pos;
            if (TryInstallSchoolTree(transform, "pine-tree", Vector3.zero, new Vector3(7.5f, 11.7f, 7.5f), .48f, 6.4f,
                Mathf.Repeat(pos.x * 13f + pos.z * 7f, 360f))) return;
			CreateCylinder(transform, new Vector3(0f, 3.2f, 0f), 0.85f, 6.4f, matBark, addCollider: true, "Trunk");
			for (int i = 0; i < 4; i++)
			{
				float f = (float)i * MathF.PI * 0.5f;
				CreateBox(transform, new Vector3(Mathf.Sin(f) * 0.95f, 0.6f, Mathf.Cos(f) * 0.95f), new Vector3(0.9f, 1.2f, 0.9f), matBark);
			}
			for (int j = 0; j < 5; j++)
			{
				float f2 = (float)j / 5f * MathF.PI * 2f + 0.35f;
				Vector3 localPos = new Vector3(Mathf.Sin(f2) * 2.2f, 6f + (float)(j % 2) * 0.9f, Mathf.Cos(f2) * 2.2f);
				CreateCylinder(transform, localPos, 0.38f, 4.4f, matBark, addCollider: false);
				Vector3 localPos2 = new Vector3(Mathf.Sin(f2) * 3.6f, 7.8f + (float)(j % 2) * 1f, Mathf.Cos(f2) * 3.6f);
				CreateBox(transform, localPos2, new Vector3(4.8f, 3.2f, 4.8f), matFoliage, addCollider: false);
			}
			CreateBox(transform, new Vector3(0f, 9.6f, 0f), new Vector3(7f, 4.2f, 7f), matFoliage, addCollider: false);
		}

		private void BuildStoneLantern(Transform parent, Vector3 pos)
		{
			Transform transform = new GameObject("Stone Toro").transform;
			transform.SetParent(parent, worldPositionStays: false);
			transform.localPosition = pos;
			CreateBox(transform, new Vector3(0f, 0.15f, 0f), new Vector3(0.95f, 0.3f, 0.95f), matStone);
			CreateCylinder(transform, new Vector3(0f, 0.85f, 0f), 0.26f, 1.1f, matStone);
			CreateBox(transform, new Vector3(0f, 1.45f, 0f), new Vector3(0.88f, 0.18f, 0.88f), matStone);
			CreateBox(transform, new Vector3(0f, 1.8f, 0f), new Vector3(0.58f, 0.55f, 0.58f), matDarkMetal);
			CreateBox(transform, new Vector3(0f, 2.2f, 0f), new Vector3(1.1f, 0.22f, 1.1f), matStone);
			CreateBox(transform, new Vector3(0f, 2.4f, 0f), new Vector3(0.65f, 0.18f, 0.65f), matStone, addCollider: false);
			Light light = transform.gameObject.AddComponent<Light>();
			light.type = LightType.Point;
			light.color = new Color(1f, 0.68f, 0.28f);
			light.intensity = 1.5f;
			light.range = 5.2f;
		}

		private void BuildToriiGate(Transform parent, Vector3 pos)
		{
			Transform transform = new GameObject("Grand Torii Gate").transform;
			transform.SetParent(parent, worldPositionStays: false);
			transform.localPosition = pos;
			for (int i = -1; i <= 1; i += 2)
			{
				CreateCylinder(transform, new Vector3((float)i * 3.8f, 0.4f, 0f), 0.52f, 0.8f, matDarkMetal);
				CreateCylinder(transform, new Vector3((float)i * 3.8f, 4.2f, 0f), 0.4f, 6.8f, matTorii);
			}
			CreateBox(transform, new Vector3(0f, 5.6f, 0f), new Vector3(8.8f, 0.45f, 0.45f), matTorii);
			CreateBox(transform, new Vector3(0f, 7.2f, 0f), new Vector3(10.8f, 0.6f, 0.6f), matTorii);
			CreateBox(transform, new Vector3(0f, 7.6f, 0f), new Vector3(11.2f, 0.2f, 0.7f), matDarkMetal, addCollider: false);
		}

		private void BuildShintoShrine(Transform parent, Vector3 pos)
		{
			Transform transform = new GameObject("Shinto Shrine").transform;
			transform.SetParent(parent, worldPositionStays: false);
			transform.localPosition = pos;
			CreateBox(transform, new Vector3(0f, 2.2f, 0f), new Vector3(5.6f, 4.4f, 5f), matWoodFloor, addCollider: true, "Shrine Hall");
			BuildPitchedRoof(transform, new Vector3(0f, 5.6f, 0f), new Vector3(7.2f, 0.1f, 6.2f));
			CreateBox(transform, new Vector3(0f, 0.8f, -2.8f), new Vector3(1.6f, 0.9f, 1f), matWoodFloor, addCollider: true, "Offering Box");
		}

		private void BuildAzumayaGazebo(Transform parent, Vector3 pos)
		{
			Transform transform = new GameObject("Azumaya Gazebo (四阿)").transform;
			transform.SetParent(parent, worldPositionStays: false);
			transform.localPosition = pos;
			CreateBox(transform, new Vector3(0f, 0.25f, 0f), new Vector3(4.8f, 0.5f, 4.8f), matWoodFloor);
			for (int i = -1; i <= 1; i += 2)
			{
				for (int j = -1; j <= 1; j += 2)
				{
					CreateCylinder(transform, new Vector3((float)i * 2f, 1.8f, (float)j * 2f), 0.12f, 3.2f, matWoodFloor, addCollider: false);
				}
			}
			BuildPitchedRoof(transform, new Vector3(0f, 4.6f, 0f), new Vector3(5.8f, 0.1f, 5.8f));
		}

		private void BuildParkBench(Transform parent, Vector3 pos, float yaw)
		{
            GameObject scanned = DeadAirSchoolPhysics.InstallScanned(parent, "wooden-bench", pos, Quaternion.Euler(0f, yaw, 0f), new Vector3(2.2f, .49f, 0.6f));
            if (scanned != null)
            {
                AddCollisionProxy(scanned.transform, new Vector3(0f, 0.45f, 0f), new Vector3(2.2f, 0.08f, 0.58f), "Bench seat collision");

                return;
            }
			Transform transform = new GameObject("Park Bench").transform;
			transform.SetParent(parent, worldPositionStays: false);
			transform.localPosition = pos;
			transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
			CreateBox(transform, new Vector3(0f, 0.45f, 0f), new Vector3(2.2f, 0.08f, 0.6f), matWoodFloor);
			CreateBox(transform, new Vector3(0f, 0.88f, 0.26f), new Vector3(2.2f, 0.45f, 0.06f), matWoodFloor, addCollider: false);
			CreateCylinder(transform, new Vector3(-0.9f, 0.22f, 0f), 0.035f, 0.45f, matDarkMetal, addCollider: false);
			CreateCylinder(transform, new Vector3(0.9f, 0.22f, 0f), 0.035f, 0.45f, matDarkMetal, addCollider: false);
		}

		private void BuildStreetLamp(Transform parent, Vector3 pos, bool worksNormally)
		{
			Transform transform = new GameObject("Street Lamp").transform;
			transform.SetParent(parent, worldPositionStays: false);
			transform.localPosition = pos;
			CreateCylinder(transform, new Vector3(0f, 3f, 0f), 0.09f, 6f, matDarkMetal);
			CreateBox(transform, new Vector3(0.42f, 5.8f, 0f), new Vector3(0.9f, 0.08f, 0.08f), matDarkMetal, addCollider: false);
			CreateBox(transform, new Vector3(0.85f, 5.65f, 0f), new Vector3(0.45f, 0.25f, 0.45f), matDarkMetal, addCollider: false);
			GameObject gameObject = CreateBox(transform, new Vector3(0.85f, 5.48f, 0f), new Vector3(0.35f, 0.06f, 0.35f), matWhite, addCollider: false);
			Light light = transform.gameObject.AddComponent<Light>();
			light.type = LightType.Point;
			light.color = new Color(1f, 0.9f, 0.68f);
			light.intensity = (worksNormally ? 3f : 1.4f);
			light.range = 15f;
			light.shadows = LightShadows.Soft;
			lamps.Add(new FlickeringLamp
			{
				light = light,
				bulb = gameObject.GetComponent<Renderer>(),
				baseIntensity = 3f,
				worksNormally = worksNormally,
				flickerOffset = UnityEngine.Random.value * 50f,
				isInterior = false
			});
		}

		private void BuildNewsVan()
		{
            BuildRefinedNewsVan();
        }

		public void Update(RoomState state, float dt)
		{
            UpdateSchoolDoorMotion(state, dt);
			if (state == null)
			{
				return;
			}
			float time = Time.time;
			if (time >= nextLightUpdate)
			{
				nextLightUpdate = time + 0.1f;
				if (viewCamera == null)
				{
					viewCamera = Camera.main;
                    if (viewCamera == null)
                    {
                        var player = UnityEngine.Object.FindAnyObjectByType<DeadAirPlayer>();
                        if (player != null) viewCamera = player.ViewCamera;
                    }
				}
				Vector3 vector = ((viewCamera != null) ? viewCamera.transform.position : Vector3.zero);
				bool interiorPowered = state != null && state.power && state.breakerOn;
				for (int i = 0; i < lamps.Count; i++)
				{
					FlickeringLamp flickeringLamp = lamps[i];
					if (flickeringLamp.light == null)
					{
						continue;
					}
					if (flickeringLamp.isInterior && !interiorPowered)
					{
						if (flickeringLamp.light.enabled)
						{
							flickeringLamp.light.enabled = false;
						}
						if (flickeringLamp.bulb != null && flickeringLamp.lastBulbOn)
						{
							lampPropertyBlock.SetColor(LampColorId, new Color(0.1f, 0.1f, 0.1f));
							flickeringLamp.bulb.SetPropertyBlock(lampPropertyBlock);
							flickeringLamp.lastBulbOn = false;
						}
						continue;
					}
					float num = flickeringLamp.light.range + 18f;
					bool flag = viewCamera == null || (flickeringLamp.light.transform.position - vector).sqrMagnitude < num * num;
					if (flickeringLamp.worksNormally)
					{
						if (flickeringLamp.light.enabled != flag)
						{
							flickeringLamp.light.enabled = flag;
						}
						if (flickeringLamp.bulb != null && !flickeringLamp.lastBulbOn && flag)
						{
							lampPropertyBlock.SetColor(LampColorId, Color.white);
							flickeringLamp.bulb.SetPropertyBlock(lampPropertyBlock);
							flickeringLamp.lastBulbOn = true;
						}
						continue;
					}
					float num2 = Mathf.Sin((time + flickeringLamp.flickerOffset) * 12.3f) * Mathf.Cos((time + flickeringLamp.flickerOffset) * 3.7f);
					bool flag2 = flag && num2 > -0.2f && Mathf.Sin((time + flickeringLamp.flickerOffset) * 1.8f) > -0.6f;
					if (flickeringLamp.light.enabled != flag2)
					{
						flickeringLamp.light.enabled = flag2;
					}
					if (flag2)
					{
						flickeringLamp.light.intensity = flickeringLamp.baseIntensity * UnityEngine.Random.Range(0.6f, 1.1f);
					}
					if (flickeringLamp.bulb != null && flag2 != flickeringLamp.lastBulbOn)
					{
						lampPropertyBlock.SetColor(LampColorId, flag2 ? Color.white : new Color(0.2f, 0.2f, 0.2f));
						flickeringLamp.bulb.SetPropertyBlock(lampPropertyBlock);
						flickeringLamp.lastBulbOn = flag2;
					}
				}
				if (state != null)
				{
					if (generatorIndicatorRenderer != null)
					{
						lampPropertyBlock.SetColor(LampColorId, state.power ? new Color(0.2f, 0.95f, 0.2f) : new Color(0.85f, 0.2f, 0.1f));
						generatorIndicatorRenderer.SetPropertyBlock(lampPropertyBlock);
					}
					if (breakerIndicatorRenderer != null)
					{
						Color bColor = (state.power && state.breakerOn) ? new Color(0.2f, 0.95f, 0.2f) : (state.power ? new Color(0.95f, 0.7f, 0.1f) : new Color(0.18f, 0.18f, 0.18f));
						lampPropertyBlock.SetColor(LampColorId, bColor);
						breakerIndicatorRenderer.SetPropertyBlock(lampPropertyBlock);
					}
					if (breakerHandleTransform != null)
					{
						breakerHandleTransform.localRotation = Quaternion.Euler(0f, 0f, state.breakerOn ? 35f : -35f);
					}
				}
			}
            artReflectionProbes?.Present(state, viewCamera != null ? viewCamera : Camera.main);
			for (int j = 0; j < pills.Count; j++)
			{
				PillItem pillItem = pills[j];
				if (!(pillItem.go == null))
				{
					bool flag3 = state.Collected(pillItem.id);
					if (flag3 != pillItem.collected)
					{
						pillItem.collected = flag3;
						pillItem.go.SetActive(!flag3);
					}
					if (!pillItem.collected)
					{
						float y = Mathf.Sin(time * 3f + (float)j) * 0.04f;
						pillItem.go.transform.localPosition = pillItem.position + new Vector3(0f, y, 0f);
						pillItem.go.transform.Rotate(0f, 45f * dt, 0f);
					}
				}
			}
			for (int k = 0; k < evidenceTapes.Count; k++)
			{
				EvidenceTapeItem evidenceTapeItem = evidenceTapes[k];
				if (!(evidenceTapeItem.go == null))
				{
					bool flag4 = state.Collected(evidenceTapeItem.id);
					if (flag4 != evidenceTapeItem.collected)
					{
						evidenceTapeItem.collected = flag4;
						evidenceTapeItem.go.SetActive(!flag4);
					}
					if (!evidenceTapeItem.collected)
					{
						float y2 = Mathf.Sin(time * 2.5f + (float)k) * 0.03f;
						evidenceTapeItem.go.transform.localPosition = evidenceTapeItem.position + new Vector3(0f, y2, 0f);
					}
				}
			}
		}
	}
}

