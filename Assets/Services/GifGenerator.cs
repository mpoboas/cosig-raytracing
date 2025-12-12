using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// GIF Generator for Ray Tracing - Renders 360-degree rotation animation
/// Creates a GIF by rendering the scene at multiple rotation angles
/// </summary>
public class GifGenerator
{
    private RayTracer rayTracer;
    private ObjectData scene;
    
    public GifGenerator(RayTracer rayTracer, ObjectData scene)
    {
        this.rayTracer = rayTracer;
        this.scene = scene;
    }
    
    /// <summary>
    /// Generate a 360-degree rotation GIF
    /// </summary>
    /// <param name="baseSettings">Base render settings (resolution, toggles, etc.)</param>
    /// <param name="progress">Progress callback (0-1)</param>
    /// <param name="token">Cancellation token</param>
    /// <returns>List of rendered frames as Texture2D</returns>
    public async Task<List<Texture2D>> GenerateRotationFrames(
        RenderSettings baseSettings, 
        Action<float, string> progress,
        CancellationToken token)
    {
        List<Texture2D> frames = new List<Texture2D>();
        
        // Render frames from 0 to 350 in increments of 10 (360 is same as 0)
        int totalFrames = 36; 
        
        for (int i = 0; i < 360; i += 10)
        {
            if (token.IsCancellationRequested) break;
            
            int frameIndex = i / 10;
            float progressValue = (float)frameIndex / totalFrames;
            progress?.Invoke(progressValue, $"Rendering frame {frameIndex + 1}/{totalFrames} (Z={i}°)");
            
            // Create settings for this frame with Z rotation
            RenderSettings frameSettings = baseSettings;
            
            // Get base rotation or use zero
            Vector3 baseRotation = baseSettings.CameraRotationOverride ?? Vector3.zero;
            
            // Apply Z rotation for this frame
            frameSettings.CameraRotationOverride = new Vector3(baseRotation.x, baseRotation.y, i);
            
            // Render this frame
            var frame = await rayTracer.RenderAsync(scene, frameSettings, null, token);
            
            if (frame != null)
            {
                frames.Add(frame);
            }
        }
        
        return frames;
    }
    
    /// <summary>
    /// Encode frames to GIF and save to file with progress reporting
    /// </summary>
    public async Task SaveGifAsync(List<Texture2D> frames, string filePath, Action<float, string> progress, int frameDelay = 10)
    {
        if (frames == null || frames.Count == 0) return;
        
        progress?.Invoke(0f, "Starting GIF encoding...");
        await Task.Yield();
        
        int width = frames[0].width;
        int height = frames[0].height;
        
        using (FileStream fs = new FileStream(filePath, FileMode.Create))
        using (BinaryWriter writer = new BinaryWriter(fs))
        {
            // GIF Header
            writer.Write(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 }); // "GIF89a"
            
            // Logical Screen Descriptor
            writer.Write((ushort)width);
            writer.Write((ushort)height);
            writer.Write((byte)0xF7); // Global Color Table Flag, 256 colors
            writer.Write((byte)0x00); // Background color index
            writer.Write((byte)0x00); // Pixel aspect ratio
            
            // Global Color Table (256 colors - simple grayscale + basic colors)
            byte[] colorTable = GenerateColorTable();
            writer.Write(colorTable);
            
            // Netscape Extension for looping
            writer.Write((byte)0x21); // Extension introducer
            writer.Write((byte)0xFF); // Application extension
            writer.Write((byte)0x0B); // Block size
            writer.Write(System.Text.Encoding.ASCII.GetBytes("NETSCAPE2.0"));
            writer.Write((byte)0x03); // Sub-block size
            writer.Write((byte)0x01); // Loop indicator
            writer.Write((ushort)0x0000); // Loop count (0 = infinite)
            writer.Write((byte)0x00); // Block terminator
            
            // Pre-compute all frame data in parallel for massive speedup
            progress?.Invoke(0.1f, "Extracting pixel data...");
            await Task.Yield();
            
            // MUST extract pixels on main thread (Unity restriction)
            var rawPixels = new Color[frames.Count][];
            var frameSizes = new (int w, int h)[frames.Count];
            for (int i = 0; i < frames.Count; i++)
            {
                rawPixels[i] = frames[i].GetPixels();
                frameSizes[i] = (frames[i].width, frames[i].height);
            }
            
            progress?.Invoke(0.2f, "Compressing frames...");
            await Task.Yield();
            
            // Prepare frame data storage
            var frameData = new byte[frames.Count][];
            
            // Parallel conversion and compression (CPU-intensive work)
            await Task.Run(() =>
            {
                System.Threading.Tasks.Parallel.For(0, frames.Count, i =>
                {
                    // Convert pixels to indexed (this was the slow part)
                    byte[] indexed = ConvertToIndexed(rawPixels[i], frameSizes[i].w, frameSizes[i].h);
                    frameData[i] = LzwCompress(indexed, 8);
                });
            });
            
            progress?.Invoke(0.7f, "Writing GIF file...");
            await Task.Yield();
            
            // Write all frames sequentially (file IO must be sequential)
            for (int i = 0; i < frames.Count; i++)
            {
                float writeProgress = 0.7f + (0.25f * (float)i / frames.Count);
                progress?.Invoke(writeProgress, $"Writing frame {i + 1}/{frames.Count}...");
                
                WriteFrameData(writer, frameSizes[i].w, frameSizes[i].h, frameData[i], frameDelay);
                
                if (i % 5 == 0) await Task.Yield();
            }
            
            progress?.Invoke(0.95f, "Finalizing GIF...");
            await Task.Yield();
            
            // GIF Trailer
            writer.Write((byte)0x3B);
        }
        
        progress?.Invoke(1f, "GIF saved!");
        Debug.Log($"[GifGenerator] GIF saved to: {filePath}");
    }
    
    /// <summary>
    /// Encode frames to GIF and save to file (synchronous version for backward compatibility)
    /// </summary>
    public void SaveGif(List<Texture2D> frames, string filePath, int frameDelay = 10)
    {
        if (frames == null || frames.Count == 0) return;
        
        int width = frames[0].width;
        int height = frames[0].height;
        
        using (FileStream fs = new FileStream(filePath, FileMode.Create))
        using (BinaryWriter writer = new BinaryWriter(fs))
        {
            // GIF Header
            writer.Write(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 }); // "GIF89a"
            
            // Logical Screen Descriptor
            writer.Write((ushort)width);
            writer.Write((ushort)height);
            writer.Write((byte)0xF7); // Global Color Table Flag, 256 colors
            writer.Write((byte)0x00); // Background color index
            writer.Write((byte)0x00); // Pixel aspect ratio
            
            // Global Color Table (256 colors - simple grayscale + basic colors)
            byte[] colorTable = GenerateColorTable();
            writer.Write(colorTable);
            
            // Netscape Extension for looping
            writer.Write((byte)0x21); // Extension introducer
            writer.Write((byte)0xFF); // Application extension
            writer.Write((byte)0x0B); // Block size
            writer.Write(System.Text.Encoding.ASCII.GetBytes("NETSCAPE2.0"));
            writer.Write((byte)0x03); // Sub-block size
            writer.Write((byte)0x01); // Loop indicator
            writer.Write((ushort)0x0000); // Loop count (0 = infinite)
            writer.Write((byte)0x00); // Block terminator
            
            // Write each frame
            foreach (var frame in frames)
            {
                WriteFrame(writer, frame, frameDelay);
            }
            
            // GIF Trailer
            writer.Write((byte)0x3B);
        }
        
        Debug.Log($"[GifGenerator] GIF saved to: {filePath}");
    }
    
    private byte[] GenerateColorTable()
    {
        byte[] table = new byte[256 * 3];
        
        // Generate a color palette
        // First 216 colors: 6x6x6 color cube
        int idx = 0;
        for (int r = 0; r < 6; r++)
        {
            for (int g = 0; g < 6; g++)
            {
                for (int b = 0; b < 6; b++)
                {
                    table[idx++] = (byte)(r * 51);
                    table[idx++] = (byte)(g * 51);
                    table[idx++] = (byte)(b * 51);
                }
            }
        }
        
        // Remaining 40 colors: grayscale
        for (int i = 216; i < 256; i++)
        {
            byte gray = (byte)((i - 216) * 6.5f);
            table[idx++] = gray;
            table[idx++] = gray;
            table[idx++] = gray;
        }
        
        return table;
    }
    
    // Write a frame with pre-compressed data (for parallel encoding)
    private void WriteFrameData(BinaryWriter writer, int width, int height, byte[] compressed, int delay)
    {
        // Graphic Control Extension
        writer.Write((byte)0x21); // Extension introducer
        writer.Write((byte)0xF9); // Graphic control label
        writer.Write((byte)0x04); // Block size
        writer.Write((byte)0x00); // Packed byte (no transparency)
        writer.Write((ushort)delay); // Delay time (in 1/100 seconds)
        writer.Write((byte)0x00); // Transparent color index
        writer.Write((byte)0x00); // Block terminator
        
        // Image Descriptor
        writer.Write((byte)0x2C); // Image separator
        writer.Write((ushort)0); // Left position
        writer.Write((ushort)0); // Top position
        writer.Write((ushort)width);
        writer.Write((ushort)height);
        writer.Write((byte)0x00); // Packed byte (no local color table)
        
        // Image Data (already LZW compressed)
        byte minCodeSize = 8;
        writer.Write(minCodeSize);
        
        // Write compressed data in sub-blocks (max 255 bytes each)
        int offset = 0;
        while (offset < compressed.Length)
        {
            int blockSize = Math.Min(255, compressed.Length - offset);
            writer.Write((byte)blockSize);
            writer.Write(compressed, offset, blockSize);
            offset += blockSize;
        }
        
        writer.Write((byte)0x00); // Block terminator
    }
    
    private void WriteFrame(BinaryWriter writer, Texture2D frame, int delay)
    {
        int width = frame.width;
        int height = frame.height;
        
        // Graphic Control Extension
        writer.Write((byte)0x21); // Extension introducer
        writer.Write((byte)0xF9); // Graphic control label
        writer.Write((byte)0x04); // Block size
        writer.Write((byte)0x00); // Packed byte (no transparency)
        writer.Write((ushort)delay); // Delay time (in 1/100 seconds)
        writer.Write((byte)0x00); // Transparent color index
        writer.Write((byte)0x00); // Block terminator
        
        // Image Descriptor
        writer.Write((byte)0x2C); // Image separator
        writer.Write((ushort)0); // Left position
        writer.Write((ushort)0); // Top position
        writer.Write((ushort)width);
        writer.Write((ushort)height);
        writer.Write((byte)0x00); // Packed byte (no local color table)
        
        // Image Data (LZW compressed)
        byte minCodeSize = 8;
        writer.Write(minCodeSize);
        
        // Convert pixels to indexed colors and compress with LZW
        byte[] pixels = GetIndexedPixels(frame);
        byte[] compressed = LzwCompress(pixels, minCodeSize);
        
        // Write compressed data in sub-blocks (max 255 bytes each)
        int offset = 0;
        while (offset < compressed.Length)
        {
            int blockSize = Math.Min(255, compressed.Length - offset);
            writer.Write((byte)blockSize);
            writer.Write(compressed, offset, blockSize);
            offset += blockSize;
        }
        
        writer.Write((byte)0x00); // Block terminator
    }
    
    // Thread-safe conversion from Color array to indexed pixels
    private byte[] ConvertToIndexed(Color[] pixels, int width, int height)
    {
        byte[] indexed = new byte[pixels.Length];
        
        // Parallelize pixel conversion for speed
        System.Threading.Tasks.Parallel.For(0, pixels.Length, i =>
        {
            // Map RGB to 6x6x6 color cube index
            int r = Math.Max(0, Math.Min(5, (int)(pixels[i].r * 5.99f)));
            int g = Math.Max(0, Math.Min(5, (int)(pixels[i].g * 5.99f)));
            int b = Math.Max(0, Math.Min(5, (int)(pixels[i].b * 5.99f)));
            indexed[i] = (byte)(r * 36 + g * 6 + b);
        });
        
        // Flip vertically (Unity textures are bottom-to-top)
        byte[] flipped = new byte[indexed.Length];
        
        // Parallelize row flipping too
        System.Threading.Tasks.Parallel.For(0, height, y =>
        {
            Array.Copy(indexed, y * width, flipped, (height - 1 - y) * width, width);
        });
        
        return flipped;
    }
    
    private byte[] GetIndexedPixels(Texture2D texture)
    {
        Color[] pixels = texture.GetPixels();
        byte[] indexed = new byte[pixels.Length];
        
        // Parallelize pixel conversion for speed
        System.Threading.Tasks.Parallel.For(0, pixels.Length, i =>
        {
            // Map RGB to 6x6x6 color cube index
            int r = Mathf.Clamp((int)(pixels[i].r * 5.99f), 0, 5);
            int g = Mathf.Clamp((int)(pixels[i].g * 5.99f), 0, 5);
            int b = Mathf.Clamp((int)(pixels[i].b * 5.99f), 0, 5);
            indexed[i] = (byte)(r * 36 + g * 6 + b);
        });
        
        // Flip vertically (Unity textures are bottom-to-top)
        int width = texture.width;
        int height = texture.height;
        byte[] flipped = new byte[indexed.Length];
        
        // Parallelize row flipping too
        System.Threading.Tasks.Parallel.For(0, height, y =>
        {
            Array.Copy(indexed, y * width, flipped, (height - 1 - y) * width, width);
        });
        
        return flipped;
    }
    
    private byte[] LzwCompress(byte[] data, int minCodeSize)
    {
        // LZW compression for GIF
        List<byte> output = new List<byte>();
        int clearCode = 1 << minCodeSize;
        int endCode = clearCode + 1;
        
        Dictionary<string, int> codeTable = new Dictionary<string, int>();
        int nextCode = endCode + 1;
        int codeSize = minCodeSize + 1;
        
        // Initialize code table with single-byte entries
        for (int i = 0; i < clearCode; i++)
        {
            codeTable[((char)i).ToString()] = i;
        }
        
        // Bit buffer for output
        int bitBuffer = 0;
        int bitCount = 0;
        
        void WriteCode(int code, int size)
        {
            bitBuffer |= code << bitCount;
            bitCount += size;
            while (bitCount >= 8)
            {
                output.Add((byte)(bitBuffer & 0xFF));
                bitBuffer >>= 8;
                bitCount -= 8;
            }
        }
        
        // Write clear code
        WriteCode(clearCode, codeSize);
        
        if (data.Length == 0)
        {
            WriteCode(endCode, codeSize);
            if (bitCount > 0) output.Add((byte)bitBuffer);
            return output.ToArray();
        }
        
        string current = ((char)data[0]).ToString();
        
        for (int i = 1; i < data.Length; i++)
        {
            char c = (char)data[i];
            string next = current + c;
            
            if (codeTable.ContainsKey(next))
            {
                current = next;
            }
            else
            {
                WriteCode(codeTable[current], codeSize);
                
                if (nextCode < 4096)
                {
                    codeTable[next] = nextCode;
                    
                    // Check if we need to increase code size AFTER adding to table
                    if (nextCode == (1 << codeSize))
                    {
                        codeSize++;
                    }
                    nextCode++;
                }
                
                current = c.ToString();
            }
        }
        
        // Write final code
        WriteCode(codeTable[current], codeSize);
        WriteCode(endCode, codeSize);
        
        // Flush remaining bits
        if (bitCount > 0)
        {
            output.Add((byte)bitBuffer);
        }
        
        return output.ToArray();
    }
}
