using ClinicApi.DTOs;
using ClinicApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace ClinicApi.Controllers;

[ApiController]
[Route("api/appointments")]
public class AppointmentsController : ControllerBase
{
    private readonly IAppointmentService _service;

    public AppointmentsController(IAppointmentService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? status,
        [FromQuery] string? patientLastName)
    {
        var appointments = await _service.GetAppointmentsAsync(status, patientLastName);
        return Ok(appointments);
    }

    [HttpGet("{idAppointment:int}")]
    public async Task<IActionResult> GetById(int idAppointment)
    {
        var appointment = await _service.GetAppointmentByIdAsync(idAppointment);
        if (appointment is null)
            return NotFound(new ErrorResponseDto($"Appointment with id {idAppointment} was not found."));

        return Ok(appointment);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateAppointmentRequestDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(new ErrorResponseDto("Invalid input."));

        try
        {
            var newId = await _service.CreateAppointmentAsync(dto);
            return CreatedAtAction(nameof(GetById), new { idAppointment = newId }, new { idAppointment = newId });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ErrorResponseDto(ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new ErrorResponseDto(ex.Message));
        }
    }

    [HttpPut("{idAppointment:int}")]
    public async Task<IActionResult> Update(int idAppointment, [FromBody] UpdateAppointmentRequestDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(new ErrorResponseDto("Invalid input."));

        try
        {
            var updated = await _service.UpdateAppointmentAsync(idAppointment, dto);
            if (!updated)
                return NotFound(new ErrorResponseDto($"Appointment with id {idAppointment} was not found."));

            return Ok();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ErrorResponseDto(ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new ErrorResponseDto(ex.Message));
        }
    }

    [HttpDelete("{idAppointment:int}")]
    public async Task<IActionResult> Delete(int idAppointment)
    {
        try
        {
            var deleted = await _service.DeleteAppointmentAsync(idAppointment);
            if (!deleted)
                return NotFound(new ErrorResponseDto($"Appointment with id {idAppointment} was not found."));

            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new ErrorResponseDto(ex.Message));
        }
    }
}