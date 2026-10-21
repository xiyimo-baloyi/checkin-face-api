using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace Africanideasfaceapi.Validation;

public class SouthAfricanIdAttribute : ValidationAttribute
{
    public string CountryProperty { get; set; } = "Country";

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        // 1. Get Country Value
        var property = validationContext.ObjectType.GetProperty(CountryProperty);
        if (property == null)
        {
            return new ValidationResult($"Unknown property: {CountryProperty}");
        }

        var countryValue = property.GetValue(validationContext.ObjectInstance) as string;

        // 2. Only validate if Country is "South Africa" (or null/empty, defaulting to SA)
        if (!string.IsNullOrEmpty(countryValue) && countryValue != "South Africa")
        {
            return ValidationResult.Success;
        }

        var idNumber = value as string;
        if (string.IsNullOrWhiteSpace(idNumber))
        {
            // If it's required, [Required] attribute should handle it. 
            // Here we just say if it's there, it must be valid.
            return ValidationResult.Success; 
        }

        // 3. Regex Check (13 numeric digits)
        if (!Regex.IsMatch(idNumber, @"^\d{13}$"))
        {
            return new ValidationResult("South African ID must be 13 digits.");
        }

        // 4. Date Validation (YYMMDD)
        if (!IsValidDate(idNumber.Substring(0, 6)))
        {
             return new ValidationResult("Invalid birth date in ID number.");
        }

        // 5. Luhn Algorithm Check
        if (!IsValidLuhn(idNumber))
        {
            return new ValidationResult("Invalid ID number (Checksum failed).");
        }

        return ValidationResult.Success;
    }

    private bool IsValidDate(string datePart)
    {
        // YYMMDD
        int yy = int.Parse(datePart.Substring(0, 2));
        int mm = int.Parse(datePart.Substring(2, 2));
        int dd = int.Parse(datePart.Substring(4, 2));

        // Simple check for safe ranges
        // We simulate a date creation to be sure
        // Guess century: 
        // 00-24 -> 2000-2024? 
        // 25-99 -> 1925-1999?
        // This is heuristic but sufficient for basic validation
        // More robust: Compare with DateOfBirth field if available
        
        int year = (yy >= 0 && yy <= 24) ? 2000 + yy : 1900 + yy;

        try 
        {
            var date = new DateTime(year, mm, dd);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private bool IsValidLuhn(string id)
    {
        int sum = 0;
        bool alternate = false;
        for (int i = id.Length - 1; i >= 0; i--)
        {
            char c = id[i];
            int n = (int)(c - '0');
            // From right to left, double every second digit
            // Since array is 0-indexed and length is 13 (odd), 
            // index 12 (last digit) is the check digit (not doubled).
            // Actually Luhn counts from right: 1st (rightmost) is check digit.
            // Using standard implementation:
            
            if (alternate)
            {
                n *= 2;
                if (n > 9)
                {
                    n = (n % 10) + 1;
                }
            }
            sum += n;
            alternate = !alternate;
        }
        return (sum % 10 == 0);
    }
}
