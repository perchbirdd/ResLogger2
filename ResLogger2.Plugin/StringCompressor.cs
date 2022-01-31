using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using Dalamud.Logging;

namespace ResLogger2.Plugin;

public static class StringCompressor
{
    /// <summary>
    /// Compresses the string.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns></returns>
    public static string CompressString(string text)
    {
        byte[] buffer = Encoding.UTF8.GetBytes(text);
        byte[] compressedData;
            
        using (var memoryStream = new MemoryStream())
        {
            using (var gZipStream = new GZipStream(memoryStream, CompressionLevel.Optimal, true))
            {
                gZipStream.Write(buffer, 0, buffer.Length);
            }

            memoryStream.Position = 0;

            compressedData = new byte[memoryStream.Length];
            PluginLog.Verbose($"Compressed length: {compressedData.Length}");
            memoryStream.Read(compressedData, 0, compressedData.Length);
        }

        return Convert.ToBase64String(compressedData);
    }
}