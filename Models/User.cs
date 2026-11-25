using System;
using System.Collections.Generic;

namespace PrivateGym.Models;

public partial class User
{
    public int UserId { get; set; } // รหัสผู้ใช้งาน (PK)

    public string FullName { get; set; } = null!; // ชื่อ-นามสกุล

    public string Email { get; set; } = null!; // อีเมล (ใช้เข้าสู่ระบบ)

    public string Phone { get; set; } = null!; // เบอร์โทรศัพท์

    public string PasswordHash { get; set; } = null!; // รหัสผ่าน

    public string? Gender { get; set; } // เพศ

    public decimal? Height { get; set; } // ส่วนสูง (ซม.)

    public decimal? Weight { get; set; } // น้ำหนัก (กก.)

    public string? ExperienceLevel { get; set; } // ระดับประสบการณ์ฝึกซ้อม

    public string? FitnessGoal { get; set; } // เป้าหมายการออกกำลังกาย

    public string? TaxId { get; set; } // เลขประจำตัวผู้เสียภาษี (สำหรับออกใบกำกับภาษี)

    public string? BillingAddress { get; set; } // ที่อยู่สำหรับออกใบเสร็จ

    public int RoleId { get; set; } // รหัสบทบาทผู้ใช้ (FK -> Roles: 1=Admin, 2=Staff, 3=Trainer, 4=Member, 5=Accountant)

    public int? HomeBranchId { get; set; } // รหัสสาขาประจำ (FK -> Branches)

    public DateTime? CreatedAt { get; set; } // วันที่สร้างบัญชี

    public string? Status { get; set; } = "Active"; // สถานะบัญชี (Active, Inactive, Suspended)

    public virtual ICollection<Booking> Bookings { get; set; } = new List<Booking>(); // รายการจองห้องซ้อมของผู้ใช้

    public virtual ICollection<CheckIn> CheckIns { get; set; } = new List<CheckIn>(); // ประวัติการเช็กอินเข้า-ออก

    public virtual Branch? HomeBranch { get; set; } // ข้อมูลสาขาประจำ

    public virtual ICollection<MemberSubscription> MemberSubscriptions { get; set; } = new List<MemberSubscription>(); // ประวัติสัญญาแพ็กเกจสมาชิก

    public virtual ICollection<Payment> Payments { get; set; } = new List<Payment>(); // ประวัติการชำระเงิน

    public virtual Role Role { get; set; } = null!; // ข้อมูลบทบาทสิทธิ์การใช้งาน

    public virtual Trainer? Trainer { get; set; } // ข้อมูลโปรไฟล์เทรนเนอร์ (กรณีเป็น Role Trainer)
}
