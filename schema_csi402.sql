-- =======================================================
-- CSI402 Database Script: CSI402DB
-- Project: PrivateGym (ระบบบริหารจัดการฟิตเนสส่วนตัว)
-- =======================================================

-- 1. สร้าง Database
CREATE DATABASE IF NOT EXISTS CSI402DB
DEFAULT CHARACTER SET utf8mb4
COLLATE utf8mb4_unicode_ci;

USE CSI402DB;

-- =======================================================
-- 2. สร้าง Table LabStudent (ตามสไลด์ Lab 7 หน้า 21)
-- =======================================================
DROP TABLE IF EXISTS LabStudent;
CREATE TABLE LabStudent
(
    StdId NVARCHAR(10),
    StdPassword NVARCHAR(30),
    StdName NVARCHAR(50),
    StdLastname NVARCHAR(100),
    PRIMARY KEY (StdId)
);

-- =======================================================
-- 3 & 4. สร้าง Tables สำหรับโปรเจกต์ PrivateGym
-- =======================================================

-- ตารางที่ 1: สาขาฟิตเนส (Branches)
DROP TABLE IF EXISTS CheckIns;
DROP TABLE IF EXISTS Bookings;
DROP TABLE IF EXISTS Payments;
DROP TABLE IF EXISTS MemberSubscriptions;
DROP TABLE IF EXISTS Trainers;
DROP TABLE IF EXISTS Pods;
DROP TABLE IF EXISTS Users;
DROP TABLE IF EXISTS Packages;
DROP TABLE IF EXISTS Roles;
DROP TABLE IF EXISTS Branches;

CREATE TABLE Branches (
    BranchId INT AUTO_INCREMENT PRIMARY KEY,
    BranchName VARCHAR(100) NOT NULL,
    Location VARCHAR(255) NOT NULL,
    Phone VARCHAR(20),
    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP
);

-- ตารางที่ 2: สิทธิ์และบทบาทผู้ใช้ (Roles)
CREATE TABLE Roles (
    RoleId INT AUTO_INCREMENT PRIMARY KEY,
    RoleName VARCHAR(50) NOT NULL UNIQUE,
    Description VARCHAR(255)
);

-- ตารางที่ 3: ผู้ใช้งานระบบ (Users)
CREATE TABLE Users (
    UserId INT AUTO_INCREMENT PRIMARY KEY,
    FullName VARCHAR(100) NOT NULL,
    Email VARCHAR(100) NOT NULL UNIQUE,
    Phone VARCHAR(20) NOT NULL,
    PasswordHash VARCHAR(255) NOT NULL,
    Gender VARCHAR(10) NULL,
    Height DECIMAL(5, 2) NULL,
    Weight DECIMAL(5, 2) NULL,
    ExperienceLevel VARCHAR(50) NULL,
    FitnessGoal VARCHAR(100) NULL,
    TaxId VARCHAR(20) NULL,
    BillingAddress TEXT NULL,
    RoleId INT NOT NULL,
    HomeBranchId INT,
    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (RoleId) REFERENCES Roles(RoleId),
    FOREIGN KEY (HomeBranchId) REFERENCES Branches(BranchId)
);

-- ตารางที่ 4: แพ็กเกจสมาชิก (Packages)
CREATE TABLE Packages (
    PackageId INT AUTO_INCREMENT PRIMARY KEY,
    PackageName VARCHAR(100) NOT NULL,
    Price DECIMAL(10, 2) NOT NULL,
    DurationDays INT NOT NULL,
    PodQuota INT NOT NULL,
    PtQuota INT NOT NULL,
    InBodyQuota INT NOT NULL,
    IsVip BOOLEAN DEFAULT FALSE,
    Description TEXT
);

-- ตารางที่ 5: สมาชิกที่สมัครแพ็กเกจ (MemberSubscriptions)
CREATE TABLE MemberSubscriptions (
    SubscriptionId INT AUTO_INCREMENT PRIMARY KEY,
    UserId INT NOT NULL,
    PackageId INT NOT NULL,
    StartDate DATE NOT NULL,
    EndDate DATE NOT NULL,
    RemainingPods INT NOT NULL,
    RemainingPT INT NOT NULL,
    RemainingInBody INT NOT NULL,
    Status ENUM('Active', 'Expired', 'Cancelled') DEFAULT 'Active',
    FOREIGN KEY (UserId) REFERENCES Users(UserId),
    FOREIGN KEY (PackageId) REFERENCES Packages(PackageId)
);

-- ตารางที่ 6: ห้องออกกำลังกายส่วนตัว (Pods)
CREATE TABLE Pods (
    PodId INT AUTO_INCREMENT PRIMARY KEY,
    BranchId INT NOT NULL,
    PodName VARCHAR(50) NOT NULL,
    PodType VARCHAR(50) NOT NULL, -- เช่น Cardio Pod, Powerlifting Pod, Functional Pod
    Status ENUM('Available', 'Occupied', 'Maintenance') DEFAULT 'Available',
    FOREIGN KEY (BranchId) REFERENCES Branches(BranchId)
);

-- ตารางที่ 7: ข้อมูลเทรนเนอร์ (Trainers)
CREATE TABLE Trainers (
    TrainerId INT AUTO_INCREMENT PRIMARY KEY,
    UserId INT NOT NULL UNIQUE,
    Specialty VARCHAR(100),
    Bio TEXT,
    HourlyRate DECIMAL(10, 2) DEFAULT 0.00,
    FOREIGN KEY (UserId) REFERENCES Users(UserId)
);

-- ตารางที่ 8: การจองห้องและเทรนเนอร์ (Bookings)
CREATE TABLE Bookings (
    BookingId INT AUTO_INCREMENT PRIMARY KEY,
    UserId INT NOT NULL,
    PodId INT NOT NULL,
    TrainerId INT NULL,
    BookingDate DATE NOT NULL,
    StartTime TIME NOT NULL,
    EndTime TIME NOT NULL,
    Status ENUM('Confirmed', 'Cancelled', 'Completed') DEFAULT 'Confirmed',
    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (UserId) REFERENCES Users(UserId),
    FOREIGN KEY (PodId) REFERENCES Pods(PodId),
    FOREIGN KEY (TrainerId) REFERENCES Trainers(TrainerId)
);

-- ตารางที่ 9: การชำระเงิน (Payments)
CREATE TABLE Payments (
    PaymentId INT AUTO_INCREMENT PRIMARY KEY,
    UserId INT NOT NULL,
    PackageId INT NOT NULL,
    Amount DECIMAL(10, 2) NOT NULL,
    PaymentMethod ENUM('CreditCard', 'PromptPay', 'BankTransfer') NOT NULL,
    PaymentDate DATETIME DEFAULT CURRENT_TIMESTAMP,
    Status ENUM('Pending', 'Success', 'Failed') DEFAULT 'Success',
    SlipReference VARCHAR(100),
    FOREIGN KEY (UserId) REFERENCES Users(UserId),
    FOREIGN KEY (PackageId) REFERENCES Packages(PackageId)
);

-- ตารางที่ 10: ประวัติการเช็คอินเข้าใช้บริการ (CheckIns)
CREATE TABLE CheckIns (
    CheckInId INT AUTO_INCREMENT PRIMARY KEY,
    UserId INT NOT NULL,
    BookingId INT,
    CheckInTime DATETIME DEFAULT CURRENT_TIMESTAMP,
    CheckOutTime DATETIME NULL,
    AccessMethod ENUM('QR_Code', 'Turnstile_Card', 'Manual') DEFAULT 'QR_Code',
    FOREIGN KEY (UserId) REFERENCES Users(UserId),
    FOREIGN KEY (BookingId) REFERENCES Bookings(BookingId)
);

-- =======================================================
-- ข้อมูลจำลองเบื้องต้น (Initial Seed Data)
-- =======================================================
INSERT INTO Roles (RoleName, Description) VALUES
('Admin', 'ผู้ดูแลระบบสูงสุด'),
('Staff', 'พนักงานประจำสาขา'),
('Trainer', 'เทรนเนอร์ส่วนตัว'),
('Member', 'สมาชิกฟิตเนส');

INSERT INTO Branches (BranchName, Location, Phone) VALUES
('สาขาสุขุมวิท 39', 'ซอยสุขุมวิท 39 แขวงคลองตันเหนือ กรุงเทพฯ', '02-123-4567'),
('สาขาทองหล่อ', 'ซอยทองหล่อ 13 แขวงคลองตันเหนือ กรุงเทพฯ', '02-987-6543'),
('สาขาอารีย์', 'ซอยอารีย์ 4 แขวงสามเสนใน กรุงเทพฯ', '02-555-8888');

INSERT INTO Packages (PackageName, Price, DurationDays, PodQuota, PtQuota, InBodyQuota, IsVip, Description) VALUES
('Starter Experience', 2900.00, 30, 4, 0, 1, FALSE, 'แพ็กเกจเริ่มต้นใช้งาน Pod 4 ครั้ง'),
('Pro Athlete', 5900.00, 30, 8, 4, 2, FALSE, 'แพ็กเกจยอดนิยม Pod 8 ครั้ง พร้อม PT 4 ครั้ง'),
('Elite VIP Exclusive', 12900.00, 30, 999, 999, 999, TRUE, 'แพ็กเกจระดับพรีเมียม ใช้งาน Pod และ PT ไม่จำกัด');
