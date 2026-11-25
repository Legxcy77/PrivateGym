using System;
using System.Collections.Generic;

namespace PrivateGym.Models;

public partial class Trainer
{
    public int TrainerId { get; set; } // รหัสเทรนเนอร์ (PK)

    public int UserId { get; set; } // รหัสบัญชีผู้ใช้ของโค้ช (FK -> Users)

    public string? Specialty { get; set; } // ความเชี่ยวชาญหลัก

    public string? Bio { get; set; } // ประวัติและคำแนะนำตัวโค้ช

    public decimal? HourlyRate { get; set; } // เรทค่าสอนต่อเซสชัน (บาท)

    public virtual ICollection<Booking> Bookings { get; set; } = new List<Booking>(); // รายการจองที่โค้ชคนนี้รับผิดชอบ

    public virtual User User { get; set; } = null!; // ข้อมูลบัญชีผู้ใช้ของโค้ช
}
