using System;
using System.Collections.Generic;
using PrivateGym.Models;

namespace PrivateGym.ViewModels;

// ViewModel สำหรับหน้าฟอร์มจองบริการ (Book Session)
public class BookSessionViewModel
{
    public int UserId { get; set; }
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public int RemainingPods { get; set; }
    public int RemainingPt { get; set; }
    public bool IsVip { get; set; }

    // รายการสาขา, ห้อง Pod และเทรนเนอร์ให้ผู้ใช้เลือก
    public List<Branch> Branches { get; set; } = new();
    public List<Pod> Pods { get; set; } = new();
    public List<Trainer> Trainers { get; set; } = new();

    // ค่าที่ผู้ใช้เลือกส่งมาในฟอร์ม
    public string ServiceType { get; set; } = "pod_single"; // pod_single, pod_trainer, recovery, duo
    public int SelectedBranchId { get; set; }
    public int SelectedPodId { get; set; }
    public int? SelectedTrainerId { get; set; }
    public DateOnly BookingDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public string SelectedTimeSlot { get; set; } = "18:00 - 19:15";
}

