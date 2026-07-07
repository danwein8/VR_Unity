using System;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Encodes mic audio to WAV for sending, and decodes the reply WAV back into an
/// AudioClip in memory (no temp files), which is more robust on Android.
/// </summary>
public static class WavUtility
{
    // ---- float samples (-1..1) -> 16-bit PCM WAV bytes (for the backend) ----
    public static byte[] EncodeWav(float[] samples, int sampleRate, int channels)
    {
        const short bitsPerSample = 16;
        int blockAlign = channels * bitsPerSample / 8;
        int byteRate = sampleRate * blockAlign;
        int dataSize = samples.Length * sizeof(short);

        using (var ms = new MemoryStream(44 + dataSize))
        using (var bw = new BinaryWriter(ms))
        {
            bw.Write(Encoding.ASCII.GetBytes("RIFF"));
            bw.Write(36 + dataSize);
            bw.Write(Encoding.ASCII.GetBytes("WAVE"));
            bw.Write(Encoding.ASCII.GetBytes("fmt "));
            bw.Write(16);
            bw.Write((short)1);
            bw.Write((short)channels);
            bw.Write(sampleRate);
            bw.Write(byteRate);
            bw.Write((short)blockAlign);
            bw.Write(bitsPerSample);
            bw.Write(Encoding.ASCII.GetBytes("data"));
            bw.Write(dataSize);
            for (int i = 0; i < samples.Length; i++)
            {
                short s = (short)Mathf.Clamp(samples[i] * 32767f, short.MinValue, short.MaxValue);
                bw.Write(s);
            }
            return ms.ToArray();
        }
    }

    // ---- 16-bit PCM WAV bytes -> AudioClip (for playing the reply) ----
    public static AudioClip ToAudioClip(byte[] wav, string name = "reply")
    {
        if (wav == null || wav.Length < 44) return null;

        int channels = 1, sampleRate = 24000, bits = 16;
        int dataOffset = -1, dataLength = 0;

        int pos = 12; // skip "RIFF"<size>"WAVE"
        while (pos + 8 <= wav.Length)
        {
            string id = Encoding.ASCII.GetString(wav, pos, 4);
            int size = BitConverter.ToInt32(wav, pos + 4);
            int body = pos + 8;

            if (id == "fmt ")
            {
                channels   = BitConverter.ToInt16(wav, body + 2);
                sampleRate = BitConverter.ToInt32(wav, body + 4);
                bits       = BitConverter.ToInt16(wav, body + 14);
            }
            else if (id == "data")
            {
                dataOffset = body;
                dataLength = size;
                break;
            }
            pos = body + size + (size & 1); // chunks are word-aligned
        }

        if (dataOffset < 0 || bits != 16) return null;

        // TTS WAVs are streamed, so the header's data-chunk size is often 0 or
        // bogus. Use the real number of bytes we actually received instead.
        if (dataLength <= 0 || dataLength > wav.Length - dataOffset)
            dataLength = wav.Length - dataOffset;

        int sampleCount = dataLength / 2;
        float[] samples = new float[sampleCount];
        for (int i = 0; i < sampleCount; i++)
            samples[i] = BitConverter.ToInt16(wav, dataOffset + i * 2) / 32768f;

        AudioClip clip = AudioClip.Create(name, sampleCount / channels, channels, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }
}