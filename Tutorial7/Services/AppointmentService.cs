using System.Data;
using ClinicApi.DTOs;
using Microsoft.Data.SqlClient;

namespace ClinicApi.Services;

public class AppointmentService : IAppointmentService
{
    private readonly string _connectionString;
    private static readonly string[] AllowedStatuses = { "Scheduled", "Completed", "Cancelled" };

    public AppointmentService(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
    }

    public async Task<IEnumerable<AppointmentListDto>> GetAppointmentsAsync(string? status, string? patientLastName)
    {
        var result = new List<AppointmentListDto>();

        const string sql = """
            SELECT
                a.IdAppointment,
                a.AppointmentDate,
                a.Status,
                a.Reason,
                p.FirstName + N' ' + p.LastName AS PatientFullName,
                p.Email AS PatientEmail
            FROM dbo.Appointments a
            JOIN dbo.Patients p ON p.IdPatient = a.IdPatient
            WHERE (@Status IS NULL OR a.Status = @Status)
              AND (@PatientLastName IS NULL OR p.LastName = @PatientLastName)
            ORDER BY a.AppointmentDate;
            """;

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@Status", SqlDbType.NVarChar, 20).Value =
            string.IsNullOrWhiteSpace(status) ? DBNull.Value : status;
        command.Parameters.Add("@PatientLastName", SqlDbType.NVarChar, 100).Value =
            string.IsNullOrWhiteSpace(patientLastName) ? DBNull.Value : patientLastName;

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new AppointmentListDto
            {
                IdAppointment = reader.GetInt32(reader.GetOrdinal("IdAppointment")),
                AppointmentDate = reader.GetDateTime(reader.GetOrdinal("AppointmentDate")),
                Status = reader.GetString(reader.GetOrdinal("Status")),
                Reason = reader.GetString(reader.GetOrdinal("Reason")),
                PatientFullName = reader.GetString(reader.GetOrdinal("PatientFullName")),
                PatientEmail = reader.GetString(reader.GetOrdinal("PatientEmail"))
            });
        }

        return result;
    }

    public async Task<AppointmentDetailsDto?> GetAppointmentByIdAsync(int idAppointment)
    {
        const string sql = """
            SELECT
                a.IdAppointment,
                a.AppointmentDate,
                a.Status,
                a.Reason,
                a.InternalNotes,
                a.CreatedAt,
                p.IdPatient,
                p.FirstName + N' ' + p.LastName AS PatientFullName,
                p.Email AS PatientEmail,
                p.PhoneNumber AS PatientPhone,
                d.IdDoctor,
                d.FirstName + N' ' + d.LastName AS DoctorFullName,
                d.LicenseNumber AS DoctorLicenseNumber,
                s.Name AS SpecializationName
            FROM dbo.Appointments a
            JOIN dbo.Patients p ON p.IdPatient = a.IdPatient
            JOIN dbo.Doctors d ON d.IdDoctor = a.IdDoctor
            JOIN dbo.Specializations s ON s.IdSpecialization = d.IdSpecialization
            WHERE a.IdAppointment = @IdAppointment;
            """;

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@IdAppointment", SqlDbType.Int).Value = idAppointment;

        await using var reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
            return null;

        var notesOrdinal = reader.GetOrdinal("InternalNotes");
        var phoneOrdinal = reader.GetOrdinal("PatientPhone");

        return new AppointmentDetailsDto
        {
            IdAppointment = reader.GetInt32(reader.GetOrdinal("IdAppointment")),
            AppointmentDate = reader.GetDateTime(reader.GetOrdinal("AppointmentDate")),
            Status = reader.GetString(reader.GetOrdinal("Status")),
            Reason = reader.GetString(reader.GetOrdinal("Reason")),
            InternalNotes = reader.IsDBNull(notesOrdinal) ? null : reader.GetString(notesOrdinal),
            CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
            IdPatient = reader.GetInt32(reader.GetOrdinal("IdPatient")),
            PatientFullName = reader.GetString(reader.GetOrdinal("PatientFullName")),
            PatientEmail = reader.GetString(reader.GetOrdinal("PatientEmail")),
            PatientPhone = reader.IsDBNull(phoneOrdinal) ? null : reader.GetString(phoneOrdinal),
            IdDoctor = reader.GetInt32(reader.GetOrdinal("IdDoctor")),
            DoctorFullName = reader.GetString(reader.GetOrdinal("DoctorFullName")),
            DoctorLicenseNumber = reader.GetString(reader.GetOrdinal("DoctorLicenseNumber")),
            SpecializationName = reader.GetString(reader.GetOrdinal("SpecializationName"))
        };
    }

    public async Task<int> CreateAppointmentAsync(CreateAppointmentRequestDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Reason))
            throw new ArgumentException("Reason is required.");
        if (dto.Reason.Length > 250)
            throw new ArgumentException("Reason cannot exceed 250 characters.");
        if (dto.AppointmentDate < DateTime.Now)
            throw new ArgumentException("Appointment date cannot be in the past.");

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        await EnsurePatientActiveAsync(connection, dto.IdPatient);
        await EnsureDoctorActiveAsync(connection, dto.IdDoctor);
        await EnsureNoDoctorConflictAsync(connection, dto.IdDoctor, dto.AppointmentDate, excludeAppointmentId: null);

        const string insertSql = """
            INSERT INTO dbo.Appointments
                (IdPatient, IdDoctor, AppointmentDate, Status, Reason, CreatedAt)
            OUTPUT INSERTED.IdAppointment
            VALUES
                (@IdPatient, @IdDoctor, @AppointmentDate, N'Scheduled', @Reason, SYSDATETIME());
            """;

        await using var command = new SqlCommand(insertSql, connection);
        command.Parameters.Add("@IdPatient", SqlDbType.Int).Value = dto.IdPatient;
        command.Parameters.Add("@IdDoctor", SqlDbType.Int).Value = dto.IdDoctor;
        command.Parameters.Add("@AppointmentDate", SqlDbType.DateTime2).Value = dto.AppointmentDate;
        command.Parameters.Add("@Reason", SqlDbType.NVarChar, 250).Value = dto.Reason;

        var newId = (int)(await command.ExecuteScalarAsync())!;
        return newId;
    }

    public async Task<bool> UpdateAppointmentAsync(int idAppointment, UpdateAppointmentRequestDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Reason))
            throw new ArgumentException("Reason is required.");
        if (dto.Reason.Length > 250)
            throw new ArgumentException("Reason cannot exceed 250 characters.");
        if (!AllowedStatuses.Contains(dto.Status))
            throw new ArgumentException($"Status must be one of: {string.Join(", ", AllowedStatuses)}.");

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        var existing = await GetAppointmentBaseAsync(connection, idAppointment);
        if (existing is null)
            return false;

        await EnsurePatientActiveAsync(connection, dto.IdPatient);
        await EnsureDoctorActiveAsync(connection, dto.IdDoctor);

        var dateChanged = existing.Value.AppointmentDate != dto.AppointmentDate;

        if (existing.Value.Status == "Completed" && dateChanged)
            throw new InvalidOperationException("Cannot change the date of a completed appointment.");

        if (dateChanged)
            await EnsureNoDoctorConflictAsync(connection, dto.IdDoctor, dto.AppointmentDate, excludeAppointmentId: idAppointment);

        const string updateSql = """
            UPDATE dbo.Appointments
            SET IdPatient = @IdPatient,
                IdDoctor = @IdDoctor,
                AppointmentDate = @AppointmentDate,
                Status = @Status,
                Reason = @Reason,
                InternalNotes = @InternalNotes
            WHERE IdAppointment = @IdAppointment;
            """;

        await using var command = new SqlCommand(updateSql, connection);
        command.Parameters.Add("@IdPatient", SqlDbType.Int).Value = dto.IdPatient;
        command.Parameters.Add("@IdDoctor", SqlDbType.Int).Value = dto.IdDoctor;
        command.Parameters.Add("@AppointmentDate", SqlDbType.DateTime2).Value = dto.AppointmentDate;
        command.Parameters.Add("@Status", SqlDbType.NVarChar, 20).Value = dto.Status;
        command.Parameters.Add("@Reason", SqlDbType.NVarChar, 250).Value = dto.Reason;
        command.Parameters.Add("@InternalNotes", SqlDbType.NVarChar, 1000).Value =
            string.IsNullOrWhiteSpace(dto.InternalNotes) ? DBNull.Value : dto.InternalNotes;
        command.Parameters.Add("@IdAppointment", SqlDbType.Int).Value = idAppointment;

        await command.ExecuteNonQueryAsync();
        return true;
    }

    public async Task<bool> DeleteAppointmentAsync(int idAppointment)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        var existing = await GetAppointmentBaseAsync(connection, idAppointment);
        if (existing is null)
            return false;

        if (existing.Value.Status == "Completed")
            throw new InvalidOperationException("Cannot delete an appointment that is already completed.");

        const string deleteSql = "DELETE FROM dbo.Appointments WHERE IdAppointment = @IdAppointment;";

        await using var command = new SqlCommand(deleteSql, connection);
        command.Parameters.Add("@IdAppointment", SqlDbType.Int).Value = idAppointment;

        await command.ExecuteNonQueryAsync();
        return true;
    }



    private static async Task<(DateTime AppointmentDate, string Status)?> GetAppointmentBaseAsync(
        SqlConnection connection, int idAppointment)
    {
        const string sql = """
            SELECT AppointmentDate, Status
            FROM dbo.Appointments
            WHERE IdAppointment = @IdAppointment;
            """;

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@IdAppointment", SqlDbType.Int).Value = idAppointment;

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;

        return (reader.GetDateTime(0), reader.GetString(1));
    }

    private static async Task EnsurePatientActiveAsync(SqlConnection connection, int idPatient)
    {
        const string sql = "SELECT IsActive FROM dbo.Patients WHERE IdPatient = @IdPatient;";
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@IdPatient", SqlDbType.Int).Value = idPatient;

        var result = await command.ExecuteScalarAsync();
        if (result is null)
            throw new ArgumentException($"Patient with id {idPatient} does not exist.");
        if (result is bool isActive && !isActive)
            throw new ArgumentException($"Patient with id {idPatient} is not active.");
    }

    private static async Task EnsureDoctorActiveAsync(SqlConnection connection, int idDoctor)
    {
        const string sql = "SELECT IsActive FROM dbo.Doctors WHERE IdDoctor = @IdDoctor;";
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@IdDoctor", SqlDbType.Int).Value = idDoctor;

        var result = await command.ExecuteScalarAsync();
        if (result is null)
            throw new ArgumentException($"Doctor with id {idDoctor} does not exist.");
        if (result is bool isActive && !isActive)
            throw new ArgumentException($"Doctor with id {idDoctor} is not active.");
    }

    private static async Task EnsureNoDoctorConflictAsync(
        SqlConnection connection, int idDoctor, DateTime appointmentDate, int? excludeAppointmentId)
    {
        const string sql = """
            SELECT COUNT(1)
            FROM dbo.Appointments
            WHERE IdDoctor = @IdDoctor
              AND AppointmentDate = @AppointmentDate
              AND Status = N'Scheduled'
              AND (@ExcludeId IS NULL OR IdAppointment <> @ExcludeId);
            """;

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@IdDoctor", SqlDbType.Int).Value = idDoctor;
        command.Parameters.Add("@AppointmentDate", SqlDbType.DateTime2).Value = appointmentDate;
        command.Parameters.Add("@ExcludeId", SqlDbType.Int).Value =
            excludeAppointmentId.HasValue ? excludeAppointmentId.Value : DBNull.Value;

        var count = (int)(await command.ExecuteScalarAsync())!;
        if (count > 0)
            throw new InvalidOperationException("The doctor already has another scheduled appointment at this time.");
    }
}