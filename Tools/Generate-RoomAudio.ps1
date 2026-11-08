[CmdletBinding()]
param([string]$OutputPath)
$ErrorActionPreference='Stop'
if (-not $OutputPath) { $OutputPath = Join-Path $PSScriptRoot '..\Assets\Audio\AfternoonRoom.wav' }
# Original procedural room tone: filtered air and quiet, distant synthetic birds.
# No recordings, downloaded samples, external libraries, or licensed media.
if(-not ('VintageRoomSound' -as [type])) {
Add-Type -TypeDefinition @'
using System;
using System.IO;
public static class VintageRoomSound
{
    public static void Generate(string path)
    {
        const int rate=22050;
        const int seconds=24;
        int count=rate*seconds;
        var data=new short[count];
        var random=new Random(1634);
        double filtered=0;
        double[] starts={2.1,2.36,7.7,8.0,13.8,14.05,19.5,19.85};
        for(int i=0;i<count;i++)
        {
            double t=(double)i/rate;
            filtered=filtered*.992+(random.NextDouble()*2-1)*.008;
            double value=filtered*.025;
            foreach(double begin in starts)
            {
                double local=t-begin;
                if(local<0 || local>.17) continue;
                double envelope=Math.Sin(Math.PI*local/.17);
                envelope*=envelope;
                double phase=2*Math.PI*(2350*local+900*local*local);
                value+=.021*envelope*(Math.Sin(phase)+.15*Math.Sin(phase*1.51));
            }
            double edge=Math.Min(1,Math.Min(t,seconds-t)/.8);
            data[i]=(short)(Math.Max(-1,Math.Min(1,value*edge))*32767);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
        using(var stream=new BinaryWriter(File.Create(path)))
        {
            stream.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
            stream.Write(36+count*2);
            stream.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
            stream.Write(16); stream.Write((short)1); stream.Write((short)1);
            stream.Write(rate); stream.Write(rate*2); stream.Write((short)2); stream.Write((short)16);
            stream.Write(System.Text.Encoding.ASCII.GetBytes("data")); stream.Write(count*2);
            foreach(short sample in data) stream.Write(sample);
        }
    }
}
'@
}
[VintageRoomSound]::Generate($OutputPath)
Get-Item -LiteralPath $OutputPath | Select-Object FullName,Length
