using System;
using System.Collections.Generic;

namespace PrivateGym.ViewModels;

public class AdminDashboardViewModel
{
    // Executive Top KPIs
    public decimal MonthlyRevenue { get; set; }
    public decimal RevenueTarget { get; set; }
    public decimal MomGrowthPercent { get; set; }
    public int ActiveClients { get; set; }
    public int MaxSlots { get; set; }
    public decimal CapacityPercent { get; set; }
    public int TodayBookings { get; set; }
    public int CheckInDone { get; set; }
    public int CheckInPercent { get; set; }
    public int ExpiringIn7Days { get; set; }
    public int ExpiringToday { get; set; }
    public int IssueCount { get; set; }
    public string IssueSummary { get; set; } = "";

    // Member Lifecycle KPIs
    public int NewThisMonth { get; set; }
    public int TargetNew { get; set; }
    public int PendingAuditCount { get; set; }
    public decimal PendingAuditAmount { get; set; }

    // Counts for Tabs
    public int TotalCount { get; set; }
    public int ActiveCount { get; set; }
    public int ExpiringCount { get; set; }
    public int SuspendedCount { get; set; }
    public int ExpiredCount { get; set; }

    public List<AdminMemberRowViewModel> Members { get; set; } = new();
    public List<AdminActionItemViewModel> ActionItems { get; set; } = new();
    public List<AdminRevenueShareItem> RevenueShare { get; set; } = new();
    public decimal AverageArpu { get; set; }
    public decimal ArpuGrowthPercent { get; set; }
}

public class AdminMemberRowViewModel
{
    public int UserId { get; set; }
    public string MemberCode { get; set; } = ""; // e.g. PG-88219
    public string FullName { get; set; } = "";
    public string AvatarInitials { get; set; } = "";
    public string? AvatarUrl { get; set; }
    public bool IsVerified { get; set; } = true;
    public string Phone { get; set; } = "";
    public string Email { get; set; } = "";
    public string BranchName { get; set; } = "";

    public string PackageName { get; set; } = "PERFORMANCE PRO";
    public string PackageTierClass { get; set; } = "pro"; // elite, pro, essential

    public string StatusText { get; set; } = "กำลังใช้งาน";
    public string StatusClass { get; set; } = "active"; // active, warning, suspended, expired

    // Quotas
    public int RemainingPods { get; set; }
    public int TotalPods { get; set; } = 20;
    public int RemainingPt { get; set; }
    public int TotalPt { get; set; } = 8;
    public string QuotaDisplay { get; set; } = "";
    public int PodProgressPercent { get; set; }

    // Dates
    public string StartDateFormatted { get; set; } = "";
    public string EndDateFormatted { get; set; } = "";
    public int DaysRemaining { get; set; }
    public string ExpiryNote { get; set; } = "";
    public string ExpiryNoteClass { get; set; } = "green"; // green, orange, red, dim

    // Finance
    public decimal LifetimeValue { get; set; }
    public string LastPaymentText { get; set; } = "";
    public string PaymentMethodText { get; set; } = "";

    // Action button
    public string PrimaryActionText { get; set; } = "เติมโควต้า";
    public string PrimaryActionClass { get; set; } = "ghost"; // ghost, red
}

public class AdminActionItemViewModel
{
    public string Title { get; set; } = "";
    public string BadgeText { get; set; } = "";
    public string BadgeClass { get; set; } = "deadline";
    public string Description { get; set; } = "";
    public List<AdminActionLink> Links { get; set; } = new();
}

public class AdminActionLink
{
    public string Label { get; set; } = "";
    public string Url { get; set; } = "#";
    public string Icon { get; set; } = "";
}

public class AdminRevenueShareItem
{
    public string PackageName { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public decimal Amount { get; set; }
    public int Percentage { get; set; }
    public string BarClass { get; set; } = "red";
}
