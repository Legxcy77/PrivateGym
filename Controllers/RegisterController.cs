using Microsoft.AspNetCore.Mvc;
using PrivateGym.Models;
using PrivateGym.Services;
using PrivateGym.ViewModels;

namespace PrivateGym.Controllers;

// [RegisterController] โซนหลัก:
// 1) ฟอร์มสมัครสมาชิก: Index (GET), LoadRegisterOptions
// 2) บันทึกสมาชิกใหม่ + เปิดสัญญาแพ็กเกจ + ออกบิล Payment: Register (POST)
public class RegisterController : Controller
{
    private readonly Csi402dbContext _db; // ตัวเชื่อมต่อฐานข้อมูล MySQL ผ่าน EF Core
    public RegisterController(Csi402dbContext db)
    {
        _db = db;
    }

    // โหลดรายการแพ็กเกจและสาขาจากฐานข้อมูลไปแสดงในหน้าสมัครสมาชิก
    private void LoadRegisterOptions()
    {
        ViewBag.Packages = _db.Packages.OrderBy(p => p.Price).ToList();
        ViewBag.Branches = _db.Branches.OrderBy(b => b.BranchId).ToList();
    }

    // แสดงหน้าฟอร์มสมัครสมาชิกใหม่
    [HttpGet]
    public IActionResult Index()
    {
        LoadRegisterOptions();
        return View();
    }

    // บันทึกสมาชิกใหม่, เปิดสัญญาแพ็กเกจเริ่มต้น (MemberSubscription) และออกบิล Payment
    [HttpPost]
    public IActionResult Register(RegisterViewModel data)
    {
        LoadRegisterOptions();

        if (string.IsNullOrWhiteSpace(data.FullName))
        {
            ViewBag.Error = "กรุณากรอกชื่อ-นามสกุล";
            return View("Index", data);
        }

        if (string.IsNullOrWhiteSpace(data.Email))
        {
            ViewBag.Error = "กรุณากรอกอีเมล";
            return View("Index", data);
        }

        if (string.IsNullOrWhiteSpace(data.Phone))
        {
            ViewBag.Error = "กรุณากรอกหมายเลขโทรศัพท์";
            return View("Index", data);
        }

        if (string.IsNullOrWhiteSpace(data.Password))
        {
            ViewBag.Error = "กรุณากรอกรหัสผ่าน";
            return View("Index", data);
        }

        bool emailExists = _db.Users.Any(u => u.Email == data.Email);
        if (emailExists)
        {
            ViewBag.Error = "อีเมลนี้ถูกใช้งานในระบบแล้ว กรุณาใช้อีเมลอื่นหรือเข้าสู่ระบบ";
            return View("Index", data);
        }

        bool phoneExists = _db.Users.Any(u => u.Phone == data.Phone);
        if (phoneExists)
        {
            ViewBag.Error = "หมายเลขโทรศัพท์นี้ถูกใช้งานในระบบแล้ว";
            return View("Index", data);
        }

        int? homeBranchId = null;
        if (!string.IsNullOrWhiteSpace(data.HomeBranch))
        {
            string bInput = data.HomeBranch.Trim();
            var exactBranch = _db.Branches.FirstOrDefault(b => b.BranchName == bInput || b.BranchName.Contains(bInput));
            if (exactBranch != null)
            {
                homeBranchId = exactBranch.BranchId;
            }
            else
            {
                string bLower = bInput.ToLower();
                if (bLower.Contains("thonglor") || bLower.Contains("ทองหล่อ"))
                    homeBranchId = _db.Branches.FirstOrDefault(b => b.BranchName.Contains("ทองหล่อ"))?.BranchId;
                else if (bLower.Contains("sathorn") || bLower.Contains("สาทร"))
                    homeBranchId = _db.Branches.FirstOrDefault(b => b.BranchName.Contains("สาทร"))?.BranchId;
                else if (bLower.Contains("sukhumvit") || bLower.Contains("สุขุมวิท"))
                    homeBranchId = _db.Branches.FirstOrDefault(b => b.BranchName.Contains("สุขุมวิท"))?.BranchId;
                else if (bLower.Contains("ari") || bLower.Contains("อารีย์"))
                    homeBranchId = _db.Branches.FirstOrDefault(b => b.BranchName.Contains("อารีย์"))?.BranchId;
            }
        }
        homeBranchId ??= _db.Branches.FirstOrDefault()?.BranchId;
        var memberRole = _db.Roles.FirstOrDefault(r => r.RoleName == "Member");
        int roleId = memberRole != null ? memberRole.RoleId : 4;
        var newUser = new User
        {
            FullName = data.FullName,
            Email = data.Email,
            Phone = data.Phone,
            PasswordHash = PasswordHelper.HashPassword(new User(), data.Password),
            Gender = data.Gender,
            Height = data.Height,
            Weight = data.Weight,
            ExperienceLevel = data.ExperienceLevel,
            FitnessGoal = data.FitnessGoal,
            TaxId = data.TaxId,
            BillingAddress = data.BillingAddress,
            RoleId = roleId,
            HomeBranchId = homeBranchId,
            Status = "Active",
            CreatedAt = DateTime.Now
        };
        _db.Users.Add(newUser);
        _db.SaveChanges();

        Package? package = null;
        string pkgRaw = (data.MembershipPackage ?? "").Trim();
        if (!string.IsNullOrEmpty(pkgRaw))
        {
            package = _db.Packages.FirstOrDefault(p => p.PackageName == pkgRaw);
        }
        if (package == null)
        {
            string pkgInput = pkgRaw.ToLower();
            if (pkgInput.Contains("starter"))
                package = _db.Packages.FirstOrDefault(p => p.PackageName.Contains("Starter") || p.PackageId == 1);
            else if (pkgInput.Contains("pro") || pkgInput.Contains("performance") || pkgInput.Contains("athlete"))
                package = _db.Packages.FirstOrDefault(p => p.PackageName.Contains("Pro") || p.PackageId == 2);
            else if (pkgInput.Contains("elite") || pkgInput.Contains("vip") || pkgInput.Contains("recovery"))
                package = _db.Packages.FirstOrDefault(p => p.PackageName.Contains("Elite") || p.PackageId == 3);
        }

        package ??= _db.Packages.FirstOrDefault(p => p.PackageId == 2) ?? _db.Packages.FirstOrDefault();

        if (package != null)
        {
            var subscription = new MemberSubscription
            {
                UserId = newUser.UserId,
                PackageId = package.PackageId,
                StartDate = DateOnly.FromDateTime(DateTime.Today),
                EndDate = DateOnly.FromDateTime(DateTime.Today.AddDays(package.DurationDays > 0 ? package.DurationDays : 30)),
                RemainingPods = package.PodQuota,
                RemainingPt = package.PtQuota,
                RemainingInBody = package.InBodyQuota,
                Status = "Active"
            };
            _db.MemberSubscriptions.Add(subscription);

            string paymentMethod = !string.IsNullOrWhiteSpace(data.PaymentMethod) ? data.PaymentMethod : "PromptPay";
            string slipRef = !string.IsNullOrWhiteSpace(data.TransactionRef) 
                ? data.TransactionRef 
                : (!string.IsNullOrWhiteSpace(data.BankTransferRef) ? data.BankTransferRef : "REG-" + Guid.NewGuid().ToString("N")[..8].ToUpper());

            var payment = new Payment
            {
                UserId = newUser.UserId,
                PackageId = package.PackageId,
                Amount = package.Price,
                PaymentMethod = paymentMethod,
                PaymentDate = DateTime.Now,
                Status = "Success",
                SlipReference = slipRef
            };
            _db.Payments.Add(payment);

            _db.SaveChanges();
        }

        return RedirectToAction("Index", "Login", new { 
            email = data.Email,
            password = data.Password, 
            fullName = data.FullName,
            membershipPackage = data.MembershipPackage,
            homeBranch = data.HomeBranch 
        });
    }
}
