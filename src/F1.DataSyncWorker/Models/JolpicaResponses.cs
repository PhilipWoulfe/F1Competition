using System.Text.Json.Serialization;

namespace F1.DataSyncWorker.Models;

public sealed class JolpicaDriversEnvelope
{
    [JsonPropertyName("MRData")]
    public JolpicaDriversMetadata? Metadata { get; set; }
}

public sealed class JolpicaDriversMetadata
{
    [JsonPropertyName("DriverTable")]
    public JolpicaDriverTable? DriverTable { get; set; }
}

public sealed class JolpicaDriverTable
{
    [JsonPropertyName("Drivers")]
    public List<JolpicaDriverDto> Drivers { get; set; } = [];
}

public sealed class JolpicaDriverDto
{
    [JsonPropertyName("driverId")]
    public string DriverId { get; set; } = string.Empty;

    [JsonPropertyName("givenName")]
    public string GivenName { get; set; } = string.Empty;

    [JsonPropertyName("familyName")]
    public string FamilyName { get; set; } = string.Empty;

    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("permanentNumber")]
    public string? PermanentNumber { get; set; }

    [JsonPropertyName("nationality")]
    public string? Nationality { get; set; }
}

public sealed class JolpicaRacesEnvelope
{
    [JsonPropertyName("MRData")]
    public JolpicaRacesMetadata? Metadata { get; set; }
}

public sealed class JolpicaRacesMetadata
{
    [JsonPropertyName("RaceTable")]
    public JolpicaRaceTable? RaceTable { get; set; }
}

public sealed class JolpicaRaceTable
{
    [JsonPropertyName("Races")]
    public List<JolpicaRaceDto> Races { get; set; } = [];
}

public sealed class JolpicaRaceDto
{
    [JsonPropertyName("season")]
    public string Season { get; set; } = string.Empty;

    [JsonPropertyName("round")]
    public string Round { get; set; } = string.Empty;

    [JsonPropertyName("raceName")]
    public string RaceName { get; set; } = string.Empty;

    [JsonPropertyName("date")]
    public string Date { get; set; } = string.Empty;

    [JsonPropertyName("time")]
    public string? Time { get; set; }

    [JsonPropertyName("Circuit")]
    public JolpicaCircuitDto? Circuit { get; set; }
}

public sealed class JolpicaCircuitDto
{
    [JsonPropertyName("circuitId")]
    public string? CircuitId { get; set; }

    [JsonPropertyName("circuitName")]
    public string? CircuitName { get; set; }
}