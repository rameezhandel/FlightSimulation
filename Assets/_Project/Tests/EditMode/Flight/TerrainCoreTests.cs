using System;
using System.Collections.Generic;
using System.IO;
using Cirrus.Terrain;
using NUnit.Framework;

namespace Cirrus.Tests.Terrain
{
    [TestFixture]
    public class RegionProjectionTests
    {
        [Test]
        public void CenterMapsToOrigin()
        {
            LocalNE local = RegionProjection.ToLocal(new GeoPoint(Region.CenterLatitude, Region.CenterLongitude));
            Assert.AreEqual(0f, local.North, 0.01f);
            Assert.AreEqual(0f, local.East, 0.01f);
        }

        [Test]
        public void RoundTripIsExact()
        {
            var juneau = Airports.JuneauGeo;
            GeoPoint back = RegionProjection.ToGeo(RegionProjection.ToLocal(juneau));
            Assert.AreEqual(juneau.Latitude, back.Latitude, 1e-5);
            Assert.AreEqual(juneau.Longitude, back.Longitude, 1e-5);
        }

        [Test]
        public void OneDegreeOfLatitudeIsAbout111Km()
        {
            LocalNE p = RegionProjection.ToLocal(new GeoPoint(Region.CenterLatitude + 1.0, Region.CenterLongitude));
            Assert.AreEqual(111_195f, p.North, 500f);
        }

        [Test]
        public void RegionCoversTheNamedTowns()
        {
            // CLAUDE.md §1: Juneau, Sitka, Skagway, Haines, Gustavus.
            var towns = new[]
            {
                new GeoPoint(58.3547, -134.5763), // Juneau
                new GeoPoint(57.0531, -135.3300), // Sitka
                new GeoPoint(59.4600, -135.3144), // Skagway
                new GeoPoint(59.2358, -135.4453), // Haines
                new GeoPoint(58.4133, -135.7378), // Gustavus
            };
            foreach (GeoPoint town in towns)
                Assert.IsTrue(RegionProjection.IsInsideRegion(RegionProjection.ToLocal(town)),
                    $"({town.Latitude}, {town.Longitude}) fell outside the region");
        }
    }

    [TestFixture]
    public class QuadtreeSelectorTests
    {
        static List<TileAddress> Select(float north, float east, float height, int maxLevel = 6)
        {
            var results = new List<TileAddress>();
            QuadtreeSelector.Select(new LocalNE(north, east), height, maxLevel,
                QuadtreeSelector.DefaultSplitFactor, results);
            return results;
        }

        [Test]
        public void SelectionTilesTheRegionExactly()
        {
            List<TileAddress> tiles = Select(1000f, -2000f, 500f);
            double area = 0;
            foreach (TileAddress tile in tiles)
                area += (double)tile.Size * tile.Size;
            Assert.AreEqual((double)Region.Size * Region.Size, area, 1.0, "selected tiles must cover the region exactly once");

            // No tile may be an ancestor of another (that would double-cover).
            var set = new HashSet<TileAddress>(tiles);
            foreach (TileAddress tile in tiles)
            {
                var (level, x, y) = (tile.Level, tile.X, tile.Y);
                while (level > 0)
                {
                    level--; x /= 2; y /= 2;
                    Assert.IsFalse(set.Contains(new TileAddress(level, x, y)),
                        $"{tile} and its ancestor are both selected");
                }
            }
        }

        [Test]
        public void FinestTilesAppearNearTheViewer()
        {
            var viewer = new LocalNE(0f, 0f);
            List<TileAddress> tiles = Select(0f, 0f, 100f);
            int finestNear = 0;
            foreach (TileAddress tile in tiles)
                if (tile.Level == 6 && tile.DistanceTo(viewer) < 2f * tile.Size)
                    finestNear++;
            Assert.Greater(finestNear, 0, "leaf tiles must surround the viewer");
        }

        [Test]
        public void FarTilesAreCoarse()
        {
            List<TileAddress> tiles = Select(-Region.HalfSize + 1000f, -Region.HalfSize + 1000f, 200f);
            var farCorner = new LocalNE(Region.HalfSize - 1000f, Region.HalfSize - 1000f);
            foreach (TileAddress tile in tiles)
                if (tile.DistanceTo(farCorner) < 1f)
                    Assert.LessOrEqual(tile.Level, 2, $"far corner should be coarse, got {tile}");
        }

        [Test]
        public void SelectionIsDeterministicAndBounded()
        {
            List<TileAddress> a = Select(5000f, 7000f, 300f);
            List<TileAddress> b = Select(5000f, 7000f, 300f);
            CollectionAssert.AreEqual(a, b);
            // Frame budget sanity (CLAUDE.md §6): tile count stays manageable.
            Assert.Less(a.Count, 400, $"selected {a.Count} tiles");
        }

        [Test]
        public void HighAltitudeCoarsensTheFinestDetail()
        {
            static int LeafCount(List<TileAddress> tiles)
            {
                int count = 0;
                foreach (TileAddress tile in tiles)
                    if (tile.Level == 6) count++;
                return count;
            }

            // Selection radii quantize to coarse-tile widths, so compare altitudes
            // far enough apart to guarantee different split sets.
            int low = LeafCount(Select(0f, 0f, 100f));
            int high = LeafCount(Select(0f, 0f, 20_000f));
            Assert.Less(high, low, "altitude must shrink the ring of finest tiles");
        }
    }

    [TestFixture]
    public class TileMesherTests
    {
        sealed class FlatSource : IHeightSource
        {
            public float Elevation = 100f;
            public float SampleElevation(float north, float east) => Elevation;
        }

        sealed class SlopeSource : IHeightSource
        {
            // h = 0.1 * north: constant gradient along north.
            public float SampleElevation(float north, float east) => 0.1f * north;
        }

        [Test]
        public void FlatSourceProducesPlanarGridWithUpNormals()
        {
            var tile = new TileAddress(6, 30, 30);
            TileMeshData mesh = TileMesher.Build(new FlatSource(), tile, skirtDepth: 10f);

            int n = TileMesher.VertexRows;
            Assert.AreEqual(n * n + 4 * n, mesh.VertexCount);
            for (int v = 0; v < n * n; v++)
            {
                Assert.AreEqual(100f, mesh.Positions[v * 3 + 1], 1e-3f, $"grid vertex {v} height");
                Assert.AreEqual(1f, mesh.Normals[v * 3 + 1], 1e-3f, $"grid vertex {v} normal");
            }
            // Skirt vertices sit below the surface.
            for (int v = n * n; v < mesh.VertexCount; v++)
                Assert.AreEqual(90f, mesh.Positions[v * 3 + 1], 1e-3f, $"skirt vertex {v}");
        }

        [Test]
        public void SlopeSourceProducesCorrectNormals()
        {
            var tile = new TileAddress(6, 32, 32);
            TileMeshData mesh = TileMesher.Build(new SlopeSource(), tile, 10f);

            // Analytic normal of h = 0.1n: core (-0.1, 0, 1)/|.| -> unity (0, 1, -0.1)/|.|
            float inv = 1f / MathF.Sqrt(1.01f);
            for (int v = 0; v < 5; v++)
            {
                Assert.AreEqual(0f, mesh.Normals[v * 3 + 0], 1e-3f);
                Assert.AreEqual(inv, mesh.Normals[v * 3 + 1], 1e-3f);
                Assert.AreEqual(-0.1f * inv, mesh.Normals[v * 3 + 2], 1e-3f);
            }
        }

        [Test]
        public void TriangleIndicesAreInRangeAndComplete()
        {
            var tile = new TileAddress(3, 4, 4);
            TileMeshData mesh = TileMesher.Build(new FlatSource(), tile, 10f);
            int expectedTriangles = TileMesher.QuadsPerSide * TileMesher.QuadsPerSide * 2
                                    + 4 * TileMesher.QuadsPerSide * 2;
            Assert.AreEqual(expectedTriangles * 3, mesh.Triangles.Length);
            foreach (int index in mesh.Triangles)
                Assert.That(index, Is.InRange(0, mesh.VertexCount - 1));
        }

        [Test]
        public void VertexPositionsAreCornerRelative()
        {
            var tile = new TileAddress(6, 10, 50);
            TileMeshData mesh = TileMesher.Build(new FlatSource(), tile, 10f);
            // First grid vertex is the SW corner: local (0, h, 0).
            Assert.AreEqual(0f, mesh.Positions[0], 1e-3f);
            Assert.AreEqual(0f, mesh.Positions[2], 1e-3f);
            // Last grid vertex is the NE corner: local (size, h, size).
            int last = TileMesher.VertexRows * TileMesher.VertexRows - 1;
            Assert.AreEqual(tile.Size, mesh.Positions[last * 3 + 0], 0.01f);
            Assert.AreEqual(tile.Size, mesh.Positions[last * 3 + 2], 0.01f);
        }
    }

    [TestFixture]
    public class HeightSourceTests
    {
        [Test]
        public void SyntheticTerrainIsDeterministic()
        {
            var a = new SyntheticAlaskaHeightSource();
            var b = new SyntheticAlaskaHeightSource();
            var random = new Random(7);
            for (int i = 0; i < 200; i++)
            {
                float north = (float)(random.NextDouble() - 0.5) * Region.Size;
                float east = (float)(random.NextDouble() - 0.5) * Region.Size;
                Assert.AreEqual(a.SampleElevation(north, east), b.SampleElevation(north, east));
            }
        }

        [Test]
        public void SyntheticTerrainHasMountainsAndSea()
        {
            var source = new SyntheticAlaskaHeightSource();
            float min = float.MaxValue, max = float.MinValue;
            int belowSea = 0, samples = 0;
            for (float north = -Region.HalfSize; north < Region.HalfSize; north += 4000f)
            {
                for (float east = -Region.HalfSize; east < Region.HalfSize; east += 4000f)
                {
                    float h = source.SampleElevation(north, east);
                    min = MathF.Min(min, h);
                    max = MathF.Max(max, h);
                    if (h < 0f) belowSea++;
                    samples++;
                }
            }
            Assert.Greater(max, 800f, "needs real mountains");
            Assert.Less(min, -50f, "needs flooded fjords");
            float seaFraction = (float)belowSea / samples;
            Assert.That(seaFraction, Is.InRange(0.15f, 0.75f),
                $"sea fraction {seaFraction:F2} — fjords/sea should be a big share of the region (CLAUDE.md §5)");
        }

        [Test]
        public void JuneauRunwayPadIsFlatAtFieldElevation()
        {
            IHeightSource source = Airports.WithJuneau(new SyntheticAlaskaHeightSource());
            LocalNE pajn = RegionProjection.ToLocal(Airports.JuneauGeo);
            float heading = Airports.JuneauRunwayHeading * MathF.PI / 180f;

            // Sample along the centreline.
            for (float along = -1300f; along <= 1300f; along += 100f)
            {
                float north = pajn.North + along * MathF.Cos(heading);
                float east = pajn.East + along * MathF.Sin(heading);
                Assert.AreEqual(Airports.JuneauFieldElevation, source.SampleElevation(north, east), 1e-3f,
                    $"runway not flat at {along} m from the threshold midpoint");
            }
        }

        [Test]
        public void RunwayPadBlendsBackToTerrain()
        {
            var raw = new SyntheticAlaskaHeightSource();
            IHeightSource padded = Airports.WithJuneau(raw);
            LocalNE pajn = RegionProjection.ToLocal(Airports.JuneauGeo);
            // 3 km north of the field, the pad must have no influence at all.
            float north = pajn.North + 3000f;
            Assert.AreEqual(raw.SampleElevation(north, pajn.East), padded.SampleElevation(north, pajn.East));
        }
    }

    [TestFixture]
    public class TilePyramidFormatTests
    {
        [Test]
        public void RoundTripPreservesEverything()
        {
            var tile = new TileAddress(6, 12, 60);
            var samples = new short[TilePyramidFormat.SamplesPerSide * TilePyramidFormat.SamplesPerSide];
            var random = new Random(42);
            for (int i = 0; i < samples.Length; i++)
                samples[i] = (short)random.Next(-2400, 17_000);

            using var buffer = new MemoryStream();
            TilePyramidFormat.Write(buffer, tile, samples);
            buffer.Position = 0;
            (TileAddress readTile, short[] readSamples) = TilePyramidFormat.Read(buffer);

            Assert.AreEqual(tile, readTile);
            CollectionAssert.AreEqual(samples, readSamples);
        }

        [Test]
        public void TileHeightSourceInterpolatesBilinearly()
        {
            var tile = new TileAddress(0, 0, 0);
            int n = TilePyramidFormat.SamplesPerSide;
            var samples = new short[n * n];
            // Height ramp along north: 10 m per sample row (100 decimetre units).
            for (int r = 0; r < n; r++)
                for (int c = 0; c < n; c++)
                    samples[r * n + c] = (short)(r * 100);

            var source = new TileHeightSource(tile, samples);
            float cell = tile.Size / (n - 1);
            LocalNE corner = tile.MinCorner;
            Assert.AreEqual(0f, source.SampleElevation(corner.North, corner.East), 1e-2f);
            Assert.AreEqual(10f, source.SampleElevation(corner.North + cell, corner.East), 1e-2f);
            Assert.AreEqual(5f, source.SampleElevation(corner.North + 0.5f * cell, corner.East), 1e-2f,
                "midpoint must interpolate");
        }

        [Test]
        public void GarbageIsRejected()
        {
            using var garbage = new MemoryStream(new byte[] { 9, 9, 9, 9, 9, 9, 9, 9 });
            Assert.Throws<InvalidDataException>(() => { TilePyramidFormat.Read(garbage); });
        }
    }
}
