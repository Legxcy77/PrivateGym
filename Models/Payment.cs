using System;
using System.Collections.Generic;

namespace PrivateGym.Models;

public partial class Payment
{
    public int PaymentId { get; set; } // รหัสรายการชำระเงิน (PK)

    public int UserId { get; set; } // รหัสสมาชิกผู้ชำระ (FK -> Users)

    public int PackageId { get; set; } // รหัสแพ็กเกจที่ชำระ (FK -> Packages)

    public decimal Amount { get; set; } // ยอดเงินที่ชำระ (บาท)

    public string PaymentMethod { get; set; } = null!; // ช่องทางชำระเงิน (CreditCard, PromptPay, BankTransfer)

    public DateTime? PaymentDate { get; set; } // วันเวลาที่ชำระ/ออกบิล

    public string? Status { get; set; } // สถานะบิล (Success, Pending, Failed)

    public string? SlipReference { get; set; } // เลขอ้างอิงสลิป/ใบเสร็จ (เช่น RENEW-, NEXT-, INV-)

    public virtual Package Package { get; set; } = null!; // ข้อมูลแพ็กเกจที่ซื้อ

    public virtual User User { get; set; } = null!; // ข้อมูลสมาชิกผู้ชำระเงิน
}
