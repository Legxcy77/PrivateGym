using System;
using System.Collections.Generic;

namespace PrivateGym.Models;

public partial class Package
{
    public int PackageId { get; set; } // รหัสแพ็กเกจ (PK)

    public string PackageName { get; set; } = null!; // ชื่อแพ็กเกจสมาชิก

    public decimal Price { get; set; } // ราคาแพ็กเกจ (บาท)

    public int DurationDays { get; set; } // จำนวนวันใช้งาน (เช่น 30 วัน)

    public int PodQuota { get; set; } // จำนวนโควต้าห้อง Pod ที่ได้รับ

    public int PtQuota { get; set; } // จำนวนโควต้าเทรนเนอร์ (PT) ที่ได้รับ

    public int InBodyQuota { get; set; } // จำนวนโควต้าตรวจ InBody ที่ได้รับ

    public bool? IsVip { get; set; } // สิทธิ์ระดับ VIP (true = ไม่จำกัดรอบจอง)

    public string? Description { get; set; } // รายละเอียดสิทธิ์ของแพ็กเกจ

    public virtual ICollection<MemberSubscription> MemberSubscriptions { get; set; } = new List<MemberSubscription>(); // สัญญาที่ใช้งานแพ็กเกจนี้

    public virtual ICollection<Payment> Payments { get; set; } = new List<Payment>(); // ประวัติการชำระเงินของแพ็กเกจนี้
}
