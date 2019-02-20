using System;
using System.IO;
using Xunit;

namespace LibXboxOne.Tests
{
    public static class HashUtilsData
    {
        public static byte[] Data => new byte[]{
                0x48,0x45,0x4c,0x4c,0x4f,0x2c,0x20,0x49,0x54,0x53,0x20,0x4d,0x45,0x2c,0x20,0x54,
                0x45,0x53,0x54,0x44,0x41,0x54,0x41,0x0a,0x01,0x11,0x21,0x31,0x41,0x51,0x61,0x71};
        
        public static byte[] Sha256Hash => new byte[]{
                0x3C,0x47,0xF6,0x32,0x57,0x72,0xCC,0x80,0xAE,0x72,0x0D,0xA2,0x4C,0x60,0xD8,0x1A,
                0xBF,0xD1,0x7E,0x8A,0x63,0xA8,0xCC,0xDE,0x4B,0xD9,0xF2,0xB8,0xBB,0xC3,0x82,0x0F};
    }

    public class HashUtilsTests
    {
        [Fact]
        public void TestComputeSha256()
        {
            var result = HashUtils.ComputeSha256(HashUtilsData.Data);
            
            Assert.Equal(HashUtilsData.Sha256Hash, result);
        }
    }
}