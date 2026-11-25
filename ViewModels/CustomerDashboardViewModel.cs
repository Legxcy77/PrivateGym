using PrivateGym.Models;

namespace PrivateGym.ViewModels;

public class CustomerDashboardViewModel
{
    public int UserId { get; set; }
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string MembershipPackage { get; set; } = "";
    public string HomeBranch { get; set; } = "";
    public bool IsVip { get; set; }
    public string PodQuotaText { get; set; } = "";
    public string PtQuotaText { get; set; } = "";
    public string InBodyQuotaText { get; set; } = "";
    public string QuotaBadgeText { get; set; } = "";
    public string? Gender { get; set; }
    public decimal? Height { get; set; }
    public decimal? Weight { get; set; }
    public string? ExperienceLevel { get; set; }
    public string? FitnessGoal { get; set; }
    public int DaysRemaining { get; set; } = 30;
    public string CycleDateText { get; set; } = "";

    // ข้อมูลการจองล่าสุดที่กำลังจะมาถึง
    public Booking? NextBooking { get; set; }
}
