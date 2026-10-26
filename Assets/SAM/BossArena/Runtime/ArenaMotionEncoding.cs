using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEngine;
namespace SamProduction.BossArena
{
    // Storage encoding only. Decoded bytes retain the original authored JSON and its SHA.
    public static class ArenaMotionEncoding
    {
        public static byte[] SourceBytes(TextAsset asset)
        {
            if(!asset)throw new ArgumentNullException(nameof(asset));
            byte[] data=asset.bytes;
            if(data.Length<2||data[0]!=0x1f||data[1]!=0x8b)return data;
            using(var input=new MemoryStream(data))
            using(var gzip=new GZipStream(input,CompressionMode.Decompress))
            using(var output=new MemoryStream()){gzip.CopyTo(output);return output.ToArray();}
        }
        public static string Text(TextAsset asset)
        {
            if(!asset)throw new ArgumentNullException(nameof(asset));
            byte[] data=asset.bytes;
            if(data.Length<2||data[0]!=0x1f||data[1]!=0x8b)return asset.text;
            return Encoding.UTF8.GetString(SourceBytes(asset)).TrimStart('\ufeff');
        }
#if UNITY_EDITOR
        public static byte[] Pack(byte[] source)
        {
            using(var output=new MemoryStream())
            {using(var gzip=new GZipStream(output,System.IO.Compression.CompressionLevel.Optimal,true))gzip.Write(source,0,source.Length);return output.ToArray();}
        }
#endif
    }
}