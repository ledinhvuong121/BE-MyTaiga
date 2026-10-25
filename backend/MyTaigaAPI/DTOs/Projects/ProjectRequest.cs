using System.ComponentModel.DataAnnotations;

namespace MyTaigaAPI.DTOs.Projects;

public class ProjectRequest : IValidatableObject
{
    [Required, StringLength(200)]
    public string ProjectName { get; set; } = string.Empty;
    [StringLength(10000)]
    public string? Description { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(ProjectName))
            yield return new ValidationResult("Tên dự án không được để trống.", [nameof(ProjectName)]);
        if (StartDate.HasValue && EndDate.HasValue && EndDate < StartDate)
            yield return new ValidationResult("Ngày kết thúc không được trước ngày bắt đầu.", [nameof(EndDate)]);
    }
}
