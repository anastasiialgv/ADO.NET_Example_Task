namespace WebApplication.Controllers;

public class UpdateAppointmentRequestDto : CreateAppointmentRequestDto {
    public string Status { get; set; } = string.Empty;
}