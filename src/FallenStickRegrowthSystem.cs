using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace RenewableFallenSticks;

public sealed class FallenStickRegrowthSystem : ModSystem
{
    private const string ModdataKey = "renewablefallensticks:last-check";
    private const string TreeGroupAttribute = "treeFellingGroupCode";
    private const string TreeSpreadAttribute = "treeFellingGroupSpreadIndex";
    private const int LoadedChunkCheckIntervalMs = 6000;

    private ICoreServerAPI api = null!;
    private RegrowthConfig config = null!;
    private int looseStickId;
    private long tickListenerId;
    private readonly Dictionary<long, LoadedColumn> loadedColumns = new();

    public override void StartServerSide(ICoreServerAPI api)
    {
        this.api = api;
        config = LoadConfig(api);
        looseStickId = api.World.GetBlock(new AssetLocation("game:loosestick-free"))?.Id ?? 0;
        api.Logger.Notification(
            "Renewable Fallen Sticks loaded. Check interval: {0} game hours, catch-up limit: {1}.",
            config.CheckIntervalHours,
            config.MaxCatchUpAttempts
        );
        if (looseStickId == 0)
        {
            api.Logger.Error("Renewable Fallen Sticks could not resolve block game:loosestick-free.");
        }
        api.Event.ChunkColumnLoaded += OnChunkColumnLoaded;
        api.Event.ChunkColumnUnloaded += OnChunkColumnUnloaded;
        tickListenerId = api.Event.RegisterGameTickListener(ProcessLoadedColumn, LoadedChunkCheckIntervalMs);
    }

    private void OnChunkColumnLoaded(Vec2i chunkCoord, IWorldChunk[] chunks)
    {
        if (chunks.Length == 0 || chunks[0] == null || looseStickId == 0) return;

        IWorldChunk chunk = chunks[0];
        loadedColumns[ChunkKey(chunkCoord.X, chunkCoord.Y)] = new LoadedColumn(chunkCoord.X, chunkCoord.Y, chunk);
        ProcessColumn(chunkCoord.X, chunkCoord.Y, chunk);
    }

    private void OnChunkColumnUnloaded(Vec3i chunkCoord)
    {
        loadedColumns.Remove(ChunkKey(chunkCoord.X, chunkCoord.Z));
    }

    private void ProcessLoadedColumn(float dt)
    {
        foreach (var entry in loadedColumns)
        {
            LoadedColumn column = entry.Value;
            if (ProcessColumn(column.ChunkX, column.ChunkZ, column.Chunk)) break;
        }
    }

    private bool ProcessColumn(int chunkX, int chunkZ, IWorldChunk chunk)
    {
        double now = api.World.Calendar.TotalHours;
        double lastCheck = ReadLastCheck(chunk);
        double elapsedHours = now - lastCheck;
        if (elapsedHours < config.CheckIntervalHours) return false;

        int attempts = Math.Clamp(
            (int)(elapsedHours / config.CheckIntervalHours),
            1,
            config.MaxCatchUpAttempts
        );

        IBlockAccessor blockAccessor = api.World.BlockAccessor;
        var random = new LCGRandom();
        long regrowthCycle = (long)Math.Floor(now / config.CheckIntervalHours);
        random.SetWorldSeed(api.World.Seed ^ regrowthCycle);
        random.InitPositionSeed(chunkX, chunkZ);
        WriteLastCheck(chunk, now);

        int spawned = 0;
        int maxSpawned = config.MaxSticksPerAttempt * attempts;
        int treeSamples = 0;
        for (int attempt = 0; attempt < attempts && spawned < maxSpawned; attempt++)
        {
            for (int sample = 0; sample < config.SamplesPerAttempt && spawned < maxSpawned; sample++)
            {
                int centerX = chunkX * GlobalConstants.ChunkSize + random.NextInt(GlobalConstants.ChunkSize);
                int centerZ = chunkZ * GlobalConstants.ChunkSize + random.NextInt(GlobalConstants.ChunkSize);
                int centerY = FindSurfaceY(blockAccessor, centerX, centerZ, random);
                if (centerY < 0)
                {
                    api.Logger.Notification(
                        "Renewable Fallen Sticks attempt {0}/{1}, sample {2}/{3}: position {4}, ?, {5}; no surface found.",
                        attempt + 1,
                        attempts,
                        sample + 1,
                        config.SamplesPerAttempt,
                        centerX,
                        centerZ
                    );
                    continue;
                }

                int[,] surfaceHeights = BuildSurfaceHeights(blockAccessor, centerX, centerY, centerZ, random);
                int treeCount = CountTreeBlocks(blockAccessor, centerX, centerZ, surfaceHeights);

                ClimateCondition climate = blockAccessor.GetClimateAt(
                    new BlockPos(centerX, centerY, centerZ),
                    EnumGetClimateMode.WorldGenValues
                );

                float highDensityTreeCount = config.ReferenceTreesPerChunk
                    * (2 * config.SampleRadius + 1) * (2 * config.SampleRadius + 1)
                    / (GlobalConstants.ChunkSize * GlobalConstants.ChunkSize);
                float localForestDensity = GameMath.Clamp(
                    MathF.Sqrt(treeCount / highDensityTreeCount),
                    0f,
                    1f
                );
                float forestness = localForestDensity * localForestDensity * 4f * (climate.Fertility + 0.25f);
                int targetSticks = (int)MathF.Round(treeCount * 0.75f * forestness);
                int currentSticks = CountLooseSticks(blockAccessor, centerX, centerZ, surfaceHeights);
                api.Logger.Notification(
                    "Renewable Fallen Sticks attempt {0}/{1}, sample {2}/{3}: position {4}, {5}, {6}; trees {7}, fertility {8:0.###}, localForestDensity {9:0.###}, forestness {10:0.###}, targetSticks {11}, currentSticks {12}.",
                    attempt + 1,
                    attempts,
                    sample + 1,
                    config.SamplesPerAttempt,
                    centerX,
                    centerY,
                    centerZ,
                    treeCount,
                    climate.Fertility,
                    localForestDensity,
                    forestness,
                    targetSticks,
                    currentSticks
                );
                if (treeCount == 0) continue;
                treeSamples++;
                int missing = Math.Min(
                    config.MaxSticksPerAttempt,
                    Math.Max(0, targetSticks - currentSticks)
                );

                while (missing > 0 && spawned < maxSpawned)
                {
                    if (!TryPlaceStick(blockAccessor, random, centerX, centerZ, surfaceHeights)) break;
                    missing--;
                    spawned++;
                }
            }
        }

        api.Logger.Notification(
            "Renewable Fallen Sticks checked chunk {0}, {1}: elapsed {2:0.##} hours, attempts {3}, spawned {4}, treeSamples {5}.",
            chunkX,
            chunkZ,
            elapsedHours,
            attempts,
            spawned,
            treeSamples
        );

        chunk.MarkModified();
        return true;
    }

    public override void Dispose()
    {
        if (tickListenerId != 0) api.Event.UnregisterGameTickListener(tickListenerId);
        loadedColumns.Clear();
        base.Dispose();
    }

    private int[,] BuildSurfaceHeights(IBlockAccessor blockAccessor, int centerX, int centerY, int centerZ, IRandom random)
    {
        int size = config.SampleRadius * 2 + 1;
        int[,] heights = new int[size, size];
        BlockPos pos = new BlockPos(centerX, centerY, centerZ);

        for (int dx = -config.SampleRadius; dx <= config.SampleRadius; dx++)
        {
            for (int dz = -config.SampleRadius; dz <= config.SampleRadius; dz++)
            {
                int x = centerX + dx;
                int z = centerZ + dz;
                int height = FindSurfaceYAt(blockAccessor, x, z, centerY, 2);
                heights[dx + config.SampleRadius, dz + config.SampleRadius] = height;
            }
        }

        return heights;
    }

    private int FindSurfaceY(IBlockAccessor blockAccessor, int x, int z, IRandom random)
    {
        int randomY = random.NextInt(Math.Max(1, api.WorldManager.MapSizeY - 1));
        return FindSurfaceYAt(blockAccessor, x, z, randomY, random.NextInt(50) + 1);
    }

    private int FindSurfaceYAt(IBlockAccessor blockAccessor, int x, int z, int startY, int step)
    {
        int maxY = Math.Clamp(
            blockAccessor.GetRainMapHeightAt(new BlockPos(x, 0, z)),
            0,
            api.WorldManager.MapSizeY - 1
        );
        int y = Math.Clamp(startY, 0, maxY);
        BlockPos pos = new BlockPos(x, y, z);
        bool startAir = IsAir(blockAccessor, pos);

        if (!startAir && IsAirAbove(blockAccessor, pos)) return y;
        if (startAir && y > 0 && IsSolidBelow(blockAccessor, pos)) return y - 1;

        int low;
        int high;

        if (startAir)
        {
            high = y;
            low = Math.Max(0, y - step);
            pos.Y = low;
            if (IsAir(blockAccessor, pos))
            {
                low = 0;
                pos.Y = low;
                if (IsAir(blockAccessor, pos)) return -1;
            }
        }
        else
        {
            low = y;
            high = Math.Min(maxY, y + step);
            pos.Y = high;
            if (!IsAir(blockAccessor, pos))
            {
                high = maxY;
                pos.Y = high;
                if (!IsAir(blockAccessor, pos)) return -1;
            }
        }

        while (high - low > 1)
        {
            int mid = low + (high - low) / 2;
            pos.Y = mid;
            bool midAir = IsAir(blockAccessor, pos);
            if (!midAir && IsAirAbove(blockAccessor, pos)) return mid;
            if (midAir && IsSolidBelow(blockAccessor, pos)) return mid - 1;

            if (midAir) high = mid;
            else low = mid;
        }

        return low;
    }

    private static bool IsAirAbove(IBlockAccessor blockAccessor, BlockPos pos)
    {
        return pos.Y + 1 >= blockAccessor.MapSizeY
            || IsAir(blockAccessor, pos.AddCopy(0, 1, 0));
    }

    private static bool IsSolidBelow(IBlockAccessor blockAccessor, BlockPos pos)
    {
        return !IsAir(blockAccessor, pos.AddCopy(0, -1, 0));
    }

    private static bool IsAir(IBlockAccessor blockAccessor, BlockPos pos)
    {
        return IsAir(blockAccessor.GetBlock(pos, BlockLayersAccess.Solid));
    }

    private static bool IsAir(Block? block)
    {
        if (block == null || block.Id == 0) return true;
        if (block.BlockMaterial == EnumBlockMaterial.Snow) return true;
        if (block.BlockMaterial == EnumBlockMaterial.Leaves) return true;
        if (IsTreeTrunk(block)) return true;

        return block.BlockMaterial == EnumBlockMaterial.Plant
            && (block.CollisionBoxes == null || block.CollisionBoxes.Length == 0);
    }

    private static bool CanReplaceForStick(Block? block)
    {
        if (block == null || block.Id == 0) return true;
        if (block.BlockMaterial == EnumBlockMaterial.Snow) return true;

        string path = block.Code?.Path ?? "";
        return block.BlockMaterial == EnumBlockMaterial.Plant
            && path.Contains("grass", StringComparison.OrdinalIgnoreCase);
    }

    private int CountTreeBlocks(IBlockAccessor blockAccessor, int centerX, int centerZ, int[,] surfaceHeights)
    {
        int count = 0;
        BlockPos pos = new BlockPos(centerX, 0, centerZ);

        for (int dx = -config.SampleRadius; dx <= config.SampleRadius; dx++)
        {
            for (int dz = -config.SampleRadius; dz <= config.SampleRadius; dz++)
            {
                int surfaceY = surfaceHeights[dx + config.SampleRadius, dz + config.SampleRadius];
                if (surfaceY < 0) continue;

                pos.Set(centerX + dx, surfaceY + 1, centerZ + dz);
                if (!IsTreeTrunk(blockAccessor.GetBlock(pos, BlockLayersAccess.Solid))) continue;
                if (HasLeavesAbove(blockAccessor, pos)) count++;
            }
        }

        return count;
    }

    private static bool IsTreeTrunk(Block? block)
    {
        if (block == null || block.Attributes == null) return false;
        string? group = block.Attributes[TreeGroupAttribute]?.AsString();
        int spreadIndex = block.Attributes[TreeSpreadAttribute]?.AsInt(0) ?? 0;
        return !string.IsNullOrEmpty(group) && spreadIndex >= 2;
    }

    private static bool HasLeavesAbove(IBlockAccessor blockAccessor, BlockPos trunkPos)
    {
        BlockPos pos = trunkPos.AddCopy(0, 1, 0);
        for (int dy = 0; dy < 10; dy++)
        {
            for (int dx = -2; dx <= 2; dx++)
            {
                for (int dz = -2; dz <= 2; dz++)
                {
                    pos.Set(trunkPos.X + dx, trunkPos.Y + dy, trunkPos.Z + dz);
                    if (blockAccessor.GetBlock(pos, BlockLayersAccess.Solid)?.BlockMaterial == EnumBlockMaterial.Leaves) return true;
                }
            }
        }

        return false;
    }

    private int CountLooseSticks(IBlockAccessor blockAccessor, int centerX, int centerZ, int[,] surfaceHeights)
    {
        int count = 0;
        BlockPos pos = new BlockPos(centerX, 0, centerZ);

        for (int dx = -config.SampleRadius; dx <= config.SampleRadius; dx++)
        {
            for (int dz = -config.SampleRadius; dz <= config.SampleRadius; dz++)
            {
                int surfaceY = surfaceHeights[dx + config.SampleRadius, dz + config.SampleRadius];
                if (surfaceY < 0) continue;

                pos.Set(centerX + dx, surfaceY + 1, centerZ + dz);
                if (blockAccessor.GetBlock(pos, BlockLayersAccess.Solid).Code?.Path == "loosestick-free") count++;
            }
        }

        return count;
    }

    private bool TryPlaceStick(IBlockAccessor blockAccessor, IRandom random, int centerX, int centerZ, int[,] surfaceHeights)
    {
        BlockPos floorPos = new BlockPos(centerX, 0, centerZ);

        for (int attempt = 0; attempt < 12; attempt++)
        {
            int x = centerX + random.NextInt(2 * config.SampleRadius + 1) - config.SampleRadius;
            int z = centerZ + random.NextInt(2 * config.SampleRadius + 1) - config.SampleRadius;
            int surfaceY = surfaceHeights[
                x - centerX + config.SampleRadius,
                z - centerZ + config.SampleRadius
            ];
            if (surfaceY < 0) continue;

            floorPos.Set(x, surfaceY, z);
            Block floor = blockAccessor.GetBlock(floorPos, BlockLayersAccess.Solid);
            Block above = blockAccessor.GetBlock(floorPos.AddCopy(0, 1, 0), BlockLayersAccess.Solid);
            if (floor?.BlockMaterial != EnumBlockMaterial.Soil || !CanReplaceForStick(above)) continue;

            floorPos.Y++;
            blockAccessor.SetBlock(looseStickId, floorPos);
            if (blockAccessor.GetBlock(floorPos, BlockLayersAccess.Solid).Id == looseStickId)
            {
                api.Logger.Notification(
                    "Renewable Fallen Sticks: generated loosestick-free at {0}, {1}, {2} (chunk {3}, {4}; sample center {5}, {6}, {7}).",
                    floorPos.X,
                    floorPos.Y,
                    floorPos.Z,
                    floorPos.X / GlobalConstants.ChunkSize,
                    floorPos.Z / GlobalConstants.ChunkSize,
                    centerX,
                    surfaceHeights[config.SampleRadius, config.SampleRadius],
                    centerZ
                );
                return true;
            }

            api.Logger.Warning(
                "Renewable Fallen Sticks: failed to place loosestick-free at {0}, {1}, {2}.",
                floorPos.X,
                floorPos.Y,
                floorPos.Z
            );
        }

        return false;
    }

    private static RegrowthConfig LoadConfig(ICoreServerAPI api)
    {
        const string filename = "renewablefallensticks.json";
        try
        {
            RegrowthConfig? loaded = api.LoadModConfig<RegrowthConfig>(filename);
            if (loaded != null) return loaded;
        }
        catch (Exception e)
        {
            api.Logger.Warning("Could not load {0}: {1}. Using defaults.", filename, e.Message);
        }

        var defaults = new RegrowthConfig();
        api.StoreModConfig(defaults, filename);
        return defaults;
    }

    private static double ReadLastCheck(IWorldChunk chunk)
    {
        byte[]? data = chunk.GetModdata(ModdataKey);
        return data == null || data.Length != sizeof(double)
            ? double.NegativeInfinity
            : BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(data));
    }

    private static void WriteLastCheck(IWorldChunk chunk, double value)
    {
        byte[] data = new byte[sizeof(double)];
        BinaryPrimitives.WriteInt64LittleEndian(data, BitConverter.DoubleToInt64Bits(value));
        chunk.SetModdata(ModdataKey, data);
    }

    private static long ChunkKey(int chunkX, int chunkZ)
    {
        return ((long)chunkX << 32) ^ (uint)chunkZ;
    }

    private sealed class LoadedColumn
    {
        public readonly int ChunkX;
        public readonly int ChunkZ;
        public readonly IWorldChunk Chunk;

        public LoadedColumn(int chunkX, int chunkZ, IWorldChunk chunk)
        {
            ChunkX = chunkX;
            ChunkZ = chunkZ;
            Chunk = chunk;
        }
    }
}
