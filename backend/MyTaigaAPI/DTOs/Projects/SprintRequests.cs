using System.ComponentModel.DataAnnotations;

namespace MyTaigaAPI.DTOs.Projects;

public class SprintRequest : IValidatableObject
{
    [Range(1, long.MaxValue)] public long ProjectId { get; set; }
    [Required, StringLength(150)] public string SprintName { get; set; } = string.Empty;
    [StringLength(500)] public string? SprintGoal { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (string.IsNullOrWhiteSpace(SprintName)) yield return new("Tên Sprint không được trống.", [nameof(SprintName)]);
        if (StartDate == default || EndDate == default || EndDate < StartDate || EndDate == DateOnly.MaxValue)
            yield return new("Cần ngày bắt đầu/kết thúc hợp lệ; kết thúc không trước bắt đầu.", [nameof(EndDate)]);
        else if (EndDate.DayNumber - StartDate.DayNumber > 365)
            yield return new("Sprint không được dài quá 366 ngày.", [nameof(EndDate)]);
    }
}

public class SprintBacklogRequest
{
    [Required, MinLength(1), MaxLength(500)] public List<long> PbiIds { get; set; } = [];
}
