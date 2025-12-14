using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Generates animated GIF files from ray-traced scene rotations.
/// 
/// Features:
/// - Renders 36 frames at 10° increments for a smooth 360° rotation
/// - Parallel LZW compression for fast encoding
/// - Asynchronous operation with progress reporting
/// - Produces standard GIF89a format with infinite looping
/// </summary>
public class GifGenerator
{
    private RayTracer rayTracer;
    private ObjectData scene;
    
    /// <summary>
    /// Creates a new GIF generator for the specified scene.
    /// </summary>
    /// <param name="rayTracer">Ray tracer instance for rendering frames</param>
    /// <param name="scene">Scene data to render</param>
    public GifGenerator(RayTracer rayTracer, ObjectData scene)
    {
        this.rayTracer = rayTracer;
        this.scene = scene;
    }
    
    /// <summary>
    /// Renders frames for a 360-degree rotation animation around the Z-axis.
    /// </summary>
    /// <param name="baseSettings">Base render settings (resolution, lighting toggles, etc.)</param>
    /// <param name="progress">Callback for progress updates (value 0-1, status message)</param>
    /// <param name="token">Cancellation token to abort generation</param>
    /// <returns>List of rendered frames as Texture2D objects</returns>
    public async Task<List<Texture2D>> GenerateRotationFrames(
        RenderSettings baseSettings, 
        Action<float, string> progress,
        CancellationToken token)
    {
        List<Texture2D> frames = new List<Texture2D>();
        
        int totalFrames = 36; // 360° at 10° increments
        
        for (int angle = 0; angle < 360; angle += 10)
        {
            if (token.IsCancellationRequested) break;
            
            int frameIndex = angle / 10;
            float progressValue = (float)frameIndex / totalFrames;
            progress?.Invoke(progressValue, $"Rendering frame {frameIndex + 1}/{totalFrames} (Z={angle}°)");
            
            // Clone base settings and apply rotation for this frame
            RenderSettings frameSettings = baseSettings;
            Vector3 baseRotation = baseSettings.CameraRotationOverride ?? Vector3.zero;
            frameSettings.CameraRotationOverride = new Vector3(baseRotation.x, baseRotation.y, angle);
            
            // Render the frame
            var frame = await rayTracer.RenderAsync(scene, frameSettings, null, token);
            
            if (frame != null)
            {
                frames.Add(frame);
            }
        }
        
        return frames;
    }
    
    /// <summary>
    /// Encodes frames to GIF format and saves to disk asynchronously.
    /// Uses parallel processing for compression to maximize performance.
    /// </summary>
    /// <param name="frames">List of frames to encode</param>
    /// <param name="filePath">Output file path</param>
    /// <param name="progress">Callback for progress updates</param>
    /// <param name="frameDelay">Delay between frames in centiseconds (default: 10 = 100ms)</param>
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
            // Write GIF89a header
            WriteGifHeader(writer, width, height);
            
            // Write global color table (6x6x6 color cube + grayscale)
            byte[] colorTable = GenerateColorTable();
            writer.Write(colorTable);
            
            // Write Netscape extension for infinite looping
            WriteLoopExtension(writer);
            
            // Extract pixel data on main thread (Unity API restriction)
            progress?.Invoke(0.1f, "Extracting pixel data...");
            await Task.Yield();
            
            var rawPixels = new Color[frames.Count][];
            var frameSizes = new (int w, int h)[frames.Count];
            for (int i = 0; i < frames.Count; i++)
            {
                rawPixels[i] = frames[i].GetPixels();
                frameSizes[i] = (frames[i].width, frames[i].height);
            }
            
            // Parallel compression on background threads
            progress?.Invoke(0.2f, "Compressing frames...");
            await Task.Yield();
            
            var frameData = new byte[frames.Count][];
            
            await Task.Run(() =>
            {
                Parallel.For(0, frames.Count, i =>
                {
                    byte[] indexed = ConvertToIndexed(rawPixels[i], frameSizes[i].w, frameSizes[i].h);
                    frameData[i] = LzwCompress(indexed, 8);
                });
            });
            
            // Write frames sequentially (file I/O must be sequential)
            progress?.Invoke(0.7f, "Writing GIF file...");
            await Task.Yield();
            
            for (int i = 0; i < frames.Count; i++)
            {
                float writeProgress = 0.7f + (0.25f * (float)i / frames.Count);
                progress?.Invoke(writeProgress, $"Writing frame {i + 1}/{frames.Count}...");
                
                WriteFrameData(writer, frameSizes[i].w, frameSizes[i].h, frameData[i], frameDelay);
                
                // Yield occasionally to keep UI responsive
                if (i % 5 == 0) await Task.Yield();
            }
            
            progress?.Invoke(0.95f, "Finalizing GIF...");
            await Task.Yield();
            
            // Write GIF trailer
            writer.Write((byte)0x3B);
        }
        
        progress?.Invoke(1f, "GIF saved!");
        Debug.Log($"[GifGenerator] GIF saved to: {filePath}");
    }
    
    /// <summary>
    /// Synchronous version of SaveGifAsync for backward compatibility.
    /// </summary>
    public void SaveGif(List<Texture2D> frames, string filePath, int frameDelay = 10)
    {
        if (frames == null || frames.Count == 0) return;
        
        int width = frames[0].width;
        int height = frames[0].height;
        
        using (FileStream fs = new FileStream(filePath, FileMode.Create))
        using (BinaryWriter writer = new BinaryWriter(fs))
        {
            WriteGifHeader(writer, width, height);
            
            byte[] colorTable = GenerateColorTable();
            writer.Write(colorTable);
            
            WriteLoopExtension(writer);
            
            foreach (var frame in frames)
            {
                WriteFrame(writer, frame, frameDelay);
            }
            
            writer.Write((byte)0x3B); // GIF trailer
        }
        
        Debug.Log($"[GifGenerator] GIF saved to: {filePath}");
    }

    #region GIF Format Helpers
    
    /// <summary>
    /// Writes the GIF89a header and logical screen descriptor.
    /// </summary>
    private void WriteGifHeader(BinaryWriter writer, int width, int height)
    {
        writer.Write(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 }); // "GIF89a"
        writer.Write((ushort)width);
        writer.Write((ushort)height);
        writer.Write((byte)0xF7); // Global color table flag, 256 colors (8-bit)
        writer.Write((byte)0x00); // Background color index
        writer.Write((byte)0x00); // Pixel aspect ratio (1:1)
    }
    
    /// <summary>
    /// Writes the Netscape 2.0 application extension for infinite looping.
    /// </summary>
    private void WriteLoopExtension(BinaryWriter writer)
    {
        writer.Write((byte)0x21); // Extension introducer
        writer.Write((byte)0xFF); // Application extension label
        writer.Write((byte)0x0B); // Block size
        writer.Write(System.Text.Encoding.ASCII.GetBytes("NETSCAPE2.0"));
        writer.Write((byte)0x03); // Sub-block size
        writer.Write((byte)0x01); // Loop indicator
        writer.Write((ushort)0x0000); // Loop count (0 = infinite)
        writer.Write((byte)0x00); // Block terminator
    }
    
    /// <summary>
    /// Generates a 256-color palette (6x6x6 color cube + 40 grayscale values).
    /// This provides reasonable color coverage for ray-traced images.
    /// </summary>
    private byte[] GenerateColorTable()
    {
        byte[] table = new byte[256 * 3];
        
        // First 216 colors: 6x6x6 color cube
        int idx = 0;
        for (int r = 0; r < 6; r++)
        {
            for (int g = 0; g < 6; g++)
            {
                for (int b = 0; b < 6; b++)
                {
                    table[idx++] = (byte)(r * 51); // 0, 51, 102, 153, 204, 255
                    table[idx++] = (byte)(g * 51);
                    table[idx++] = (byte)(b * 51);
                }
            }
        }
        
        // Remaining 40 colors: grayscale ramp
        for (int i = 216; i < 256; i++)
        {
            byte gray = (byte)((i - 216) * 6.5f);
            table[idx++] = gray;
            table[idx++] = gray;
            table[idx++] = gray;
        }
        
        return table;
    }
    
    #endregion

    #region Frame Encoding
    
    /// <summary>
    /// Writes a single frame using pre-compressed data (for parallel encoding path).
    /// </summary>
    private void WriteFrameData(BinaryWriter writer, int width, int height, byte[] compressed, int delay)
    {
        // Graphic Control Extension
        writer.Write((byte)0x21); // Extension introducer
        writer.Write((byte)0xF9); // Graphic control label
        writer.Write((byte)0x04); // Block size
        writer.Write((byte)0x00); // Packed byte (no transparency, no disposal)
        writer.Write((ushort)delay); // Delay time in centiseconds
        writer.Write((byte)0x00); // Transparent color index (unused)
        writer.Write((byte)0x00); // Block terminator
        
        // Image Descriptor
        writer.Write((byte)0x2C); // Image separator
        writer.Write((ushort)0); // Left position
        writer.Write((ushort)0); // Top position
        writer.Write((ushort)width);
        writer.Write((ushort)height);
        writer.Write((byte)0x00); // Packed byte (no local color table, not interlaced)
        
        // LZW Image Data
        byte minCodeSize = 8;
        writer.Write(minCodeSize);
        
        // Write compressed data in sub-blocks (max 255 bytes each per GIF spec)
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
    
    /// <summary>
    /// Writes a single frame (synchronous version with inline compression).
    /// </summary>
    private void WriteFrame(BinaryWriter writer, Texture2D frame, int delay)
    {
        int width = frame.width;
        int height = frame.height;
        
        // Graphic Control Extension
        writer.Write((byte)0x21);
        writer.Write((byte)0xF9);
        writer.Write((byte)0x04);
        writer.Write((byte)0x00);
        writer.Write((ushort)delay);
        writer.Write((byte)0x00);
        writer.Write((byte)0x00);
        
        // Image Descriptor
        writer.Write((byte)0x2C);
        writer.Write((ushort)0);
        writer.Write((ushort)0);
        writer.Write((ushort)width);
        writer.Write((ushort)height);
        writer.Write((byte)0x00);
        
        // Compress and write image data
        byte minCodeSize = 8;
        writer.Write(minCodeSize);
        
        byte[] pixels = GetIndexedPixels(frame);
        byte[] compressed = LzwCompress(pixels, minCodeSize);
        
        int offset = 0;
        while (offset < compressed.Length)
        {
            int blockSize = Math.Min(255, compressed.Length - offset);
            writer.Write((byte)blockSize);
            writer.Write(compressed, offset, blockSize);
            offset += blockSize;
        }
        
        writer.Write((byte)0x00);
    }
    
    #endregion

    #region Pixel Conversion
    
    /// <summary>
    /// Converts RGBA pixels to palette indices using the 6x6x6 color cube.
    /// Thread-safe for parallel processing.
    /// </summary>
    private byte[] ConvertToIndexed(Color[] pixels, int width, int height)
    {
        byte[] indexed = new byte[pixels.Length];
        
        // Parallel pixel quantization
        Parallel.For(0, pixels.Length, i =>
        {
            // Map each channel to 0-5 range for 6x6x6 cube indexing
            int r = Math.Max(0, Math.Min(5, (int)(pixels[i].r * 5.99f)));
            int g = Math.Max(0, Math.Min(5, (int)(pixels[i].g * 5.99f)));
            int b = Math.Max(0, Math.Min(5, (int)(pixels[i].b * 5.99f)));
            indexed[i] = (byte)(r * 36 + g * 6 + b);
        });
        
        // Flip vertically (Unity textures are bottom-to-top, GIF is top-to-bottom)
        byte[] flipped = new byte[indexed.Length];
        
        Parallel.For(0, height, y =>
        {
            Array.Copy(indexed, y * width, flipped, (height - 1 - y) * width, width);
        });
        
        return flipped;
    }
    
    /// <summary>
    /// Converts a Texture2D to palette indices (main-thread version).
    /// </summary>
    private byte[] GetIndexedPixels(Texture2D texture)
    {
        Color[] pixels = texture.GetPixels();
        byte[] indexed = new byte[pixels.Length];
        
        Parallel.For(0, pixels.Length, i =>
        {
            int r = Mathf.Clamp((int)(pixels[i].r * 5.99f), 0, 5);
            int g = Mathf.Clamp((int)(pixels[i].g * 5.99f), 0, 5);
            int b = Mathf.Clamp((int)(pixels[i].b * 5.99f), 0, 5);
            indexed[i] = (byte)(r * 36 + g * 6 + b);
        });
        
        // Flip vertically
        int width = texture.width;
        int height = texture.height;
        byte[] flipped = new byte[indexed.Length];
        
        Parallel.For(0, height, y =>
        {
            Array.Copy(indexed, y * width, flipped, (height - 1 - y) * width, width);
        });
        
        return flipped;
    }
    
    #endregion

    #region LZW Compression
    
    /// <summary>
    /// LZW compresses data for GIF encoding.
    /// Implements the GIF-specific LZW variant with variable code sizes.
    /// </summary>
    /// <param name="data">Indexed pixel data to compress</param>
    /// <param name="minCodeSize">Minimum code size (always 8 for 256-color GIFs)</param>
    /// <returns>LZW compressed data</returns>
    private byte[] LzwCompress(byte[] data, int minCodeSize)
    {
        List<byte> output = new List<byte>();
        int clearCode = 1 << minCodeSize;   // 256 for 8-bit
        int endCode = clearCode + 1;        // 257
        
        // Initialize code table with single-byte codes
        Dictionary<string, int> codeTable = new Dictionary<string, int>();
        int nextCode = endCode + 1;
        int codeSize = minCodeSize + 1; // Start at 9 bits
        
        for (int i = 0; i < clearCode; i++)
        {
            codeTable[((char)i).ToString()] = i;
        }
        
        // Bit buffer for variable-width output
        int bitBuffer = 0;
        int bitCount = 0;
        
        // Local function to write a code to the bit buffer
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
        
        // Start with clear code
        WriteCode(clearCode, codeSize);
        
        if (data.Length == 0)
        {
            WriteCode(endCode, codeSize);
            if (bitCount > 0) output.Add((byte)bitBuffer);
            return output.ToArray();
        }
        
        // LZW encoding loop
        string current = ((char)data[0]).ToString();
        
        for (int i = 1; i < data.Length; i++)
        {
            char c = (char)data[i];
            string next = current + c;
            
            if (codeTable.ContainsKey(next))
            {
                // Extend current string
                current = next;
            }
            else
            {
                // Output code for current string
                WriteCode(codeTable[current], codeSize);
                
                // Add new string to table (if not at max)
                if (nextCode < 4096) // GIF LZW max is 12-bit codes
                {
                    codeTable[next] = nextCode;
                    
                    // Increase code size when needed
                    if (nextCode == (1 << codeSize))
                    {
                        codeSize++;
                    }
                    nextCode++;
                }
                
                // Start new string with current character
                current = c.ToString();
            }
        }
        
        // Output final code and end code
        WriteCode(codeTable[current], codeSize);
        WriteCode(endCode, codeSize);
        
        // Flush remaining bits
        if (bitCount > 0)
        {
            output.Add((byte)bitBuffer);
        }
        
        return output.ToArray();
    }
    
    #endregion
}
