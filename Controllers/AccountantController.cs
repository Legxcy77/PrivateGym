using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PrivateGym.Models;

namespace PrivateGym.Controllers;

// [AccountantController] โซนหลัก:
// 1) ภาพรวมรายรับ & ตรวจรับเงิน: Index, Payments, ApprovePayment, RejectPayment
// 2) ใบกำกับภาษี & รอบบิลสมาชิก: TaxInvoices, Subscriptions
using PrivateGym.Filters;

[SessionAuthorize("Accountant", "Admin")]
public class AccountantController : Controller
{
    private readonly Csi402dbContext _db; // ตัวเชื่อมต่อฐานข้อมูล MySQL ผ่าน EF Core

    public AccountantController(Csi402dbContext db)
    {
        _db = db;
    }

    // ================================================================
    // 1. ภาพรวมรายรับและการเงิน (ACCOUNTING DASHBOARD)
    // ================================================================
    public IActionResult Index(string? email)
    {
        EnsureAccountantSeeded();
        var user = GetCurrentAccountant(email);
        SetAccountantViewBag(user);

        var allPayments = _db.Payments
            .Include(p => p.User)
                .ThenInclude(u => u.HomeBranch)
            .Include(p => p.Package)
            .OrderByDescending(p => p.PaymentDate)
            .ThenByDescending(p => p.PaymentId)
            .ToList();

        var successPayments = allPayments.Where(p => p.Status == "Success").ToList();
        var pendingPayments = allPayments.Where(p => p.Status == "Pending").ToList();
        var failedPayments = allPayments.Where(p => p.Status == "Failed").ToList();

        var now = DateTime.Now;
        decimal totalRevenue = successPayments.Sum(p => p.Amount);
        decimal thisMonthRevenue = successPayments
            .Where(p => p.PaymentDate.HasValue && p.PaymentDate.Value.Month == now.Month && p.PaymentDate.Value.Year == now.Year)
            .Sum(p => p.Amount);

        decimal pendingAmount = pendingPayments.Sum(p => p.Amount);

        // แยกรายรับตามช่องทางชำระเงิน
        decimal promptPayTotal = successPayments.Where(p => p.PaymentMethod == "PromptPay").Sum(p => p.Amount);
        decimal creditCardTotal = successPayments.Where(p => p.PaymentMethod == "CreditCard").Sum(p => p.Amount);
        decimal bankTransferTotal = successPayments.Where(p => p.PaymentMethod == "BankTransfer").Sum(p => p.Amount);

        // แยกรายรับตามแพ็กเกจ
        var packageBreakdown = successPayments
            .GroupBy(p => p.Package?.PackageName ?? "อื่นๆ")
            .Select(g => new
            {
                PackageName = g.Key,
                Count = g.Count(),
                TotalAmount = g.Sum(x => x.Amount)
            })
            .OrderByDescending(x => x.TotalAmount)
            .ToList();

        // นับสมาชิกที่มีแพ็กเกจ Active ในรอบปัจจุบัน
        var today = DateOnly.FromDateTime(DateTime.Today);
        int activeSubCount = _db.MemberSubscriptions
            .Count(s => s.Status == "Active" && s.StartDate <= today && s.EndDate >= today);

        ViewBag.TotalRevenue = totalRevenue;
        ViewBag.ThisMonthRevenue = thisMonthRevenue;
        ViewBag.PendingAmount = pendingAmount;
        ViewBag.PendingCount = pendingPayments.Count;
        ViewBag.SuccessCount = successPayments.Count;
        ViewBag.FailedCount = failedPayments.Count;
        ViewBag.ActiveSubCount = activeSubCount;

        ViewBag.PromptPayTotal = promptPayTotal;
        ViewBag.CreditCardTotal = creditCardTotal;
        ViewBag.BankTransferTotal = bankTransferTotal;
        ViewBag.PackageBreakdown = packageBreakdown;

        return View(allPayments.Take(10).ToList());
    }

    // ================================================================
    // 2. ตรวจสอบและอนุมัติรายการชำระเงิน (PAYMENTS & APPROVALS)
    // ================================================================
    public IActionResult Payments(string? email, string? status = "all", string? method = "all", string? search = "")
    {
        EnsureAccountantSeeded();
        var user = GetCurrentAccountant(email);
        SetAccountantViewBag(user);

        var query = _db.Payments
            .Include(p => p.User)
                .ThenInclude(u => u.HomeBranch)
            .Include(p => p.Package)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(status) && status != "all")
        {
            query = query.Where(p => p.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(method) && method != "all")
        {
            query = query.Where(p => p.PaymentMethod == method);
        }

        var list = query
            .OrderByDescending(p => p.PaymentDate)
            .ThenByDescending(p => p.PaymentId)
            .ToList();

        if (!string.IsNullOrWhiteSpace(search))
        {
            string q = search.Trim().ToLower();
            list = list.Where(p =>
                (p.User?.FullName?.ToLower().Contains(q) ?? false) ||
                (p.User?.Email?.ToLower().Contains(q) ?? false) ||
                (p.SlipReference?.ToLower().Contains(q) ?? false) ||
                (p.Package?.PackageName?.ToLower().Contains(q) ?? false)
            ).ToList();
        }

        ViewBag.FilterStatus = status ?? "all";
        ViewBag.FilterMethod = method ?? "all";
        ViewBag.Search = search ?? "";

        ViewBag.AllCount = _db.Payments.Count();
        ViewBag.PendingCount = _db.Payments.Count(p => p.Status == "Pending");
        ViewBag.SuccessCount = _db.Payments.Count(p => p.Status == "Success");
        ViewBag.FailedCount = _db.Payments.Count(p => p.Status == "Failed");

        return View(list);
    }

    // POST: /Accountant/ApprovePayment (ยืนยันรับชำระเงิน -> เปลี่ยน Pending เป็น Success)
    [HttpPost]
    public IActionResult ApprovePayment(int paymentId, string? email)
    {
        var payment = _db.Payments
            .Include(p => p.User)
            .Include(p => p.Package)
            .FirstOrDefault(p => p.PaymentId == paymentId);

        if (payment == null)
        {
            TempData["Error"] = "ไม่พบรายการชำระเงินที่ต้องการอนุมัติ";
            return RedirectToAction("Payments", new { email });
        }

        if (payment.Status == "Success")
        {
            TempData["Error"] = "รายการนี้ได้รับการอนุมัติเรียบร้อยแล้ว";
            return RedirectToAction("Payments", new { email });
        }

        payment.Status = "Success";
        payment.PaymentDate = DateTime.Now;
        if (string.IsNullOrWhiteSpace(payment.SlipReference))
        {
            payment.SlipReference = $"INV-{DateTime.Now:yyyyMMdd}-ACC{payment.PaymentId:D4}";
        }
        else if (payment.SlipReference.StartsWith("RENEW-", StringComparison.OrdinalIgnoreCase))
        {
            if (payment.User != null && payment.Package != null)
            {
                AdminController.ApplySubscriptionRenewal(_db, payment.User, payment.Package);
            }
            payment.SlipReference = $"INV-RENEW-{DateTime.Now:yyyyMMdd}-ACC{payment.PaymentId:D4}";
        }
        else if (payment.SlipReference.StartsWith("NEXT-"))
        {
            // ใช้ prefix INV-NEXT เพื่อให้ระบบ Auto-Rollover ของฝั่ง Customer รู้ว่าชำระบิลรอบถัดไปล่วงหน้าแล้ว
            payment.SlipReference = $"INV-NEXT-{DateTime.Now:yyyyMMdd}-ACC{payment.PaymentId:D4}";
        }

        _db.SaveChanges();

        TempData["Success"] = $"อนุมัติรับชำระเงินรายการ #PAY-{payment.PaymentId:D5} ({payment.User?.FullName} • ฿{payment.Amount:N0}) เรียบร้อยแล้ว";
        return RedirectToAction("Payments", new { email });
    }

    // POST: /Accountant/RejectPayment (ปฏิเสธรายการชำระเงิน -> เปลี่ยนเป็น Failed และยกเลิกสัญญารอบถัดไปถ้าเป็นบิลล่วงหน้า)
    [HttpPost]
    public IActionResult RejectPayment(int paymentId, string? email)
    {
        var payment = _db.Payments
            .Include(p => p.User)
            .FirstOrDefault(p => p.PaymentId == paymentId);

        if (payment == null)
        {
            TempData["Error"] = "ไม่พบรายการชำระเงินที่ต้องการปฏิเสธ";
            return RedirectToAction("Payments", new { email });
        }

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

        TempData["Success"] = $"บันทึกรายการ #PAY-{payment.PaymentId:D5} ({payment.User?.FullName}) เป็นไม่สำเร็จ/ยกเลิกแล้ว";
        return RedirectToAction("Payments", new { email });
    }

    // ================================================================
    // 3. ใบเสร็จรับเงินและใบกำกับภาษี (TAX INVOICES & RECEIPTS)
    // ================================================================
    public IActionResult TaxInvoices(string? email, string? search = "", string? taxOnly = "all")
    {
        EnsureAccountantSeeded();
        var user = GetCurrentAccountant(email);
        SetAccountantViewBag(user);

        var paidList = _db.Payments
            .Include(p => p.User)
                .ThenInclude(u => u.HomeBranch)
            .Include(p => p.Package)
            .Where(p => p.Status == "Success")
            .OrderByDescending(p => p.PaymentDate)
            .ThenByDescending(p => p.PaymentId)
            .ToList();

        if (taxOnly == "tax")
        {
            paidList = paidList.Where(p => !string.IsNullOrWhiteSpace(p.User?.TaxId)).ToList();
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            string q = search.Trim().ToLower();
            paidList = paidList.Where(p =>
                (p.User?.FullName?.ToLower().Contains(q) ?? false) ||
                (p.User?.TaxId?.ToLower().Contains(q) ?? false) ||
                (p.SlipReference?.ToLower().Contains(q) ?? false)
            ).ToList();
        }

        ViewBag.Search = search ?? "";
        ViewBag.TaxOnly = taxOnly ?? "all";
        return View(paidList);
    }

    // ================================================================
    // 4. ติดตามสัญญาสมาชิกและรอบบิล (SUBSCRIPTIONS)
    // ================================================================
    public IActionResult Subscriptions(string? email, string? filter = "all")
    {
        EnsureAccountantSeeded();
        var user = GetCurrentAccountant(email);
        SetAccountantViewBag(user);

        var today = DateOnly.FromDateTime(DateTime.Today);
        var allSubs = _db.MemberSubscriptions
            .Include(s => s.User)
                .ThenInclude(u => u.HomeBranch)
            .Include(s => s.Package)
            .OrderByDescending(s => s.SubscriptionId)
            .ToList();

        var activeList = allSubs.Where(s => s.Status == "Active" && s.StartDate <= today && s.EndDate >= today).ToList();
        var scheduledList = allSubs.Where(s => s.Status == "Active" && s.StartDate > today).ToList();
        var expiringList = activeList.Where(s => (s.EndDate.DayNumber - today.DayNumber) <= 7).ToList();
        var expiredList = allSubs.Where(s => s.Status != "Active" || s.EndDate < today).ToList();

        var displayList = (filter ?? "all").ToLower() switch
        {
            "active" => activeList,
            "scheduled" => scheduledList,
            "expiring" => expiringList,
            "expired" => expiredList,
            _ => allSubs
        };

        ViewBag.Filter = filter ?? "all";
        ViewBag.AllCount = allSubs.Count;
        ViewBag.ActiveCount = activeList.Count;
        ViewBag.ScheduledCount = scheduledList.Count;
        ViewBag.ExpiringCount = expiringList.Count;
        ViewBag.ExpiredCount = expiredList.Count;

        return View(displayList);
    }

    // ================================================================
    // HELPER FUNCTIONS (เข้าใจง่าย แพทเทิร์นเดียวกับ CustomerController)
    // ================================================================
    private void EnsureAccountantSeeded()
    {
        var accountantRole = _db.Roles.FirstOrDefault(r => r.RoleName == "Accountant");
        if (accountantRole == null)
        {
            accountantRole = new Role
            {
                RoleName = "Accountant",
                Description = "ฝ่ายบัญชีและการเงิน"
            };
            _db.Roles.Add(accountantRole);
            _db.SaveChanges();
        }

        if (!_db.Users.Any(u => u.RoleId == accountantRole.RoleId))
        {
            int defaultBranchId = _db.Branches.FirstOrDefault()?.BranchId ?? 1;
            _db.Users.Add(new User
            {
                FullName = "คุณณัฐิดา ฝ่ายบัญชี (Accountant)",
                Email = "accountant@privategym.club",
                Phone = "081-999-0005",
                PasswordHash = "123456",
                RoleId = accountantRole.RoleId,
                HomeBranchId = defaultBranchId,
                Status = "Active",
                CreatedAt = DateTime.Now
            });
            _db.SaveChanges();
        }
    }

    private User? GetCurrentAccountant(string? email)
    {
        if (!string.IsNullOrWhiteSpace(email))
        {
            string clean = email.Trim();
            var matched = _db.Users
                .Include(u => u.HomeBranch)
                .Include(u => u.Role)
                .FirstOrDefault(u => u.Email == clean || u.Phone == clean);
            if (matched != null) return matched;
        }

        var role = _db.Roles.FirstOrDefault(r => r.RoleName == "Accountant");
        if (role != null)
        {
            return _db.Users
                .Include(u => u.HomeBranch)
                .Include(u => u.Role)
                .FirstOrDefault(u => u.RoleId == role.RoleId);
        }

        return null;
    }

    private void SetAccountantViewBag(User? user)
    {
        ViewBag.Email = user?.Email ?? "accountant@privategym.club";
        ViewBag.FullName = user?.FullName ?? "ฝ่ายบัญชีและการเงิน";
        ViewData["FullName"] = ViewBag.FullName;
    }
}

