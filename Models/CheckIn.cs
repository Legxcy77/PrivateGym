using System;
using System.Collections.Generic;

namespace PrivateGym.Models;

public partial class CheckIn
{
    public int CheckInId { get; set; } // รหัสการเช็กอิน (PK)

    public int UserId { get; set; } // รหัสสมาชิกที่เข้าใช้งาน (FK -> Users)

    public int? BookingId { get; set; } // รหัสการจองที่อ้างอิง (FK -> Bookings)

    public DateTime? CheckInTime { get; set; } // วันเวลาที่สแกนเข้าประตู/เช็กอิน

    public DateTime? CheckOutTime { get; set; } // วันเวลาที่เช็กเอาต์ออกจากห้อง (null = กำลังซ้อมอยู่)

    public string? AccessMethod { get; set; } // ช่องทางเข้าใช้งาน (QR_Code, Staff_Desk, Admin_Desk)

    public virtual Booking? Booking { get; set; } // ข้อมูลการจองที่เกี่ยวข้อง

    public virtual User User { get; set; } = null!; // ข้อมูลสมาชิก
}
