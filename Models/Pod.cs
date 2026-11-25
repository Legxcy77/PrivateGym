using System;
using System.Collections.Generic;

namespace PrivateGym.Models;

public partial class Pod
{
    public int PodId { get; set; } // รหัสห้องซ้อม Private Pod (PK)

    public int BranchId { get; set; } // รหัสสาขาที่ตั้ง (FK -> Branches)

    public string PodName { get; set; } = null!; // ชื่อห้องซ้อม (เช่น Pod 01: Solo Precision)

    public string PodType { get; set; } = null!; // ประเภทห้อง (Solo, Strength, Recovery ฯลฯ)

    public string? Status { get; set; } // สถานะห้อง (Available = พร้อมใช้, Maintenance = ปิดปรับปรุง)

    public virtual ICollection<Booking> Bookings { get; set; } = new List<Booking>(); // คิวจองทั้งหมดของห้องนี้

    public virtual Branch Branch { get; set; } = null!; // ข้อมูลสาขาที่ตั้ง
}
