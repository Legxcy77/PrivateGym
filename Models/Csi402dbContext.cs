using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace PrivateGym.Models;

public partial class Csi402dbContext : DbContext
{
    public Csi402dbContext()
    {
    }

    public Csi402dbContext(DbContextOptions<Csi402dbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<Booking> Bookings { get; set; }

    public virtual DbSet<Branch> Branches { get; set; }

    public virtual DbSet<CheckIn> CheckIns { get; set; }


    public virtual DbSet<MemberSubscription> MemberSubscriptions { get; set; }

    public virtual DbSet<Package> Packages { get; set; }

    public virtual DbSet<Payment> Payments { get; set; }

    public virtual DbSet<Pod> Pods { get; set; }

    public virtual DbSet<Role> Roles { get; set; }

    public virtual DbSet<Trainer> Trainers { get; set; }

    public virtual DbSet<User> Users { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {

        modelBuilder.Entity<Booking>(entity =>
        {
            entity.HasKey(e => e.BookingId);

            entity.HasIndex(e => e.PodId, "PodId");

            entity.HasIndex(e => e.TrainerId, "TrainerId");

            entity.HasIndex(e => e.UserId, "UserId");

            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp");
            entity.Property(e => e.EndTime).HasColumnType("time");
            entity.Property(e => e.StartTime).HasColumnType("time");
            entity.Property(e => e.Status)
                .HasDefaultValueSql("'Confirmed'")
                .HasMaxLength(50);

            entity.HasOne(d => d.Pod).WithMany(p => p.Bookings)
                .HasForeignKey(d => d.PodId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("bookings_ibfk_2");

            entity.HasOne(d => d.Trainer).WithMany(p => p.Bookings)
                .HasForeignKey(d => d.TrainerId)
                .HasConstraintName("bookings_ibfk_3");

            entity.HasOne(d => d.User).WithMany(p => p.Bookings)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("bookings_ibfk_1");
        });

        modelBuilder.Entity<Branch>(entity =>
        {
            entity.HasKey(e => e.BranchId);

            entity.Property(e => e.BranchName).HasMaxLength(100);
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp");
            entity.Property(e => e.Location).HasMaxLength(255);
            entity.Property(e => e.Phone).HasMaxLength(20);
        });

        modelBuilder.Entity<CheckIn>(entity =>
        {
            entity.HasKey(e => e.CheckInId);

            entity.HasIndex(e => e.BookingId, "BookingId");

            entity.HasIndex(e => e.UserId, "UserId");

            entity.Property(e => e.AccessMethod)
                .HasDefaultValueSql("'QR_Code'")
                .HasMaxLength(50);
            entity.Property(e => e.CheckInTime)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp");
            entity.Property(e => e.CheckOutTime).HasColumnType("timestamp");

            entity.HasOne(d => d.Booking).WithMany(p => p.CheckIns)
                .HasForeignKey(d => d.BookingId)
                .HasConstraintName("checkins_ibfk_2");

            entity.HasOne(d => d.User).WithMany(p => p.CheckIns)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("checkins_ibfk_1");
        });


        modelBuilder.Entity<MemberSubscription>(entity =>
        {
            entity.HasKey(e => e.SubscriptionId);

            entity.HasIndex(e => e.PackageId, "PackageId");

            entity.HasIndex(e => e.UserId, "UserId");

            entity.Property(e => e.RemainingPt).HasColumnName("RemainingPT");
            entity.Property(e => e.Status)
                .HasDefaultValueSql("'Active'")
                .HasMaxLength(50);

            entity.HasOne(d => d.Package).WithMany(p => p.MemberSubscriptions)
                .HasForeignKey(d => d.PackageId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("membersubscriptions_ibfk_2");

            entity.HasOne(d => d.User).WithMany(p => p.MemberSubscriptions)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("membersubscriptions_ibfk_1");
        });

        modelBuilder.Entity<Package>(entity =>
        {
            entity.HasKey(e => e.PackageId);

            entity.Property(e => e.Description).HasColumnType("text");
            entity.Property(e => e.IsVip).HasDefaultValueSql("'0'");
            entity.Property(e => e.PackageName).HasMaxLength(100);
            entity.Property(e => e.Price).HasPrecision(10, 2);
        });

        modelBuilder.Entity<Payment>(entity =>
        {
            entity.HasKey(e => e.PaymentId);

            entity.HasIndex(e => e.PackageId, "PackageId");

            entity.HasIndex(e => e.UserId, "UserId");

            entity.Property(e => e.Amount).HasPrecision(10, 2);
            entity.Property(e => e.PaymentDate)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp");
            entity.Property(e => e.PaymentMethod).HasMaxLength(50);
            entity.Property(e => e.SlipReference).HasMaxLength(100);
            entity.Property(e => e.Status)
                .HasDefaultValueSql("'Success'")
                .HasMaxLength(50);

            entity.HasOne(d => d.Package).WithMany(p => p.Payments)
                .HasForeignKey(d => d.PackageId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("payments_ibfk_2");

            entity.HasOne(d => d.User).WithMany(p => p.Payments)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("payments_ibfk_1");
        });

        modelBuilder.Entity<Pod>(entity =>
        {
            entity.HasKey(e => e.PodId);

            entity.HasIndex(e => e.BranchId, "BranchId");

            entity.Property(e => e.PodName).HasMaxLength(50);
            entity.Property(e => e.PodType).HasMaxLength(50);
            entity.Property(e => e.Status)
                .HasDefaultValueSql("'Available'")
                .HasMaxLength(50);

            entity.HasOne(d => d.Branch).WithMany(p => p.Pods)
                .HasForeignKey(d => d.BranchId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("pods_ibfk_1");
        });

        modelBuilder.Entity<Role>(entity =>
        {
            entity.HasKey(e => e.RoleId);

            entity.HasIndex(e => e.RoleName, "RoleName").IsUnique();

            entity.Property(e => e.Description).HasMaxLength(255);
            entity.Property(e => e.RoleName).HasMaxLength(50);
        });

        modelBuilder.Entity<Trainer>(entity =>
        {
            entity.HasKey(e => e.TrainerId);

            entity.HasIndex(e => e.UserId, "UserId").IsUnique();

            entity.Property(e => e.Bio).HasColumnType("text");
            entity.Property(e => e.HourlyRate)
                .HasPrecision(10, 2)
                .HasDefaultValueSql("'0.00'");
            entity.Property(e => e.Specialty).HasMaxLength(100);

            entity.HasOne(d => d.User).WithOne(p => p.Trainer)
                .HasForeignKey<Trainer>(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("trainers_ibfk_1");
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.UserId);

            entity.HasIndex(e => e.Email, "Email").IsUnique();

            entity.HasIndex(e => e.HomeBranchId, "HomeBranchId");

            entity.HasIndex(e => e.RoleId, "RoleId");

            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp");
            entity.Property(e => e.Email).HasMaxLength(100);
            entity.Property(e => e.FullName).HasMaxLength(100);
            entity.Property(e => e.PasswordHash).HasMaxLength(255);
            entity.Property(e => e.Phone).HasMaxLength(20);
            entity.Property(e => e.Status)
                .HasMaxLength(50)
                .HasDefaultValueSql("'Active'");

            entity.HasOne(d => d.HomeBranch).WithMany(p => p.Users)
                .HasForeignKey(d => d.HomeBranchId)
                .HasConstraintName("users_ibfk_2");

            entity.HasOne(d => d.Role).WithMany(p => p.Users)
                .HasForeignKey(d => d.RoleId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("users_ibfk_1");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);

    public static void EnsureAllBranchesPodsAndTrainersSeeded(Csi402dbContext db)
    {
        var branches = db.Branches.OrderBy(b => b.BranchId).ToList();
        if (!branches.Any()) return;

        int firstBranchId = branches.First().BranchId;
        int thonglorId = branches.FirstOrDefault(b => b.BranchName.Contains("ทองหล่อ"))?.BranchId ?? firstBranchId;
        int sukhumvitId = branches.FirstOrDefault(b => b.BranchName.Contains("สุขุมวิท"))?.BranchId ?? firstBranchId;
        int ariId = branches.FirstOrDefault(b => b.BranchName.Contains("อารีย์"))?.BranchId ?? firstBranchId;
        int? sathornId = branches.FirstOrDefault(b => b.BranchName.Contains("สาทร"))?.BranchId;

        var podSpecsList = new List<(int BranchId, string Name, string Type)>
        {
            (thonglorId, "Pod 01: Solo Precision", "Solo Precision"),
            (thonglorId, "Pod 02: Power Rig & Barbell", "Strength & Power"),
            (thonglorId, "Pod 03: Athletic & Cardio", "Cardio & Turf"),
            (thonglorId, "Pod 04: Recovery Suite", "Recovery & Sauna"),
            (thonglorId, "Pod 05: Boxing & Functional", "Combat & HIIT"),
            (thonglorId, "Pod 06: Pilates & Mobility", "Pilates & Flow"),

            (sukhumvitId, "SKV Pod 01: Solo Precision", "Solo Precision"),
            (sukhumvitId, "SKV Pod 02: Strength & Barbell", "Strength & Power"),
            (sukhumvitId, "SKV Pod 03: Athletic & Recovery", "Recovery & Cardio"),

            (ariId, "ARI Pod 01: Solo Precision", "Solo Precision"),
            (ariId, "ARI Pod 02: Power & Functional", "Strength & Power"),
            (ariId, "ARI Pod 03: Pilates & Mobility", "Pilates & Flow")
        };

        if (sathornId.HasValue)
        {
            podSpecsList.Add((sathornId.Value, "STN Pod 01: Solo Precision", "Solo Precision"));
            podSpecsList.Add((sathornId.Value, "STN Pod 02: Power Rig & Barbell", "Strength & Power"));
            podSpecsList.Add((sathornId.Value, "STN Pod 03: Recovery & Sauna", "Recovery & Sauna"));
        }

        bool podsChanged = false;
        var existingPods = db.Pods.OrderBy(p => p.PodId).ToList();

        foreach (var spec in podSpecsList)
        {
            var matched = existingPods.FirstOrDefault(p => p.PodName == spec.Name);
            if (matched == null)
            {
                db.Pods.Add(new Pod
                {
                    BranchId = spec.BranchId,
                    PodName = spec.Name,
                    PodType = spec.Type,
                    Status = "Available"
                });
                podsChanged = true;
            }
            else if (matched.BranchId != spec.BranchId)
            {
                matched.BranchId = spec.BranchId;
                podsChanged = true;
            }
        }
        if (podsChanged) db.SaveChanges();

        var trainerRole = db.Roles.FirstOrDefault(r => r.RoleName == "Trainer");
        int trainerRoleId = trainerRole?.RoleId ?? 3;

        var coachSpecs = new List<(int BranchId, string FullName, string Email, string Phone, string Specialty, string Bio, decimal Rate)>
        {
            (thonglorId, "Coach Alex (วรเมธ ศิริกุล)", "alex@privategym.club", "081-901-1001", "Strength & Hypertrophy", "Lead Strength & Conditioning Specialist (CSCS) เชี่ยวชาญการฝึก Barbell Compound Lifts และสร้างกล้ามเนื้อ", 1500m),
            (thonglorId, "Coach Sarah (ลลิตา พิริยะชัย)", "sarah@privategym.club", "081-901-1002", "Functional & Mobility", "Senior Functional Movement Coach (EXOS-XPS) เชี่ยวชาญการเคลื่อนไหวร่างกายและแกนกลางลำตัว", 1400m),
            (thonglorId, "Coach Ken (ธนภัทร ฤทธิเดช)", "ken@privategym.club", "081-901-1003", "Olympic Weightlifting & Power", "Olympic Weightlifting & Explosive Power Coach (USAW-L2) เน้นพัฒนาพละกำลังและความเร็ว", 1500m),
            (thonglorId, "Coach Mew (พิมพ์มาดา วิจิตรสุข)", "mew@privategym.club", "081-901-1004", "Posture & Pilates Rehab", "Posture Correction & Pilates Specialist (NASM-CES) ปรับสรีระ แก้ออฟฟิศซินโดรม และฟื้นฟูการบาดเจ็บ", 1400m),
            (thonglorId, "Coach Ton (ต้นกล้า พิทักษ์พงษ์)", "ton@privategym.club", "081-901-1005", "Boxing & HIIT Conditioning", "Combat Conditioning & Metabolic Specialist เน้นฝึกความทนทาน ระบบหัวใจ และการเผาผลาญไขมัน", 1400m),
            (thonglorId, "Coach Beam (ภานุพงศ์ วงศ์สุวรรณ)", "beam@privategym.club", "081-901-1006", "Athletic & Speed Training", "Athletic Performance Coach (NSCA) ออกแบบโปรแกรมเพิ่มพลังระเบิดและความคล่องตัวสำหรับนักกีฬา", 1400m),

            (sukhumvitId, "Coach Frame (ธนกฤต ศรีสวัสดิ์)", "frame@privategym.club", "081-902-2001", "Strength & Hypertrophy", "Certified Strength Coach เชี่ยวชาญการจัดโปรแกรมสร้างกล้ามเนื้อและปรับเทคนิคการยกน้ำหนักอย่างปลอดภัย", 1500m),
            (sukhumvitId, "Coach Namo (ณโม จิรวัฒน์)", "namo@privategym.club", "081-902-2002", "Functional & Mobility", "Functional Movement Specialist เน้นฝึกความแข็งแรงแกนกลางลำตัวและความยืดหยุ่นของข้อต่อ", 1400m),
            (sukhumvitId, "Coach Ice (ไอซ์ รัตนากร)", "ice@privategym.club", "081-902-2003", "Posture & Pilates Rehab", "Rehabilitation & Pilates Coach ดูแลการปรับสมดุลสรีระ ลดอาการปวดหลังและออฟฟิศซินโดรม", 1400m),

            (ariId, "Coach Bank (ณัฐพล ธีระพงษ์)", "bank@privategym.club", "081-903-3001", "Powerlifting & Strength", "Competitive Powerlifting Coach เน้นพัฒนาพละกำลังพื้นฐาน Squat, Bench Press และ Deadlift", 1500m),
            (ariId, "Coach May (เมธาวี สุขสำราญ)", "may@privategym.club", "081-903-3002", "Posture & Pilates Rehab", "Pilates & Core Stability Specialist ออกแบบโปรแกรมฟื้นฟูกล้ามเนื้อและความกระชับเฉพาะบุคคล", 1400m),
            (ariId, "Coach Petch (พชร กิตติคุณ)", "petch@privategym.club", "081-903-3003", "Boxing & Functional", "Combat & Functional HIIT Specialist เน้นฝึกความคล่องตัวและลดไขมันแบบเข้มข้น", 1400m)
        };

        if (sathornId.HasValue)
        {
            coachSpecs.Add((sathornId.Value, "Coach Pun (ปันปัน ภูริชญา)", "pun@privategym.club", "081-904-4001", "Functional & Mobility", "Senior Mobility & Core Conditioning Coach ประจำสาขาสาทร", 1400m));
            coachSpecs.Add((sathornId.Value, "Coach Ohm (โอม ชัยวัฒน์)", "ohm@privategym.club", "081-904-4002", "Strength & Hypertrophy", "Strength & Physique Specialist ออกแบบโปรแกรมสร้างกล้ามเนื้อเฉพาะบุคคล", 1500m));
            coachSpecs.Add((sathornId.Value, "Coach Nina (นีน่า กัญญารัตน์)", "nina@privategym.club", "081-904-4003", "Posture & Pilates Rehab", "Reformer Pilates & Recovery Specialist ปรับสมดุลร่างกายและฟื้นฟูอาการบาดเจ็บ", 1400m));
        }

        foreach (var c in coachSpecs)
        {
            var coachUser = db.Users.FirstOrDefault(u => u.Email == c.Email);
            if (coachUser == null)
            {
                coachUser = new User
                {
                    FullName = c.FullName,
                    Email = c.Email,
                    Phone = c.Phone,
                    PasswordHash = "123456",
                    RoleId = trainerRoleId,
                    HomeBranchId = c.BranchId,
                    Status = "Active",
                    CreatedAt = DateTime.Now
                };
                db.Users.Add(coachUser);
                db.SaveChanges();
            }
            else if (coachUser.HomeBranchId != c.BranchId)
            {
                coachUser.HomeBranchId = c.BranchId;
                db.SaveChanges();
            }

            if (!db.Trainers.Any(t => t.UserId == coachUser.UserId))
            {
                db.Trainers.Add(new Trainer
                {
                    UserId = coachUser.UserId,
                    Specialty = c.Specialty,
                    Bio = c.Bio,
                    HourlyRate = c.Rate
                });
                db.SaveChanges();
            }
        }
    }
}
