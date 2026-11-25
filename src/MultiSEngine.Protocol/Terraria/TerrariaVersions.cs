namespace MultiSEngine.Protocol.Terraria;

public static class TerrariaVersions
{
    public const int MinimumSupportedVersion = 269;
    public const int LatestProtocolVersion = 329;

    public static string ToVersionString(int version) => version switch
    {
        269 => "v1.4.4",
        270 => "v1.4.4.1",
        271 => "v1.4.4.2",
        272 => "v1.4.4.3",
        273 => "v1.4.4.4",
        274 => "v1.4.4.5",
        275 => "v1.4.4.6",
        276 => "v1.4.4.7",
        277 => "v1.4.4.8",
        278 => "v1.4.4.8.1",
        279 => "v1.4.4.9",
        318 => "v1.4.5.5",
        319 => "v1.4.5.6",
        325 => "v1.4.5.7",
        326 => "v1.4.5.8",
        329 => "v1.4.6.1",
        _ => "Unknown",
    };

    public static bool TryParseClientHelloVersion(string? rawVersion, out int versionNum)
    {
        versionNum = 0;
        if (string.IsNullOrWhiteSpace(rawVersion))
            return false;

        if (rawVersion.StartsWith("Terraria", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(rawVersion.AsSpan("Terraria".Length), out versionNum))
        {
            return versionNum > 0;
        }

        return int.TryParse(rawVersion, out versionNum) && versionNum > 0;
    }

    public static string ToClientHelloString(int versionNum) => $"Terraria{versionNum}";
}
