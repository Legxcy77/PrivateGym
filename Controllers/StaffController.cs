using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PrivateGym.Models;

namespace PrivateGym.Controllers;

// [StaffController] โซนหลัก:
// 1) หน้างาน & เช็กอิน: Index, CheckInMember, CheckOutMember, AddHandoverNote
// 2) จัดการห้องซ้อม & ตาราง: Pods, TogglePodStatus, LockPodSlot, UnlockPodSlot, Schedule
// 3) สมาชิก & ประวัติเข้า-ออก: Members, History
using PrivateGym.Filters;

[SessionAuthorize("Staff", "Admin")]
public class StaffController : Controller
{
    private readonly Csi402dbContext _db; // ตัวเชื่อมต่อฐานข้อมูล MySQL ผ่าน EF Core

    public StaffController(Csi402dbContext db)
    {
        _db = db;
    }

    // ================================================================
    // 1. หน้างานและเช็กอินหน้าเคาน์เตอร์ (OPERATIONS & CHECK-IN)
    // ================================================================
    public IActionResult Index(string? email, string? tab = "today", string? search = "", int? branchId = null)
    {
        EnsureStaffSeeded();
        Csi402dbContext.EnsureAllBranchesPodsAndTrainersSeeded(_db);
        var staffUser = GetCurrentStaff(email);
        SetStaffViewBag(staffUser);

        SyncExpiredCheckInsAll();

        var today = DateOnly.FromDateTime(DateTime.Today);
        var nowTime = TimeOnly.FromDateTime(DateTime.Now);

        var branches = _db.Branches
            .Include(b => b.Pods)
            .OrderByDescending(b => b.Pods.Count)
            .ThenBy(b => b.BranchId)
            .ToList();

        int selectedBranchId = (branchId.HasValue && branches.Any(b => b.BranchId == branchId.Value))
            ? branchId.Value
            : 0; // 0 = ทุกสาขา

        var podsQuery = _db.Pods.Include(p => p.Branch).AsQueryable();
        if (selectedBranchId > 0)
        {
            podsQuery = podsQuery.Where(p => p.BranchId == selectedBranchId);
        }
        var pods = podsQuery.OrderBy(p => p.BranchId).ThenBy(p => p.PodName).ToList();

        var activeBookingsQuery = _db.Bookings
            .Include(b => b.User)
                .ThenInclude(u => u!.MemberSubscriptions)
                    .ThenInclude(ms => ms.Package)
            .Include(b => b.Pod)
                .ThenInclude(p => p.Branch)
            .Include(b => b.Trainer)
                .ThenInclude(t => t!.User)
            .Include(b => b.CheckIns)
            .Where(b => b.Status == "Confirmed" &&
                        b.User != null && b.User.RoleId == 4 &&
                        (b.BookingDate > today || (b.BookingDate == today && b.EndTime > nowTime)));

        if (selectedBranchId > 0)
        {
            activeBookingsQuery = activeBookingsQuery.Where(b => b.Pod != null && b.Pod.BranchId == selectedBranchId);
        }

        var activeBookings = activeBookingsQuery
            .OrderBy(b => b.BookingDate)
            .ThenBy(b => b.StartTime)
            .ToList();

        var todayList = activeBookings.Where(b => b.BookingDate == today).ToList();
        var inRoomList = activeBookings.Where(b => b.CheckIns != null && b.CheckIns.Any()).ToList();

        var todayAllMemberBookingsQuery = _db.Bookings
            .Include(b => b.User)
            .Include(b => b.Pod)
            .Include(b => b.Trainer)
                .ThenInclude(t => t!.User)
            .Include(b => b.CheckIns)
            .Where(b => b.BookingDate == today &&
                        b.Status != "Cancelled" &&
                        b.User != null && b.User.RoleId == 4);

        if (selectedBranchId > 0)
        {
            todayAllMemberBookingsQuery = todayAllMemberBookingsQuery.Where(b => b.Pod != null && b.Pod.BranchId == selectedBranchId);
        }
        var todayAllMemberBookings = todayAllMemberBookingsQuery.ToList();

        var slotLocksQuery = _db.Bookings
            .Include(b => b.Pod)
                .ThenInclude(p => p.Branch)
            .Include(b => b.User)
            .Where(b => b.BookingDate == today &&
                        b.Status == "Confirmed" &&
                        b.EndTime > nowTime &&
                        (b.User == null || b.User.RoleId != 4));

        if (selectedBranchId > 0)
        {
            slotLocksQuery = slotLocksQuery.Where(b => b.Pod != null && b.Pod.BranchId == selectedBranchId);
        }
        var slotLocks = slotLocksQuery.OrderBy(b => b.StartTime).ToList();

        var displayList = (tab ?? "today").ToLower() switch
        {
            "active" => inRoomList,
            "upcoming" => activeBookings,
            _ => todayList
        };

        if (!string.IsNullOrWhiteSpace(search))
        {
            string q = search.Trim().ToLower();
            displayList = displayList.Where(b =>
                (b.User?.FullName?.ToLower().Contains(q) ?? false) ||
                (b.User?.Phone?.ToLower().Contains(q) ?? false) ||
                (b.Pod?.PodName?.ToLower().Contains(q) ?? false) ||
                ($"#pg-{b.BookingId:D5}".Contains(q))
            ).ToList();
        }

        int totalPods = pods.Count;
        int availablePods = pods.Count(p => p.Status == "Available");
        int maintenancePods = totalPods - availablePods;
        int todayCheckedInCount = todayAllMemberBookings.Count(b => (b.CheckIns != null && b.CheckIns.Any()) || b.Status == "Completed");

        ViewBag.Tab = tab ?? "today";
        ViewBag.Search = search ?? "";
        ViewBag.Branches = branches;
        ViewBag.SelectedBranchId = selectedBranchId;
        ViewBag.Pods = pods;
        ViewBag.TodayBookings = todayList;
        ViewBag.SlotLocks = slotLocks;
        ViewBag.TodayCount = todayList.Count;
        ViewBag.TodayTotalCount = todayAllMemberBookings.Count;
        ViewBag.TodayCheckedInCount = todayCheckedInCount;
        ViewBag.InRoomCount = inRoomList.Count;
        ViewBag.UpcomingCount = activeBookings.Count;
        ViewBag.AvailablePods = availablePods;
        ViewBag.MaintenancePods = maintenancePods;
        ViewBag.TotalPods = totalPods;
        ViewBag.MaintenanceLogs = ReadMaintenanceLogs().Take(5).ToList();
        ViewBag.HandoverNotes = ReadHandoverNotes().Take(8).ToList();

        return View(displayList);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult AddHandoverNote(string note, string? email, string? tab, int? branchId)
    {
        EnsureStaffSeeded();
        var staffUser = GetCurrentStaff(email);
        if (!string.IsNullOrWhiteSpace(note))
        {
            AppendHandoverNote(note.Trim(), staffUser?.FullName ?? "พนักงานสาขา");
            TempData["SuccessMsg"] = "บันทึกข้อความส่งเวรเรียบร้อยแล้ว";
        }
        return RedirectToAction("Index", new { email = staffUser?.Email ?? email, tab = tab ?? "today", branchId });
    }

    // POST: /Staff/CheckInMember (พนักงานกดเช็กอินเข้าห้องซ้อมให้สมาชิกหน้าเคาน์เตอร์)
    // กดเช็กอินให้สมาชิกเข้าใช้งานห้อง Pod หน้าเคาน์เตอร์ (สร้างแถวใน CheckIns)
    [HttpPost]
    public IActionResult CheckInMember(int bookingId, string? email)
    {
        var booking = _db.Bookings
            .Include(b => b.User)
            .Include(b => b.Pod)
            .Include(b => b.CheckIns)
            .FirstOrDefault(b => b.BookingId == bookingId && b.Status == "Confirmed");

        if (booking == null)
        {
            TempData["Error"] = "ไม่พบรายการจองที่สามารถเช็กอินได้";
            return RedirectToAction("Index", new { email });
        }

        if (!booking.CheckIns.Any())
        {
            _db.CheckIns.Add(new CheckIn
            {
                BookingId = booking.BookingId,
                UserId = booking.UserId,
                CheckInTime = DateTime.Now,
                AccessMethod = "QR_Code"
            });
            _db.SaveChanges();
        }

        TempData["Success"] = $"เช็กอินคุณ {booking.User?.FullName} เข้าห้อง {booking.Pod?.PodName} (#PG-{booking.BookingId:D5}) เรียบร้อยแล้ว";
        return RedirectToAction("Index", new { email, tab = "active" });
    }

    // POST: /Staff/CheckOutMember (พนักงานกดเช็กเอาต์ / ปิดรอบห้องซ้อม)
    // กดเช็กเอาต์เมื่อสมาชิกซ้อมเสร็จ (บันทึก CheckOutTime และปิดคิวเป็น Completed)
    [HttpPost]
    public IActionResult CheckOutMember(int bookingId, string? email)
    {
        var booking = _db.Bookings
            .Include(b => b.User)
            .Include(b => b.Pod)
            .Include(b => b.CheckIns)
            .FirstOrDefault(b => b.BookingId == bookingId);

        if (booking == null)
        {
            TempData["Error"] = "ไม่พบรายการจองที่ต้องการเช็กเอาต์";
            return RedirectToAction("Index", new { email });
        }

        var ci = booking.CheckIns.OrderByDescending(c => c.CheckInId).FirstOrDefault();
        if (ci != null && ci.CheckOutTime == null)
        {
            ci.CheckOutTime = DateTime.Now;
        }
        else if (ci == null)
        {
            _db.CheckIns.Add(new CheckIn
            {
                BookingId = booking.BookingId,
                UserId = booking.UserId,
                CheckInTime = DateTime.Now.AddMinutes(-45),
                CheckOutTime = DateTime.Now,
                AccessMethod = "QR_Code"
            });
        }

        booking.Status = "Completed";
        _db.SaveChanges();

        TempData["Success"] = $"เช็กเอาต์คุณ {booking.User?.FullName} ออกจากห้อง {booking.Pod?.PodName} เรียบร้อยแล้ว";
        return RedirectToAction("Index", new { email });
    }

    // ================================================================
    // 2. สถานะห้องซ้อม (PODS STATUS & MAINTENANCE)
    // ================================================================
    public IActionResult Pods(string? email)
    {
        EnsureStaffSeeded();
        Csi402dbContext.EnsureAllBranchesPodsAndTrainersSeeded(_db);
        var staffUser = GetCurrentStaff(email);
        SetStaffViewBag(staffUser);

        var today = DateOnly.FromDateTime(DateTime.Today);
        var nowTime = TimeOnly.FromDateTime(DateTime.Now);

        var pods = _db.Pods
            .Include(p => p.Branch)
            .OrderBy(p => p.PodId)
            .ToList();

        var todayBookings = _db.Bookings
            .Include(b => b.User)
            .Include(b => b.CheckIns)
            .Where(b => b.BookingDate == today && b.Status == "Confirmed" && b.EndTime > nowTime)
            .OrderBy(b => b.StartTime)
            .ToList();

        var slotLocks = _db.Bookings
            .Include(b => b.User)
            .Where(b => b.BookingDate >= today && b.Status == "Confirmed" && b.User != null && b.User.RoleId != 4)
            .OrderBy(b => b.BookingDate)
            .ThenBy(b => b.StartTime)
            .ToList();

        ViewBag.TodayBookings = todayBookings;
        ViewBag.SlotLocks = slotLocks;
        ViewBag.MaintenanceLogs = ReadMaintenanceLogs();
        return View(pods);
    }

    [HttpPost]
    public IActionResult LockPodSlot(int podId, string date, string startTime, string? reason, string? email)
    {
        EnsureStaffSeeded();
        var staffUser = GetCurrentStaff(email);
        var pod = _db.Pods.FirstOrDefault(p => p.PodId == podId);
        if (staffUser != null && pod != null &&
            DateOnly.TryParse(date, out var lockDate) &&
            TimeOnly.TryParse(startTime, out var slotStart))
        {
            var slotEnd = slotStart.AddMinutes(75);
            bool alreadyLockedOrBooked = _db.Bookings.Any(b =>
                b.PodId == podId &&
                b.BookingDate == lockDate &&
                b.StartTime == slotStart &&
                b.Status == "Confirmed");

            if (alreadyLockedOrBooked)
            {
                TempData["Error"] = $"ห้อง {pod.PodName} รอบ {slotStart:HH:mm} น. วันที่ {lockDate:dd/MM/yyyy} มีคิวจองหรือถูกล็อกไว้อยู่แล้ว";
            }
            else
            {
                string cleanReason = string.IsNullOrWhiteSpace(reason) ? "ปิดปรับปรุงและตรวจเช็กห้องตามรอบ" : reason.Trim();
                _db.Bookings.Add(new Booking
                {
                    UserId = staffUser.UserId,
                    PodId = podId,
                    TrainerId = null,
                    BookingDate = lockDate,
                    StartTime = slotStart,
                    EndTime = slotEnd,
                    Status = "Confirmed",
                    CreatedAt = DateTime.Now
                });
                _db.SaveChanges();

                AppendMaintenanceLog(
                    pod.PodName,
                    "ปิดเฉพาะรอบเวลา",
                    $"{lockDate:dd/MM/yyyy} ({slotStart:HH:mm}-{slotEnd:HH:mm})",
                    cleanReason,
                    staffUser.FullName
                );
                TempData["Success"] = $"ปิดปรับปรุงเฉพาะรอบ {slotStart:HH:mm} - {slotEnd:HH:mm} น. ของห้อง {pod.PodName} ({lockDate:dd/MM/yyyy}) เรียบร้อยแล้ว";
            }
        }
        return RedirectToAction("Pods", new { email });
    }

    [HttpPost]
    public IActionResult UnlockPodSlot(int bookingId, string? email)
    {
        var staffUser = GetCurrentStaff(email);
        var lockBooking = _db.Bookings.Include(b => b.Pod).FirstOrDefault(b => b.BookingId == bookingId);
        if (lockBooking != null)
        {
            lockBooking.Status = "Cancelled";
            _db.SaveChanges();
            AppendMaintenanceLog(
                lockBooking.Pod?.PodName ?? $"Pod #{lockBooking.PodId}",
                "เปิดให้บริการรอบเวลา",
                $"{lockBooking.BookingDate:dd/MM/yyyy} ({lockBooking.StartTime:HH:mm}-{lockBooking.EndTime:HH:mm})",
                "ยกเลิกการปิดปรับปรุงรอบเวลา (พร้อมให้สมาชิกจอง)",
                staffUser?.FullName ?? "พนักงานสาขา"
            );
            TempData["Success"] = $"เปิดให้บริการรอบ {lockBooking.StartTime:HH:mm} น. ของห้อง {lockBooking.Pod?.PodName} เรียบร้อยแล้ว";
        }
        return RedirectToAction("Pods", new { email });
    }

    // สลับสถานะห้องซ้อม Pod (Available <-> Maintenance) ซิงค์กับหน้า Admin และ Customer ทันที
    [HttpPost]
    public IActionResult TogglePodStatus(int podId, string? reason, string? email)
    {
        var staffUser = GetCurrentStaff(email);
        var pod = _db.Pods.FirstOrDefault(p => p.PodId == podId);
        if (pod != null)
        {
            pod.Status = pod.Status == "Available" ? "Maintenance" : "Available";
            _db.SaveChanges();
            string statusText = pod.Status == "Available" ? "พร้อมใช้งาน (Available)" : "ปิดปรับปรุง (Maintenance)";
            string actionLabel = pod.Status == "Available" ? "เปิดให้บริการทั้งห้อง" : "ปิดปรับปรุงทั้งห้อง";
            string cleanReason = string.IsNullOrWhiteSpace(reason)
                ? (pod.Status == "Available" ? "เปิดให้บริการห้องตามปกติ" : "ปิดปรับปรุงห้องทั้งวัน")
                : reason.Trim();

            AppendMaintenanceLog(
                pod.PodName,
                actionLabel,
                "ทั้งวัน",
                cleanReason,
                staffUser?.FullName ?? "พนักงานสาขา"
            );
            TempData["Success"] = $"เปลี่ยนสถานะห้อง {pod.PodName} เป็น {statusText} เรียบร้อยแล้ว";
        }
        return RedirectToAction("Pods", new { email });
    }

    // ================================================================
    // 3. รายชื่อสมาชิก (MEMBERS DIRECTORY)
    // ================================================================
    public IActionResult Members(string? email, string? search = "")
    {
        EnsureStaffSeeded();
        var staffUser = GetCurrentStaff(email);
        SetStaffViewBag(staffUser);

        var members = _db.Users
            .Include(u => u.HomeBranch)
            .Include(u => u.MemberSubscriptions)
                .ThenInclude(ms => ms.Package)
            .Where(u => u.RoleId == 4)
            .OrderByDescending(u => u.UserId)
            .ToList();

        if (!string.IsNullOrWhiteSpace(search))
        {
            string q = search.Trim().ToLower();
            members = members.Where(u =>
                (u.FullName?.ToLower().Contains(q) ?? false) ||
                (u.Email?.ToLower().Contains(q) ?? false) ||
                (u.Phone?.ToLower().Contains(q) ?? false)
            ).ToList();
        }

        ViewBag.Search = search ?? "";
        return View(members);
    }

    // ================================================================
    // 4. ประวัติการเข้า-ออกสาขา (BRANCH HISTORY)
    // ================================================================
    public IActionResult History(string? email)
    {
        EnsureStaffSeeded();
        var staffUser = GetCurrentStaff(email);
        SetStaffViewBag(staffUser);

        SyncExpiredCheckInsAll();

        var today = DateOnly.FromDateTime(DateTime.Today);
        var nowTime = TimeOnly.FromDateTime(DateTime.Now);

        var pastBookings = _db.Bookings
            .Include(b => b.User)
            .Include(b => b.Pod)
                .ThenInclude(p => p.Branch)
            .Include(b => b.CheckIns)
            .OrderByDescending(b => b.BookingDate)
            .ThenByDescending(b => b.StartTime)
            .ToList()
            .Where(b =>
                b.User != null && b.User.RoleId == 4 &&
                (b.Status == "Completed" ||
                 b.Status == "Cancelled" ||
                 (b.CheckIns != null && b.CheckIns.Any()) ||
                 b.BookingDate < today ||
                 (b.BookingDate == today && b.EndTime <= nowTime)))
            .Take(40)
            .ToList();

        return View(pastBookings);
    }

    // ================================================================
    // HELPER FUNCTIONS (แพทเทิร์นเดียวกับ CustomerController)
    // ================================================================
    private void EnsureStaffSeeded()
    {
        int staffRoleId = _db.Roles.FirstOrDefault(r => r.RoleName == "Staff")?.RoleId ?? 2;
        if (!_db.Users.Any(u => u.Email == "staff@privategym.club"))
        {
            int defaultBranchId = _db.Branches.FirstOrDefault()?.BranchId ?? 1;
            _db.Users.Add(new User
            {
                FullName = "คุณกิตติพงษ์ พนักงานสาขา (Staff)",
                Email = "staff@privategym.club",
                Phone = "081-999-0002",
                PasswordHash = "123456",
                RoleId = staffRoleId,
                HomeBranchId = defaultBranchId,
                Status = "Active",
                CreatedAt = DateTime.Now
            });
            _db.SaveChanges();
        }
    }

    // ============================================================
    // 5. ตารางการใช้ห้องและเทรนเนอร์ (Master Schedule Grid - Real DB)
    // ============================================================
    [HttpGet]
    public IActionResult Schedule(string? date, string? viewMode, int? branchId, string? email)
    {
        EnsureStaffSeeded();
        SyncExpiredCheckInsAll();
        Csi402dbContext.EnsureAllBranchesPodsAndTrainersSeeded(_db);

        var staff = GetCurrentStaff(email);
        SetStaffViewBag(staff);

        DateOnly selectedDate = DateOnly.TryParse(date, out var parsed)
            ? parsed
            : DateOnly.FromDateTime(DateTime.Today);

        string resolvedMode = string.Equals(viewMode, "trainer", StringComparison.OrdinalIgnoreCase)
            ? "trainer"
            : "pod";

        var allPodCounts = _db.Pods
            .GroupBy(p => p.BranchId)
            .Select(g => new { BranchId = g.Key, Count = g.Count() })
            .ToDictionary(x => x.BranchId, x => x.Count);

        var branches = _db.Branches
            .ToList()
            .OrderByDescending(b => allPodCounts.GetValueOrDefault(b.BranchId, 0))
            .ThenBy(b => b.BranchId)
            .ToList();

        int totalAllPods = allPodCounts.Values.Sum();
        int selectedBranchId = branchId ?? 0; // 0 = ทุกสาขา

        var podsQuery = _db.Pods.Include(p => p.Branch).AsQueryable();
        var trainersQuery = _db.Trainers.Include(t => t.User).ThenInclude(u => u.HomeBranch).AsQueryable();

        if (selectedBranchId > 0)
        {
            podsQuery = podsQuery.Where(p => p.BranchId == selectedBranchId);
            trainersQuery = trainersQuery.Where(t => t.User != null && t.User.HomeBranchId == selectedBranchId);
        }

        var pods = podsQuery.OrderBy(p => p.PodId).ToList();
        var trainers = trainersQuery.OrderBy(t => t.TrainerId).ToList();

        var bookingsQuery = _db.Bookings
            .Include(b => b.User)
            .Include(b => b.Pod)
            .Include(b => b.Trainer)
                .ThenInclude(t => t.User)
            .Include(b => b.CheckIns)
            .Where(b => b.BookingDate == selectedDate && b.Status != "Cancelled");

        if (selectedBranchId > 0)
        {
            bookingsQuery = bookingsQuery.Where(b => b.Pod != null && b.Pod.BranchId == selectedBranchId);
        }

        var bookings = bookingsQuery.OrderBy(b => b.StartTime).ToList();

        ViewBag.SelectedDate = selectedDate;
        ViewBag.ViewMode = resolvedMode;
        ViewBag.SelectedBranchId = selectedBranchId;
        ViewBag.Branches = branches;
        ViewBag.BranchPodCounts = allPodCounts;
        ViewBag.TotalAllPods = totalAllPods;
        ViewBag.Pods = pods;
        ViewBag.Trainers = trainers;

        return View(bookings);
    }

    private User? GetCurrentStaff(string? email)
    {
        if (!string.IsNullOrWhiteSpace(email))
        {
            string clean = email.Trim();
            var matched = _db.Users
                .Include(u => u.HomeBranch)
                .FirstOrDefault(u => u.Email == clean || u.Phone == clean);
            if (matched != null) return matched;
        }

        int staffRoleId = _db.Roles.FirstOrDefault(r => r.RoleName == "Staff")?.RoleId ?? 2;
        return _db.Users
            .Include(u => u.HomeBranch)
            .FirstOrDefault(u => u.RoleId == staffRoleId);
    }

    private void SyncExpiredCheckInsAll()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var nowTime = TimeOnly.FromDateTime(DateTime.Now);

        var expired = _db.Bookings
            .Include(b => b.CheckIns)
            .Where(b => b.Status == "Confirmed" &&
                        (b.BookingDate < today || (b.BookingDate == today && b.EndTime <= nowTime)))
            .ToList();

        bool hasChanges = false;
        foreach (var b in expired)
        {
            if (b.CheckIns != null && b.CheckIns.Any())
            {
                b.Status = "Completed";
                foreach (var ci in b.CheckIns.Where(c => c.CheckOutTime == null))
                {
                    ci.CheckOutTime = b.BookingDate.ToDateTime(b.EndTime);
                }
                hasChanges = true;
            }
        }

        if (hasChanges) _db.SaveChanges();
    }

    private void SetStaffViewBag(User? staff)
    {
        ViewBag.Email = staff?.Email ?? "staff@privategym.club";
        ViewBag.FullName = staff?.FullName ?? "พนักงานสาขา";
        ViewBag.BranchName = staff?.HomeBranch?.BranchName ?? "สาขาทองหล่อ";
        ViewData["FullName"] = ViewBag.FullName;
    }

    private static string GetMaintenanceLogPath()
    {
        string dir = Path.Combine(Directory.GetCurrentDirectory(), "App_Data");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "pod_maintenance_logs.json");
    }

    private static List<Dictionary<string, string>> ReadMaintenanceLogs()
    {
        try
        {
            string path = GetMaintenanceLogPath();
            if (!System.IO.File.Exists(path)) return new List<Dictionary<string, string>>();
            string json = System.IO.File.ReadAllText(path);
            return System.Text.Json.JsonSerializer.Deserialize<List<Dictionary<string, string>>>(json)
                   ?? new List<Dictionary<string, string>>();
        }
        catch
        {
            return new List<Dictionary<string, string>>();
        }
    }

    private static void AppendMaintenanceLog(string podName, string action, string slotInfo, string reason, string staffName)
    {
        try
        {
            var logs = ReadMaintenanceLogs();
            logs.Insert(0, new Dictionary<string, string>
            {
                ["Timestamp"] = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss"),
                ["PodName"] = podName,
                ["Action"] = action,
                ["SlotInfo"] = slotInfo,
                ["Reason"] = reason,
                ["StaffName"] = staffName
            });
            if (logs.Count > 100) logs = logs.Take(100).ToList();
            string json = System.Text.Json.JsonSerializer.Serialize(logs);
            System.IO.File.WriteAllText(GetMaintenanceLogPath(), json);
        }
        catch
        {
            // Ignore file I/O log failure so core DB operation still succeeds
        }
    }

    private static string GetHandoverLogPath()
    {
        string dir = Path.Combine(Directory.GetCurrentDirectory(), "App_Data");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "staff_handover_logs.json");
    }

    private static List<Dictionary<string, string>> ReadHandoverNotes()
    {
        try
        {
            string path = GetHandoverLogPath();
            if (!System.IO.File.Exists(path)) return new List<Dictionary<string, string>>();
            string json = System.IO.File.ReadAllText(path);
            return System.Text.Json.JsonSerializer.Deserialize<List<Dictionary<string, string>>>(json)
                   ?? new List<Dictionary<string, string>>();
        }
        catch
        {
            return new List<Dictionary<string, string>>();
        }
    }

    private static void AppendHandoverNote(string note, string staffName)
    {
        try
        {
            var notes = ReadHandoverNotes();
            notes.Insert(0, new Dictionary<string, string>
            {
                ["Timestamp"] = DateTime.Now.ToString("dd/MM HH:mm"),
                ["Note"] = note,
                ["StaffName"] = staffName
            });
            if (notes.Count > 50) notes = notes.Take(50).ToList();
            string json = System.Text.Json.JsonSerializer.Serialize(notes);
            System.IO.File.WriteAllText(GetHandoverLogPath(), json);
        }
        catch
        {
            // Ignore file I/O log failure so core operation still succeeds
        }
    }
}
