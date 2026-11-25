using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PrivateGym.Models;
using PrivateGym.Services;
using PrivateGym.ViewModels;
namespace PrivateGym.Controllers;
// [LoginController] โซนหลัก:
// 1) หน้าเข้าสู่ระบบ: Index (GET)
// 2) ตรวจสอบสิทธิ์, บล็อกบัญชีถูกระงับ และแยก Redirect ตาม RoleId: Login (POST), Logout
public class LoginController : Controller
{
    private readonly Csi402dbContext _db; // ตัวเชื่อมต่อฐานข้อมูล MySQL ผ่าน EF Core
    public LoginController(Csi402dbContext db)
    {
        _db = db;
    }

    // แสดงหน้าเข้าสู่ระบบ (รองรับรับค่า prefill จากหน้า Register)
    [HttpGet]
    public IActionResult Index(string? email, string? password, 
    string? fullName, string? membershipPackage, string? homeBranch)
    {
        var model = new LoginViewModel
        {
            Email = email ?? "",
            Password = password ?? "",
            FullName = fullName ?? "",
            MembershipPackage = membershipPackage ?? "",
            HomeBranch = homeBranch ?? ""
        };

        return View(model);
    }

    // ตรวจสอบอีเมล/รหัสผ่าน, สถานะบัญชี และแยก Redirect ไปยัง Portal ของแต่ละ Role
    [HttpPost]
    public IActionResult Login(LoginViewModel data)
    {
        if (string.IsNullOrWhiteSpace(data.Email))
        {
            ViewBag.Error = "กรุณากรอกอีเมลหรือหมายเลขโทรศัพท์";
            return View("Index", data);
        }

        if (string.IsNullOrWhiteSpace(data.Password))
        {
            ViewBag.Error = "กรุณากรอกรหัสผ่าน";
            return View("Index", data);
        }

        string inputIdentifier = data.Email.Trim();
        EnsureAccountantSeeded();

        var user = _db.Users
                      .Include(u => u.HomeBranch)
                      .Include(u => u.Role)
                      .FirstOrDefault(u => u.Email == inputIdentifier || u.Phone == inputIdentifier);

        if (user == null)
        {
            ViewBag.Error = "ไม่พบบัญชีผู้ใช้นี้ในระบบ กรุณาตรวจสอบอีเมลหรือหมายเลขโทรศัพท์";
            return View("Index", data);
        }

        if (!PasswordHelper.VerifyPassword(user, data.Password, out bool needsRehash))
        {
            ViewBag.Error = "รหัสผ่านไม่ถูกต้อง";
            return View("Index", data);
        }

        if (needsRehash)
        {
            user.PasswordHash = PasswordHelper.HashPassword(user, data.Password);
            _db.SaveChanges();
        }

        HttpContext.Session.SetInt32("UserId", user.UserId);
        HttpContext.Session.SetInt32("RoleId", user.RoleId);
        HttpContext.Session.SetString("UserEmail", user.Email);

        if (user.Status == "Inactive" || user.Status == "Suspended" || user.Status == "โดนลบแล้ว")
        {
            ViewBag.Error = "บัญชีนี้ถูกระงับหรือปิดการใช้งาน กรุณาติดต่อผู้ดูแลระบบ";
            return View("Index", data);
        }

        var subscription = _db.MemberSubscriptions
                              .Include(s => s.Package)
                              .Where(s => s.UserId == user.UserId && s.Status == "Active")
                              .OrderByDescending(s => s.SubscriptionId)
                              .FirstOrDefault();

        string membershipPackage = subscription?.Package?.PackageName ?? "Starter Experience";
        string branchName = user.HomeBranch?.BranchName ?? "สาขาทองหล่อ";
        if (user.RoleId == 1) 
        {
            return RedirectToAction("Index", "Admin");
        }
        else if (user.RoleId == 2) 
        {
            return RedirectToAction("Index", "Staff", new { email = user.Email });
        }
        else if (user.RoleId == 3) 
        {
            return RedirectToAction("Index", "Trainer", new { email = user.Email });
        }
        else if (user.RoleId == 5 || string.Equals(user.Role?.RoleName, "Accountant", StringComparison.OrdinalIgnoreCase))
        {
            return RedirectToAction("Index", "Accountant", new { email = user.Email });
        }

        return RedirectToAction("Index", "Customer", new
        {
            email = user.Email,
            fullName = user.FullName,
            membershipPackage = membershipPackage,
            homeBranch = branchName
        });
    }
    [HttpGet]
    public IActionResult Logout()
    {
        return RedirectToAction("Index", "Home");
    }

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
}
