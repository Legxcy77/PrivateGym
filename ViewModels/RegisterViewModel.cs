using System.ComponentModel.DataAnnotations;

namespace PrivateGym.ViewModels;

public class RegisterViewModel
{
    public string FullName { get; set; }
    public string Email { get; set; }
    [RegularExpression(@"^[0-9]{10}$", ErrorMessage = "เบอร์โทรศัพท์ต้องเป็นตัวเลข 10 หลัก")]
    public string Phone { get; set; }
    [RegularExpression(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d).{8,}$",
     ErrorMessage = "รหัสผ่านต้องมีอย่างน้อย 8 ตัวอักษร มีตัวพิมพ์ใหญ่ ตัวพิมพ์เล็ก และตัวเลข")]
    public string Password { get; set; }
    public string? ConfirmPassword { get; set; }
    public string? Gender { get; set; }
    public decimal? Height { get; set; }
    public decimal? Weight { get; set; }
    public string? ExperienceLevel { get; set; }
    public string? FitnessGoal { get; set; }
    public string? MembershipPackage { get; set; }
    public string? HomeBranch { get; set; }
    public string? PaymentMethod { get; set; }
    public string? TransactionRef { get; set; }
    public string? BankTransferRef { get; set; }
    public string? TaxId { get; set; }
    public string? BillingAddress { get; set; }
}