using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using static UnityEngine.GraphicsBuffer;
using UnityEngine.XR;

public struct WorldSettings
{
    public int chunkSize;
    public int2 noChunks;
    public int width;
    public int height;
    public uint tick;
}

[BurstCompile]
public struct BlockUpdateJob : IJobParallelFor
{
    [NativeDisableContainerSafetyRestriction]
    public NativeArray<BlockData> blockDataArray;

    public bool2 result;
    public WorldSettings settings;

    public void Execute(int i)
    {
        byte id = blockDataArray[i].id;

        if (id != (byte)Blocks.Human &&
            id != (byte)Blocks.Predator &&
            id != (byte)Blocks.Dirt)
            return;

        int chunkArea = settings.chunkSize * settings.chunkSize;
        int chunkIndex = i / chunkArea;
        int chunkX = chunkIndex % settings.noChunks.x;
        int chunkY = chunkIndex / settings.noChunks.x;

        if (((chunkX & 1) == 0) == result.x &&
            ((chunkY & 1) == 0) == result.y)
        {
            if (settings.tick != blockDataArray[i].lastMovedTick)
            {
                blockDataArray[i].Update(ref blockDataArray, in settings, i);
            }
        }
    }
}

public struct BlockData
{
    public byte id;
    public int2 pos;
    public int2 posLast;
    public uint lastMovedTick;

    // Life-sim stats
    public byte energy; // 0..255
    public byte social; // 0..255
    public byte age;    // 0..255

    public BlockData(byte id, int2 pos)
    {
        this.id = id;
        this.pos = pos;
        this.posLast = pos;
        this.lastMovedTick = 0;

        this.energy = 128;
        this.social = 0;
        this.age = 0;
    }

    public void Update(ref NativeArray<BlockData> arr, in WorldSettings s, int selfIndex)
    {
        var rng = MakeRng(in s, pos, selfIndex);

        if (id == (byte)Blocks.Human)
        {
            HumanUpdate(ref arr, in s, ref rng, selfIndex);
            return;
        }

        if (id == (byte)Blocks.Predator)
        {
            PredatorUpdate(ref arr, in s, ref rng, selfIndex);
            return;
        }

        if (id == (byte)Blocks.Dirt)
        {
            DirtUpdate(ref arr, in s, selfIndex);
            return;
        }
    }

    void DirtUpdate(ref NativeArray<BlockData> arr, in WorldSettings s, int selfIndex)
    {
        // Slow it down: only attempt growth once every N ticks
        // (prevents instant grass explosion)
        const uint growthInterval = 1;
        if ((s.tick + (uint)selfIndex) % growthInterval != 0)
            return;

        // Deterministic per-cell randomness
        uint seed =
            (uint)(pos.x * 73856093) ^
            (uint)(pos.y * 19349663) ^
            (uint)(s.tick * 83492791);

        // 10% chance
        if ((seed & 0xFF) < 26) // 26 / 256 ≈ 10%
        {
            // Optional rule: don't grow if water nearby
            if (HasNeighborOfType(ref arr, in s, pos, (byte)Blocks.Water))
                return;

            // Optional rule: prefer growing near grass
            int grassCount = CountNeighborsOfType(ref arr, in s, pos, (byte)Blocks.Grass);
            if (grassCount == 0)
                return;

            WriteId(ref arr, in s, pos, (byte)Blocks.Grass);
        }
    }

    void HumanUpdate(ref NativeArray<BlockData> arr, in WorldSettings s, ref Random rng, int selfIndex)
    {
        age++;
        if (energy > 0) energy--;

        if (energy == 0)
        {
            WriteId(ref arr, in s, pos, (byte)Blocks.Grass);
            return;
        }

        bool nearHuman = HasNeighborOfType(ref arr, in s, pos, (byte)Blocks.Human);
        if (nearHuman) social = (byte)math.min(255, social + 1);
        else social = (byte)(social > 0 ? social - 1 : 0);

        // Move: prefer grass then empty
        int2 target;
        byte destId;
        if (TryPickMove(ref arr, in s, ref rng, preferId: (byte)Blocks.Grass, fallbackId: (byte)Blocks.Dirt, out target, out destId))
        {
            // Move by writing (keeps grass from "swapping into" old cell)
            int2 old = pos;

            WriteId(ref arr, in s, old, (byte)Blocks.Dirt);

            var moved = this;
            moved.posLast = old;
            moved.pos = target;
            moved.lastMovedTick = s.tick;

            // Eat if grass
            if (destId == (byte)Blocks.Grass)
            {
                moved.energy = (byte)math.min(255, moved.energy + 20);
            }

            WriteBlock(ref arr, in s, target, moved);
        }
        else
        {
            // still update stats even if not moving
            WriteBlock(ref arr, in s, pos, this);
        }

        const uint growthInterval = 30;
        if ((s.tick + (uint)selfIndex) % growthInterval != 0)
            return;

        // Breed rule (tweak numbers)
        if (energy >= 150 || social >= 200)
        {
            if (TrySpawn(ref arr, in s, ref rng, (byte)Blocks.Human, out _))
            {
                // cost to parent
                byte cost = 60;
                energy = (byte)math.max(0, energy - cost);
                social = 0;
            }
        }
    }


    void PredatorUpdate(ref NativeArray<BlockData> arr, in WorldSettings s, ref Random rng, int selfIndex)
    {
        age++;
        if (energy > 0) energy--;

        if (energy == 0)
        {
            WriteId(ref arr, in s, pos, (byte)Blocks.Grass);
            return;
        }

        // Attack if any adjacent human
        int2 victim;
        if (TryFindNeighborOfType(ref arr, in s, pos, ref rng, (byte)Blocks.Human, out victim))
        {
            int2 old = pos;

            // kill victim (overwrite), move predator there
            WriteId(ref arr, in s, old, (byte)Blocks.Grass);

            var moved = this;
            moved.posLast = old;
            moved.pos = victim;
            moved.lastMovedTick = s.tick;
            moved.energy = (byte)math.min(255, moved.energy + 90);

            WriteBlock(ref arr, in s, victim, moved);
            return;
        }

        // Roam: choose empty neighbor with best "human nearby" score
        int start = rng.NextInt(0, 8);
        int bestScore = -1;
        int2 best = pos;
        bool found = false;

        for (int t = 0; t < 8; t++)
        {
            int k = (start + t) & 7;
            int2 np = pos + NeighborOffset(k);
            if (!InBounds(np, in s)) continue;
            if (np.Equals(posLast)) continue;

            byte dest = arr[GetPosInBlockArray(np, in s)].id;
            if (dest == (byte)Blocks.Predator) continue;

            int score = CountNeighborsOfType(ref arr, in s, np, (byte)Blocks.Human);
            if (score > bestScore)
            {
                bestScore = score;
                best = np;
                found = true;
            }

            if(!found)
            {
                best = np;
            }
        }

        if (!best.Equals(pos))
        {
            int2 old = pos;

            WriteId(ref arr, in s, old, (byte)Blocks.Dirt);

            var moved = this;
            moved.posLast = old;
            moved.pos = best;
            moved.lastMovedTick = s.tick;

            WriteBlock(ref arr, in s, best, moved);
        }
        else
        {
            // write back stats (energy/age changes)
            WriteBlock(ref arr, in s, pos, this);
        }

        const uint growthInterval = 60;
        if ((s.tick + (uint)selfIndex) % growthInterval != 0)
            return;

        // Optional: breed if very high energy
        if (energy >= 120)
        {
            if (TrySpawn(ref arr, in s, ref rng, (byte)Blocks.Predator, out _))
            {
                energy = (byte)math.max(0, energy - 60);
            }
        }
    }

    bool TryPickMove(ref NativeArray<BlockData> arr, in WorldSettings s, ref Random rng,
        byte preferId, byte fallbackId,
        out int2 chosen, out byte destId)
    {
        // Pass 1: preferred (grass)
        if (TryPickNeighborOfType(ref arr, in s, ref rng, preferId, out chosen))
        {
            destId = preferId;
            return true;
        }

        // Pass 2: fallback (empty)
        if (TryPickNeighborOfType(ref arr, in s, ref rng, fallbackId, out chosen))
        {
            destId = fallbackId;
            return true;
        }

        chosen = default;
        destId = 0;
        return false;
    }

    bool TryPickNeighborOfType(ref NativeArray<BlockData> arr, in WorldSettings s, ref Random rng, byte wantedId, out int2 chosen)
    {
        int start = rng.NextInt(0, 8);

        // First try to avoid going back to posLast
        for (int t = 0; t < 8; t++)
        {
            int k = (start + t) & 7;
            int2 np = pos + NeighborOffset(k);
            if (!InBounds(np, in s)) continue;
            if (np.Equals(posLast)) continue;

            if (arr[GetPosInBlockArray(np, in s)].id == wantedId)
            {
                chosen = np;
                return true;
            }
        }

        // If nothing, allow posLast too
        for (int t = 0; t < 8; t++)
        {
            int k = (start + t) & 7;
            int2 np = pos + NeighborOffset(k);
            if (!InBounds(np, in s)) continue;

            if (arr[GetPosInBlockArray(np, in s)].id == wantedId)
            {
                chosen = np;
                return true;
            }
        }

        chosen = default;
        return false;
    }

    bool TrySpawn(ref NativeArray<BlockData> arr, in WorldSettings s, ref Random rng, byte spawnId, out int2 spawnPos)
    {
        // spawn into an empty adjacent cell
        int start = rng.NextInt(0, 8);

        for (int t = 0; t < 8; t++)
        {
            int k = (start + t) & 7;
            int2 np = pos + NeighborOffset(k);
            if (!InBounds(np, in s)) continue;

            int idx = GetPosInBlockArray(np, in s);
            if (arr[idx].id != (byte)Blocks.Grass) continue;

            var baby = new BlockData(spawnId, np);
            baby.lastMovedTick = s.tick; // newborn won't move this tick
            baby.energy = (spawnId == (byte)Blocks.Predator) ? (byte)90 : (byte)80;

            arr[idx] = baby;
            spawnPos = np;
            return true;
        }

        spawnPos = default;
        return false;
    }

    static bool HasNeighborOfType(ref NativeArray<BlockData> arr, in WorldSettings s, int2 center, byte wantedId)
    {
        for (int n = 0; n < 8; n++)
        {
            int2 np = center + NeighborOffset(n);
            if (!InBounds(np, in s)) continue;
            if (arr[GetPosInBlockArray(np, in s)].id == wantedId) return true;
        }
        return false;
    }

    static int CountNeighborsOfType(ref NativeArray<BlockData> arr, in WorldSettings s, int2 center, byte wantedId)
    {
        int c = 0;
        for (int n = 0; n < 8; n++)
        {
            int2 np = center + NeighborOffset(n);
            if (!InBounds(np, in s)) continue;
            if (arr[GetPosInBlockArray(np, in s)].id == wantedId) c++;
        }
        return c;
    }

    static bool TryFindNeighborOfType(ref NativeArray<BlockData> arr, in WorldSettings s, int2 center, ref Random rng, byte wantedId, out int2 found)
    {
        int start = rng.NextInt(0, 8);
        for (int t = 0; t < 8; t++)
        {
            int k = (start + t) & 7;
            int2 np = center + NeighborOffset(k);
            if (!InBounds(np, in s)) continue;

            if (arr[GetPosInBlockArray(np, in s)].id == wantedId)
            {
                found = np;
                return true;
            }
        }

        found = default;
        return false;
    }

    static void WriteId(ref NativeArray<BlockData> arr, in WorldSettings s, int2 p, byte id)
    {
        int idx = GetPosInBlockArray(p, in s);
        var b = arr[idx];
        b.id = id;
        b.pos = p;
        b.posLast = p;
        b.lastMovedTick = s.tick;
        b.energy = 0;
        b.social = 0;
        b.age = 0;
        arr[idx] = b;
    }

    static void WriteBlock(ref NativeArray<BlockData> arr, in WorldSettings s, int2 p, BlockData b)
    {
        // Ensure coordinates are consistent
        b.pos = p;
        arr[GetPosInBlockArray(p, in s)] = b;
    }

    static int2 NeighborOffset(int k)
    {
        switch (k & 7)
        {
            default: return new int2(-1, -1);
            case 1: return new int2(0, -1);
            case 2: return new int2(1, -1);
            case 3: return new int2(-1, 0);
            case 4: return new int2(1, 0);
            case 5: return new int2(-1, 1);
            case 6: return new int2(0, 1);
            case 7: return new int2(1, 1);
        }
    }

    static bool InBounds(int2 p, in WorldSettings s)
    {
        return (uint)p.x < (uint)s.width && (uint)p.y < (uint)s.height;
    }

    static Random MakeRng(in WorldSettings s, int2 p, int i)
    {
        uint seed =
            (uint)(s.tick * 747796405u) ^
            (uint)(p.x * 73856093) ^
            (uint)(p.y * 19349663) ^
            (uint)(i * 83492791);

        if (seed == 0) seed = 1u;
        return new Random(seed);
    }

    // -------------------------
    // Your indexing helpers (kept)
    // -------------------------
    public static int GetPosInBlockArray(int2 pos, in WorldSettings s)
    {
        int cs = s.chunkSize;
        int2 chunk = new int2(pos.x / cs, pos.y / cs);
        int inChunk = (pos.y - chunk.y * cs) * cs + (pos.x - chunk.x * cs);
        return (chunk.y * s.noChunks.x + chunk.x) * (cs * cs) + inChunk;
    }
}