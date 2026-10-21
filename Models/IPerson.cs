namespace Africanideasfaceapi.Models;

/// <summary>
/// Shared person contract implemented by both <see cref="User"/> and <see cref="Worker"/>.
/// This allows services to work with either type without duplicating logic,
/// while keeping the existing concrete models and persistence shape unchanged.
/// </summary>
public interface IPerson
{
    string Id { get; }

    string Name { get; set; }

    string IdNumber { get; set; }

    string Email { get; set; }

    string Role { get; set; }

    DateTime? RegisteredDate { get; set; }

    bool? IsActive { get; set; }

    bool? HasConsented { get; set; }

    DateTime? ConsentDate { get; set; }
}

