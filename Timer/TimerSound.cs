
using System;
using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ducky;

public static class TimerSound
{
    [DllImport("winmm.dll", SetLastError = true)]
    private static extern bool PlaySound(
        string fileName,
        IntPtr module,
        uint flags
    );

    private const uint SND_FILENAME = 0x00020000;
    private const uint SND_ASYNC = 0x00000001;

    public static void PlayBell()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "ducky_timer_bell.wav"
        );

        CreateBell(path);

        try
        {
            if (OperatingSystem.IsWindows())
            {
                PlaySound(path, IntPtr.Zero, SND_FILENAME | SND_ASYNC);
            }
            else if (OperatingSystem.IsLinux())
            {
                Process.Start("paplay", path);
            }
            else if (OperatingSystem.IsMacOS())
            {
                Process.Start("afplay", path);
            }
        }
        catch
        {
            // Če predvajanje ni mogoče, aplikacija normalno nadaljuje.
        }
    }

    private static void CreateBell(string path)
    {
        int sampleRate = 44100;
        double duration = 3.0;
        int samples = (int)(sampleRate * duration);

        using BinaryWriter writer = new BinaryWriter(File.Create(path));

        writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + samples * 2);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
        writer.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(sampleRate);
        writer.Write(sampleRate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        writer.Write(samples * 2);

        // Nežna melodija: C5, E5, G5, C6
        double[] notes =
        {
            523.25,
            659.25,
            783.99,
            1046.50
        };

        double[] starts =
        {
            0.0,
            0.55,
            1.1,
            1.65
        };

        double[] lengths =
        {
            1.3,
            1.3,
            1.3,
            0.75
        };

        for (int i = 0; i < samples; i++)
        {
            double time = (double)i / sampleRate;
            double sound = 0;

            for (int n = 0; n < notes.Length; n++)
            {
                double noteTime = time - starts[n];

                if (noteTime < 0 || noteTime > lengths[n])
                    continue;

                // Mehko pojemanje zvoka
                double envelope = Math.Exp(-2.8 * noteTime);

                // Nežen začetek
                double fadeIn = Math.Min(noteTime / 0.04, 1.0);

                // Osnovni ton in nežni harmoniki
                double fundamental =
                    Math.Sin(2 * Math.PI * notes[n] * noteTime);

                double harmonic1 =
                    0.35 * Math.Sin(2 * Math.PI * notes[n] * 2 * noteTime);

                double harmonic2 =
                    0.12 * Math.Sin(2 * Math.PI * notes[n] * 3 * noteTime);

                sound += (fundamental + harmonic1 + harmonic2)
                    * envelope * fadeIn;
            }

            // Nizka glasnost
            sound *= 0.18;

            if (sound > 1)
                sound = 1;

            if (sound < -1)
                sound = -1;

            short value = (short)(sound * 32767);

            writer.Write(value);
        }
    }
}
