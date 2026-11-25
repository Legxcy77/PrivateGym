using System;
using System.Collections.Generic;

namespace PrivateGym.Models;

public partial class Role
{
    public int RoleId { get; set; } // รหัสบทบาท (PK: 1=Admin, 2=Staff, 3=Trainer, 4=Member, 5=Accountant)

    public string RoleName { get; set; } = null!; // ชื่อบทบาท

    public string? Description { get; set; } // คำอธิบายสิทธิ์ของบทบาท

    public virtual ICollection<User> Users { get; set; } = new List<User>(); // ผู้ใช้ทั้งหมดในบทบาทนี้
}
