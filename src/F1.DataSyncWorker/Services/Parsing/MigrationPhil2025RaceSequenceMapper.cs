namespace F1.DataSyncWorker.Services;

internal static class MigrationPhil2025RaceSequenceMapper
{
    private static readonly string[] CircuitIdsByRaceSequence =
    [
        "albert_park",
        "shanghai",
        "suzuka",
        "bahrain",
        "jeddah",
        "miami",
        "imola",
        "monaco",
        "catalunya",
        "villeneuve",
        "red_bull_ring",
        "silverstone",
        "spa",
        "hungaroring",
        "zandvoort",
        "monza",
        "baku",
        "marina_bay",
        "americas",
        "rodriguez",
        "interlagos",
        "vegas",
        "losail",
        "yas_marina"
    ];

    public static string? TryResolveCircuitId(int raceSequence)
    {
        if (raceSequence <= 0 || raceSequence > CircuitIdsByRaceSequence.Length)
        {
            return null;
        }

        return CircuitIdsByRaceSequence[raceSequence - 1];
    }
}