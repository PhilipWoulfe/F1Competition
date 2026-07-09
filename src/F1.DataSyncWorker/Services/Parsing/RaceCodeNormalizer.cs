namespace F1.DataSyncWorker.Services;

internal static class RaceCodeNormalizer
{
    private static readonly Dictionary<string, string> RaceCodeAliasDictionary = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AUS"] = "albert_park",
        ["AUSTRALIA"] = "albert_park",
        ["ALBERT PARK"] = "albert_park",
        ["CHN"] = "shanghai",
        ["CHINA"] = "shanghai",
        ["SHANGHAI"] = "shanghai",
        ["JPN"] = "suzuka",
        ["JAPAN"] = "suzuka",
        ["SUZUKA"] = "suzuka",
        ["BAH"] = "bahrain",
        ["BAHRAIN"] = "bahrain",
        ["SAR"] = "jeddah",
        ["SAUDI"] = "jeddah",
        ["JEDDAH"] = "jeddah",
        ["MIA"] = "miami",
        ["MIAMI"] = "miami",
        ["IMO"] = "imola",
        ["IMOLA"] = "imola",
        ["MON"] = "monaco",
        ["MONACO"] = "monaco",
        ["MONZA"] = "monza",
        ["MNZ"] = "monza",
        ["ITA"] = "monza",
        ["ITALY"] = "monza",
        ["BAR"] = "catalunya",
        ["ESP"] = "catalunya",
        ["SPAIN"] = "catalunya",
        ["CAN"] = "villeneuve",
        ["CANADA"] = "villeneuve",
        ["AUSTRIA"] = "red_bull_ring",
        ["AUT"] = "red_bull_ring",
        ["RED BULL RING"] = "red_bull_ring",
        ["GBR"] = "silverstone",
        ["BRITAIN"] = "silverstone",
        ["SILVERSTONE"] = "silverstone",
        ["SPA"] = "spa",
        ["BEL"] = "spa",
        ["BELGIUM"] = "spa",
        ["HUN"] = "hungaroring",
        ["HUNGARY"] = "hungaroring",
        ["NED"] = "zandvoort",
        ["NETHERLANDS"] = "zandvoort",
        ["BAK"] = "baku",
        ["AZE"] = "baku",
        ["AZERBAIJAN"] = "baku",
        ["SIN"] = "marina_bay",
        ["SINGAPORE"] = "marina_bay",
        ["COTA"] = "americas",
        ["USA"] = "americas",
        ["UNITED STATES"] = "americas",
        ["MEX"] = "rodriguez",
        ["MEXICO"] = "rodriguez",
        ["BRA"] = "interlagos",
        ["BRAZIL"] = "interlagos",
        ["LAS"] = "vegas",
        ["VEGAS"] = "vegas",
        ["QAT"] = "losail",
        ["QATAR"] = "losail",
        ["ABD"] = "yas_marina",
        ["ABU DHABI"] = "yas_marina"
    };

    public static string NormalizeRaceCode(string raceToken)
    {
        var upper = NormalizeAliasLookupToken(raceToken);
        if (RaceCodeAliasDictionary.TryGetValue(upper, out var mapped))
        {
            return mapped;
        }

        return upper.Length <= 3 ? upper : upper[..3];
    }

    private static string NormalizeAliasLookupToken(string rawValue)
    {
        return string.Join(" ", rawValue.Trim().ToUpperInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}