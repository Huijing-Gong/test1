using System;
using UnityEngine;

namespace SamProduction
{
    // SubmitRenderRequest uses destination.antiAliasing, not just the URP asset setting.
    // Resolve multisampled output explicitly before CPU PNG readback.
    public sealed class SamFrameReadback : IDisposable
    {
        public static int Samples
        {
            get
            {
                var args=Environment.GetCommandLineArgs();
                int index=Array.IndexOf(args,"--sam-capture-msaa");
                if(index<0)return 4;
                if(index+1>=args.Length || !int.TryParse(args[index+1],out int value) || (value!=1 && value!=4))
                    throw new ArgumentException("--sam-capture-msaa requires 1 or 4.");
                return value;
            }
        }
        private readonly RenderTexture target, resolved;
        public SamFrameReadback(RenderTexture source)
        {
            target=source;
            if(!target.IsCreated() || target.antiAliasing!=Samples)
                throw new InvalidOperationException("Capture target does not have the requested MSAA sample count.");
            resolved=new RenderTexture(target.width,target.height,0,target.format,RenderTextureReadWrite.sRGB)
                {name="SAM explicit capture resolve",antiAliasing=1};
            if(!resolved.Create())throw new InvalidOperationException("Unable to create capture resolve target.");
        }
        public void Read(Texture2D texture)
        {
            if(target.antiAliasing>1)target.ResolveAntiAliasedSurface(resolved);
            else Graphics.Blit(target,resolved);
            var previous=RenderTexture.active;
            try
            {
                RenderTexture.active=resolved;
                texture.ReadPixels(new Rect(0,0,target.width,target.height),0,0);
                texture.Apply();
            }
            finally { RenderTexture.active=previous; }
        }
        public void Dispose()
        { resolved.Release();UnityEngine.Object.Destroy(resolved); }
    }
}
