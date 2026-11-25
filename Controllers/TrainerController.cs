using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PrivateGym.Models;

namespace PrivateGym.Controllers;

// [TrainerController] โซนหลัก:
// 1) หน้างานสอน & บันทึกฝึก: Index, SaveWorkoutLog, CompleteSession
// 2) ตารางสอน & เวลาว่าง: Schedule, Availability, SaveWeeklyAvailability, AddDateOverride, DeleteDateOverride
// 3) ลูกค้า, ประวัติ & โปรไฟล์: Clients, History, ExportHistoryCsv, Profile, UpdateProfile
using PrivateGym.Filters;

[SessionAuthorize("Trainer", "Admin")]
public class TrainerController : Controller
{
    private readonly Csi402dbContext _db; // ตัวเชื่อมต่อฐานข้อมูล MySQL ผ่าน EF Core

    public TrainerController(Csi402dbContext db)
    {
        _db = db;
    }

    // ================================================================
    // 1. แดชบอร์ด & บันทึกการฝึกสอน (DASHBOARD & ACTIVE COACHING)
    // ================================================================
    public IActionResult Index(string? email, int? selectedBookingId = null)
    {
        EnsureTrainersSeeded();
        var trainer = GetCurrentTrainer(email);
        if (trainer == null) return RedirectToAction("Index", "Login");

        SyncExpiredCheckInsForTrainer(trainer.TrainerId);
        SetTrainerViewBag(trainer);

        var today = DateOnly.FromDateTime(DateTime.Today);
        var nowTime = TimeOnly.FromDateTime(DateTime.Now);

        var allBookings = _db.Bookings
            .Include(b => b.User)
                .ThenInclude(u => u!.HomeBranch)
            .Include(b => b.User)
                .ThenInclude(u => u!.MemberSubscriptions)
                    .ThenInclude(ms => ms.Package)
            .Include(b => b.Pod)
                .ThenInclude(p => p!.Branch)
            .Include(b => b.CheckIns)
            .Where(b => b.TrainerId == trainer.TrainerId)
            .OrderBy(b => b.BookingDate)
            .ThenBy(b => b.StartTime)
            .ToList();

        var todaySchedule = allBookings
            .Where(b => b.BookingDate == today && b.Status != "Cancelled")
            .OrderBy(b => b.StartTime)
            .ToList();

        var upcomingList = allBookings
            .Where(b => b.Status == "Confirmed" &&
                        (b.BookingDate > today || (b.BookingDate == today && b.EndTime > nowTime)))
            .ToList();

        var workoutLogs = ReadWorkoutLogs(trainer.TrainerId);
        var loggedBookingIds = workoutLogs
            .Select(l => int.TryParse(l.GetValueOrDefault("BookingId"), out int id) ? id : 0)
            .Where(id => id > 0)
            .ToHashSet();

        int todayTotal = todaySchedule.Count;
        int todayCompleted = todaySchedule.Count(b => b.Status == "Completed");
        int pendingLogsCount = allBookings.Count(b =>
            (b.Status == "Completed" || (b.BookingDate == today && b.Status == "Confirmed")) &&
            !loggedBookingIds.Contains(b.BookingId));

        var nextSlot = upcomingList.FirstOrDefault();

        // เลือกเซสชันสำหรับ Active Coaching Panel
        Booking? activeBooking = null;
        if (selectedBookingId.HasValue)
        {
            activeBooking = allBookings.FirstOrDefault(b => b.BookingId == selectedBookingId.Value);
        }
        activeBooking ??= todaySchedule.FirstOrDefault(b => b.Status == "Confirmed" && b.CheckIns.Any())
                       ?? upcomingList.FirstOrDefault()
                       ?? allBookings.OrderByDescending(b => b.BookingDate).ThenByDescending(b => b.StartTime).FirstOrDefault();

        // ลูกค้าในการดูแล (สรุปการ์ดด้านล่าง)
        var managedClients = allBookings
            .Where(b => b.User != null)
            .GroupBy(b => b.UserId)
            .Select(g => g.OrderByDescending(x => x.BookingDate).First())
            .Take(4)
            .ToList();

        ViewBag.Trainer = trainer;
        ViewBag.TodaySchedule = todaySchedule;
        ViewBag.TodayTotal = todayTotal;
        ViewBag.TodayCompleted = todayCompleted;
        ViewBag.UpcomingCount = upcomingList.Count;
        ViewBag.PendingLogsCount = pendingLogsCount;
        ViewBag.NextSlot = nextSlot;
        ViewBag.CompletedCount = allBookings.Count(b => b.Status == "Completed");
        ViewBag.ClientCount = allBookings.Select(b => b.UserId).Distinct().Count();
        ViewBag.ActiveBooking = activeBooking;
        ViewBag.WorkoutLogs = workoutLogs;
        ViewBag.LoggedBookingIds = loggedBookingIds;
        ViewBag.ManagedClients = managedClients;

        return View(upcomingList);
    }

    // POST: /Trainer/SaveWorkoutLog (บันทึกผลการฝึกสอนหลายท่า/หลายเซ็ตลง JSON + ตัวเลือกปิดจบเซสชัน)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult SaveWorkoutLog(
        int bookingId,
        string? email,
        string? exercisesJson,
        string? primaryMovement,
        string? setsSummary,
        string? topLoad,
        string? rpe,
        string? feedbackNote,
        bool completeNow = false)
    {
        var trainer = GetCurrentTrainer(email);
        if (trainer == null) return RedirectToAction("Index", "Login");

        var booking = _db.Bookings
            .Include(b => b.User)
            .Include(b => b.Pod)
            .Include(b => b.CheckIns)
            .FirstOrDefault(b => b.BookingId == bookingId && b.TrainerId == trainer.TrainerId);

        if (booking == null)
        {
            TempData["Error"] = "ไม่พบเซสชันที่ต้องการบันทึกผลการฝึก";
            return RedirectToAction("Index", new { email = trainer.User?.Email ?? email });
        }

        string resolvedPrimary = string.IsNullOrWhiteSpace(primaryMovement) ? "Strength & Conditioning" : primaryMovement.Trim();
        string resolvedSetsSummary = string.IsNullOrWhiteSpace(setsSummary) ? "3 Sets x 8-10 Reps" : setsSummary.Trim();
        string resolvedTopLoad = string.IsNullOrWhiteSpace(topLoad) ? "-" : topLoad.Trim();
        string cleanExercisesJson = "[]";

        if (!string.IsNullOrWhiteSpace(exercisesJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(exercisesJson);
                if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0)
                {
                    cleanExercisesJson = exercisesJson;
                    var names = new List<string>();
                    int totalSets = 0;
                    double totalVolume = 0;
                    double maxWeight = -1;
                    string maxWeightLabel = "-";

                    foreach (var ex in doc.RootElement.EnumerateArray())
                    {
                        string exName = ex.TryGetProperty("name", out var nEl) ? (nEl.GetString()?.Trim() ?? "") : "";
                        if (!string.IsNullOrWhiteSpace(exName)) names.Add(exName);

                        if (ex.TryGetProperty("sets", out var setsEl) && setsEl.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var st in setsEl.EnumerateArray())
                            {
                                totalSets++;
                                double w = st.TryGetProperty("weight", out var wEl) && wEl.TryGetDouble(out var wVal) ? wVal : 0;
                                int reps = st.TryGetProperty("reps", out var rEl) && rEl.TryGetInt32(out var rVal) ? rVal : 0;
                                totalVolume += w * reps;

                                if (w > maxWeight || (Math.Abs(w - maxWeight) < 0.01 && reps > 0))
                                {
                                    maxWeight = w;
                                    string shortEx = string.IsNullOrWhiteSpace(exName) ? "Set" : exName;
                                    maxWeightLabel = w > 0 ? $"{shortEx} {w:0.#}kg x {reps}" : $"{shortEx} {reps} reps";
                                }
                            }
                        }
                    }

                    if (names.Any())
                    {
                        resolvedPrimary = names.Count == 1 ? names[0] : $"{names[0]} (+{names.Count - 1} ท่า)";
                    }
                    if (totalSets > 0)
                    {
                        resolvedSetsSummary = totalVolume > 0
                            ? $"{names.Count} ท่า • {totalSets} เซ็ต • Vol {totalVolume:N0} kg"
                            : $"{names.Count} ท่า • {totalSets} เซ็ต";
                    }
                    if (maxWeight >= 0 && totalSets > 0)
                    {
                        resolvedTopLoad = maxWeightLabel;
                    }
                }
            }
            catch
            {
                // Fall back to string parameters if JSON parse fails
            }
        }

        UpsertWorkoutLog(new Dictionary<string, string>
        {
            ["BookingId"] = booking.BookingId.ToString(),
            ["TrainerId"] = trainer.TrainerId.ToString(),
            ["UserId"] = booking.UserId.ToString(),
            ["MemberName"] = booking.User?.FullName ?? "สมาชิก",
            ["BookingDate"] = booking.BookingDate.ToString("dd/MM/yyyy"),
            ["TimeRange"] = $"{booking.StartTime:HH:mm} - {booking.EndTime:HH:mm}",
            ["PodName"] = booking.Pod?.PodName ?? "-",
            ["PrimaryMovement"] = resolvedPrimary,
            ["SetsSummary"] = resolvedSetsSummary,
            ["TopLoad"] = resolvedTopLoad,
            ["Rpe"] = string.IsNullOrWhiteSpace(rpe) ? "8.0" : rpe.Trim(),
            ["FeedbackNote"] = string.IsNullOrWhiteSpace(feedbackNote) ? "-" : feedbackNote.Trim(),
            ["ExercisesJson"] = cleanExercisesJson,
            ["SavedAt"] = DateTime.Now.ToString("dd/MM/yyyy HH:mm")
        });

        if (completeNow && booking.Status != "Completed")
        {
            MarkBookingCompleted(booking);
        }

        TempData["Success"] = completeNow
            ? $"บันทึกผลการฝึกและจบการสอนคุณ {booking.User?.FullName} (#PG-{booking.BookingId:D5}) เรียบร้อยแล้ว"
            : $"บันทึกผลการฝึกของ {booking.User?.FullName} (#PG-{booking.BookingId:D5}) เรียบร้อยแล้ว";

        return RedirectToAction("Index", new { email = trainer.User?.Email ?? email, selectedBookingId = booking.BookingId });
    }

    // POST: /Trainer/CompleteSession (โค้ชกดบันทึกจบการสอน -> ปรับเป็น Completed)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult CompleteSession(int bookingId, string? email)
    {
        var trainer = GetCurrentTrainer(email);
        if (trainer == null) return RedirectToAction("Index", "Login");

        var booking = _db.Bookings
            .Include(b => b.User)
            .Include(b => b.CheckIns)
            .FirstOrDefault(b => b.BookingId == bookingId && b.TrainerId == trainer.TrainerId);

        if (booking == null)
        {
            TempData["Error"] = "ไม่พบคิวสอนที่ต้องการบันทึกจบคลาส";
            return RedirectToAction("Index", new { email = trainer.User?.Email ?? email });
        }

        MarkBookingCompleted(booking);

        TempData["Success"] = $"บันทึกจบการสอนคุณ {booking.User?.FullName} (#PG-{booking.BookingId:D5}) เรียบร้อยแล้ว";
        return RedirectToAction("Index", new { email = trainer.User?.Email ?? email });
    }

    // ================================================================
    // 2. ตารางของฉัน (MY WEEKLY SCHEDULE)
    // ================================================================
    public IActionResult Schedule(string? email, string? weekStart = null)
    {
        EnsureTrainersSeeded();
        var trainer = GetCurrentTrainer(email);
        if (trainer == null) return RedirectToAction("Index", "Login");

        SyncExpiredCheckInsForTrainer(trainer.TrainerId);
        SetTrainerViewBag(trainer);

        DateOnly baseDate = DateOnly.FromDateTime(DateTime.Today);
        if (!string.IsNullOrWhiteSpace(weekStart) && DateOnly.TryParse(weekStart, out var parsedDate))
        {
            baseDate = parsedDate;
        }

        int diffToMonday = ((int)baseDate.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        var monday = baseDate.AddDays(-diffToMonday);
        var sunday = monday.AddDays(6);

        var weekBookings = _db.Bookings
            .Include(b => b.User)
            .Include(b => b.Pod)
                .ThenInclude(p => p!.Branch)
            .Include(b => b.CheckIns)
            .Where(b => b.TrainerId == trainer.TrainerId &&
                        b.Status != "Cancelled" &&
                        b.BookingDate >= monday &&
                        b.BookingDate <= sunday)
            .OrderBy(b => b.BookingDate)
            .ThenBy(b => b.StartTime)
            .ToList();

        int branchId = trainer.User?.HomeBranchId ?? _db.Branches.Select(b => b.BranchId).FirstOrDefault();
        var branchPods = _db.Pods
            .Include(p => p.Branch)
            .Where(p => p.BranchId == branchId)
            .OrderBy(p => p.PodName)
            .ToList();

        var availability = ReadAvailability(trainer.TrainerId);
        double weeklyOpenHours = availability.WeeklyDays.Where(d => d.IsActive).Sum(d => d.HoursPerDay);
        double taughtHours = weekBookings.Sum(b => (b.EndTime.ToTimeSpan() - b.StartTime.ToTimeSpan()).TotalHours);
        int capacityPct = weeklyOpenHours > 0 ? Math.Min(100, (int)Math.Round(taughtHours * 100.0 / weeklyOpenHours)) : 0;

        ViewBag.Monday = monday;
        ViewBag.Sunday = sunday;
        ViewBag.PrevWeek = monday.AddDays(-7).ToString("yyyy-MM-dd");
        ViewBag.NextWeek = monday.AddDays(7).ToString("yyyy-MM-dd");
        ViewBag.BranchPods = branchPods;
        ViewBag.Availability = availability;
        ViewBag.WeeklyOpenHours = weeklyOpenHours;
        ViewBag.TaughtHours = taughtHours;
        ViewBag.CapacityPct = capacityPct;

        return View(weekBookings);
    }

    // ================================================================
    // 3. เวลาว่าง & สล็อตการสอน (AVAILABILITY)
    // ================================================================
    public IActionResult Availability(string? email)
    {
        EnsureTrainersSeeded();
        var trainer = GetCurrentTrainer(email);
        if (trainer == null) return RedirectToAction("Index", "Login");

        SetTrainerViewBag(trainer);
        var model = ReadAvailability(trainer.TrainerId);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult SaveWeeklyAvailability(string? email, int[]? activeDays, string[]? selectedSlots)
    {
        var trainer = GetCurrentTrainer(email);
        if (trainer == null) return RedirectToAction("Index", "Login");

        var config = ReadAvailability(trainer.TrainerId);
        var activeSet = (activeDays ?? Array.Empty<int>()).ToHashSet();
        var slotsByDay = new Dictionary<int, List<string>>();

        if (selectedSlots != null)
        {
            foreach (var item in selectedSlots)
            {
                var parts = item.Split('|', 2);
                if (parts.Length == 2 && int.TryParse(parts[0], out int dIdx) && ClubSlotStarts.Contains(parts[1]))
                {
                    if (!slotsByDay.ContainsKey(dIdx)) slotsByDay[dIdx] = new List<string>();
                    if (!slotsByDay[dIdx].Contains(parts[1])) slotsByDay[dIdx].Add(parts[1]);
                }
            }
        }

        for (int i = 0; i < config.WeeklyDays.Count; i++)
        {
            var day = config.WeeklyDays[i];
            day.IsActive = activeSet.Contains(i);
            day.EnabledSlots = slotsByDay.TryGetValue(i, out var daySlots)
                ? ClubSlotStarts.Where(s => daySlots.Contains(s)).ToList()
                : new List<string>();
        }

        WriteAvailability(trainer.TrainerId, config);
        TempData["Success"] = "บันทึกตารางเวลาว่างและสล็อตรับสอนประจำสัปดาห์เรียบร้อยแล้ว";
        return RedirectToAction("Availability", new { email = trainer.User?.Email ?? email });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult AddDateOverride(string? email, string blockDate, string blockType, string? timeWindow, string reason)
    {
        var trainer = GetCurrentTrainer(email);
        if (trainer == null) return RedirectToAction("Index", "Login");

        if (string.IsNullOrWhiteSpace(blockDate))
        {
            TempData["Error"] = "กรุณาเลือกวันที่ต้องการบล็อกเวลา";
            return RedirectToAction("Availability", new { email = trainer.User?.Email ?? email });
        }

        var config = ReadAvailability(trainer.TrainerId);
        config.Overrides.Insert(0, new AvailabilityOverride
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            Date = blockDate.Trim(),
            BlockType = string.IsNullOrWhiteSpace(blockType) ? "ทั้งวัน (Full Day)" : blockType.Trim(),
            TimeWindow = string.IsNullOrWhiteSpace(timeWindow) ? "ทุกสล็อต (08:00 - 20:45 น.)" : timeWindow.Trim(),
            Reason = string.IsNullOrWhiteSpace(reason) ? "ติดภารกิจส่วนตัว" : reason.Trim()
        });

        WriteAvailability(trainer.TrainerId, config);
        TempData["Success"] = $"บันทึกวันหยุด/บล็อกช่วงเวลาวันที่ {blockDate} เรียบร้อยแล้ว";
        return RedirectToAction("Availability", new { email = trainer.User?.Email ?? email });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult DeleteDateOverride(string? email, string overrideId)
    {
        var trainer = GetCurrentTrainer(email);
        if (trainer == null) return RedirectToAction("Index", "Login");

        var config = ReadAvailability(trainer.TrainerId);
        config.Overrides.RemoveAll(o => o.Id == overrideId);
        WriteAvailability(trainer.TrainerId, config);

        TempData["Success"] = "ลบรายการบล็อกเวลาเรียบร้อยแล้ว";
        return RedirectToAction("Availability", new { email = trainer.User?.Email ?? email });
    }

    // ================================================================
    // 4. ลูกค้าในการดูแล (MY CLIENTS & QUOTA TRACKING)
    // ================================================================
    public IActionResult Clients(string? email, string? search = "", string? statusFilter = "all")
    {
        EnsureTrainersSeeded();
        var trainer = GetCurrentTrainer(email);
        if (trainer == null) return RedirectToAction("Index", "Login");

        SetTrainerViewBag(trainer);

        var trainerBookings = _db.Bookings
            .Include(b => b.User)
                .ThenInclude(u => u!.HomeBranch)
            .Include(b => b.User)
                .ThenInclude(u => u!.MemberSubscriptions)
                    .ThenInclude(ms => ms.Package)
            .Include(b => b.Pod)
            .Include(b => b.CheckIns)
            .Where(b => b.TrainerId == trainer.TrainerId)
            .OrderByDescending(b => b.BookingDate)
            .ThenByDescending(b => b.StartTime)
            .ToList();

        var clients = trainerBookings
            .GroupBy(b => b.UserId)
            .Select(g => g.First().User)
            .Where(u => u != null)
            .Cast<User>()
            .ToList();

        if (!string.IsNullOrWhiteSpace(search))
        {
            string q = search.Trim().ToLower();
            clients = clients.Where(u =>
                (u.FullName?.ToLower().Contains(q) ?? false) ||
                (u.FitnessGoal?.ToLower().Contains(q) ?? false) ||
                (u.Phone?.ToLower().Contains(q) ?? false) ||
                ($"pg-{u.UserId:D5}".Contains(q))
            ).ToList();
        }

        if (statusFilter == "lowquota")
        {
            clients = clients.Where(u =>
            {
                var sub = u.MemberSubscriptions?.Where(ms => ms.Status == "Active").OrderByDescending(ms => ms.EndDate).FirstOrDefault();
                return sub == null || sub.RemainingPt <= 2;
            }).ToList();
        }

        int totalClients = trainerBookings.Select(b => b.UserId).Distinct().Count();
        int totalNonCancelled = trainerBookings.Count(b => b.Status != "Cancelled");
        int totalCompleted = trainerBookings.Count(b => b.Status == "Completed");
        int avgCompliance = totalNonCancelled > 0 ? (int)Math.Round(totalCompleted * 100.0 / totalNonCancelled) : 100;

        int quotaAtRisk = trainerBookings
            .Select(b => b.User)
            .Where(u => u != null)
            .GroupBy(u => u!.UserId)
            .Select(g => g.First()!)
            .Count(u =>
            {
                var sub = u.MemberSubscriptions?.Where(ms => ms.Status == "Active").OrderByDescending(ms => ms.EndDate).FirstOrDefault();
                return sub == null || sub.RemainingPt <= 2;
            });

        ViewBag.TrainerBookings = trainerBookings;
        ViewBag.WorkoutLogs = ReadWorkoutLogs(trainer.TrainerId);
        ViewBag.Search = search ?? "";
        ViewBag.StatusFilter = statusFilter ?? "all";
        ViewBag.TotalClients = totalClients;
        ViewBag.AvgCompliance = avgCompliance;
        ViewBag.QuotaAtRisk = quotaAtRisk;

        return View(clients);
    }

    // ================================================================
    // 5. ประวัติการสอนและค่าตอบแทน (TEACHING HISTORY & PAYOUT)
    // ================================================================
    public IActionResult History(string? email, string? filter = "all", string? search = "")
    {
        EnsureTrainersSeeded();
        var trainer = GetCurrentTrainer(email);
        if (trainer == null) return RedirectToAction("Index", "Login");

        SyncExpiredCheckInsForTrainer(trainer.TrainerId);
        SetTrainerViewBag(trainer);

        var today = DateOnly.FromDateTime(DateTime.Today);
        var nowTime = TimeOnly.FromDateTime(DateTime.Now);

        var pastBookings = _db.Bookings
            .Include(b => b.User)
            .Include(b => b.Pod)
                .ThenInclude(p => p!.Branch)
            .Include(b => b.CheckIns)
            .Where(b => b.TrainerId == trainer.TrainerId)
            .OrderByDescending(b => b.BookingDate)
            .ThenByDescending(b => b.StartTime)
            .ToList()
            .Where(b =>
                b.Status == "Completed" ||
                b.Status == "Cancelled" ||
                b.BookingDate < today ||
                (b.BookingDate == today && b.EndTime <= nowTime))
            .ToList();

        var completedList = pastBookings.Where(b => b.Status == "Completed").ToList();
        var noShowList = pastBookings.Where(b => b.Status == "Confirmed" && !(b.CheckIns?.Any() ?? false)).ToList();
        var cancelledList = pastBookings.Where(b => b.Status == "Cancelled").ToList();

        var displayList = (filter ?? "all").ToLower() switch
        {
            "completed" => completedList,
            "noshow" => noShowList,
            "cancelled" => cancelledList,
            _ => pastBookings
        };

        if (!string.IsNullOrWhiteSpace(search))
        {
            string q = search.Trim().ToLower();
            displayList = displayList.Where(b =>
                (b.User?.FullName?.ToLower().Contains(q) ?? false) ||
                (b.Pod?.PodName?.ToLower().Contains(q) ?? false) ||
                ($"#pg-{b.BookingId:D5}".Contains(q))
            ).ToList();
        }

        decimal ratePerSession = trainer.HourlyRate ?? 800m;
        decimal estPayout = completedList.Count * ratePerSession;
        int attendanceRate = pastBookings.Count > 0
            ? (int)Math.Round(completedList.Count * 100.0 / pastBookings.Count)
            : 100;

        ViewBag.Filter = filter ?? "all";
        ViewBag.Search = search ?? "";
        ViewBag.AllCount = pastBookings.Count;
        ViewBag.CompletedCount = completedList.Count;
        ViewBag.NoShowCount = noShowList.Count;
        ViewBag.CancelledCount = cancelledList.Count;
        ViewBag.RatePerSession = ratePerSession;
        ViewBag.EstPayout = estPayout;
        ViewBag.AttendanceRate = attendanceRate;
        ViewBag.WorkoutLogs = ReadWorkoutLogs(trainer.TrainerId);

        return View(displayList);
    }

    [HttpGet]
    // ส่งออกประวัติการสอนและค่าตอบแทนของโค้ชเป็นไฟล์ CSV
    public IActionResult ExportHistoryCsv(string? email)
    {
        var trainer = GetCurrentTrainer(email);
        if (trainer == null) return RedirectToAction("Index", "Login");

        var bookings = _db.Bookings
            .Include(b => b.User)
            .Include(b => b.Pod)
            .Where(b => b.TrainerId == trainer.TrainerId)
            .OrderByDescending(b => b.BookingDate)
            .ThenByDescending(b => b.StartTime)
            .ToList();

        decimal rate = trainer.HourlyRate ?? 800m;
        var sb = new StringBuilder();
        sb.AppendLine("BookingCode,Date,StartTime,EndTime,MemberName,PodName,Status,PayoutTHB");
        foreach (var b in bookings)
        {
            decimal payout = b.Status == "Completed" ? rate : 0m;
            string member = (b.User?.FullName ?? "-").Replace(",", " ");
            string pod = (b.Pod?.PodName ?? "-").Replace(",", " ");
            sb.AppendLine($"#PG-{b.BookingId:D5},{b.BookingDate:yyyy-MM-dd},{b.StartTime:HH:mm},{b.EndTime:HH:mm},{member},{pod},{b.Status},{payout:F0}");
        }

        byte[] bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        return File(bytes, "text/csv", $"trainer-history-{DateTime.Today:yyyyMMdd}.csv");
    }

    // ================================================================
    // 6. โปรไฟล์โค้ช (TRAINER PROFILE)
    // ================================================================
    public IActionResult Profile(string? email)
    {
        EnsureTrainersSeeded();
        var trainer = GetCurrentTrainer(email);
        if (trainer == null) return RedirectToAction("Index", "Login");

        SetTrainerViewBag(trainer);

        var trainerBookings = _db.Bookings
            .Where(b => b.TrainerId == trainer.TrainerId)
            .ToList();

        int completedSessions = trainerBookings.Count(b => b.Status == "Completed");
        int totalClients = trainerBookings.Select(b => b.UserId).Distinct().Count();
        int nonCancelled = trainerBookings.Count(b => b.Status != "Cancelled");
        int attendanceRate = nonCancelled > 0 ? (int)Math.Round(completedSessions * 100.0 / nonCancelled) : 100;

        string key = ((trainer.User?.Email ?? "") + " " + (trainer.User?.FullName ?? "")).ToLower();
        string avatarPath = key.Contains("ken") ? "/images/trainer-ken.jpg"
            : key.Contains("mew") ? "/images/trainer-mew.jpg"
            : key.Contains("sarah") ? "/images/trainer-sarah.jpg"
            : "/images/trainer-alex.jpg";

        ViewBag.CompletedSessions = completedSessions;
        ViewBag.TotalClients = totalClients;
        ViewBag.AttendanceRate = attendanceRate;
        ViewBag.AvatarPath = avatarPath;
        ViewBag.Credentials = ReadCredentials(trainer.TrainerId);

        return View(trainer);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult UpdateProfile(int trainerId, string? email, string? fullName, string? phone, string? specialty, string? bio, string? credentialsJson)
    {
        var trainer = _db.Trainers
            .Include(t => t.User)
            .FirstOrDefault(t => t.TrainerId == trainerId);

        if (trainer != null)
        {
            if (trainer.User != null)
            {
                if (!string.IsNullOrWhiteSpace(fullName))
                {
                    trainer.User.FullName = fullName.Trim();
                }
                if (!string.IsNullOrWhiteSpace(phone))
                {
                    trainer.User.Phone = phone.Trim();
                }
            }
            trainer.Specialty = specialty?.Trim();
            trainer.Bio = bio?.Trim();
            _db.SaveChanges();

            if (!string.IsNullOrWhiteSpace(credentialsJson))
            {
                WriteCredentials(trainer.TrainerId, credentialsJson);
            }

            TempData["Success"] = "บันทึกข้อมูลโปรไฟล์และความเชี่ยวชาญโค้ชเรียบร้อยแล้ว";
            return RedirectToAction("Profile", new { email = trainer.User?.Email ?? email });
        }

        TempData["Error"] = "ไม่พบข้อมูลโค้ชในระบบ";
        return RedirectToAction("Profile", new { email });
    }

    private static string GetCredentialsPath(int trainerId)
    {
        string dir = Path.Combine(Directory.GetCurrentDirectory(), "App_Data");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, $"trainer_credentials_{trainerId}.json");
    }

    private static List<string> ReadCredentials(int trainerId)
    {
        try
        {
            string path = GetCredentialsPath(trainerId);
            if (System.IO.File.Exists(path))
            {
                var list = JsonSerializer.Deserialize<List<string>>(System.IO.File.ReadAllText(path));
                if (list != null) return list;
            }
        }
        catch
        {
            // Fall through to defaults
        }

        return new List<string>
        {
            "NSCA - CSCS (Certified Strength & Conditioning Specialist)",
            "ACE / NASM Certified Personal Trainer",
            "B.Sc. Sports Science (วิทยาศาสตร์การกีฬา)"
        };
    }

    private static void WriteCredentials(int trainerId, string credentialsJson)
    {
        try
        {
            var list = JsonSerializer.Deserialize<List<string>>(credentialsJson);
            if (list != null)
            {
                var cleaned = list.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Take(12).ToList();
                System.IO.File.WriteAllText(GetCredentialsPath(trainerId), JsonSerializer.Serialize(cleaned));
            }
        }
        catch
        {
            // Ignore file I/O failure
        }
    }

    // ================================================================
    // HELPER FUNCTIONS
    // ================================================================
    private void MarkBookingCompleted(Booking booking)
    {
        var activeCi = booking.CheckIns.OrderByDescending(c => c.CheckInId).FirstOrDefault();
        if (activeCi == null)
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
        else if (activeCi.CheckOutTime == null)
        {
            activeCi.CheckOutTime = DateTime.Now;
        }

        booking.Status = "Completed";
        _db.SaveChanges();
    }

    private void EnsureTrainersSeeded()
    {
        Csi402dbContext.EnsureAllBranchesPodsAndTrainersSeeded(_db);
    }

    private Trainer? GetCurrentTrainer(string? email)
    {
        if (!string.IsNullOrWhiteSpace(email))
        {
            string clean = email.Trim();
            var matched = _db.Trainers
                .Include(t => t.User)
                    .ThenInclude(u => u!.HomeBranch)
                .FirstOrDefault(t => t.User != null && (t.User.Email == clean || t.User.Phone == clean));
            if (matched != null) return matched;

            var trainerUser = _db.Users
                .Include(u => u.HomeBranch)
                .FirstOrDefault(u => (u.Email == clean || u.Phone == clean) && u.RoleId == 3);
            if (trainerUser != null)
            {
                var newTrainer = new Trainer
                {
                    UserId = trainerUser.UserId,
                    Specialty = "Strength & Conditioning",
                    Bio = "Personal Trainer ประจำคลับ PrivateGym",
                    HourlyRate = 1500m
                };
                _db.Trainers.Add(newTrainer);
                _db.SaveChanges();
                newTrainer.User = trainerUser;
                return newTrainer;
            }
        }

        return _db.Trainers
            .Include(t => t.User)
                .ThenInclude(u => u!.HomeBranch)
            .OrderBy(t => t.TrainerId)
            .FirstOrDefault();
    }

    private void SyncExpiredCheckInsForTrainer(int trainerId)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var nowTime = TimeOnly.FromDateTime(DateTime.Now);

        var expired = _db.Bookings
            .Include(b => b.CheckIns)
            .Where(b => b.TrainerId == trainerId &&
                        b.Status == "Confirmed" &&
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

    private void SetTrainerViewBag(Trainer trainer)
    {
        ViewBag.Email = trainer.User?.Email ?? "alex@privategym.club";
        ViewBag.FullName = trainer.User?.FullName ?? "Coach PrivateGym";
        ViewBag.Specialty = trainer.Specialty ?? "Strength & Conditioning";
        ViewData["FullName"] = ViewBag.FullName;
    }

    // ---- JSON Persistence for Workout Logs ----
    private static string GetWorkoutLogPath()
    {
        string dir = Path.Combine(Directory.GetCurrentDirectory(), "App_Data");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "trainer_workout_logs.json");
    }

    private static List<Dictionary<string, string>> ReadWorkoutLogs(int trainerId)
    {
        try
        {
            string path = GetWorkoutLogPath();
            if (!System.IO.File.Exists(path)) return new List<Dictionary<string, string>>();
            string json = System.IO.File.ReadAllText(path);
            var all = JsonSerializer.Deserialize<List<Dictionary<string, string>>>(json)
                      ?? new List<Dictionary<string, string>>();
            return all.Where(x => x.GetValueOrDefault("TrainerId") == trainerId.ToString()).ToList();
        }
        catch
        {
            return new List<Dictionary<string, string>>();
        }
    }

    public static Dictionary<int, Dictionary<string, string>> ReadWorkoutLogsByBookingForUser(int userId)
    {
        var map = new Dictionary<int, Dictionary<string, string>>();
        try
        {
            string path = GetWorkoutLogPath();
            if (!System.IO.File.Exists(path)) return map;
            string json = System.IO.File.ReadAllText(path);
            var all = JsonSerializer.Deserialize<List<Dictionary<string, string>>>(json)
                      ?? new List<Dictionary<string, string>>();
            foreach (var entry in all.Where(x => x.GetValueOrDefault("UserId") == userId.ToString()))
            {
                if (int.TryParse(entry.GetValueOrDefault("BookingId"), out int bid) && !map.ContainsKey(bid))
                {
                    map[bid] = entry;
                }
            }
        }
        catch
        {
            // Return empty map on read error
        }
        return map;
    }

    private static void UpsertWorkoutLog(Dictionary<string, string> entry)
    {
        try
        {
            string path = GetWorkoutLogPath();
            var all = new List<Dictionary<string, string>>();
            if (System.IO.File.Exists(path))
            {
                all = JsonSerializer.Deserialize<List<Dictionary<string, string>>>(System.IO.File.ReadAllText(path))
                      ?? new List<Dictionary<string, string>>();
            }

            string bookingId = entry.GetValueOrDefault("BookingId", "");
            all.RemoveAll(x => x.GetValueOrDefault("BookingId") == bookingId);
            all.Insert(0, entry);
            if (all.Count > 200) all = all.Take(200).ToList();
            System.IO.File.WriteAllText(path, JsonSerializer.Serialize(all));
        }
        catch
        {
            // Ignore file I/O failure so core DB action succeeds
        }
    }

    // ---- JSON Persistence for Availability (6 Club Slots) ----
    public static readonly string[] ClubSlotStarts = new[] { "08:00", "09:30", "11:00", "14:00", "18:00", "19:30" };

    public static readonly Dictionary<string, string> ClubSlotLabels = new()
    {
        ["08:00"] = "08:00 - 09:15",
        ["09:30"] = "09:30 - 10:45",
        ["11:00"] = "11:00 - 12:15",
        ["14:00"] = "14:00 - 15:15",
        ["18:00"] = "18:00 - 19:15",
        ["19:30"] = "19:30 - 20:45"
    };

    public class AvailabilityDay
    {
        public string DayName { get; set; } = "";
        public string ShortName { get; set; } = "";
        public bool IsActive { get; set; } = true;
        public List<string> EnabledSlots { get; set; } = new();
        public double HoursPerDay => IsActive ? Math.Round(EnabledSlots.Count * 1.25, 1) : 0;
    }

    public class AvailabilityOverride
    {
        public string Id { get; set; } = "";
        public string Date { get; set; } = "";
        public string BlockType { get; set; } = "";
        public string TimeWindow { get; set; } = "";
        public string Reason { get; set; } = "";
    }

    public class TrainerAvailabilityConfig
    {
        public int TrainerId { get; set; }
        public List<AvailabilityDay> WeeklyDays { get; set; } = new();
        public List<AvailabilityOverride> Overrides { get; set; } = new();
    }

    private static string GetAvailabilityPath(int trainerId)
    {
        string dir = Path.Combine(Directory.GetCurrentDirectory(), "App_Data");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, $"trainer_availability_{trainerId}.json");
    }

    public static TrainerAvailabilityConfig ReadAvailability(int trainerId)
    {
        try
        {
            string path = GetAvailabilityPath(trainerId);
            if (System.IO.File.Exists(path))
            {
                var cfg = JsonSerializer.Deserialize<TrainerAvailabilityConfig>(System.IO.File.ReadAllText(path));
                if (cfg != null && cfg.WeeklyDays.Count == 7)
                {
                    // Ensure legacy files without EnabledSlots get migrated cleanly
                    foreach (var d in cfg.WeeklyDays)
                    {
                        d.EnabledSlots ??= new List<string>();
                    }
                    return cfg;
                }
            }
        }
        catch
        {
            // Fall through to default
        }

        var allSlots = ClubSlotStarts.ToList();
        return new TrainerAvailabilityConfig
        {
            TrainerId = trainerId,
            WeeklyDays = new List<AvailabilityDay>
            {
                new() { DayName = "วันจันทร์", ShortName = "จ.", IsActive = true, EnabledSlots = new List<string>(allSlots) },
                new() { DayName = "วันอังคาร", ShortName = "อ.", IsActive = true, EnabledSlots = new List<string>(allSlots) },
                new() { DayName = "วันพุธ", ShortName = "พ.", IsActive = true, EnabledSlots = new List<string>(allSlots) },
                new() { DayName = "วันพฤหัสบดี", ShortName = "พฤ.", IsActive = true, EnabledSlots = new List<string>(allSlots) },
                new() { DayName = "วันศุกร์", ShortName = "ศ.", IsActive = true, EnabledSlots = new List<string>(allSlots) },
                new() { DayName = "วันเสาร์", ShortName = "ส.", IsActive = true, EnabledSlots = new List<string>(allSlots) },
                new() { DayName = "วันอาทิตย์", ShortName = "อา.", IsActive = false, EnabledSlots = new List<string>() }
            },
            Overrides = new List<AvailabilityOverride>()
        };
    }

    public static HashSet<string> GetUnavailableSlotsForTrainer(int trainerId, DateOnly date)
    {
        var unavailable = new HashSet<string>();
        var cfg = ReadAvailability(trainerId);

        int dayIdx = ((int)date.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        if (dayIdx >= 0 && dayIdx < cfg.WeeklyDays.Count)
        {
            var day = cfg.WeeklyDays[dayIdx];
            if (!day.IsActive)
            {
                foreach (var s in ClubSlotStarts) unavailable.Add(s);
                return unavailable;
            }
            foreach (var s in ClubSlotStarts)
            {
                if (!day.EnabledSlots.Contains(s)) unavailable.Add(s);
            }
        }

        string dateStr = date.ToString("yyyy-MM-dd");
        foreach (var ov in cfg.Overrides.Where(o => o.Date == dateStr))
        {
            if (ov.BlockType.Contains("ทั้งวัน"))
            {
                foreach (var s in ClubSlotStarts) unavailable.Add(s);
            }
            else
            {
                foreach (var s in ClubSlotStarts)
                {
                    if (ov.TimeWindow.Contains(s)) unavailable.Add(s);
                }
            }
        }

        return unavailable;
    }

    private static void WriteAvailability(int trainerId, TrainerAvailabilityConfig cfg)
    {
        try
        {
            cfg.TrainerId = trainerId;
            System.IO.File.WriteAllText(GetAvailabilityPath(trainerId), JsonSerializer.Serialize(cfg));
        }
        catch
        {
            // Ignore file I/O failure
        }
    }
}
