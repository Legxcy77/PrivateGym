using System;
using System.Collections.Generic;

namespace PrivateGym.Models;

public partial class Booking
{
    public int BookingId { get; set; } // รหัสการจอง (PK)

    public int UserId { get; set; } // รหัสสมาชิกผู้จอง (FK -> Users)

    public int PodId { get; set; } // รหัสห้องซ้อม Private Pod ที่จอง (FK -> Pods)

    public int? TrainerId { get; set; } // รหัสเทรนเนอร์ที่เลือก (null = เล่นเดี่ยว Solo Pod)

    public DateOnly BookingDate { get; set; } // วันที่เข้าใช้บริการ

    public TimeOnly StartTime { get; set; } // เวลาเริ่มต้นรอบฝึก

    public TimeOnly EndTime { get; set; } // เวลาสิ้นสุดรอบฝึก

    public string? Status { get; set; } // สถานะการจอง (Confirmed, Completed, Cancelled)

    public DateTime? CreatedAt { get; set; } // วันเวลาที่กดจอง

    public virtual ICollection<CheckIn> CheckIns { get; set; } = new List<CheckIn>(); // บันทึกการเช็กอินของการจองนี้

    public virtual Pod Pod { get; set; } = null!; // ข้อมูลห้องซ้อม Pod

    public virtual Trainer? Trainer { get; set; } // ข้อมูลเทรนเนอร์ (ถ้ามี)

    public virtual User User { get; set; } = null!; // ข้อมูลสมาชิกผู้จอง
}
