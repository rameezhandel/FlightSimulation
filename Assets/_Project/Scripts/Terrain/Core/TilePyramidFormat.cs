using System;
using System.IO;

namespace Cirrus.Terrain
{
    /// <summary>
    /// Binary format for one precomputed heightmap tile, produced offline by
    /// tools/terrain-pipeline (Python) and shipped in StreamingAssets (CLAUDE.md §5
    /// — terrain is never fetched at runtime). Elevations are int16 decimetres
    /// (±3276.7 m, 0.1 m steps), 129x129 samples so neighbouring tiles share their
    /// edge row. Little-endian throughout; the Python writer must match
    /// (tools/terrain-pipeline/tile_format.py).
    /// </summary>
    public static class TilePyramidFormat
    {
        public const uint Magic = 0x4C495443; // "CTIL"
        public const int FormatVersion = 1;
        public const int SamplesPerSide = 129;
        public const float MetersPerUnit = 0.1f;

        public static string FileName(in TileAddress tile) => $"tile_{tile.Level}_{tile.X}_{tile.Y}.ctil";

        public static void Write(Stream stream, in TileAddress tile, short[] elevationDecimeters)
        {
            if (elevationDecimeters.Length != SamplesPerSide * SamplesPerSide)
                throw new ArgumentException($"expected {SamplesPerSide * SamplesPerSide} samples, got {elevationDecimeters.Length}");

            using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
            writer.Write(Magic);
            writer.Write(FormatVersion);
            writer.Write(tile.Level);
            writer.Write(tile.X);
            writer.Write(tile.Y);
            foreach (short sample in elevationDecimeters)
                writer.Write(sample);
        }

        public static (TileAddress tile, short[] elevationDecimeters) Read(Stream stream)
        {
            using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
            if (reader.ReadUInt32() != Magic)
                throw new InvalidDataException("Not a Cirrus terrain tile.");
            int version = reader.ReadInt32();
            if (version != FormatVersion)
                throw new InvalidDataException($"Unsupported tile version {version}.");

            var tile = new TileAddress(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
            var samples = new short[SamplesPerSide * SamplesPerSide];
            for (int i = 0; i < samples.Length; i++)
                samples[i] = reader.ReadInt16();
            return (tile, samples);
        }
    }

    /// <summary>
    /// Height source backed by one decoded tile, bilinear interpolation, for use by
    /// the streamer once real pipeline tiles exist. Samples outside the tile clamp
    /// to its edge (the caller is responsible for picking the right tile).
    /// </summary>
    public sealed class TileHeightSource : IHeightSource
    {
        readonly TileAddress _tile;
        readonly short[] _samples;
        readonly LocalNE _corner;
        readonly float _cellSize;

        public TileHeightSource(in TileAddress tile, short[] samples)
        {
            _tile = tile;
            _samples = samples;
            _corner = tile.MinCorner;
            _cellSize = tile.Size / (TilePyramidFormat.SamplesPerSide - 1);
        }

        public float SampleElevation(float north, float east)
        {
            int n = TilePyramidFormat.SamplesPerSide;
            float fn = Math.Clamp((north - _corner.North) / _cellSize, 0f, n - 1.001f);
            float fe = Math.Clamp((east - _corner.East) / _cellSize, 0f, n - 1.001f);
            int r = (int)fn, c = (int)fe;
            float tn = fn - r, te = fe - c;

            float h00 = _samples[r * n + c];
            float h10 = _samples[(r + 1) * n + c];
            float h01 = _samples[r * n + c + 1];
            float h11 = _samples[(r + 1) * n + c + 1];
            float h = (h00 * (1 - tn) + h10 * tn) * (1 - te) + (h01 * (1 - tn) + h11 * tn) * te;
            return h * TilePyramidFormat.MetersPerUnit;
        }
    }
}
