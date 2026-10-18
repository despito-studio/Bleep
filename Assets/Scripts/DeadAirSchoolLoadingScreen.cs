using UnityEngine;
using UnityEngine.UI;

namespace DeadAir
{
	internal sealed class DeadAirSchoolLoadingScreen
	{
		private readonly GameObject root;

		private readonly RectTransform progressFill;

		private readonly Text detail;

		private const float BarWidth = 600f;

		public DeadAirSchoolLoadingScreen()
		{
			root = new GameObject("Chapter 2 loading screen", typeof(RectTransform));
			Canvas canvas = root.AddComponent<Canvas>();
			canvas.renderMode = RenderMode.ScreenSpaceOverlay;
			canvas.sortingOrder = 32000;
			CanvasScaler canvasScaler = root.AddComponent<CanvasScaler>();
			canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
			canvasScaler.referenceResolution = new Vector2(1920f, 1080f);
			root.AddComponent<GraphicRaycaster>();
			RectTransform rectTransform = Child(root.transform, "Black loading curtain");
			rectTransform.anchorMin = Vector2.zero;
			rectTransform.anchorMax = Vector2.one;
			rectTransform.offsetMin = (rectTransform.offsetMax = Vector2.zero);
			rectTransform.gameObject.AddComponent<Image>().color = new Color(0.025f, 0.035f, 0.055f, 1f);
			Font font = Resources.Load<Font>("Fonts/IBMPlexMono-Regular") ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
			Label(rectTransform, "School loading title", font, "ЯПОНСЬКА ШКОЛА", 44, 84f, new Color(0.93f, 0.95f, 0.94f)).fontStyle = FontStyle.Bold;
			detail = Label(rectTransform, "School loading detail", font, "Завантаження світу...", 24, 18f, new Color(0.75f, 0.82f, 0.88f));
			RectTransform rectTransform2 = Child(rectTransform, "Loading track");
			rectTransform2.anchorMin = (rectTransform2.anchorMax = new Vector2(0.5f, 0.5f));
			rectTransform2.pivot = new Vector2(0.5f, 0.5f);
			rectTransform2.anchoredPosition = new Vector2(0f, -38f);
			rectTransform2.sizeDelta = new Vector2(600f, 12f);
			rectTransform2.gameObject.AddComponent<Image>().color = new Color(0.16f, 0.22f, 0.29f);
			progressFill = Child(rectTransform2, "Loading progress");
			progressFill.anchorMin = new Vector2(0f, 0f);
			progressFill.anchorMax = new Vector2(0f, 1f);
			progressFill.pivot = new Vector2(0f, 0.5f);
			progressFill.anchoredPosition = Vector2.zero;
			progressFill.sizeDelta = new Vector2(0f, 0f);
			progressFill.gameObject.AddComponent<Image>().color = new Color(0.42f, 0.78f, 0.67f);
			root.SetActive(value: false);
		}

		private static RectTransform Child(Transform parent, string name)
		{
			RectTransform component = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
			component.SetParent(parent, worldPositionStays: false);
			return component;
		}

		private static Text Label(Transform parent, string name, Font font, string value, int size, float height, Color color)
		{
			RectTransform rectTransform = Child(parent, name);
			Vector2 anchorMin = (rectTransform.anchorMax = new Vector2(0.5f, 0.5f));
			rectTransform.anchorMin = anchorMin;
			rectTransform.pivot = new Vector2(0.5f, 0.5f);
			rectTransform.anchoredPosition = new Vector2(0f, height);
			rectTransform.sizeDelta = new Vector2(1100f, 72f);
			Text text = rectTransform.gameObject.AddComponent<Text>();
			text.font = font;
			text.fontSize = size;
			text.color = color;
			text.text = value;
			text.alignment = TextAnchor.MiddleCenter;
			text.raycastTarget = false;
			return text;
		}

		public void SetVisible(bool visible)
		{
			if (root != null && root.activeSelf != visible)
			{
				root.SetActive(visible);
			}
		}

		public void SetProgress(int steps, int count)
		{
			progressFill.sizeDelta = new Vector2(600f * Mathf.Clamp01((float)steps / (float)Mathf.Max(1, count)), 0f);
		}

		public void SetFailed()
		{
			detail.text = "Не вдалося завантажити школу. Перевірте лог гри.";
			detail.color = new Color(0.88f, 0.29f, 0.25f);
		}

		public void Dispose()
		{
			if (root != null)
			{
				Object.Destroy(root);
			}
		}
	}
}

