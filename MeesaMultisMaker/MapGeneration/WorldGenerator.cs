using System;
using System.Collections.Generic;
using MeesaMultisMaker.Mul;

namespace MeesaMultisMaker.MapGeneration
{
    /// <summary>
    /// Configuration for world generation.
    /// </summary>
    public class WorldGenConfig
    {
        public int Width { get; set; } = 512;
        public int Height { get; set; } = 512;
        public int Seed { get; set; } = 0;

        // Terrain shape
        public double LandMassPercent { get; set; } = 40;   // 0-100
        public double MountainPercent { get; set; } = 10;    // of land
        public double IslandScatter { get; set; } = 0.5;     // 0 = one continent, 1 = many islands
        public double TerrainRoughness { get; set; } = 0.5;  // 0 = smooth, 1 = rough

        // Biome weights (0-100, relative to each other for land area)
        public double GrassWeight { get; set; } = 30;
        public double ForestWeight { get; set; } = 20;
        public double DesertWeight { get; set; } = 10;
        public double JungleWeight { get; set; } = 8;
        public double SwampWeight { get; set; } = 7;
        public double SnowWeight { get; set; } = 8;
        public double FarmlandWeight { get; set; } = 5;
        public double LavaWeight { get; set; } = 2;

        // Z range
        public sbyte WaterZ { get; set; } = -5;
        public sbyte LandMinZ { get; set; } = 0;
        public sbyte LandMaxZ { get; set; } = 80;
        public sbyte MountainMaxZ { get; set; } = 120;
    }

    /// <summary>
    /// Progress callback info for the world generator.
    /// </summary>
    public class WorldGenProgress
    {
        public string Phase { get; set; }
        public double Percent { get; set; }
    }

    /// <summary>
    /// Procedural UO world generator.
    /// 
    /// Pipeline:
    ///   1. Generate elevation heightmap (Perlin FBM)
    ///   2. Apply island mask / continent shape
    ///   3. Determine water level from land-mass percentage
    ///   4. Generate moisture map (separate Perlin)
    ///   5. Generate temperature map (latitude + noise)
    ///   6. Classify biomes from elevation × moisture × temperature
    ///   7. Assign UO tile IDs + Z values
    ///   8. Add transition / beach tiles along coastlines
    /// </summary>
    public class WorldGenerator
    {
        private readonly WorldGenConfig _cfg;
        private readonly Random _rng;
        private readonly MapTransitionAnalyzer _transitionAnalyzer;

        public WorldGenerator(WorldGenConfig config)
        {
            _cfg = config ?? new WorldGenConfig();
            _rng = _cfg.Seed == 0 ? new Random() : new Random(_cfg.Seed);
        }

        /// <summary>
        /// Create a generator that uses a pre-learned transition analyzer for tile placement.
        /// Use this to regenerate an existing map with the new biome system while preserving
        /// the original tile placement patterns as closely as possible.
        /// </summary>
        public WorldGenerator(WorldGenConfig config, MapTransitionAnalyzer transitionAnalyzer)
            : this(config)
        {
            _transitionAnalyzer = transitionAnalyzer;
        }

        /// <summary>
        /// Generate a complete UO map.
        /// </summary>
        /// <param name="progress">Optional progress callback.</param>
        public MapData Generate(Action<WorldGenProgress> progress = null)
        {
            int w = (_cfg.Width / 8) * 8;
            int h = (_cfg.Height / 8) * 8;
            if (w < 8) w = 8;
            if (h < 8) h = 8;

            var map = new MapData { Width = w, Height = h, Tiles = new LandTile[w, h] };

            // ── Phase 1: Elevation ──────────────────────────────────
            progress?.Invoke(new WorldGenProgress { Phase = "Generating elevation...", Percent = 0 });

            var elevNoise = new PerlinNoise(_rng.Next());
            double[,] elevation = new double[w, h];

            // Scale controls island size. Low scale = big continents, high = small islands.
            double baseScale = 0.002 + _cfg.IslandScatter * 0.008;
            int octaves = 4 + (int)(_cfg.TerrainRoughness * 4);

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    elevation[x, y] = elevNoise.FBM(x * baseScale, y * baseScale, octaves, 2.0, 0.5);
                }
            }

            // ── Phase 2: Island mask ────────────────────────────────
            progress?.Invoke(new WorldGenProgress { Phase = "Shaping landmass...", Percent = 15 });

            // Circular falloff pushes edges underwater for island-style maps
            double cx = w / 2.0, cy = h / 2.0;
            double maxDist = Math.Sqrt(cx * cx + cy * cy);
            double falloffStrength = 0.3 + _cfg.IslandScatter * 0.7;

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    double dx = (x - cx) / cx;
                    double dy = (y - cy) / cy;
                    double dist = Math.Sqrt(dx * dx + dy * dy);
                    double falloff = Math.Max(0, 1.0 - dist) * falloffStrength;
                    elevation[x, y] = elevation[x, y] * 0.5 + 0.5; // normalise to ~0-1
                    elevation[x, y] -= (1.0 - falloff) * 0.3;
                }
            }

            // ── Phase 3: Water level ────────────────────────────────
            progress?.Invoke(new WorldGenProgress { Phase = "Setting water level...", Percent = 25 });

            double waterLevel = FindPercentile(elevation, w, h, 1.0 - _cfg.LandMassPercent / 100.0);

            // ── Phase 4: Moisture (domain-warped for organic shapes) ─
            progress?.Invoke(new WorldGenProgress { Phase = "Generating moisture...", Percent = 35 });

            var moistNoise = new PerlinNoise(_rng.Next());
            var moistWarpNoise = new PerlinNoise(_rng.Next());
            double[,] moisture = new double[w, h];
            double moistScale = 0.005;
            double moistWarpScale = 0.003;
            double moistWarpStrength = 30.0;

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    double wx = moistWarpNoise.Noise(x * moistWarpScale, y * moistWarpScale) * moistWarpStrength;
                    double wy = moistWarpNoise.Noise(x * moistWarpScale + 100, y * moistWarpScale + 100) * moistWarpStrength;
                    moisture[x, y] = (moistNoise.FBM((x + wx) * moistScale, (y + wy) * moistScale, 3) + 1.0) * 0.5;
                }
            }

            // ── Phase 5: Temperature (domain-warped) ────────────────
            progress?.Invoke(new WorldGenProgress { Phase = "Generating temperature...", Percent = 45 });

            var tempNoise = new PerlinNoise(_rng.Next());
            var tempWarpNoise = new PerlinNoise(_rng.Next());
            double[,] temperature = new double[w, h];
            double tempScale = 0.004;
            double tempWarpScale = 0.003;
            double tempWarpStrength = 25.0;

            for (int y = 0; y < h; y++)
            {
                double latBase = 1.0 - Math.Abs(y - h / 2.0) / (h / 2.0);
                for (int x = 0; x < w; x++)
                {
                    double twx = tempWarpNoise.Noise(x * tempWarpScale, y * tempWarpScale) * tempWarpStrength;
                    double twy = tempWarpNoise.Noise(x * tempWarpScale + 200, y * tempWarpScale + 200) * tempWarpStrength;
                    double noise = tempNoise.FBM((x + twx) * tempScale, (y + twy) * tempScale, 3) * 0.3;
                    temperature[x, y] = Math.Max(0, Math.Min(1, latBase + noise));
                }
            }

            // ── Phase 6: Biome classification ──────────────────────
            progress?.Invoke(new WorldGenProgress { Phase = "Classifying biomes...", Percent = 55 });

            double mountainThreshold = 1.0 - (_cfg.MountainPercent / 100.0) * (_cfg.LandMassPercent / 100.0);
            double mountainLevel = FindPercentile(elevation, w, h, mountainThreshold);

            AdjustClimateForWeights(temperature, moisture, w, h);

            var biomes = new UOBiomeType[w, h];
            var isLand = new bool[w, h];

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    double elev = elevation[x, y];
                    if (elev <= waterLevel)
                    {
                        double depth = (waterLevel - elev) / Math.Max(0.001, waterLevel);
                        biomes[x, y] = depth > 0.3 ? UOBiomeType.DeepWater : UOBiomeType.ShallowWater;
                        isLand[x, y] = false;
                    }
                    else
                    {
                        double relativeElev = (elev - waterLevel) / Math.Max(0.001, 1.0 - waterLevel);
                        biomes[x, y] = UOBiomes.Classify(relativeElev, moisture[x, y], temperature[x, y]);
                        isLand[x, y] = true;
                    }
                }
            }

            // ── Phase 6b: Smooth biomes (majority filter) ───────────
            progress?.Invoke(new WorldGenProgress { Phase = "Smoothing biomes...", Percent = 65 });
            SmoothBiomes(biomes, isLand, w, h, 2);

            // ── Phase 6c: Dither biome edges ────────────────────────
            var ditherNoise = new PerlinNoise(_rng.Next());
            DitherBiomeEdges(biomes, isLand, w, h, ditherNoise);

            // ── Phase 7: Tile assignment (coherent selection or learned transitions) ───────
            progress?.Invoke(new WorldGenProgress { Phase = "Assigning tiles...", Percent = 75 });

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    double elev = elevation[x, y];
                    UOBiomeType biome = biomes[x, y];

                    if (!isLand[x, y])
                    {
                        map.Tiles[x, y] = new LandTile
                        {
                            TileId = _transitionAnalyzer != null
                                ? _transitionAnalyzer.SampleTileByBiome(biome, biomes, x, y, _rng, preferMostCommon: false)
                                : UOBiomes.CoherentTile(biome, x, y),
                            Z = _cfg.WaterZ
                        };
                    }
                    else
                    {
                        double relativeElev = (elev - waterLevel) / Math.Max(0.001, 1.0 - waterLevel);
                        sbyte z = CalculateZ(relativeElev, elev >= mountainLevel);
                        map.Tiles[x, y] = new LandTile
                        {
                            TileId = _transitionAnalyzer != null
                                ? _transitionAnalyzer.SampleTileByBiome(biome, biomes, x, y, _rng, preferMostCommon: false)
                                : UOBiomes.CoherentTile(biome, x, y),
                            Z = z
                        };
                    }
                }
            }

            // ── Phase 8: Coastline smoothing ────────────────────────
            progress?.Invoke(new WorldGenProgress { Phase = "Smoothing coastlines...", Percent = 85 });
            AddBeachTransitions(map, w, h, elevation, waterLevel);

            progress?.Invoke(new WorldGenProgress { Phase = "Complete", Percent = 100 });
            return map;
        }

        /// <summary>
        /// Regenerate with a random seed, preserving the transition analyzer.
        /// </summary>
        public MapData Regenerate(Action<WorldGenProgress> progress = null)
        {
            _cfg.Seed = new Random().Next();
            return new WorldGenerator(_cfg, _transitionAnalyzer).Generate(progress);
        }

        // ================================================================
        //  Private helpers
        // ================================================================

        private sbyte CalculateZ(double relativeElev, bool isMountain)
        {
            double z;
            if (isMountain)
            {
                z = _cfg.LandMaxZ + relativeElev * (_cfg.MountainMaxZ - _cfg.LandMaxZ);
            }
            else
            {
                z = _cfg.LandMinZ + relativeElev * (_cfg.LandMaxZ - _cfg.LandMinZ);
            }
            return (sbyte)Math.Max(-128, Math.Min(127, (int)Math.Round(z)));
        }

        /// <summary>
        /// Find the elevation value at a given percentile (0-1).
        /// </summary>
        private static double FindPercentile(double[,] data, int w, int h, double percentile)
        {
            // Sample a subset for performance (full sort of millions of tiles is expensive)
            int sampleCount = Math.Min(w * h, 100000);
            var samples = new double[sampleCount];
            var rng = new Random(12345);

            for (int i = 0; i < sampleCount; i++)
            {
                int sx = rng.Next(w);
                int sy = rng.Next(h);
                samples[i] = data[sx, sy];
            }

            Array.Sort(samples);
            int idx = (int)(percentile * (sampleCount - 1));
            return samples[Math.Max(0, Math.Min(sampleCount - 1, idx))];
        }

        /// <summary>
        /// Subtly shift moisture/temperature arrays to bias toward the configured biome weights.
        /// This is a soft influence, not a hard override.
        /// </summary>
        private void AdjustClimateForWeights(double[,] temperature, double[,] moisture, int w, int h)
        {
            double totalWeight = _cfg.GrassWeight + _cfg.ForestWeight + _cfg.DesertWeight +
                                 _cfg.JungleWeight + _cfg.SwampWeight + _cfg.SnowWeight +
                                 _cfg.FarmlandWeight + _cfg.LavaWeight;
            if (totalWeight <= 0) return;

            // Compute desired temperature bias: desert/jungle/lava want hot, snow wants cold
            double hotWeight = (_cfg.DesertWeight + _cfg.JungleWeight + _cfg.LavaWeight) / totalWeight;
            double coldWeight = _cfg.SnowWeight / totalWeight;
            double tempBias = (hotWeight - coldWeight) * 0.15;

            // Compute desired moisture bias: forest/jungle/swamp want wet, desert wants dry
            double wetWeight = (_cfg.ForestWeight + _cfg.JungleWeight + _cfg.SwampWeight) / totalWeight;
            double dryWeight = _cfg.DesertWeight / totalWeight;
            double moistBias = (wetWeight - dryWeight) * 0.15;

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    temperature[x, y] = Math.Max(0, Math.Min(1, temperature[x, y] + tempBias));
                    moisture[x, y] = Math.Max(0, Math.Min(1, moisture[x, y] + moistBias));
                }
            }
        }

        /// <summary>
        /// Replace land tiles adjacent to water with beach tiles for natural coastlines.
        /// </summary>
        private void AddBeachTransitions(MapData map, int w, int h, double[,] elevation, double waterLevel)
        {
            // Mark tiles that are land but adjacent to water
            bool[,] isCoastal = new bool[w, h];

            for (int y = 1; y < h - 1; y++)
            {
                for (int x = 1; x < w - 1; x++)
                {
                    if (elevation[x, y] <= waterLevel) continue;

                    // Check 4 neighbours
                    if (elevation[x - 1, y] <= waterLevel ||
                        elevation[x + 1, y] <= waterLevel ||
                        elevation[x, y - 1] <= waterLevel ||
                        elevation[x, y + 1] <= waterLevel)
                    {
                        isCoastal[x, y] = true;
                    }
                }
            }

            // Also mark tiles 1 step from coastal for a wider beach
            bool[,] isNearCoast = new bool[w, h];
            for (int y = 1; y < h - 1; y++)
                for (int x = 1; x < w - 1; x++)
                    if (!isCoastal[x, y] && elevation[x, y] > waterLevel)
                        if (isCoastal[x - 1, y] || isCoastal[x + 1, y] ||
                            isCoastal[x, y - 1] || isCoastal[x, y + 1])
                            isNearCoast[x, y] = true;

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (isCoastal[x, y] || isNearCoast[x, y])
                    {
                        var tile = map.Tiles[x, y];
                        if (tile != null)
                        {
                            tile.TileId = UOBiomes.CoherentTile(UOBiomeType.Beach, x, y);
                                tile.Z = (sbyte)Math.Max((int)_cfg.LandMinZ, Math.Min(3, (int)tile.Z));
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Majority-filter smooth biomes to remove isolated single-tile biome spots.
        /// </summary>
        private static void SmoothBiomes(UOBiomeType[,] biomes, bool[,] isLand, int w, int h, int passes)
        {
            int numBiomes = Enum.GetValues(typeof(UOBiomeType)).Length;

            for (int pass = 0; pass < passes; pass++)
            {
                var copy = (UOBiomeType[,])biomes.Clone();
                for (int y = 1; y < h - 1; y++)
                {
                    for (int x = 1; x < w - 1; x++)
                    {
                        if (!isLand[x, y]) continue;

                        int[] counts = new int[numBiomes];
                        for (int dy = -1; dy <= 1; dy++)
                        {
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                if (!isLand[x + dx, y + dy]) continue;
                                counts[(int)copy[x + dx, y + dy]]++;
                            }
                        }

                        int maxCount = 0;
                        UOBiomeType majority = copy[x, y];
                        for (int i = 0; i < numBiomes; i++)
                        {
                            if (counts[i] > maxCount)
                            {
                                maxCount = counts[i];
                                majority = (UOBiomeType)i;
                            }
                        }
                        biomes[x, y] = majority;
                    }
                }
            }
        }

        /// <summary>
        /// Add noise-based dithering at biome boundaries for softer transitions.
        /// </summary>
        private static void DitherBiomeEdges(UOBiomeType[,] biomes, bool[,] isLand, int w, int h, PerlinNoise noise)
        {
            var copy = (UOBiomeType[,])biomes.Clone();
            double ditherScale = 0.15;

            for (int y = 1; y < h - 1; y++)
            {
                for (int x = 1; x < w - 1; x++)
                {
                    if (!isLand[x, y]) continue;

                    UOBiomeType current = copy[x, y];
                    UOBiomeType neighbor = current;
                    bool isEdge = false;

                    for (int dy = -1; dy <= 1 && !isEdge; dy++)
                    {
                        for (int dx = -1; dx <= 1 && !isEdge; dx++)
                        {
                            if (dx == 0 && dy == 0) continue;
                            if (!isLand[x + dx, y + dy]) continue;
                            if (copy[x + dx, y + dy] != current)
                            {
                                isEdge = true;
                                neighbor = copy[x + dx, y + dy];
                            }
                        }
                    }

                    if (isEdge)
                    {
                        double n = (noise.Noise(x * ditherScale, y * ditherScale) + 1.0) * 0.5;
                        if (n > 0.55)
                        {
                            biomes[x, y] = neighbor;
                        }
                    }
                }
            }
        }
    }
}
