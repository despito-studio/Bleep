using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace DeadAir.Editor
{
    public sealed class DeadAirMapBuilder : EditorWindow
    {
        const string JsonPath = "Assets/Resources/world.json";
        WorldData data;
        Vector2 scroll;
        int selected = -1;
        string filter = "";

        [MenuItem("Dead Air/Map Builder")]
        public static void ShowWindow()
        {
            var window = GetWindow<DeadAirMapBuilder>("Blackpine Map Builder");
            window.minSize = new Vector2(750, 520);
            window.Load();
        }

        void OnEnable() { Load(); }

        void Load()
        {
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(JsonPath);
            if (asset != null) data = JsonUtility.FromJson<WorldData>(asset.text);
            Repaint();
        }

        void Save()
        {
            if (data == null) return;
            File.WriteAllText(JsonPath, JsonUtility.ToJson(data, true));
            AssetDatabase.ImportAsset(JsonPath, ImportAssetOptions.ForceUpdate);
            EditorUtility.DisplayDialog("Blackpine", "Мапу збережено. Запустіть сцену повторно, щоб побачити зміни.", "Гаразд");
        }

        void OnGUI()
        {
            if (data == null) { EditorGUILayout.HelpBox("Не знайдено Assets/Resources/world.json", MessageType.Error); return; }
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("BLACKPINE / MAP BUILDER", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Reload", EditorStyles.toolbarButton)) Load();
                if (GUILayout.Button("Save", EditorStyles.toolbarButton)) Save();
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(270)))
                {
                    filter = EditorGUILayout.TextField("Пошук", filter);
                    scroll = EditorGUILayout.BeginScrollView(scroll);
                    for (int i = 0; i < data.props.Length; i++)
                    {
                        var prop = data.props[i];
                        if (!string.IsNullOrWhiteSpace(filter) &&
                            (prop.id + " " + prop.type + " " + prop.label).IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                        if (GUILayout.Toggle(selected == i, prop.id + "  /  " + prop.type, "Button")) selected = i;
                    }
                    EditorGUILayout.EndScrollView();
                    if (GUILayout.Button("+ Додати об’єкт"))
                    {
                        var list = data.props.ToList();
                        list.Add(new PropData { id = "prop-new-" + list.Count, type = "crate", label = "Новий об’єкт", x = 0, z = 0, size = .9f });
                        data.props = list.ToArray(); selected = list.Count - 1;
                    }
                }
                using (new EditorGUILayout.VerticalScope())
                {
                    DrawMap();
                    if (selected < 0 || selected >= data.props.Length) return;
                    var prop = data.props[selected];
                    EditorGUILayout.LabelField("ВИБРАНИЙ ОБ’ЄКТ", EditorStyles.boldLabel);
                    prop.id = EditorGUILayout.TextField("ID", prop.id);
                    prop.type = EditorGUILayout.TextField("Тип", prop.type);
                    prop.label = EditorGUILayout.TextField("Назва", prop.label);
                    prop.x = EditorGUILayout.FloatField("X", prop.x);
                    prop.y = EditorGUILayout.FloatField("Y", prop.y);
                    prop.z = EditorGUILayout.FloatField("Z", prop.z);
                    prop.rotation = EditorGUILayout.FloatField("Поворот (радіани)", prop.rotation);
                    prop.w = EditorGUILayout.FloatField("Ширина", prop.w);
                    prop.d = EditorGUILayout.FloatField("Глибина", prop.d);
                    prop.size = EditorGUILayout.FloatField("Розмір", prop.size);
                    if (prop.type == "locker")
                    {
                        prop.exitX = EditorGUILayout.FloatField("Вихід X", prop.exitX);
                        prop.exitZ = EditorGUILayout.FloatField("Вихід Z", prop.exitZ);
                    }
                    if (GUILayout.Button("Видалити вибраний об’єкт"))
                    {
                        data.props = data.props.Where((_, i) => i != selected).ToArray();
                        selected = -1;
                    }
                }
            }
        }

        void DrawMap()
        {
            var area = GUILayoutUtility.GetRect(390, 300, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(area, new Color(.08f, .13f, .12f));
            float scale = Mathf.Min(area.width / 50f, area.height / 96f);
            Vector2 Map(float x, float z) => new Vector2(area.center.x + x * scale, area.yMax - (z + 56) * scale);
            Handles.BeginGUI();
            Handles.color = new Color(.5f, .62f, .55f);
            Vector2 a = Map(-12, -54), b = Map(12, 6);
            Handles.DrawAAPolyLine(2, new Vector3(a.x, a.y), new Vector3(a.x, b.y), new Vector3(b.x, b.y), new Vector3(b.x, a.y), new Vector3(a.x, a.y));
            foreach (var prop in data.props)
            {
                var p = Map(prop.x, prop.z);
                Handles.color = prop == (selected >= 0 && selected < data.props.Length ? data.props[selected] : null) ? Color.yellow : new Color(.72f, .47f, .23f);
                bool picked = selected >= 0 && selected < data.props.Length && prop == data.props[selected];
                Handles.DrawSolidDisc(p, Vector3.forward, picked ? 4 : 2);
            }
            Handles.EndGUI();
            if (Event.current.type == EventType.MouseDown && area.Contains(Event.current.mousePosition))
            {
                float closest = 12;
                for (int i = 0; i < data.props.Length; i++)
                {
                    var p = Map(data.props[i].x, data.props[i].z);
                    float distance = Vector2.Distance(p, Event.current.mousePosition);
                    if (distance < closest) { selected = i; closest = distance; }
                }
                Event.current.Use(); Repaint();
            }
        }
    }
}
