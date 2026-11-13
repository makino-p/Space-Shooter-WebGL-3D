using UnityEngine;

// Делает объёмный меш из 2D-спрайта: силуэт по альфе "надувается" (толще в центре, тоньше к краям),
// сверху и снизу лежит исходная картинка, по яркости пикселей добавляется рельеф.
public static class SpriteMeshGenerator
{
    public struct Settings
    {
        public int Grid;             // разрешение сетки по ширине картинки
        public float PixelsPerUnit;  // как у спрайта (100)
        public float MaxHeight;      // толщина в центре (в юнитах спрайта); <=0 — круглый профиль по радиусу
        public float EdgeHeight;     // толщина на краю
        public float BottomFactor;   // насколько выпукла нижняя сторона относительно верхней (0..1)
        public float LumaRelief;     // рельеф от яркости картинки
        public bool RoundProfile;    // профиль-полусфера (для астероида)
    }

    public static Mesh Build(Color32[] pixels, int width, int height, Settings s, string name)
    {
        int n = s.Grid;
        float cellPx = width / (float)n;
        int m = Mathf.Max(1, Mathf.RoundToInt(height / cellPx));
        float cellPy = height / (float)m;

        // 1. какие клетки сетки заняты картинкой
        bool[,] solid = new bool[n, m];
        for (int cx = 0; cx < n; cx++)
        {
            for (int cy = 0; cy < m; cy++)
            {
                int hits = 0;
                for (int sx = 0; sx < 3; sx++)
                {
                    for (int sy = 0; sy < 3; sy++)
                    {
                        int px = Mathf.Clamp(Mathf.FloorToInt((cx + (sx + 0.5f) / 3f) * cellPx), 0, width - 1);
                        int py = Mathf.Clamp(Mathf.FloorToInt((cy + (sy + 0.5f) / 3f) * cellPy), 0, height - 1);
                        if (pixels[py * width + px].a > 127) hits++;
                    }
                }
                solid[cx, cy] = hits >= 5;
            }
        }

        // 2. расстояние до края силуэта (chamfer distance transform, в клетках)
        const float inf = 1e6f;
        float[,] dist = new float[n, m];
        for (int x = 0; x < n; x++)
            for (int y = 0; y < m; y++)
                dist[x, y] = solid[x, y] ? inf : 0f;

        for (int y = 0; y < m; y++)
            for (int x = 0; x < n; x++)
                if (solid[x, y])
                {
                    float d = dist[x, y];
                    d = Mathf.Min(d, D(dist, x - 1, y, n, m) + 1f);
                    d = Mathf.Min(d, D(dist, x, y - 1, n, m) + 1f);
                    d = Mathf.Min(d, D(dist, x - 1, y - 1, n, m) + 1.414f);
                    d = Mathf.Min(d, D(dist, x + 1, y - 1, n, m) + 1.414f);
                    dist[x, y] = d;
                }
        for (int y = m - 1; y >= 0; y--)
            for (int x = n - 1; x >= 0; x--)
                if (solid[x, y])
                {
                    float d = dist[x, y];
                    d = Mathf.Min(d, D(dist, x + 1, y, n, m) + 1f);
                    d = Mathf.Min(d, D(dist, x, y + 1, n, m) + 1f);
                    d = Mathf.Min(d, D(dist, x + 1, y + 1, n, m) + 1.414f);
                    d = Mathf.Min(d, D(dist, x - 1, y + 1, n, m) + 1.414f);
                    dist[x, y] = d;
                }

        float maxD = 0.001f;
        foreach (float d in dist) if (d < inf && d > maxD) maxD = d;

        float unitPerCell = cellPx / s.PixelsPerUnit;
        float radius = maxD * unitPerCell; // полуширина самой толстой части

        // 3. высоты в узлах сетки
        float[,] h = new float[n + 1, m + 1];
        bool[,] boundary = new bool[n + 1, m + 1];
        for (int i = 0; i <= n; i++)
        {
            for (int j = 0; j <= m; j++)
            {
                float minD = inf;
                bool edge = false;
                for (int dx = -1; dx <= 0; dx++)
                {
                    for (int dy = -1; dy <= 0; dy++)
                    {
                        int cx = i + dx, cy = j + dy;
                        if (cx < 0 || cy < 0 || cx >= n || cy >= m || !solid[cx, cy]) { edge = true; continue; }
                        minD = Mathf.Min(minD, dist[cx, cy]);
                    }
                }
                boundary[i, j] = edge;
                float dd = edge ? 0f : Mathf.Max(0f, minD - 0.5f);

                float vh;
                if (s.RoundProfile)
                {
                    float r = radius;
                    float du = dd * unitPerCell;
                    vh = s.EdgeHeight + Mathf.Sqrt(Mathf.Max(0f, du * (2f * r - du)));
                }
                else
                {
                    vh = s.EdgeHeight + (s.MaxHeight - s.EdgeHeight) * Mathf.Sqrt(Mathf.Clamp01(dd / maxD));
                }

                if (!edge && s.LumaRelief > 0f)
                {
                    int px = Mathf.Clamp(Mathf.RoundToInt(i * cellPx), 0, width - 1);
                    int py = Mathf.Clamp(Mathf.RoundToInt(j * cellPy), 0, height - 1);
                    Color32 c = pixels[py * width + px];
                    float luma = (0.299f * c.r + 0.587f * c.g + 0.114f * c.b) / 255f;
                    vh += s.LumaRelief * (luma - 0.35f);
                }
                h[i, j] = Mathf.Max(s.EdgeHeight * 0.5f, vh);
            }
        }

        // 4. сборка меша
        var verts = new System.Collections.Generic.List<Vector3>();
        var uvs = new System.Collections.Generic.List<Vector2>();
        var tris = new System.Collections.Generic.List<int>();
        int[,] top = new int[n + 1, m + 1];
        int[,] bottom = new int[n + 1, m + 1];
        for (int i = 0; i <= n; i++) for (int j = 0; j <= m; j++) { top[i, j] = -1; bottom[i, j] = -1; }

        System.Func<int, int, float, Vector3> pos = (i, j, y) =>
            new Vector3((i * cellPx - width * 0.5f) / s.PixelsPerUnit, y, (j * cellPy - height * 0.5f) / s.PixelsPerUnit);
        System.Func<int, int, Vector2> uv = (i, j) => new Vector2(i * cellPx / width, j * cellPy / height);

        System.Func<int, int, int> Top = (i, j) =>
        {
            if (top[i, j] < 0) { top[i, j] = verts.Count; verts.Add(pos(i, j, h[i, j])); uvs.Add(uv(i, j)); }
            return top[i, j];
        };
        System.Func<int, int, int> Bottom = (i, j) =>
        {
            if (bottom[i, j] < 0) { bottom[i, j] = verts.Count; verts.Add(pos(i, j, -h[i, j] * s.BottomFactor)); uvs.Add(uv(i, j)); }
            return bottom[i, j];
        };

        for (int cx = 0; cx < n; cx++)
        {
            for (int cy = 0; cy < m; cy++)
            {
                if (!solid[cx, cy]) continue;

                // верх (нормаль вверх)
                int a = Top(cx, cy), b = Top(cx + 1, cy), c = Top(cx, cy + 1), d = Top(cx + 1, cy + 1);
                tris.Add(a); tris.Add(c); tris.Add(d);
                tris.Add(a); tris.Add(d); tris.Add(b);

                // низ (нормаль вниз)
                a = Bottom(cx, cy); b = Bottom(cx + 1, cy); c = Bottom(cx, cy + 1); d = Bottom(cx + 1, cy + 1);
                tris.Add(a); tris.Add(d); tris.Add(c);
                tris.Add(a); tris.Add(b); tris.Add(d);

                // боковые стенки там, где сосед пустой
                AddSide(cx, cy, cx - 1, cy, cx, cy, cx, cy + 1, new Vector3(-1, 0, 0));
                AddSide(cx, cy, cx + 1, cy, cx + 1, cy, cx + 1, cy + 1, new Vector3(1, 0, 0));
                AddSide(cx, cy, cx, cy - 1, cx, cy, cx + 1, cy, new Vector3(0, 0, -1));
                AddSide(cx, cy, cx, cy + 1, cx, cy + 1, cx + 1, cy + 1, new Vector3(0, 0, 1));
            }
        }

        void AddSide(int cx, int cy, int nx, int ny, int ai, int aj, int bi, int bj, Vector3 outward)
        {
            if (nx >= 0 && ny >= 0 && nx < n && ny < m && solid[nx, ny]) return;

            Vector3 tA = pos(ai, aj, h[ai, aj]);
            Vector3 tB = pos(bi, bj, h[bi, bj]);
            Vector3 bA = pos(ai, aj, -h[ai, aj] * s.BottomFactor);
            Vector3 bB = pos(bi, bj, -h[bi, bj] * s.BottomFactor);
            int start = verts.Count;
            verts.Add(tA); verts.Add(tB); verts.Add(bB); verts.Add(bA);
            Vector2 uA = uv(ai, aj), uB = uv(bi, bj);
            uvs.Add(uA); uvs.Add(uB); uvs.Add(uB); uvs.Add(uA);

            Vector3 normal = Vector3.Cross(tB - tA, bB - tA);
            if (Vector3.Dot(normal, outward) > 0f)
            {
                tris.Add(start); tris.Add(start + 1); tris.Add(start + 2);
                tris.Add(start); tris.Add(start + 2); tris.Add(start + 3);
            }
            else
            {
                tris.Add(start); tris.Add(start + 2); tris.Add(start + 1);
                tris.Add(start); tris.Add(start + 3); tris.Add(start + 2);
            }
        }

        Mesh mesh = new Mesh();
        mesh.name = name;
        if (verts.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(verts);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        return mesh;
    }

    static float D(float[,] dist, int x, int y, int n, int m)
    {
        if (x < 0 || y < 0 || x >= n || y >= m) return 0f;
        return dist[x, y];
    }
}
