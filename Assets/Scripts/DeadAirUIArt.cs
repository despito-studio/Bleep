using System;
using System.Collections.Generic;
using UnityEngine;

namespace DeadAir
{
    /// <summary>Small, cached monochrome UI marks. Tint with Image.color; all distress is alpha.</summary>
    public static class DeadAirUIArt
    {
        static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();

        public static Sprite Sprite(string id)
        {
            id = string.IsNullOrEmpty(id) ? "frame" : id.ToLowerInvariant();
            if (Cache.TryGetValue(id, out var cached) && cached != null) return cached;
            var width = id == "bar" || id == "divider" ? 512 : id == "frame" || id == "button" ? 256 : id == "paper" || id == "grain" ? 256 : 128;
            var height = id == "bar" ? 32 : id == "divider" ? 12 : id == "frame" || id == "button" ? 128 : width;
            var art = new Mark(width, height);
            var border = Vector4.zero;
            var distress = .12f;
            switch (id)
            {
                case "eye": Eye(art); break;
                case "stamina": Stamina(art); break;
                case "tape": Tape(art); break;
                case "fuse": Fuse(art); break;
                case "flashlight": Flashlight(art); break;
                case "fuel": Fuel(art); break;
                case "cigarette-pack":
                    art.Path(4,30,35,98,35,98,112,30,112,30,35);
                    art.Path(3,30,55,98,55);art.Line(45,35,45,15,7);art.Line(60,35,60,9,7);art.Line(75,35,75,18,7);art.Line(89,35,89,12,7);
                    art.Line(42,77,86,77,4);art.Line(42,88,77,88,3);break;
                case "wrench":
                    art.Path(8,43,21,27,42,40,59,56,60,102,104);art.Path(8,67,23,72,44,58,57);art.Circle(102,104,12,5);break;
                case "recorder":
                    art.Path(4,37,16,91,16,91,113,37,113,37,16);art.Path(3,47,29,81,29,81,49,47,49,47,29);
                    for(int row=0;row<3;row++)art.Line(48,63+row*8,80,63+row*8,3);art.Circle(64,99,6,3);break;
                case "compass":
                    art.Circle(64,68,43,4);art.Circle(64,68,34,2);art.Path(4,52,24,52,13,76,13,76,24);
                    art.Polygon(78,40,69,75,48,94,58,61);break;
                case "key": Key(art); break;
                case "radio": Radio(art); break;
                case "chapter": Chapter(art); break;
                case "lock": Lock(art); break;
                case "arrow": art.Path(5.5f, 44, 25, 82, 64, 44, 103); break;
                case "close": art.Line(30, 30, 98, 98, 5); art.Line(98, 30, 30, 98, 5); break;
                case "cross":
                    art.Polygon(51, 15, 77, 15, 77, 50, 112, 50, 112, 77, 77, 77, 77, 112, 51, 112, 51, 77, 16, 77, 16, 50, 51, 50);
                    distress = .24f; break;
                case "bar":
                    Bar(art); distress = .14f; break;
                case "divider":
                    art.Line(3, 6, 509, 6, 1.55f);
                    art.Line(25, 8, 183, 6, .65f, .32f);
                    art.Line(336, 5, 497, 4, .65f, .42f);
                    distress = .16f; break;
                case "paper":
                    Paper(art, .94f); border = new Vector4(12, 12, 12, 12); distress = .05f; break;
                case "grain":
                    Grain(art); distress = 0; break;
                case "button":
                    Paper(art, .8f); border = new Vector4(12, 12, 12, 12); distress = .12f; break;
                default:
                    Frame(art); border = new Vector4(12, 12, 12, 12); distress = .16f; break;
            }
            var texture = art.Texture("Dead Air / UI / " + id, distress);
            var sprite = UnityEngine.Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect, border);
            sprite.name = "Dead Air / UI / " + id;
            sprite.hideFlags = HideFlags.HideAndDontSave;
            Cache[id] = sprite;
            return sprite;
        }

        static void Eye(Mark m)
        {
            m.Curve(20, 64, 43, 26, 85, 26, 108, 64, 4.3f);
            m.Curve(20, 64, 43, 102, 85, 102, 108, 64, 4.3f);
            m.Circle(64, 64, 20, 4.4f);
            m.Polygon(63, 48, 71, 62, 65, 81, 56, 65);
            m.Curve(11, 44, 5, 54, 5, 74, 11, 84, 3.7f);
            m.Curve(118, 44, 124, 54, 124, 74, 118, 84, 3.7f);
            m.Line(23, 32, 27, 40, 2.2f, .7f);
            m.Line(104, 87, 109, 96, 2, .6f);
        }

        static void Stamina(Mark m)
        {
            // Two shoe soles with a break at the heel, like chalk evidence marks.
            m.Polygon(35, 12, 45, 10, 53, 18, 58, 37, 56, 59, 49, 73, 38, 71, 31, 52, 29, 28);
            m.Polygon(38, 79, 50, 80, 51, 94, 44, 102, 36, 99, 32, 89);
            m.Polygon(80, 32, 92, 29, 100, 38, 102, 57, 96, 79, 86, 94, 75, 89, 72, 70, 73, 48);
            m.Polygon(73, 98, 85, 102, 82, 116, 73, 120, 65, 114, 65, 105);
            m.Line(34, 39, 50, 45, 2, 1, true);
            m.Line(77, 57, 95, 62, 2, 1, true);
            m.Line(79, 71, 92, 76, 1.5f, 1, true);
        }

        static void Tape(Mark m)
        {
            m.Path(4, 18, 26, 109, 26, 112, 96, 17, 96, 18, 26);
            m.Path(2.6f, 25, 33, 102, 33, 102, 69, 25, 69, 25, 33);
            m.Circle(42, 51, 10, 3.4f); m.Circle(86, 51, 10, 3.4f);
            m.Circle(42, 51, 3, 0); m.Circle(86, 51, 3, 0);
            m.Line(53, 51, 75, 51, 2.1f);
            m.Path(3, 32, 96, 39, 79, 87, 79, 96, 96);
            m.Circle(27, 85, 1.5f, 0); m.Circle(100, 85, 1.5f, 0);
        }

        static void Fuse(Mark m)
        {
            m.Path(3.5f, 47, 24, 81, 24, 81, 104, 47, 104, 47, 24);
            m.Box(42, 16, 44, 13); m.Box(42, 99, 44, 13);
            m.Path(2, 53, 34, 53, 91);
            m.Polygon(67, 36, 54, 67, 65, 65, 58, 91, 76, 56, 65, 58);
        }

        static void Flashlight(Mark m)
        {
            m.Path(4.3f, 35, 27, 93, 27, 93, 39, 80, 58, 80, 114, 48, 114, 48, 58, 35, 39, 35, 27);
            m.Line(36, 37, 92, 37, 3.2f);
            m.Line(49, 59, 79, 59, 3);
            m.Line(49, 105, 79, 105, 3.1f);
            m.Box(60, 68, 8, 14);
            m.Line(55, 90, 55, 99, 1.7f, .65f);
            m.Line(64, 90, 64, 99, 1.7f, .65f);
            m.Line(73, 90, 73, 99, 1.7f, .65f);
            m.Line(64, 16, 64, 4, 3);
            m.Line(39, 18, 30, 9, 3);
            m.Line(89, 18, 98, 9, 3);
        }

        static void Fuel(Mark m)
        {
            m.Path(4.3f, 30, 41, 88, 41, 102, 54, 102, 113, 25, 113, 25, 52, 30, 41);
            m.Path(4, 43, 40, 47, 18, 77, 18, 88, 40);
            m.Path(3.1f, 52, 35, 54, 26, 72, 26, 76, 35);
            m.Path(4, 28, 42, 22, 28, 36, 22, 43, 40);
            m.Line(22, 27, 37, 21, 5.5f);
            m.Path(2.5f, 38, 58, 85, 58, 88, 101, 38, 101, 38, 58);
            m.Path(3.2f, 44, 64, 63, 80, 81, 95);
            m.Path(3.2f, 80, 64, 63, 80, 44, 95);
            m.Line(31, 107, 94, 107, 1.8f, .55f);
        }

        static void Key(Mark m)
        {
            m.Circle(41, 39, 24, 5); m.Circle(41, 39, 9, 3);
            m.Line(57, 57, 105, 106, 7);
            m.Path(6, 83, 84, 74, 94, 83, 103);
            m.Path(6, 97, 98, 89, 107, 97, 115);
        }

        static void Radio(Mark m)
        {
            m.Line(37, 33, 24, 9, 4); m.Circle(23, 8, 3, 0);
            m.Path(4, 27, 34, 102, 34, 108, 106, 21, 106, 27, 34);
            m.Path(3, 35, 43, 91, 43, 93, 60, 34, 60, 35, 43);
            m.Line(44, 50, 80, 50, 2);
            m.Circle(87, 84, 10, 3.8f); m.Circle(87, 84, 3, 0);
            for (var y = 73; y <= 95; y += 7) m.Line(34, y, 67, y, 2.5f);
            m.Line(42, 26, 66, 26, 4); m.Line(77, 26, 88, 26, 4);
        }

        static void Chapter(Mark m)
        {
            m.Path(4, 17, 23, 44, 25, 64, 37, 84, 25, 112, 23, 112, 94, 84, 96, 64, 108, 44, 96, 17, 94, 17, 23);
            m.Line(64, 38, 64, 104, 3.7f);
            m.Path(2, 27, 39, 43, 42, 52, 48);
            m.Path(2, 27, 54, 43, 57, 52, 63);
            m.Path(2, 27, 69, 43, 72, 52, 78);
            m.Path(2, 76, 47, 87, 41, 101, 38);
            m.Path(2, 76, 62, 87, 56, 101, 53);
            m.Path(2, 76, 77, 87, 71, 101, 68);
        }

        static void Lock(Mark m)
        {
            m.Curve(39, 52, 36, 4, 92, 4, 89, 52, 5);
            m.Path(4, 27, 51, 102, 51, 102, 109, 27, 109, 27, 51);
            m.Circle(64, 75, 7, 0); m.Polygon(61, 78, 67, 78, 71, 96, 57, 96);
            m.Line(33, 102, 94, 102, 1.5f, .45f);
        }

        static void Frame(Mark m)
        {
            var w = m.Width; var h = m.Height;
            m.Path(1.9f, 7, 7, w - 7, 7, w - 7, h - 7, 7, h - 7, 7, 7);
            m.Line(11, 10, 54, 9, .8f, .35f);
            m.Line(w - 10, h - 10, w - 53, h - 9, .8f, .4f);
            m.Line(4, 3, 20, 4, 1.6f, .6f);
            m.Line(w - 4, h - 3, w - 20, h - 4, 1.5f, .5f);
        }

        static void Bar(Mark m)
        {
            for (var x = 3; x < m.Width - 3; x++)
            {
                var top = 3 + Mathf.RoundToInt(1.2f * Mathf.Sin(x * .14f) + Mark.Noise(x / 3, 23) * 1.5f);
                var bottom = m.Height - 4 + Mathf.RoundToInt(Mathf.Sin(x * .11f) - Mark.Noise(x / 4, 53));
                m.Line(x, top, x, bottom, 1.1f);
            }
            // Fine irregular fractures; fillAmount still crops this as a continuous meter.
            for (var x = 58; x < 500; x += 71)
            {
                m.PathErase(1.5f, x, 3, x - 5, 9, x + 2, 16, x - 2, 22, x + 2, 29);
                m.PathErase(.8f, x + 1, 16, x + 14, 12, x + 20, 12);
            }
        }

        static void Paper(Mark m, float opacity)
        {
            for (var y = 3; y < m.Height - 3; y++)
            {
                var inset = 3 + Mathf.FloorToInt(Mark.Noise(y / 4, 39) * 3);
                m.Line(inset, y, m.Width - inset, y, 1.2f, opacity);
            }
            for (var i = 0; i < 23; i++)
            {
                var x = 5 + Mark.Noise(i, 351) * (m.Width - 10);
                var y = 4 + Mark.Noise(i, 883) * (m.Height - 8);
                m.Line(x, y, x + 7 + Mark.Noise(i, 593) * 24, y + 2, .8f, .25f, true);
            }
        }

        static void Grain(Mark m)
        {
            for (var y = 0; y < m.Height; y++)
                for (var x = 0; x < m.Width; x++)
                {
                    var n = Mark.Noise(x, y);
                    if (n > .84f) m.Put(x, y, (n - .84f) * 1.4f);
                }
            for (var i = 0; i < 7; i++)
            {
                var x = Mark.Noise(i, 900) * 256;
                var y = Mark.Noise(i, 967) * 256;
                m.Line(x, y, x + 18 + 70 * Mark.Noise(i, 93), y + 1, .6f, .08f);
            }
        }

        sealed class Mark
        {
            public readonly int Width, Height;
            readonly float[] alpha;
            public Mark(int width, int height) { Width = width; Height = height; alpha = new float[width * height]; }

            public static float Noise(int x, int y)
            {
                unchecked
                {
                    var n = (uint)(x * 374761393 + y * 668265263 + 486187739);
                    n = (n ^ (n >> 13)) * 1274126177;
                    return ((n ^ (n >> 16)) & 65535) / 65535f;
                }
            }

            public void Put(int x, int y, float a, bool erase = false)
            {
                if (x < 0 || y < 0 || x >= Width || y >= Height) return;
                var at = x + (Height - 1 - y) * Width;
                if (erase) alpha[at] *= 1 - Mathf.Clamp01(a);
                else alpha[at] = Mathf.Max(alpha[at], Mathf.Clamp01(a));
            }

            public void Line(float ax, float ay, float bx, float by, float thickness, float opacity = 1, bool erase = false)
            {
                var pad = thickness * .5f + 1;
                var dx = bx - ax; var dy = by - ay;
                var len = dx * dx + dy * dy;
                var minX = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(ax, bx) - pad));
                var maxX = Mathf.Min(Width - 1, Mathf.CeilToInt(Mathf.Max(ax, bx) + pad));
                var minY = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(ay, by) - pad));
                var maxY = Mathf.Min(Height - 1, Mathf.CeilToInt(Mathf.Max(ay, by) + pad));
                for (var y = minY; y <= maxY; y++) for (var x = minX; x <= maxX; x++)
                {
                    var t = len < .0001f ? 0 : Mathf.Clamp01(((x - ax) * dx + (y - ay) * dy) / len);
                    var ex = x - ax - t * dx; var ey = y - ay - t * dy;
                    var coverage = Mathf.Clamp01(thickness * .5f + .7f - Mathf.Sqrt(ex * ex + ey * ey));
                    Put(x, y, coverage * opacity, erase);
                }
            }

            public void Path(float thickness, params float[] xy)
            { for (var i = 2; i < xy.Length; i += 2) Line(xy[i - 2], xy[i - 1], xy[i], xy[i + 1], thickness); }

            public void PathErase(float thickness, params float[] xy)
            { for (var i = 2; i < xy.Length; i += 2) Line(xy[i - 2], xy[i - 1], xy[i], xy[i + 1], thickness, 1, true); }

            public void Circle(float cx, float cy, float radius, float thickness)
            {
                var pad = radius + thickness + 1;
                for (var y = Mathf.Max(0, Mathf.FloorToInt(cy - pad)); y <= Mathf.Min(Height - 1, Mathf.CeilToInt(cy + pad)); y++)
                    for (var x = Mathf.Max(0, Mathf.FloorToInt(cx - pad)); x <= Mathf.Min(Width - 1, Mathf.CeilToInt(cx + pad)); x++)
                    {
                        var distance = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                        Put(x, y, thickness <= 0 ? Mathf.Clamp01(radius + .7f - distance) : Mathf.Clamp01(thickness * .5f + .7f - Mathf.Abs(radius - distance)));
                    }
            }

            public void Box(float x, float y, float width, float height)
            { Polygon(x, y, x + width, y, x + width, y + height, x, y + height); }

            public void Polygon(params float[] points)
            {
                var minX = Width - 1f; var maxX = 0f; var minY = Height - 1f; var maxY = 0f;
                for (var p = 0; p < points.Length; p += 2)
                { minX = Mathf.Min(minX, points[p]); maxX = Mathf.Max(maxX, points[p]); minY = Mathf.Min(minY, points[p + 1]); maxY = Mathf.Max(maxY, points[p + 1]); }
                for (var y = Mathf.Max(0, Mathf.FloorToInt(minY)); y <= Mathf.Min(Height - 1, Mathf.CeilToInt(maxY)); y++)
                    for (var x = Mathf.Max(0, Mathf.FloorToInt(minX)); x <= Mathf.Min(Width - 1, Mathf.CeilToInt(maxX)); x++)
                    {
                        var inside = false;
                        for (int i = 0, j = points.Length - 2; i < points.Length; j = i, i += 2)
                            if ((points[i + 1] > y) != (points[j + 1] > y) && x < (points[j] - points[i]) * (y - points[i + 1]) / (points[j + 1] - points[i + 1]) + points[i]) inside = !inside;
                        if (inside) Put(x, y, 1);
                    }
                for (int i = 0, j = points.Length - 2; i < points.Length; j = i, i += 2)
                    Line(points[j], points[j + 1], points[i], points[i + 1], .9f);
            }

            public void Curve(float ax, float ay, float bx, float by, float cx, float cy, float dx, float dy, float thickness)
            {
                var px = ax; var py = ay;
                for (var i = 1; i <= 28; i++)
                {
                    var t = i / 28f; var u = 1 - t;
                    var x = u * u * u * ax + 3 * u * u * t * bx + 3 * u * t * t * cx + t * t * t * dx;
                    var y = u * u * u * ay + 3 * u * u * t * by + 3 * u * t * t * cy + t * t * t * dy;
                    Line(px, py, x, y, thickness); px = x; py = y;
                }
            }

            public Texture2D Texture(string name, float distress)
            {
                var pixels = new Color32[alpha.Length];
                for (var y = 0; y < Height; y++) for (var x = 0; x < Width; x++)
                {
                    var at = x + y * Width;
                    var n = Noise(x, y);
                    var grain = 1 - distress * (.2f + .8f * n);
                    // A few paper-sized pores rather than TV static over every stroke.
                    if (distress > .08f && Noise(x / 2, y / 2) > .979f) grain *= .26f;
                    if (distress > .08f && n > .985f) grain *= .35f;
                    pixels[at] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha[at] * grain) * 255));
                }
                var texture = new Texture2D(Width, Height, TextureFormat.RGBA32, false)
                {
                    name = name,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.HideAndDontSave
                };
                texture.SetPixels32(pixels); texture.Apply(false, true);
                return texture;
            }
        }
    }
}
