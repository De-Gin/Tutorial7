using System.ComponentModel.DataAnnotations;

namespace ClinicApi.DTOs;

public class UpdateAppointmentRequestDto
{
    [Required]
    public int IdPatient { get; set; }

    [Required]
    public int IdDoctor { get; set; }

    [Required]
    public DateTime AppointmentDate { get; set; }

    [Required]
    [StringLength(20)]
    public string Status { get; set; } = string.Empty;

    [Required]
    [StringLength(250, MinimumLength = 1)]
    public string Reason { get; set; } = string.Empty;

    [StringLength(1000)]
    public string? InternalNotes { get; set; }
}