// A unity urp implantation of "The Power of Box Filters: Real-time Approximation to Large Convolution Kernel by Box-filtered Image Pyramid".
// https://dl.acm.org/doi/pdf/10.1145/3355088.3365143
using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Sif
{
public class MipmapBlur : ScriptableRendererFeature
{
    [Serializable]
    public class Settings
    {
        public string passTag = "Mipmap Blur";
        public RenderPassEvent @event = RenderPassEvent.BeforeRenderingPostProcessing;
        public Material blurMaterial;
        [Range(0,50)]
        public int blurLevel = 25;
    }
    
    public Settings settings = new Settings();
    private MipmapBlurPass m_Pass;

    public override void Create()
    {
        m_Pass = new MipmapBlurPass
        (
            settings.passTag,
            settings.blurMaterial,
            settings.@event,
            settings.blurLevel
        );
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        m_Pass.Setup(renderer.cameraColorTarget);
        renderer.EnqueuePass(m_Pass);
    }
}

public class MipmapBlurPass : ScriptableRenderPass
{
    public static class PID
    {
        public static readonly int _TextureWithMips = Shader.PropertyToID("_TextureWithMips");
        public static readonly int _MipCount = Shader.PropertyToID("_MipCount");
        public static readonly int _CurMipLevel = Shader.PropertyToID("_CurMipLevel");
        public static readonly int _BlurLevel = Shader.PropertyToID("_BlurLevel");
        public static readonly int _TempMipTexture = Shader.PropertyToID("_TempMipTexture");
    }
    
    private readonly Material m_BlurMaterial;
    private readonly ProfilingSampler m_ProfilingSampler;
    private readonly int m_BlurLevel;
    private RenderTargetIdentifier m_CameraColorBuffer;
    private RenderTextureDescriptor m_CameraBufferDescriptor;
    private int[] m_MipIDs;
    private int m_MipCount; // exclude mip0;

    private Vector2Int m_LastFrameScreenSize;

    public MipmapBlurPass(string passTag, Material blurMaterial, RenderPassEvent evt, int blurLevel)
    {
        m_ProfilingSampler = new ProfilingSampler(passTag);
        m_BlurMaterial = blurMaterial;
        renderPassEvent = evt;
        m_BlurLevel = blurLevel;
    }

    public void Setup(RenderTargetIdentifier source)
    {
        m_CameraColorBuffer = source;
    }

    public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
    {
        m_CameraBufferDescriptor = cameraTextureDescriptor;
        var desc = cameraTextureDescriptor;
        desc.useMipMap = true;
        desc.autoGenerateMips = true;
        cmd.GetTemporaryRT(PID._TextureWithMips, desc);
        cmd.Blit(m_CameraColorBuffer, PID._TextureWithMips);

        CheckScreenResize();
    }

    public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        var cmd = CommandBufferPool.Get(m_ProfilingSampler.name);
        //using (new ProfilingScope(cmd, m_ProfilingSampler))
        {
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            var maxRect = m_LastFrameScreenSize;
            cmd.SetGlobalInt(PID._MipCount, m_MipCount);
            for (var i = m_MipCount; i >= 0; i--)
            {
                var denominator = Mathf.Pow(2, i);
                var x = Mathf.Max(Mathf.FloorToInt(maxRect.x / denominator), 1);
                var y = Mathf.Max(Mathf.FloorToInt(maxRect.y / denominator), 1);
                var rect = new Vector2Int( x, y);
                // Prepare for manual blit
                cmd.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
                cmd.SetViewport(new Rect(0, 0, rect.x, rect.y));
                cmd.SetGlobalInt(PID._CurMipLevel, i);
                cmd.SetGlobalInt(PID._BlurLevel, m_BlurLevel);
                cmd.SetGlobalTexture(PID._TextureWithMips, PID._TextureWithMips);
                if (i == 0)
                {
                    cmd.SetViewport(new Rect(0, 0, m_CameraBufferDescriptor.width, m_CameraBufferDescriptor.height));
                    cmd.SetRenderTarget(m_CameraColorBuffer);
                    cmd.SetGlobalTexture(PID._TempMipTexture, m_MipIDs[i]);
                }
                else
                {
                    cmd.GetTemporaryRT(m_MipIDs[i - 1], rect.x, rect.y, 0, FilterMode.Bilinear,
                        m_CameraBufferDescriptor.colorFormat);
                    cmd.SetRenderTarget(m_MipIDs[i - 1]);
                    cmd.SetGlobalTexture(PID._TempMipTexture, i == m_MipCount ? PID._TextureWithMips : m_MipIDs[i]);
                }

                cmd.DrawMesh(RenderingUtils.fullscreenMesh, Matrix4x4.identity, m_BlurMaterial, 0, 0);
            }

            var camera = renderingData.cameraData.camera;
            cmd.SetViewProjectionMatrices(camera.worldToCameraMatrix, camera.projectionMatrix);
        }

        context.ExecuteCommandBuffer(cmd);
        CommandBufferPool.Release(cmd);
    }
    
    private void CheckScreenResize()
    {
        if ((m_CameraBufferDescriptor.width != m_LastFrameScreenSize.x) ||
            (m_CameraBufferDescriptor.height != m_LastFrameScreenSize.y))
        {
            m_LastFrameScreenSize = new Vector2Int(m_CameraBufferDescriptor.width, m_CameraBufferDescriptor.height);

            var maxLen = Mathf.Max(m_LastFrameScreenSize.x, m_LastFrameScreenSize.y);
            m_MipCount = Mathf.FloorToInt(Mathf.Log(maxLen, 2));
            m_MipIDs = new int[m_MipCount];
            
            for (var i = 0; i < m_MipCount; i++)
            {
                m_MipIDs[i] = Shader.PropertyToID("_TempMip" + (1 + i).ToString());
            }
        }
    }

    public override void FrameCleanup(CommandBuffer cmd)
    {
        cmd.ReleaseTemporaryRT(PID._TextureWithMips);
        for (var i = 0; i < m_MipCount; i++)
        {
            cmd.ReleaseTemporaryRT(m_MipIDs[i]);
        }
    }
}
}
