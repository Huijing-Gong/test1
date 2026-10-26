using System;
using System.IO;
using System.IO.Compression;
using UnityEngine;
public static class OwnPngStorageEncoder
{
    static uint Crc(byte[] data){uint c=0xffffffff;foreach(byte b in data){c^=b;for(int i=0;i<8;i++)c=(c&1)!=0?(c>>1)^0xedb88320:c>>1;}return c^0xffffffff;}
    static void U32(Stream s,uint v){s.WriteByte((byte)(v>>24));s.WriteByte((byte)(v>>16));s.WriteByte((byte)(v>>8));s.WriteByte((byte)v);}
    static void Chunk(Stream s,string tag,byte[] body)
    {byte[] name=System.Text.Encoding.ASCII.GetBytes(tag);U32(s,(uint)body.Length);s.Write(name,0,4);s.Write(body,0,body.Length);var both=new byte[4+body.Length];Array.Copy(name,both,4);Array.Copy(body,0,both,4,body.Length);U32(s,Crc(both));}
    static int Paeth(int a,int b,int c){int p=a+b-c,pa=Math.Abs(p-a),pb=Math.Abs(p-b),pc=Math.Abs(p-c);return pa<=pb&&pa<=pc?a:pb<=pc?b:c;}
    public static byte[] Pack(Color32[] pixels,int width,int height)
    {
        int rowSize=width*4;var filtered=new byte[(rowSize+1)*height];var row=new byte[rowSize];var prev=new byte[rowSize];var best=new byte[rowSize];var trial=new byte[rowSize];
        for(int y=0;y<height;y++)
        {
            for(int x=0;x<width;x++){var p=pixels[(height-1-y)*width+x];int k=x*4;row[k]=p.r;row[k+1]=p.g;row[k+2]=p.b;row[k+3]=p.a;}
            long bestCost=long.MaxValue;byte bestType=0;
            for(byte type=0;type<5;type++)
            {
                long cost=0;for(int k=0;k<rowSize;k++){int a=k>=4?row[k-4]:0,b=prev[k],c=k>=4?prev[k-4]:0;int predictor=type==0?0:type==1?a:type==2?b:type==3?(a+b)/2:Paeth(a,b,c);byte value=unchecked((byte)(row[k]-predictor));trial[k]=value;cost+=Math.Abs((int)unchecked((sbyte)value));}
                if(cost<bestCost){bestCost=cost;bestType=type;Array.Copy(trial,best,rowSize);}
            }
            int offset=y*(rowSize+1);filtered[offset]=bestType;Array.Copy(best,0,filtered,offset+1,rowSize);Array.Copy(row,prev,rowSize);
        }
        byte[] compressed;using(var output=new MemoryStream()){using(var deflate=new DeflateStream(output,System.IO.Compression.CompressionLevel.Optimal,true))deflate.Write(filtered,0,filtered.Length);compressed=output.ToArray();}
        uint adlerA=1,adlerB=0;foreach(byte b in filtered){adlerA=(adlerA+b)%65521;adlerB=(adlerB+adlerA)%65521;}
        using(var png=new MemoryStream())
        {
            png.Write(new byte[]{137,80,78,71,13,10,26,10},0,8);byte[] head;using(var h=new MemoryStream()){U32(h,(uint)width);U32(h,(uint)height);h.Write(new byte[]{8,6,0,0,0},0,5);head=h.ToArray();}Chunk(png,"IHDR",head);
            using(var z=new MemoryStream()){z.WriteByte(0x78);z.WriteByte(0xda);z.Write(compressed,0,compressed.Length);U32(z,(adlerB<<16)|adlerA);Chunk(png,"IDAT",z.ToArray());}Chunk(png,"IEND",Array.Empty<byte>());return png.ToArray();
        }
    }
}