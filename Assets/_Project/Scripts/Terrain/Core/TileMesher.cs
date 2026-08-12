using System;

namespace Cirrus.Terrain
{
    /// <summary>
    /// Plain-array mesh payload for one tile, in Unity axis order (x = east,
    /// y = up, z = north) and positioned relative to the tile's south-west corner —
    /// tile GameObjects sit at the corner so vertex floats stay small (float
    /// precision, CLAUDE.md §5). Produced by pure code on worker threads; the Unity
    /// streamer only copies it into a Mesh on the main thread.
    /// </summary>
    public sealed class TileMeshData
    {
        public TileAddress Tile;
        public float[] Positions = Array.Empty<float>(); // xyz triplets
        public float[] Normals = Array.Empty<float>();   // xyz triplets
        public float[] Uvs = Array.Empty<float>();       // uv pairs
        public byte[] Colors = Array.Empty<byte>();      // rgba quads: provisional landcover tint
        public int[] Triangles = Array.Empty<int>();
        public int VertexCount => Positions.Length / 3;
    }

    /// <summary>
    /// Samples a height source into a regular grid mesh with a skirt ring: the
    /// outermost vertices are duplicated and dropped by SkirtDepth so gaps between
    /// neighbouring LOD levels never show daylight (the crack-handling strategy —
    /// see QuadtreeSelector).
    /// </summary>
    public static class TileMesher
    {
        public const int QuadsPerSide = 64;
        public const int VertexRows = QuadsPerSide + 1;

        public static TileMeshData Build(IHeightSource source, in TileAddress tile, float skirtDepth)
        {
            int n = VertexRows;
            int gridVerts = n * n;
            int skirtVerts = 4 * n;
            float cell = tile.Size / QuadsPerSide;
            LocalNE corner = tile.MinCorner;

            var data = new TileMeshData
            {
                Tile = tile,
                Positions = new float[(gridVerts + skirtVerts) * 3],
                Normals = new float[(gridVerts + skirtVerts) * 3],
                Uvs = new float[(gridVerts + skirtVerts) * 2],
                Colors = new byte[(gridVerts + skirtVerts) * 4],
                Triangles = new int[(QuadsPerSide * QuadsPerSide * 2 + 4 * QuadsPerSide * 2) * 3],
            };

            // Grid vertices. r indexes north, c indexes east.
            for (int r = 0; r < n; r++)
            {
                for (int c = 0; c < n; c++)
                {
                    float north = corner.North + r * cell;
                    float east = corner.East + c * cell;
                    float h = source.SampleElevation(north, east);

                    int v = r * n + c;
                    data.Positions[v * 3 + 0] = c * cell;  // unity x = east offset
                    data.Positions[v * 3 + 1] = h;         // unity y = up
                    data.Positions[v * 3 + 2] = r * cell;  // unity z = north offset

                    // Central-difference normal; core (north, east, up) gradient
                    // converted to unity order (east, up, north).
                    float dhdn = (source.SampleElevation(north + cell, east) - source.SampleElevation(north - cell, east)) / (2f * cell);
                    float dhde = (source.SampleElevation(north, east + cell) - source.SampleElevation(north, east - cell)) / (2f * cell);
                    float inv = 1f / MathF.Sqrt(dhdn * dhdn + dhde * dhde + 1f);
                    data.Normals[v * 3 + 0] = -dhde * inv;
                    data.Normals[v * 3 + 1] = inv;
                    data.Normals[v * 3 + 2] = -dhdn * inv;

                    data.Uvs[v * 2 + 0] = (float)c / QuadsPerSide;
                    data.Uvs[v * 2 + 1] = (float)r / QuadsPerSide;

                    WriteLandcoverColor(data.Colors, v, h, data.Normals[v * 3 + 1]);
                }
            }

            // Grid triangles, wound clockwise seen from above (Unity front face).
            int t = 0;
            for (int r = 0; r < QuadsPerSide; r++)
            {
                for (int c = 0; c < QuadsPerSide; c++)
                {
                    int i00 = r * n + c;
                    int i10 = (r + 1) * n + c;
                    int i01 = r * n + c + 1;
                    int i11 = (r + 1) * n + c + 1;
                    data.Triangles[t++] = i00; data.Triangles[t++] = i10; data.Triangles[t++] = i01;
                    data.Triangles[t++] = i10; data.Triangles[t++] = i11; data.Triangles[t++] = i01;
                }
            }

            // Skirts: duplicate each border vertex, dropped by skirtDepth, and stitch.
            // Order: south row (r=0), north row (r=n-1), west column, east column.
            int skirtBase = gridVerts;
            int s = skirtBase;
            for (int c = 0; c < n; c++) s = AddSkirtVertex(data, s, 0 * n + c, skirtDepth);
            for (int c = 0; c < n; c++) s = AddSkirtVertex(data, s, (n - 1) * n + c, skirtDepth);
            for (int r = 0; r < n; r++) s = AddSkirtVertex(data, s, r * n + 0, skirtDepth);
            for (int r = 0; r < n; r++) s = AddSkirtVertex(data, s, r * n + (n - 1), skirtDepth);

            for (int c = 0; c < QuadsPerSide; c++)
            {
                // South edge: outward face points south.
                t = StitchSkirt(data, t, 0 * n + c, 0 * n + c + 1, skirtBase + c, skirtBase + c + 1, flip: false);
                // North edge.
                t = StitchSkirt(data, t, (n - 1) * n + c, (n - 1) * n + c + 1, skirtBase + n + c, skirtBase + n + c + 1, flip: true);
            }
            for (int r = 0; r < QuadsPerSide; r++)
            {
                // West edge.
                t = StitchSkirt(data, t, r * n, (r + 1) * n, skirtBase + 2 * n + r, skirtBase + 2 * n + r + 1, flip: true);
                // East edge.
                t = StitchSkirt(data, t, r * n + (n - 1), (r + 1) * n + (n - 1), skirtBase + 3 * n + r, skirtBase + 3 * n + r + 1, flip: false);
            }

            return data;
        }

        /// <summary>
        /// Provisional landcover tint by elevation band and slope (real procedural
        /// texturing from ESA WorldCover classes replaces this later in M2 — see
        /// CLAUDE.md §5). Bands: tidal flat, conifer, alpine meadow-rock, rock,
        /// snow; steep faces read as rock regardless of altitude.
        /// </summary>
        static void WriteLandcoverColor(byte[] colors, int vertex, float elevation, float normalUp)
        {
            byte red, green, blue;
            if (elevation < 1f) { red = 116; green = 106; blue = 84; }        // tidal flat / shore
            else if (elevation < 350f) { red = 44; green = 78; blue = 46; }   // conifer forest
            else if (elevation < 700f) { red = 76; green = 92; blue = 60; }   // subalpine
            else if (elevation < 1000f) { red = 118; green = 114; blue = 108; } // rock
            else { red = 235; green = 238; blue = 240; }                       // snow / glacier
            if (normalUp < 0.72f && elevation > 1f) { red = 104; green = 100; blue = 96; } // cliffs
            colors[vertex * 4 + 0] = red;
            colors[vertex * 4 + 1] = green;
            colors[vertex * 4 + 2] = blue;
            colors[vertex * 4 + 3] = 255;
        }

        static int AddSkirtVertex(TileMeshData data, int dest, int sourceVertex, float skirtDepth)
        {
            data.Positions[dest * 3 + 0] = data.Positions[sourceVertex * 3 + 0];
            data.Positions[dest * 3 + 1] = data.Positions[sourceVertex * 3 + 1] - skirtDepth;
            data.Positions[dest * 3 + 2] = data.Positions[sourceVertex * 3 + 2];
            data.Normals[dest * 3 + 0] = data.Normals[sourceVertex * 3 + 0];
            data.Normals[dest * 3 + 1] = data.Normals[sourceVertex * 3 + 1];
            data.Normals[dest * 3 + 2] = data.Normals[sourceVertex * 3 + 2];
            data.Uvs[dest * 2 + 0] = data.Uvs[sourceVertex * 2 + 0];
            data.Uvs[dest * 2 + 1] = data.Uvs[sourceVertex * 2 + 1];
            for (int channel = 0; channel < 4; channel++)
                data.Colors[dest * 4 + channel] = data.Colors[sourceVertex * 4 + channel];
            return dest + 1;
        }

        static int StitchSkirt(TileMeshData data, int t, int edgeA, int edgeB, int skirtA, int skirtB, bool flip)
        {
            if (flip)
            {
                data.Triangles[t++] = edgeA; data.Triangles[t++] = skirtA; data.Triangles[t++] = edgeB;
                data.Triangles[t++] = skirtA; data.Triangles[t++] = skirtB; data.Triangles[t++] = edgeB;
            }
            else
            {
                data.Triangles[t++] = edgeA; data.Triangles[t++] = edgeB; data.Triangles[t++] = skirtA;
                data.Triangles[t++] = edgeB; data.Triangles[t++] = skirtB; data.Triangles[t++] = skirtA;
            }
            return t;
        }
    }
}
