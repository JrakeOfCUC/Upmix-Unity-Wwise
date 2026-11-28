using UnityEngine;

public sealed partial class SpatialStemPlayer
{
    private sealed class SourceMap
    {
        private GUIStyle mapText;
        private GUIStyle mapMarker;
        private string[] markerNumbers;
        public string[] Legend { get; private set; }

        public void Refresh(Stem[] stems)
        {
            markerNumbers = new string[stems.Length];
            Legend = new string[stems.Length];
            for (int i = 0; i < stems.Length; i++)
            {
                markerNumbers[i] = (i + 1).ToString();
                Legend[i] = markerNumbers[i] + "  " + stems[i].label;
            }
        }

        private static void MapRect(Rect rect, Color tint)
        {
            GUI.color = tint;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        public void Draw(Stem[] stems, float minimapRadius, bool hallPreset)
        {
            if (Event.current.type != EventType.Repaint || Legend == null) return;
            Camera camera = Camera.main;
            if (camera == null) return;
            if (mapText == null)
            {
                mapText = new GUIStyle(GUI.skin.label) { fontSize = 12, padding = new RectOffset() };
                mapText.normal.textColor = Color.white;
                mapMarker = new GUIStyle(mapText) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            }

            const float size = 184f;
            float height = size + 28f + 17f * ((stems.Length + 1) / 2);
            float scale = Mathf.Min(1f, (Screen.width - 12f) / size, (Screen.height - 12f) / height);
            if (scale <= 0f) return;
            Matrix4x4 previousMatrix = GUI.matrix;
            Color previousColor = GUI.color;
            int previousDepth = GUI.depth;
            GUI.depth = -50;
            GUI.matrix = Matrix4x4.TRS(new Vector3(Screen.width - size * scale - 6f,
                Screen.height - height * scale - 6f, 0), Quaternion.identity, new Vector3(scale, scale, 1));
            MapRect(new Rect(0, 0, size, height), new Color(0.04f, 0.08f, 0.11f, 0.92f));
            float range = Mathf.Max(0.1f, minimapRadius);
            GUI.Label(new Rect(8, 4, size - 16, 18), $"音源方位   ·   {range:0.#} m", mapText);

            Vector2 center = new Vector2(size / 2, 24 + size / 2);
            float radius = size / 2 - 10;
            var grid = new Color(0.27f, 0.38f, 0.42f);
            MapRect(new Rect(center.x - radius, center.y, 2 * radius, 1), grid);
            MapRect(new Rect(center.x, center.y - radius, 1, 2 * radius), grid);
            Quaternion heading = Quaternion.Euler(0, -camera.transform.eulerAngles.y, 0);
            bool Project(Vector3 world, out Vector2 point)
            {
                Vector3 offset = heading * (world - camera.transform.position);
                Vector2 ground = new Vector2(offset.x, -offset.z);
                point = center + ground * (radius / range);
                return ground.sqrMagnitude <= range * range;
            }

            var ink = new Color(0.10f, 0.42f, 0.47f);
            for (int i = 0; i < stems.Length; i++)
            {
                var emitter = stems[i].emitter;
                if (emitter == null || !emitter.activeInHierarchy) continue;
                if (!Project(emitter.transform.position, out Vector2 point)) continue;
                var badge = new Rect(point.x - 6, point.y - 7, 12, 14);
                MapRect(badge, ink);
                GUI.Label(badge, markerNumbers[i], mapMarker);
            }
            // Small upward chevron marks the listener and its forward direction.
            var listener = new Color(1f, 0.78f, 0.35f);
            for (int y = 0; y < 4; y++)
                MapRect(new Rect(center.x - y, center.y - 3 + y, 1 + 2 * y, 1), listener);
            for (int i = 0; i < Legend.Length; i++)
                GUI.Label(new Rect(8 + (i % 2) * size / 2, size + 24 + (i / 2) * 17,
                    size / 2 - 8, 17), Legend[i], mapText);
            if (hallPreset)
            {
                for (int i = 0; i < HallReturns.Length; i++)
                {
                    if (!Project(HallReturns[i].Position, out Vector2 point)) continue;
                    MapRect(new Rect(point.x - 8, point.y - 7, 16, 14), i < 2 ? new Color(.65f,.36f,.08f) : new Color(.4f,.28f,.65f));
                    GUI.Label(new Rect(point.x - 8, point.y - 7, 16, 14), HallReturns[i].Marker, mapMarker);
                }
            }
            GUI.matrix = previousMatrix;
            GUI.color = previousColor;
            GUI.depth = previousDepth;
        }
    }
}
