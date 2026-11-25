using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PrivateGym.Models;
using PrivateGym.ViewModels;

namespace PrivateGym.Controllers;

using PrivateGym.Filters;

[SessionAuthorize("Admin")]
public class AdminController : Controller
{
    private readonly Csi402dbContext _db; // ตัวเชื่อมต่อฐานข้อมูล MySQL ผ่าน EF Core

    public AdminController(Csi402dbContext db)
    {
        _db = db;
    }

    // ================================================================
    // 1. DASHBOARD & MEMBER DIRECTORY (/Admin & /Admin/UserList)
    // ================================================================
    // หน้า Dashboard หลักของ Admin: แสดง KPI รายได้, สมาชิก, และสถานะทรัพยากร
    public IActionResult Index(string? search, string? status, string? pkg)
    {
        Csi402dbContext.EnsureAllBranchesPodsAndTrainersSeeded(_db);
        ViewData["Username"] = "Admin";
        var model = BuildAdminDashboardData(search, status, pkg, activeTab: "all");
        ViewBag.Search = search ?? "";
        ViewBag.Status = status ?? "all";
        ViewBag.Pkg = pkg ?? "all";
        ViewBag.AllPackages = _db.Packages.OrderBy(p => p.Price).ToList();
        return View(model);
    }

    // หน้าจัดการสมาชิก (Members): กรองแท็บ, ค้นหา, และแบ่งหน้า Pagination จริง
    public IActionResult UserList(string? search, string? status, string? pkg, string? tab = "all", int page = 1, int pageSize = 10)
    {
        Csi402dbContext.EnsureAllBranchesPodsAndTrainersSeeded(_db);
        ViewData["Username"] = "Admin";
        if (pageSize != 10 && pageSize != 25 && pageSize != 50) pageSize = 10;

        var model = BuildAdminDashboardData(search, status, pkg, activeTab: tab ?? "all");
        int filteredCount = model.Members.Count;
        int totalPages = Math.Max(1, (int)Math.Ceiling(filteredCount / (double)pageSize));
        page = Math.Clamp(page, 1, totalPages);

        model.Members = model.Members
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        ViewBag.ActiveTab = tab ?? "all";
        ViewBag.Search = search;
        ViewBag.Status = status;
        ViewBag.Pkg = pkg;
        ViewBag.Page = page;
        ViewBag.PageSize = pageSize;
        ViewBag.TotalPages = totalPages;
        ViewBag.FilteredCount = filteredCount;
        ViewBag.AllPackages = _db.Packages.OrderBy(p => p.Price).ToList();
        return View(model);
    }

    public IActionResult Members(string? search, string? status, string? pkg, string? tab = "all")
    {
        return RedirectToAction("UserList", new { search, status, pkg, tab });
    }

    // คำนวณข้อมูล Dashboard และตารางสมาชิกทั้งหมดจากฐานข้อมูลจริง
    private AdminDashboardViewModel BuildAdminDashboardData(string? search, string? status, string? pkg, string activeTab)
    {
        var todayDate = DateOnly.FromDateTime(DateTime.Today);

        // ดึงเฉพาะสมาชิก (RoleId == 4) จากฐานข้อมูลจริง ไม่เอา Admin/Staff/Trainer มาปน
        var dbUsers = _db.Users
            .Include(u => u.HomeBranch)
            .Include(u => u.Role)
            .Include(u => u.MemberSubscriptions)
                .ThenInclude(ms => ms.Package)
            .Include(u => u.Payments)
            .Where(u => (u.Role != null && u.Role.RoleName == "Member"))
            .OrderByDescending(u => u.UserId)
            .ToList();

        var memberRows = new List<AdminMemberRowViewModel>();

        foreach (var u in dbUsers)
        {
            var activeSub = u.MemberSubscriptions
                .OrderByDescending(s => s.Status == "Active")
                .ThenByDescending(s => s.SubscriptionId)
                .FirstOrDefault();

            var lastPayment = u.Payments
                .OrderByDescending(p => p.PaymentDate)
                .ThenByDescending(p => p.PaymentId)
                .FirstOrDefault();

            string pkgName = activeSub?.Package?.PackageName ?? "ยังไม่มีแพ็กเกจ";
            string tierClass = "pro";
            if (pkgName.Contains("Elite", StringComparison.OrdinalIgnoreCase) ||
                pkgName.Contains("VIP", StringComparison.OrdinalIgnoreCase) ||
                pkgName.Contains("Concierge", StringComparison.OrdinalIgnoreCase))
            {
                tierClass = "elite";
            }
            else if (pkgName.Contains("Starter", StringComparison.OrdinalIgnoreCase) ||
                     pkgName.Contains("Essential", StringComparison.OrdinalIgnoreCase) ||
                     activeSub == null)
            {
                tierClass = "essential";
            }

            int remainingPods = activeSub?.RemainingPods ?? 0;
            int totalPods = activeSub?.Package?.PodQuota ?? (remainingPods > 0 ? remainingPods : 10);
            if (remainingPods > totalPods) totalPods = remainingPods;
            int remainingPt = activeSub?.RemainingPt ?? 0;
            int totalPt = activeSub?.Package?.PtQuota ?? 0;

            int daysRemaining = 0;
            if (activeSub != null)
            {
                daysRemaining = activeSub.EndDate.DayNumber - todayDate.DayNumber;
                if (daysRemaining < 0) daysRemaining = 0;
            }

            bool isInactive = u.Status == "Inactive" || u.Status == "โดนลบแล้ว" || u.Status == "Suspended";

            string statusText = "กำลังใช้งาน";
            string statusClass = "active";
            string expiryClass = "green";
            string expiryNote = activeSub != null ? $"เหลือ {daysRemaining} วัน" : "ยังไม่มีแพ็กเกจ";
            string actionText = "";
            string actionClass = "ghost";

            if (isInactive)
            {
                statusText = "ปิดการใช้งาน";
                statusClass = "suspended";
                expiryClass = "red";
                expiryNote = "ปิดการใช้งาน (Inactive)";
                actionText = "เปิดใช้งาน";
                actionClass = "ghost";
            }
            else if (activeSub == null || daysRemaining <= 0)
            {
                statusText = "หมดอายุแล้ว";
                statusClass = "expired";
                expiryClass = "red";
                expiryNote = activeSub == null ? "ยังไม่สมัครแพ็กเกจ" : "หมดอายุแล้ว";
                actionText = "ต่ออายุ";
                actionClass = "red";
            }
            else if (daysRemaining <= 7)
            {
                statusText = "รอต่ออายุ";
                statusClass = "warning";
                expiryClass = "orange";
                expiryNote = $"เหลือ {daysRemaining} วัน";
                actionText = "ต่ออายุ";
                actionClass = "red";
            }

            decimal ltv = u.Payments.Where(p => p.Status == "Success").Sum(p => (decimal?)p.Amount) ?? 0m;
            string cleanName = u.FullName.TrimStart("คุณ".ToCharArray()).Trim();
            string initials = cleanName.Length >= 2 ? cleanName.Substring(0, 2) : "PG";

            memberRows.Add(new AdminMemberRowViewModel
            {
                UserId = u.UserId,
                MemberCode = $"PG-{u.UserId:D5}",
                FullName = u.FullName.StartsWith("คุณ") ? u.FullName : "คุณ" + u.FullName,
                AvatarInitials = initials,
                IsVerified = true,
                Phone = u.Phone,
                Email = u.Email,
                BranchName = u.HomeBranch?.BranchName ?? "สาขาทองหล่อ",
                PackageName = pkgName,
                PackageTierClass = tierClass,
                StatusText = statusText,
                StatusClass = statusClass,
                RemainingPods = remainingPods,
                TotalPods = totalPods,
                RemainingPt = remainingPt,
                TotalPt = totalPt,
                QuotaDisplay = activeSub == null ? "ไม่มีโควต้า" : $"Pods: {remainingPods}/{totalPods}",
                PodProgressPercent = totalPods > 0 ? Math.Min(100, remainingPods * 100 / totalPods) : 0,
                StartDateFormatted = activeSub?.StartDate.ToString("dd/MM/yyyy") ?? "-",
                EndDateFormatted = activeSub?.EndDate.ToString("dd/MM/yyyy") ?? "-",
                DaysRemaining = daysRemaining,
                ExpiryNote = expiryNote,
                ExpiryNoteClass = expiryClass,
                LifetimeValue = ltv,
                LastPaymentText = lastPayment != null ? $"฿{lastPayment.Amount:N0}" : "฿0",
                PaymentMethodText = lastPayment?.PaymentMethod ?? "ยังไม่มีประวัติชำระ",
                PrimaryActionText = actionText,
                PrimaryActionClass = actionClass
            });
        }

        var filteredMembers = memberRows.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            string q = search.Trim().ToLower();
            filteredMembers = filteredMembers.Where(m =>
                m.FullName.ToLower().Contains(q) ||
                m.MemberCode.ToLower().Contains(q) ||
                m.Email.ToLower().Contains(q) ||
                m.Phone.Contains(q));
        }

        if (!string.IsNullOrWhiteSpace(status) && status != "all")
        {
            filteredMembers = filteredMembers.Where(m => m.StatusClass == status || m.StatusText == status);
        }

        if (!string.IsNullOrWhiteSpace(pkg) && pkg != "all")
        {
            filteredMembers = filteredMembers.Where(m =>
                m.PackageTierClass == pkg ||
                m.PackageName.Contains(pkg, StringComparison.OrdinalIgnoreCase));
        }

        if (activeTab == "active") filteredMembers = filteredMembers.Where(m => m.StatusClass == "active");
        else if (activeTab == "expiring") filteredMembers = filteredMembers.Where(m => m.StatusClass == "warning" || (m.DaysRemaining > 0 && m.DaysRemaining <= 7));
        else if (activeTab == "suspended") filteredMembers = filteredMembers.Where(m => m.StatusClass == "suspended");
        else if (activeTab == "expired") filteredMembers = filteredMembers.Where(m => m.StatusClass == "expired");

        // คำนวณสถิติจริงจากฐานข้อมูล MySQL 100%
        var successPayments = _db.Payments
            .Include(p => p.Package)
            .Where(p => p.Status == "Success")
            .ToList();

        decimal realSuccessRevenue = successPayments.Sum(p => p.Amount);
        int realPendingCount = _db.Payments.Count(p => p.Status == "Pending");
        decimal realPendingAmount = _db.Payments.Where(p => p.Status == "Pending").Sum(p => (decimal?)p.Amount) ?? 0m;
        int realTodayBookings = _db.Bookings.Count(b => b.BookingDate == todayDate && b.Status != "Cancelled" && b.User != null && (b.User.Role != null && b.User.Role.RoleName == "Member"));
        int realTodayCheckIns = _db.CheckIns.Count(c => c.CheckInTime.HasValue && c.CheckInTime.Value.Date == DateTime.Today);

        int totalPodsCount = _db.Pods.Count();
        int availPodsCount = _db.Pods.Count(p => p.Status == "Available");
        int maintPodCount = totalPodsCount - availPodsCount;
        int totalTrainersCount = _db.Trainers.Count();
        int busyTrainersToday = _db.Bookings
            .Where(b => b.BookingDate == todayDate && b.Status != "Cancelled" && b.TrainerId != null)
            .Select(b => b.TrainerId)
            .Distinct()
            .Count();
        int totalBranchesCount = _db.Branches.Count();

        ViewBag.TotalPodsCount = totalPodsCount;
        ViewBag.AvailablePodsCount = availPodsCount;
        ViewBag.TotalTrainersCount = totalTrainersCount;
        ViewBag.BusyTrainersToday = busyTrainersToday;
        ViewBag.TotalBranchesCount = totalBranchesCount;

        int totalRows = memberRows.Count;
        int activeRows = memberRows.Count(m => m.StatusClass == "active");
        int expiringRows = memberRows.Count(m => m.StatusClass == "warning" || (m.DaysRemaining > 0 && m.DaysRemaining <= 7));
        int suspendedRows = memberRows.Count(m => m.StatusClass == "suspended");
        int expiredRows = memberRows.Count(m => m.StatusClass == "expired");

        var actionItems = new List<AdminActionItemViewModel>();
        if (realPendingCount > 0)
        {
            actionItems.Add(new AdminActionItemViewModel
            {
                Title = $"บิลรอตรวจสอบจากสมาชิก ({realPendingCount} รายการ)",
                BadgeText = "PENDING AUDIT",
                BadgeClass = "deadline",
                Description = $"ยอดรอการตรวจสอบรวม ฿{realPendingAmount:N0}",
                Links = new List<AdminActionLink> { new() { Label = "เปิดหน้าอนุมัติการชำระเงิน", Url = "/Admin/Payments?status=Pending" } }
            });
        }
        if (maintPodCount > 0)
        {
            actionItems.Add(new AdminActionItemViewModel
            {
                Title = $"ห้องซ้อมอยู่ระหว่างปิดปรับปรุง ({maintPodCount} ห้อง)",
                BadgeText = "MAINTENANCE",
                BadgeClass = "progress",
                Description = "ตรวจสอบและเปิดให้บริการห้องซ้อมได้ที่หน้าแพ็กเกจและห้องซ้อม",
                Links = new List<AdminActionLink> { new() { Label = "จัดการห้องซ้อม", Url = "/Admin/Packages" } }
            });
        }
        if (expiringRows > 0)
        {
            actionItems.Add(new AdminActionItemViewModel
            {
                Title = $"สมาชิกแพ็กเกจใกล้หมดอายุ ({expiringRows} ท่าน)",
                BadgeText = "EXPIRING",
                BadgeClass = "deadline",
                Description = "สามารถกดต่ออายุ/เติมโควต้าให้สมาชิกได้จากตารางสมาชิก",
                Links = new List<AdminActionLink> { new() { Label = "ดูรายชื่อที่ใกล้หมดอายุ", Url = "/Admin/UserList?tab=expiring" } }
            });
        }
        if (!actionItems.Any())
        {
            actionItems.Add(new AdminActionItemViewModel
            {
                Title = "ระบบปฏิบัติการปกติทุกรายการ",
                BadgeText = "ALL CLEAR",
                BadgeClass = "progress",
                Description = "ไม่มีบิลค้างตรวจสอบหรือห้องซ้อมที่ปิดซ่อมบำรุงในขณะนี้",
                Links = new List<AdminActionLink>()
            });
        }

        // คำนวณสัดส่วนรายได้ตามแพ็กเกจจริง (Group By Package)
        var revenueShare = successPayments
            .GroupBy(p => p.Package?.PackageName ?? "แพ็กเกจทั่วไป")
            .Select((g, idx) =>
            {
                decimal amt = g.Sum(x => x.Amount);
                int pct = realSuccessRevenue > 0 ? (int)Math.Round(amt * 100m / realSuccessRevenue) : 0;
                return new AdminRevenueShareItem
                {
                    PackageName = g.Key,
                    Subtitle = $"{g.Count()} รายการชำระ",
                    Amount = amt,
                    Percentage = pct,
                    BarClass = idx == 0 ? "red" : "green"
                };
            })
            .OrderByDescending(r => r.Amount)
            .ToList();

        if (!revenueShare.Any())
        {
            revenueShare.Add(new AdminRevenueShareItem
            {
                PackageName = "ยังไม่มีรายการชำระเงินที่สำเร็จ",
                Subtitle = "0 รายการ",
                Amount = 0m,
                Percentage = 0,
                BarClass = "red"
            });
        }

        return new AdminDashboardViewModel
        {
            MonthlyRevenue = realSuccessRevenue,
            RevenueTarget = 100000m,
            MomGrowthPercent = 12.5m,
            ActiveClients = activeRows,
            MaxSlots = Math.Max(totalRows, 50),
            CapacityPercent = totalRows > 0 ? Math.Round((decimal)activeRows * 100m / totalRows, 1) : 100m,
            TodayBookings = realTodayBookings,
            CheckInDone = realTodayCheckIns,
            CheckInPercent = realTodayBookings > 0 ? Math.Min(100, realTodayCheckIns * 100 / realTodayBookings) : 0,
            ExpiringIn7Days = expiringRows,
            ExpiringToday = memberRows.Count(m => m.DaysRemaining == 1),
            IssueCount = realPendingCount + maintPodCount,
            IssueSummary = $"บิลรอตรวจ {realPendingCount} • ซ่อมบำรุง {maintPodCount}",
            NewThisMonth = totalRows,
            TargetNew = 20,
            PendingAuditCount = realPendingCount,
            PendingAuditAmount = realPendingAmount,
            TotalCount = totalRows,
            ActiveCount = activeRows,
            ExpiringCount = expiringRows,
            SuspendedCount = suspendedRows,
            ExpiredCount = expiredRows,
            Members = filteredMembers.ToList(),
            ActionItems = actionItems,
            RevenueShare = revenueShare,
            AverageArpu = totalRows > 0 ? Math.Round(realSuccessRevenue / totalRows, 0) : 0m,
            ArpuGrowthPercent = 8.3m
        };
    }

    public static void ApplySubscriptionRenewal(Csi402dbContext db, User user, Package targetPkg)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        int duration = targetPkg.DurationDays > 0 ? targetPkg.DurationDays : 30;
        var sub = db.MemberSubscriptions
            .Where(s => s.UserId == user.UserId)
            .OrderByDescending(s => s.Status == "Active")
            .ThenByDescending(s => s.SubscriptionId)
            .FirstOrDefault();

        if (sub == null)
        {
            sub = new MemberSubscription
            {
                UserId = user.UserId,
                PackageId = targetPkg.PackageId,
                StartDate = today,
                EndDate = today.AddDays(duration),
                RemainingPods = targetPkg.PodQuota,
                RemainingPt = targetPkg.PtQuota,
                RemainingInBody = targetPkg.InBodyQuota,
                Status = "Active"
            };
            db.MemberSubscriptions.Add(sub);
        }
        else if (sub.EndDate > today)
        {
            // ยังไม่หมดอายุ: ขยายวันต่อจากของเดิมและทบโควต้าเดิม + แพ็กเกจที่ซื้อเพิ่ม
            sub.PackageId = targetPkg.PackageId;
            sub.EndDate = sub.EndDate.AddDays(duration);
            sub.RemainingPods += targetPkg.PodQuota;
            sub.RemainingPt += targetPkg.PtQuota;
            sub.RemainingInBody += targetPkg.InBodyQuota;
            sub.Status = "Active";
        }
        else
        {
            // หมดอายุแล้ว: เริ่มรอบใหม่ทันทีและรีเซ็ตโควต้าตามแพ็กเกจใหม่
            sub.PackageId = targetPkg.PackageId;
            sub.StartDate = today;
            sub.EndDate = today.AddDays(duration);
            sub.RemainingPods = targetPkg.PodQuota;
            sub.RemainingPt = targetPkg.PtQuota;
            sub.RemainingInBody = targetPkg.InBodyQuota;
            sub.Status = "Active";
        }

        user.Status = "Active";
    }

    // POST: /Admin/RenewMemberSubscription (ชำระเงินต่ออายุหน้าเคาน์เตอร์ -> บันทึกบิล Payments + ต่ออายุ/เติมสิทธิ์)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult RenewMemberSubscription(
        int userId,
        int packageId = 0,
        string paymentMethod = "Cash",
        string paymentMode = "instant",
        string? slipRef = "",
        string? returnAction = "UserList")
    {
        var user = _db.Users
            .Include(u => u.MemberSubscriptions)
                .ThenInclude(ms => ms.Package)
            .FirstOrDefault(u => u.UserId == userId);

        if (user == null)
        {
            TempData["ErrorMessage"] = "ไม่พบข้อมูลสมาชิก";
            return RedirectToAction(returnAction ?? "UserList");
        }

        if (user.Status == "Suspended" || user.Status == "Inactive")
        {
            TempData["ErrorMessage"] = $"ไม่สามารถต่ออายุให้ {user.FullName} ได้ เนื่องจากบัญชีถูกระงับอยู่";
            return RedirectToAction(returnAction ?? "UserList");
        }

        var currentSub = user.MemberSubscriptions.OrderByDescending(s => s.SubscriptionId).FirstOrDefault();
        var targetPkg = (packageId > 0 ? _db.Packages.FirstOrDefault(p => p.PackageId == packageId) : null)
                        ?? currentSub?.Package
                        ?? _db.Packages.OrderBy(p => p.PackageId).FirstOrDefault();

        if (targetPkg == null)
        {
            TempData["ErrorMessage"] = "ไม่พบแพ็กเกจที่ต้องการต่ออายุ";
            return RedirectToAction(returnAction ?? "UserList");
        }

        if (_db.Payments.Any(p => p.UserId == userId && p.Status == "Pending" && (p.SlipReference ?? "").StartsWith("RENEW-")))
        {
            TempData["ErrorMessage"] = $"สมาชิก {user.FullName} มีบิลต่ออายุสถานะ Pending ค้างตรวจสอบอยู่แล้ว กรุณาอนุมัติหรือปฏิเสธบิลเดิมก่อน";
            return RedirectToAction(returnAction ?? "UserList");
        }

        bool isCash = string.Equals(paymentMethod, "Cash", StringComparison.OrdinalIgnoreCase);
        string cleanRef = (slipRef ?? "").Trim();

        // Map to strict MySQL ENUM('CreditCard','PromptPay','BankTransfer')
        string dbPaymentMethod = paymentMethod switch
        {
            "CreditCard" or "Credit Card" => "CreditCard",
            "BankTransfer" or "Bank Transfer" or "Cash" => "BankTransfer",
            _ => "PromptPay"
        };

        if (!isCash && string.IsNullOrWhiteSpace(cleanRef))
        {
            TempData["ErrorMessage"] = "กรุณากรอกเลขอ้างอิงสลิป / ใบเสร็จเครื่องรูดบัตรก่อนยืนยันการชำระเงิน";
            return RedirectToAction(returnAction ?? "UserList");
        }

        if (string.Equals(paymentMode, "pending", StringComparison.OrdinalIgnoreCase))
        {
            _db.Payments.Add(new Payment
            {
                UserId = user.UserId,
                PackageId = targetPkg.PackageId,
                Amount = targetPkg.Price,
                PaymentMethod = dbPaymentMethod,
                PaymentDate = DateTime.Now,
                Status = "Pending",
                SlipReference = $"RENEW-{(isCash ? "CASH-" : "")}{(string.IsNullOrWhiteSpace(cleanRef) ? "WAIT" : cleanRef)}"
            });
            _db.SaveChanges();
            TempData["SuccessMessage"] = $"บันทึกบิลต่ออายุ {targetPkg.PackageName} (฿{targetPkg.Price:N0}) ของคุณ {user.FullName} เป็นสถานะ Pending แล้ว (สิทธิ์จะเพิ่มอัตโนมัติเมื่ออนุมัติยอด)";
            return RedirectToAction(returnAction ?? "UserList");
        }

        string finalRef = string.IsNullOrWhiteSpace(cleanRef)
            ? $"INV-RENEW-CASH-{DateTime.Now:yyyyMMddHHmmss}"
            : $"INV-RENEW-{cleanRef}";

        _db.Payments.Add(new Payment
        {
            UserId = user.UserId,
            PackageId = targetPkg.PackageId,
            Amount = targetPkg.Price,
            PaymentMethod = dbPaymentMethod,
            PaymentDate = DateTime.Now,
            Status = "Success",
            SlipReference = finalRef
        });

        ApplySubscriptionRenewal(_db, user, targetPkg);
        _db.SaveChanges();
        TempData["SuccessMessage"] = $"รับชำระเงิน ฿{targetPkg.Price:N0} ({paymentMethod}) และต่ออายุแพ็กเกจ {targetPkg.PackageName} ให้คุณ {user.FullName} เรียบร้อยแล้ว";
        return RedirectToAction(returnAction ?? "UserList");
    }

    // ปรับแก้โควต้าสมาชิกกรณีพิเศษ (บังคับระบุเหตุผลและบันทึกลง Audit Log)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult AdjustMemberQuota(int userId, int addPods, int addPt, int addInBody, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            TempData["ErrorMessage"] = "กรุณาระบุเหตุผลการปรับแก้โควต้ากรณีพิเศษทุกครั้งเพื่อใช้ตรวจสอบย้อนหลัง";
            return RedirectToAction("EditUser", new { id = userId });
        }

        var user = _db.Users
            .Include(u => u.MemberSubscriptions)
            .FirstOrDefault(u => u.UserId == userId);

        if (user == null)
        {
            return RedirectToAction("UserList");
        }

        var sub = user.MemberSubscriptions
            .OrderByDescending(s => s.Status == "Active")
            .ThenByDescending(s => s.SubscriptionId)
            .FirstOrDefault();

        if (sub == null)
        {
            TempData["ErrorMessage"] = "สมาชิกยังไม่มีแพ็กเกจในระบบ ไม่สามารถปรับแก้โควต้าได้";
            return RedirectToAction("EditUser", new { id = userId });
        }

        sub.RemainingPods = Math.Max(0, sub.RemainingPods + addPods);
        sub.RemainingPt = Math.Max(0, sub.RemainingPt + addPt);
        sub.RemainingInBody = Math.Max(0, sub.RemainingInBody + addInBody);
        _db.SaveChanges();

        try
        {
            string logPath = Path.Combine(Directory.GetCurrentDirectory(), "App_Data", "quota_adjustments.json");
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            var logs = System.IO.File.Exists(logPath)
                ? JsonSerializer.Deserialize<List<Dictionary<string, string>>>(System.IO.File.ReadAllText(logPath)) ?? new()
                : new List<Dictionary<string, string>>();
            logs.Insert(0, new Dictionary<string, string>
            {
                ["UserId"] = user.UserId.ToString(),
                ["MemberName"] = user.FullName,
                ["Delta"] = $"Pod:{addPods:+0;-0;0}, PT:{addPt:+0;-0;0}, InBody:{addInBody:+0;-0;0}",
                ["Reason"] = reason.Trim(),
                ["Timestamp"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            });
            System.IO.File.WriteAllText(logPath, JsonSerializer.Serialize(logs.Take(100).ToList()));
        }
        catch { }

        TempData["SuccessMessage"] = $"ปรับแก้โควต้าของ {user.FullName} เรียบร้อยแล้ว (เหตุผล: {reason.Trim()})";
        return RedirectToAction("EditUser", new { id = userId });
    }

    // ส่งออกรายชื่อสมาชิกและข้อมูลแพ็กเกจเป็นไฟล์ CSV
    [HttpGet]
    public IActionResult ExportDashboardCsv()
    {
        var users = _db.Users
            .Include(u => u.HomeBranch)
            .Include(u => u.MemberSubscriptions).ThenInclude(s => s.Package)
            .Include(u => u.Payments)
            .Where(u => (u.Role != null && u.Role.RoleName == "Member"))
            .OrderBy(u => u.UserId)
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine("MemberCode,FullName,Email,Phone,Branch,Package,RemainingPods,RemainingPT,EndDate,TotalPaidTHB,Status");
        foreach (var u in users)
        {
            var sub = u.MemberSubscriptions.OrderByDescending(s => s.SubscriptionId).FirstOrDefault();
            decimal paid = u.Payments.Where(p => p.Status == "Success").Sum(p => p.Amount);
            sb.AppendLine($"PG-{u.UserId:D5},\"{u.FullName}\",{u.Email},{u.Phone},\"{u.HomeBranch?.BranchName ?? "-"}\",\"{sub?.Package?.PackageName ?? "-"}\",{sub?.RemainingPods ?? 0},{sub?.RemainingPt ?? 0},{sub?.EndDate.ToString("yyyy-MM-dd") ?? "-"},{paid:F0},{u.Status ?? "Active"}");
        }

        byte[] bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        return File(bytes, "text/csv", $"privategym-members-{DateTime.Today:yyyyMMdd}.csv");
    }

    // ================================================================
    // 2. ADD / EDIT / TOGGLE / DELETE USER
    // ================================================================
    public IActionResult AddUser()
    {
        ViewData["Username"] = "Admin";
        ViewBag.Branches = _db.Branches.ToList();
        ViewBag.Roles = _db.Roles.ToList();
        return View();
    }

    // บันทึกผู้ใช้งานใหม่ลงฐานข้อมูล (พร้อมสร้างสิทธิ์/โปรไฟล์ตาม Role)
    [HttpPost]
    public IActionResult AddUser(string firstName, string lastName, string email,
                                 string phone, string role, string password,
                                 int? homeBranchId, string? gender, decimal? height,
                                 decimal? weight, string? experienceLevel, string? fitnessGoal)
    {
        ViewData["Username"] = "Admin";

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            ViewBag.Error = "กรุณากรอกอีเมลและรหัสผ่านให้ครบถ้วน";
            ViewBag.Branches = _db.Branches.ToList();
            ViewBag.Roles = _db.Roles.ToList();
            return View();
        }

        string trimmedEmail = email.Trim();
        if (_db.Users.Any(u => u.Email == trimmedEmail))
        {
            ViewBag.Error = "อีเมลนี้มีอยู่ในระบบแล้ว กรุณาใช้อีเมลอื่น";
            ViewBag.Branches = _db.Branches.ToList();
            ViewBag.Roles = _db.Roles.ToList();
            return View();
        }

        var roleObj = _db.Roles.FirstOrDefault(r => r.RoleName == role);
        int roleId = roleObj?.RoleId ?? 4;

        string fullName = $"{firstName?.Trim()} {lastName?.Trim()}".Trim();
        if (string.IsNullOrWhiteSpace(fullName)) fullName = trimmedEmail.Split('@')[0];

        var newUser = new User
        {
            FullName = fullName,
            Email = trimmedEmail,
            Phone = string.IsNullOrWhiteSpace(phone) ? "0800000000" : phone.Trim(),
            PasswordHash = password,
            RoleId = roleId,
            HomeBranchId = homeBranchId ?? _db.Branches.FirstOrDefault()?.BranchId ?? 1,
            Gender = gender,
            Height = height,
            Weight = weight,
            ExperienceLevel = experienceLevel,
            FitnessGoal = fitnessGoal,
            Status = "Active",
            CreatedAt = DateTime.Now
        };

        _db.Users.Add(newUser);
        _db.SaveChanges();

        if (roleId == 3 && !_db.Trainers.Any(t => t.UserId == newUser.UserId))
        {
            _db.Trainers.Add(new Trainer
            {
                UserId = newUser.UserId,
                Specialty = !string.IsNullOrWhiteSpace(fitnessGoal) ? fitnessGoal : "Strength & Conditioning",
                Bio = "Personal Trainer ประจำคลับ PrivateGym",
                HourlyRate = 1500m
            });
            _db.SaveChanges();
        }
        else if (roleId == 4)
        {
            var starterPkg = _db.Packages.OrderBy(p => p.PackageId).FirstOrDefault();
            if (starterPkg != null)
            {
                var today = DateOnly.FromDateTime(DateTime.Today);
                _db.MemberSubscriptions.Add(new MemberSubscription
                {
                    UserId = newUser.UserId,
                    PackageId = starterPkg.PackageId,
                    StartDate = today,
                    EndDate = today.AddDays(starterPkg.DurationDays > 0 ? starterPkg.DurationDays : 30),
                    RemainingPods = starterPkg.PodQuota,
                    RemainingPt = starterPkg.PtQuota,
                    RemainingInBody = starterPkg.InBodyQuota,
                    Status = "Active"
                });
                _db.SaveChanges();
            }
        }

        TempData["SuccessMessage"] = $"เพิ่มผู้ใช้งาน {newUser.FullName} สำเร็จเรียบร้อยแล้ว";
        return roleId == 2 ? RedirectToAction("Staff")
             : roleId == 3 ? RedirectToAction("Trainers")
             : RedirectToAction("UserList");
    }

    public IActionResult EditUser(int id)
    {
        ViewData["Username"] = "Admin";
        var user = _db.Users
            .Include(u => u.Role)
            .Include(u => u.HomeBranch)
            .FirstOrDefault(u => u.UserId == id);

        if (user == null)
        {
            TempData["ErrorMessage"] = $"ไม่พบผู้ใช้งานรหัส #{id}";
            return RedirectToAction("UserList");
        }

        ViewBag.Branches = _db.Branches.ToList();
        ViewBag.Roles = _db.Roles.ToList();
        return View(user);
    }

    [HttpPost]
    public IActionResult EditUser(int userId, string fullName, string email,
                                  string phone, string? role, string? password,
                                  int? homeBranchId, string? gender, decimal? height,
                                  decimal? weight, string? experienceLevel, string? fitnessGoal,
                                  string? taxId, string? billingAddress)
    {
        ViewData["Username"] = "Admin";

        var user = _db.Users.FirstOrDefault(u => u.UserId == userId);
        if (user == null)
        {
            TempData["ErrorMessage"] = $"ไม่พบผู้ใช้งานรหัส #{userId}";
            return RedirectToAction("UserList");
        }

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(fullName))
        {
            ViewBag.Error = "กรุณากรอกชื่อ-นามสกุล และอีเมลให้ครบถ้วน";
            ViewBag.Branches = _db.Branches.ToList();
            ViewBag.Roles = _db.Roles.ToList();
            return View(user);
        }

        string trimmedEmail = email.Trim();
        if (_db.Users.Any(u => u.Email == trimmedEmail && u.UserId != userId))
        {
            ViewBag.Error = "อีเมลนี้มีอยู่ในระบบแล้ว กรุณาใช้อีเมลอื่น";
            ViewBag.Branches = _db.Branches.ToList();
            ViewBag.Roles = _db.Roles.ToList();
            return View(user);
        }

        if (!string.IsNullOrWhiteSpace(role))
        {
            var roleObj = _db.Roles.FirstOrDefault(r => r.RoleName == role);
            if (roleObj != null) user.RoleId = roleObj.RoleId;
        }

        user.FullName = fullName.Trim();
        user.Email = trimmedEmail;
        user.Phone = string.IsNullOrWhiteSpace(phone) ? user.Phone : phone.Trim();
        user.HomeBranchId = homeBranchId ?? user.HomeBranchId;
        user.Gender = gender;
        user.Height = height;
        user.Weight = weight;
        user.ExperienceLevel = experienceLevel;
        user.FitnessGoal = fitnessGoal;
        user.TaxId = taxId;
        user.BillingAddress = billingAddress;

        if (!string.IsNullOrWhiteSpace(password))
        {
            user.PasswordHash = password;
        }

        _db.SaveChanges();

        if ((user.Role != null && user.Role.RoleName == "Trainer") && !_db.Trainers.Any(t => t.UserId == user.UserId))
        {
            _db.Trainers.Add(new Trainer
            {
                UserId = user.UserId,
                Specialty = !string.IsNullOrWhiteSpace(fitnessGoal) ? fitnessGoal : "Strength & Conditioning",
                Bio = "Personal Trainer ประจำคลับ PrivateGym",
                HourlyRate = 1500m
            });
            _db.SaveChanges();
        }

        TempData["SuccessMessage"] = $"แก้ไขข้อมูลผู้ใช้งาน #{user.UserId} ({user.FullName}) สำเร็จเรียบร้อยแล้ว";
        return RedirectToAction("UserList");
    }

    // สลับสถานะเปิด/ปิดการใช้งานบัญชี (Active <-> Inactive)
    [HttpPost]
    public IActionResult ToggleUserStatus(int id, string? returnAction = "UserList")
    {
        var user = _db.Users.FirstOrDefault(u => u.UserId == id);
        if (user != null)
        {
            if (user.Status == "Inactive" || user.Status == "Suspended" || user.Status == "โดนลบแล้ว")
            {
                user.Status = "Active";
                TempData["SuccessMessage"] = $"เปิดการใช้งานบัญชี #{id} ({user.FullName}) สำเร็จ (สถานะ: Active)";
            }
            else
            {
                user.Status = "Inactive";
                TempData["SuccessMessage"] = $"ปิดการใช้งานบัญชี #{id} ({user.FullName}) สำเร็จ (สถานะ: Inactive)";
            }
            _db.SaveChanges();
        }
        return RedirectToAction(returnAction ?? "UserList");
    }

    [HttpPost]
    public IActionResult DeleteUser(int id, string? returnAction = "UserList")
    {
        var user = _db.Users
            .Include(u => u.Bookings)
            .Include(u => u.CheckIns)
            .Include(u => u.MemberSubscriptions)
            .Include(u => u.Payments)
            .Include(u => u.Trainer)
            .FirstOrDefault(u => u.UserId == id);

        if (user != null)
        {
            string userName = user.FullName;
            if (user.Bookings != null && user.Bookings.Any()) _db.Bookings.RemoveRange(user.Bookings);
            if (user.CheckIns != null && user.CheckIns.Any()) _db.CheckIns.RemoveRange(user.CheckIns);
            if (user.MemberSubscriptions != null && user.MemberSubscriptions.Any()) _db.MemberSubscriptions.RemoveRange(user.MemberSubscriptions);
            if (user.Payments != null && user.Payments.Any()) _db.Payments.RemoveRange(user.Payments);
            if (user.Trainer != null) _db.Trainers.Remove(user.Trainer);

            _db.Users.Remove(user);
            _db.SaveChanges();

            TempData["SuccessMessage"] = $"ลบข้อมูลผู้ใช้งาน #{id} ({userName}) ออกจากระบบสำเร็จเรียบร้อยแล้ว";
        }
        return RedirectToAction(returnAction ?? "UserList");
    }

    [HttpGet]
    public IActionResult GetUserDetails(int id)
    {
        var user = _db.Users
            .Include(u => u.Role)
            .Include(u => u.HomeBranch)
            .Include(u => u.MemberSubscriptions)
                .ThenInclude(ms => ms.Package)
            .Include(u => u.Payments)
            .FirstOrDefault(u => u.UserId == id);

        if (user == null) return NotFound();

        var activeSub = user.MemberSubscriptions.OrderByDescending(s => s.SubscriptionId).FirstOrDefault();

        return Json(new
        {
            userId = user.UserId,
            fullName = user.FullName,
            email = user.Email,
            phone = user.Phone,
            role = user.Role?.RoleName ?? "Member",
            branch = user.HomeBranch?.BranchName ?? "-",
            packageName = activeSub?.Package?.PackageName ?? "-",
            remainingPods = activeSub?.RemainingPods ?? 0,
            remainingPt = activeSub?.RemainingPt ?? 0,
            gender = user.Gender ?? "-",
            height = user.Height?.ToString() ?? "-",
            weight = user.Weight?.ToString() ?? "-",
            experience = user.ExperienceLevel ?? "-",
            goal = user.FitnessGoal ?? "-",
            taxId = user.TaxId ?? "-",
            billingAddress = user.BillingAddress ?? "-",
            createdAt = user.CreatedAt?.ToString("dd/MM/yyyy HH:mm") ?? "-"
        });
    }

    // ================================================================
    // 3. STAFF MANAGEMENT (/Admin/Staff)
    // ================================================================
    public IActionResult Staff(string? search = "")
    {
        Csi402dbContext.EnsureAllBranchesPodsAndTrainersSeeded(_db);
        ViewData["Username"] = "Admin";

        var staffUsers = _db.Users
            .Include(u => u.HomeBranch)
            .Include(u => u.Role)
            .Where(u => (u.Role != null && u.Role.RoleName == "Staff"))
            .OrderBy(u => u.UserId)
            .ToList();

        if (!string.IsNullOrWhiteSpace(search))
        {
            string q = search.Trim().ToLower();
            staffUsers = staffUsers.Where(u =>
                (u.FullName?.ToLower().Contains(q) ?? false) ||
                (u.Email?.ToLower().Contains(q) ?? false) ||
                (u.Phone?.ToLower().Contains(q) ?? false) ||
                (u.HomeBranch?.BranchName?.ToLower().Contains(q) ?? false)).ToList();
        }

        // ดึงบันทึกส่งเวรจริงที่พนักงานบันทึกจากหน้า Staff
        var handoverLogs = new List<Dictionary<string, string>>();
        try
        {
            string path = Path.Combine(Directory.GetCurrentDirectory(), "App_Data", "staff_handover_logs.json");
            if (System.IO.File.Exists(path))
            {
                handoverLogs = JsonSerializer.Deserialize<List<Dictionary<string, string>>>(System.IO.File.ReadAllText(path))
                               ?? new List<Dictionary<string, string>>();
            }
        }
        catch { }

        ViewBag.Search = search ?? "";
        ViewBag.Branches = _db.Branches.Include(b => b.Pods).ToList();
        ViewBag.HandoverLogs = handoverLogs.Take(6).ToList();
        ViewBag.MaintPodsCount = _db.Pods.Count(p => p.Status != "Available");
        return View(staffUsers);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult AddStaff(string fullName, string email, string phone, int branchId, string password)
    {
        string trimmedEmail = (email ?? "").Trim();
        if (string.IsNullOrWhiteSpace(trimmedEmail) || _db.Users.Any(u => u.Email == trimmedEmail))
        {
            TempData["ErrorMessage"] = "อีเมลนี้มีอยู่ในระบบแล้วหรือไม่ถูกต้อง";
            return RedirectToAction("Staff");
        }

        var staffUser = new User
        {
            FullName = string.IsNullOrWhiteSpace(fullName) ? trimmedEmail.Split('@')[0] : fullName.Trim(),
            Email = trimmedEmail,
            Phone = string.IsNullOrWhiteSpace(phone) ? "0800000000" : phone.Trim(),
            PasswordHash = string.IsNullOrWhiteSpace(password) ? "123456" : password,
            RoleId = 2,
            HomeBranchId = branchId > 0 ? branchId : (_db.Branches.FirstOrDefault()?.BranchId ?? 1),
            Status = "Active",
            CreatedAt = DateTime.Now
        };
        _db.Users.Add(staffUser);
        _db.SaveChanges();
        TempData["SuccessMessage"] = $"เพิ่มพนักงาน {staffUser.FullName} เข้าระบบเรียบร้อยแล้ว";
        return RedirectToAction("Staff");
    }

    // ================================================================
    // 4. TRAINERS MANAGEMENT (/Admin/Trainers)
    // ================================================================
    public IActionResult Trainers(string? search = "")
    {
        Csi402dbContext.EnsureAllBranchesPodsAndTrainersSeeded(_db);
        ViewData["Username"] = "Admin";

        var trainers = _db.Trainers
            .Include(t => t.User)
                .ThenInclude(u => u!.HomeBranch)
            .Include(t => t.Bookings)
            .OrderBy(t => t.TrainerId)
            .ToList();

        if (!string.IsNullOrWhiteSpace(search))
        {
            string q = search.Trim().ToLower();
            trainers = trainers.Where(t =>
                (t.User?.FullName?.ToLower().Contains(q) ?? false) ||
                (t.Specialty?.ToLower().Contains(q) ?? false) ||
                (t.User?.HomeBranch?.BranchName?.ToLower().Contains(q) ?? false)).ToList();
        }

        ViewBag.Search = search ?? "";
        ViewBag.Branches = _db.Branches.OrderBy(b => b.BranchId).ToList();
        return View(trainers);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult AddTrainer(string fullName, string email, string phone, int branchId, string specialty, decimal hourlyRate, string password)
    {
        string trimmedEmail = (email ?? "").Trim();
        if (string.IsNullOrWhiteSpace(trimmedEmail) || _db.Users.Any(u => u.Email == trimmedEmail))
        {
            TempData["ErrorMessage"] = "อีเมลนี้มีอยู่ในระบบแล้วหรือไม่ถูกต้อง";
            return RedirectToAction("Trainers");
        }

        var coachUser = new User
        {
            FullName = string.IsNullOrWhiteSpace(fullName) ? trimmedEmail.Split('@')[0] : fullName.Trim(),
            Email = trimmedEmail,
            Phone = string.IsNullOrWhiteSpace(phone) ? "0800000000" : phone.Trim(),
            PasswordHash = string.IsNullOrWhiteSpace(password) ? "123456" : password,
            RoleId = 3,
            HomeBranchId = branchId > 0 ? branchId : (_db.Branches.FirstOrDefault()?.BranchId ?? 1),
            Status = "Active",
            CreatedAt = DateTime.Now
        };
        _db.Users.Add(coachUser);
        _db.SaveChanges();

        _db.Trainers.Add(new Trainer
        {
            UserId = coachUser.UserId,
            Specialty = string.IsNullOrWhiteSpace(specialty) ? "Strength & Conditioning" : specialty.Trim(),
            Bio = "Personal Trainer ประจำคลับ PrivateGym",
            HourlyRate = hourlyRate > 0 ? hourlyRate : 1500m
        });
        _db.SaveChanges();

        TempData["SuccessMessage"] = $"ลงทะเบียนโค้ช {coachUser.FullName} เรียบร้อยแล้ว";
        return RedirectToAction("Trainers");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult UpdateTrainerRate(int trainerId, decimal hourlyRate, string? specialty)
    {
        var trainer = _db.Trainers.Include(t => t.User).FirstOrDefault(t => t.TrainerId == trainerId);
        if (trainer != null)
        {
            trainer.HourlyRate = hourlyRate > 0 ? hourlyRate : 1000m;
            if (!string.IsNullOrWhiteSpace(specialty))
            {
                trainer.Specialty = specialty.Trim();
            }
            _db.SaveChanges();
            TempData["SuccessMessage"] = $"อัปเดตเรทค่าสอนและความเชี่ยวชาญของ {trainer.User?.FullName} เรียบร้อยแล้ว";
        }
        return RedirectToAction("Trainers");
    }

    // ================================================================
    // 5. PACKAGES & PODS MANAGEMENT (/Admin/Packages)
    // ================================================================
    public IActionResult Packages()
    {
        Csi402dbContext.EnsureAllBranchesPodsAndTrainersSeeded(_db);
        ViewData["Username"] = "Admin";

        var packages = _db.Packages.OrderBy(p => p.Price).ToList();
        var pods = _db.Pods.Include(p => p.Branch).OrderBy(p => p.BranchId).ThenBy(p => p.PodName).ToList();
        var subCounts = _db.MemberSubscriptions
            .Where(s => s.Status == "Active")
            .GroupBy(s => s.PackageId)
            .ToDictionary(g => g.Key, g => g.Count());

        ViewBag.Packages = packages;
        ViewBag.Pods = pods;
        ViewBag.SubCounts = subCounts;
        ViewBag.TotalActiveSubs = _db.MemberSubscriptions.Count(s => s.Status == "Active");
        ViewBag.TotalRevenue = _db.Payments.Where(p => p.Status == "Success").Sum(p => (decimal?)p.Amount) ?? 0m;
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult CreatePackage(string packageName, decimal price, int durationDays, int podQuota, int ptQuota, int inBodyQuota, bool isVip = false)
    {
        if (!string.IsNullOrWhiteSpace(packageName))
        {
            _db.Packages.Add(new Package
            {
                PackageName = packageName.Trim(),
                Price = price > 0 ? price : 3500m,
                DurationDays = durationDays > 0 ? durationDays : 30,
                PodQuota = Math.Max(0, podQuota),
                PtQuota = Math.Max(0, ptQuota),
                InBodyQuota = Math.Max(0, inBodyQuota),
                IsVip = isVip
            });
            _db.SaveChanges();
            TempData["SuccessMessage"] = $"สร้างแพ็กเกจใหม่ {packageName.Trim()} สำเร็จเรียบร้อยแล้ว";
        }
        return RedirectToAction("Packages");
    }

    [HttpPost]
    public IActionResult UpdatePackage(int packageId, decimal price, int durationDays, int podQuota, int ptQuota, int inBodyQuota)
    {
        var pkg = _db.Packages.FirstOrDefault(p => p.PackageId == packageId);
        if (pkg != null)
        {
            pkg.Price = price;
            pkg.DurationDays = durationDays > 0 ? durationDays : 30;
            pkg.PodQuota = podQuota;
            pkg.PtQuota = ptQuota;
            pkg.InBodyQuota = inBodyQuota;
            _db.SaveChanges();
            TempData["SuccessMessage"] = $"อัปเดตแพ็กเกจ {pkg.PackageName} เรียบร้อยแล้ว";
        }
        return RedirectToAction("Packages");
    }

    [HttpPost]
    public IActionResult TogglePodStatus(int podId)
    {
        var pod = _db.Pods.FirstOrDefault(p => p.PodId == podId);
        if (pod != null)
        {
            pod.Status = pod.Status == "Available" ? "Maintenance" : "Available";
            _db.SaveChanges();
            TempData["SuccessMessage"] = $"อัปเดตสถานะห้อง {pod.PodName} เป็น {pod.Status} เรียบร้อยแล้ว";
        }
        return RedirectToAction("Packages");
    }

    // ================================================================
    // 6. BOOKINGS MANAGEMENT (/Admin/Bookings)
    // ================================================================
    public IActionResult Bookings(string? status = "all", string? search = "")
    {
        Csi402dbContext.EnsureAllBranchesPodsAndTrainersSeeded(_db);
        ViewData["Username"] = "Admin";

        var allBookings = _db.Bookings
            .Include(b => b.User)
            .Include(b => b.Pod).ThenInclude(p => p!.Branch)
            .Include(b => b.Trainer).ThenInclude(t => t!.User)
            .Include(b => b.CheckIns)
            .Where(b => b.User != null && (b.User.Role != null && b.User.Role.RoleName == "Member"))
            .OrderByDescending(b => b.BookingDate)
            .ThenByDescending(b => b.StartTime)
            .ToList();

        ViewBag.TotalCount = allBookings.Count;
        ViewBag.ConfirmedCount = allBookings.Count(b => b.Status == "Confirmed");
        ViewBag.CompletedCount = allBookings.Count(b => b.Status == "Completed");
        ViewBag.CancelledCount = allBookings.Count(b => b.Status == "Cancelled");

        var filtered = allBookings.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(status) && status != "all")
        {
            filtered = filtered.Where(b => b.Status == status);
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            string q = search.Trim().ToLower();
            filtered = filtered.Where(b =>
                (b.User?.FullName?.ToLower().Contains(q) ?? false) ||
                (b.Pod?.PodName?.ToLower().Contains(q) ?? false) ||
                ($"#pg-{b.BookingId:D5}".Contains(q)));
        }

        ViewBag.FilterStatus = status ?? "all";
        ViewBag.Search = search ?? "";
        ViewBag.Pods = _db.Pods.Include(p => p.Branch).OrderBy(p => p.PodId).ToList();
        ViewBag.ActiveCheckIns = _db.CheckIns
            .Include(c => c.Booking).ThenInclude(b => b!.User)
            .Include(c => c.Booking).ThenInclude(b => b!.Trainer).ThenInclude(t => t!.User)
            .Where(c => c.CheckOutTime == null && c.Booking != null)
            .ToList();
        return View(filtered.Take(80).ToList());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult CancelBookingAdmin(int bookingId)
    {
        var booking = _db.Bookings
            .Include(b => b.User)
                .ThenInclude(u => u!.MemberSubscriptions)
            .Include(b => b.CheckIns)
            .FirstOrDefault(b => b.BookingId == bookingId);

        if (booking != null && booking.Status == "Confirmed")
        {
            if (booking.CheckIns != null && booking.CheckIns.Any())
            {
                TempData["ErrorMessage"] = $"ไม่สามารถยกเลิกและคืนโควต้า #PG-{booking.BookingId:D5} ได้ เนื่องจากสมาชิกเช็กอินเข้าใช้งานแล้ว";
                return RedirectToAction("Bookings");
            }
            booking.Status = "Cancelled";
            var sub = booking.User?.MemberSubscriptions
                .Where(s => s.Status == "Active")
                .OrderByDescending(s => s.SubscriptionId)
                .FirstOrDefault();

            if (sub != null)
            {
                sub.RemainingPods += 1;
                if (booking.TrainerId.HasValue)
                {
                    sub.RemainingPt += 1;
                }
            }
            _db.SaveChanges();
            TempData["SuccessMessage"] = $"ยกเลิกรายการจอง #PG-{booking.BookingId:D5} และคืนโควต้าให้คุณ {booking.User?.FullName} เรียบร้อยแล้ว";
        }
        return RedirectToAction("Bookings");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult CompleteBookingAdmin(int bookingId)
    {
        var booking = _db.Bookings
            .Include(b => b.User)
            .Include(b => b.CheckIns)
            .FirstOrDefault(b => b.BookingId == bookingId);

        if (booking != null && booking.Status != "Completed")
        {
            if (booking.CheckIns == null || !booking.CheckIns.Any())
            {
                _db.CheckIns.Add(new CheckIn
                {
                    BookingId = booking.BookingId,
                    UserId = booking.UserId,
                    CheckInTime = DateTime.Now.AddMinutes(-60),
                    CheckOutTime = DateTime.Now,
                    AccessMethod = "Admin_Desk"
                });
            }
            else
            {
                foreach (var ci in booking.CheckIns.Where(c => c.CheckOutTime == null))
                {
                    ci.CheckOutTime = DateTime.Now;
                }
            }
            booking.Status = "Completed";
            _db.SaveChanges();
            TempData["SuccessMessage"] = $"บันทึกจบเซสชัน #PG-{booking.BookingId:D5} ({booking.User?.FullName}) เรียบร้อยแล้ว";
        }
        return RedirectToAction("Bookings");
    }

    // หน้าตารางเวลา Master Schedule (รองรับทั้งมุมมองรายสัปดาห์ Weekly และรายวันแยกห้อง Daily)
    public IActionResult Schedule(string? weekStart = null, string? date = null, string viewMode = "weekly", int branchId = 0, int trainerId = -1)
    {
        ViewData["Username"] = "Admin";

        var today = DateOnly.FromDateTime(DateTime.Today);
        DateOnly targetDate = today;
        if (viewMode == "daily" && !string.IsNullOrWhiteSpace(date) && DateOnly.TryParse(date, out var parsedDaily))
        {
            targetDate = parsedDaily;
        }
        else if (!string.IsNullOrWhiteSpace(weekStart) && DateOnly.TryParse(weekStart, out var parsedDate))
        {
            targetDate = parsedDate;
        }

        int diffToMonday = ((int)targetDate.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        var monday = targetDate.AddDays(-diffToMonday);
        var sunday = monday.AddDays(6);

        var query = _db.Bookings
            .Include(b => b.User)
            .Include(b => b.Pod)
                .ThenInclude(p => p!.Branch)
            .Include(b => b.Trainer)
                .ThenInclude(t => t!.User)
            .Include(b => b.CheckIns)
            .Where(b => b.Status != "Cancelled");

        if (viewMode == "daily")
        {
            query = query.Where(b => b.BookingDate == targetDate);
        }
        else
        {
            query = query.Where(b => b.BookingDate >= monday && b.BookingDate <= sunday);
        }

        if (branchId > 0)
        {
            query = query.Where(b => b.Pod != null && b.Pod.BranchId == branchId);
        }

        if (trainerId == 0)
        {
            query = query.Where(b => b.TrainerId == null);
        }
        else if (trainerId > 0)
        {
            query = query.Where(b => b.TrainerId == trainerId);
        }

        var bookings = query
            .OrderBy(b => b.BookingDate)
            .ThenBy(b => b.StartTime)
            .ToList();

        ViewBag.ViewMode = viewMode == "daily" ? "daily" : "weekly";
        ViewBag.SelectedDate = targetDate;
        ViewBag.Monday = monday;
        ViewBag.Sunday = sunday;
        ViewBag.PrevWeek = monday.AddDays(-7).ToString("yyyy-MM-dd");
        ViewBag.NextWeek = monday.AddDays(7).ToString("yyyy-MM-dd");
        ViewBag.SelectedBranchId = branchId;
        ViewBag.SelectedTrainerId = trainerId;
        ViewBag.Branches = _db.Branches.OrderBy(b => b.BranchId).ToList();
        ViewBag.Trainers = _db.Trainers.Include(t => t.User).OrderBy(t => t.TrainerId).ToList();
        ViewBag.Pods = _db.Pods
            .Include(p => p.Branch)
            .Where(p => branchId == 0 || p.BranchId == branchId)
            .OrderBy(p => p.PodId)
            .ToList();

        return View(bookings);
    }
    public IActionResult Promotions() => RedirectToAction("Packages");

    // ================================================================
    // 7. CHECK-IN LOGS (/Admin/CheckIn)
    // ================================================================
    public IActionResult CheckIn(string? search = "")
    {
        ViewData["Username"] = "Admin";
        var checkIns = _db.CheckIns
            .Include(c => c.User)
            .Include(c => c.Booking)
                .ThenInclude(b => b!.Pod)
                    .ThenInclude(p => p!.Branch)
            .OrderByDescending(c => c.CheckInTime)
            .ToList();

        ViewBag.TotalCheckIns = checkIns.Count;
        ViewBag.TodayCheckIns = checkIns.Count(c => c.CheckInTime.HasValue && c.CheckInTime.Value.Date == DateTime.Today);
        ViewBag.ActiveInRoom = checkIns.Count(c => c.CheckOutTime == null);

        if (!string.IsNullOrWhiteSpace(search))
        {
            string q = search.Trim().ToLower();
            checkIns = checkIns.Where(c =>
                (c.User?.FullName?.ToLower().Contains(q) ?? false) ||
                (c.Booking?.Pod?.PodName?.ToLower().Contains(q) ?? false) ||
                ($"#ci-{c.CheckInId:D5}".Contains(q))).ToList();
        }

        ViewBag.Search = search ?? "";
        ViewBag.PendingBookings = _db.Bookings
            .Include(b => b.User)
            .Include(b => b.Pod).ThenInclude(p => p!.Branch)
            .Where(b => b.Status == "Confirmed" && !b.CheckIns.Any() && b.User != null && (b.User.Role != null && b.User.Role.RoleName == "Member"))
            .OrderBy(b => b.BookingDate)
            .ThenBy(b => b.StartTime)
            .Take(25)
            .ToList();
        return View(checkIns.Take(80).ToList());
    }

    // บันทึกเช็กอินเข้าใช้งานหน้าเคาน์เตอร์แอดมิน (ตรวจสถานะคิว, บัญชี และห้อง Pod ก่อนเสมอ)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult ManualCheckIn(int bookingId)
    {
        var booking = _db.Bookings
            .Include(b => b.User)
            .Include(b => b.Pod)
            .Include(b => b.CheckIns)
            .FirstOrDefault(b => b.BookingId == bookingId);

        if (booking != null && booking.Status == "Confirmed" && !booking.CheckIns.Any())
        {
            if (booking.User?.Status == "Suspended" || booking.User?.Status == "Inactive")
            {
                TempData["ErrorMessage"] = $"ไม่สามารถเช็กอินให้ {booking.User?.FullName} ได้ เนื่องจากบัญชีถูกระงับอยู่";
                return RedirectToAction("CheckIn");
            }
            if (booking.Pod?.Status != "Available")
            {
                TempData["ErrorMessage"] = $"ไม่สามารถเช็กอินได้ เนื่องจากห้อง {booking.Pod?.PodName} อยู่ในสถานะปิดปรับปรุง";
                return RedirectToAction("CheckIn");
            }
            _db.CheckIns.Add(new CheckIn
            {
                BookingId = booking.BookingId,
                UserId = booking.UserId,
                CheckInTime = DateTime.Now,
                AccessMethod = "Admin_Desk"
            });
            _db.SaveChanges();
            TempData["SuccessMessage"] = $"เช็กอินสมาชิก {booking.User?.FullName} (#PG-{booking.BookingId:D5}) เรียบร้อยแล้ว";
        }
        return RedirectToAction("CheckIn");
    }

    // บันทึกเช็กเอาต์ออกจากห้องซ้อมและปิดสถานะการจองเป็น Completed
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult ManualCheckOut(int checkInId)
    {
        var ci = _db.CheckIns
            .Include(c => c.User)
            .Include(c => c.Booking)
            .FirstOrDefault(c => c.CheckInId == checkInId);

        if (ci != null && !ci.CheckOutTime.HasValue)
        {
            ci.CheckOutTime = DateTime.Now;
            if (ci.Booking != null && ci.Booking.Status == "Confirmed")
            {
                ci.Booking.Status = "Completed";
            }
            _db.SaveChanges();
            TempData["SuccessMessage"] = $"เช็กเอาต์สมาชิก {ci.User?.FullName} เรียบร้อยแล้ว";
        }
        return RedirectToAction("CheckIn");
    }

    // ================================================================
    // 8. PAYMENTS & BILLING LEDGER (/Admin/Payments)
    // ================================================================
    public IActionResult Payments(string? status = "all", string? search = "")
    {
        ViewData["Username"] = "Admin";
        var allPayments = _db.Payments
            .Include(p => p.User)
            .Include(p => p.Package)
            .OrderByDescending(p => p.PaymentDate)
            .ThenByDescending(p => p.PaymentId)
            .ToList();

        var successList = allPayments.Where(p => p.Status == "Success").ToList();
        ViewBag.TotalRevenue = successList.Sum(p => p.Amount);
        ViewBag.PromptPayRevenue = successList.Where(p => (p.PaymentMethod ?? "").Contains("PromptPay", StringComparison.OrdinalIgnoreCase) || (p.PaymentMethod ?? "").Contains("QR", StringComparison.OrdinalIgnoreCase)).Sum(p => p.Amount);
        ViewBag.CardAndBankRevenue = ViewBag.TotalRevenue - ViewBag.PromptPayRevenue;
        ViewBag.PendingRevenue = allPayments.Where(p => p.Status == "Pending").Sum(p => p.Amount);
        ViewBag.PendingCount = allPayments.Count(p => p.Status == "Pending");

        var filtered = allPayments.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(status) && status != "all")
        {
            filtered = filtered.Where(p => p.Status == status);
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            string q = search.Trim().ToLower();
            filtered = filtered.Where(p =>
                (p.User?.FullName?.ToLower().Contains(q) ?? false) ||
                (p.Package?.PackageName?.ToLower().Contains(q) ?? false) ||
                ($"#pay-{p.PaymentId:D5}".Contains(q)));
        }

        ViewBag.FilterStatus = status ?? "all";
        ViewBag.Search = search ?? "";
        ViewBag.Pods = _db.Pods.Include(p => p.Branch).OrderBy(p => p.PodId).ToList();
        ViewBag.ActiveCheckIns = _db.CheckIns
            .Include(c => c.Booking).ThenInclude(b => b!.User)
            .Include(c => c.Booking).ThenInclude(b => b!.Trainer).ThenInclude(t => t!.User)
            .Where(c => c.CheckOutTime == null && c.Booking != null)
            .ToList();
        return View(filtered.Take(80).ToList());
    }

    // อนุมัติยอดชำระเงิน Pending -> Success (หากเป็นบิลต่ออายุ RENEW- จะเติมสิทธิ์ให้อัตโนมัติ)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult ApprovePayment(int paymentId)
    {
        var payment = _db.Payments
            .Include(p => p.User)
                .ThenInclude(u => u!.MemberSubscriptions)
            .Include(p => p.Package)
            .FirstOrDefault(p => p.PaymentId == paymentId);

        if (payment != null && payment.Status != "Success")
        {
            payment.Status = "Success";
            payment.PaymentDate = DateTime.Now;

            if (payment.User != null && payment.Package != null)
            {
                if ((payment.SlipReference ?? "").StartsWith("RENEW-", StringComparison.OrdinalIgnoreCase))
                {
                    ApplySubscriptionRenewal(_db, payment.User, payment.Package);
                    payment.SlipReference = $"INV-RENEW-{DateTime.Now:yyyyMMdd}-PAY{payment.PaymentId:D4}";
                }
                else
                {
                    var sub = payment.User.MemberSubscriptions
                        .OrderByDescending(s => s.SubscriptionId)
                        .FirstOrDefault(s => s.PackageId == payment.PackageId);

                    if (sub != null)
                    {
                        sub.Status = "Active";
                    }
                }
            }

            _db.SaveChanges();
            TempData["SuccessMessage"] = $"อนุมัติรายการชำระเงิน #PAY-{payment.PaymentId:D5} (฿{payment.Amount:N0}) สำเร็จเรียบร้อยแล้ว";
        }
        return RedirectToAction("Payments");
    }

    // ปฏิเสธยอดชำระเงินเป็น Failed (และลบสัญญาล่วงหน้าออกหากเป็นบิลรอบถัดไป)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult RejectPayment(int paymentId)
    {
        var payment = _db.Payments.FirstOrDefault(p => p.PaymentId == paymentId);
        if (payment != null)
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            var scheduledSub = _db.MemberSubscriptions
                .Where(s => s.UserId == payment.UserId &&
                            s.PackageId == payment.PackageId &&
                            s.Status == "Active" &&
                            s.StartDate > today)
                .OrderByDescending(s => s.SubscriptionId)
                .FirstOrDefault();
            if (scheduledSub != null)
            {
                _db.MemberSubscriptions.Remove(scheduledSub);
            }

            payment.Status = "Failed";
            _db.SaveChanges();
            TempData["SuccessMessage"] = $"ปฏิเสธรายการชำระเงิน #PAY-{payment.PaymentId:D5} เรียบร้อยแล้ว";
        }
        return RedirectToAction("Payments");
    }

    // ================================================================
    // 9. EXECUTIVE REPORTS (/Admin/Reports)
    // ================================================================
    public IActionResult Reports()
    {
        Csi402dbContext.EnsureAllBranchesPodsAndTrainersSeeded(_db);
        ViewData["Username"] = "Admin";

        ViewBag.TotalMembers = _db.Users.Count(u => (u.Role != null && u.Role.RoleName == "Member"));
        ViewBag.TotalTrainers = _db.Trainers.Count();
        ViewBag.TotalBookings = _db.Bookings.Count(b => b.User != null && (b.User.Role != null && b.User.Role.RoleName == "Member"));
        ViewBag.CompletedBookings = _db.Bookings.Count(b => b.Status == "Completed" && b.User != null && (b.User.Role != null && b.User.Role.RoleName == "Member"));
        ViewBag.TotalRevenue = _db.Payments.Where(p => p.Status == "Success").Sum(p => (decimal?)p.Amount) ?? 0m;

        ViewBag.Branches = _db.Branches
            .Include(b => b.Pods)
                .ThenInclude(p => p.Bookings)
            .ToList();

        ViewBag.Packages = _db.Packages
            .Include(p => p.MemberSubscriptions)
            .Include(p => p.Payments)
            .OrderBy(p => p.Price)
            .ToList();

        ViewBag.Trainers = _db.Trainers
            .Include(t => t.User)
                .ThenInclude(u => u!.HomeBranch)
            .Include(t => t.Bookings)
            .OrderBy(t => t.TrainerId)
            .ToList();

        var activeBookings = _db.Bookings
            .Where(b => b.Status != "Cancelled" && b.User != null && (b.User.Role != null && b.User.Role.RoleName == "Member"))
            .ToList();
        var buckets = new (string Label, int MinHour, int MaxHour)[]
        {
            ("06:00 - 09:00", 6, 8),
            ("09:00 - 12:00", 9, 11),
            ("12:00 - 14:00", 12, 13),
            ("14:00 - 17:00", 14, 16),
            ("17:00 - 19:00", 17, 18),
            ("19:00 - 21:00", 19, 20),
            ("21:00 - 23:00", 21, 23)
        };
        int maxBucket = Math.Max(1, buckets.Max(bk => activeBookings.Count(b => b.StartTime.Hour >= bk.MinHour && b.StartTime.Hour <= bk.MaxHour)));
        ViewBag.HourlyDemand = buckets.Select(bk =>
        {
            int cnt = activeBookings.Count(b => b.StartTime.Hour >= bk.MinHour && b.StartTime.Hour <= bk.MaxHour);
            int pct = (int)Math.Round(cnt * 100.0 / maxBucket);
            return new Dictionary<string, object> { ["Label"] = bk.Label, ["Count"] = cnt, ["Pct"] = pct };
        }).ToList();

        return View();
    }

    // ================================================================
    // 10. USERS & ROLES (/Admin/UsersAndRoles)
    // ================================================================
    public IActionResult UsersAndRoles(string? roleFilter = "all", string? search = "")
    {
        ViewData["Username"] = "Admin";
        var roles = _db.Roles.Include(r => r.Users).OrderBy(r => r.RoleId).ToList();
        var users = _db.Users
            .Include(u => u.Role)
            .Include(u => u.HomeBranch)
            .OrderBy(u => u.RoleId)
            .ThenByDescending(u => u.UserId)
            .ToList();

        if (!string.IsNullOrWhiteSpace(roleFilter) && roleFilter != "all" && int.TryParse(roleFilter, out int rId))
        {
            users = users.Where(u => u.RoleId == rId).ToList();
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            string q = search.Trim().ToLower();
            users = users.Where(u =>
                (u.FullName?.ToLower().Contains(q) ?? false) ||
                (u.Email?.ToLower().Contains(q) ?? false) ||
                (u.Role?.RoleName?.ToLower().Contains(q) ?? false)).ToList();
        }

        ViewBag.Users = users;
        ViewBag.RoleFilter = roleFilter ?? "all";
        ViewBag.Search = search ?? "";
        return View(roles);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult ChangeUserRole(int userId, int roleId)
    {
        var user = _db.Users.FirstOrDefault(u => u.UserId == userId);
        var role = _db.Roles.FirstOrDefault(r => r.RoleId == roleId);
        if (user != null && role != null)
        {
            user.RoleId = role.RoleId;
            _db.SaveChanges();

            if (string.Equals(role.RoleName, "Trainer", StringComparison.OrdinalIgnoreCase) && !_db.Trainers.Any(t => t.UserId == user.UserId))
            {
                _db.Trainers.Add(new Trainer
                {
                    UserId = user.UserId,
                    Specialty = "Strength & Conditioning",
                    Bio = "Personal Trainer ประจำคลับ PrivateGym",
                    HourlyRate = 1500m
                });
                _db.SaveChanges();
            }

            TempData["SuccessMessage"] = $"เปลี่ยนบทบาทของคุณ {user.FullName} เป็น {role.RoleName} เรียบร้อยแล้ว";
        }
        return RedirectToAction("UsersAndRoles");
    }

    // ================================================================
    // 11. SETTINGS & BRANCHES (/Admin/Settings)
    // ================================================================
    public IActionResult Settings()
    {
        Csi402dbContext.EnsureAllBranchesPodsAndTrainersSeeded(_db);
        ViewData["Username"] = "Admin";
        var branches = _db.Branches
            .Include(b => b.Pods)
            .Include(b => b.Users)
            .OrderBy(b => b.BranchId)
            .ToList();
        return View(branches);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult UpdateBranch(int branchId, string branchName, string location)
    {
        var branch = _db.Branches.FirstOrDefault(b => b.BranchId == branchId);
        if (branch != null && !string.IsNullOrWhiteSpace(branchName))
        {
            branch.BranchName = branchName.Trim();
            branch.Location = string.IsNullOrWhiteSpace(location) ? branch.Location : location.Trim();
            _db.SaveChanges();
            TempData["SuccessMessage"] = $"อัปเดตข้อมูลสาขา {branch.BranchName} เรียบร้อยแล้ว";
        }
        return RedirectToAction("Settings");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult AddBranch(string branchName, string location)
    {
        if (!string.IsNullOrWhiteSpace(branchName))
        {
            _db.Branches.Add(new Branch
            {
                BranchName = branchName.Trim(),
                Location = string.IsNullOrWhiteSpace(location) ? "กรุงเทพมหานคร" : location.Trim()
            });
            _db.SaveChanges();
            TempData["SuccessMessage"] = $"เพิ่มสาขาใหม่ {branchName.Trim()} เข้าสู่ระบบเรียบร้อยแล้ว";
        }
        return RedirectToAction("Settings");
    }

    [HttpGet]
    public IActionResult Logout()
    {
        return RedirectToAction("Index", "Home");
    }
}
