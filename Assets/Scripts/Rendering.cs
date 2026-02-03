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

    // Multi-pass RTs
    RenderTexture baseRT;
    RenderTexture overlayRT;      // mask output from Shade ("SmokeOut")
    RenderTexture overlayTempRT;
    RenderTexture overlayBlurRT;  // blurred overlay
    RenderTexture waterRT;
    RenderTexture finalRT;

    int width, height;

    // Kernels
    int kShade;
    int kBlurH;
    int kBlurV;
    int kComposite;

    const int TX = 8;
    const int TY = 8;

    // --- Project-specific ID mapping (set these to match PixelLife)
    [Header("PixelLife ID Mapping")]
    public int emptyId = 4;        // was black in the old shader; now treated as sky/air
    public int waterId = 2;        // was blue in the old shader

    [Header("Optional blurred overlay")]
    [Tooltip("Set to -1 to disable. If you have a 'smoke/cloud/shadow' material ID, set it here.")]
    public int overlayId = -1;

    [Tooltip("Tint applied to the blurred overlay mask.")]
    public Color overlayTint = new Color(0.10f, 0.11f, 0.14f, 1f);

    [Range(0f, 2f)]
    public float overlayStrength = 1.0f;

    [Range(1f, 3f)]
    public float overlayGamma = 1.1f;

    void Start()
    {
        width = World.instance.chunkSize * World.instance.noChunks.x;
        height = World.instance.chunkSize * World.instance.noChunks.y;

        // Find kernels by name (PixelLifeCorrectShader.compute must have these)
        kShade = computeShader.FindKernel("Shade");
        kBlurH = computeShader.FindKernel("BlurH");
        kBlurV = computeShader.FindKernel("BlurV");
        kComposite = computeShader.FindKernel("Composite");

        textureNativeArray = new NativeArray<uint>(World.instance.blocksData.Length, Allocator.Persistent);

        // IMPORTANT: stride is sizeof(uint) (4 bytes)
        textureBuffer = new ComputeBuffer(textureNativeArray.Length, sizeof(uint));
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

    void EnsureRT(ref RenderTexture rt, int w, int h, RenderTextureFormat format, FilterMode filter)
    {
        if (rt != null && rt.width == w && rt.height == h && rt.format == format)
            return;

        if (rt != null) rt.Release();

        rt = new RenderTexture(w, h, 0, format);
        rt.enableRandomWrite = true;
        rt.filterMode = filter;
        rt.wrapMode = TextureWrapMode.Clamp;
        rt.Create();
    }

    private void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        int w = width;
        int h = height;

        // Allocate RTs
        EnsureRT(ref baseRT, w, h, RenderTextureFormat.ARGB32, FilterMode.Point);
        EnsureRT(ref finalRT, w, h, RenderTextureFormat.ARGB32, FilterMode.Bilinear);

        EnsureRT(ref overlayRT, w, h, RenderTextureFormat.RFloat, FilterMode.Point);
        EnsureRT(ref overlayTempRT, w, h, RenderTextureFormat.RFloat, FilterMode.Point);
        EnsureRT(ref overlayBlurRT, w, h, RenderTextureFormat.RFloat, FilterMode.Point);
        EnsureRT(ref waterRT, w, h, RenderTextureFormat.RFloat, FilterMode.Point);

        // Upload ids
        textureBuffer.SetData(textureNativeArray);

        int groupsX = (w + TX - 1) / TX;
        int groupsY = (h + TY - 1) / TY;

        // Common uniforms
        computeShader.SetInt("Width", w);
        computeShader.SetInt("Height", h);
        computeShader.SetFloat("Time", Time.time);

        // ID mapping
        computeShader.SetInt("EmptyId", emptyId);
        computeShader.SetInt("WaterId", waterId);
        computeShader.SetInt("SmokeId", overlayId);

        // Overlay params
        computeShader.SetVector("SmokeTint", new Vector3(overlayTint.r, overlayTint.g, overlayTint.b));
        computeShader.SetFloat("SmokeStrength", overlayStrength);
        computeShader.SetFloat("SmokeGamma", overlayGamma);

        // -------------------------
        // Pass A: Shade -> baseRT + overlayRT + waterRT
        // -------------------------
        computeShader.SetBuffer(kShade, "Ids", textureBuffer);
        computeShader.SetTexture(kShade, "BaseOut", baseRT);
        computeShader.SetTexture(kShade, "SmokeOut", overlayRT);
        computeShader.SetTexture(kShade, "WaterOut", waterRT);
        computeShader.Dispatch(kShade, groupsX, groupsY, 1);

        // -------------------------
        // Pass B/C: Blur overlay if enabled
        // -------------------------
        if (overlayId >= 0)
        {
            computeShader.SetTexture(kBlurH, "InTex", overlayRT);
            computeShader.SetTexture(kBlurH, "OutTex", overlayTempRT);
            computeShader.Dispatch(kBlurH, groupsX, groupsY, 1);

            computeShader.SetTexture(kBlurV, "InTex", overlayTempRT);
            computeShader.SetTexture(kBlurV, "OutTex", overlayBlurRT);
            computeShader.Dispatch(kBlurV, groupsX, groupsY, 1);
        }
        else
        {
            // If disabled, just copy overlayRT -> overlayBlurRT (keeps bindings simple)
            Graphics.Blit(overlayRT, overlayBlurRT);
        }

        // -------------------------
        // Pass D: Composite -> finalRT
        // -------------------------
        computeShader.SetTexture(kComposite, "BaseIn", baseRT);
        computeShader.SetTexture(kComposite, "SmokeBlur", overlayBlurRT);
        computeShader.SetTexture(kComposite, "WaterMask", waterRT);
        computeShader.SetTexture(kComposite, "Result", finalRT);
        computeShader.Dispatch(kComposite, groupsX, groupsY, 1);

        Graphics.Blit(finalRT, destination);
    }

    private void OnDestroy()
    {
        if (textureNativeArray.IsCreated) textureNativeArray.Dispose();
        if (textureBuffer != null) textureBuffer.Dispose();

        if (baseRT != null) baseRT.Release();
        if (overlayRT != null) overlayRT.Release();
        if (overlayTempRT != null) overlayTempRT.Release();
        if (overlayBlurRT != null) overlayBlurRT.Release();
        if (waterRT != null) waterRT.Release();
        if (finalRT != null) finalRT.Release();
    }
}
