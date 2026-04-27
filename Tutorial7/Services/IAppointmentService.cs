using ClinicApi.DTOs;

namespace ClinicApi.Services;

public interface IAppointmentService
{
    Task<IEnumerable<AppointmentListDto>> GetAppointmentsAsync(string? status, string? patientLastName);
    Task<AppointmentDetailsDto?> GetAppointmentByIdAsync(int idAppointment);
    Task<int> CreateAppointmentAsync(CreateAppointmentRequestDto dto);
    Task<bool> UpdateAppointmentAsync(int idAppointment, UpdateAppointmentRequestDto dto);
    Task<bool> DeleteAppointmentAsync(int idAppointment);
}