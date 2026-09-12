namespace RenewableFallenSticks;

public sealed class RegrowthConfig
{
    public double CheckIntervalHours = 24;
    public int MaxCatchUpAttempts = 7;
    public int MaxSticksPerAttempt = 4;
    public int SamplesPerAttempt = 3;
    public int SampleRadius = 5;
    public float ReferenceTreesPerChunk = 70;
}
