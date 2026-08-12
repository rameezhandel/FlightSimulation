using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Cirrus.Terrain;
using NUnit.Framework;

namespace Cirrus.Tests.Terrain
{
    [TestFixture]
    public class TilePyramidHeightSourceTests
    {
        /// <summary>In-memory byte source; also counts reads so cache behaviour is observable.</summary>
        sealed class FakeByteSource : ITileByteSource
        {
            readonly Dictionary<TileAddress, byte[]> _tiles = new Dictionary<TileAddress, byte[]>();
            public int ReadCount;

            public void Add(in TileAddress tile, float constantElevation)
            {
                var samples = new short[TilePyramidFormat.SamplesPerSide * TilePyramidFormat.SamplesPerSide];
                Array.Fill(samples, (short)MathF.Round(constantElevation / TilePyramidFormat.MetersPerUnit));
                using var buffer = new MemoryStream();
                TilePyramidFormat.Write(buffer, tile, samples);
                _tiles[tile] = buffer.ToArray();
            }

            public void AddRaw(in TileAddress tile, byte[] bytes) => _tiles[tile] = bytes;

            public byte[]? TryRead(in TileAddress tile)
            {
                System.Threading.Interlocked.Increment(ref ReadCount);
                return _tiles.TryGetValue(tile, out byte[]? bytes) ? bytes : null;
            }
        }

        sealed class ConstantSource : IHeightSource
        {
            readonly float _value;
            public ConstantSource(float value) => _value = value;
            public float SampleElevation(float north, float east) => _value;
        }

        static readonly LocalNE Somewhere = new LocalNE(12_000f, -35_000f);

        [Test]
        public void UsesTheFinestAvailableTile()
        {
            var bytes = new FakeByteSource();
            bytes.Add(TileAddress.ForPoint(3, Somewhere), 300f);
            bytes.Add(TileAddress.ForPoint(6, Somewhere), 600f);

            var source = new TilePyramidHeightSource(bytes, new ConstantSource(-999f), finestLevel: 6);
            Assert.AreEqual(600f, source.SampleElevation(Somewhere.North, Somewhere.East), 0.05f);
        }

        [Test]
        public void FallsBackToCoarserLevelsWhenFineTilesAreMissing()
        {
            var bytes = new FakeByteSource();
            bytes.Add(TileAddress.ForPoint(2, Somewhere), 250f);

            var source = new TilePyramidHeightSource(bytes, new ConstantSource(-999f), finestLevel: 6);
            Assert.AreEqual(250f, source.SampleElevation(Somewhere.North, Somewhere.East), 0.05f,
                "a partially built pyramid must still fly, just coarser");
        }

        [Test]
        public void FallsBackToTheBackupSourceWhenNothingIsPresent()
        {
            var source = new TilePyramidHeightSource(new FakeByteSource(), new ConstantSource(42f), finestLevel: 6);
            Assert.AreEqual(42f, source.SampleElevation(Somewhere.North, Somewhere.East), 1e-3f);
        }

        [Test]
        public void RepeatedSamplesDoNotReReadTiles()
        {
            var bytes = new FakeByteSource();
            bytes.Add(TileAddress.ForPoint(6, Somewhere), 100f);
            var source = new TilePyramidHeightSource(bytes, new ConstantSource(0f), finestLevel: 6);

            source.SampleElevation(Somewhere.North, Somewhere.East);
            int afterFirst = bytes.ReadCount;
            for (int i = 0; i < 500; i++)
                source.SampleElevation(Somewhere.North + i * 0.1f, Somewhere.East);

            Assert.AreEqual(afterFirst, bytes.ReadCount,
                "hits and misses must both be cached — the mesher samples a tile tens of thousands of times");
        }

        [Test]
        public void CacheRespectsCapacityAndStaysCorrect()
        {
            var bytes = new FakeByteSource();
            // A row of distinct level-6 tiles with distinct elevations.
            var points = new List<LocalNE>();
            for (int i = 0; i < 20; i++)
            {
                var point = new LocalNE(-100_000f + i * 5000f, 0f);
                points.Add(point);
                bytes.Add(TileAddress.ForPoint(6, point), 10f * i);
            }

            var source = new TilePyramidHeightSource(bytes, new ConstantSource(-1f), finestLevel: 6, cacheCapacity: 4);

            // Two passes: the second is all evictions, and must still be right.
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < points.Count; i++)
                    Assert.AreEqual(10f * i, source.SampleElevation(points[i].North, points[i].East), 0.05f,
                        $"pass {pass}, tile {i}");

            Assert.LessOrEqual(source.CachedTileCount, 4 + 7,
                "cache must stay bounded (capacity plus the coarse-level probes for one sample)");
        }

        [Test]
        public void CorruptOrMisfiledTilesDegradeToFallback()
        {
            var bytes = new FakeByteSource();
            TileAddress address = TileAddress.ForPoint(6, Somewhere);
            bytes.AddRaw(address, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });

            var source = new TilePyramidHeightSource(bytes, new ConstantSource(7f), finestLevel: 6);
            Assert.AreEqual(7f, source.SampleElevation(Somewhere.North, Somewhere.East), 1e-3f,
                "a corrupt tile must not throw into the mesher");
        }

        [Test]
        public void TileFiledUnderTheWrongAddressIsRejected()
        {
            var bytes = new FakeByteSource();
            TileAddress requested = TileAddress.ForPoint(6, Somewhere);
            // Encode a DIFFERENT address into the bytes stored at the requested key.
            var samples = new short[TilePyramidFormat.SamplesPerSide * TilePyramidFormat.SamplesPerSide];
            Array.Fill(samples, (short)5000);
            using var buffer = new MemoryStream();
            TilePyramidFormat.Write(buffer, new TileAddress(6, requested.X + 1, requested.Y), samples);
            bytes.AddRaw(requested, buffer.ToArray());

            var source = new TilePyramidHeightSource(bytes, new ConstantSource(-3f), finestLevel: 6);
            Assert.AreEqual(-3f, source.SampleElevation(Somewhere.North, Somewhere.East), 1e-3f,
                "a mislabelled tile would silently warp the world — trust the header, not the filename");
        }

        [Test]
        public void ConcurrentSamplingIsSafeAndConsistent()
        {
            var bytes = new FakeByteSource();
            for (int i = 0; i < 32; i++)
                bytes.Add(TileAddress.ForPoint(6, new LocalNE(-120_000f + i * 6000f, 0f)), 5f * i);

            var source = new TilePyramidHeightSource(bytes, new ConstantSource(0f), finestLevel: 6, cacheCapacity: 8);

            // Meshing runs on worker threads (CLAUDE.md §5) — this must not tear.
            Parallel.For(0, 64, iteration =>
            {
                for (int i = 0; i < 32; i++)
                {
                    var point = new LocalNE(-120_000f + i * 6000f, 0f);
                    float h = source.SampleElevation(point.North, point.East);
                    Assert.AreEqual(5f * i, h, 0.05f);
                }
            });
        }

        [Test]
        public void DirectoryByteSourceReadsWhatThePipelineWrites()
        {
            string directory = Path.Combine(Path.GetTempPath(), "cirrus-tiles-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var address = new TileAddress(4, 3, 9);
                var samples = new short[TilePyramidFormat.SamplesPerSide * TilePyramidFormat.SamplesPerSide];
                Array.Fill(samples, (short)1234);
                using (FileStream file = File.Create(Path.Combine(directory, TilePyramidFormat.FileName(address))))
                    TilePyramidFormat.Write(file, address, samples);

                var byteSource = new DirectoryTileByteSource(directory);
                Assert.IsNotNull(byteSource.TryRead(address));
                Assert.IsNull(byteSource.TryRead(new TileAddress(4, 3, 10)), "absent tiles read as null, not an exception");

                var source = new TilePyramidHeightSource(byteSource, new ConstantSource(-1f), finestLevel: 4);
                LocalNE center = address.Center;
                Assert.AreEqual(123.4f, source.SampleElevation(center.North, center.East), 0.05f);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [TestFixture]
    public class TileAddressForPointTests
    {
        [Test]
        public void ForPointFindsTheContainingTile()
        {
            for (int level = 0; level <= 6; level++)
            {
                var point = new LocalNE(37_000f, -88_000f);
                TileAddress tile = TileAddress.ForPoint(level, point);
                Assert.AreEqual(0f, tile.DistanceTo(point), 1e-3f, $"level {level} tile must contain the point");
            }
        }

        [Test]
        public void ForPointClampsOutsideTheRegion()
        {
            TileAddress tile = TileAddress.ForPoint(4, new LocalNE(Region.Size, Region.Size));
            Assert.AreEqual(15, tile.X);
            Assert.AreEqual(15, tile.Y);
        }

        [Test]
        public void ParentContainsChild()
        {
            var child = new TileAddress(5, 21, 8);
            TileAddress parent = child.Parent;
            Assert.AreEqual(4, parent.Level);
            Assert.AreEqual(0f, parent.DistanceTo(child.Center), 1e-3f);
        }
    }
}
