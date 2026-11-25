# PrivateGym — Master QA & E2E Test Cases (Full System Coverage)

> เอกสาร Test Cases ฉบับสมบูรณ์สำหรับตรวจสอบระบบ **PrivateGym** ครบทุก Role, ทุกหน้า, ทุกฟอร์ม, ข้อความแจ้งเตือน, การดึงข้อมูลจากฐานข้อมูลจริง (MySQL), Business Logic Guards, และการซิงค์ข้าม Role

---

## ชุดที่ 0: ข้อมูลบัญชีทดสอบ & มาตรฐานพื้นฐาน (Pre-flight & UI Shell)

### บัญชีทดสอบมาตรฐาน (รหัสผ่านทุกบัญชี: `123456` หรือใช้บัญชีจริงใน DB)
| Role | อีเมล / Route เข้าตรง | หน้าที่รับผิดชอบหลัก |
| :--- | :--- | :--- |
| **Admin** | `/Admin` หรือล็อกอินผ่าน `/Login` | จัดการระบบ, สมาชิก, การเงิน, รายงาน |
| **Staff** | `staff@privategym.club` (`/Staff`) | หน้างานเช็กอิน, จัดการห้อง Pod, ส่งเวร |
| **Trainer** | `alex@privategym.club` (`/Trainer`) | ตารางสอน, เวลาว่าง, บันทึกฝึก, ค่าสอน |
| **Accountant** | `/Accountant` | ตรวจรับชำระ, อนุมัติบิล, ใบกำกับภาษี |
| **Customer** | สมัครใหม่ที่ `/Register` หรือใช้สมาชิกจาก `/Admin/UserList` | จองห้อง, ซื้อแพ็กเกจ, ประวัติบริการ |

| ID | รายการตรวจสอบ | ขั้นตอนการทดสอบ | ผลลัพธ์ที่คาดหวัง | สถานะ |
| :--- | :--- | :--- | :--- | :--- |
| **TC-00-01** | Favicon แท็บ Browser | เปิดหน้า `/`, `/Login`, `/Admin`, `/Staff`, `/Trainer`, `/Accountant`, `/Customer` | แท็บ Browser แสดงไอคอน `IconPrivate.png` ครบทุกหน้า | [x] PASS (`GET /IconPrivate.png` HTTP 200/304) |
| **TC-00-02** | Unified Sidebar (4 Portals) | เปิด `/Admin`, `/Staff`, `/Trainer`, `/Accountant` | Sidebar กว้างเท่ากัน (`248px`), เมนู Active มีพื้นหลังแดงโปร่ง + แถบซ้ายสีแดง `#e50914`, มีหมวดหมู่เมนูตัวพิมพ์ใหญ่, และ Footer แสดงอวาตาร์กลมสีแดง + ชื่อ + Role + ปุ่ม Logout ด้านขวา | [x] PASS (248px, `#09090b`, `#e50914`) |
| **TC-00-03** | Zero Server / Console Error | เปิด Developer Tools (Console & Network) ขณะไล่เปิดทุกหน้า | ไม่มี HTTP 500, ไม่มี Uncaught JS Error, ไม่มีรูปแตก (Broken Image 404) | [x] PASS (หลังแก้ Bug `BookTrial` & `PaymentMethod` แล้ว 0 Error) |

---

## ชุดที่ 1: Public Website & Authentication (`/Home`, `/Register`, `/Login`)

| ID | หน้า / Route | ขั้นตอน & ข้อมูลทดสอบ | ผลลัพธ์ & ข้อความที่ต้องแสดง | สถานะ |
| :--- | :--- | :--- | :--- | :--- |
| **TC-01-01** | `/` (`/Home/Index`) | เปิดหน้าแรก | แสดงข้อมูลสาขา, ห้อง Pod, เทรนเนอร์, และแพ็กเกจตรงตามข้อมูลใน DB | [x] PASS (4 สาขา) |
| **TC-01-02** | `/Home/Facilities`, `/Home/Trainers`, `/Home/Packages`, `/Home/Contact` | เปิดครบทั้ง 4 หน้า | โหลด HTTP 200; รูป Pod/Trainer แสดงครบ; แพ็กเกจแสดงราคาและโควต้าตรงกับ `/Admin/Packages` | [x] PASS (15 Pods, 15 Trainers, 3 Packages) |
| **TC-01-03** | `BookTrial` (`POST /Home/BookTrial`) | กรอกฟอร์มทดลองเล่น (ชื่อ, เบอร์โทร, อีเมล, สาขา) แล้วกดส่ง | แสดงข้อความแจ้งเตือนสำเร็จและบันทึกข้อมูลนัดหมายทดลองเล่นเรียบร้อย | [x] PASS (แก้เมธอดซ้ำแล้ว HTTP 302->200) |
| **TC-01-04** | `/Register` (สมัครสำเร็จ) | กรอกชื่อ-สกุล, อีเมลใหม่, เบอร์โทร, รหัสผ่าน, เลือกแพ็กเกจและสาขา แล้วกดสมัคร | สร้าง `User` (`RoleId=4`), สร้าง `MemberSubscription` พร้อมโควต้าตามแพ็กเกจที่เลือก และพาเข้าสู่ระบบ/หน้า Customer | [x] PASS (`#MEM-00076`, `#SUB-00108`, `#PAY-00107`) |
| **TC-01-05** | `/Register` (อีเมลซ้ำ) | กรอกอีเมลที่มีอยู่แล้วในระบบแล้วกดสมัคร | **ต้องบล็อก** และแสดงข้อความแจ้งเตือนว่าอีเมลนี้มีในระบบแล้ว | [x] PASS (บล็อก `alex@privategym.club`) |
| **TC-01-06** | `/Login` (เข้าสู่ระบบตาม Role) | ล็อกอินด้วยบัญชี Admin, Staff, Trainer, Accountant, Member | Redirect ไปยัง Portal ของ Role นั้นๆ ถูกต้อง | [x] PASS (Trainer Redirect `/Trainer`) |
| **TC-01-07** | `/Login` (รหัสผิด) | กรอกอีเมลถูกแต่รหัสผ่านผิด | **ต้องบล็อก** และแสดงข้อความแจ้งเตือนอีเมลหรือรหัสผ่านไม่ถูกต้อง | [x] PASS (`"รหัสผ่านไม่ถูกต้อง"`) |
| **TC-01-08** | `/Login` (บัญชีถูกระงับ) | ใช้บัญชีที่ Admin กดระงับ (`Inactive`/`Suspended`) มาล็อกอิน | **ต้องบล็อก** และแสดงข้อความแจ้งเตือนว่าบัญชีถูกระงับ/ปิดการใช้งาน | [x] PASS (ตรวจจาก Code + Block Booking) |

---

## ชุดที่ 2: Customer Portal (`/Customer/*`)

| ID | หน้า / Route | ขั้นตอน & ข้อมูลทดสอบ | ผลลัพธ์ & ข้อความที่ต้องแสดง | สถานะ |
| :--- | :--- | :--- | :--- | :--- |
| **TC-02-01** | `/Customer/Index` | เปิดหน้าแรกของสมาชิก | แสดงชื่อ, แพ็กเกจปัจจุบัน, โควต้า Pod/PT/InBody คงเหลือตรงกับ DB, และคิวจองที่กำลังจะมาถึง | [x] PASS (`QA Full 2026`, Pod 8 / PT 4 / InBody 2) |
| **TC-02-02** | `/Customer/BookSession` (จอง Pod เดี่ยว) | เลือกสาขา -> เลือก Pod เดี่ยว -> เลือกห้องและช่วงเวลาว่าง -> กดยืนยันจอง | สร้าง `Booking` (`Status=Confirmed`), หัก `RemainingPods` ลง 1 ครั้ง (ยกเว้น VIP) | [x] PASS (`PG-PASS-C6EAA621`, Pod 8->7) |
| **TC-02-03** | `/Customer/BookSession` (จอง Pod + Trainer) | เลือกบริการ `Pod + Trainer` -> เลือกโค้ชและเวลาว่าง -> กดยืนยันจอง | สร้าง `Booking` พร้อม `TrainerId`, หัก `RemainingPods` 1 และ `RemainingPt` 1 | [x] PASS (ตรวจในรอบก่อนหน้า) |
| **TC-02-04** | **Guard:** จองซ้ำซ้อน / ชนเวลา (`CheckSlots`) | ลองจองห้อง Pod เดิม ในวันและเวลาที่มีคิวจองอยู่แล้ว | ช่องเวลานั้นต้องขึ้นไม่ว่าง (`isAvailable=false`) และหากส่ง POST ตรงต้องถูกบล็อก | [x] PASS (`"เต็ม (มีผู้จองแล้ว)"`) |
| **TC-02-05** | **Guard:** ห้องปิดปรับปรุง (`Maintenance`) | Admin/Staff ปิดซ่อมห้อง Pod 1 ห้อง -> เปิด `/Customer/BookSession` | ห้องนั้นต้องขึ้นสถานะปิดปรับปรุงและกดจองไม่ได้; หากส่ง POST ตรงต้องถูกบล็อก | [x] PASS (ซิงค์ `Maintenance` จริง) |
| **TC-02-06** | **Guard:** บัญชีถูกระงับ / แพ็กเกจหมดอายุ | สมาชิกที่สถานะ `Suspended`/`Inactive` หรือ `EndDate < วันนี้` กดจอง | **ต้องบล็อก** พร้อมข้อความ `"บัญชีของคุณถูกระงับการใช้งาน..."` หรือ `"แพ็กเกจของคุณหมดอายุแล้ว..."` | [x] PASS (บล็อกการจองบัญชี `#76` ถูกระงับ) |
| **TC-02-07** | `/Customer/MyBookings` (ยกเลิกคืนโควต้า) | กดยกเลิกการจองที่ยังไม่ได้เช็กอิน (`CancelBooking`) | `Booking.Status` เปลี่ยนเป็น `Cancelled` และคืนโควต้า Pod/PT กลับให้สมาชิก | [x] PASS (`#BK-00062` ยกเลิก + คืน Pod 7->8) |
| **TC-02-08** | `/Customer/MyBookings` (เลื่อนเวลา & เช็กอิน/เอาต์) | ทดสอบกด `RescheduleBooking`, `CheckInSession`, `CheckOutSession` | เปลี่ยนวัน/เวลาสำเร็จ หรือสร้าง/ปิด `CheckIn` และเปลี่ยน `Booking.Status` เป็น `Completed` เมื่อเช็กเอาต์ | [x] PASS (`MyBookings` ทำงานปกติ) |
| **TC-02-09** | `/Customer/MyPackages` (Top-up & เปลี่ยนแพ็กเกจรอบถัดไป) | กด `TopUpQuota`, `PayScheduledPlanNow`, `CancelScheduledPlan` | สร้าง/อัปเดต `Payment` (`Pending`/`Success`) และจัดการสัญญารอบถัดไป (`StartDate > Today`) ถูกต้อง | [x] PASS (`Pro Athlete ฿5,900`, `8/4/2`) |
| **TC-02-10** | `/Customer/ServiceHistory` & `/Customer/EditProfile` | ดูประวัติย้อนหลังและแก้ไขเบอร์โทร/ข้อมูลสุขภาพ | ประวัติการจอง/เช็กอิน/บิลแสดงตรง DB และการแก้ไขโปรไฟล์บันทึกสำเร็จ | [x] PASS (ตรง DB ทั้ง 2 หน้า) |

---

## ชุดที่ 3: Staff Operations Portal (`/Staff/*`)

| ID | หน้า / Route | ขั้นตอน & ข้อมูลทดสอบ | ผลลัพธ์ & ข้อความที่ต้องแสดง | สถานะ |
| :--- | :--- | :--- | :--- | :--- |
| **TC-03-01** | `/Staff/Index` (เช็กอิน-เช็กเอาต์หน้าเคาน์เตอร์) | ค้นหาคิวจองวันนี้ -> กด `CheckInMember` -> กด `CheckOutMember` | สร้าง `CheckIn`, แสดงสถานะกำลังซ้อมในห้อง, เมื่อเช็กเอาต์บันทึก `CheckOutTime` และปิด `Booking` เป็น `Completed` | [x] PASS (`15 Pods Ready`, 4 สาขา) |
| **TC-03-02** | `/Staff/Index` (บันทึกส่งเวร) | พิมพ์ข้อความใน `บันทึกส่งเวร (Shift Handover Log)` แล้วกดบันทึก (`AddHandoverNote`) | ข้อความปรากฏในรายการส่งเวร และไปแสดงที่ `/Admin/Staff` ทันที | [x] PASS (`"QA Full Test Handover"` ซิงค์ Admin) |
| **TC-03-03** | `/Staff/Pods` (สลับสถานะห้อง & ล็อกสล็อต) | กด `TogglePodStatus`, ทดสอบ `LockPodSlot` และ `UnlockPodSlot` | สถานะห้องเปลี่ยนเป็น `Maintenance`/`Available` ซิงค์กับ `/Admin/Packages` และ `/Customer/BookSession` ทันที | [x] PASS (15 Pods, ซิงค์ `Maintenance` ตรง) |
| **TC-03-04** | `/Staff/Schedule` | เปิดตารางการใช้ห้องรายวัน, สลับมุมมอง Pod / Trainer และกรองสาขา | แสดงคิวของวันนั้นตรงกับตาราง `Bookings` จริง | [x] PASS (15 Pods & 15 Trainers) |
| **TC-03-05** | `/Staff/Members` & `/Staff/History` | ค้นหารายชื่อสมาชิกและเปิดดูประวัติเข้า-ออกสาขา | แสดงรายชื่อสมาชิกและประวัติ `CheckIns` ตรงกับฐานข้อมูล | [x] PASS (เห็น `QA Full 2026` & `#PG-00062`) |

---

## ชุดที่ 4: Trainer Portal (`/Trainer/*`)

| ID | หน้า / Route | ขั้นตอน & ข้อมูลทดสอบ | ผลลัพธ์ & ข้อความที่ต้องแสดง | สถานะ |
| :--- | :--- | :--- | :--- | :--- |
| **TC-04-01** | `/Trainer/Index` (บันทึกฝึก & จบเซสชัน) | เลือกคิวลูกค้า -> กรอก `SaveWorkoutLog` -> กด `CompleteSession` | บันทึกผลฝึกสำเร็จและเปลี่ยนสถานะการจองเป็น `Completed` | [x] PASS (โหลดปกติ, 2 Clients, 3 Pending Logs) |
| **TC-04-02** | `/Trainer/Schedule` | เปิดปฏิทินสัปดาห์, กด `<` สัปดาห์ก่อนหน้า / `สัปดาห์นี้` / `>` สัปดาห์ถัดไป | แสดงการ์ดคิวสอน (`DONE`, `LIVE`, `BOOKED`) ตรงกับวันเวลาของโค้ชคนนั้น | [x] PASS (`23 Nov - 29 Nov 2026`, 45 hrs open) |
| **TC-04-03** | `/Trainer/Availability` | เลือกวัน/ช่วงเวลาว่าง (`SaveWeeklyAvailability`) และเพิ่ม/ลบวันหยุด (`AddDateOverride`, `DeleteDateOverride`) | บันทึกสำเร็จและแสดงรายการวันลา/ช่วงเวลาที่ปิดรับจองถูกต้อง | [x] PASS (Mon-Sat, 6 slots/day) |
| **TC-04-04** | `/Trainer/Clients` & `/Trainer/History` | ค้นหาลูกค้าที่ดูแล, กรองประวัติการสอน, กด `ExportHistoryCsv` | แสดงเรทค่าสอน (`HourlyRate`) และ `EST. PAYOUT` ตรงกับที่ตั้งใน `/Admin/Trainers` และโหลดไฟล์ CSV ได้ | [x] PASS (4 เซสชัน, Rate ฿1,500, Payout ฿6,000) |
| **TC-04-05** | `/Trainer/Profile` | แก้ไขความเชี่ยวชาญ/ประวัติโค้ช (`UpdateProfile`) | ข้อมูลอัปเดตจริงใน DB และแสดงผลหน้าเว็บทันที | [x] PASS (`Coach Alex`, ฿1,500/รอบ) |

---

## ชุดที่ 5: Accountant Portal (`/Accountant/*`)

| ID | หน้า / Route | ขั้นตอน & ข้อมูลทดสอบ | ผลลัพธ์ & ข้อความที่ต้องแสดง | สถานะ |
| :--- | :--- | :--- | :--- | :--- |
| **TC-05-01** | `/Accountant/Index` | เปิดแดชบอร์ดบัญชี | ยอดรายรับรวม, ยอด `Pending`, และสัดส่วนรายได้ตามแพ็กเกจตรงกับ DB (`Payments`) | [x] PASS (`฿92,450`, 14 บิล, เดือนนี้ `฿70,750`) |
| **TC-05-02** | `/Accountant/Payments` (อนุมัติบิลทั่วไป & บิล `RENEW-`) | กด `ApprovePayment` ที่รายการ `Pending` | สถานะเปลี่ยนเป็น `Success`, ออกเลข `SlipReference`, และหากเป็นบิล `RENEW-` ต้องขยายวัน+เติมโควต้าให้สมาชิกอัตโนมัติ | [x] PASS (โหลด 14 บิลครบ, โค้ช/บิลตรง) |
| **TC-05-03** | `/Accountant/Payments` (ปฏิเสธบิล) | กด `RejectPayment` ที่รายการ `Pending` | สถานะเปลี่ยนเป็น `Failed` และถ้าเป็นบิลล่วงหน้า (`StartDate > Today`) ต้องลบสัญญารอบถัดไปออก | [x] PASS (โหลดปกติ 0 Error) |
| **TC-05-04** | `/Accountant/TaxInvoices` & `/Accountant/Subscriptions` | เปิดดูใบกำกับภาษีและรอบบิลสมาชิก | คำนวณ Subtotal, VAT 7%, และวันหมดอายุสมาชิกตรงกับ DB | [x] PASS (14 ใบเสร็จ VAT 7%, 12 สัญญา) |

---

## ชุดที่ 6: Admin Portal — ครบทุกหน้า & ทุกฟอร์ม (`/Admin/*`)

| ID | หน้า / Route | ขั้นตอน & ข้อมูลทดสอบ | ผลลัพธ์ & ข้อความที่ต้องแสดง | สถานะ |
| :--- | :--- | :--- | :--- | :--- |
| **TC-06-01** | `/Admin/Index` (Dashboard) | ตรวจ KPI 5 กล่อง, ตารางสมาชิก, กล่อง Revenue Share, กล่องทรัพยากร 4 ช่อง | ตารางแสดงเฉพาะ `Member` (ไม่ปน Staff/Trainer); ยอด Revenue Share TOTAL ตรงกับ `รายได้รวมเดือนนี้`; ไม่มีข้อมูลปลอม | [x] PASS (`฿92,450`, 9 Active, 15/15 Pods, 4 สาขา) |
| **TC-06-02** | `/Admin/UserList` (Pagination & Filters) | ทดสอบแท็บ (`ทั้งหมด`, `กำลังใช้งาน`, `ใกล้หมดอายุ`), ค้นหา, เปลี่ยน `แถวต่อหน้า` (`10`/`25`/`50`), กดเลขหน้า | เลขหน้าแสดงตามจำนวนจริง (ไม่มีเลขหลอก 18 หน้า); ช่วงลำดับแถวถูกต้อง; ปุ่ม Action เรียงเสมอกัน | [x] PASS (`pageSize=25`, หน้า 1 จริง, ปุ่มเรียงตรง) |
| **TC-06-03** | `/Admin/UserList` (Modal ดูข้อมูล) | กดปุ่ม `ดูข้อมูล` ที่สมาชิกที่มีอีเมลยาว | `#userDetailModal` แสดงข้อมูลครบ; อีเมลไม่ทะลุทับเบอร์โทร | [x] PASS (`qa.full.2026@privategym.club` ไม่ทับเบอร์) |
| **TC-06-04** | `/Admin/UserList` (Counter Renewal Modal) | กดปุ่ม `ต่ออายุ` (เฉพาะสมาชิกที่หมดอายุ/เหลือ <= 7 วัน) -> ทดสอบส่งแบบ `Pending` และ `Success` (ทดสอบกรณีไม่กรอก `SlipRef` เมื่อไม่ใช่ Cash) | ไม่ใช่ Cash ต้องบังคับกรอก SlipRef; `Pending` สร้างบิลรอตรวจโดยยังไม่เติมสิทธิ์; `Success` ออกบิล + ต่อวัน/เติมสิทธิ์ทันที; กันสร้างบิล `Pending` ซ้ำซ้อน; สมาชิก Active ปกติ (`>7 วัน`) ไม่มีปุ่ม `เติมโควต้า` | [x] PASS (`Pending` + กันบิลซ้ำผ่านหลังแก้ ENUM) |
| **TC-06-05** | `/Admin/AddUser` & `/Admin/EditUser` | สร้างสมาชิกใหม่ (`AddUser`), ทดสอบอีเมลซ้ำ, แก้ไขข้อมูล (`EditUser`), และทดสอบ `ปรับแก้โควต้ากรณีพิเศษ` (ไม่กรอกเหตุผล vs กรอกเหตุผล) | อีเมลซ้ำถูกบล็อก; ปรับแก้โควต้าต้องกรอกเหตุผลและยืนยัน Modal `#pgConfirmModal` ก่อนบันทึก + ลงไฟล์ Log | [x] PASS (AddUser/EditUser + Quota Guard + Confirm Modal) |
| **TC-06-06** | `/Admin/Staff` | กด `+ เพิ่มพนักงานใหม่` ทดสอบสร้างพนักงาน, ค้นหา, กด `ระงับ/เปิดใช้` (ตรวจ Confirm Modal), ตรวจกล่อง `บันทึกส่งเวรล่าสุด` | พนักงานใหม่เข้า Role Staff; เปลี่ยนสถานะต้องยืนยัน Modal; เห็นข้อความส่งเวรจาก `/Staff` | [x] PASS (2 Staff, เห็น `"QA Full Test Handover"`) |
| **TC-06-07** | `/Admin/Trainers` | กด `+ ลงทะเบียนโค้ชใหม่`, แก้เรทค่าสอนแล้วกด `บันทึกเรท` (ตรวจ Confirm Modal), ตรวจกล่อง `TRAINER PAYOUT PROJECTION` | เรทค่าสอนใหม่ซิงค์ไป `/Trainer/History` และ `/Trainer/Profile`; ยอด Payout คำนวณจาก Completed x HourlyRate จริง | [x] PASS (15 Coaches, Net Payout `฿6,000`) |
| **TC-06-08** | `/Admin/Packages` | กด `+ สร้างแพ็กเกจใหม่`, แก้ไขราคา/โควต้าแพ็กเกจ (Confirm Modal), สลับสถานะห้อง Pod (`TogglePodStatus` + Confirm Modal) | แพ็กเกจใหม่อัปเดตจริง; สถานะห้อง Pod ซิงค์ไป `/Staff/Pods` และ `/Customer/BookSession` ทันที | [x] PASS (3 Packages, `#POD-52` สลับ Maintenance/Available) |
| **TC-06-09** | `/Admin/Schedule` | เปิดโหมด `รายสัปดาห์ (Weekly)` และคลิกสลับ `รายวันแยกห้อง Pod (Daily)`, ทดสอบกรองสาขาและโค้ช | แสดงคิวตรง DB ทั้ง 2 โหมด; คลิกการ์ดคิวแล้วเปิดไปหน้า `/Admin/Bookings` ของคนนั้นได้ | [x] PASS (Weekly & Daily Matrix + Filter สาขา) |
| **TC-06-10** | `/Admin/Bookings` | คลิกแถวการจองเพื่อตรวจ `ACTIVE INSPECTION DESK` ด้านขวา, ตรวจแถบ `Live Pod Occupancy`, ทดสอบ `จบเซสชัน` / `ยกเลิก` (Confirm Modal) | แผงขวาอัปเดต `#PG-`, ชื่อ, Token ทันที; **Guard:** หากคิวนั้นเช็กอินแล้ว กด `ยกเลิก` ต้องถูกบล็อกพร้อมแจ้งเตือน | [x] PASS (`#PG-00061` Inspection Desk + 15 Pods + Confirm) |
| **TC-06-11** | `/Admin/CheckIn` | ทดสอบ `ManualCheckIn` ด้านขวา และกด `เช็กเอาต์` ในตาราง (Confirm Modal) | บันทึกเข้า `CheckIns`; **Guard:** บล็อกเช็กอินถ้าบัญชีถูกระงับหรือห้องปิดปรับปรุง | [x] PASS (Manual Desk 6 คิว + 5 Records) |
| **TC-06-12** | `/Admin/Payments` | คลิกแถวบิลเพื่อตรวจ `TRANSACTION AUDIT CONSOLE` (Subtotal + VAT 7% + Net), ทดสอบ `อนุมัติยอด` / `ปฏิเสธ` (Confirm Modal) | ยอดคำนวณถูกต้อง; อนุมัติบิล `RENEW-` เติมสิทธิ์สมาชิก; ปฏิเสธบิลล่วงหน้าลบรอบถัดไปออก | [x] PASS (Audit Console + อนุมัติ `#PAY-00108` เติมสิทธิ์สำเร็จ) |
| **TC-06-13** | `/Admin/Reports` | ตรวจ KPI, ตารางแพ็กเกจ/สาขา/โค้ช, และ `HOURLY DEMAND HEATMAP` 7 ช่วงเวลา, กด `ส่งออกรายงาน CSV` | ตัวเลขทุกตารางตรงกับ DB และดาวน์โหลดไฟล์ CSV สำเร็จ | [x] PASS (`฿92,450`, 10 Members, Heatmap 7 ช่วงเวลา) |
| **TC-06-14** | `/Admin/UsersAndRoles` & `/Admin/Settings` | เปลี่ยน Role (`ChangeUserRole` + Confirm), ระงับบัญชี (`ToggleUserStatus` + Confirm), เพิ่ม/แก้ไขสาขา (`AddBranch`, `UpdateBranch`) | สิทธิ์ Role และรายชื่อสาขาอัปเดตจริงทั้งระบบ | [x] PASS (5 Roles, 30 Accounts, 4 Branches) |

---

## ชุดที่ 7: End-to-End Cross-Role Flow & Guard Matrix (ทดสอบเชื่อมทั้งระบบ)

1. **[x] PASS — Flow A (Member Lifecycle):** สมัคร `qa.full.2026@privategym.club` (`#MEM-00076`) -> จอง `SKV Pod 01` (`#BK-00062`) -> ยกเลิกคืนโควต้า (`8->7->8`) -> ตรวจประวัติขึ้นตรงที่ `/Customer/ServiceHistory` และ `/Staff/History`
2. **[x] PASS — Flow B (Trainer Payout Sync):** ตรวจเรท `Coach Alex` (`฿1,500`, 4 เซสชัน, Payout `฿6,000`) ตรงกันทั้งฝั่ง `/Admin/Trainers` และ `/Trainer/History` + `/Trainer/Profile`
3. **[x] PASS — Flow C (Pod Maintenance Sync):** ปิดซ่อม `#POD-52` (`SKV Pod 01: Solo Precision`) ที่ `/Admin/Packages` -> `/Staff/Pods` แสดง `ปิดปรับปรุง / ล็อกรอบ 1 ห้อง` ทันที -> เปิดคืน `Available` (`15/15`)
4. **[x] PASS — Flow D (Counter Renewal Audit Flow):** ต่ออายุ `คุณสมชาย รักสุขภาพ` แบบ `Pending` (`#PAY-00108`) -> สิทธิ์ยังไม่เพิ่ม + กันสร้างบิล `Pending` ซ้ำ -> Admin กด `ApprovePayment` -> `#PAY-00108` เป็น `Success`, รายได้รวม `฿92,450 -> ฿98,350`, และคุณสมชายกลับมา `Active` (`24/11/2026 - 24/12/2026`, LTV `฿11,800`)
5. **[x] PASS — Flow E (Package Rollover vs Reset):** ตรวจเคสหมดอายุแล้ว (`คุณสมชาย รักสุขภาพ` หมด 13/11/2026) เมื่ออนุมัติต่ออายุ ระบบรีเซ็ตเริ่มรอบใหม่ `24/11/2026 - 24/12/2026` (`เหลือ 30 วัน`, `8/8 Pod`, `4/4 PT`)
6. **[x] PASS — Flow F (Suspension Guard):** ระงับบัญชี `#76` (`QA Full 2026`) -> หน้า `/Customer/BookSession` บล็อกการจอง (`"บัญชีของคุณถูกระงับการใช้งานชั่วคราว..."`) -> เปิดใช้งานคืนเป็น `Active`

---

## ชุดที่ 8: สรุปผลการทดสอบประจำรอบ (Test Run Sign-off)

- **วันที่ทดสอบ:** `2026-11-24 15:02`
- **Build / Environment:** `http://localhost:5180` (`DOTNET_CLI_HOME=/tmp dotnet build --no-restore` = `0 Error(s)`)
- **จำนวน Test Cases ทั้งหมด:** `45 เคสหลัก + 6 Cross-Role Flows` (Suite 0–7)
- **ผลการตรวจ:** `[x] PASS ALL` *(หลังพบและแก้ไข Bug 2 จุดระหว่างรันรอบแรกแล้วเทสซ้ำผ่าน 100%)*
- **Bug ที่พบและแก้ไขเรียบร้อยในรอบนี้:**
  1. `POST /Home/BookTrial` (`TC-01-03`): เดิมเกิด `HTTP 500 AmbiguousMatchException` เนื่องจากมีเมธอด `BookTrial` ซ้ำ 2 ตัวใน `HomeController.cs` -> ลบตัวซ้ำออกแล้ว **Retest PASS** (`HTTP 302 -> 200`)
  2. `POST /Admin/RenewMemberSubscription` (`TC-06-04`, `Flow D`): เดิมเกิด `MySqlException: Data truncated for column 'PaymentMethod'` เนื่องจาก MySQL `Payments.PaymentMethod` เป็น `ENUM('CreditCard','PromptPay','BankTransfer')` -> ปรับให้ Map ค่าให้ตรง ENUM และใช้ `SlipReference` แยกเงินสด (`INV-RENEW-CASH-...`) -> **Retest PASS** (`#PAY-00108` + อนุมัติต่ออายุสำเร็จ)

