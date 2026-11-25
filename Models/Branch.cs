using System;
using System.Collections.Generic;

namespace PrivateGym.Models;

public partial class Branch
{
    public int BranchId { get; set; } // รหัสสาขา (PK)

    public string BranchName { get; set; } = null!; // ชื่อสาขา

    public string Location { get; set; } = null!; // ที่ตั้งสาขา

    public string? Phone { get; set; } // เบอร์โทรศัพท์ประจำสาขา

    public DateTime? CreatedAt { get; set; } // วันที่เปิดสาขาในระบบ

    public virtual ICollection<Pod> Pods { get; set; } = new List<Pod>(); // ห้องซ้อมทั้งหมดในสาขานี้

    public virtual ICollection<User> Users { get; set; } = new List<User>(); // สมาชิกและพนักงานที่สังกัดสาขานี้
}
