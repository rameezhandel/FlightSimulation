using System;
using System.Collections.Generic;
using System.IO;

namespace Cirrus.Terrain
{
    /// <summary>
    /// Where tile bytes come from. Abstracted so the pyramid logic is testable
    /// without touching a filesystem, and so a StreamingAssets/bundle reader can
    /// slot in later without changing anything above it.
    /// Implementations must be thread-safe: tile meshing runs on worker threads.
    /// </summary>
    public interface ITileByteSource
    {
        /// <summary>Raw .ctil bytes, or null when that tile is not in the shipped set.</summary>
        byte[]? TryRead(in TileAddress tile);
    }

    /// <summary>
    /// Reads tiles from a directory of .ctil files — the pyramid written by
    /// tools/terrain-pipeline. On iOS, StreamingAssets is an ordinary directory,
    /// so plain File IO is correct here (this would need UnityWebRequest on
    /// Android, which the project does not target — CLAUDE.md §1).
    /// </summary>
    public sealed class DirectoryTileByteSource : ITileByteSource
    {
        readonly string _directory;

        public DirectoryTileByteSource(string directory) => _directory = directory;

        public byte[]? TryRead(in TileAddress tile)
        {
            string path = Path.Combine(_directory, TilePyramidFormat.FileName(tile));
            try
            {
                return File.Exists(path) ? File.ReadAllBytes(path) : null;
            }
            catch (IOException)
            {
                return null; // a missing/locked tile degrades to the fallback source
            }
        }
    }

    /// <summary>
    /// Height source backed by the offline tile pyramid: for each sample it walks
    /// from the finest level toward the root and uses the first tile that is
    /// actually present, so a partially-built pyramid still flies (coarser terrain
    /// where fine tiles are missing). If no level has the tile, it falls back to
    /// the supplied source — the synthetic generator today, which keeps the sim
    /// flyable before the DEM has been processed.
    ///
    /// Decoded tiles are held in an LRU cache; misses are cached too, since the
    /// mesher samples the same tile tens of thousands of times in a row and a
    /// filesystem probe per sample would be ruinous.
    ///
    /// Thread-safe by a single lock around the cache. Decoding happens outside the
    /// lock, so two threads can occasionally decode the same tile — harmless, and
    /// far cheaper than holding the lock across IO.
    /// </summary>
    public sealed class TilePyramidHeightSource : IHeightSource
    {
        readonly ITileByteSource _bytes;
        readonly IHeightSource _fallback;
        readonly int _finestLevel;
        readonly int _capacity;

        readonly object _gate = new object();
        readonly Dictionary<TileAddress, LinkedListNode<Entry>> _index;
        readonly LinkedList<Entry> _lru = new LinkedList<Entry>();

        sealed class Entry
        {
            public TileAddress Tile;
            public TileHeightSource? Source; // null = known-missing tile
        }

        public TilePyramidHeightSource(
            ITileByteSource bytes,
            IHeightSource fallback,
            int finestLevel = 6,
            int cacheCapacity = 64)
        {
            if (cacheCapacity < 1) throw new ArgumentOutOfRangeException(nameof(cacheCapacity));
            _bytes = bytes;
            _fallback = fallback;
            _finestLevel = finestLevel;
            _capacity = cacheCapacity;
            _index = new Dictionary<TileAddress, LinkedListNode<Entry>>(cacheCapacity);
        }

        /// <summary>Tiles currently held in the cache, including known-missing entries.</summary>
        public int CachedTileCount
        {
            get { lock (_gate) return _index.Count; }
        }

        public float SampleElevation(float north, float east)
        {
            var point = new LocalNE(north, east);
            for (int level = _finestLevel; level >= 0; level--)
            {
                TileHeightSource? tile = Resolve(TileAddress.ForPoint(level, point));
                if (tile != null)
                    return tile.SampleElevation(north, east);
            }
            return _fallback.SampleElevation(north, east);
        }

        TileHeightSource? Resolve(in TileAddress address)
        {
            lock (_gate)
            {
                if (_index.TryGetValue(address, out LinkedListNode<Entry>? node))
                {
                    _lru.Remove(node);
                    _lru.AddFirst(node);
                    return node.Value.Source;
                }
            }

            // Decode outside the lock.
            TileHeightSource? decoded = null;
            byte[]? raw = _bytes.TryRead(address);
            if (raw != null)
            {
                try
                {
                    using var stream = new MemoryStream(raw, writable: false);
                    (TileAddress readAddress, short[] samples) = TilePyramidFormat.Read(stream);
                    // A tile filed under the wrong name would silently warp the
                    // world; trust the header, not the path.
                    if (readAddress.Equals(address))
                        decoded = new TileHeightSource(address, samples);
                }
                catch (InvalidDataException)
                {
                    decoded = null; // corrupt tile -> treat as missing
                }
            }

            lock (_gate)
            {
                if (_index.TryGetValue(address, out LinkedListNode<Entry>? existing))
                {
                    _lru.Remove(existing);
                    _lru.AddFirst(existing);
                    return existing.Value.Source;
                }

                var entry = new Entry { Tile = address, Source = decoded };
                _index[address] = _lru.AddFirst(entry);
                if (_index.Count > _capacity)
                {
                    LinkedListNode<Entry>? oldest = _lru.Last;
                    if (oldest != null)
                    {
                        _lru.RemoveLast();
                        _index.Remove(oldest.Value.Tile);
                    }
                }
                return decoded;
            }
        }
    }
}
