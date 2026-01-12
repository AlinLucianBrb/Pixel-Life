using Unity.Mathematics;

public struct ChunkData
{
    public int2 chunkPos;
    public int chunkQueue;

    public ChunkData(int2 chunkPos)
    {
        this.chunkPos = chunkPos;
        this.chunkQueue = this.chunkPos.y * World.instance.noChunks.x + this.chunkPos.x;
    }
}

