using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PrivateGym.Models;

namespace PrivateGym.Controllers;

// [HomeController] โซนหลัก:
// 1) หน้าเว็บสาธารณะ: Index, Packages, Facilities, Trainers, Contact
// 2) ฟอร์มจองทดลองเล่น: BookTrial
public class HomeController : Controller
{
    private readonly Csi402dbContext _db; // ตัวเชื่อมต่อฐานข้อมูล MySQL ผ่าน EF Core

    public HomeController(Csi402dbContext db)
    {
        _db = db;
    }

    // หน้าแรกเว็บไซต์ (Landing Page): โหลดแพ็กเกจและสาขาจาก DB
    public IActionResult Index()
    {
        ViewBag.Packages = _db.Packages.OrderBy(p => p.Price).ToList();
        ViewBag.Branches = _db.Branches.ToList();
        return View();
    }

    // 1. หน้าแพ็กเกจและราคาสมาชิก (Packages & Pricing)
    public IActionResult Packages()
    {
        var packages = _db.Packages.OrderBy(p => p.Price).ToList();
        return View(packages);
    }

    // 2. หน้าสิ่งอำนวยความสะดวกและห้องซ้อมส่วนตัว (Facilities & Private Pods)
    public IActionResult Facilities()
    {
        Csi402dbContext.EnsureAllBranchesPodsAndTrainersSeeded(_db);
        var pods = _db.Pods
            .Include(p => p.Branch)
            .OrderBy(p => p.PodId)
            .ToList();
        ViewBag.Branches = _db.Branches.OrderBy(b => b.BranchId).ToList();
        return View(pods);
    }

    // 3. หน้าทีมโค้ชผู้ฝึกสอน (Trainers & Coaches)
    public IActionResult Trainers()
    {
        Csi402dbContext.EnsureAllBranchesPodsAndTrainersSeeded(_db);
        var trainers = _db.Trainers
            .Include(t => t.User)
                .ThenInclude(u => u.HomeBranch)
            .OrderBy(t => t.TrainerId)
            .ToList();
        ViewBag.Branches = _db.Branches.OrderBy(b => b.BranchId).ToList();
        return View(trainers);
    }

    // 4. หน้าสาขาและติดต่อสอบถาม (Contact & Location)
    public IActionResult Contact()
    {
        var branches = _db.Branches.ToList();
        return View(branches);
    }

    // POST: /Home/BookTrial (รับฟอร์มจองทดลองเล่นจากหน้าแรก / หน้า Contact)
    [HttpPost]
    public IActionResult BookTrial(string fullName, string phone, string email, string branch, string? returnAction = "Index")
    {
        TempData["TrialSuccess"] = $"รับคำขอจองรอบทดลองฝึกของคุณ {fullName} ({branch}) เรียบร้อยแล้ว! ทีมงานจะติดต่อกลับทางเบอร์ {phone} เพื่อยืนยันเวลาครับ";
        if (returnAction == "Contact")
        {
            return RedirectToAction("Contact");
        }
        return RedirectToAction("Index");
    }

    public IActionResult Privacy()
    {
        return View();
    }
}