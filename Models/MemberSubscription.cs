using System;
using System.Collections.Generic;

namespace PrivateGym.Models;

public partial class MemberSubscription
{
    public int SubscriptionId { get; set; } // รหัสสัญญาแพ็กเกจ (PK)

    public int UserId { get; set; } // รหัสสมาชิก (FK -> Users)

    public int PackageId { get; set; } // รหัสแพ็กเกจที่ถือครอง (FK -> Packages)

    public DateOnly StartDate { get; set; } // วันเริ่มต้นใช้งานแพ็กเกจ

    public DateOnly EndDate { get; set; } // วันหมดอายุแพ็กเกจ

    public int RemainingPods { get; set; } // โควต้าห้องซ้อม Private Pod คงเหลือ

    public int RemainingPt { get; set; } // โควต้าเทรนเนอร์ส่วนตัว (PT) คงเหลือ

    public int RemainingInBody { get; set; } // โควต้าตรวจวัดองค์ประกอบร่างกาย (InBody) คงเหลือ

    public string? Status { get; set; } // สถานะสัญญา (Active, Expired, Cancelled)

    public virtual Package Package { get; set; } = null!; // ข้อมูลแพ็กเกจที่ผูกไว้

    public virtual User User { get; set; } = null!; // ข้อมูลสมาชิกเจ้าของสัญญา
}
