using System.Collections;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using System;

[BurstCompile]
public struct RenderingUpdateJob : IJobParallelFor
{
    [NativeDisableContainerSafetyRestriction]
    public NativeArray<uint> texture;
    [NativeDisableContainerSafetyRestriction]
    public NativeArray<BlockData> blockDataArray;

    public int chunkSize;
    public int2 noChunks;

    public void Execute(int i)
    {
        int chunk = (i / (chunkSize * chunkSize));
        int chunkPos = i - (chunkSize * chunkSize) * chunk;
        int x = chunkPos % chunkSize;
        int y = chunkPos / chunkSize;

        x = (chunk % noChunks.x) * chunkSize + x;
        y = (chunk / noChunks.x) * chunkSize + y;

        int arrayPos = y * chunkSize * noChunks.x + x;

        texture[arrayPos] = blockDataArray[i].id;
    }
}

public class Rendering : MonoBehaviour
{
    NativeArray<uint> textureNativeArray;
    ComputeBuffer textureBuffer;

    public ComputeShader computeShader;
    RenderTexture renderTexture;

    int width, height;
    int kernel;

    void Start()
    {
        width = World.instance.chunkSize * World.instance.noChunks.x;
        height = World.instance.chunkSize * World.instance.noChunks.y;

        kernel = computeShader.FindKernel("CSMain");

        textureNativeArray = new NativeArray<uint>(World.instance.blocksData.Length, Allocator.Persistent);
        textureBuffer = new ComputeBuffer(textureNativeArray.Length, sizeof(uint) * 4);
    }

    void Update()
    {
        if (GameManager.gameState == GameManager.GameState.Pause)
            return;

        RenderingUpdateJob renderingUpdateJob = new RenderingUpdateJob()
        {
            blockDataArray = World.instance.blocksData,
            texture = textureNativeArray,
            chunkSize = World.instance.chunkSize,
            noChunks = World.instance.noChunks
        };

        JobHandle jobHandle = renderingUpdateJob.Schedule(World.instance.blocksData.Length, 128);
        jobHandle.Complete();       
    }

    private void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        if (renderTexture == null)
        {
            renderTexture = new RenderTexture(World.instance.chunkSize * World.instance.noChunks.x, World.instance.chunkSize * World.instance.noChunks.y, 1);
            renderTexture.enableRandomWrite = true;
            renderTexture.filterMode = FilterMode.Point;
            renderTexture.Create();
        }
     
        textureBuffer.SetData(textureNativeArray);

        computeShader.SetInt("Width", width);
        computeShader.SetBuffer(kernel, "Ids", textureBuffer);
        computeShader.SetTexture(kernel, "Result", renderTexture);

        // Must dispatch in 2D
        int tx = 8, ty = 8; // must match numthreads in shader
        int groupsX = (width + tx - 1) / tx;
        int groupsY = (height + ty - 1) / ty;

        computeShader.Dispatch(kernel, groupsX, groupsY, 1);

        Graphics.Blit(renderTexture, destination);
    }

    private void OnDestroy()
    {
        textureNativeArray.Dispose();
        textureBuffer.Dispose();
    }
}
