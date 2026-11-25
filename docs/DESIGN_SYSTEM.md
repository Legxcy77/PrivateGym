# Design System — shadcn/ui Dark Standard

> มาตรฐานการออกแบบ UI กลางของระบบ **PrivateGym** สำหรับ 4 Management Portals (`Admin`, `Staff`, `Trainer`, `Accountant`) ที่รวมศูนย์ไว้ใน [`wwwroot/css/UnifiedWorkspace.css`](../wwwroot/css/UnifiedWorkspace.css)

---

## 1. Color Tokens

| Token | Hex / Value | Usage |
| :--- | :--- | :--- |
| **Background (Canvas)** | `#09090b` | พื้นหลัง Sidebar, Card, Table, Modal, Input |
| **Surface (Header/Subtle)** | `#0c0c0f` / `#111114` | หัวตาราง, กล่องสรุปย่อย |
| **Border** | `#27272a` / `#18181b` | เส้นขอบ Card, Table, Input, Divider |
| **Primary Accent** | `#e50914` | ปุ่มหลัก (`.st-btn-primary`), แถบ Active Sidebar, Focus Ring |
| **Text Primary** | `#fafafa` | หัวข้อ, ตัวเลข KPI, ข้อความหลัก |
| **Text Muted** | `#a1a1aa` / `#71717a` | คำอธิบาย, Label, หัวตารางตัวพิมพ์ใหญ่ |
| **Status Semantic** | `#10b981` (Green), `#f59e0b` (Amber), `#ef4444` (Red), `#60a5fa` (Blue) | Badges และสถานะการทำงาน |

---

## 2. Unified Workspace Shell

- **Sidebar (`248px`):**
  - แบ่งหมวดหมู่ด้วยตัวพิมพ์ใหญ่ (`.pg-sb-cat`)
  - เมนู Active ใช้พื้นหลัง `rgba(229, 9, 20, 0.14)` + เส้นขอบซ้าย `3px solid #e50914`
  - Footer (`.pg-sb-user`) แสดงอวาตาร์วงกลมสีแดง, ชื่อผู้ใช้, บทบาท, และปุ่ม Logout ด้านขวา
- **Topbar (`58px`):**
  - แสดง Breadcrumb / ชื่อหน้าปัจจุบัน และปุ่มลิงก์กลับหน้าเว็บหลัก

---

## 3. Shared Components

- **Tables (`.st-table-card`, `.st-shadcn-table`, `.adm-table`):** ขอบมน `12px`, หัวตาราง `11.5px` uppercase, แถว Hover `rgba(255,255,255,0.025)`
- **Forms (`.st-input`, `.form-control`, `.form-select`):** สูง `36px`, ขอบมน `8px`, Focus Ring สีแดง `rgba(229, 9, 20, 0.16)`
- **Buttons (`.st-btn-primary`, `.st-btn-outline`):** สูง `36px` (หรือ `30px` ในตาราง), ขอบมน `8px`
- **Global Confirmation Modal (`#pgConfirmModal`):** ดักจับฟอร์มที่มี `data-confirm` เพื่อยืนยันก่อนส่งข้อมูลจริงทุกครั้ง

