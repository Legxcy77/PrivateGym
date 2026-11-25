using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PrivateGym.Models;
using PrivateGym.ViewModels;

namespace PrivateGym.Controllers;

// [CustomerController] โซนหลัก:
// 1) หน้าแรก & โปรไฟล์: Index, EditProfile, Settings
// 2) จองห้อง Pod / เทรนเนอร์: BookSession, CheckSlots (API เช็กสล็อตว่าง)
// 3) จัดการคิวจองของฉัน: MyBookings, CheckInSession, CheckOutSession, RescheduleBooking, CancelBooking
// 4) แพ็กเกจ & ประวัติ: MyPackages, TopUpQuota, PayScheduledPlanNow, CancelScheduledPlan, ServiceHistory
public class CustomerController : Controller
{
    private readonly Csi402dbContext _db; // ตัวเชื่อมต่อฐานข้อมูล MySQL ผ่าน EF Core

    public CustomerController(Csi402dbContext db)
    {
        _db = db;
    }

    // หน้าแรกสมาชิก: แสดงโควต้าแพ็กเกจปัจจุบัน, คิวถัดไป และสถิติการใช้งาน
    public IActionResult Index(string? email, string? fullName, string? membershipPackage, string? homeBranch)
    {
        // 1. ค้นหาข้อมูล User จากฐานข้อมูลจริง
        User? user = null;
        if (!string.IsNullOrWhiteSpace(email))
        {
            user = _db.Users
                      .Include(u => u.HomeBranch)
                      .Include(u => u.MemberSubscriptions)
                          .ThenInclude(ms => ms.Package)
                      .FirstOrDefault(u => u.Email == email.Trim() || u.Phone == email.Trim());
        }

        // หากไม่ได้ส่ง email มา ให้ค้นหาจากผู้ใช้ล่าสุดในฐานข้อมูล
        if (user == null)
        {
            user = _db.Users
                      .Include(u => u.HomeBranch)
                      .Include(u => u.MemberSubscriptions)
                          .ThenInclude(ms => ms.Package)
                      .OrderByDescending(u => u.UserId)
                      .FirstOrDefault();
        }

        // 2. ดึงสิทธิ์แพ็กเกจปัจจุบันที่กำลังใช้งานอยู่ (StartDate <= วันนี้)
        var todayForSub = DateOnly.FromDateTime(DateTime.Today);
        var subscription = user?.MemberSubscriptions
                               .Where(s => s.Status == "Active" && s.StartDate <= todayForSub)
                               .OrderByDescending(s => s.SubscriptionId)
                               .FirstOrDefault()
                           ?? user?.MemberSubscriptions
                               .Where(s => s.Status == "Active")
                               .OrderByDescending(s => s.SubscriptionId)
                               .FirstOrDefault();

        var nextCycleSub = user?.MemberSubscriptions
                               .Where(s => s.Status == "Active" && s.StartDate > todayForSub)
                               .OrderByDescending(s => s.SubscriptionId)
                               .FirstOrDefault();
        ViewBag.NextCyclePackageName = nextCycleSub?.Package?.PackageName;
        ViewBag.NextCycleStartDate = nextCycleSub?.StartDate.ToString("dd/MM/yyyy");

        string resolvedName = user?.FullName ?? (string.IsNullOrWhiteSpace(fullName) ? "คุณสมาชิก PrivateGym" : fullName);
        string resolvedEmail = user?.Email ?? (string.IsNullOrWhiteSpace(email) ? "member@privategym.club" : email);
        string selectedPackage = subscription?.Package?.PackageName ?? (!string.IsNullOrWhiteSpace(membershipPackage) ? membershipPackage : "Starter Experience");
        string selectedBranch = user?.HomeBranch?.BranchName ?? (!string.IsNullOrWhiteSpace(homeBranch) ? homeBranch : "สาขาทองหล่อ");

        bool isVip = subscription?.Package?.IsVip ?? (selectedPackage.Contains("Elite") || selectedPackage.Contains("VIP"));

        // ดึงโควต้าคงเหลือจริงจากตาราง MemberSubscriptions
        string podQuotaText = isVip ? "ไม่จำกัด" : (subscription != null ? subscription.RemainingPods.ToString() : "4");
        string ptQuotaText = isVip ? "ไม่จำกัด" : (subscription != null ? subscription.RemainingPt.ToString() : "0");
        string inbodyQuotaText = isVip ? "ไม่จำกัด" : (subscription != null ? subscription.RemainingInBody.ToString() : "1");

        string quotaBadgeText = isVip
            ? "Pod: ไม่จำกัด • PT: ไม่จำกัด"
            : $"Pod: {podQuotaText} ครั้ง • PT: {ptQuotaText} ครั้ง";

        // คำนวณวันหมดอายุคงเหลือ
        int daysRemaining = 30;
        if (subscription != null)
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            daysRemaining = subscription.EndDate.DayNumber - today.DayNumber;
            if (daysRemaining < 0) daysRemaining = 0;
        }

        string cycleDateText = subscription != null 
            ? $"{subscription.StartDate:dd/MM/yyyy} – {subscription.EndDate:dd/MM/yyyy}"
            : $"{DateTime.Today:dd/MM/yyyy} – {DateTime.Today.AddDays(30):dd/MM/yyyy}";

        // 3. ดึงรายการจองรอบถัดไปของ User คนนี้จากตาราง Bookings (เฉพาะรอบที่ยังไม่หมดเวลาจริง)
        Booking? nextBooking = null;
        int usedPodsCount = 0;
        int usedPtCount = 0;
        if (user != null)
        {
            SyncExpiredCheckIns(user.UserId);

            var today = DateOnly.FromDateTime(DateTime.Today);
            var nowTime = TimeOnly.FromDateTime(DateTime.Now);
            nextBooking = _db.Bookings
                .Include(b => b.Pod)
                    .ThenInclude(p => p.Branch)
                .Include(b => b.Trainer)
                    .ThenInclude(t => t!.User)
                .Include(b => b.CheckIns)
                .Where(b => b.UserId == user.UserId &&
                            b.Status == "Confirmed" &&
                            (b.BookingDate > today || (b.BookingDate == today && b.EndTime > nowTime)))
                .OrderBy(b => b.BookingDate)
                .ThenBy(b => b.StartTime)
                .FirstOrDefault();

            var activeBookings = _db.Bookings
                .Where(b => b.UserId == user.UserId && b.Status != "Cancelled")
                .ToList();
            usedPodsCount = activeBookings.Count;
            usedPtCount = activeBookings.Count(b => b.TrainerId.HasValue);
        }

        int totalPodQuota = subscription?.Package?.PodQuota ?? 8;
        int totalPtQuota = subscription?.Package?.PtQuota ?? 4;
        ViewBag.UsedPodsCount = usedPodsCount;
        ViewBag.UsedPtCount = usedPtCount;
        ViewBag.TotalPodQuota = totalPodQuota;
        ViewBag.TotalPtQuota = totalPtQuota;

        var model = new CustomerDashboardViewModel
        {
            UserId = user?.UserId ?? 1,
            FullName = resolvedName,
            Email = resolvedEmail,
            Phone = user?.Phone ?? "",
            MembershipPackage = selectedPackage,
            HomeBranch = selectedBranch,
            IsVip = isVip,
            PodQuotaText = podQuotaText,
            PtQuotaText = ptQuotaText,
            InBodyQuotaText = inbodyQuotaText,
            QuotaBadgeText = quotaBadgeText,
            Gender = user?.Gender,
            Height = user?.Height,
            Weight = user?.Weight,
            ExperienceLevel = user?.ExperienceLevel,
            FitnessGoal = user?.FitnessGoal,
            DaysRemaining = daysRemaining,
            CycleDateText = cycleDateText,
            NextBooking = nextBooking
        };

        // ส่งข้อมูลไปยัง ViewBag/ViewData ให้ Layout และ View ใช้งาน
        ViewData["FullName"] = model.FullName;
        ViewBag.FullName = model.FullName;
        ViewBag.Email = model.Email;
        ViewBag.Package = model.MembershipPackage;
        ViewBag.Branch = model.HomeBranch;

        ViewBag.IsVip = isVip;
        ViewBag.PodQuotaText = podQuotaText;
        ViewBag.PtQuotaText = ptQuotaText;
        ViewBag.InBodyQuotaText = inbodyQuotaText;
        ViewBag.QuotaBadge = quotaBadgeText;
        ViewBag.DaysRemaining = daysRemaining;

        return View(model);
    }

    // ================================================================
    // EDIT PROFILE (ระบบแก้ไขข้อมูลส่วนตัวของ Customer ตามแบบฉบับ Update DB)
    // ================================================================

    // GET: /Customer/EditProfile
    public IActionResult EditProfile(string? email, int? id)
    {
        User? user = null;

        // 1. ค้นหา User ตาม id หรือ email ที่ส่งมา
        if (id.HasValue && id.Value > 0)
        {
            user = (from u in _db.Users
                    where u.UserId == id.Value
                    select u).Include(u => u.HomeBranch).FirstOrDefault();
        }
        else if (!string.IsNullOrWhiteSpace(email))
        {
            string cleanEmail = email.Trim();
            user = (from u in _db.Users
                    where u.Email == cleanEmail || u.Phone == cleanEmail
                    select u).Include(u => u.HomeBranch).FirstOrDefault();
        }

        // หากไม่มีพารามิเตอร์ ให้ดึงบัญชีสมาชิกล่าสุดในฐานข้อมูล
        if (user == null)
        {
            user = _db.Users
                      .Include(u => u.HomeBranch)
                      .OrderByDescending(u => u.UserId)
                      .FirstOrDefault(u => u.RoleId == 4) 
                   ?? _db.Users.Include(u => u.HomeBranch).OrderByDescending(u => u.UserId).FirstOrDefault();
        }

        if (user == null)
        {
            return RedirectToAction("Index", "Login");
        }

        // ดึงรายการสาขาทั้งหมดเพื่อให้เลือกใน Dropdown
        ViewBag.Branches = _db.Branches.ToList();

        // ส่งข้อมูลให้ Layout ใช้แสดงผล
        ViewBag.FullName = user.FullName;
        ViewBag.Email = user.Email;
        ViewData["FullName"] = user.FullName;

        return View(user);
    }

    // POST: /Customer/EditProfile
    [HttpPost]
    public IActionResult EditProfile(User data, string? newPassword)
    {
        // 1. ค้นหา Record เดิมจากฐานข้อมูลด้วย UserId
        var user = (from u in _db.Users
                    where u.UserId == data.UserId
                    select u).FirstOrDefault();

        if (user != null)
        {
            // 2. อัปเดตข้อมูลตามที่ผู้ใช้แก้ไข
            user.FullName = data.FullName;
            user.Phone = data.Phone;
            
            // หากมีการกรอกรหัสผ่านใหม่เข้ามา ให้อัปเดตรหัสผ่าน
            if (!string.IsNullOrWhiteSpace(newPassword))
            {
                user.PasswordHash = newPassword;
            }

            user.Gender = data.Gender;
            user.Height = data.Height;
            user.Weight = data.Weight;
            user.ExperienceLevel = data.ExperienceLevel;
            user.FitnessGoal = data.FitnessGoal;
            user.HomeBranchId = data.HomeBranchId;
            user.BillingAddress = data.BillingAddress;
            user.TaxId = data.TaxId;

            // 3. บันทึกการเปลี่ยนแปลงลงฐานข้อมูล MySQL
            _db.SaveChanges();

            ViewBag.Success = "บันทึกและอัปเดตข้อมูลโปรไฟล์ส่วนตัวเรียบร้อยแล้ว";
        }
        else
        {
            ViewBag.Error = "ไม่พบข้อมูลสมาชิกในระบบ";
        }

        ViewBag.Branches = _db.Branches.ToList();
        var reloadedUser = _db.Users.Include(u => u.HomeBranch).FirstOrDefault(u => u.UserId == data.UserId) ?? user ?? data;
        ViewBag.FullName = reloadedUser.FullName;
        ViewBag.Email = reloadedUser.Email;
        ViewData["FullName"] = reloadedUser.FullName;

        return View(reloadedUser);
    }

    // ================================================================
    // BOOK SESSION (ระบบจองห้อง Pod & จองเทรนเนอร์)
    // ================================================================

    // GET: /Customer/BookSession
    public IActionResult BookSession(string? email)
    {
        // 1. หา User ปัจจุบัน
        var user = GetCurrentUser(email);
        if (user == null) return RedirectToAction("Index", "Login");

        // 2. ดึงสิทธิ์แพ็กเกจปัจจุบันที่กำลังใช้งานอยู่ (StartDate <= วันนี้)
        var todayForSub = DateOnly.FromDateTime(DateTime.Today);
        var subscription = user.MemberSubscriptions
            .Where(s => s.Status == "Active" && s.StartDate <= todayForSub)
            .OrderByDescending(s => s.SubscriptionId)
            .FirstOrDefault()
            ?? user.MemberSubscriptions
            .Where(s => s.Status == "Active")
            .OrderByDescending(s => s.SubscriptionId)
            .FirstOrDefault();

        bool isVip = subscription?.Package?.IsVip ?? false;
        int remainingPods = isVip ? 999 : (subscription?.RemainingPods ?? 0);
        int remainingPt = isVip ? 999 : (subscription?.RemainingPt ?? 0);

        // 3. เตรียมสาขา และตรวจสอบข้อมูล Pods & Trainers ครบทั้ง 3 สาขา (12 Pods / 12 Trainers)
        var branches = _db.Branches.OrderBy(b => b.BranchId).ToList();
        int defaultBranchId = user.HomeBranchId ?? branches.FirstOrDefault()?.BranchId ?? 1;

        Csi402dbContext.EnsureAllBranchesPodsAndTrainersSeeded(_db);

        var pods = _db.Pods.Include(p => p.Branch).OrderBy(p => p.PodId).ToList();
        var trainers = _db.Trainers.Include(t => t.User).ThenInclude(u => u.HomeBranch).OrderBy(t => t.TrainerId).ToList();

        var defaultPodForBranch = pods.FirstOrDefault(p => p.BranchId == defaultBranchId) ?? pods.FirstOrDefault();

        var viewModel = new BookSessionViewModel
        {
            UserId = user.UserId,
            FullName = user.FullName,
            Email = user.Email,
            RemainingPods = remainingPods,
            RemainingPt = remainingPt,
            IsVip = isVip,
            Branches = branches,
            Pods = pods,
            Trainers = trainers,
            SelectedBranchId = defaultBranchId,
            SelectedPodId = defaultPodForBranch?.PodId ?? 1,
            BookingDate = DateOnly.FromDateTime(DateTime.Today)
        };

        SetCommonViewBag(user, subscription);
        return View(viewModel);
    }

    // POST: /Customer/BookSession
    // บันทึกการจองห้อง Pod / เทรนเนอร์ (ตรวจสถานะบัญชี, วันหมดอายุ, โควต้า และเวลาชน)
    [HttpPost]
    public IActionResult BookSession(BookSessionViewModel form)
    {
        bool isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest" || 
                      Request.Headers["Accept"].ToString().Contains("application/json");

        try
        {
            // 1. ตรวจสอบ User และ Subscription
            User? user = null;
            if (form.UserId > 0)
            {
                user = _db.Users
                    .Include(u => u.MemberSubscriptions)
                        .ThenInclude(ms => ms.Package)
                    .FirstOrDefault(u => u.UserId == form.UserId);
            }

            if (user == null && !string.IsNullOrWhiteSpace(form.Email))
            {
                user = GetCurrentUser(form.Email);
            }

            if (user == null)
            {
                user = GetCurrentUser(null);
            }

            if (user == null)
            {
                if (isAjax) return Json(new { success = false, message = "ไม่พบข้อมูลบัญชีผู้ใช้งาน กรุณาเข้าสู่ระบบใหม่" });
                return RedirectToAction("Index", "Login");
            }

            if (user.Status == "Inactive" || user.Status == "Suspended" || user.Status == "โดนลบแล้ว")
            {
                string suspendedMsg = "บัญชีของคุณถูกระงับการใช้งานชั่วคราว ไม่สามารถจองห้องซ้อมได้ กรุณาติดต่อเจ้าหน้าที่";
                if (isAjax) return Json(new { success = false, message = suspendedMsg });
                TempData["Error"] = suspendedMsg;
                return RedirectToAction("BookSession", new { email = user.Email });
            }

            var todayForSub = DateOnly.FromDateTime(DateTime.Today);
            var subscription = user.MemberSubscriptions
                .Where(s => s.Status == "Active" && s.StartDate <= todayForSub)
                .OrderByDescending(s => s.SubscriptionId)
                .FirstOrDefault()
                ?? user.MemberSubscriptions
                .Where(s => s.Status == "Active")
                .OrderByDescending(s => s.SubscriptionId)
                .FirstOrDefault();

            if (subscription == null || subscription.EndDate < todayForSub)
            {
                string expMsg = "แพ็กเกจของคุณหมดอายุแล้ว กรุณาต่ออายุแพ็กเกจก่อนทำการจอง";
                if (isAjax) return Json(new { success = false, message = expMsg });
                TempData["Error"] = expMsg;
                return RedirectToAction("BookSession", new { email = user.Email });
            }

            bool isVip = subscription?.Package?.IsVip ?? false;

            // 2. ตรวจสอบโควต้า Pod และ PT (กรณีเลือกใช้บริการพร้อมเทรนเนอร์)
            if (!isVip && (subscription == null || subscription.RemainingPods <= 0))
            {
                string msg = "โควต้าการใช้งาน Pod ของคุณหมดแล้ว กรุณาติดต่อเติมแพ็กเกจ";
                if (isAjax) return Json(new { success = false, message = msg });
                TempData["Error"] = msg;
                return RedirectToAction("BookSession", new { email = user.Email });
            }

            bool isTrainerService = form.ServiceType == "pod_trainer" || (form.SelectedTrainerId.HasValue && form.SelectedTrainerId.Value > 0 && form.ServiceType != "pod_only");
            if (isTrainerService && !isVip && (subscription == null || subscription.RemainingPt <= 0))
            {
                string msg = "โควต้าเทรนเนอร์ส่วนตัว (PT) ของคุณหมดแล้ว กรุณาเลือกใช้บริการ Pod เดี่ยว หรือติดต่อเติมแพ็กเกจ";
                if (isAjax) return Json(new { success = false, message = msg });
                TempData["Error"] = msg;
                return RedirectToAction("BookSession", new { email = user.Email });
            }

            // 3. แปลง TimeSlot เช่น "18:00 - 19:15" หรือ "18:00 - 19:15 น."
            TimeOnly startTime = new TimeOnly(18, 0);
            TimeOnly endTime = new TimeOnly(19, 15);
            if (!string.IsNullOrEmpty(form.SelectedTimeSlot))
            {
                var cleanSlot = form.SelectedTimeSlot.Replace("น.", "").Trim();
                if (cleanSlot.Contains("-"))
                {
                    var parts = cleanSlot.Split('-');
                    if (parts.Length == 2)
                    {
                        TimeOnly.TryParse(parts[0].Trim(), System.Globalization.CultureInfo.InvariantCulture, out startTime);
                        TimeOnly.TryParse(parts[1].Trim(), System.Globalization.CultureInfo.InvariantCulture, out endTime);
                    }
                }
            }

            // 3.1 แปลงวันที่จาก Request.Form โดยตรงด้วย InvariantCulture ไม่พึ่งพา Calendar เครื่อง
            DateOnly bookingDate;
            var rawDateStr = Request.Form["BookingDate"].ToString().Trim();
            if (!string.IsNullOrEmpty(rawDateStr) && DateOnly.TryParseExact(rawDateStr, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var exactDate))
            {
                bookingDate = exactDate;
            }
            else
            {
                bookingDate = form.BookingDate;
            }

            if (bookingDate.Year > 2400)
                bookingDate = new DateOnly(bookingDate.Year - 543, bookingDate.Month, bookingDate.Day);
            else if (bookingDate.Year < 1900)
                bookingDate = new DateOnly(bookingDate.Year + 543, bookingDate.Month, bookingDate.Day);

            var today = DateOnly.FromDateTime(DateTime.Today);
            if (today.Year > 2400)
                today = new DateOnly(today.Year - 543, today.Month, today.Day);

            var nowTime = TimeOnly.FromDateTime(DateTime.Now);
            if (bookingDate < today)
            {
                string msg = $"ไม่สามารถเลือกจองวันในอดีตได้ ({bookingDate:yyyy-MM-dd})";
                if (isAjax) return Json(new { success = false, message = msg });
                TempData["Error"] = msg;
                return RedirectToAction("BookSession", new { email = user.Email });
            }
            if (bookingDate == today && startTime <= nowTime)
            {
                string msg = $"รอบเวลา {startTime:HH:mm} น. ของวันนี้ได้ผ่านไปแล้ว กรุณาเลือกรอบเวลาถัดไป";
                if (isAjax) return Json(new { success = false, message = msg });
                TempData["Error"] = msg;
                return RedirectToAction("BookSession", new { email = user.Email });
            }

            // 3.2 ตรวจสอบและจับคู่ PodId ที่มีอยู่จริงในตาราง Pods (ป้องกัน Foreign Key Fail 100%)
            var allPods = _db.Pods.OrderBy(p => p.PodId).ToList();
            int requestedPodNumber = form.SelectedPodId > 0 ? form.SelectedPodId : 1;
            Pod? targetPod = allPods.FirstOrDefault(p => p.PodId == requestedPodNumber);
            if (targetPod == null && requestedPodNumber >= 1 && requestedPodNumber <= allPods.Count)
            {
                targetPod = allPods[requestedPodNumber - 1];
            }
            targetPod ??= allPods.FirstOrDefault();

            if (targetPod == null)
            {
                // กรณีตาราง Pods ว่างเปล่า ให้สร้างอัตโนมัติ
                var firstBranch = _db.Branches.FirstOrDefault();
                targetPod = new Pod
                {
                    BranchId = firstBranch?.BranchId ?? 1,
                    PodName = $"Pod 0{requestedPodNumber}",
                    PodType = "Private Pod",
                    Status = "Available"
                };
                _db.Pods.Add(targetPod);
                _db.SaveChanges();
            }

            int validPodId = targetPod.PodId;

            if (targetPod.Status == "Maintenance")
            {
                string msg = $"ขออภัย ห้อง {targetPod.PodName} อยู่ระหว่างปิดปรับปรุงชั่วคราว กรุณาเลือกห้องซ้อมอื่น";
                if (isAjax) return Json(new { success = false, message = msg });
                TempData["Error"] = msg;
                return RedirectToAction("BookSession", new { email = user.Email });
            }

            // 3.3 ตรวจสอบ TrainerId ที่มีอยู่จริงในตาราง Trainers (ป้องกัน Foreign Key Fail ของ TrainerId)
            int? validTrainerId = null;
            string? bookedTrainerName = null;
            if (isTrainerService)
            {
                EnsureTrainersSeeded(targetPod.BranchId);
                Trainer? targetTrainer = null;
                if (form.SelectedTrainerId.HasValue && form.SelectedTrainerId.Value > 0)
                {
                    targetTrainer = _db.Trainers.Include(t => t.User).FirstOrDefault(t => t.TrainerId == form.SelectedTrainerId.Value);
                }
                targetTrainer ??= _db.Trainers.Include(t => t.User).OrderBy(t => t.TrainerId).FirstOrDefault();
                validTrainerId = targetTrainer?.TrainerId;
                bookedTrainerName = targetTrainer != null
                    ? $"{targetTrainer.User?.FullName ?? "Coach"} ({targetTrainer.Specialty})"
                    : null;
            }

            // 3.4 ตรวจสอบการกดส่งซ้ำ (Idempotency / Double-Submit) และตรวจสอบห้องว่าง / คิวเทรนเนอร์ว่าง
            var existingSameUserBooking = _db.Bookings.FirstOrDefault(b =>
                b.UserId == user.UserId &&
                b.PodId == validPodId &&
                b.BookingDate == bookingDate &&
                b.Status == "Confirmed" &&
                b.StartTime == startTime);

            if (existingSameUserBooking != null)
            {
                // หากลูกค้าคนเดิมเพิ่งกดยืนยันรอบเดียวกันซ้ำ (เช่น เน็ตช้าแล้วคลิกซ้ำ) ไม่ตัดโควต้าซ้ำและส่งผลสำเร็จเดิมกลับไป
                if (isAjax)
                {
                    return Json(new
                    {
                        success = true,
                        bookingId = existingSameUserBooking.BookingId,
                        email = user.Email,
                        podName = targetPod.PodName,
                        trainerName = bookedTrainerName ?? "ฝึกซ้อมส่วนตัว (Solo Session)",
                        bookingDate = bookingDate.ToString("dd/MM/yyyy"),
                        timeSlot = $"{startTime:HH:mm} - {endTime:HH:mm} น.",
                        remainingPods = isVip ? -1 : (subscription?.RemainingPods ?? 0),
                        remainingPt = isVip ? -1 : (subscription?.RemainingPt ?? 0),
                        message = "จองห้องซ้อมสำเร็จเรียบร้อยแล้ว!"
                    });
                }
                return RedirectToAction("MyBookings", new { email = user.Email });
            }

            using var transaction = _db.Database.BeginTransaction(System.Data.IsolationLevel.Serializable);

            bool isAlreadyBooked = _db.Bookings.Any(b =>
                b.PodId == validPodId &&
                b.BookingDate == bookingDate &&
                b.Status != "Cancelled" &&
                b.StartTime == startTime);

            if (isAlreadyBooked)
            {
                transaction.Rollback();
                string msg = "ขออภัย ห้อง Pod นี้มีผู้จองในรอบเวลาดังกล่าวแล้ว กรุณาเลือกรอบเวลาหรือห้องอื่น";
                if (isAjax) return Json(new { success = false, message = msg });
                TempData["Error"] = msg;
                return RedirectToAction("BookSession", new { email = user.Email });
            }

            if (validTrainerId.HasValue)
            {
                bool isTrainerBusy = _db.Bookings.Any(b =>
                    b.TrainerId == validTrainerId.Value &&
                    b.BookingDate == bookingDate &&
                    b.Status != "Cancelled" &&
                    b.StartTime == startTime);

                if (isTrainerBusy)
                {
                    transaction.Rollback();
                    string msg = "ขออภัย เทรนเนอร์ท่านนี้ติดคลาสสอนในรอบเวลาดังกล่าวแล้ว กรุณาเลือกรอบเวลาอื่นหรือเลือกโค้ชท่านอื่น";
                    if (isAjax) return Json(new { success = false, message = msg });
                    TempData["Error"] = msg;
                    return RedirectToAction("BookSession", new { email = user.Email });
                }

                var trainerUnavailable = TrainerController.GetUnavailableSlotsForTrainer(validTrainerId.Value, bookingDate);
                if (trainerUnavailable.Contains(startTime.ToString("HH:mm")))
                {
                    transaction.Rollback();
                    string msg = "ขออภัย เทรนเนอร์ท่านนี้ปิดรับสอนหรืองดให้บริการในวัน/รอบเวลาดังกล่าว กรุณาเลือกรอบเวลาอื่นหรือเลือกโค้ชท่านอื่น";
                    if (isAjax) return Json(new { success = false, message = msg });
                    TempData["Error"] = msg;
                    return RedirectToAction("BookSession", new { email = user.Email });
                }
            }

            // 4. บันทึกการจองใหม่ลงตาราง Bookings
            var newBooking = new Booking
            {
                UserId = user.UserId,
                PodId = validPodId,
                TrainerId = validTrainerId,
                BookingDate = bookingDate,
                StartTime = startTime,
                EndTime = endTime,
                Status = "Confirmed",
                CreatedAt = DateTime.Now
            };

            _db.Bookings.Add(newBooking);

            // 5. ตัดโควต้าคงเหลือ (ถ้าไม่ใช่ VIP)
            if (!isVip && subscription != null)
            {
                subscription.RemainingPods = Math.Max(0, subscription.RemainingPods - 1);
                if (validTrainerId.HasValue && subscription.RemainingPt > 0)
                {
                    subscription.RemainingPt = Math.Max(0, subscription.RemainingPt - 1);
                }
            }

            _db.SaveChanges();
            transaction.Commit();

            if (isAjax)
            {
                return Json(new
                {
                    success = true,
                    bookingId = newBooking.BookingId,
                    email = user.Email,
                    podName = targetPod.PodName,
                    trainerName = bookedTrainerName ?? "ฝึกซ้อมส่วนตัว (Solo Session)",
                    bookingDate = bookingDate.ToString("dd/MM/yyyy"),
                    timeSlot = $"{startTime:HH:mm} - {endTime:HH:mm} น.",
                    remainingPods = isVip ? -1 : (subscription?.RemainingPods ?? 0),
                    remainingPt = isVip ? -1 : (subscription?.RemainingPt ?? 0),
                    message = "จองห้องซ้อมสำเร็จเรียบร้อยแล้ว!"
                });
            }

            TempData["Success"] = "จองห้องซ้อมสำเร็จเรียบร้อยแล้ว!";
            return RedirectToAction("MyBookings", new { email = user.Email });
        }
        catch (Exception ex)
        {
            var innerMsg = ex.InnerException?.Message ?? ex.Message;
            if (isAjax)
            {
                return Json(new { success = false, message = "เกิดข้อผิดพลาดในการบันทึกข้อมูล: " + innerMsg });
            }
            TempData["Error"] = "เกิดข้อผิดพลาดในการบันทึกข้อมูล: " + innerMsg;
            return RedirectToAction("BookSession", new { email = form.Email });
        }
    }

    // API เช็คสถานะความว่างของรอบเวลา (Real-time Slot Availability: เช็คทั้งห้อง Pod และคิวโค้ช Trainer)
    [HttpGet]
    // API ตรวจสอบสล็อตเวลาว่างของห้อง Pod และเทรนเนอร์ในวันที่เลือก (JSON)
    public IActionResult CheckSlots(int podId, string? date, int? trainerId)
    {
        DateOnly bookingDate;
        if (!string.IsNullOrEmpty(date) && DateOnly.TryParseExact(date, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var parsedDate))
        {
            if (parsedDate.Year > 2400)
                bookingDate = new DateOnly(parsedDate.Year - 543, parsedDate.Month, parsedDate.Day);
            else
                bookingDate = parsedDate;
        }
        else if (!string.IsNullOrEmpty(date) && DateOnly.TryParse(date, out var fallbackDate))
        {
            if (fallbackDate.Year > 2400)
                bookingDate = new DateOnly(fallbackDate.Year - 543, fallbackDate.Month, fallbackDate.Day);
            else
                bookingDate = fallbackDate;
        }
        else
        {
            bookingDate = DateOnly.FromDateTime(DateTime.Today);
        }

        // จับคู่ podId ให้ตรงกับตาราง Pods ในฐานข้อมูลเหมือนตอน POST
        var allPods = _db.Pods.OrderBy(p => p.PodId).ToList();
        int requestedPod = podId > 0 ? podId : 1;
        Pod? targetPod = allPods.FirstOrDefault(p => p.PodId == requestedPod);
        if (targetPod == null && requestedPod >= 1 && requestedPod <= allPods.Count)
        {
            targetPod = allPods[requestedPod - 1];
        }
        int resolvedPodId = targetPod?.PodId ?? podId;

        // ดึงรายการจองที่มีอยู่แล้วของ Pod นั้นในวันนั้น (รวมถึงรอบที่ Staff ล็อกปิดปรับปรุงเฉพาะรอบ และรอบที่เสร็จสิ้นแล้ว)
        var podBookingsForDate = _db.Bookings
            .Include(b => b.User)
            .Where(b => b.PodId == resolvedPodId && b.BookingDate == bookingDate && b.Status != "Cancelled")
            .ToList();
        var bookedStarts = podBookingsForDate.Select(b => b.StartTime.ToString("HH:mm")).ToList();
        var maintSlotStarts = podBookingsForDate
            .Where(b => b.User != null && b.User.RoleId != 4)
            .Select(b => b.StartTime.ToString("HH:mm"))
            .ToHashSet();

        // หากเลือกบริการพร้อมเทรนเนอร์ ให้เช็คทั้งคิวสอนใน DB และเวลาว่าง/วันหยุดของเทรนเนอร์จาก Availability
        var trainerBusyStarts = new List<string>();
        if (trainerId.HasValue && trainerId.Value > 0)
        {
            trainerBusyStarts = _db.Bookings
                .Where(b => b.TrainerId == trainerId.Value && b.BookingDate == bookingDate && b.Status != "Cancelled")
                .Select(b => b.StartTime.ToString("HH:mm"))
                .ToList();

            var unavailableBySchedule = TrainerController.GetUnavailableSlotsForTrainer(trainerId.Value, bookingDate);
            foreach (var slotStart in unavailableBySchedule)
            {
                if (!trainerBusyStarts.Contains(slotStart))
                {
                    trainerBusyStarts.Add(slotStart);
                }
            }
        }

        var today = DateOnly.FromDateTime(DateTime.Today);
        var nowTime = TimeOnly.FromDateTime(DateTime.Now);

        // รายการรอบเวลาทั้งหมด 6 รอบ
        var allSlots = new[]
        {
            new { Start = "08:00", End = "09:15", Text = "08:00 - 09:15 น." },
            new { Start = "09:30", End = "10:45", Text = "09:30 - 10:45 น." },
            new { Start = "11:00", End = "12:15", Text = "11:00 - 12:15 น." },
            new { Start = "14:00", End = "15:15", Text = "14:00 - 15:15 น." },
            new { Start = "18:00", End = "19:15", Text = "18:00 - 19:15 น." },
            new { Start = "19:30", End = "20:45", Text = "19:30 - 20:45 น." }
        };

        bool isPodMaintenance = targetPod != null && targetPod.Status == "Maintenance";

        var result = allSlots.Select(s =>
        {
            TimeOnly.TryParse(s.Start, out var slotStartTime);
            bool isPast = (bookingDate < today) || (bookingDate == today && slotStartTime <= nowTime);
            bool isPodBooked = bookedStarts.Contains(s.Start);
            bool isTrainerBusy = trainerBusyStarts.Contains(s.Start);

            string status = "available";
            string reason = "ว่าง (พร้อมจอง)";

            if (isPodMaintenance)
            {
                status = "booked";
                reason = "ปิดปรับปรุง (Maintenance)";
            }
            else if (isPast)
            {
                status = "past";
                reason = "สิ้นสุดรอบเวลา";
            }
            else if (maintSlotStarts.Contains(s.Start))
            {
                status = "booked";
                reason = "ปิดปรับปรุงเฉพาะรอบ";
            }
            else if (isPodBooked)
            {
                status = "booked";
                reason = "เต็ม (มีผู้จองแล้ว)";
            }
            else if (isTrainerBusy)
            {
                status = "booked";
                reason = "โค้ชติดคลาสสอน";
            }
            else if (s.Start == "18:00")
            {
                status = "prime";
                reason = "ว่าง (Prime Time)";
            }

            return new
            {
                text = s.Text,
                start = s.Start,
                isAvailable = !isPodMaintenance && !isPast && !isPodBooked && !isTrainerBusy,
                status = status,
                reason = reason
            };
        });

        return Json(result);
    }

    // ================================================================
    // MY BOOKINGS & REAL-TIME CHECK-IN ENGINE (รายการจอง, เช็กอินเข้าห้อง, ตัดรอบอัตโนมัติ)
    // ================================================================
    // โครงสร้างการทำงาน 4 สถานะจริง:
    // 1. จองสำเร็จ (รอเช็กอิน): Status == "Confirmed", ยังไม่เลยเวลา EndTime และยังไม่มีประวัติใน CheckIns
    // 2. เช็กอินแล้ว (กำลังซ้อมในห้อง): Status == "Confirmed", ยังไม่เลยเวลา EndTime และมีข้อมูลในตาราง CheckIns แล้ว
    // 3. เข้าฝึกซ้อมสำเร็จ (Completed): Status == "Completed" (กดเช็กเอาต์เอง หรือเคยเช็กอินแล้วเวลาผ่านเลย EndTime)
    // 4. ไม่ได้เข้าใช้งาน (No-Show / หมดเวลา): Status == "Confirmed", เลยเวลา EndTime ไปแล้ว แต่ไม่มีข้อมูลใน CheckIns
    public IActionResult MyBookings(string? email, string? tab = "upcoming")
    {
        var user = GetCurrentUser(email);
        if (user == null) return RedirectToAction("Index", "Login");

        // ซิงค์สถานะรายการที่เคยเช็กอินแล้วและหมดเวลา ให้เปลี่ยนเป็น Completed อัตโนมัติ
        SyncExpiredCheckIns(user.UserId);

        var bookings = _db.Bookings
            .Include(b => b.Pod)
                .ThenInclude(p => p.Branch)
            .Include(b => b.Trainer)
                .ThenInclude(t => t!.User)
            .Include(b => b.CheckIns)
            .Where(b => b.UserId == user.UserId)
            .OrderByDescending(b => b.BookingDate)
            .ThenByDescending(b => b.StartTime)
            .ToList();

        var todayForSub = DateOnly.FromDateTime(DateTime.Today);
        var subscription = user.MemberSubscriptions
            .Where(s => s.Status == "Active" && s.StartDate <= todayForSub)
            .OrderByDescending(s => s.SubscriptionId)
            .FirstOrDefault()
            ?? user.MemberSubscriptions
            .Where(s => s.Status == "Active")
            .OrderByDescending(s => s.SubscriptionId)
            .FirstOrDefault();

        ViewBag.ActiveTab = tab ?? "upcoming";
        SetCommonViewBag(user, subscription);
        return View(bookings);
    }

    // POST: /Customer/CheckInSession (กดปลดล็อกประตู / สแกนเช็กอินเข้าใช้งานห้องซ้อมจริง บันทึกลงตาราง CheckIns)
    [HttpPost]
    public IActionResult CheckInSession(int bookingId, string? email)
    {
        var user = GetCurrentUser(email);
        if (user == null) return RedirectToAction("Index", "Login");

        var booking = _db.Bookings
            .Include(b => b.Pod)
            .Include(b => b.CheckIns)
            .FirstOrDefault(b => b.BookingId == bookingId && b.UserId == user.UserId);

        if (booking == null || booking.Status != "Confirmed")
        {
            TempData["Error"] = "ไม่พบรายการจองที่สามารถเช็กอินเข้าห้องได้ครับ";
            return RedirectToAction("MyBookings", new { email = user.Email });
        }

        var today = DateOnly.FromDateTime(DateTime.Today);
        var nowTime = TimeOnly.FromDateTime(DateTime.Now);
        bool isExpired = booking.BookingDate < today || (booking.BookingDate == today && booking.EndTime <= nowTime);

        if (isExpired)
        {
            TempData["Error"] = "รายการจองนี้เลยเวลาสิ้นสุดเซสชันไปแล้ว ระบบได้ย้ายไปยังหน้าประวัติการใช้งาน (ไม่ได้เข้าใช้งาน) เรียบร้อยแล้วครับ";
            return RedirectToAction("MyBookings", new { email = user.Email, tab = "history" });
        }

        if (!booking.CheckIns.Any())
        {
            // บันทึกเวลาเช็กอินจริงลงตาราง CheckIns (ใช้ค่า ENUM ที่ถูกต้อง: 'QR_Code')
            _db.CheckIns.Add(new CheckIn
            {
                BookingId = booking.BookingId,
                UserId = user.UserId,
                CheckInTime = DateTime.Now,
                AccessMethod = "QR_Code"
            });
            _db.SaveChanges();
        }

        TempData["Success"] = $"ปลดล็อกประตูและเช็กอินเข้าห้อง {booking.Pod?.PodName ?? "Private Pod"} (#PG-{booking.BookingId:D5}) สำเร็จ! เริ่มเวลาการฝึกซ้อมแล้วครับ";
        return RedirectToAction("MyBookings", new { email = user.Email, tab = "upcoming" });
    }

    // POST: /Customer/CheckOutSession (กดเช็กเอาต์ / จบการซ้อมหลังใช้งานเสร็จ ย้ายไปแท็บประวัติการใช้งานทันที)
    [HttpPost]
    public IActionResult CheckOutSession(int bookingId, string? email)
    {
        var user = GetCurrentUser(email);
        if (user == null) return RedirectToAction("Index", "Login");

        var booking = _db.Bookings
            .Include(b => b.Pod)
            .Include(b => b.CheckIns)
            .FirstOrDefault(b => b.BookingId == bookingId && b.UserId == user.UserId);

        if (booking == null)
        {
            TempData["Error"] = "ไม่พบรายการจองที่ต้องการเช็กเอาต์ครับ";
            return RedirectToAction("MyBookings", new { email = user.Email });
        }

        var activeCheckIn = booking.CheckIns.OrderByDescending(c => c.CheckInId).FirstOrDefault();
        if (activeCheckIn != null && activeCheckIn.CheckOutTime == null)
        {
            activeCheckIn.CheckOutTime = DateTime.Now;
        }
        else if (activeCheckIn == null)
        {
            _db.CheckIns.Add(new CheckIn
            {
                BookingId = booking.BookingId,
                UserId = user.UserId,
                CheckInTime = DateTime.Now.AddMinutes(-45),
                CheckOutTime = DateTime.Now,
                AccessMethod = "QR_Code"
            });
        }

        booking.Status = "Completed";
        _db.SaveChanges();

        TempData["Success"] = $"เช็กเอาต์และจบเซสชันการฝึกซ้อมห้อง {booking.Pod?.PodName ?? "Private Pod"} (#PG-{booking.BookingId:D5}) เรียบร้อยแล้ว! บันทึกเข้าสู่ประวัติการใช้งานสำเร็จครับ";
        return RedirectToAction("MyBookings", new { email = user.Email, tab = "history" });
    }

    // Helper: ซิงค์รายการจองที่ลูกค้าเคยกดเช็กอินไว้ และตอนนี้เลยเวลา EndTime แล้ว ให้เปลี่ยนเป็น Completed อัตโนมัติ
    private void SyncExpiredCheckIns(int userId)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var nowTime = TimeOnly.FromDateTime(DateTime.Now);

        var expiredCheckedInBookings = _db.Bookings
            .Include(b => b.CheckIns)
            .Where(b => b.UserId == userId &&
                        b.Status == "Confirmed" &&
                        (b.BookingDate < today || (b.BookingDate == today && b.EndTime <= nowTime)))
            .ToList();

        bool hasChanges = false;
        foreach (var b in expiredCheckedInBookings)
        {
            // ถ้ารายการที่หมดเวลามีการเช็กอินเข้าห้องจริง ให้ปรับเป็น Completed อัตโนมัติ
            // ส่วนรายการที่ไม่ได้เช็กอินเลย จะคง Status == "Confirmed" (แต่ไม่มี CheckIns) เพื่อให้แสดงเป็น "ไม่ได้เข้าใช้งาน (No-Show)"
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

        if (hasChanges)
        {
            _db.SaveChanges();
        }
    }

    // POST: /Customer/RescheduleBooking (เลื่อนวันและรอบเวลาการจอง - สไตล์ Lab 10 Update)
    [HttpPost]
    public IActionResult RescheduleBooking(int bookingId, string newDate, string newTimeSlot, string? email)
    {
        var booking = _db.Bookings
            .Include(b => b.Pod)
            .Include(b => b.CheckIns)
            .FirstOrDefault(b => b.BookingId == bookingId);

        if (booking == null || booking.Status != "Confirmed")
        {
            TempData["Error"] = "ไม่พบรายการจองที่สามารถเลื่อนรอบได้";
            return RedirectToAction("MyBookings", new { email });
        }

        if (booking.Pod?.Status == "Maintenance")
        {
            TempData["Error"] = "ไม่สามารถเลื่อนรอบในห้องซ้อมนี้ได้ เนื่องจากห้องอยู่ระหว่างปิดปรับปรุง กรุณายกเลิกเพื่อคืนโควต้าแล้วจองห้องอื่นแทนครับ";
            return RedirectToAction("MyBookings", new { email });
        }

        var today = DateOnly.FromDateTime(DateTime.Today);
        var nowTime = TimeOnly.FromDateTime(DateTime.Now);

        // ห้ามเลื่อนรอบถ้าเช็กอินเข้าห้องไปแล้ว หรือเลยเวลาจบเซสชันไปแล้ว
        if (booking.CheckIns != null && booking.CheckIns.Any())
        {
            TempData["Error"] = "ไม่สามารถเลื่อนรอบได้ เนื่องจากคุณได้เช็กอินเข้าใช้งานห้องซ้อมไปแล้วครับ";
            return RedirectToAction("MyBookings", new { email });
        }

        if (booking.BookingDate < today || (booking.BookingDate == today && booking.EndTime <= nowTime))
        {
            TempData["Error"] = "ไม่สามารถเลื่อนรายการจองที่เลยเวลาใช้งานไปแล้วได้ครับ";
            return RedirectToAction("MyBookings", new { email, tab = "history" });
        }

        if (!DateOnly.TryParseExact(newDate?.Trim(), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var parsedDate))
        {
            TempData["Error"] = "รูปแบบวันที่ไม่ถูกต้อง";
            return RedirectToAction("MyBookings", new { email });
        }
        if (parsedDate.Year > 2400) parsedDate = new DateOnly(parsedDate.Year - 543, parsedDate.Month, parsedDate.Day);

        TimeOnly startTime = booking.StartTime;
        TimeOnly endTime = booking.EndTime;
        if (!string.IsNullOrEmpty(newTimeSlot))
        {
            var cleanSlot = newTimeSlot.Replace("น.", "").Trim();
            var parts = cleanSlot.Split('-');
            if (parts.Length == 2)
            {
                TimeOnly.TryParse(parts[0].Trim(), System.Globalization.CultureInfo.InvariantCulture, out startTime);
                TimeOnly.TryParse(parts[1].Trim(), System.Globalization.CultureInfo.InvariantCulture, out endTime);
            }
        }

        if (parsedDate < today || (parsedDate == today && startTime <= nowTime))
        {
            TempData["Error"] = "ไม่สามารถเลื่อนไปวันหรือรอบเวลาที่ผ่านไปแล้วได้";
            return RedirectToAction("MyBookings", new { email });
        }

        bool isSlotTaken = _db.Bookings.Any(b =>
            b.BookingId != bookingId &&
            b.PodId == booking.PodId &&
            b.BookingDate == parsedDate &&
            b.StartTime == startTime &&
            b.Status == "Confirmed");

        if (isSlotTaken)
        {
            TempData["Error"] = "ห้อง Pod ในวันและเวลาที่คุณต้องการเลื่อนไปมีผู้จองแล้ว กรุณาเลือกรอบเวลาอื่น";
            return RedirectToAction("MyBookings", new { email });
        }

        booking.BookingDate = parsedDate;
        booking.StartTime = startTime;
        booking.EndTime = endTime;
        _db.SaveChanges();

        TempData["Success"] = $"เลื่อนรอบการจอง #BK-{bookingId:D5} เป็นวันที่ {parsedDate:dd/MM/yyyy} เวลา {startTime:HH:mm} - {endTime:HH:mm} น. เรียบร้อยแล้ว";
        return RedirectToAction("MyBookings", new { email });
    }

    // POST: /Customer/CancelBooking
    // ยกเลิกการจองที่ยังไม่ได้เช็กอินและคืนโควต้า Pod/PT กลับให้สมาชิก
    [HttpPost]
    public IActionResult CancelBooking(int bookingId, string? email)
    {
        var booking = _db.Bookings
            .Include(b => b.CheckIns)
            .FirstOrDefault(b => b.BookingId == bookingId);

        if (booking != null && booking.Status == "Confirmed")
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            var nowTime = TimeOnly.FromDateTime(DateTime.Now);

            if (booking.CheckIns != null && booking.CheckIns.Any())
            {
                TempData["Error"] = "ไม่สามารถยกเลิกการจองได้ เนื่องจากคุณได้เช็กอินเข้าใช้งานห้องซ้อมไปแล้วครับ";
                return RedirectToAction("MyBookings", new { email });
            }

            if (booking.BookingDate < today || (booking.BookingDate == today && booking.EndTime <= nowTime))
            {
                TempData["Error"] = "ไม่สามารถยกเลิกรายการจองที่เลยเวลาใช้งานไปแล้วได้ครับ";
                return RedirectToAction("MyBookings", new { email, tab = "history" });
            }

            booking.Status = "Cancelled";

            // คืนโควต้าให้ลูกค้า (เฉพาะแพ็กเกจปัจจุบันที่ใช้งานอยู่และไม่ใช่ VIP)
            var todayForSub = DateOnly.FromDateTime(DateTime.Today);
            var sub = _db.MemberSubscriptions
                .Include(s => s.Package)
                .Where(s => s.UserId == booking.UserId && s.Status == "Active" && s.StartDate <= todayForSub)
                .OrderByDescending(s => s.SubscriptionId)
                .FirstOrDefault();

            if (sub != null && !(sub.Package?.IsVip ?? false))
            {
                sub.RemainingPods += 1;
                if (booking.TrainerId.HasValue)
                {
                    sub.RemainingPt += 1;
                }
            }

            _db.SaveChanges();
            TempData["Success"] = "ยกเลิกการจองและคืนโควต้าเรียบร้อยแล้ว";
        }

        return RedirectToAction("MyBookings", new { email });
    }

    // ================================================================
    // MY PACKAGES & BILLING ENGINE (ระบบจัดการแพ็กเกจ รอบบิล และการชำระเงินระดับใช้งานจริง)
    // ================================================================
    // โครงสร้างการทำงาน (Flow สำหรับอ่านทบทวน):
    // 1. ตาราง MemberSubscriptions:
    //    - คอลัมน์ Status ใน MySQL เป็น ENUM('Active','Expired','Cancelled')
    //    - แพ็กเกจปัจจุบันที่กำลังใช้งานอยู่ คือแถวที่ Status == "Active" และ StartDate <= วันนี้
    //    - แพ็กเกจรอบบิลถัดไปที่ตั้งเวลาเปลี่ยนล่วงหน้า คือแถวที่ Status == "Active" และ StartDate > วันนี้
    // 2. ตาราง Payments:
    //    - คอลัมน์ Status ใน MySQL เป็น ENUM('Pending','Success','Failed')
    //    - คอลัมน์ PaymentMethod เป็น ENUM('CreditCard','PromptPay','BankTransfer')
    //    - Status == "Pending": ใบแจ้งหนี้รอบบิลถัดไปที่รอชำระเงิน (ยังไม่ออกใบเสร็จรับเงิน)
    //    - Status == "Success": ชำระเงินสำเร็จแล้ว (ออกใบเสร็จรับเงิน PDF ได้ทันที)
    //    - Status == "Failed": รายการที่ถูกยกเลิกหรือไม่สำเร็จ
    // ================================================================
    public IActionResult MyPackages(string? email)
    {
        var user = GetCurrentUser(email);
        if (user == null) return RedirectToAction("Index", "Login");

        var subscriptions = _db.MemberSubscriptions
            .Include(ms => ms.Package)
            .Where(ms => ms.UserId == user.UserId)
            .OrderByDescending(ms => ms.SubscriptionId)
            .ToList();

        var today = DateOnly.FromDateTime(DateTime.Today);

        // STEP 1: แยก "แพ็กเกจปัจจุบัน (StartDate <= วันนี้)" กับ "แพ็กเกจรอบถัดไปที่ตั้งเวลาไว้ (StartDate > วันนี้)"
        var currentSub = subscriptions
            .Where(s => s.Status == "Active" && s.StartDate <= today)
            .OrderByDescending(s => s.SubscriptionId)
            .FirstOrDefault();

        var scheduledSub = subscriptions
            .Where(s => s.Status == "Active" && s.StartDate > today)
            .OrderByDescending(s => s.SubscriptionId)
            .FirstOrDefault();

        // STEP 2: Auto-Rollover Logic (เมื่อถึงวันสิ้นสุดรอบเดือน และมีแพ็กเกจรอบถัดไปรออยู่)
        if (currentSub != null && currentSub.EndDate < today && scheduledSub != null)
        {
            var pendingInvoice = _db.Payments
                .Where(p => p.UserId == user.UserId && p.Status == "Pending" && p.PackageId == scheduledSub.PackageId)
                .OrderByDescending(p => p.PaymentId)
                .FirstOrDefault();

            string rollMethod = pendingInvoice?.PaymentMethod ?? "PromptPay";

            // หากลูกค้าผูกบัตรเครดิต/เดบิตไว้ หรือชำระเงินล่วงหน้าไว้แล้ว ให้สลับแพ็กเกจเข้าสู่รอบใหม่ทันที
            bool alreadyPrepaid = _db.Payments.Any(p =>
                p.UserId == user.UserId &&
                p.Status == "Success" &&
                p.PackageId == scheduledSub.PackageId &&
                p.SlipReference != null &&
                p.SlipReference.StartsWith("INV-NEXT"));

            if (rollMethod == "CreditCard" || alreadyPrepaid)
            {
                currentSub.Status = "Expired";
                scheduledSub.StartDate = today;
                currentSub = scheduledSub;

                if (pendingInvoice != null)
                {
                    pendingInvoice.Status = "Success";
                    pendingInvoice.PaymentDate = DateTime.Now;
                    pendingInvoice.SlipReference = $"INV-{DateTime.Now:yyyyMMdd}-AUTO{scheduledSub.PackageId}";
                }
                _db.SaveChanges();
                scheduledSub = null;
            }
        }

        var payments = _db.Payments
            .Include(p => p.Package)
            .Where(p => p.UserId == user.UserId && p.Status != "Failed")
            .OrderByDescending(p => p.PaymentDate)
            .ThenByDescending(p => p.PaymentId)
            .ToList();

        // STEP 3: ดึงช่องทางชำระเงินหลักของลูกค้า (จากตอน Register หรือที่ตั้งค่าไว้ล่าสุด)
        string preferredMethod = payments.FirstOrDefault()?.PaymentMethod ?? "PromptPay";
        if (preferredMethod != "CreditCard" && preferredMethod != "PromptPay" && preferredMethod != "BankTransfer")
        {
            preferredMethod = "PromptPay";
        }

        // เช็คว่ารายการแพ็กเกจรอบถัดไปได้ชำระเงินล่วงหน้าไปแล้วหรือยัง
        var pendingNextPayment = payments.FirstOrDefault(p => p.Status == "Pending");
        bool isNextCyclePaid = scheduledSub != null && pendingNextPayment == null;

        ViewBag.Payments = payments;
        ViewBag.AllPackages = _db.Packages.OrderBy(p => p.Price).ToList();
        ViewBag.ScheduledSub = scheduledSub;
        ViewBag.PendingNextPayment = pendingNextPayment;
        ViewBag.IsNextCyclePaid = isNextCyclePaid;
        ViewBag.PreferredPaymentMethod = preferredMethod;

        SetCommonViewBag(user, currentSub);

        return View(subscriptions);
    }

    // POST: /Customer/TopUpQuota
    // ทำหน้าที่ทั้ง:
    // 1. ซื้อโควต้าเสริมรายครั้ง (ต้องผ่านหน้าต่างยืนยันการชำระเงิน Checkout ก่อน)
    // 2. ตั้งค่าเปลี่ยนแพ็กเกจสำหรับรอบบิลถัดไป (สร้างสัญญาล่วงหน้า StartDate > Today + สร้างใบแจ้งหนี้ Status = "Pending")
    // 3. อัปเกรดแพ็กเกจทันทีพร้อมชำระเงินจริงและทบโควต้าคงเหลือเดิม
    [HttpPost]
    public IActionResult TopUpQuota(string? email, string addonType, string paymentMethod = "", string changeMode = "next_cycle", string? slipRef = "")
    {
        var user = GetCurrentUser(email);
        if (user == null) return RedirectToAction("Index", "Login");

        var today = DateOnly.FromDateTime(DateTime.Today);

        // ============================================================
        // GUARD 1: ป้องกันการกดปุ่มรัวซ้ำย้ำๆ (Anti-Spam Cooldown 6 วินาที)
        // ============================================================
        var lastPayment = _db.Payments
            .Where(p => p.UserId == user.UserId && p.PaymentDate.HasValue)
            .OrderByDescending(p => p.PaymentId)
            .FirstOrDefault();

        if (lastPayment != null && lastPayment.PaymentDate.HasValue &&
            (DateTime.Now - lastPayment.PaymentDate.Value).TotalSeconds < 6)
        {
            TempData["Error"] = "ระบบกำลังประมวลผลรายการก่อนหน้า กรุณาอย่ากดปุ่มซ้ำติดกันครับ";
            return RedirectToAction("MyPackages", new { email = user.Email });
        }

        // ตรวจสอบ PaymentMethod ให้ตรงกับ ENUM('CreditCard','PromptPay','BankTransfer') ใน MySQL 100%
        string savedMethod = lastPayment?.PaymentMethod ?? "PromptPay";
        string selectedMethod = !string.IsNullOrWhiteSpace(paymentMethod) ? paymentMethod.Trim() : savedMethod;
        if (selectedMethod != "CreditCard" && selectedMethod != "PromptPay" && selectedMethod != "BankTransfer")
        {
            selectedMethod = "PromptPay";
        }

        // ดึงแพ็กเกจปัจจุบันที่กำลังใช้งานอยู่ (StartDate <= วันนี้)
        var activeSub = _db.MemberSubscriptions
            .Include(s => s.Package)
            .Where(s => s.UserId == user.UserId && s.Status == "Active" && s.StartDate <= today)
            .OrderByDescending(s => s.SubscriptionId)
            .FirstOrDefault();

        if (activeSub == null)
        {
            var defaultPkg = _db.Packages.FirstOrDefault(p => p.PackageId == 2) ?? _db.Packages.FirstOrDefault();
            if (defaultPkg != null)
            {
                activeSub = new MemberSubscription
                {
                    UserId = user.UserId,
                    PackageId = defaultPkg.PackageId,
                    StartDate = today,
                    EndDate = today.AddDays(30),
                    RemainingPods = defaultPkg.PodQuota,
                    RemainingPt = defaultPkg.PtQuota,
                    RemainingInBody = defaultPkg.InBodyQuota,
                    Status = "Active" // ตรงตาม ENUM('Active','Expired','Cancelled')
                };
                _db.MemberSubscriptions.Add(activeSub);
                _db.SaveChanges();
            }
        }

        if (activeSub == null)
        {
            TempData["Error"] = "ไม่พบข้อมูลแพ็กเกจสมาชิกในระบบ";
            return RedirectToAction("MyPackages", new { email = user.Email });
        }

        // ตรวจสอบว่ามีแพ็กเกจรอบบิลถัดไปที่ตั้งเวลาไว้แล้วหรือไม่ (StartDate > วันนี้)
        var existingScheduledSub = _db.MemberSubscriptions
            .Include(s => s.Package)
            .Where(s => s.UserId == user.UserId && s.Status == "Active" && s.StartDate > today)
            .OrderByDescending(s => s.SubscriptionId)
            .FirstOrDefault();

        // ============================================================
        // FLOW A: การเปลี่ยนแพ็กเกจ (switch_pkg_{id})
        // ============================================================
        if (addonType != null && addonType.StartsWith("switch_pkg_"))
        {
            if (!int.TryParse(addonType.Replace("switch_pkg_", ""), out int newPkgId))
            {
                TempData["Error"] = "รหัสแพ็กเกจไม่ถูกต้อง";
                return RedirectToAction("MyPackages", new { email = user.Email });
            }

            var newPkg = _db.Packages.FirstOrDefault(p => p.PackageId == newPkgId);
            if (newPkg == null)
            {
                TempData["Error"] = "ไม่พบแพ็กเกจที่คุณเลือกในระบบ";
                return RedirectToAction("MyPackages", new { email = user.Email });
            }

            // GUARD 2: ป้องกันการกดเลือกแพ็กเกจเดิมที่ตัวเองกำลังถืออยู่ในปัจจุบัน
            if (activeSub.PackageId == newPkg.PackageId)
            {
                TempData["Error"] = $"คุณกำลังใช้งานแพ็กเกจ {newPkg.PackageName} อยู่ในรอบปัจจุบันแล้วครับ";
                return RedirectToAction("MyPackages", new { email = user.Email });
            }

            // GUARD 3: ป้องกัน Edge Case กดสลับแพ็กเกจไปมาซ้ำๆ
            // หากตั้งค่าแพ็กเกจรอบถัดไปไว้แล้ว จะไม่อนุญาตให้กดสลับเป็นแพ็กเกจอื่นทับซ้อน ต้องกดยกเลิกคำขอเดิมก่อน
            if (existingScheduledSub != null)
            {
                TempData["Error"] = $"คุณได้ตั้งเวลาเปลี่ยนเป็นแพ็กเกจ {existingScheduledSub.Package?.PackageName} สำหรับรอบบิลถัดไปไว้แล้ว หากต้องการเปลี่ยนแผนใหม่ กรุณากดปุ่ม 'ยกเลิกการเปลี่ยนแผนล่วงหน้า' ก่อนครับ";
                return RedirectToAction("MyPackages", new { email = user.Email });
            }

            // กรณีที่ A1: ตั้งเวลาเปลี่ยนแพ็กเกจในรอบบิลถัดไป (ถือแพ็กเกจเดิมจนสิ้นเดือน)
            if (changeMode != "immediate_upgrade")
            {
                DateOnly nextStart = activeSub.EndDate.AddDays(1);
                int duration = newPkg.DurationDays > 0 ? newPkg.DurationDays : 30;
                DateOnly nextEnd = nextStart.AddDays(duration);

                // บันทึกสัญญารอบถัดไป โดยใช้ Status = "Active" (ตาม ENUM ของ MySQL) และใช้ StartDate > Today เป็นตัวแยก
                var nextSub = new MemberSubscription
                {
                    UserId = user.UserId,
                    PackageId = newPkg.PackageId,
                    StartDate = nextStart,
                    EndDate = nextEnd,
                    RemainingPods = newPkg.PodQuota,
                    RemainingPt = newPkg.PtQuota,
                    RemainingInBody = newPkg.InBodyQuota,
                    Status = "Active"
                };
                _db.MemberSubscriptions.Add(nextSub);

                // บันทึกใบแจ้งหนี้สำหรับรอบบิลถัดไปลงตาราง Payments ด้วย Status = "Pending" (ตาม ENUM ของ MySQL)
                var pendingPayment = new Payment
                {
                    UserId = user.UserId,
                    PackageId = newPkg.PackageId,
                    Amount = newPkg.Price,
                    PaymentMethod = selectedMethod,
                    PaymentDate = DateTime.Now,
                    Status = "Pending",
                    SlipReference = $"NEXT-{nextStart:yyyyMMdd}-PKG{newPkg.PackageId}"
                };
                _db.Payments.Add(pendingPayment);
                _db.SaveChanges();

                string payNotice = selectedMethod == "CreditCard"
                    ? "ระบบจะตัดบัตรเครดิต/เดบิตอัตโนมัติในวันเริ่มรอบบิลใหม่"
                    : "คุณสามารถกดชำระเงินล่วงหน้าหรือรอสแกนชำระเมื่อถึงวันครบรอบบิลได้";

                TempData["Success"] = $"บันทึกการเปลี่ยนแพ็กเกจเป็น {newPkg.PackageName} สำหรับรอบบิลถัดไป (เริ่ม {nextStart:dd/MM/yyyy}) เรียบร้อยแล้ว! คุณยังคงถือสิทธิ์แพ็กเกจ {activeSub.Package?.PackageName} เดิมได้จนถึงวันที่ {activeSub.EndDate:dd/MM/yyyy} ({payNotice})";
                return RedirectToAction("MyPackages", new { email = user.Email });
            }
            else
            {
                // กรณีที่ A2: อัปเกรดเป็นแพ็กเกจที่สูงขึ้นทันทีวันนี้ (ผ่านหน้าต่างชำระเงิน Checkout แล้ว)
                // GUARD 4: ห้ามกด Immediate Upgrade ไปยังแพ็กเกจที่ราคาต่ำกว่าหรือเท่าเดิม (ป้องกันการ Downgrade ทับสิทธิ์เดิม)
                decimal currentPrice = activeSub.Package?.Price ?? 0m;
                if (newPkg.Price <= currentPrice)
                {
                    TempData["Error"] = "การเปลี่ยนเป็นแพ็กเกจระดับต่ำกว่าเดิม ต้องมีผลในรอบบิลถัดไปเท่านั้น เพื่อรักษาสิทธิ์การใช้งานปัจจุบันของคุณครับ";
                    return RedirectToAction("MyPackages", new { email = user.Email });
                }

                int carryPods = (activeSub.Package?.IsVip == true || newPkg.IsVip == true) ? 999 : activeSub.RemainingPods + newPkg.PodQuota;
                int carryPt = (activeSub.Package?.IsVip == true || newPkg.IsVip == true) ? 999 : activeSub.RemainingPt + newPkg.PtQuota;
                int carryInBody = activeSub.RemainingInBody + newPkg.InBodyQuota;

                activeSub.PackageId = newPkg.PackageId;
                activeSub.StartDate = today;
                activeSub.EndDate = today.AddDays(newPkg.DurationDays > 0 ? newPkg.DurationDays : 30);
                activeSub.RemainingPods = carryPods;
                activeSub.RemainingPt = carryPt;
                activeSub.RemainingInBody = carryInBody;
                activeSub.Status = "Active";

                string cleanSlip = !string.IsNullOrWhiteSpace(slipRef)
                    ? slipRef.Trim().ToUpper()
                    : Guid.NewGuid().ToString("N")[..4].ToUpper();

                var upgPayment = new Payment
                {
                    UserId = user.UserId,
                    PackageId = newPkg.PackageId,
                    Amount = newPkg.Price,
                    PaymentMethod = selectedMethod,
                    PaymentDate = DateTime.Now,
                    Status = "Success",
                    SlipReference = $"INV-{DateTime.Now:yyyyMMdd}-UPG-{cleanSlip}"
                };
                _db.Payments.Add(upgPayment);
                _db.SaveChanges();

                TempData["Success"] = $"ชำระเงินและอัปเกรดเป็นแพ็กเกจ {newPkg.PackageName} สำเร็จ! ระบบทบโควต้าคงเหลือเดิมเข้าแพ็กเกจใหม่และออกใบเสร็จรับเงินเรียบร้อยแล้ว";
                return RedirectToAction("MyPackages", new { email = user.Email });
            }
        }

        // ============================================================
        // FLOW B: ซื้อโควต้าเสริมรายครั้ง (Add-on) หรือ ต่ออายุแพ็กเกจเดิมล่วงหน้า (+30 วัน)
        // ============================================================
        decimal amount = 750m;
        string itemLabel = "ห้องออกกำลังกายส่วนตัว (+1 ครั้ง)";
        string refPrefix = "POD1";

        if (addonType == "pod_1")
        {
            activeSub.RemainingPods += 1;
            amount = 750m;
            itemLabel = "ห้องออกกำลังกายส่วนตัว (+1 ครั้ง)";
            refPrefix = "POD1";
        }
        else if (addonType == "pt_1")
        {
            activeSub.RemainingPt += 1;
            amount = 1200m;
            itemLabel = "เทรนเนอร์ดูแลตัวต่อตัว (+1 ครั้ง)";
            refPrefix = "PT1";
        }
        else if (addonType == "inbody_1")
        {
            activeSub.RemainingInBody += 1;
            amount = 300m;
            itemLabel = "ตรวจวัดมวลกล้ามเนื้อและไขมัน (+1 ครั้ง)";
            refPrefix = "INB1";
        }
        else if (addonType == "guest_1")
        {
            amount = 350m;
            itemLabel = "บัตรเชิญเพื่อนเข้าใช้งาน (+1 วัน)";
            refPrefix = "GST1";
        }
        else if (addonType == "renew")
        {
            if (existingScheduledSub != null)
            {
                TempData["Error"] = "คุณมีรายการตั้งเวลาเปลี่ยนแพ็กเกจในรอบถัดไปอยู่แล้ว หากต้องการต่ออายุแพ็กเกจเดิม กรุณากดยกเลิกการเปลี่ยนแผนล่วงหน้าก่อนครับ";
                return RedirectToAction("MyPackages", new { email = user.Email });
            }

            var pkg = activeSub.Package ?? _db.Packages.FirstOrDefault(p => p.PackageId == activeSub.PackageId);
            int addDays = pkg?.DurationDays > 0 ? pkg.DurationDays : 30;
            activeSub.EndDate = activeSub.EndDate.AddDays(addDays);
            if (pkg?.IsVip != true)
            {
                activeSub.RemainingPods += (pkg?.PodQuota ?? 4);
                activeSub.RemainingPt += (pkg?.PtQuota ?? 0);
                activeSub.RemainingInBody += (pkg?.InBodyQuota ?? 1);
            }
            activeSub.Status = "Active";
            amount = pkg?.Price ?? 5900m;
            itemLabel = $"ต่ออายุแพ็กเกจ {pkg?.PackageName ?? "สมาชิก"} ล่วงหน้า (+{addDays} วัน)";
            refPrefix = "RNW";
        }

        string userSlipCode = !string.IsNullOrWhiteSpace(slipRef)
            ? slipRef.Trim().ToUpper()
            : Guid.NewGuid().ToString("N")[..4].ToUpper();

        var payment = new Payment
        {
            UserId = user.UserId,
            PackageId = activeSub.PackageId,
            Amount = amount,
            PaymentMethod = selectedMethod,
            PaymentDate = DateTime.Now,
            Status = "Success",
            SlipReference = $"INV-{DateTime.Now:yyyyMMdd}-{refPrefix}-{userSlipCode}"
        };
        _db.Payments.Add(payment);
        _db.SaveChanges();

        TempData["Success"] = $"ชำระเงินสำเร็จ: {itemLabel} (ยอดสุทธิ ฿{amount:N0}) อัปเดตโควต้าและออกใบเสร็จรับเงินเรียบร้อยแล้ว!";
        return RedirectToAction("MyPackages", new { email = user.Email });
    }

    // POST: /Customer/CancelScheduledPlan
    // ยกเลิกการเปลี่ยนแพ็กเกจล่วงหน้า (ทำได้เฉพาะตอนที่บิลรอบถัดไปเป็น Pending คือยังไม่ได้ชำระเงิน)
    [HttpPost]
    public IActionResult CancelScheduledPlan(string? email)
    {
        var user = GetCurrentUser(email);
        if (user == null) return RedirectToAction("Index", "Login");

        var today = DateOnly.FromDateTime(DateTime.Today);

        var scheduledSub = _db.MemberSubscriptions
            .Where(s => s.UserId == user.UserId && s.Status == "Active" && s.StartDate > today)
            .OrderByDescending(s => s.SubscriptionId)
            .FirstOrDefault();

        if (scheduledSub == null)
        {
            TempData["Error"] = "ไม่พบรายการเปลี่ยนแพ็กเกจล่วงหน้าที่สามารถยกเลิกได้";
            return RedirectToAction("MyPackages", new { email = user.Email });
        }

        // เช็คว่ามีบิล Pending อยู่หรือไม่ (ถ้าจ่ายเงินไปแล้ว จะไม่อนุญาตให้กดยกเลิกเอง)
        var pendingPayments = _db.Payments
            .Where(p => p.UserId == user.UserId && p.Status == "Pending")
            .ToList();

        if (!pendingPayments.Any())
        {
            TempData["Error"] = "รายการรอบบิลถัดไปนี้ได้รับการชำระเงินและออกใบเสร็จเรียบร้อยแล้ว ไม่สามารถยกเลิกด้วยตนเองได้ กรุณาติดต่อเจ้าหน้าที่ Concierge";
            return RedirectToAction("MyPackages", new { email = user.Email });
        }

        // ลบสัญญาล่วงหน้า และลบใบแจ้งหนี้ Pending ออก
        _db.MemberSubscriptions.Remove(scheduledSub);
        foreach (var p in pendingPayments)
        {
            _db.Payments.Remove(p);
        }

        _db.SaveChanges();
        TempData["Success"] = "ยกเลิกคำขอเปลี่ยนแพ็กเกจในรอบบิลถัดไปเรียบร้อยแล้ว ระบบจะคงแพ็กเกจปัจจุบันของคุณไว้ตามเดิมครับ";
        return RedirectToAction("MyPackages", new { email = user.Email });
    }

    // POST: /Customer/PayScheduledPlanNow
    // ชำระเงินล่วงหน้าสำหรับใบแจ้งหนี้รอบบิลถัดไป (เปลี่ยน Status ของ Payment จาก "Pending" -> "Success")
    // โดยที่แพ็กเกจเดือนปัจจุบันยังคงใช้งานได้จนครบวันหมดอายุเดิม
    [HttpPost]
    public IActionResult PayScheduledPlanNow(string? email, string paymentMethod = "", string? slipRef = "")
    {
        var user = GetCurrentUser(email);
        if (user == null) return RedirectToAction("Index", "Login");

        var pendingPayment = _db.Payments
            .Include(p => p.Package)
            .Where(p => p.UserId == user.UserId && p.Status == "Pending")
            .OrderByDescending(p => p.PaymentId)
            .FirstOrDefault();

        if (pendingPayment == null)
        {
            TempData["Error"] = "ไม่พบใบแจ้งหนี้รอชำระสำหรับรอบบิลถัดไป (หรือรายการนี้ชำระสำเร็จไปแล้ว)";
            return RedirectToAction("MyPackages", new { email = user.Email });
        }

        if (!string.IsNullOrWhiteSpace(paymentMethod) &&
            (paymentMethod == "CreditCard" || paymentMethod == "PromptPay" || paymentMethod == "BankTransfer"))
        {
            pendingPayment.PaymentMethod = paymentMethod;
        }

        string cleanSlip = !string.IsNullOrWhiteSpace(slipRef)
            ? slipRef.Trim().ToUpper()
            : Guid.NewGuid().ToString("N")[..4].ToUpper();

        pendingPayment.Status = "Success"; // ตรงตาม ENUM('Pending','Success','Failed')
        pendingPayment.PaymentDate = DateTime.Now;
        pendingPayment.SlipReference = $"INV-NEXT-{DateTime.Now:yyyyMMdd}-{cleanSlip}";
        _db.SaveChanges();

        TempData["Success"] = $"ชำระเงินล่วงหน้าสำหรับแพ็กเกจรอบบิลถัดไป ({pendingPayment.Package?.PackageName}) เรียบร้อยแล้ว! ออกใบเสร็จรับเงินสำเร็จ โดยคุณยังสามารถใช้สิทธิ์แพ็กเกจปัจจุบันได้จนถึงวันสิ้นสุดรอบเดิมครับ";
        return RedirectToAction("MyPackages", new { email = user.Email });
    }

    // POST: /Customer/UpdatePaymentMethod
    // เปลี่ยนช่องทางการชำระเงินหลักสำหรับรอบบิลถัดไป ('CreditCard', 'PromptPay', 'BankTransfer')
    [HttpPost]
    public IActionResult UpdatePaymentMethod(string? email, string paymentMethod)
    {
        var user = GetCurrentUser(email);
        if (user == null) return RedirectToAction("Index", "Login");

        string validMethod = "PromptPay";
        if (paymentMethod == "CreditCard" || paymentMethod == "PromptPay" || paymentMethod == "BankTransfer")
        {
            validMethod = paymentMethod;
        }

        // อัปเดตในใบแจ้งหนี้ Pending (ถ้ามี) และรายการล่าสุดของลูกค้า
        var pendingPayment = _db.Payments
            .Where(p => p.UserId == user.UserId && p.Status == "Pending")
            .OrderByDescending(p => p.PaymentId)
            .FirstOrDefault();

        if (pendingPayment != null)
        {
            pendingPayment.PaymentMethod = validMethod;
        }

        var latestPayment = _db.Payments
            .Where(p => p.UserId == user.UserId)
            .OrderByDescending(p => p.PaymentId)
            .FirstOrDefault();

        if (latestPayment != null)
        {
            latestPayment.PaymentMethod = validMethod;
        }

        _db.SaveChanges();

        string methodName = validMethod == "CreditCard"
            ? "ผูกบัตรเครดิต/เดบิต (ตัดชำระอัตโนมัติ)"
            : (validMethod == "BankTransfer" ? "โอนเงินผ่านบัญชีธนาคาร" : "สแกน QR PromptPay");

        TempData["Success"] = $"อัปเดตช่องทางการชำระเงินหลักเป็น [{methodName}] เรียบร้อยแล้วครับ";
        return RedirectToAction("MyPackages", new { email = user.Email });
    }

    public IActionResult ServiceHistory(string? email, string? filter = "all")
    {
        var user = GetCurrentUser(email);
        if (user == null) return RedirectToAction("Index", "Login");

        SyncExpiredCheckIns(user.UserId);

        var todayDate = DateOnly.FromDateTime(DateTime.Today);
        var nowTime = TimeOnly.FromDateTime(DateTime.Now);

        var allUserBookings = _db.Bookings
            .Include(b => b.Pod)
                .ThenInclude(p => p.Branch)
            .Include(b => b.Trainer)
                .ThenInclude(t => t!.User)
            .Include(b => b.CheckIns)
            .Where(b => b.UserId == user.UserId)
            .OrderByDescending(b => b.BookingDate)
            .ThenByDescending(b => b.StartTime)
            .ToList();

        // กรองเฉพาะรายการที่เป็นประวัติแล้วจริงๆ (เช็กอินแล้ว / เสร็จสิ้นแล้ว / ยกเลิกแล้ว / เลยเวลาแล้วไม่ได้เข้าใช้งาน)
        var historyBookings = allUserBookings
            .Where(b =>
                b.Status == "Completed" ||
                b.Status == "Cancelled" ||
                (b.CheckIns != null && b.CheckIns.Any()) ||
                b.BookingDate < todayDate ||
                (b.BookingDate == todayDate && b.EndTime <= nowTime))
            .ToList();

        ViewBag.Filter = filter ?? "all";
        var todayForSub = DateOnly.FromDateTime(DateTime.Today);
        var sub = user.MemberSubscriptions
            .Where(s => s.Status == "Active" && s.StartDate <= todayForSub)
            .OrderByDescending(s => s.SubscriptionId)
            .FirstOrDefault()
            ?? user.MemberSubscriptions.FirstOrDefault(s => s.Status == "Active");
        SetCommonViewBag(user, sub);
        ViewBag.WorkoutLogsByBooking = TrainerController.ReadWorkoutLogsByBookingForUser(user.UserId);

        return View(historyBookings);
    }

    public IActionResult Support(string? email)
    {
        var user = GetCurrentUser(email);
        if (user == null) return RedirectToAction("Index", "Login");

        var todayForSub = DateOnly.FromDateTime(DateTime.Today);
        var sub = user.MemberSubscriptions
            .Where(s => s.Status == "Active" && s.StartDate <= todayForSub)
            .OrderByDescending(s => s.SubscriptionId)
            .FirstOrDefault()
            ?? user.MemberSubscriptions.FirstOrDefault(s => s.Status == "Active");
        SetCommonViewBag(user, sub);
        ViewBag.Branches = _db.Branches.ToList();

        return View(user);
    }

    // ================================================================
    // HELPER FUNCTIONS (เข้าใจง่าย ไม่ซับซ้อน)
    // ================================================================
    private void EnsureTrainersSeeded(int defaultBranchId)
    {
        Csi402dbContext.EnsureAllBranchesPodsAndTrainersSeeded(_db);
    }

    private User? GetCurrentUser(string? email)
    {
        if (!string.IsNullOrWhiteSpace(email))
        {
            var clean = email.Trim();
            return _db.Users
                .Include(u => u.HomeBranch)
                .Include(u => u.MemberSubscriptions)
                    .ThenInclude(ms => ms.Package)
                .FirstOrDefault(u => u.Email == clean || u.Phone == clean);
        }

        return _db.Users
            .Include(u => u.HomeBranch)
            .Include(u => u.MemberSubscriptions)
                .ThenInclude(ms => ms.Package)
            .Where(u => u.RoleId == 4)
            .OrderByDescending(u => u.UserId)
            .FirstOrDefault()
            ?? _db.Users
                .Include(u => u.HomeBranch)
                .Include(u => u.MemberSubscriptions)
                    .ThenInclude(ms => ms.Package)
                .OrderByDescending(u => u.UserId)
                .FirstOrDefault();
    }

    private void SetCommonViewBag(User user, MemberSubscription? subscription)
    {
        bool isVip = subscription?.Package?.IsVip ?? false;
        string podQuota = isVip ? "ไม่จำกัด" : (subscription?.RemainingPods.ToString() ?? "0");
        string ptQuota = isVip ? "ไม่จำกัด" : (subscription?.RemainingPt.ToString() ?? "0");

        ViewBag.FullName = user.FullName;
        ViewBag.Email = user.Email;
        ViewData["FullName"] = user.FullName;
        ViewBag.QuotaBadge = isVip ? "Pod: ไม่จำกัด • PT: ไม่จำกัด" : $"Pod: {podQuota} ครั้ง • PT: {ptQuota} ครั้ง";
        ViewBag.Package = subscription?.Package?.PackageName ?? "Starter Experience";
    }

    public IActionResult Profile(string? email)
    {
        return RedirectToAction("EditProfile", new { email });
    }

    public IActionResult Settings(string? email)
    {
        return RedirectToAction("EditProfile", new { email });
    }

    // POST: /Customer/DeleteAccount
    [HttpPost]
    public IActionResult DeleteAccount(int userId)
    {
        var user = _db.Users.Find(userId);
        if (user != null)
        {
            user.Status = "Suspended";
            _db.SaveChanges();
        }
        return RedirectToAction("Index", "Login");
    }
}

