using System;
using System.Collections.Generic;
using System.Linq;

namespace MeesaMultisMaker.Generation
{
    /// <summary>
    /// Building-aware WFC generator for multi-level structures.
    /// Understands floors, walls, doors, roofs, and stairs through
    /// TileData flags and learned adjacency from training examples.
    /// </summary>
    public class StructureGenerator
    {
        private PatternLibrary _library;
        private Random _random;
        private int _maxRetries = 10;
        private List<int> _floorLevels;

        public StructureGenerator(PatternLibrary library, int? seed = null)
        {
            _library = library;
            _random = seed.HasValue ? new Random(seed.Value) : new Random();
        }

        /// <summary>
        /// Generate a new structure based on learned patterns.
        /// </summary>
        public List<TileInstance> Generate(int width, int height, StructureZones templateZones = null, float randomness = 0.3f)
        {
            for (int attempt = 0; attempt < _maxRetries; attempt++)
            {
                try
                {
                    var result = AttemptGeneration(width, height, templateZones, randomness);
                    if (result != null && result.Count > 0)
                    {
                        // Validate and correct against UO multi rules
                        MultiValidationResult validation;
                        result = MultiValidator.ValidateAndCorrect(result, out validation);
                        return result;
                    }
                }
                catch
                {
                    continue;
                }
            }

            return new List<TileInstance>();
        }

        private List<TileInstance> AttemptGeneration(int width, int height, StructureZones template, float randomness)
        {
            var grid = new Dictionary<Point3D, TileInstance>();
            var allowedTiles = _library.AllowedTiles.ToList();

            if (allowedTiles.Count == 0)
                return null;

            // Get the actual Z levels from the library
            var zLevels = _library.GetObservedZLevels();
            if (zLevels.Count == 0)
                zLevels = new List<int> { 0 };

            // Detect floor levels from training data for structural awareness
            _floorLevels = MultiRules.DetectFloorLevels(zLevels);

            // Phase 1: Place zone templates if provided (scaled)
            if (template != null)
            {
                PlaceZoneTemplates(grid, template, width, height, randomness);
            }

            // Phase 2: Fill using column-based WFC at observed Z levels only
            FillWithWfc(grid, width, height, zLevels, randomness);

            return grid.Values.ToList();
        }

        private void PlaceZoneTemplates(Dictionary<Point3D, TileInstance> grid, StructureZones template,
            int targetWidth, int targetHeight, float randomness)
        {
            float scaleX = template.Width > 0 ? (float)targetWidth / template.Width : 1f;
            float scaleY = template.Height > 0 ? (float)targetHeight / template.Height : 1f;

            // Place all zone tiles scaled to target size
            PlaceZoneTiles(grid, template.FloorTiles, targetWidth, targetHeight, scaleX, scaleY, randomness, 0.05f);
            PlaceZoneTiles(grid, template.WallTiles, targetWidth, targetHeight, scaleX, scaleY, randomness, 0.1f);
            PlaceZoneTiles(grid, template.RoofTiles, targetWidth, targetHeight, scaleX, scaleY, randomness, 0.2f);
            PlaceZoneTiles(grid, template.DoorTiles, targetWidth, targetHeight, scaleX, scaleY, randomness, 0.3f);
            PlaceZoneTiles(grid, template.StairTiles, targetWidth, targetHeight, scaleX, scaleY, randomness, 0.15f);
            PlaceZoneTiles(grid, template.OtherTiles, targetWidth, targetHeight, scaleX, scaleY, randomness, 0.4f);
        }

        private void PlaceZoneTiles(Dictionary<Point3D, TileInstance> grid, List<TileInstance> tiles,
            int targetWidth, int targetHeight, float scaleX, float scaleY, float randomness, float skipFactor)
        {
            foreach (var tile in tiles)
            {
                // Structural tiles (floors, walls) skip less often
                if (_random.NextDouble() < randomness * skipFactor)
                    continue;

                int scaledX = (int)Math.Round(tile.Position.X * scaleX);
                int scaledY = (int)Math.Round(tile.Position.Y * scaleY);

                scaledX = Math.Max(0, Math.Min(targetWidth - 1, scaledX));
                scaledY = Math.Max(0, Math.Min(targetHeight - 1, scaledY));

                var pos = new Point3D(scaledX, scaledY, tile.Position.Z);

                if (!grid.ContainsKey(pos))
                {
                    var placed = tile.Clone();
                    placed.Position = pos;
                    grid[pos] = placed;
                }
            }
        }

        /// <summary>
        /// Fill empty grid positions using WFC-style generation.
        /// Only fills at Z levels that were actually observed in training data,
        /// preventing the dense per-Z-level filling that creates garbage.
        /// Enforces UO structural rules: upper-floor tiles require foundation
        /// support within the allowed overhang distance.
        /// </summary>
        private void FillWithWfc(Dictionary<Point3D, TileInstance> grid, int width, int height,
            List<int> zLevels, float randomness)
        {
            var allowedTiles = _library.AllowedTiles.ToList();
            if (allowedTiles.Count == 0) return;

            // Group tiles by their Z-level role for smarter filling
            var tilesByZ = _library.GetTilesByZLevel();

            // Process Z levels in order so lower floors are filled first
            foreach (int z in zLevels)
            {
                // Get tiles that are valid at this Z level
                List<ushort> validTilesForZ;
                if (tilesByZ.ContainsKey(z))
                    validTilesForZ = tilesByZ[z];
                else
                    continue; // Don't fill Z levels with no training data

                // Determine if this is an upper floor (above foundation)
                int floorLevel = MultiRules.GetFloorLevel(z);
                bool isUpperFloor = floorLevel > 0 && _floorLevels != null && _floorLevels.Count > 1;

                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        var pos = new Point3D(x, y, z);
                        if (grid.ContainsKey(pos)) continue;

                        // Upper floors require structural support from a lower floor
                        if (isUpperFloor)
                        {
                            bool hasSupport = false;
                            // Check directly below and within overhang distance
                            for (int dx = -MultiRules.MaxRoofOverhang; dx <= MultiRules.MaxRoofOverhang && !hasSupport; dx++)
                            {
                                for (int dy = -MultiRules.MaxRoofOverhang; dy <= MultiRules.MaxRoofOverhang && !hasSupport; dy++)
                                {
                                    foreach (int lowerZ in zLevels)
                                    {
                                        if (lowerZ >= z) break;
                                        if (grid.ContainsKey(new Point3D(x + dx, y + dy, lowerZ)))
                                        {
                                            hasSupport = true;
                                            break;
                                        }
                                    }
                                }
                            }
                            if (!hasSupport) continue;
                        }

                        var bestTile = FindBestTileForPosition(grid, pos, validTilesForZ, randomness);

                        if (bestTile.HasValue)
                        {
                            bool valid = IsPlacementValid(grid, pos, bestTile.Value);
                            bool hasNeighbors = HasAnyNeighbors(grid, pos);

                            if (!hasNeighbors || valid)
                            {
                                grid[pos] = new TileInstance(bestTile.Value, pos, 1);
                            }
                        }
                    }
                }
            }
        }

        private bool HasAnyNeighbors(Dictionary<Point3D, TileInstance> grid, Point3D pos)
        {
            var offsets = new[]
            {
                new Point3D(-1, 0, 0), new Point3D(1, 0, 0),
                new Point3D(0, -1, 0), new Point3D(0, 1, 0)
            };

            foreach (var offset in offsets)
            {
                if (grid.ContainsKey(pos + offset))
                    return true;
            }
            return false;
        }

        private bool IsPlacementValid(Dictionary<Point3D, TileInstance> grid, Point3D pos, ushort tileId)
        {
            var offsets = new[]
            {
                new Point3D(-1, 0, 0), new Point3D(1, 0, 0),
                new Point3D(0, -1, 0), new Point3D(0, 1, 0),
                new Point3D(0, 0, -1), new Point3D(0, 0, 1),
                new Point3D(-1, -1, 0), new Point3D(1, -1, 0),
                new Point3D(-1, 1, 0), new Point3D(1, 1, 0)
            };

            int validatedNeighbors = 0;
            int totalNeighbors = 0;

            foreach (var offset in offsets)
            {
                var neighborPos = pos + offset;

                if (grid.ContainsKey(neighborPos))
                {
                    totalNeighbors++;
                    var neighborTile = grid[neighborPos].TileId;

                    if (_library.IsAdjacencyAllowed(tileId, offset, neighborTile))
                    {
                        validatedNeighbors++;
                    }
                }
            }

            if (totalNeighbors == 0)
                return true;

            return validatedNeighbors >= (totalNeighbors / 2);
        }

        private ushort? FindBestTileForPosition(Dictionary<Point3D, TileInstance> grid, Point3D pos,
            List<ushort> validTiles, float randomness)
        {
            var candidates = new Dictionary<ushort, float>();

            var offsets = new[]
            {
                new Point3D(-1, 0, 0), new Point3D(1, 0, 0),
                new Point3D(0, -1, 0), new Point3D(0, 1, 0),
                new Point3D(0, 0, -1), new Point3D(0, 0, 1)
            };

            foreach (var offset in offsets)
            {
                var neighborPos = pos - offset;

                if (grid.ContainsKey(neighborPos))
                {
                    var neighborTile = grid[neighborPos].TileId;
                    var validNeighbors = _library.GetValidNeighbors(neighborTile, offset);

                    foreach (var validTile in validNeighbors)
                    {
                        if (!validTiles.Contains(validTile)) continue;

                        if (!candidates.ContainsKey(validTile))
                            candidates[validTile] = 0;

                        var patterns = _library.GetPatternsFor(neighborTile);
                        var matchingPattern = patterns.FirstOrDefault(p =>
                            p.Offset == offset && p.NeighborTileId == validTile);

                        if (matchingPattern != null)
                        {
                            candidates[validTile] += matchingPattern.Probability;
                        }
                    }
                }
            }

            if (candidates.Count == 0)
                return null;

            // Apply randomness to flatten distribution
            if (randomness > 0.1f && candidates.Count > 1)
            {
                float power = Math.Max(0.1f, 1.0f - randomness);
                var flattened = new Dictionary<ushort, float>();
                foreach (var kvp in candidates)
                {
                    flattened[kvp.Key] = (float)Math.Pow(kvp.Value, power);
                }
                candidates = flattened;
            }

            // Random pick chance
            if (_random.NextDouble() < randomness * 0.3f && candidates.Count > 1)
            {
                var list = candidates.Keys.ToList();
                return list[_random.Next(list.Count)];
            }

            // Weighted selection
            float totalWeight = candidates.Values.Sum();
            float randomValue = (float)_random.NextDouble() * totalWeight;
            float cumulative = 0;

            foreach (var kvp in candidates.OrderByDescending(x => x.Value))
            {
                cumulative += kvp.Value;
                if (randomValue <= cumulative)
                    return kvp.Key;
            }

            return candidates.First().Key;
        }
    }
}
