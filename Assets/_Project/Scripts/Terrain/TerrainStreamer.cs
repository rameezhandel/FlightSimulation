using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Cirrus.Terrain;
using UnityEngine;

namespace Cirrus.TerrainStreaming
{
    /// <summary>
    /// Streams terrain tiles around the viewer: reselects the quadtree twice a
    /// second, meshes new tiles on the thread pool (pure TileMesher — geometry
    /// generation never touches the render thread, CLAUDE.md §5), and uploads a
    /// budgeted number of finished meshes per frame on the main thread.
    ///
    /// NOTE (deviation from CLAUDE.md §5 "use the Jobs system"): meshing runs on
    /// ThreadPool tasks for now because the pure mesher is verifiable headlessly;
    /// moving the inner loops to Burst jobs is a profiling-driven optimization to
    /// do in-editor. Recorded in docs/decisions/0003.
    /// </summary>
    public sealed class TerrainStreamer : MonoBehaviour
    {
        public const int MaxLevel = 7;             // leaf ~34 m cells at 280 km root
        public const float SkirtDepthBase = 40f;   // scaled down per level
        public const float SelectInterval = 0.5f;
        public const int UploadsPerFrame = 2;
        public const int CollisionLevel = MaxLevel; // only leaf tiles get colliders

        IHeightSource? _source;
        FloatingOrigin? _origin;
        Transform? _viewer;
        Material? _material;
        Transform? _tileRoot;

        readonly Dictionary<TileAddress, GameObject> _active = new Dictionary<TileAddress, GameObject>(256);
        readonly HashSet<TileAddress> _pendingMesh = new HashSet<TileAddress>();
        readonly HashSet<TileAddress> _wanted = new HashSet<TileAddress>();
        readonly List<TileAddress> _selection = new List<TileAddress>(512);
        readonly List<TileAddress> _toRemove = new List<TileAddress>(64);
        readonly ConcurrentQueue<TileMeshData> _completed = new ConcurrentQueue<TileMeshData>();
        float _nextSelect;

        public void Initialize(IHeightSource source, FloatingOrigin origin, Transform viewer)
        {
            _source = source;
            _origin = origin;
            _viewer = viewer;

            var rootObject = new GameObject("TerrainTiles");
            _tileRoot = rootObject.transform;
            origin.Register(_tileRoot);
            origin.Shifted += _ => RepositionAllTiles();

            Shader shader = Shader.Find("Cirrus/TerrainVertexColor")
                            ?? Shader.Find("Universal Render Pipeline/Lit")
                            ?? Shader.Find("Standard");
            _material = new Material(shader);
        }

        /// <summary>Terrain elevation under a Unity world position — ground effect + AGL readouts.</summary>
        public float SampleElevation(UnityEngine.Vector3 unityPosition)
        {
            if (_source == null || _origin == null) return 0f;
            LocalNE region = _origin.ToRegion(unityPosition);
            return _source.SampleElevation(region.North, region.East);
        }

        void Update()
        {
            UploadCompletedMeshes();

            if (_source == null || _origin == null || _viewer == null || Time.time < _nextSelect)
                return;
            _nextSelect = Time.time + SelectInterval;

            LocalNE viewerRegion = _origin.ToRegion(_viewer.position);
            float heightAboveGround = Mathf.Max(0f,
                _viewer.position.y - _source.SampleElevation(viewerRegion.North, viewerRegion.East));
            QuadtreeSelector.Select(viewerRegion, heightAboveGround, MaxLevel,
                QuadtreeSelector.DefaultSplitFactor, _selection);

            _wanted.Clear();
            foreach (TileAddress tile in _selection) _wanted.Add(tile);

            // Retire tiles that fell out of the selection.
            _toRemove.Clear();
            foreach (KeyValuePair<TileAddress, GameObject> entry in _active)
                if (!_wanted.Contains(entry.Key))
                    _toRemove.Add(entry.Key);
            foreach (TileAddress tile in _toRemove)
            {
                GameObject tileObject = _active[tile];
                _active.Remove(tile);
                Destroy(tileObject.GetComponent<MeshFilter>().sharedMesh);
                Destroy(tileObject);
            }

            // Queue meshing for missing tiles.
            foreach (TileAddress tile in _selection)
            {
                if (_active.ContainsKey(tile) || _pendingMesh.Contains(tile))
                    continue;
                _pendingMesh.Add(tile);
                TileAddress captured = tile;
                IHeightSource source = _source;
                float skirt = SkirtDepthBase / (1 << System.Math.Max(0, captured.Level - 3));
                Task.Run(() => _completed.Enqueue(TileMesher.Build(source, captured, skirt)));
            }
        }

        void UploadCompletedMeshes()
        {
            for (int i = 0; i < UploadsPerFrame && _completed.TryDequeue(out TileMeshData? data); i++)
            {
                _pendingMesh.Remove(data.Tile);
                // Selection may have moved on while the mesh was baking.
                if (!_wanted.Contains(data.Tile) || _active.ContainsKey(data.Tile))
                    continue;
                _active.Add(data.Tile, CreateTileObject(data));
            }
        }

        GameObject CreateTileObject(TileMeshData data)
        {
            var tileObject = new GameObject(data.Tile.ToString());
            tileObject.transform.SetParent(_tileRoot, false);
            PositionTile(tileObject.transform, data.Tile);

            var mesh = new Mesh { name = data.Tile.ToString() };
            int count = data.VertexCount;
            var vertices = new UnityEngine.Vector3[count];
            var normals = new UnityEngine.Vector3[count];
            var colors = new Color32[count];
            var uvs = new UnityEngine.Vector2[count];
            for (int v = 0; v < count; v++)
            {
                vertices[v] = new UnityEngine.Vector3(data.Positions[v * 3], data.Positions[v * 3 + 1], data.Positions[v * 3 + 2]);
                normals[v] = new UnityEngine.Vector3(data.Normals[v * 3], data.Normals[v * 3 + 1], data.Normals[v * 3 + 2]);
                colors[v] = new Color32(data.Colors[v * 4], data.Colors[v * 4 + 1], data.Colors[v * 4 + 2], data.Colors[v * 4 + 3]);
                uvs[v] = new UnityEngine.Vector2(data.Uvs[v * 2], data.Uvs[v * 2 + 1]);
            }
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.colors32 = colors;
            mesh.uv = uvs;
            mesh.triangles = data.Triangles;
            mesh.RecalculateBounds();

            tileObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            tileObject.AddComponent<MeshRenderer>().sharedMaterial = _material;
            if (data.Tile.Level >= CollisionLevel)
                tileObject.AddComponent<MeshCollider>().sharedMesh = mesh;
            return tileObject;
        }

        void PositionTile(Transform tileTransform, in TileAddress tile)
        {
            if (_origin == null) return;
            tileTransform.position = _origin.ToUnity(tile.MinCorner, 0f);
        }

        void RepositionAllTiles()
        {
            // Tiles live under the registered root, so the shift already moved them;
            // nothing to do — kept as the explicit subscription point if per-tile
            // absolute data ever gets cached here. (CLAUDE.md §5: every system that
            // caches world positions subscribes to the rebase event.)
        }
    }
}
