# System Design & Business Logic Specification

> เอกสารสรุปสถาปัตยกรรม, Business Logic Guards, และแผนผัง Controller ของระบบ **PrivateGym**

---

## 1. Cross-Role Data Synchronization

ทุก Role ใช้งานฐานข้อมูล MySQL ชุดเดียวกัน (`CSI402DB`) ผ่าน `Csi402dbContext` ทำให้ข้อมูลซิงค์กันแบบ Real-time:

1. **Pod Maintenance Sync:**
   - Admin (`/Admin/Packages`) หรือ Staff (`/Staff/Pods`) กดสลับสถานะห้องเป็น `Maintenance` -> `CustomerController.BookSession` และ `CheckSlots` บล็อกการจองห้องนั้นทันที
2. **Shift Handover Log:**
   - Staff บันทึกส่งเวรหน้าเคาน์เตอร์ (`/Staff/Index`) -> แสดงผลทันทีในกล่อง `บันทึกส่งเวรล่าสุดจากหน้า Staff` บน `/Admin/Staff`
3. **Trainer Rate & Payout Sync:**
   - Admin ปรับเรทค่าสอน (`HourlyRate`) ใน `/Admin/Trainers` -> หน้า `/Trainer/History` และ `/Trainer/Profile` คำนวณ `EST. PAYOUT` ตามเรทใหม่ทันที
4. **Counter Renewal & Accountant Approval:**
   - Admin สร้างบิลต่ออายุสถานะ `Pending` (prefix `RENEW-`) -> ทั้ง `AdminController.ApprovePayment` และ `AccountantController.ApprovePayment` จะเรียก `AdminController.ApplySubscriptionRenewal` เพื่อต่อวันและเติมโควต้าให้อัตโนมัติหลังอนุมัติ

---

## 2. Business Logic Guards

| Guard | จุดที่บังคับใช้ | พฤติกรรมที่ป้องกัน |
| :--- | :--- | :--- |
| **Account Suspension** | `LoginController.Login`, `CustomerController.BookSession`, `AdminController.ManualCheckIn` | บล็อกบัญชี `Inactive` / `Suspended` ไม่ให้ล็อกอิน, จองห้อง, หรือเช็กอิน |
| **Subscription Expiry** | `CustomerController.BookSession` | บล็อกการจองหาก `EndDate < Today` แม้ยังมีโควต้าค้าง |
| **Slot Collision** | `CustomerController.CheckSlots`, `BookSession` | ป้องกันการจองห้อง Pod หรือเทรนเนอร์ซ้ำซ้อนในช่วงเวลาเดียวกัน |
| **Checked-in Cancel Block** | `AdminController.CancelBookingAdmin` | ห้ามยกเลิกและคืนโควต้าหากคิวนั้นเช็กอินเข้าห้องแล้ว |
| **Counter POS Slip Check** | `AdminController.RenewMemberSubscription` | บังคับกรอกเลขอ้างอิงสลิป/EDC เมื่อไม่ใช่เงินสด และกันสร้างบิล `Pending` ซ้ำซ้อน |
| **Quota Adjustment Audit** | `AdminController.AdjustMemberQuota` | บังคับระบุเหตุผลทุกครั้งและบันทึกลง `App_Data/quota_adjustments.json` |
| **Password Hashing** | `Services/PasswordHelper.cs` | Hash รหัสผ่านด้วย `PasswordHasher<User>` และอัปเกรดรหัสเดิมอัตโนมัติเมื่อล็อกอิน |

---

## 3. Controller Map

- **`HomeController`:** Public Landing, Facilities, Trainers, Packages, Contact, Trial Booking (`BookTrial`)
- **`RegisterController`:** สมัครสมาชิกใหม่, เปิด `MemberSubscription` เริ่มต้น, ออกบิล `Payment`
- **`LoginController`:** ตรวจสอบรหัสผ่าน (`PasswordHelper`), จัดการ Session, แยก Redirect ตาม `RoleId` (`1=Admin, 2=Staff, 3=Trainer, 4=Member, 5=Accountant`)
- **`CustomerController`:** Dashboard, `BookSession`, `CheckSlots`, `MyBookings`, `MyPackages`, `TopUpQuota`, `ServiceHistory`, `EditProfile`
- **`StaffController`:** Operations Desk (`CheckInMember`, `CheckOutMember`, `AddHandoverNote`), `Pods`, `Schedule`, `Members`, `History`
- **`TrainerController`:** Coaching Dashboard (`SaveWorkoutLog`, `CompleteSession`), `Schedule`, `Availability`, `Clients`, `History`, `Profile`
- **`AccountantController`:** Finance Dashboard, `Payments` (`ApprovePayment`, `RejectPayment`), `TaxInvoices`, `Subscriptions`
- **`AdminController`:** Executive Dashboard, `UserList` (Pagination + Counter POS), `Staff`, `Trainers`, `Packages`, `Schedule` (Weekly/Daily), `Bookings` (Inspection Desk), `CheckIn` (Manual Desk), `Payments` (Audit Console), `Reports` (Hourly Heatmap), `UsersAndRoles`, `Settings`

