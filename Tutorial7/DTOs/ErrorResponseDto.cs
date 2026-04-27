namespace ClinicApi.DTOs;

public class ErrorResponseDto
{
    public string Message { get; set; } = string.Empty;
    public List<string>? Details { get; set; }

    public ErrorResponseDto(string message)
    {
        Message = message;
    }

    public ErrorResponseDto(string message, List<string> details)
    {
        Message = message;
        Details = details;
    }
}