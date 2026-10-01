using System.Buffers.Binary;
using PresentationSpace.Formats;
using Xunit;

namespace PresentationSpace.Tests;

public sealed class RasterHeaderTests
{
    [Fact] public void ReadsPngHeaderFromRealEncodedImage()
    {
        var h=RasterHeader.Read(Convert.FromBase64String(PictureRenderingTests.Asset().Base64));Assert.Equal(80,h.Width);Assert.Equal(40,h.Height);Assert.Equal("image/png",h.MimeType);
    }
    [Theory] [InlineData("GIF87a")] [InlineData("GIF89a")]
    public void ReadsGifLogicalScreenDimensions(string signature)
    {
        var data=new byte[10];System.Text.Encoding.ASCII.GetBytes(signature).CopyTo(data,0);BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(6),320);BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(8),240);
        var h=RasterHeader.Read(data);Assert.Equal(320,h.Width);Assert.Equal(240,h.Height);Assert.Equal("image/gif",h.MimeType);
    }
    [Theory] [InlineData(0xC0)] [InlineData(0xC2)]
    public void JpegFramesAreFoundWithoutScanningCompressedPixels(int marker)
    {
        byte[] data=[255,216,255,0xE0,0,4,1,2,255,(byte)marker,0,8,8,0,100,1,44,3];
        var h=RasterHeader.Read(data);Assert.Equal(300,h.Width);Assert.Equal(100,h.Height);Assert.Equal("jpg",h.Extension);
        data[10]=255;data[11]=255;Assert.False(RasterHeader.TryRead(data,out _));
    }
    [Theory] [InlineData("VP8X",10)] [InlineData("VP8 ",10)] [InlineData("VP8L",6)]
    public void WebpDimensionsSupportExtendedLossyAndLosslessHeaders(string tag,int size)
    {
        var data=new byte[20+size];"RIFF"u8.CopyTo(data);BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4),(uint)data.Length-8);"WEBP"u8.CopyTo(data.AsSpan(8));System.Text.Encoding.ASCII.GetBytes(tag).CopyTo(data,12);BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(16),(uint)size);
        if(tag=="VP8X") {data[24]=79;data[27]=39;}
        else if(tag=="VP8 ") {data[23]=0x9D;data[24]=1;data[25]=0x2A;data[26]=80;data[28]=40;}
        else {data[20]=0x2F;BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(21),79|(39u<<14));}
        var h=RasterHeader.Read(data);Assert.Equal(80,h.Width);Assert.Equal(40,h.Height);Assert.Equal("image/webp",h.MimeType);
        data[16]=255;Assert.False(RasterHeader.TryRead(data,out _));
    }
    [Fact] public void EmptyUnknownAndTruncatedHeadersDoNotThrowUnexpectedExceptions()
    {
        var random=new Random(5817);for(int size=0;size<300;size++){var data=new byte[size];random.NextBytes(data);Assert.False(RasterHeader.TryRead(data,out _));}
        Assert.Throws<InvalidDataException>(()=>RasterHeader.Read([]));
        byte[] jpeg=[255,216,255,0xDA,0,8,8,1];Assert.False(RasterHeader.TryRead(jpeg,out _));
    }
}
