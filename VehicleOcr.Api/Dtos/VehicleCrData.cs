using System.Text.Json.Serialization;

namespace VehicleOcr.Api.Dtos;

public class VehicleCrData
{
    [JsonPropertyName("registrationNumber")]
    public string RegistrationNumber { get; set; } = string.Empty;

    [JsonPropertyName("registrationDate")]
    public string RegistrationDate { get; set; } = string.Empty;

    [JsonPropertyName("chassisNumber")]
    public string ChassisNumber { get; set; } = string.Empty;

    [JsonPropertyName("engineNumber")]
    public string EngineNumber { get; set; } = string.Empty;

    [JsonPropertyName("ownerName")]
    public string OwnerName { get; set; } = string.Empty;

    [JsonPropertyName("fatherHusbandName")]
    public string? FatherHusbandName { get; set; }

    [JsonPropertyName("currentAddress")]
    public string? CurrentAddress { get; set; }

    [JsonPropertyName("permanentAddress")]
    public string? PermanentAddress { get; set; }

    [JsonPropertyName("vehicleClass")]
    public string VehicleClass { get; set; } = string.Empty;

    [JsonPropertyName("makersName")]
    public string MakersName { get; set; } = string.Empty;

    [JsonPropertyName("modelName")]
    public string ModelName { get; set; } = string.Empty;

    [JsonPropertyName("bodyType")]
    public string BodyType { get; set; } = string.Empty;

    [JsonPropertyName("mfgMonthYear")]
    public string MfgMonthYear { get; set; } = string.Empty;

    [JsonPropertyName("noOfCylinders")]
    public string NoOfCylinders { get; set; } = string.Empty;

    [JsonPropertyName("cubicCapacity")]
    public string CubicCapacity { get; set; } = string.Empty;

    [JsonPropertyName("horsePower")]
    public string? HorsePower { get; set; }

    [JsonPropertyName("fuelType")]
    public string FuelType { get; set; } = string.Empty;

    [JsonPropertyName("seatingCapacity")]
    public string SeatingCapacity { get; set; } = string.Empty;

    [JsonPropertyName("standingCapacity")]
    public string? StandingCapacity { get; set; }

    [JsonPropertyName("unladenWeight")]
    public string UnladenWeight { get; set; } = string.Empty;

    [JsonPropertyName("grossWeight")]
    public string GrossWeight { get; set; } = string.Empty;

    [JsonPropertyName("color")]
    public string Color { get; set; } = string.Empty;

    [JsonPropertyName("wheelbase")]
    public string? Wheelbase { get; set; }

    [JsonPropertyName("taxValidity")]
    public string TaxValidity { get; set; } = string.Empty;

    [JsonPropertyName("fitnessValidity")]
    public string FitnessValidity { get; set; } = string.Empty;

    [JsonPropertyName("insuranceDetails")]
    public string? InsuranceDetails { get; set; }

    [JsonPropertyName("hypothecation")]
    public string? Hypothecation { get; set; }

    [JsonPropertyName("issuingAuthority")]
    public string IssuingAuthority { get; set; } = string.Empty;

    [JsonPropertyName("confidenceScore")]
    public decimal ConfidenceScore { get; set; } = 0.98m;
}
