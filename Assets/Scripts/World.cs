using UnityEngine;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Jobs;
using System.Collections.Generic;

public enum Blocks
{
    Dirt,
    Grass,
    Water,
    Human,
    Predator,
    COUNT,
    INVALID,
}

[System.Serializable]
public class BlockWeight
{
    public Blocks block; [Min(0f)] public float weight = 1f; // can be 70, 30, 20 etc.
}

public class World : Singleton<World>
{
    public List<BlockWeight> distribution = new List<BlockWeight>();

    public int chunkSize = 16;
    public int2 noChunks = new int2(64,36);
    public WorldSettings worldSettings = new WorldSettings();

    public NativeArray<BlockData> blocksData;
    public float humanSpawnChance;
    public float predatorSpawnChance;

    public const int SIM_TICKS_PER_SECOND = 30;
    float simAccumulator;
    const float SIM_DT = 1f / SIM_TICKS_PER_SECOND;
    private uint tick;

    BlockPainter painter;

    public override void Awake()
    {
        painter = new BlockPainter();

        worldSettings.chunkSize = chunkSize; 
        worldSettings.noChunks = noChunks;
        worldSettings.width = noChunks.x * chunkSize;
        worldSettings.height = noChunks.y * chunkSize;

        blocksData = new NativeArray<BlockData>(chunkSize * noChunks.y * chunkSize * noChunks.x, Allocator.Persistent);

        UnityEngine.Random.InitState(System.Environment.TickCount);
        float offsetX = UnityEngine.Random.Range(-1000f, 1000f);
        float offsetY = UnityEngine.Random.Range(-1000f, 1000f);
        float scale = UnityEngine.Random.Range(0.001f, 0.01f);

        int curPos = 0;
        for (int j = 0; j < noChunks.y; j++)
        {
            for (int i = 0; i < noChunks.x; i++)
            {
                for (int y = 0; y < chunkSize; y++)
                {
                    for (int x = 0; x < chunkSize; x++)
                    {
                        int worldX = i * chunkSize + x;
                        int worldY = j * chunkSize + y;

                        float perlinNoise = Mathf.PerlinNoise(worldX * scale + offsetX, worldY * scale + offsetY);
                        Blocks chosen = PickBlock(perlinNoise);
                        
                        if (chosen != Blocks.Water)
                        {
                            float h = Hash01(worldX, worldY, 12345);
                            float p = Hash01(worldX, worldY, 54321);

                            if (h < humanSpawnChance)
                            {
                                chosen = Blocks.Human;
                            }
                            else if (p < predatorSpawnChance)
                            {
                                chosen = Blocks.Predator;
                            }
                        }

                        blocksData[curPos] = new BlockData((byte)chosen, new int2(worldX, worldY));

                        curPos++;
                    }
                }
            }
        }
    }

    static float Hash01(int x, int y, uint seed)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393) + (uint)(y * 668265263) + seed * 2654435761u;
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= (h >> 16);
            return (h & 0x00FFFFFF) / 16777215f; // 0..1
        }
    }

    private Blocks PickBlock(float value01)
    {
        // clamp just in case
        value01 = Mathf.Clamp01(value01);

        float totalWeight = 0f;
        for (int i = 0; i < distribution.Count; i++)
            totalWeight += distribution[i].weight;

        float cumulative = 0f;

        for (int i = 0; i < distribution.Count; i++)
        {
            float normalized = distribution[i].weight / totalWeight;
            cumulative += normalized;

            if (value01 <= cumulative)
                return distribution[i].block;
        }

        // safety fallback
        return distribution[^1].block;
    }


    private void Update()
    {
        if (GameManager.gameState == GameManager.GameState.Pause)
            return;

        bool2 result;

        bool2[] test = { new bool2(true, true), new bool2(true, false), new bool2(false, true), new bool2(false, false) };

        painter.Paint(worldSettings);

        simAccumulator += Time.deltaTime;

        while (simAccumulator >= SIM_DT)
        {
            simAccumulator -= SIM_DT;

            tick++;
            worldSettings.tick = tick;

            for (int i = 0; i < 4; i++)
            {
                result = test[i];

                BlockUpdateJob blockUpdateJob = new BlockUpdateJob()
                {
                    blockDataArray = blocksData,
                    result = result,
                    settings = worldSettings
                };

                JobHandle jobHandle = blockUpdateJob.Schedule(blocksData.Length, chunkSize * chunkSize);
                jobHandle.Complete();
            }
        }
    }

    private void OnDestroy()
    {
        blocksData.Dispose();
    }
}
