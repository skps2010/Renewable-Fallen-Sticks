namespace RenewableFallenSticks;

public sealed class RegrowthConfig
{
    public bool EnableNotificationLog = false;
    public double CheckIntervalHours = 24;
    public int MaxCatchUpAttempts = 7;
    public int MaxSticksPerAttempt = 4;
    public int SamplesPerAttempt = 3;
    public int SampleRadius = 4;
    public int StickSpawnRadius = 1;
    public float ReferenceTreesPerChunk = 70;
    public string[] StickGroundCodes = ["soil", "soil-*", "forestfloor", "forestfloor-*"];
    public string[] StickReplaceableCodes = ["tallgrass", "tallgrass-*", "snowlayer", "snowlayer-*"];
}
