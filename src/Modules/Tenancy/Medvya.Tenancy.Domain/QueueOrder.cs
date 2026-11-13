using System.Text.Json.Serialization;

namespace Medvya.Tenancy.Domain;

/// <summary>
/// How the clinic sees its arrived patients (MDV-P1-103), one rule for the doctor's day, the board and the queue display: by arrival
/// (check-in time, the default), by token or by appointment time. A walk-in always counts from its arrival, never from a far slot.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<QueueOrder>))]
public enum QueueOrder { Arrival, Token, Appointment }
