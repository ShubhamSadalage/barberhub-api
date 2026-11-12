using System.ComponentModel.DataAnnotations;

namespace BarberHub.Api.Features.Services.Dtos;

public class ServiceDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Category { get; set; } = "Hair";
    public decimal Price { get; set; }
    public int DurationMinutes { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class ServiceUpsertRequestDto
{
    [Required, StringLength(200)]
    public string Name { get; set; } = string.Empty;

    [StringLength(500)]
    public string? Description { get; set; }

    [Required, StringLength(50)]
    public string Category { get; set; } = "Hair";

    [Required, Range(1, 99999)]
    public decimal Price { get; set; }

    [Required, Range(5, 480)]
    public int DurationMinutes { get; set; } = 30;

    public bool IsActive { get; set; } = true;
}

// ---------- Templates (quick-add menu for new barbers) ----------

public class ServiceTemplateDto
{
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public decimal DefaultPrice { get; set; }
    public int DefaultDuration { get; set; }
    public string Description { get; set; } = string.Empty;
    /// <summary>True if the barber doesn't already have a service with this name — used by the client to pre-check the checkbox.</summary>
    public bool SuggestedSelected { get; set; }
}

public class TemplateActivateRequestDto
{
    public List<TemplateActivateItemDto> Items { get; set; } = new();
}

public class TemplateActivateItemDto
{
    [Required] public string Name { get; set; } = string.Empty;
    [Required] public string Category { get; set; } = string.Empty;
    public string? Description { get; set; }
    [Required, Range(1, 99999)] public decimal Price { get; set; }
    [Required, Range(5, 480)] public int DurationMinutes { get; set; }
}
