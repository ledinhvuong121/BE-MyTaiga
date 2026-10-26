using System.ComponentModel.DataAnnotations;
namespace MyTaigaAPI.DTOs.Projects;
public class WipLimitRequest
{
    [Range(1,int.MaxValue)] public int WipLimit { get; set; }
}
