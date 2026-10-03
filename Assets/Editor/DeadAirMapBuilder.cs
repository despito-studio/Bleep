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
        enum ItemKind { Props, Objectives, Doors, Lockers }
        static readonly string[] KindLabels = { "Меблі", "Цілі", "Двері", "Шафи" };
        WorldData data;
        Vector2 scroll;
        int selected = -1, selectedFloor;
        ItemKind kind;
        string filter = "";
        float[] floors = { 0 };
        string[] floorLabels = { "1 поверх" };
        GUIStyle roomLabelStyle;
        bool IsOffice => data != null && data.stairs != null && data.stairs.Length > 0 && floors.Length > 1;

        [MenuItem("Dead Air/Map Builder")]
        public static void ShowWindow()
        {
            var window = GetWindow<DeadAirMapBuilder>("Blackpine Map Builder");
            window.minSize = new Vector2(810, 660);
            window.Load();
        }

        void OnEnable() { Load(); }

        void Load()
        {
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(JsonPath);
            data = asset != null ? JsonUtility.FromJson<WorldData>(asset.text) : null;
            if (data != null)
            {
                data.props = data.props ?? Array.Empty<PropData>();
                data.objects = data.objects ?? Array.Empty<ObjectiveData>();
                data.doors = data.doors ?? Array.Empty<DoorData>();
                data.lockers = data.lockers ?? Array.Empty<LockerData>();
                data.rooms = data.rooms ?? Array.Empty<RoomData>();
                floors = data.rooms.Select(room => Mathf.Round(room.floorY * 100) / 100)
                    .Distinct().OrderBy(y => y).DefaultIfEmpty(0).ToArray();
                floorLabels = floors.Select((y, i) => $"{i + 1} поверх · Y {y:0.##}").ToArray();
                selectedFloor = Mathf.Clamp(selectedFloor, 0, floors.Length - 1);
                foreach (var prop in data.props)
                {
                    var locker = data.lockers.FirstOrDefault(item => item.id == prop.id);
                    if (locker == null) continue;
                    prop.exitX = locker.exitX; prop.exitZ = locker.exitZ;
                }
            }
            selected = -1;
            Repaint();
        }

        void Save()
        {
            if (data == null) return;
            SynchronizeInteractivePlacements(data);
            File.WriteAllText(JsonPath, JsonUtility.ToJson(data, true));
            AssetDatabase.ImportAsset(JsonPath, ImportAssetOptions.ForceUpdate);
            if (IsOffice) DeadAirOfficeBuilder.RebuildFromSavedData();
            EditorUtility.DisplayDialog("Blackpine", IsOffice
                ? "Розташування збережено, геометрію офісу оновлено. Перезапустіть сцену."
                : "Мапу збережено. Запустіть сцену повторно, щоб побачити зміни.", "Гаразд");
        }

        public static void SynchronizeInteractivePlacements(WorldData world)
        {
            if (world == null || world.props == null) return;
            foreach (var prop in world.props)
            {
                var objective = world.objects?.FirstOrDefault(item => item.id == prop.id);
                if (objective != null)
                {
                    objective.x = prop.x; objective.y = prop.y; objective.z = prop.z;
                    objective.label = prop.label;
                }
                var locker = world.lockers?.FirstOrDefault(item => item.id == prop.id);
                if (locker == null) continue;
                locker.x = prop.x; locker.y = prop.y; locker.z = prop.z;
                locker.ry = prop.rotation; locker.label = prop.label;
                locker.exitX = prop.exitX; locker.exitY = prop.y; locker.exitZ = prop.exitZ;
            }
            var exit = world.objects?.FirstOrDefault(item => item.id == "exit");
            if (exit != null)
            {
                world.extraction = world.extraction ?? new PointData();
                world.extraction.x = exit.x; world.extraction.y = exit.y; world.extraction.z = exit.z;
            }
        }

        void OnGUI()
        {
            if (data == null) { EditorGUILayout.HelpBox("Не знайдено Assets/Resources/world.json", MessageType.Error); return; }
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("BLACKPINE / MAP BUILDER", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Reload", EditorStyles.toolbarButton)) Load();
                using (new EditorGUI.DisabledScope(EditorApplication.isPlaying))
                    if (GUILayout.Button(IsOffice ? "Save + Rebuild" : "Save", EditorStyles.toolbarButton)) Save();
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                int floor = EditorGUILayout.Popup("Поверх", selectedFloor, floorLabels);
                if (floor != selectedFloor) { selectedFloor = floor; selected = -1; }
                var nextKind = (ItemKind)GUILayout.Toolbar((int)kind, KindLabels);
                if (nextKind != kind) { kind = nextKind; selected = -1; }
            }
            if (IsOffice)
                EditorGUILayout.HelpBox("Save + Rebuild зберігає розташування й перебудовує офіс. "
                    + "Дверні отвори та кімнати задає план будівлі; двері й шафи переглядайте у відповідних вкладках.", MessageType.Info);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(255)))
                {
                    filter = EditorGUILayout.TextField("Пошук", filter);
                    scroll = EditorGUILayout.BeginScrollView(scroll);
                    for (int i = 0; i < Count(kind); i++)
                    {
                        if (!OnFloor(Position(kind, i).y) || !MatchesFilter(kind, i)) continue;
                        if (GUILayout.Toggle(selected == i, Id(kind, i) + "  /  " + Label(kind, i), "Button")) selected = i;
                    }
                    EditorGUILayout.EndScrollView();
                    using (new EditorGUI.DisabledScope(kind != ItemKind.Props || EditorApplication.isPlaying))
                        if (GUILayout.Button("+ Додати предмет"))
                        {
                            var list = data.props.ToList();
                            list.Add(new PropData { id = "prop-new-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                                type = "crate", label = "Новий предмет", x = -10, y = floors[selectedFloor], z = -3, size = .9f });
                            data.props = list.ToArray(); selected = list.Count - 1;
                        }
                }
                using (new EditorGUILayout.VerticalScope())
                {
                    DrawMap();
                    EditorGUILayout.LabelField("Меблі ●   Цілі ◆   Двері ━   Шафи ■", EditorStyles.miniLabel);
                    if (selected < 0 || selected >= Count(kind) || !OnFloor(Position(kind, selected).y)) return;
                    using (new EditorGUI.DisabledScope(EditorApplication.isPlaying)) DrawSelection();
                }
            }
        }

        void DrawSelection()
        {
            EditorGUILayout.LabelField(Label(kind, selected), EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(true)) EditorGUILayout.TextField("ID", Id(kind, selected));
            if (kind == ItemKind.Props)
            {
                var prop = data.props[selected];
                bool interactive = data.objects.Any(item => item.id == prop.id) || data.lockers.Any(item => item.id == prop.id);
                using (new EditorGUI.DisabledScope(IsOffice || interactive)) prop.type = EditorGUILayout.TextField("Тип", prop.type);
                prop.label = EditorGUILayout.TextField("Назва", prop.label);
                Vector3 old = new Vector3(prop.x, prop.y, prop.z);
                Vector3 position = EditorGUILayout.Vector3Field("Розташування", old);
                float rotation = EditorGUILayout.FloatField("Поворот (радіани)", prop.rotation);
                if (prop.type == "locker")
                {
                    float dx = prop.exitX - old.x, dz = prop.exitZ - old.z, angle = rotation - prop.rotation;
                    prop.exitX = position.x + dx * Mathf.Cos(angle) + dz * Mathf.Sin(angle);
                    prop.exitZ = position.z + dz * Mathf.Cos(angle) - dx * Mathf.Sin(angle);
                    prop.exitX = EditorGUILayout.FloatField("Вихід X", prop.exitX);
                    prop.exitZ = EditorGUILayout.FloatField("Вихід Z", prop.exitZ);
                }
                prop.x = position.x; prop.y = position.y; prop.z = position.z; prop.rotation = rotation;
                using (new EditorGUI.DisabledScope(IsOffice && prop.type == "locker"))
                {
                    prop.w = EditorGUILayout.FloatField("Ширина (план)", prop.w);
                    prop.d = EditorGUILayout.FloatField("Глибина (план)", prop.d);
                    prop.size = EditorGUILayout.FloatField("Розмір", prop.size);
                }
                using (new EditorGUI.DisabledScope(interactive))
                    if (GUILayout.Button("Видалити предмет"))
                    {
                        data.props = data.props.Where((_, i) => i != selected).ToArray(); selected = -1;
                    }
            }
            else if (kind == ItemKind.Objectives)
            {
                var item = data.objects[selected];
                item.label = EditorGUILayout.TextField("Назва", item.label);
                Vector3 position = EditorGUILayout.Vector3Field("Розташування", new Vector3(item.x, item.y, item.z));
                item.x = position.x; item.y = position.y; item.z = position.z;
                var prop = data.props.FirstOrDefault(p => p.id == item.id);
                if (prop != null) { prop.x = item.x; prop.y = item.y; prop.z = item.z; prop.label = item.label; }
            }
            else
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.Vector3Field("Розташування", Position(kind, selected));
                    if (kind == ItemKind.Lockers)
                    {
                        var locker = data.lockers[selected];
                        EditorGUILayout.Vector3Field("Вихід", new Vector3(locker.exitX, locker.ExitFloorY, locker.exitZ));
                    }
                }
                EditorGUILayout.HelpBox(kind == ItemKind.Lockers
                    ? "Редагуйте цю шафу у вкладці «Меблі». Вихід переміщується й повертається разом із шафою."
                    : "Розташування дверей прив’язане до отворів у стінах і редагується в плані будівлі.", MessageType.None);
            }
        }

        void DrawMap()
        {
            var area = GUILayoutUtility.GetRect(390, Mathf.Max(220, position.height - 420), GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(area, new Color(.055f, .085f, .08f));
            Rect bounds = MapBounds();
            float scale = Mathf.Min((area.width - 28) / bounds.width, (area.height - 28) / bounds.height);
            Vector2 Map(float x, float z) => area.center + new Vector2(x - bounds.center.x, z - bounds.center.y) * scale;
            if (roomLabelStyle == null)
            {
                roomLabelStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter, wordWrap = true };
                roomLabelStyle.normal.textColor = new Color(.76f, .83f, .78f);
            }
            Handles.BeginGUI();
            if (data.courtyard != null && OnFloor(0))
                DrawPlanRect(Map(data.courtyard.minX, data.courtyard.minZ), Map(data.courtyard.maxX, data.courtyard.maxZ), new Color(.25f, .39f, .27f));
            foreach (var room in data.rooms)
            {
                if (!OnFloor(room.floorY)) continue;
                Vector2 min = Map(room.minX, room.minZ), max = Map(room.maxX, room.maxZ);
                DrawPlanRect(min, max, new Color(.37f, .5f, .44f));
                GUI.Label(new Rect(min.x + 2, min.y + 2, max.x - min.x - 4, max.y - min.y - 4),
                    string.IsNullOrEmpty(room.mapLabel) ? room.name : room.mapLabel, roomLabelStyle);
            }
            if (data.stairs != null)
                foreach (var stair in data.stairs)
                {
                    if (floors[selectedFloor] < stair.lowerY - .1f || floors[selectedFloor] > stair.upperY + .1f) continue;
                    for (int step = 0; step <= stair.steps; step++)
                    {
                        float z = Mathf.Lerp(stair.minZ, stair.maxZ, step / (float)Mathf.Max(1, stair.steps));
                        Handles.color = new Color(.7f, .73f, .57f);
                        Handles.DrawLine(Map(stair.minX, z), Map(stair.maxX, z));
                    }
                }
            foreach (ItemKind itemKind in Enum.GetValues(typeof(ItemKind)))
                for (int i = 0; i < Count(itemKind); i++)
                {
                    Vector3 position = Position(itemKind, i);
                    if (!OnFloor(position.y)) continue;
                    Vector2 p = Map(position.x, position.z);
                    bool picked = itemKind == kind && i == selected;
                    Handles.color = picked ? Color.yellow : ItemColor(itemKind);
                    float radius = picked ? 5 : 2.7f;
                    if (itemKind == ItemKind.Doors)
                    {
                        var door = data.doors[i];
                        Vector2 extent = new Vector2(Mathf.Sin(door.rotation), Mathf.Cos(door.rotation)) * Mathf.Max(.5f, door.w) * scale * .5f;
                        Handles.DrawAAPolyLine(3, p - extent, p + extent);
                    }
                    else if (itemKind == ItemKind.Props) Handles.DrawSolidDisc(p, Vector3.forward, radius);
                    else
                    {
                        Vector2 a = itemKind == ItemKind.Objectives ? new Vector2(0, radius) : new Vector2(radius, radius);
                        Vector2 b = itemKind == ItemKind.Objectives ? new Vector2(radius, 0) : new Vector2(radius, -radius);
                        Handles.DrawSolidRectangleWithOutline(new Vector3[] { p + a, p + b, p - a, p - b }, Handles.color, Handles.color);
                    }
                }
            Handles.EndGUI();
            if (Event.current.type != EventType.MouseDown || Event.current.button != 0 || !area.Contains(Event.current.mousePosition)) return;
            float closest = 12;
            for (int i = 0; i < Count(kind); i++)
            {
                Vector3 position = Position(kind, i);
                if (!OnFloor(position.y) || !MatchesFilter(kind, i)) continue;
                float distance = Vector2.Distance(Map(position.x, position.z), Event.current.mousePosition);
                if (distance < closest) { selected = i; closest = distance; }
            }
            Event.current.Use(); Repaint();
        }

        Rect MapBounds()
        {
            float minX = float.PositiveInfinity, maxX = float.NegativeInfinity, minZ = float.PositiveInfinity, maxZ = float.NegativeInfinity;
            void Include(float x, float z) { minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x); minZ = Mathf.Min(minZ, z); maxZ = Mathf.Max(maxZ, z); }
            foreach (var room in data.rooms)
                if (OnFloor(room.floorY)) { Include(room.minX, room.minZ); Include(room.maxX, room.maxZ); }
            if (data.courtyard != null && OnFloor(0))
            { Include(data.courtyard.minX, data.courtyard.minZ); Include(data.courtyard.maxX, data.courtyard.maxZ); }
            foreach (ItemKind itemKind in Enum.GetValues(typeof(ItemKind)))
                for (int i = 0; i < Count(itemKind); i++)
                { var position = Position(itemKind, i); if (OnFloor(position.y)) Include(position.x, position.z); }
            if (float.IsInfinity(minX)) return new Rect(-20, -60, 40, 90);
            return Rect.MinMaxRect(minX - 2, minZ - 2, Mathf.Max(minX + 2, maxX) + 2, Mathf.Max(minZ + 2, maxZ) + 2);
        }

        static void DrawPlanRect(Vector2 min, Vector2 max, Color color)
        {
            Handles.color = color;
            Handles.DrawAAPolyLine(1.5f, min, new Vector2(max.x, min.y), max, new Vector2(min.x, max.y), min);
        }
        bool OnFloor(float y)
        {
            // Objective/door Y includes mounting height. A ceiling object at
            // Y 3.5 still belongs to floor 0, rather than the nearer floor 4.2.
            int floor = 0;
            for (int i = 1; i < floors.Length; i++) if (y >= floors[i] - .01f) floor = i;
            return floor == selectedFloor;
        }
        int Count(ItemKind itemKind) => itemKind == ItemKind.Props ? data.props.Length : itemKind == ItemKind.Objectives ? data.objects.Length : itemKind == ItemKind.Doors ? data.doors.Length : data.lockers.Length;
        string Id(ItemKind itemKind, int i) => itemKind == ItemKind.Props ? data.props[i].id : itemKind == ItemKind.Objectives ? data.objects[i].id : itemKind == ItemKind.Doors ? data.doors[i].id : data.lockers[i].id;
        string Label(ItemKind itemKind, int i) => itemKind == ItemKind.Props ? data.props[i].label : itemKind == ItemKind.Objectives ? data.objects[i].label : itemKind == ItemKind.Doors ? data.doors[i].label : data.lockers[i].label;
        bool MatchesFilter(ItemKind itemKind, int i) => string.IsNullOrWhiteSpace(filter) || (Id(itemKind, i) + " " + Label(itemKind, i)
            + (itemKind == ItemKind.Props ? " " + data.props[i].type : "")).IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
        Vector3 Position(ItemKind itemKind, int i)
        {
            if (itemKind == ItemKind.Props) { var item = data.props[i]; return new Vector3(item.x, item.y, item.z); }
            if (itemKind == ItemKind.Objectives) { var item = data.objects[i]; return new Vector3(item.x, item.y, item.z); }
            if (itemKind == ItemKind.Doors) { var item = data.doors[i]; return new Vector3(item.x, item.y, item.z); }
            var locker = data.lockers[i]; return new Vector3(locker.x, locker.y, locker.z);
        }
        static Color ItemColor(ItemKind itemKind) => itemKind == ItemKind.Props ? new Color(.74f, .49f, .24f)
            : itemKind == ItemKind.Objectives ? new Color(.32f, .83f, .89f) : itemKind == ItemKind.Doors ? new Color(.9f, .79f, .44f) : new Color(.73f, .47f, .83f);
    }
}
