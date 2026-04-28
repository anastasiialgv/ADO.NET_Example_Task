using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using WebApplication.DTOs;

namespace WebApplication.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AppointmentsController : ControllerBase
{
    private readonly IConfiguration _configuration;

    public AppointmentsController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? status,
                                                        [FromQuery] string? patientLastName)
    {
        var appointments = new List<AppointmentListDto>();
        string connectionString = _configuration
            .GetConnectionString("DefaultConnection")!;
        
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        var query = """
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
        await using var command = new SqlCommand(query, connection);
        
        command.Parameters.AddWithValue("@Status", (object?)status ?? DBNull.Value);
        command.Parameters.AddWithValue("@PatientLastName", (object?)patientLastName ?? DBNull.Value);

        await using var reader =  await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            appointments.Add(new AppointmentListDto
            {
                IdAppointment = reader.GetInt32(0),
                AppointmentDate = reader.GetDateTime(1),
                Status = reader.GetString(2),
                Reason = reader.GetString(3),
                PatientFullName = reader.GetString(4),
                PatientEmail = reader.GetString(5)
            });
        }
        return Ok(appointments);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById([FromRoute] int id)
    {
        string connectionString = _configuration.GetConnectionString("DefaultConnection")!;
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        var query = """
                    SELECT
                        a.IdAppointment,
                        a.AppointmentDate,
                        a.Status,
                        a.Reason,
                        p.FirstName + ' ' + p.LastName AS PatientFullName,
                        p.Email AS PatientEmail
                    FROM Appointments a
                    JOIN Patients p ON a.IdPatient = p.IdPatient
                    WHERE a.IdAppointment = @Id
                    """;
        await using var command = new SqlCommand(query, connection);
        command.Parameters.AddWithValue("@Id", id);
        await using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            var dto = new AppointmentListDto
            {
                IdAppointment = reader.GetInt32(0),
                AppointmentDate = reader.GetDateTime(1),
                Status = reader.GetString(2),
                Reason = reader.GetString(3),
                PatientFullName = reader.GetString(4),
                PatientEmail = reader.GetString(5)
            };

            return Ok(dto);
        }

        return NotFound(new { message = $"Appointment {id} not found." });
    }

    [HttpPost]
public async Task<IActionResult> CreateAppointment([FromBody] CreateAppointmentRequestDto request)
{
    if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 250)
        return BadRequest("Reason is required and must be up to 250 characters.");

    if (request.AppointmentDate < DateTime.Now)
        return BadRequest("Appointment date cannot be in the past.");

    string connectionString = _configuration.GetConnectionString("DefaultConnection")!;
    await using var connection = new SqlConnection(connectionString);
    await connection.OpenAsync();

    var checkQuery = """
        SELECT 
            (SELECT COUNT(*) FROM Patients WHERE IdPatient = @IdPatient) as PatientExists,
            (SELECT COUNT(*) FROM Doctors WHERE IdDoctor = @IdDoctor) as DoctorExists,
            (SELECT COUNT(*) FROM Appointments WHERE IdDoctor = @IdDoctor AND AppointmentDate = @Date) as Conflict
        """;

    await using var checkCmd = new SqlCommand(checkQuery, connection);
    checkCmd.Parameters.AddWithValue("@IdPatient", request.IdPatient);
    checkCmd.Parameters.AddWithValue("@IdDoctor", request.IdDoctor);
    checkCmd.Parameters.AddWithValue("@Date", request.AppointmentDate);

    await using var reader = await checkCmd.ExecuteReaderAsync();
    if (await reader.ReadAsync())
    {
        if (reader.GetInt32(0) == 0) return BadRequest("Patient not found.");
        if (reader.GetInt32(1) == 0) return BadRequest("Doctor not found.");
        if (reader.GetInt32(2) > 0) return Conflict("Doctor already has an appointment at this time.");
    }
    await reader.CloseAsync();

    var insertQuery = """
        INSERT INTO Appointments (IdPatient, IdDoctor, AppointmentDate, Status, Reason)
        VALUES (@IdPatient, @IdDoctor, @Date, 'Scheduled', @Reason);
        """;

    await using var insertCmd = new SqlCommand(insertQuery, connection);
    insertCmd.Parameters.AddWithValue("@IdPatient", request.IdPatient);
    insertCmd.Parameters.AddWithValue("@IdDoctor", request.IdDoctor);
    insertCmd.Parameters.AddWithValue("@Date", request.AppointmentDate);
    insertCmd.Parameters.AddWithValue("@Reason", request.Reason);

    await insertCmd.ExecuteNonQueryAsync();

    return StatusCode(201);
    }
    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateAppointment(int id, [FromBody] UpdateAppointmentRequestDto request)
    {
        string connectionString = _configuration.GetConnectionString("DefaultConnection")!;
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        var existQuery = "SELECT Status, AppointmentDate FROM Appointments WHERE IdAppointment = @Id";
        await using var existCmd = new SqlCommand(existQuery, connection);
        existCmd.Parameters.AddWithValue("@Id", id);

        await using var reader = await existCmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return NotFound();

        string currentStatus = reader.GetString(0);
        DateTime currentDate = reader.GetDateTime(1);
        await reader.CloseAsync();

        if (currentStatus == "Completed" && currentDate != request.AppointmentDate)
            return BadRequest("Cannot change date of a completed appointment.");

        var updateQuery = """
                          UPDATE Appointments
                          SET IdPatient = @IdPatient,
                              IdDoctor = @IdDoctor,
                              AppointmentDate = @Date,
                              Status = @Status,
                              Reason = @Reason
                          WHERE IdAppointment = @Id
                          """;

        await using var updateCmd = new SqlCommand(updateQuery, connection);
        updateCmd.Parameters.AddWithValue("@Id", id);
        updateCmd.Parameters.AddWithValue("@IdPatient", request.IdPatient);
        updateCmd.Parameters.AddWithValue("@IdDoctor", request.IdDoctor);
        updateCmd.Parameters.AddWithValue("@Date", request.AppointmentDate);
        updateCmd.Parameters.AddWithValue("@Status", request.Status);
        updateCmd.Parameters.AddWithValue("@Reason", request.Reason);

        await updateCmd.ExecuteNonQueryAsync();
        return Ok();
    
    }
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteAppointment(int id)
    {
        string connectionString = _configuration.GetConnectionString("DefaultConnection")!;
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        var statusQuery = "SELECT Status FROM Appointments WHERE IdAppointment = @Id";
        await using var statusCmd = new SqlCommand(statusQuery, connection);
        statusCmd.Parameters.AddWithValue("@Id", id);

        var result = await statusCmd.ExecuteScalarAsync();

        if (result == null) return NotFound();
        if (result.ToString() == "Completed") return Conflict("Cannot delete a completed appointment.");

        var deleteQuery = "DELETE FROM Appointments WHERE IdAppointment = @Id";
        await using var deleteCmd = new SqlCommand(deleteQuery, connection);
        deleteCmd.Parameters.AddWithValue("@Id", id);

        await deleteCmd.ExecuteNonQueryAsync();
        return NoContent();
    }
    
}